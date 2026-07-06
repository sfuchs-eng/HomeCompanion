using System.Threading.Channels;
using HomeCompanion.Base.Model;
using HomeCompanion.Base.Utilities;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters;

/// <summary>
/// <para>Controls the operation of shutters, including processing shutter targets and executing the necessary actions to achieve the desired shutter states.</para>
/// <para>It contains the full automation stack for shutter operation, including the logic to determine the target shutter positions based on various inputs such as time of day, weather conditions, and user preferences, as well as the logic to execute the necessary actions to bring the shutters into the desired state, e.g. by sending commands to the actuators, while also implementing safety interlocks and movement rate limits as needed.</para>
/// <para>It's using <see cref="HomeCompanion.Base.Model"/> as configuration base and to connect to the <see cref="IValue"/> related framework.</para>
/// <para>It implements multiple proccessing stages:</para>
/// <list type="number">
/// <item><see cref="shutterAutomationTriggerCollector"/> aggregates <see cref="ShutterAutomationComputationTriggerContext"/> in method <see cref="ShutterController.CollectShutterAutomationTriggersAsync(Channel{HomeCompanion.Logics.Shutters.ShutterAutomationComputationTriggerContext}, CancellationToken)"/></item>
/// <item><see cref="shutterAutomationStateComputationLoop"/> computes shutter target states based on aggregated <see cref="ShutterAutomationComputationTriggerContext"/> in method <see cref="ShutterController.ComputeShutterTargetStateAsync(HomeCompanion.Logics.Shutters.ShutterController.ShutterRuntimeContext, HomeCompanion.Logics.Shutters.ShutterAutomationComputationTriggerContext, CancellationToken)"/></item>
/// <item><see cref="shutterTargetProcessingLoop"/> controls shutters based on <see cref="ShutterTarget"/> to the desired position in method <see cref="ShutterController.ProcessShutterTargetsAsync(Channel{HomeCompanion.Logics.Shutters.ShutterTarget}, CancellationToken)"/></item>
/// </list>
/// </summary>
/// <typeparam name="ShutterController"></typeparam>
public partial class ShutterController : LogicBase
{
    private readonly IValueProvider valuesProvider;
    private readonly IEventPublisher eventPublisher;
    private readonly IEventSubscriber eventSubscriber;
    private readonly TimeProvider timeProvider;
    private readonly IModelProvider modelProvider;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<ShutterController> logger;
    private readonly Dictionary<ShutterKey, ShutterRuntime> shutterRuntimes = [];
    private readonly Dictionary<BuildingKey, BuildingRuntime> buildingRuntimes = [];
    private readonly Dictionary<RoomKey, RoomRuntime> roomRuntimes = [];

    /// <summary>
    /// <see cref="ShadowingRuntimesController"/> sends triggers to the event bus from which we received them and enqueue them into the <b>shutter automation trigger collector</b>.
    /// The collector assesses each trigger for its urgency and the time it has been waiting since it was triggered, and decides whether to immediately enqueue it for processing in the <b>state computation loop</b> or to defer it for a short time to allow for more triggers to arrive and be processed together, e.g. to avoid excessive shutter movements in case of rapidly changing input conditions; the collector also batches triggers that are due for processing together into one trigger with a collection of all individual triggers, which is then enqueued into the <b>state computation loop</b>.
    /// </summary>
    private readonly BackgroundRunner<ShutterAutomationComputationTriggerContext> shutterAutomationTriggerCollector;

    /// <summary>
    /// The <b>state computation loop</b> determines the desired target state for each shutter, e.g. based on time of day, weather conditions, user preferences, etc., and enqueues the resulting shutter targets into the <b>shutter target processing loop</b>.
    /// </summary>
    private readonly BackgroundRunner<ShutterAutomationComputationTriggerContext> shutterAutomationStateComputationLoop;

    /// <summary>
    /// The <b>shutter target processing loop</b> receives the desired target states for each shutter from the <b>state computation loop</b> and executes the necessary actions to bring the shutters into the desired state, e.g. by sending commands to the actuators, while also implementing safety interlocks and movement rate limits as needed.
    /// </summary>
    private readonly BackgroundRunner<ShutterTarget> shutterTargetProcessingLoop;

