using HomeCompanion.Base.Utilities;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters;

public class BuildingRuntime(
    BuildingContext buildingContext,
    IQueueFeeder<ShutterAutomationComputationTriggerContext> queueFeeder,
    ILogger<BuildingRuntime> logger
) : RuntimeBase(logger)
{
    public BuildingKey BuildingKey { get; } = buildingContext.BuildingKey;
    public BuildingContext BuildingContext { get; } = buildingContext;
    protected ShadowingSpecial? ShadowingSpecialRegisteredWith { get; private set; } = null;
    private readonly IQueueFeeder<ShutterAutomationComputationTriggerContext> queueFeeder = queueFeeder;
    private readonly ILogger<BuildingRuntime> logger = logger;

    public override Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public override Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Register with IValue events for all relevant building level inputs
        var bs = BuildingContext.Building.GetShadowingSpecial();
        ShadowingSpecialRegisteredWith = bs;

        bs.Absence?.Changed += HandleValueChangedEvent_NormalAllRooms;
        bs.DisableAutoShadowAssessment?.Changed += HandleValueChangedEvent_FastAllRooms;
        bs.GlobalShutterScene?.Written += HandleValueWrittenEvent_FastAllRooms;
        bs.ThermalControlMode?.Changed += HandleValueChangedEvent_NormalAllRooms;

        // Environmental measurements are handled by <see cref="EnvironmentalsEvaluatorLogic"/> which will trigger shutter automation computations as needed.

        return Task.CompletedTask;
    }

    private void HandleValueChangedEvent_SlowEnvironmentalAll(object? sender, ValueChangedEventArgs e)
    {
        // Enqueue a shutter automation computation trigger for all rooms and shuttters in the building, as environmental changes may affect all rooms and their shutters.
        // issue a normal one for the rooms, followed by a slow one for the shutters. Like this the shuttters will be handled after the rooms have been handled, preventing double shutter evaluation.
        HandleValueWrittenEvent(sender, e.Timestamp, [e.NewValue], [e], ShutterAutomationComputationTriggerUrgency.Normal, ShutterAutomationComputationTriggerUrgency.Slow);
    }

    private void HandleValueChangedEvent_NormalAllRooms(object? sender, ValueChangedEventArgs e)
    {
        // Enqueue a shutter automation computation trigger for all rooms in the building, as changes to absence or thermal control mode may affect all rooms and their shutters.
        HandleValueWrittenEvent(sender, e.Timestamp, [e.NewValue], [e], ShutterAutomationComputationTriggerUrgency.Normal, ShutterAutomationComputationTriggerUrgency.Slow);
    }

    private void HandleValueChangedEvent_FastAllRooms(object? sender, ValueChangedEventArgs e)
    {
        HandleValueWrittenEvent(sender, e.Timestamp, [e.NewValue], [e], ShutterAutomationComputationTriggerUrgency.Immediate, ShutterAutomationComputationTriggerUrgency.Immediate);
    }

    private void HandleValueWrittenEvent_FastAllRooms(object? sender, ValueWrittenEventArgs e)
    {
        HandleValueWrittenEvent(sender, e.Timestamp, [e.NewValue], [e], ShutterAutomationComputationTriggerUrgency.Immediate, ShutterAutomationComputationTriggerUrgency.Immediate);
    }

    private void HandleValueWrittenEvent(object? sender, DateTimeOffset timeStamp, IEnumerable<IValue> values, IEnumerable<ValueEventArgs> valueEventArgs, ShutterAutomationComputationTriggerUrgency roomUrgency, ShutterAutomationComputationTriggerUrgency shutterUrgency)
    {
        // trigger rooms
        var triggerContext = new ShutterAutomationComputationTriggerContext(
            thingKeys: BuildingContext.EnumerateRoomKeys(),
            scope: ShutterAutomationComputationScope.RoomSpecific,
            triggeringValue: values,
            valueEventArgs: valueEventArgs,
            timestamp: timeStamp,
            urgency: roomUrgency
        );
        queueFeeder.Enqueue(triggerContext);

        // trigger shutters
        var triggerContextShutters = new ShutterAutomationComputationTriggerContext(
            thingKeys: BuildingContext.EnumerateShutterKeys(),
            scope: ShutterAutomationComputationScope.ShutterSpecific,
            triggeringValue: values,
            valueEventArgs: valueEventArgs,
            timestamp: timeStamp,
            urgency: shutterUrgency
        );
        queueFeeder.Enqueue(triggerContextShutters);
    }

    public override Task StopAsync(CancellationToken cancellationToken = default)
    {
        // unregister with IValue events for all that was registered in StartAsync
        var bs = ShadowingSpecialRegisteredWith;
        if (bs == null)
        {
            logger.LogWarning("BuildingRuntime for building {BuildingKey} was not registered with a ShadowingSpecial. Cannot unregister event handlers.", BuildingKey.Key);
            return Task.CompletedTask;
        }
        bs.Absence?.Changed -= HandleValueChangedEvent_NormalAllRooms;
        bs.DisableAutoShadowAssessment?.Changed -= HandleValueChangedEvent_FastAllRooms;
        bs.GlobalShutterScene?.Written -= HandleValueWrittenEvent_FastAllRooms;
        bs.ThermalControlMode?.Changed -= HandleValueChangedEvent_NormalAllRooms;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates new runtimes for all buildings in the model unless there's already a matching key in the provided existing runtimes.
    /// </summary>
    /// <param name="runtimeCreationContext"></param>
    /// <returns>Only newly created runtimes</returns>
    public static Dictionary<BuildingKey, BuildingRuntime> Create(RuntimeCreationContext<BuildingKey, BuildingRuntime> runtimeCreationContext)
    {
        var model = runtimeCreationContext.Model;
        var existingRuntimes = runtimeCreationContext.ExistingRuntimes;
        var queueFeeder = runtimeCreationContext.ComputationTriggerQueueFeeder;
        var loggerFactory = runtimeCreationContext.LoggerFactory;
        var newRuntimes = new Dictionary<BuildingKey, BuildingRuntime>();

        foreach (var buildingContext in model.EnumerateBuildingContexts())
        {
            if (existingRuntimes != null && existingRuntimes.ContainsKey(buildingContext.BuildingKey))
            {
                continue;
            }

            var runtime = new BuildingRuntime(buildingContext, queueFeeder, loggerFactory.CreateLogger<BuildingRuntime>());
            newRuntimes[buildingContext.BuildingKey] = runtime;
        }
        return newRuntimes;
    }
}
