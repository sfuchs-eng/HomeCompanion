using HomeCompanion.Base.Model;
using HomeCompanion.Persistence;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Base.Logics.Shutters;

/// <summary>
/// Coordinates shadowing policy evaluation and shutter control decisions.
/// </summary>
/// <remarks>
/// This initial implementation materializes effective room policy metadata and restores persisted manual overrides.
/// Command planning and actuator writes are added in subsequent increments.
/// </remarks>
public class ShutterControl(
    IModelProvider modelProvider,
    IValueReferenceProvider valueReferenceProvider,
    IStateStore stateStore,
    TimeProvider timeProvider,
    ILogger<ShutterControl> logger,
    IEventPublisher publisher,
    IEventSubscriber subscriber)
    : LogicBase(publisher, subscriber)
{
    private const string StateSetName = "ShutterControlManualOverrides";

    private readonly IModelProvider _modelProvider = modelProvider;
    private readonly IValueReferenceProvider _valueReferenceProvider = valueReferenceProvider;
    private readonly IStateStore _stateStore = stateStore;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<ShutterControl> _logger = logger;
    private IRoomScheduleEvaluator _scheduleEvaluator = new InProcessCronRoomScheduleEvaluator();

    private readonly Dictionary<string, RoomPolicySnapshot> _roomPolicyByRoomKey = [];
    private readonly HashSet<IValue> _subscribedValues = [];
    private readonly SemaphoreSlim _evaluationSemaphore = new(1, 1);
    private CancellationTokenSource? _scheduleLoopCts;
    private Task? _scheduleLoopTask;
    private ShutterManualOverrideStateSet _manualOverrideState = new();

    protected override async Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
    {
        if (!_modelProvider.IsInitialized)
        {
            _logger.LogWarning("Model provider is not initialized yet. ShutterControl skipped initial materialization.");
            return;
        }

        var model = _modelProvider.GetModel();
        _scheduleEvaluator = CreateScheduleEvaluator(model);
        MaterializeRoomPolicy(model);
        RegisterDynamicInputSubscriptions(model);
        await RestoreManualOverridesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "ShutterControl initialized with {RoomCount} room policy snapshots and {ManualOverrideCount} active manual override entries.",
            _roomPolicyByRoomKey.Count,
            _manualOverrideState.RoomOverrides.Count);

        StartScheduleLoop();
    }

    public override async Task DisableAsync(CancellationToken cancellationToken = default)
    {
        if (_scheduleLoopCts is not null)
        {
            _scheduleLoopCts.Cancel();
            if (_scheduleLoopTask is not null)
            {
                try
                {
                    await _scheduleLoopTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected when disabling.
                }
            }

            _scheduleLoopCts.Dispose();
            _scheduleLoopCts = null;
            _scheduleLoopTask = null;
        }

        foreach (var value in _subscribedValues)
            value.Changed -= OnDynamicInputChanged;
        _subscribedValues.Clear();

        await base.DisableAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Materializes the room policy snapshots for all rooms in the given model.
    /// This is called on initialization and whenever the model changes in a way that may affect shadowing policy (e.g. configuration changes or dynamic input value changes).
    /// </summary>
    /// <param name="model">The model containing the buildings, floors, and rooms to materialize.</param>
    private void MaterializeRoomPolicy(HomeCompanion.Base.Model.Model model)
    {
        _roomPolicyByRoomKey.Clear();

        foreach (var building in model.Buildings.Values)
        {
            var globalShadowing = building.Specials.Values
                .OfType<ShadowingSpecial>()
                .FirstOrDefault();

            if (globalShadowing is null)
                continue;

            var globalShadowConfig = globalShadowing.Configuration;

            foreach (var floor in building.Floors.Values)
            {
                foreach (var room in floor.Rooms.Values)
                {
                    if (room.Shutters.Count == 0)
                        continue;

                    var roomConfig = room.Configuration;
                    var roomKey = CreateRoomKey(building.Name, floor.Name, room.Name);

                    var objective = ShutterPolicyResolver.ResolveRoomObjective(
                        globalShadowing,
                        room,
                        _valueReferenceProvider);
                    var automationLevel = roomConfig.AutomationLevelOverride ?? globalShadowConfig.DefaultAutomationLevel;
                    var persistManualOverride = roomConfig.PersistManualOverride ?? globalShadowConfig.PersistManualOverrides;
                    var manualOverrideDuration = roomConfig.ManualOverrideDuration ?? globalShadowConfig.DefaultManualOverrideDuration;

                    var hasInvalidCron = roomConfig.ScheduleTransitions.Values
                        .Any(schedule => !LooksLikeCronExpression(schedule.CronExpression));
                    if (hasInvalidCron)
                    {
                        _logger.LogWarning(
                            "Room '{RoomKey}' has one or more schedule transitions with a non-cron-shaped expression. " +
                            "Expected 5 or 6 whitespace-separated cron fields.",
                            roomKey);
                    }

                    _roomPolicyByRoomKey[roomKey] = new RoomPolicySnapshot(
                        roomKey,
                        objective,
                        automationLevel,
                        persistManualOverride,
                        manualOverrideDuration,
                        roomConfig.ScheduleTransitions.Count);
                }
            }
        }
    }

    private void RegisterDynamicInputSubscriptions(HomeCompanion.Base.Model.Model model)
    {
        var nextValues = new HashSet<IValue>();

        // collect all relevant dynamic input values based on the current model, including global shadowing inputs and room-level objective selector inputs
        foreach (var building in model.Buildings.Values)
        {
            foreach (var shadowing in building.Specials.Values.OfType<ShadowingSpecial>())
            {
                AddIfNotNull(nextValues, shadowing.GlobalShutterScene);
                AddIfNotNull(nextValues, shadowing.Absence);
                AddIfNotNull(nextValues, shadowing.DisableAutoShadowAssessment);
                AddIfNotNull(nextValues, shadowing.OutdoorTemperature);
                AddIfNotNull(nextValues, shadowing.SunIntensityEast);
                AddIfNotNull(nextValues, shadowing.SunIntensitySouth);
                AddIfNotNull(nextValues, shadowing.SunIntensityWest);
                AddIfNotNull(nextValues, shadowing.UvIntensity);
                AddIfNotNull(nextValues, shadowing.ThermalControlMode);
            }

            foreach (var floor in building.Floors.Values)
            {
                foreach (var room in floor.Rooms.Values)
                {
                    AddIfNotNull(nextValues, room.ShutterScene);
                    AddIfNotNull(nextValues, room.Temperature);

                    foreach (var inputRule in room.Configuration.ObjectiveSelectorInputs.Values)
                    {
                        if (string.IsNullOrWhiteSpace(inputRule.ValueReference))
                            continue;

                        if (_valueReferenceProvider.TryResolve(inputRule.ValueReference, out var value) && value is not null)
                            nextValues.Add(value);
                    }
                }
            }
        }

        // Unsubscribe from values that are no longer relevant
        foreach (var value in _subscribedValues.Except(nextValues).ToArray())
        {
            value.Changed -= OnDynamicInputChanged;
            _subscribedValues.Remove(value);
        }

        // Subscribe to new values
        foreach (var value in nextValues.Except(_subscribedValues))
        {
            value.Changed += OnDynamicInputChanged;
            _subscribedValues.Add(value);
        }
    }

    System.Threading.Timer? dynamicInputChangeDebounceTimer = null;

    private void OnDynamicInputChanged(object? sender, ValueChangedEventArgs e)
    {
        // we perform a delayed reevaluation in order to debounce potential cascades of changes and to ensure that the model is in a consistent state when we reevaluate
        // the actual value change that triggered this is not relevant for the decision to reevaluate, since we will reevaluate all relevant inputs anyway, acting on state instead of event.

        // just return in case we're already waiting for a pending debounce timer, which means a reevaluation is already scheduled. This can happen when multiple inputs change at the same time, e.g. when a new model is loaded or when multiple related inputs are updated together.
        if (dynamicInputChangeDebounceTimer is not null)
            return;

        dynamicInputChangeDebounceTimer?.Dispose();
        dynamicInputChangeDebounceTimer = new System.Threading.Timer(_ =>
        {
            dynamicInputChangeDebounceTimer?.Dispose();
            dynamicInputChangeDebounceTimer = null;
            _ = ReevaluateFromModelAsync();
        }, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
    }

    private void StartScheduleLoop()
    {
        if (_scheduleLoopTask is not null)
            return;

        _scheduleLoopCts = new CancellationTokenSource();
        _scheduleLoopTask = Task.Run(() => RunScheduleLoopAsync(_scheduleLoopCts.Token));
    }

    private IRoomScheduleEvaluator CreateScheduleEvaluator(HomeCompanion.Base.Model.Model model)
    {
        var engine = ResolveScheduleEngine(model);
        _logger.LogInformation("ShutterControl schedule engine: {Engine}", engine);

        return engine switch
        {
            ShadowingScheduleEngine.Quartz => new QuartzRoomScheduleEvaluator(),
            _ => new InProcessCronRoomScheduleEvaluator(),
        };
    }

    private static ShadowingScheduleEngine ResolveScheduleEngine(HomeCompanion.Base.Model.Model model)
    {
        foreach (var building in model.Buildings.Values)
        {
            var config = building.Specials.Values
                .OfType<ShadowingSpecial>()
                .FirstOrDefault()?.Configuration;

            if (config is not null)
                return config.ScheduleEngine;
        }

        return ShadowingScheduleEngine.InProcess;
    }

    private async Task RunScheduleLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        await ReevaluateFromModelAsync().ConfigureAwait(false);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await ReevaluateFromModelAsync().ConfigureAwait(false);
            await EvaluateDueSchedulesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EvaluateDueSchedulesAsync(CancellationToken cancellationToken)
    {
        if (!_modelProvider.IsInitialized)
            return;

        var model = _modelProvider.GetModel();
        var dueTransitions = _scheduleEvaluator.EvaluateDueTransitions(model, _timeProvider.GetUtcNow());
        if (dueTransitions.Count == 0)
            return;

        foreach (var due in dueTransitions)
        {
            _logger.LogInformation(
                "Room schedule transition due: room={RoomKey}, schedule={ScheduleKey}, scene={Scene}, closeOnly={CloseOnly}, triggerLocal={TriggerLocal}",
                due.RoomKey,
                due.ScheduleKey,
                due.Scene,
                due.CloseOnly,
                due.TriggerLocalTime);

            await Publisher.PublishAsync(new RoomScheduleTransitionDueEvent
            {
                Timestamp = _timeProvider.GetUtcNow(),
                RoomKey = due.RoomKey,
                ScheduleKey = due.ScheduleKey,
                Scene = due.Scene,
                CloseOnly = due.CloseOnly,
                ManualOpenGracePeriod = due.ManualOpenGracePeriod,
                EnableShadowTranslationAfterManualOpen = due.EnableShadowTranslationAfterManualOpen,
                TriggerLocalTime = due.TriggerLocalTime,
                ResumeAutomationAfter = due.ResumeAutomationAfter,
                ResumeAutomationAtLocalTime = due.ResumeAutomationAtLocalTime,
                ResumeAutomationScene = due.ResumeAutomationScene,
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReevaluateFromModelAsync()
    {
        if (!_modelProvider.IsInitialized)
            return;

        await _evaluationSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var model = _modelProvider.GetModel();
            MaterializeRoomPolicy(model);
            RegisterDynamicInputSubscriptions(model);
        }
        finally
        {
            _evaluationSemaphore.Release();
        }
    }

    private static void AddIfNotNull(HashSet<IValue> values, IValue? value)
    {
        if (value is not null)
            values.Add(value);
    }

    private async Task RestoreManualOverridesAsync(CancellationToken cancellationToken)
    {
        var loaded = await _stateStore
            .LoadAsync<ShutterManualOverrideStateSet>(StateSetName, TimeSpan.FromDays(30))
            .ConfigureAwait(false);

        _manualOverrideState = loaded.StateSet ?? new ShutterManualOverrideStateSet();

        var now = _timeProvider.GetUtcNow();
        var expiredKeys = _manualOverrideState.RoomOverrides
            .Where(kv => kv.Value.ExpiresAtUtc <= now)
            .Select(kv => kv.Key)
            .ToArray();

        foreach (var key in expiredKeys)
            _manualOverrideState.RoomOverrides.Remove(key);
    }

    internal static string CreateRoomKey(string buildingName, string floorName, string roomName)
        => $"{buildingName}/{floorName}/{roomName}";

    internal static bool LooksLikeCronExpression(string cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            return false;

        var fieldCount = cronExpression
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;

        return fieldCount is 5 or 6;
    }
}

internal sealed record RoomPolicySnapshot(
    string RoomKey,
    RoomObjectiveProfile Objective,
    ShadowingAutomationLevel AutomationLevel,
    bool PersistManualOverride,
    TimeSpan ManualOverrideDuration,
    int ScheduleTransitionCount);

/// <summary>
/// Persisted manual override state for <see cref="ShutterControl"/>.
/// </summary>
public class ShutterManualOverrideStateSet
{
    /// <summary>
    /// Room-scoped overrides keyed as <c>Building/Floor/Room</c>.
    /// </summary>
    public Dictionary<string, ShutterManualOverrideEntry> RoomOverrides { get; set; } = [];
}

/// <summary>
/// Manual override entry with creation and expiry timestamps.
/// </summary>
public class ShutterManualOverrideEntry
{
    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// Expiry timestamp.
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

/// <summary>
/// Published when a room schedule transition becomes due.
/// </summary>
public sealed class RoomScheduleTransitionDueEvent : HomeCompanionEvent
{
    public string RoomKey { get; init; } = string.Empty;
    public string ScheduleKey { get; init; } = string.Empty;
    public int Scene { get; init; }
    public bool CloseOnly { get; init; }
    public TimeSpan ManualOpenGracePeriod { get; init; }
    public bool EnableShadowTranslationAfterManualOpen { get; init; }
    public DateTime TriggerLocalTime { get; init; }
    public TimeSpan? ResumeAutomationAfter { get; init; }
    public TimeSpan? ResumeAutomationAtLocalTime { get; init; }
    public int? ResumeAutomationScene { get; init; }
}