    public ShutterController(
        IValueProvider valuesProvider,
        IEventPublisher eventPublisher,
        IEventSubscriber eventSubscriber,
        TimeProvider timeProvider,
        IModelProvider modelProvider,
        ILoggerFactory loggerFactory,
        ILogger<ShutterController> logger
        ) : base(eventPublisher, eventSubscriber)
    {
        this.valuesProvider = valuesProvider;
        this.eventPublisher = eventPublisher;
        this.eventSubscriber = eventSubscriber;
        this.timeProvider = timeProvider;
        this.modelProvider = modelProvider;
        this.loggerFactory = loggerFactory;
        this.logger = logger;

        shutterAutomationTriggerCollector = new BackgroundRunner<ShutterAutomationComputationTriggerContext>(CollectShutterAutomationTriggersAsync);
        shutterAutomationStateComputationLoop = new BackgroundRunner<ShutterAutomationComputationTriggerContext>(ProcessShutterAutomationComputationAsync);
        shutterTargetProcessingLoop = new BackgroundRunner<ShutterTarget>(ProcessShutterTargetsAsync);
    }

    protected override async Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
    {
        // starting the background runners is done at the end of initialization to ensure that the full setup is in place before any triggers are processed, as the runners will start processing triggers as soon as they are started, and we want to avoid processing triggers before the setup is complete to prevent errors and ensure that all triggers are processed correctly.
        shutterAutomationTriggerCollector.Start(cancellationToken);

        // run the shutter automation computation loop on a linked cancellation token that is cancelled when the logic is stopped;
        shutterAutomationStateComputationLoop.Start(cancellationToken);

        // run the shutter target processing loop on a linked cancellation token that is cancelled when the logic is stopped;
        shutterTargetProcessingLoop.Start(cancellationToken);

        // subscribe to the event bus to receive triggers for shutter automation computation, e.g. when a value changes that affects the desired shutter state, or when a manual override is performed on a shutter, so that the automation logic can adjust its behavior accordingly.
        eventSubscriber.Subscribe<ShutterAutomationComputationTriggerEvent>(new ComputationTriggerEventHandler(shutterAutomationTriggerCollector, loggerFactory.CreateLogger<ComputationTriggerEventHandler>()));

        // register with shutter runtimes to handle external overrides, e.g. when a shutter is manually operated via a wall switch or remote control, to ensure that the automation logic is aware of the manual operation and can adjust its behavior accordingly, e.g. by temporarily pausing automation for the affected shutter
        foreach (var shutterRuntime in shutterRuntimes.Values)
        {
            shutterRuntime.ShutterExternalOverride += HandleShutterExternalOverride;
        }
    }

    private class ComputationTriggerEventHandler : IEventHandler<ShutterAutomationComputationTriggerEvent>
    {
        private readonly BackgroundRunner<ShutterAutomationComputationTriggerContext> shutterAutomationTriggerCollector;
        private readonly ILogger<ComputationTriggerEventHandler> logger;

        public ComputationTriggerEventHandler(BackgroundRunner<ShutterAutomationComputationTriggerContext> shutterAutomationTriggerCollector, ILogger<ComputationTriggerEventHandler> logger)
        {
            this.shutterAutomationTriggerCollector = shutterAutomationTriggerCollector;
            this.logger = logger;
        }

        public async ValueTask HandleAsync(ShutterAutomationComputationTriggerEvent @event, CancellationToken cancellationToken = default)
        {
            // enqueue the trigger for processing in the shutter automation trigger collector, which will assess its urgency and decide whether to immediately process it or to defer it for a short time to allow for more triggers to arrive and be processed together
            await shutterAutomationTriggerCollector.EnqueueAsync(@event.Context, cancellationToken);
        }
    }

    private void HandleShutterExternalOverride(object? sender, ShutterExternalOverrideEventArgs e)
    {
        logger.LogWarning("Not implemented yet: Shutter {ShutterKey} was externally overridden to position {OverridePosition}. The automation logic should adjust its behavior accordingly, e.g. by temporarily pausing automation for the affected shutter.", e.ShutterKey, e.ValueWrittenEventArgs.NewValue.Format());
    }

