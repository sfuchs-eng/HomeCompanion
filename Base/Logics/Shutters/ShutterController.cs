using System.Threading.Channels;
using HomeCompanion.Base.Model;
using HomeCompanion.Base.Utilities;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters;

/// <summary>
/// Controls the operation of shutters, including processing shutter targets and executing the necessary actions to achieve the desired shutter states.
/// It contains the full automation stack for shutter operation, including the logic to determine the target shutter positions based on various inputs such as time of day, weather conditions, and user preferences, as well as the logic to execute the necessary actions to bring the shutters into the desired state, e.g. by sending commands to the actuators, while also implementing safety interlocks and movement rate limits as needed.
/// It's using <see cref="HomeCompanion.Base.Model"/> as configuration base and to connect to the <see cref="IValue"/> related framework.
/// It consists of multiple proccessing stages:
/// - Input events are collected and assessed whether to immediately process or to defer and aggregate them for a later processing, e.g. to avoid excessive shutter movements in case of rapidly changing input conditions; this is done in the <b>event processing loop</b>
/// - The <b>state computation loop</b> determines the desired target state for each shutter, e.g. based on time of day, weather conditions, user preferences, etc.
/// - The desired target states are then sent to the <b>shutter target processing loop</b>
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
    /// <see cref="IValue"/> or event bus received triggers are first enqueued into the <b>shutter automation trigger collector</b>.
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
        var model = modelProvider.GetModel();
        CheckConfiguration(model);

        // hook up to value changed / written events
        // must be done while queue runners are not active yet as for a while there will be a partial setup which wouldn't survive multi-threading.
        await MaterializeRuntime(model);

        // starting the background runners is done at the end of initialization to ensure that the full setup is in place before any triggers are processed, as the runners will start processing triggers as soon as they are started, and we want to avoid processing triggers before the setup is complete to prevent errors and ensure that all triggers are processed correctly.
        shutterAutomationTriggerCollector.Start(cancellationToken);

        // run the shutter automation computation loop on a linked cancellation token that is cancelled when the logic is stopped;
        shutterAutomationStateComputationLoop.Start(cancellationToken);

        // run the shutter target processing loop on a linked cancellation token that is cancelled when the logic is stopped;
        shutterTargetProcessingLoop.Start(cancellationToken);

        // register with shutter runtimes to handle external overrides, e.g. when a shutter is manually operated via a wall switch or remote control, to ensure that the automation logic is aware of the manual operation and can adjust its behavior accordingly, e.g. by temporarily pausing automation for the affected shutter
        foreach (var shutterRuntime in shutterRuntimes.Values)
        {
            shutterRuntime.ShutterExternalOverride += HandleShutterExternalOverride;
        }
    }

    private void HandleShutterExternalOverride(object? sender, ShutterExternalOverrideEventArgs e)
    {
        throw new NotImplementedException();
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
                    var triggersGroupedByShutter = queuedTriggers.SelectMany(tc => tc.ShutterKeys.Select(sk => (TriggerContext: tc, ShutterKey: sk)))
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

    /// <summary>
    /// Create shutter runtimes from the model configuration, and reuse existing runtimes where possible to avoid unnecessary restarts and loss of state in the runtimes; new runtimes are created for new shutters, and runtimes that are no longer needed are stopped and removed.
     /// The method returns the new set of runtimes that should be active based on the current model configuration, but does not start or stop any runtimes itself, this is done in <see cref="MaterializeRuntime"/> to ensure that all necessary checks and preparations are done before starting the runtimes.
     /// The method is called on initialization and whenever the model configuration changes, e.g. when a new shutter is added, removed or reconfigured in the model, to ensure that the runtime state is always in sync with the model configuration.
    /// </summary>
    /// <param name="model"></param>
    /// <returns></returns>
    private async Task MaterializeRuntime(Model model, CancellationToken token = default)
    {
        // ** buildings **
        var newBuildingRuntimes = CreateBuildingRuntimes(model);

        // Stop runtimes that are not needed anymore
        foreach (var oldRuntime in buildingRuntimes.Values)
        {
            if (!newBuildingRuntimes.ContainsKey(oldRuntime.BuildingKey))
                await oldRuntime.StopAsync(token);
        }

        // Start new runtimes
        foreach (var newRuntime in newBuildingRuntimes.Values)
        {
            if (!buildingRuntimes.ContainsKey(newRuntime.BuildingKey))
                await newRuntime.StartAsync(token);
        }

        buildingRuntimes.Clear();
        foreach (var kvp in newBuildingRuntimes)
            buildingRuntimes[kvp.Key] = kvp.Value;


        // ** rooms **
        var newRoomRuntimes = CreateRoomRuntimes(model);

        // Stop runtimes that are not needed anymore
        foreach (var oldRuntime in roomRuntimes.Values)
        {
            if (!newRoomRuntimes.ContainsKey(oldRuntime.RoomKey))
                await oldRuntime.StopAsync(token);
        }

        // Start new runtimes
        foreach (var newRuntime in newRoomRuntimes.Values)
        {
            if (!roomRuntimes.ContainsKey(newRuntime.RoomKey))
                await newRuntime.StartAsync(token);
        }

        roomRuntimes.Clear();
        foreach (var kvp in newRoomRuntimes)
            roomRuntimes[kvp.Key] = kvp.Value;


        // ** shutters **
        var newRuntimes = CreateShutterRuntimes(model);

        // Stop runtimes that are not needed anymore
        foreach (var oldRuntime in shutterRuntimes.Values)
        {
            if (!newRuntimes.ContainsKey(oldRuntime.ShutterKey))
                await oldRuntime.StopAsync(token);
        }

        // Start new runtimes
        foreach (var newRuntime in newRuntimes.Values)
        {
            if (!shutterRuntimes.ContainsKey(newRuntime.ShutterKey))
                await newRuntime.StartAsync(token);
        }

        shutterRuntimes.Clear();
        foreach (var kvp in newRuntimes)
            shutterRuntimes[kvp.Key] = kvp.Value;
    }

    private Dictionary<BuildingKey, BuildingRuntime> CreateBuildingRuntimes(Model model)
    {
        var runtimes = new Dictionary<BuildingKey, BuildingRuntime>();
        foreach (var building in model.Buildings.Values)
        {
            var buildingKey = new BuildingKey(building);
            if (buildingRuntimes.TryGetValue(buildingKey, out var existingRuntime))
            {
                runtimes[buildingKey] = existingRuntime;
                continue;
            }

            var runtime = new BuildingRuntime(buildingKey, shutterAutomationTriggerCollector, loggerFactory.CreateLogger<BuildingRuntime>());
            runtimes[buildingKey] = runtime;
        }
        return runtimes;
    }

    private Dictionary<RoomKey, RoomRuntime> CreateRoomRuntimes(Model model)
    {
        var runtimes = new Dictionary<RoomKey, RoomRuntime>();
        foreach (var roomKey in model.EnumerateRooms())
        {
            if (roomRuntimes.TryGetValue(roomKey, out var existingRuntime))
            {
                runtimes[roomKey] = existingRuntime;
                continue;
            }

            var runtime = new RoomRuntime(roomKey, shutterAutomationTriggerCollector, loggerFactory.CreateLogger<RoomRuntime>());
            runtimes[roomKey] = runtime;
        }
        return runtimes;
    }

    private Dictionary<ShutterKey, ShutterRuntime> CreateShutterRuntimes(Model model)
    {
        var runtimes = new Dictionary<ShutterKey, ShutterRuntime>();
        foreach (var shutterKey in model.EnumerateShutters())
        {
            if (shutterRuntimes.TryGetValue(shutterKey, out var existingRuntime))
            {
                runtimes[shutterKey] = existingRuntime;
                continue;
            }

            var runtime = new ShutterRuntime(shutterKey, shutterAutomationTriggerCollector, loggerFactory.CreateLogger<ShutterRuntime>());
            runtimes[shutterKey] = runtime;
        }
        return runtimes;
    }

    /// <summary>
    /// Check the aspects in the model that would result in errors or warning when <see cref="MaterializeRuntime"/> is executed, and log them as warnings or errors.
    /// This allows to detect and fix model issues before they manifest as incorrect or missing shutter commands or failed scene changes at runtime.
    /// The checks include:
    /// - Are there any buildings without a shadowing special configured, which is required for the shutter controller to function properly?
    /// - Do all shutters have the necessary IValue references bound for the corresponding type? E.g. position/angle for venetian blinds, position for roller shutters, open/close for simple ones?
    /// - Do all shutters have a facade bound?
    /// - Do all shutters belong to a room with a shutter scene configured? If there are such without, is there a global shutter scene bound as fall back?
    /// </summary>
    private void CheckConfiguration(Model model)
    {
        var allShutters = model.EnumerateShutters().ToArray();

        //=====
        // shadowing special?
        var buildingsNotHavingExactlyOneShadowingSpecial = model.Buildings.Values
            .Where(b => !b.TryGetShadowingSpecial(out var _));
        if (buildingsNotHavingExactlyOneShadowingSpecial.Any())
        {
            logger.LogWarning("The following buildings do not have a shadowing special configured, which is required for the shutter controller to function properly. Please check the model configuration for these buildings: {Buildings}", string.Join(", ", buildingsNotHavingExactlyOneShadowingSpecial.Select(b => b.Name)));
        }

        //=====
        // Do all shutters have the necessary IValue references bound for the corresponding type? E.g. position/angle for venetian blinds, position for roller shutters, open/close for simple ones?
        var shuttersWithMissingReferences = allShutters
            .Where(sk => (sk.ShutterConfig.Type == ShutterType.VenetianBlind && (string.IsNullOrEmpty(sk.ShutterConfig.PositionValueReference) || string.IsNullOrEmpty(sk.ShutterConfig.AngleValueReference) || sk.Shutter.PositionValue == null || sk.Shutter.AngleValue == null))
                || (sk.ShutterConfig.Type == ShutterType.Positional && (string.IsNullOrEmpty(sk.ShutterConfig.PositionValueReference) || sk.Shutter.PositionValue == null))
                || (sk.ShutterConfig.Type == ShutterType.OpenClose && (string.IsNullOrEmpty(sk.ShutterConfig.OpenCloseReference) || sk.Shutter.OpenCloseValue == null)));

        foreach (var shutter in shuttersWithMissingReferences)
        {
            logger.LogError("Shutter {ShutterKey} is missing necessary IValue references for its type {ShutterType}. Please check the configuration for this shutter. Shutter configuration: {ShutterConfig}", shutter.Key, shutter.ShutterConfig.Type, shutter.ShutterConfig);
        }

        //=====
        // Do all shutters have a facade bound?
        var shuttersWithMissingFacade = allShutters
            .Where(sk => sk.Shutter.Facade == null);
        foreach (var shutter in shuttersWithMissingFacade)
        {
            logger.LogError("Shutter {ShutterKey} has no facade bound. Please check the configuration for this shutter. Shutter configuration: {ShutterConfig}", shutter.Key, shutter.ShutterConfig);
        }

        //=====
        // Do all shutters belong to a room with a shutter scene configured? If there are such without, is there a global shutter scene bound as fall back?
        var roomsWithShuttersWithoutScene = allShutters
            .Where(sk => sk.RoomKey.Room.ShutterScene == null && sk.RoomKey.Building.TryGetShadowingSpecial(out var shadowingSpecial) && shadowingSpecial.GlobalShutterScene == null)
            .Select(sk => sk.RoomKey)
            .Distinct();
        foreach (var roomKey in roomsWithShuttersWithoutScene)
        {
            logger.LogWarning("Room {RoomKey} has shutters but no shutter scene configured, and there is no global shutter scene configured in the building's shadowing special as fallback. Please check the configuration for this room and building. Room configuration: {RoomConfig}", roomKey, roomKey.Room);
        }
    }
}

/// <summary>
/// Encapsulates the desired target state for a shutter, including the logic to determine the target position based on various inputs such as time of day, weather conditions, and user preferences.
/// Serves as input for shutter actuation, allowing for a last gating possibility to e.g. limit movement rates, or implement safety interlocks at shutter level just prior actuator hardware.
/// </summary>
internal class ShutterTarget(ShutterKey shutterKey, ShutterPosition targetPosition)
{
    public ShutterKey ShutterKey { get; } = shutterKey;
    public ShutterPosition TargetPosition { get; } = targetPosition;
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
    public double LiftPosition { get; } = liftPosition;

    /// <summary>
    /// Tilt angle of the shutter slats in p.u., where 0 degrees represents fully open = horizontal slats, 1.0 represents slats fully closed = vertical.
    /// </summary>
    public double TiltAngle { get; } = tiltAngle;
}