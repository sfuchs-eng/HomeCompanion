using HomeCompanion.Base.Utilities;
using Microsoft.Extensions.Logging;
using Quartz;

namespace HomeCompanion.Logics.Shutters;

/// <summary>
/// Creates runtimes for the <see cref="RoomShutterSceneLogic"/> and <see cref="ShutterController"/> logics which are provided to them by <see cref="ShadowingRuntimesController"/>.
/// The factory methods create runtimes for all rooms/buildings in the model that have a corresponding configuration, unless an existing runtime is provided for a given key. In that case, the existing runtime is untouched and not returned amongst the new ones.
/// </summary>
public class RuntimesFactory(
    IValueProvider valuesProvider,
    IEventPublisher eventPublisher,
    IEventSubscriber eventSubscriber,
    IQueueFeeder<ShutterAutomationComputationTriggerContext> computationTriggerQueueFeeder,
    TimeProvider timeProvider,
    IModelProvider modelProvider,
    ISchedulerFactory schedulerFactory,
    ILoggerFactory loggerFactory,
    ILogger<RuntimesFactory> logger
) : IRuntimesFactory
{
    private readonly IValueProvider valuesProvider = valuesProvider;
    private readonly IEventPublisher eventPublisher = eventPublisher;
    private readonly IEventSubscriber eventSubscriber = eventSubscriber;
    private readonly IQueueFeeder<ShutterAutomationComputationTriggerContext> computationTriggerQueueFeeder = computationTriggerQueueFeeder;
    private readonly TimeProvider timeProvider = timeProvider;
    private readonly IModelProvider modelProvider = modelProvider;
    private readonly ISchedulerFactory schedulerFactory = schedulerFactory;
    private readonly ILoggerFactory loggerFactory = loggerFactory;
    private readonly ILogger<RuntimesFactory> logger = logger;

    /// <summary>
    /// Creates a new <see cref="BuildingRuntime"/> for each building in the model if it has a <see cref="ShadowingSpecial"/> with shutter automation enabled.
    /// If an existing runtime is provided for a building, no new runtime is created for that building.
    /// </summary>
    /// <returns>New runtimes, excluding any existing ones</returns>
    public Dictionary<BuildingKey, BuildingRuntime> CreateBuildingRuntimes(Dictionary<BuildingKey, BuildingRuntime>? existingRuntimes = null) => BuildingRuntime.Create(new RuntimeCreationContext<BuildingKey, BuildingRuntime>(
        modelProvider.GetModel(),
        existingRuntimes,
        valuesProvider,
        eventPublisher,
        eventSubscriber,
        computationTriggerQueueFeeder,
        schedulerFactory,
        timeProvider,
        loggerFactory
    ));

    /// <summary>
    /// Creates a new <see cref="RoomRuntime"/> for each room in the model if the corresponding building has a <see cref="ShadowingSpecial"/>.
    /// If an existing runtime is provided for a room, no new runtime is created for that room.
    /// </summary>
    /// <param name="model"></param>
    /// <param name="existingRoomRuntimes">An existing runtime for a given room key is not recreated</param>
    /// <returns>New runtimes, excluding any existing ones</returns>
    public Dictionary<RoomKey, RoomRuntime> CreateRoomRuntimes(Dictionary<RoomKey, RoomRuntime>? existingRoomRuntimes = null) => RoomRuntime.Create(new RuntimeCreationContext<RoomKey, RoomRuntime>(
        modelProvider.GetModel(),
        existingRoomRuntimes,
        valuesProvider,
        eventPublisher,
        eventSubscriber,
        computationTriggerQueueFeeder,
        schedulerFactory,
        timeProvider,
        loggerFactory
    ));

    public Dictionary<ShutterKey, ShutterRuntime> CreateShutterRuntimes(Dictionary<ShutterKey, ShutterRuntime>? existingShutterRuntimes = null) => ShutterRuntime.Create(new RuntimeCreationContext<ShutterKey, ShutterRuntime>(
        modelProvider.GetModel(),
        existingShutterRuntimes,
        valuesProvider,
        eventPublisher,
        eventSubscriber,
        computationTriggerQueueFeeder,
        schedulerFactory,
        timeProvider,
        loggerFactory
    ));
}

public interface IRuntimesFactory
{
    Dictionary<BuildingKey, BuildingRuntime> CreateBuildingRuntimes(Dictionary<BuildingKey, BuildingRuntime>? existingRuntimes = null);
    Dictionary<RoomKey, RoomRuntime> CreateRoomRuntimes(Dictionary<RoomKey, RoomRuntime>? existingRoomRuntimes = null);
    Dictionary<ShutterKey, ShutterRuntime> CreateShutterRuntimes(Dictionary<ShutterKey, ShutterRuntime>? existingShutterRuntimes = null);
}

public record class RuntimeCreationContext<TKey, TRuntime>(
    Model Model,
    Dictionary<TKey, TRuntime>? ExistingRuntimes,
    IValueProvider ValuesProvider,
    IEventPublisher EventPublisher,
    IEventSubscriber EventSubscriber,
    IQueueFeeder<ShutterAutomationComputationTriggerContext> ComputationTriggerQueueFeeder,
    ISchedulerFactory SchedulerFactory,
    TimeProvider TimeProvider,
    ILoggerFactory LoggerFactory
) where TKey : notnull, IThingKey
  where TRuntime : IThingRuntime;