    private async Task CollectShutterAutomationTriggersAsync(Channel<ShutterAutomationComputationTriggerContext> channel, CancellationToken token)
    {
        /// take triggers from the channel, in blocks of triggers that are processed together depending on their urgency and the remaining time until they need to be processed.
        while (!token.IsCancellationRequested)
        {
            try
            {
                var triggerCollectionStart = timeProvider.GetUtcNow();
                var queuedTriggers = new List<ShutterAutomationComputationTriggerContext>();

                // **batching loop**
                // iterate: collect all triggers in queue, determine max timeout duration remaining, wait for more triggers to arrive, max until timeout.
                // on each iteration: check new urgency and adjust total waiting time accordingly, e.g. if a new trigger with higher urgency arrives, we can shorten the waiting time to process the triggers sooner.
                while (!token.IsCancellationRequested)
                {
                    // we shall not wait in total longer than the fastest trigger we caught in the queue.
                    var maxRemainingTime = queuedTriggers.GetRemainingTimeUntilProcessing(timeProvider.GetUtcNow());

                    if (maxRemainingTime <= TimeSpan.Zero)
                        break;

                    // read next one, then reassess the remaining time until processing for all triggers in the queue, including the new one, to decide whether to continue waiting for more triggers or to proceed with processing the collected triggers
                    try
                    {
                        var readTimeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, new CancellationTokenSource(maxRemainingTime).Token);
                        await channel.Reader.ReadAsync(readTimeoutTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // expected when the read timeout is reached, just break to proceed with processing the collected triggers; if the main token is cancelled, this will also throw an OperationCanceledException, but in that case we also want to break and proceed with processing any triggers collected so far, and then exit the loop on the next iteration when we check token.IsCancellationRequested
                        break;
                    }
                }

                // **trigger grouping**
                // if there's a global scoped trigger --> process all in one new trigger
                // otherwise: group by shutter and merge triggers for each shutter into one new trigger per shutter; saves evaluation of shutters not having received a trigger.
                var triggersToProcess = new List<ShutterAutomationComputationTriggerContext>();
                if (queuedTriggers.Any(tc => tc.Scope == ShutterAutomationComputationScope.Global))
                {
                    triggersToProcess.Add(new ShutterAutomationComputationTriggerContext(queuedTriggers));
                }
                else
                {
                    var triggersGroupedByShutter = queuedTriggers.SelectMany(tc => tc.ThingKeys.Select(sk => (TriggerContext: tc, ShutterKey: sk)))
                        .GroupBy(x => x.ShutterKey);
                    foreach (var shutterGroup in triggersGroupedByShutter)
                    {
                        var shutterTriggers = shutterGroup.Select(x => x.TriggerContext);
                        triggersToProcess.Add(new ShutterAutomationComputationTriggerContext(shutterTriggers));
                    }
                }

                // **processing**
                // sort by urgency, highest go first into the processing channel
                foreach (var trigger in triggersToProcess.OrderByDescending(tc => tc.Urgency))
                {
                    await shutterAutomationStateComputationLoop.EnqueueAsync(trigger, token);
                }
            }
            catch (OperationCanceledException)
            {
                // expected when the token is cancelled, just exit the loop
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error in shutter automation computation loop. Dropping all currently queued triggers and restarting the loop. Exception details: {ExceptionMessage}", ex.Message);
            }
        }
    }

