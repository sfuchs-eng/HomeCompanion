using HomeCompanion.Base.Model;
using HomeCompanion.Base.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters;

/// <summary>
/// Manages the lifecycles of runtimes for buildings, rooms, and shutters, and handles the distribution of shutter automation computation triggers.
/// Triggers are sent to the event bus as <see cref="ShutterAutomationComputationTriggerEvent"/> events for consumption by the <see cref="ShutterController"/>, <see cref="RoomShutterSceneLogic"/>, and potentially other components.
/// </summary>
public class ShadowingRuntimesController : LogicBase, IRuntimesProvider
{
//    private readonly IValueProvider valuesProvider;
//    private readonly IEventPublisher eventPublisher;
//    private readonly IEventSubscriber eventSubscriber;
//    private readonly TimeProvider timeProvider;
    private readonly IModelProvider modelProvider;
    private readonly IQueueFeeder<ShutterAutomationComputationTriggerContext> computationTriggerQueueFeeder;
//    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<ShadowingRuntimesController> logger;
    private readonly RuntimesFactory runtimesFactory;

    private readonly Dictionary<BuildingKey, BuildingRuntime> buildingRuntimes = [];
    private readonly Dictionary<RoomKey, RoomRuntime> roomRuntimes = [];
    private readonly Dictionary<ShutterKey, ShutterRuntime> shutterRuntimes = [];

    public IReadOnlyDictionary<BuildingKey, BuildingRuntime> BuildingRuntimes => buildingRuntimes;

    public IReadOnlyDictionary<RoomKey, RoomRuntime> RoomRuntimes => roomRuntimes;

    public IReadOnlyDictionary<ShutterKey, ShutterRuntime> ShutterRuntimes => shutterRuntimes;

    [ActivatorUtilitiesConstructor]
    public ShadowingRuntimesController(
        IValueProvider valuesProvider,
        IEventPublisher eventPublisher,
        IEventSubscriber eventSubscriber,
        TimeProvider timeProvider,
        IModelProvider modelProvider,
        ILoggerFactory loggerFactory
) : base(eventPublisher, eventSubscriber)
    {
        //      this.valuesProvider = valuesProvider;
        //      this.eventPublisher = eventPublisher;
        //      this.eventSubscriber = eventSubscriber;
        //      this.timeProvider = timeProvider;
        this.modelProvider = modelProvider;
        computationTriggerQueueFeeder = new FeedTriggerQueueViaEventBus(eventPublisher);
        //      this.loggerFactory = loggerFactory;
        this.logger = loggerFactory.CreateLogger<ShadowingRuntimesController>();
        runtimesFactory = new(valuesProvider, eventPublisher, eventSubscriber, computationTriggerQueueFeeder, timeProvider, modelProvider, loggerFactory, loggerFactory.CreateLogger<RuntimesFactory>());
    }

    // constructor for testing, allowing to inject a custom queue feeder for the computation triggers
    public ShadowingRuntimesController(
        IValueProvider valuesProvider,
        IEventPublisher eventPublisher,
        IEventSubscriber eventSubscriber,
        TimeProvider timeProvider,
        IModelProvider modelProvider,
        IQueueFeeder<ShutterAutomationComputationTriggerContext> computationTriggerQueueFeeder,
        ILoggerFactory loggerFactory
) : base(eventPublisher, eventSubscriber)
    {
        //      this.valuesProvider = valuesProvider;
        //      this.eventPublisher = eventPublisher;
        //      this.eventSubscriber = eventSubscriber;
        //      this.timeProvider = timeProvider;
        this.modelProvider = modelProvider;
        this.computationTriggerQueueFeeder = computationTriggerQueueFeeder;
        //      this.loggerFactory = loggerFactory;
        this.logger = loggerFactory.CreateLogger<ShadowingRuntimesController>();
        runtimesFactory = new(valuesProvider, eventPublisher, eventSubscriber, computationTriggerQueueFeeder, timeProvider, modelProvider, loggerFactory, loggerFactory.CreateLogger<RuntimesFactory>());
    }

    /// <summary>
    /// Feeds shutter automation computation triggers into the event bus.
    /// </summary>
    private class FeedTriggerQueueViaEventBus(IEventPublisher eventPublisher) : IQueueFeeder<ShutterAutomationComputationTriggerContext>
    {
        private readonly IEventPublisher eventPublisher = eventPublisher;

        public void Enqueue(ShutterAutomationComputationTriggerContext trigger)
        {
            // Enqueue the trigger by publishing it as an event to the event bus.
            eventPublisher.PublishAsync(new ShutterAutomationComputationTriggerEvent { Context = trigger }).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public async ValueTask EnqueueAsync(ShutterAutomationComputationTriggerContext item, CancellationToken cancellationToken = default)
        {
            await eventPublisher.PublishAsync(new ShutterAutomationComputationTriggerEvent { Context = item }, cancellationToken).ConfigureAwait(false);
        }

        async Task IQueueFeeder<ShutterAutomationComputationTriggerContext>.EnqueueAsync(ShutterAutomationComputationTriggerContext trigger, CancellationToken token)
        {
            await EnqueueAsync(trigger, token);
        }
    }

