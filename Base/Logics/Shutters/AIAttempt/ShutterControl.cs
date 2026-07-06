using HomeCompanion.Base.Model;
using HomeCompanion.Base.Utilities;
using HomeCompanion.Persistence;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace HomeCompanion.Base.Logics.Shutters.AIAttempt;

/// <summary>
/// Coordinates shadowing policy evaluation and shutter control decisions.
/// Commands the shuttters directly, executing also complex scene commands that may involve multiple actuators according configured IValue targets.
/// Determining and setting room scenes per se is the responsibility of <see cref="ShutterSceneCommandControl"/>. 
/// </summary>
/// <remarks>
/// This initial implementation materializes effective room policy metadata and restores persisted manual overrides.
/// Command planning and actuator writes are added in subsequent increments.
/// </remarks>
public class ShutterControl(
    IModelProvider modelProvider,
    IValueProvider valueReferenceProvider,
    IStateStore stateStore,
    TimeProvider timeProvider,
    ILogger<ShutterControl> logger,
    IEventPublisher publisher,
    IEventSubscriber subscriber)
    : LogicBase(publisher, subscriber)
{
    private const string StateSetName = "ShutterControlManualOverrides";

    private readonly IModelProvider _modelProvider = modelProvider;
    private readonly IValueProvider _valueReferenceProvider = valueReferenceProvider;
    private readonly IStateStore _stateStore = stateStore;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<ShutterControl> _logger = logger;
    private IRoomScheduleEvaluator _scheduleEvaluator = new InProcessCronRoomScheduleEvaluator();
    private readonly object _manualOverrideSync = new();
    private readonly object _roomPolicySync = new();
    private readonly object _commandRuntimeSync = new();
    private readonly object _scheduledResumeSync = new();

    private readonly Dictionary<string, RoomPolicySnapshot> _roomPolicyByRoomKey = [];
    private readonly Dictionary<string, RoomCommandRuntime> _roomCommandByRoomKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string RoomKey, int Scene), CfgShadowingSceneController> _sceneControllers = [];
    private readonly Dictionary<string, ShutterCommandRuntime> _shutterByTarget = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _scheduledResumeByRoom = new(StringComparer.OrdinalIgnoreCase);
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
        MaterializeCommandRuntime(model);
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

        CancelAllScheduledResumes();

        await base.DisableAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Materializes the room policy snapshots for all rooms in the given model.
    /// This is called on initialization and whenever the model changes in a way that may affect shadowing policy (e.g. configuration changes or dynamic input value changes).
    /// </summary>
    /// <param name="model">The model containing the buildings, floors, and rooms to materialize.</param>
    private void MaterializeRoomPolicy(HomeCompanion.Base.Model.Model model)
    {
        lock (_roomPolicySync)
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
                            _valueReferenceProvider,
                            _logger);
                        var automationLevel = roomConfig.AutomationLevelOverride ?? globalShadowConfig.DefaultAutomationLevel;
                        var persistManualOverride = roomConfig.PersistManualOverride ?? globalShadowConfig.PersistManualOverrides;
                        var manualOverrideDuration = roomConfig.RoomSceneManualOverrideDuration ?? globalShadowConfig.DefaultRoomSceneManualOverrideDuration;

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

    private void MaterializeCommandRuntime(HomeCompanion.Base.Model.Model model)
    {
        var nextRoomByKey = new Dictionary<string, RoomCommandRuntime>(StringComparer.OrdinalIgnoreCase);
        var nextControllers = new Dictionary<(string RoomKey, int Scene), CfgShadowingSceneController>();
        var nextShutterByTarget = new Dictionary<string, ShutterCommandRuntime>(StringComparer.OrdinalIgnoreCase);

        foreach (var building in model.Buildings.Values)
        {
            var shadowing = building.Specials.Values.OfType<ShadowingSpecial>().FirstOrDefault();
            if (shadowing is null)
                continue;

            var globalConfig = shadowing.Configuration;
            var roomBySceneReference = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var floor in building.Floors.Values)
            {
                foreach (var room in floor.Rooms.Values)
                {
                    if (room.Shutters.Count == 0 || room.ShutterScene is null)
                        continue;

                    var roomKey = CreateRoomKey(building.Name, floor.Name, room.Name);
                    var roomConfig = room.Configuration;
                    var shutterRuntimes = new List<ShutterCommandRuntime>();

                    foreach (var shutter in room.Shutters.Values)
                    {
                        var shutterConfig = shutter.Configuration;
                        if (string.IsNullOrWhiteSpace(shutterConfig.FacadeReference))
                            continue;

                        if (!building.Facades.TryGetValue(shutterConfig.FacadeReference, out var facade))
                        {
                            _logger.LogWarning(
                                "Room {RoomKey} shutter {ShutterName} references unknown facade '{FacadeReference}'.",
                                roomKey,
                                shutter.Name,
                                shutterConfig.FacadeReference);
                            continue;
                        }

                        var shutterRuntime = new ShutterCommandRuntime(
                            roomKey,
                            shutter.Name,
                            shutterConfig.Type,
                            facade.OrientationRad,
                            shutterConfig.ShadowingZones,
                            shutterConfig.PositionValueReference,
                            shutterConfig.AngleValueReference,
                            shutterConfig.OpenCloseReference,
                            shutterConfig.DefaultShadowSlat);
                        shutterRuntimes.Add(shutterRuntime);

                        RegisterShutterTarget(nextShutterByTarget, shutterRuntime, shutterConfig.PositionValueReference);
                        RegisterShutterTarget(nextShutterByTarget, shutterRuntime, shutterConfig.AngleValueReference);
                        RegisterShutterTarget(nextShutterByTarget, shutterRuntime, shutterConfig.OpenCloseReference);
                    }

                    nextRoomByKey[roomKey] = new RoomCommandRuntime(
                        roomKey,
                        shadowing,
                        roomConfig.FacadeSunCutoverAngleOverride,
                        [.. roomConfig.FacadeSunCutoverAngleDynamicRules],
                        [.. globalConfig.DynamicFacadeSunCutoverRules],
                        globalConfig.DefaultFacadeSunCutoverAngle,
                        globalConfig.MinSunElevationToConsider,
                        shutterRuntimes);

                    if (!string.IsNullOrWhiteSpace(roomConfig.ShutterSceneReference))
                        roomBySceneReference[roomConfig.ShutterSceneReference] = roomKey;
                }
            }

            foreach (var controller in globalConfig.SpecialScenesAIAttempt.Values)
            {
                var roomKey = ResolveControllerRoomKey(controller, roomBySceneReference);
                if (string.IsNullOrWhiteSpace(roomKey))
                    continue;

                nextControllers[(roomKey, controller.Number)] = controller;
            }
        }

        lock (_commandRuntimeSync)
        {
            _roomCommandByRoomKey.Clear();
            foreach (var kv in nextRoomByKey)
                _roomCommandByRoomKey[kv.Key] = kv.Value;

            _sceneControllers.Clear();
            foreach (var kv in nextControllers)
                _sceneControllers[kv.Key] = kv.Value;

            _shutterByTarget.Clear();
            foreach (var kv in nextShutterByTarget)
                _shutterByTarget[kv.Key] = kv.Value;
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

            await Publisher.PublishAsync(new RoomSceneWriteRequestedEvent
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
            MaterializeCommandRuntime(model);
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

        var now = _timeProvider.GetUtcNow();
        lock (_manualOverrideSync)
        {
            _manualOverrideState = loaded.StateSet ?? new ShutterManualOverrideStateSet();

            var expiredKeys = _manualOverrideState.RoomOverrides
                .Where(kv => kv.Value.ExpiresAtUtc <= now)
                .Select(kv => kv.Key)
                .ToArray();

            foreach (var key in expiredKeys)
                _manualOverrideState.RoomOverrides.Remove(key);
        }

        if (!cancellationToken.IsCancellationRequested)
            await PersistManualOverridesAsync().ConfigureAwait(false);
    }

    internal bool HasActiveManualOverride(string roomKey, DateTimeOffset now)
    {
        var shouldPersist = false;

        lock (_manualOverrideSync)
        {
            if (!_manualOverrideState.RoomOverrides.TryGetValue(roomKey, out var entry))
                return false;

            if (entry.ExpiresAtUtc > now)
                return true;

            _manualOverrideState.RoomOverrides.Remove(roomKey);
            shouldPersist = true;
        }

        if (shouldPersist)
            _ = PersistManualOverridesAsync();

        return false;
    }

    internal async Task ActivateManualOverrideAsync(string roomKey, TimeSpan duration)
    {
        var now = _timeProvider.GetUtcNow();
        lock (_manualOverrideSync)
        {
            _manualOverrideState.RoomOverrides[roomKey] = new ShutterManualOverrideEntry
            {
                CreatedAtUtc = now,
                ExpiresAtUtc = now + duration,
            };
        }

        await PersistManualOverridesAsync().ConfigureAwait(false);
    }

    internal async Task ClearManualOverrideAsync(string roomKey)
    {
        var changed = false;
        lock (_manualOverrideSync)
        {
            changed = _manualOverrideState.RoomOverrides.Remove(roomKey);
        }

        if (changed)
            await PersistManualOverridesAsync().ConfigureAwait(false);
    }

    private async Task PersistManualOverridesAsync()
    {
        ShutterManualOverrideStateSet state;

        lock (_manualOverrideSync)
        {
            var filtered = new Dictionary<string, ShutterManualOverrideEntry>(StringComparer.OrdinalIgnoreCase);
            lock (_roomPolicySync)
            {
                foreach (var kv in _manualOverrideState.RoomOverrides)
                {
                    if (_roomPolicyByRoomKey.TryGetValue(kv.Key, out var room) && room.PersistManualOverride)
                        filtered[kv.Key] = kv.Value;
                }
            }

            state = new ShutterManualOverrideStateSet { RoomOverrides = filtered };
        }

        await _stateStore.SaveAsync(StateSetName, state, timeoutSeconds: 30).ConfigureAwait(false);
    }

    internal bool TryExecuteSceneCommands(string roomKey, int scene, string source, bool applySunExposureGate)
    {
        if (!TryGetRoomCommand(roomKey, out var room))
            return false;

        if (!TryResolveSceneController(roomKey, scene, out var controller))
            return false;

        ExecuteSceneControllerCommands(controller, room, scene, source, applySunExposureGate);
        return true;
    }

    internal bool HasAnySunExposedShutter(string roomKey)
    {
        if (!TryGetRoomCommand(roomKey, out var room))
            return false;

        return HasAnySunExposedShutter(room);
    }

    internal void CancelScheduledResume(string roomKey)
    {
        CancellationTokenSource? toCancel = null;
        lock (_scheduledResumeSync)
        {
            if (_scheduledResumeByRoom.TryGetValue(roomKey, out var existing))
            {
                toCancel = existing;
                _scheduledResumeByRoom.Remove(roomKey);
            }
        }

        if (toCancel is null)
            return;

        toCancel.Cancel();
        toCancel.Dispose();
    }

    internal void CancelAllScheduledResumes()
    {
        List<CancellationTokenSource> all;
        lock (_scheduledResumeSync)
        {
            all = [.. _scheduledResumeByRoom.Values];
            _scheduledResumeByRoom.Clear();
        }

        foreach (var cancellation in all)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    internal void ScheduleResumeAutomation(string roomKey, IValue sceneValue, int resumeScene, TimeSpan delay, string reason)
    {
        var cancellation = RegisterScheduledResume(roomKey);
        _ = RunScheduledResumeAsync(roomKey, sceneValue, resumeScene, delay, reason, cancellation.Token);
    }

    private CancellationTokenSource RegisterScheduledResume(string roomKey)
    {
        CancellationTokenSource? toCancel = null;
        var replacement = new CancellationTokenSource();

        lock (_scheduledResumeSync)
        {
            if (_scheduledResumeByRoom.TryGetValue(roomKey, out var existing))
                toCancel = existing;

            _scheduledResumeByRoom[roomKey] = replacement;
        }

        if (toCancel is not null)
        {
            toCancel.Cancel();
            toCancel.Dispose();
        }

        return replacement;
    }

    private async Task RunScheduledResumeAsync(
        string roomKey,
        IValue sceneValue,
        int resumeScene,
        TimeSpan delay,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            if (!HasAnySunExposedShutter(roomKey))
            {
                _logger.LogInformation(
                    "Room {RoomKey}: skipped auto-resume scene {Scene} ({Reason}) because no shutter is sun-exposed.",
                    roomKey,
                    resumeScene,
                    reason);
                return;
            }

            if (!sceneValue.TryWriteNumeric(resumeScene, this, _logger))
            {
                _logger.LogWarning(
                    "Room {RoomKey}: failed to write auto-resume scene {Scene} ({Reason}) to '{SceneName}'.",
                    roomKey,
                    resumeScene,
                    reason,
                    sceneValue.Name);
            }
            else
            {
                await ClearManualOverrideAsync(roomKey).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when scene changes or shutdown cancels pending resumes.
        }
        finally
        {
            lock (_scheduledResumeSync)
            {
                if (_scheduledResumeByRoom.TryGetValue(roomKey, out var current) && current.Token == cancellationToken)
                    _scheduledResumeByRoom.Remove(roomKey);
            }
        }
    }

    private bool TryGetRoomCommand(string roomKey, out RoomCommandRuntime room)
    {
        lock (_commandRuntimeSync)
        {
            return _roomCommandByRoomKey.TryGetValue(roomKey, out room!);
        }
    }

    private bool TryResolveSceneController(string roomKey, int scene, out CfgShadowingSceneController controller)
    {
        lock (_commandRuntimeSync)
        {
            return _sceneControllers.TryGetValue((roomKey, scene), out controller!);
        }
    }

    private bool HasAnySunExposedShutter(RoomCommandRuntime room)
    {
        if (room.Shutters.Count == 0)
            return false;

        if (!room.Shadowing.SunPositionAzimuth.TryGetValue<double>(out var sunAzimuthDeg, _logger) ||
            !room.Shadowing.SunPositionElevation.TryGetValue<double>(out var sunElevationDeg, _logger))
        {
            return false;
        }

        if (sunElevationDeg < room.MinSunElevationToConsider)
            return false;

        var cutover = ResolveEffectiveCutoverAngle(room);
        var maxIncidence = 90.0 - ClampCutoverAngle(cutover);

        foreach (var shutter in room.Shutters)
        {
            if (IsShutterSunExposed(shutter, sunAzimuthDeg, sunElevationDeg, maxIncidence))
                return true;
        }

        return false;
    }

    private void ExecuteSceneControllerCommands(
        CfgShadowingSceneController controller,
        RoomCommandRuntime room,
        int scene,
        string source,
        bool applySunExposureGate)
    {
        foreach (var command in controller.Commands.Values)
        {
            if (string.IsNullOrWhiteSpace(command.TargetValueReference))
                continue;

            if (TryResolveShutter(room.RoomKey, command.TargetValueReference, out var shutter))
            {
                ExecuteShutterCommand(shutter, command, room, scene, source, applySunExposureGate);
                continue;
            }

            if (applySunExposureGate && IsSunExposureBlocked(room, command.TargetValueReference))
            {
                _logger.LogInformation(
                    "Room {RoomKey}: scene {Scene} ({Source}) skipped target '{TargetValueReference}' because facade is not sun-exposed.",
                    room.RoomKey,
                    scene,
                    source,
                    command.TargetValueReference);
                continue;
            }

            if (!_valueReferenceProvider.TryResolve(command.TargetValueReference, out var targetValue) || targetValue is null)
            {
                _logger.LogWarning(
                    "Room {RoomKey}: scene {Scene} ({Source}) command target '{TargetValueReference}' could not be resolved.",
                    room.RoomKey,
                    scene,
                    source,
                    command.TargetValueReference);
                continue;
            }

            if (!targetValue.TryWriteNumeric(command.Value, this, _logger))
            {
                _logger.LogWarning(
                    "Room {RoomKey}: scene {Scene} ({Source}) could not write numeric value {Value} to '{TargetName}' ({TargetType}).",
                    room.RoomKey,
                    scene,
                    source,
                    command.Value,
                    targetValue.Name,
                    targetValue.ValueType.Name);
                continue;
            }

            _logger.LogInformation(
                "Room {RoomKey}: scene {Scene} ({Source}) wrote {Value} to '{TargetName}'.",
                room.RoomKey,
                scene,
                source,
                command.Value,
                targetValue.Name);
        }
    }

    private bool TryResolveShutter(string roomKey, string targetValueReference, out ShutterCommandRuntime shutter)
    {
        var key = BuildRoomTargetKey(roomKey, targetValueReference);

        lock (_commandRuntimeSync)
        {
            return _shutterByTarget.TryGetValue(key, out shutter!);
        }
    }

    private void ExecuteShutterCommand(
        ShutterCommandRuntime shutter,
        CfgShadowingSceneCommand command,
        RoomCommandRuntime room,
        int scene,
        string source,
        bool applySunExposureGate)
    {
        if (applySunExposureGate && IsShutterSunExposureBlocked(room, shutter))
        {
            _logger.LogInformation(
                "Room {RoomKey}: scene {Scene} ({Source}) skipped shutter '{ShutterName}' because its facade is not sun-exposed.",
                room.RoomKey,
                scene,
                source,
                shutter.Name);
            return;
        }

        switch (shutter.Type)
        {
            case ShutterType.OpenClose:
                ExecuteOpenCloseShutterCommand(shutter, command, room, scene, source);
                return;

            case ShutterType.VenetianBlind:
                ExecuteVenetianBlindCommand(shutter, command, room, scene, source);
                return;

            default:
                ExecutePositionalShutterCommand(shutter, command, room, scene, source);
                return;
        }
    }

    private void ExecutePositionalShutterCommand(
        ShutterCommandRuntime shutter,
        CfgShadowingSceneCommand command,
        RoomCommandRuntime room,
        int scene,
        string source)
    {
        if (string.IsNullOrWhiteSpace(shutter.PositionValueReference))
        {
            _logger.LogWarning(
                "Room {RoomKey}: scene {Scene} ({Source}) shutter '{ShutterName}' has no position target configured.",
                room.RoomKey,
                scene,
                source,
                shutter.Name);
            return;
        }

        WriteResolvedTarget(shutter.PositionValueReference, command.Value, room, scene, source, shutter.Name, "position");
    }

    private void ExecuteVenetianBlindCommand(
        ShutterCommandRuntime shutter,
        CfgShadowingSceneCommand command,
        RoomCommandRuntime room,
        int scene,
        string source)
    {
        if (!string.IsNullOrWhiteSpace(shutter.PositionValueReference))
            WriteResolvedTarget(shutter.PositionValueReference, command.Value, room, scene, source, shutter.Name, "position");

        if (string.IsNullOrWhiteSpace(shutter.AngleValueReference))
        {
            _logger.LogWarning(
                "Room {RoomKey}: scene {Scene} ({Source}) venetian shutter '{ShutterName}' has no angle target configured.",
                room.RoomKey,
                scene,
                source,
                shutter.Name);
            return;
        }

        var angleValue = command.Value > 0
            ? shutter.DefaultShadowSlat
            : 0;
        WriteResolvedTarget(shutter.AngleValueReference, angleValue, room, scene, source, shutter.Name, "angle");
    }

    private void ExecuteOpenCloseShutterCommand(
        ShutterCommandRuntime shutter,
        CfgShadowingSceneCommand command,
        RoomCommandRuntime room,
        int scene,
        string source)
    {
        if (string.IsNullOrWhiteSpace(shutter.OpenCloseReference))
        {
            _logger.LogWarning(
                "Room {RoomKey}: scene {Scene} ({Source}) open/close shutter '{ShutterName}' has no open/close target configured.",
                room.RoomKey,
                scene,
                source,
                shutter.Name);
            return;
        }

        var closed = Math.Abs(command.Value) >= 0.5;
        WriteResolvedTarget(shutter.OpenCloseReference, closed ? 1 : 0, room, scene, source, shutter.Name, "open-close");
    }

    private void WriteResolvedTarget(
        string targetValueReference,
        double value,
        RoomCommandRuntime room,
        int scene,
        string source,
        string shutterName,
        string aspect)
    {
        if (!_valueReferenceProvider.TryResolve(targetValueReference, out var targetValue) || targetValue is null)
        {
            _logger.LogWarning(
                "Room {RoomKey}: scene {Scene} ({Source}) shutter '{ShutterName}' {Aspect} target '{TargetValueReference}' could not be resolved.",
                room.RoomKey,
                scene,
                source,
                shutterName,
                aspect,
                targetValueReference);
            return;
        }

        if ( !targetValue.TryWriteNumeric(value, this, _logger) )
        {
            _logger.LogWarning(
                "Room {RoomKey}: scene {Scene} ({Source}) could not write {Aspect} value {Value} to shutter '{ShutterName}' target '{TargetName}' ({TargetType}).",
                room.RoomKey,
                scene,
                source,
                aspect,
                value,
                shutterName,
                targetValue.Name,
                targetValue.ValueType.Name);
            return;
        }

        _logger.LogInformation(
            "Room {RoomKey}: scene {Scene} ({Source}) wrote {Aspect} value {Value} to shutter '{ShutterName}' target '{TargetName}'.",
            room.RoomKey,
            scene,
            source,
            aspect,
            value,
            shutterName,
            targetValue.Name);
    }

    private bool IsSunExposureBlocked(RoomCommandRuntime room, string targetValueReference)
    {
        if (!TryResolveShutter(room.RoomKey, targetValueReference, out var shutter))
            return false;

        return IsShutterSunExposureBlocked(room, shutter);
    }

    private bool IsShutterSunExposureBlocked(RoomCommandRuntime room, ShutterCommandRuntime shutter)
    {
        if ( !room.Shadowing.SunPositionAzimuth.TryGetValue<double>(out var sunAzimuthDeg, _logger) ||
             !room.Shadowing.SunPositionElevation.TryGetValue<double>(out var sunElevationDeg, _logger) )
        {
            _logger.LogWarning(
                "Room {RoomKey}: sun position values are missing or invalid; skipping automation command for shutter '{ShutterName}'.",
                room.RoomKey,
                shutter.Name);
            return true;
        }

        if (sunElevationDeg < room.MinSunElevationToConsider)
            return true;

        var cutover = ResolveEffectiveCutoverAngle(room);
        var maxIncidence = 90.0 - ClampCutoverAngle(cutover);

        return !IsShutterSunExposed(shutter, sunAzimuthDeg, sunElevationDeg, maxIncidence);
    }

    private static bool IsShutterSunExposed(
        ShutterCommandRuntime shutter,
        double sunAzimuthDeg,
        double sunElevationDeg,
        double maxIncidence)
    {
        if (IsBlockedByShadowingZones(shutter.ShadowingZones, sunAzimuthDeg, sunElevationDeg))
            return false;

        var incidence = ComputeIncidenceAngleDeg(shutter.FacadeOrientationDeg, sunAzimuthDeg, sunElevationDeg);
        return incidence <= maxIncidence;
    }

    private double ResolveEffectiveCutoverAngle(RoomCommandRuntime room)
    {
        var baseline = room.RoomCutoverAngleOverride ?? room.GlobalCutoverAngle;
        var rules = room.RoomDynamicCutoverRules.Count > 0
            ? room.RoomDynamicCutoverRules
            : room.GlobalDynamicCutoverRules;

        if (rules.Count == 0)
            return baseline;

        var thermalMode = ShutterPolicyResolver.ResolveThermalControlMode(room.Shadowing, _logger);
        var hasOutdoorTemperature = room.Shadowing.OutdoorTemperature.TryGetValue<double>(out var outdoorTemperature, _logger);

        foreach (var rule in rules)
        {
            if (rule.ThermalControlMode.HasValue && rule.ThermalControlMode.Value != thermalMode)
                continue;

            if (rule.OutdoorTemperatureMin.HasValue)
            {
                if (!hasOutdoorTemperature || outdoorTemperature < rule.OutdoorTemperatureMin.Value)
                    continue;
            }

            if (rule.OutdoorTemperatureMax.HasValue)
            {
                if (!hasOutdoorTemperature || outdoorTemperature > rule.OutdoorTemperatureMax.Value)
                    continue;
            }

            return rule.CutoverAngle;
        }

        return baseline;
    }

    private static void RegisterShutterTarget(
        Dictionary<string, ShutterCommandRuntime> bindings,
        ShutterCommandRuntime shutter,
        string? targetReference)
    {
        if (string.IsNullOrWhiteSpace(targetReference))
            return;

        bindings[BuildRoomTargetKey(shutter.RoomKey, targetReference)] = shutter;
    }

    private string? ResolveControllerRoomKey(
        CfgShadowingSceneController controller,
        Dictionary<string, string> roomBySceneReference)
    {
        if (!string.IsNullOrWhiteSpace(controller.RoomReference))
            return NormalizeRoomKey(controller.RoomReference);

        if (string.IsNullOrWhiteSpace(controller.SceneReference))
        {
            _logger.LogWarning("Shadowing scene controller Number={Number} has neither RoomReference nor SceneReference. Ignoring.", controller.Number);
            return null;
        }

        if (roomBySceneReference.TryGetValue(controller.SceneReference, out var roomKey))
            return roomKey;

        _logger.LogWarning(
            "Shadowing scene controller Number={Number} references scene value '{SceneReference}', but no room scene reference matches it.",
            controller.Number,
            controller.SceneReference);
        return null;
    }

    private static string NormalizeRoomKey(string roomReference)
        => roomReference.Trim().Replace('\\', '/');

    private static bool IsBlockedByShadowingZones(Dictionary<string, CfgShadowingZone> zones, double sunAzimuthDeg, double sunElevationDeg)
    {
        foreach (var zone in zones.Values)
        {
            if (zone.Mode == ShadowingZoneMode.Default)
                continue;

            var inside = IsSunInsideZone(zone, sunAzimuthDeg, sunElevationDeg);
            if (zone.Mode == ShadowingZoneMode.Inside && !inside)
                return true;

            if (zone.Mode == ShadowingZoneMode.Outside && inside)
                return true;
        }

        return false;
    }

    private static bool IsSunInsideZone(CfgShadowingZone zone, double sunAzimuthDeg, double sunElevationDeg)
    {
        if (zone.AzimuthMin.HasValue && sunAzimuthDeg < zone.AzimuthMin.Value)
            return false;
        if (zone.AzimuthMax.HasValue && sunAzimuthDeg > zone.AzimuthMax.Value)
            return false;
        if (zone.ElevationMin.HasValue && sunElevationDeg < zone.ElevationMin.Value)
            return false;
        if (zone.ElevationMax.HasValue && sunElevationDeg > zone.ElevationMax.Value)
            return false;

        return true;
    }

    private static double ComputeIncidenceAngleDeg(SphericVector facadeOrientationDeg, double sunAzimuthDeg, double sunElevationDeg)
    {
        var (facadeAzimuthDeg, facadeElevationDeg) = facadeOrientationDeg.ToDegreesPair();
        var facade = ToCartesianUnit(facadeAzimuthDeg, facadeElevationDeg);
        var sun = ToCartesianUnit(sunAzimuthDeg, sunElevationDeg);

        var dot = (facade.X * sun.X) + (facade.Y * sun.Y) + (facade.Z * sun.Z);
        dot = Math.Clamp(dot, -1.0, 1.0);

        return Math.Acos(dot) * 180.0 / Math.PI;
    }

    private static (double X, double Y, double Z) ToCartesianUnit(double azimuthDeg, double elevationDeg)
    {
        var azimuthRad = azimuthDeg * Math.PI / 180.0;
        var elevationRad = elevationDeg * Math.PI / 180.0;
        var cosElevation = Math.Cos(elevationRad);

        return (
            X: cosElevation * Math.Sin(azimuthRad),
            Y: cosElevation * Math.Cos(azimuthRad),
            Z: Math.Sin(elevationRad));
    }

    private static double ClampCutoverAngle(double cutoverDeg)
        => Math.Clamp(cutoverDeg, 0.0, 90.0);

    [Obsolete("Use IValue.TryGetNumericValue extension method instead.", error: true)]
    private static bool TryGetNumericValue(IValue? value, out double numeric)
    {
        if (value is IValue<double> dblValue)
        {
            numeric = dblValue.Value;
            return true;
        }

        numeric = 0;

        if (value?.OValue is null)
            return false;

        try
        {
            numeric = Convert.ToDouble(value.OValue, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildRoomTargetKey(string roomKey, string targetValueReference)
        => string.Concat(roomKey, "|", NormalizeValueReference(targetValueReference));

    private static string NormalizeValueReference(string reference)
        => reference.Trim();

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

internal sealed record RoomCommandRuntime(
    string RoomKey,
    ShadowingSpecial Shadowing,
    double? RoomCutoverAngleOverride,
    List<CfgDynamicCutoverAngleRule> RoomDynamicCutoverRules,
    List<CfgDynamicCutoverAngleRule> GlobalDynamicCutoverRules,
    double GlobalCutoverAngle,
    double MinSunElevationToConsider,
    List<ShutterCommandRuntime> Shutters);

internal sealed record ShutterCommandRuntime(
    string RoomKey,
    string Name,
    ShutterType Type,
    SphericVector FacadeOrientationDeg,
    Dictionary<string, CfgShadowingZone> ShadowingZones,
    string? PositionValueReference,
    string? AngleValueReference,
    string? OpenCloseReference,
    double DefaultShadowSlat);

/// <summary>
/// Persisted manual override state for <see cref="ShutterControl"/>.
/// </summary>
public class ShutterManualOverrideStateSet
{
    /// <summary>
    /// Room-scoped overrides keyed as <c>Building/Floor/Room</c> as created by <see cref="ShutterControl.CreateRoomKey"/>.
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