    /// <summary>
    /// Execute the shutter target processing loop, which reads shutter targets from the channel and executes the necessary actions to bring the shutters into the desired state, e.g. by sending commands to the actuators, while also implementing safety interlocks and movement rate limits as needed.
    /// Take 500ms of collecting time and aggregate commands by shutter prior execution, last one wins.
    /// Actual rate throttling and latching should be done in <see cref="ShutterRuntime.ExecuteShutterTargetAsync"/>.
    /// </summary>
    /// <param name="channel"></param>
    /// <param name="token"></param>
    private async Task ProcessShutterTargetsAsync(Channel<ShutterTarget> channel, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var shutterTargets = new List<ShutterTarget>();
            try
            {
                shutterTargets.Add(await channel.Reader.ReadAsync(token));
                // wait a short time to allow for more targets to arrive, then take the latest target for each shutter, so that we don't execute multiple times for the same shutter if multiple targets arrive in a short time, e.g. due to rapidly changing input conditions or because the state computation loop is producing multiple intermediate targets while converging towards the final target state.
                var timeOutToken = CancellationTokenSource.CreateLinkedTokenSource(token, new CancellationTokenSource(TimeSpan.FromMilliseconds(500)).Token).Token;
                while (timeOutToken.IsCancellationRequested)
                {
                    try
                    {
                        shutterTargets.Add(await channel.Reader.ReadAsync(timeOutToken));
                    }
                    catch (OperationCanceledException)
                    {
                        // expected when the read timeout is reached, just break to proceed with processing the collected targets; if the main token is cancelled, this will also throw an OperationCanceledException, but in that case we also want to break and proceed with processing any targets collected so far, and then exit the loop on the next iteration when we check token.IsCancellationRequested
                        break;
                    }
                }

                var latestTargets = shutterTargets
                    .GroupBy(st => st.ShutterKey)
                    .Select(g => g.Last());

                var discardedTargets = shutterTargets.Except(latestTargets).ToList();
                if (discardedTargets.Any())
                {
                    logger.LogTrace("Discarding {DiscardedTargetCount} intermediate shutter targets that arrived within the aggregation time window. Latest target for each shutter will be executed. Discarded targets: {DiscardedTargets}", discardedTargets.Count, string.Join(", ", discardedTargets.Select(dt => $"{dt.ShutterKey}: {dt.TargetPosition}")));
                }

                foreach (var shutterTarget in latestTargets)
                {
                    if (shutterRuntimes.TryGetValue(shutterTarget.ShutterKey, out var runtime))
                    {
                        try
                        {
                            await runtime.ExecuteShutterTargetAsync(shutterTarget);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Error executing shutter target for shutter {ShutterKey}. Exception details: {ExceptionMessage}", shutterTarget.ShutterKey, ex.Message);
                        }
                    }
                    else
                    {
                        logger.LogWarning("Received shutter target for unknown shutter {ShutterKey}", shutterTarget.ShutterKey);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // expected when the token is cancelled, just exit the loop
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error processing shutter target for shutter {ShutterKey}. Exception details: {ExceptionMessage}", ex is ShutterTargetProcessingException stpe ? stpe.ShutterKey : "<unknown>", ex.Message);
            }
        }
    }
}

/// <summary>
/// Encapsulates the desired target state for a shutter, including the logic to determine the target position based on various inputs such as time of day, weather conditions, and user preferences.
/// Serves as input for shutter actuation, allowing for a last gating possibility to e.g. limit movement rates, or implement safety interlocks at shutter level just prior actuator hardware.
/// </summary>
internal class ShutterTarget(ShutterRuntimeContext shutterRuntimeContext, ShutterPosition targetPosition)
{
    public ShutterKey ShutterKey => ShutterRuntimeContext.ShutterKey;
    public ShutterRuntimeContext ShutterRuntimeContext { get; } = shutterRuntimeContext;
    public ShutterPosition TargetPosition { get; } = targetPosition;

    public void Set(double liftPosition, double tiltAngle)
    {
        TargetPosition.LiftPosition = liftPosition;
        TargetPosition.TiltAngle = tiltAngle;
    }

    public bool IsNoOp => TargetPosition.PreventPositionChange && TargetPosition.PreventTiltChange;
}

/// <summary>
/// Represents the position of a shutter, including its current state and any relevant metadata.
/// As venetian blinds are the most complex type of shutter implemented, the targets for other types are derived from the same model, e.g. roller shutters are either fully open or fully closed, but the logic can still use the same target position model and just limit the possible target positions accordingly.
/// </summary>
/// <param name="liftPosition">Lift position of the shutter, where 0.0 represents fully closed and 1.0 represents fully open. For roller shutters, this value is either 0.0 or 1.0, while for venetian blinds it can take any value in between to represent partial opening.</param>
/// <param name="tiltAngle">Tilt angle of the shutter slats in p.u., where 0 degrees represents fully open = horizontal slats, 1.0 represents slats fully closed = vertical</param>
public class ShutterPosition(double liftPosition, double tiltAngle)
{
    /// <summary>
    /// Lift position of the shutter, where 0.0 represents fully closed and 1.0 represents fully open. For roller shutters, this value is either 0.0 or 1.0, while for venetian blinds it can take any value in between to represent partial opening.
    /// </summary>
    public double LiftPosition { get; set; } = liftPosition;

    public bool PreventPositionChange => LiftPosition < 0.0;

    /// <summary>
    /// Tilt angle of the shutter slats in p.u., where 0 degrees represents fully open = horizontal slats, 1.0 represents slats fully closed = vertical.
    /// </summary>
    public double TiltAngle { get; set; } = tiltAngle;

    public bool PreventTiltChange => TiltAngle < 0.0;
}