    protected override async Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
    {
        await MaterializeRuntime(cancellationToken);
    }

    private async Task MaterializeRuntime(CancellationToken cancellationToken = default)
    {
        var model = modelProvider.GetModel();
        if (!CheckConfiguration(model))
        {
            logger.LogWarning("Model configuration issues detected. Please check the logs for details. The runtimes will not be materialized until the issues are resolved.");
            return;
        }

        await UpdateRuntimes(buildingRuntimes, runtimesFactory.CreateBuildingRuntimes(buildingRuntimes), cancellationToken);
        await UpdateRuntimes(roomRuntimes, runtimesFactory.CreateRoomRuntimes(roomRuntimes), cancellationToken);
        await UpdateRuntimes(shutterRuntimes, runtimesFactory.CreateShutterRuntimes(shutterRuntimes), cancellationToken);
    }

    private static async Task UpdateRuntimes<TKey, TRuntime>(Dictionary<TKey, TRuntime> runtimesRepo, Dictionary<TKey, TRuntime> newRuntimes, CancellationToken cancellationToken = default)
        where TRuntime : IThingRuntime
        where TKey : notnull, IThingKey
    {
        // newRuntimes: if same key is in runtimesRepo, stop and remove it. Then add and start the new runtime. If not in runtimesRepo, just add and start it.
        foreach (var kvp in newRuntimes)
        {
            if (runtimesRepo.TryGetValue(kvp.Key, out var existingRuntime))
            {
                await existingRuntime.StopAsync(cancellationToken);
                runtimesRepo.Remove(kvp.Key);
                kvp.Value.Dispose();
            }
            runtimesRepo[kvp.Key] = kvp.Value;
            await kvp.Value.StartAsync(cancellationToken);
        }
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
    /// <returns>True if no issues were found, false if any issues were found and logged as warnings or errors.</returns>
    public bool CheckConfiguration(Model model)
    {
        var success = true;
        var allShutters = model.EnumerateShutterContexts().ToArray();

        //=====
        // shadowing special?
        var buildingsNotHavingExactlyOneShadowingSpecial = model.Buildings.Values
            .Where(b => !b.TryGetShadowingSpecial(out var _));
        if (buildingsNotHavingExactlyOneShadowingSpecial.Any())
        {
            logger.LogWarning("The following buildings do not have a shadowing special configured, which is required for the shutter controller to function properly. Please check the model configuration for these buildings: {Buildings}", string.Join(", ", buildingsNotHavingExactlyOneShadowingSpecial.Select(b => b.Name)));
            success = false;
        }

        //=====
        // Do all shutters have the necessary IValue references bound for the corresponding type? E.g. position/angle for venetian blinds, position for roller shutters, open/close for simple ones?
        var shuttersWithMissingReferences = allShutters.Select(sk => sk.Shutter)
            .Where(sc => (sc.Configuration.Type == ShutterType.VenetianBlind && (string.IsNullOrEmpty(sc.Configuration.PositionValueReference) || string.IsNullOrEmpty(sc.Configuration.AngleValueReference) || sc.PositionValue == null || sc.AngleValue == null))
                || (sc.Configuration.Type == ShutterType.Positional && (string.IsNullOrEmpty(sc.Configuration.PositionValueReference) || sc.PositionValue == null))
                || (sc.Configuration.Type == ShutterType.OpenClose && (string.IsNullOrEmpty(sc.Configuration.OpenCloseReference) || sc.OpenCloseValue == null)))
            .ToHashSet();

        foreach (var shutterCtx in allShutters.Where(sk => shuttersWithMissingReferences.Contains(sk.Shutter)))
        {
            logger.LogError("Shutter {ShutterKey} is missing necessary IValue references for its type {ShutterType}. Please check the configuration for this shutter. Shutter configuration: {ShutterConfig}", shutterCtx.Key, shutterCtx.Shutter.Configuration.Type, shutterCtx.Shutter.Configuration);
            success = false;
        }

        //=====
        // Do all shutters have a facade bound?
        var shuttersWithMissingFacade = allShutters
            .Where(sk => sk.Shutter.Facade == null);
        foreach (var shutterCtx in shuttersWithMissingFacade)
        {
            logger.LogError("Shutter {ShutterKey} has no facade bound. Please check the configuration for this shutter. Shutter configuration: {ShutterConfig}", shutterCtx.Key, shutterCtx.Shutter.Configuration);
            success = false;
        }

        //=====
        // Do all shutters belong to a room with a shutter scene configured? If there are such without, is there a global shutter scene bound as fall back?
        var roomsWithShuttersWithoutScene = allShutters
            .Where(sk => sk.Room.ShutterScene == null && sk.Building.TryGetShadowingSpecial(out var shadowingSpecial) && shadowingSpecial.GlobalShutterScene == null)
            .Distinct();
        foreach (var roomCtx in roomsWithShuttersWithoutScene)
        {
            logger.LogWarning("Room {RoomKey} has shutters but no shutter scene configured, and there is no global shutter scene configured in the building's shadowing special as fallback. Please check the configuration for this room and building. Room configuration: {RoomConfig}", roomCtx.RoomKey, roomCtx.Room.Configuration);
            success = false;
        }
        return success;
    }
}