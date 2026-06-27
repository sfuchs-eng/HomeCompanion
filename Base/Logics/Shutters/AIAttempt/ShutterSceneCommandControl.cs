using HomeCompanion.Base.Model;
using HomeCompanion.Base.Utilities;
using HomeCompanion.Events;
using HomeCompanion.Persistence;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace HomeCompanion.Base.Logics.Shutters.AIAttempt;

/// <summary>
/// Executes room-scoped shutter scene commands and manages manual-override state based on room scene writes.
/// </summary>
/// <remarks>
/// Scene semantics:
/// <list type="bullet">
/// <item><description>1,2,3: manual override active; actuator behavior is implemented at KNX actor level.</description></item>
/// <item><description>50,52: clear manual override and resume automation.</description></item>
/// <item><description>Other scenes: treated as manual-override scenes when a matching <see cref="CfgShadowingSceneController"/> exists for the room and scene number.</description></item>
/// </list>
/// While a room is in scene 50/52 and has no active manual override, schedule-driven automation transitions
/// are executed by resolving room-scoped scene controllers and writing their target commands.
/// </remarks>
public sealed class ShutterSceneCommandControl(
    IModelProvider modelProvider,
    IValueProvider valueReferenceProvider,
    IStateStore stateStore,
    TimeProvider timeProvider,
    ILogger<ShutterSceneCommandControl> logger,
    IEventPublisher publisher,
    IEventSubscriber subscriber)
    : LogicBase(publisher, subscriber)
{
    private const string StateSetName = "ShutterControlManualOverrides";
    private static readonly HashSet<int> ManualActorScenes = [.. Enumerable.Range(1, 63).Where(i => i != 50 && i != 51 && i != 52)];
    private static readonly HashSet<int> DefaultResumeAutomationScenes = [50, 51, 52];

    private readonly IModelProvider _modelProvider = modelProvider;
    private readonly IValueProvider _valueReferenceProvider = valueReferenceProvider;
    private readonly IStateStore _stateStore = stateStore;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<ShutterSceneCommandControl> _logger = logger;

    private readonly object _syncLock = new();
    private readonly Dictionary<IValue, RoomRuntime> _roomBySceneValue = [];
    private readonly Dictionary<string, RoomRuntime> _roomByRoomKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string RoomKey, int Scene), CfgShadowingSceneController> _sceneControllers = [];
    private readonly Dictionary<string, ShutterRuntime> _shutterByTarget = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<IValue> _subscribedSceneValues = [];
    private readonly Dictionary<string, CancellationTokenSource> _scheduledResumeByRoom = new(StringComparer.OrdinalIgnoreCase);

    private ShutterManualOverrideStateSet _manualOverrideState = new();

    protected override async Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
    {
        Subscribe(new RoomSceneWriteRequestedEventHandler(this));

        if (!_modelProvider.IsInitialized)
        {
            _logger.LogError("Model provider is not initialized yet. ShutterSceneCommandControl skipped initialization.");
            return;
        }

        var model = _modelProvider.GetModel();

        CheckConfiguration(model);

        MaterializeRuntime(model);
        await RestoreManualOverridesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "ShutterSceneCommandControl initialized with {RoomCount} rooms and {ControllerCount} scene controllers.",
            _roomByRoomKey.Count,
            _sceneControllers.Count);
    }

    /// <summary>
    /// Check the aspects in the model that would result in errors or warning when <see cref="MaterializeRuntime"/> is executed, and log them as warnings or errors.
    /// This allows to detect and fix model issues before they manifest as incorrect or missing shutter commands or failed scene changes at runtime.
    /// </summary>
    /// <param name="model"></param>
    private void CheckConfiguration(Model.Model model)
    {
        foreach (var building in model.Buildings.Values)
        {
            var shadowing = building.Specials.Values.OfType<ShadowingSpecial>().FirstOrDefault();
            if (shadowing is null)
            {
                _logger.LogWarning(
                    "Building '{BuildingName}' has no shadowing special configured. All rooms in this building will be ignored by {LogicName}.",
                    building.Name,
                    nameof(ShutterSceneCommandControl));
                continue;
            }

            foreach (var floor in building.Floors.Values)
            {
                foreach (var room in floor.Rooms.Values)
                {
                    if (room.Shutters.Count == 0 || room.ShutterScene is null)
                        continue;

                    var roomKey = ShutterControl.CreateRoomKey(building.Name, floor.Name, room.Name);

                    foreach (var shutter in room.Shutters.Values)
                    {
                        if (string.IsNullOrWhiteSpace(shutter.Configuration.FacadeReference))
                        {
                            _logger.LogWarning(
                                "Room {RoomKey} shutter {ShutterName} has no facade reference configured.",
                                roomKey,
                                shutter.Name);
                            continue;
                        }

                        if (!building.Facades.TryGetValue(shutter.Configuration.FacadeReference, out var facade))
                        {
                            _logger.LogWarning(
                                "Room {RoomKey} shutter {ShutterName} references unknown facade '{FacadeReference}'.",
                                roomKey,
                                shutter.Name,
                                shutter.Configuration.FacadeReference);
                            continue;
                        }
                    }
                }
            }
        }
    }

    public override Task DisableAsync(CancellationToken cancellationToken = default)
    {
        CancelAllScheduledResumes();

        lock (_syncLock)
        {
            foreach (var sceneValue in _subscribedSceneValues)
                sceneValue.Changed -= OnRoomSceneChanged;
            _subscribedSceneValues.Clear();
        }

        return base.DisableAsync(cancellationToken);
    }

    private void MaterializeRuntime(Model.Model model)
    {
        var nextRoomByScene = new Dictionary<IValue, RoomRuntime>();
        var nextRoomByKey = new Dictionary<string, RoomRuntime>(StringComparer.OrdinalIgnoreCase);
        var nextControllers = new Dictionary<(string RoomKey, int Scene), CfgShadowingSceneController>();
        var nextShutterByTarget = new Dictionary<string, ShutterRuntime>(StringComparer.OrdinalIgnoreCase);

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

                    var roomKey = ShutterControl.CreateRoomKey(building.Name, floor.Name, room.Name);
                    var roomConfig = room.Configuration;
                    var manualOverrideDuration = roomConfig.ManualOverrideDuration ?? globalConfig.DefaultManualOverrideDuration;
                    var persistManualOverride = roomConfig.PersistManualOverride ?? globalConfig.PersistManualOverrides;
                    var resumeAutomationScenes = ResolveResumeAutomationScenes(globalConfig);
                    var defaultResumeAutomationScene = ResolveDefaultResumeAutomationScene(globalConfig, resumeAutomationScenes);
                    var shutterRuntimes = new List<ShutterRuntime>();

                    foreach (var shutter in room.Shutters.Values)
                    {
                        var shutterConfig = shutter.Configuration;
                        if (string.IsNullOrWhiteSpace(shutterConfig.FacadeReference))
                        {
                            _logger.LogWarning(
                                "Room {RoomKey} shutter {ShutterName} has no facade reference configured.",
                                roomKey,
                                shutter.Name);
                            continue;
                        }

                        if (!building.Facades.TryGetValue(shutterConfig.FacadeReference, out var facade))
                        {
                            _logger.LogWarning(
                                "Room {RoomKey} shutter {ShutterName} references unknown facade '{FacadeReference}'.",
                                roomKey,
                                shutter.Name,
                                shutterConfig.FacadeReference);
                            continue;
                        }

                        var shutterRuntime = new ShutterRuntime(
                            roomKey,
                            shutter.Name,
                            shutter,
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

                    var roomRuntime = new RoomRuntime(
                        roomKey,
                        room,
                        room.ShutterScene,
                        manualOverrideDuration,
                        persistManualOverride,
                        resumeAutomationScenes,
                        defaultResumeAutomationScene,
                        shadowing,
                        roomConfig.FacadeSunCutoverAngleOverride,
                        [.. roomConfig.FacadeSunCutoverAngleDynamicRules],
                        [.. globalConfig.DynamicFacadeSunCutoverRules],
                        globalConfig.DefaultFacadeSunCutoverAngle,
                        globalConfig.MinSunElevationToConsider,
                        shutterRuntimes);
                    nextRoomByScene[room.ShutterScene] = roomRuntime;
                    nextRoomByKey[roomKey] = roomRuntime;

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

        lock (_syncLock)
        {
            // Unsubscribe from scene value changes that are no longer relevant.
            foreach (var old in _subscribedSceneValues.Except(nextRoomByScene.Keys).ToArray())
            {
                old.Changed -= OnRoomSceneChanged;
                _subscribedSceneValues.Remove(old);
            }

            // Subscribe to new scene values that are now relevant.
            foreach (var sceneValue in nextRoomByScene.Keys.Except(_subscribedSceneValues))
            {
                sceneValue.Changed += OnRoomSceneChanged;
                _subscribedSceneValues.Add(sceneValue);
            }

            _roomBySceneValue.Clear();
            foreach (var kv in nextRoomByScene)
                _roomBySceneValue[kv.Key] = kv.Value;

            _roomByRoomKey.Clear();
            foreach (var kv in nextRoomByKey)
                _roomByRoomKey[kv.Key] = kv.Value;

            _sceneControllers.Clear();
            foreach (var kv in nextControllers)
                _sceneControllers[kv.Key] = kv.Value;

            _shutterByTarget.Clear();
            foreach (var kv in nextShutterByTarget)
                _shutterByTarget[kv.Key] = kv.Value;
        }
    }

    /// <summary>
    /// Registers a shutter target in the specified bindings dictionary.
     /// The target reference is normalized by trimming and replacing backslashes with slashes to allow flexible configuration formats.
     /// The key is built as <c>{RoomKey}:{NormalizedTargetReference}</c> to allow quick lookup of shutters by room and target reference during command execution.
     /// If the target reference is null, empty or whitespace, no registration is performed.
    /// </summary>
    /// <param name="bindings"></param>
    /// <param name="shutter"></param>
    /// <param name="targetReference">The target reference for the shutter. This value is normalized by trimming and replacing backslashes with slashes.</param>
    private static void RegisterShutterTarget(
        Dictionary<string, ShutterRuntime> bindings,
        ShutterRuntime shutter,
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

    private void OnRoomSceneChanged(object? sender, ValueChangedEventArgs e)
    {
        if (sender is not IValue sceneValue)
            return;

        if (ReferenceEquals(e.Initiator, this))
            return;

        RoomRuntime? room;
        lock (_syncLock)
        {
            _roomBySceneValue.TryGetValue(sceneValue, out room);
        }

        if (room is null)
            return;

        if (!TryGetSceneNumber(sceneValue, out var scene))
            return;

        _ = HandleRoomSceneChangedAsync(room, scene);
    }

    private async Task HandleRoomSceneChangedAsync(RoomRuntime room, int scene)
    {
        try
        {
            CancelScheduledResume(room.RoomKey);

            if (room.ResumeAutomationScenes.Contains(scene))
            {
                await ClearManualOverrideAsync(room.RoomKey).ConfigureAwait(false);
                _logger.LogInformation("Room {RoomKey}: scene {Scene} -> resume automation.", room.RoomKey, scene);
                return;
            }

            if (ManualActorScenes.Contains(scene))
            {
                await ActivateManualOverrideAsync(room.RoomKey, room.ManualOverrideDuration).ConfigureAwait(false);
                _logger.LogInformation("Room {RoomKey}: actor-manual scene {Scene} -> manual override active.", room.RoomKey, scene);
                return;
            }

            if (TryResolveSceneController(room.RoomKey, scene, out var controller))
            {
                await ActivateManualOverrideAsync(room.RoomKey, room.ManualOverrideDuration).ConfigureAwait(false);
                ExecuteSceneControllerCommands(controller, room, scene, "manual-scene", applySunExposureGate: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed handling room scene change for room {RoomKey}, scene {Scene}.", room.RoomKey, scene);
        }
    }

    private ValueTask HandleRoomScheduleTransitionDueAsync(RoomSceneWriteRequestedEvent due, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (!TryGetRoom(due.RoomKey, out var room))
            return ValueTask.CompletedTask;

        if (!TryGetSceneNumber(room.SceneValue, out var currentScene) || !room.ResumeAutomationScenes.Contains(currentScene))
            return ValueTask.CompletedTask;

        var now = _timeProvider.GetUtcNow();
        if (HasActiveManualOverride(room.RoomKey, now))
            return ValueTask.CompletedTask;

        if (!TryWriteNumeric(room.SceneValue, due.Scene))
        {
            _logger.LogWarning(
                "Room {RoomKey}: schedule {ScheduleKey} could not write scene {Scene} to room scene value '{SceneName}'.",
                room.RoomKey,
                due.ScheduleKey,
                due.Scene,
                room.SceneValue.Name);
            return ValueTask.CompletedTask;
        }

        _ = ActivateManualOverrideAsync(room.RoomKey, room.ManualOverrideDuration);

        if (TryResolveSceneController(room.RoomKey, due.Scene, out var controller))
            ExecuteSceneControllerCommands(controller, room, due.Scene, "automation-schedule", applySunExposureGate: true);

        ScheduleResumeAutomation(room, due);

        return ValueTask.CompletedTask;
    }

    private void ScheduleResumeAutomation(RoomRuntime room, RoomSceneWriteRequestedEvent due)
    {
        var plan = ResolveResumePlan(room, due);
        if (plan is null)
            return;

        var cancellation = RegisterScheduledResume(room.RoomKey);
        _ = RunScheduledResumeAsync(room.RoomKey, plan.Value, cancellation.Token);
    }

    private ResumePlan? ResolveResumePlan(RoomRuntime room, RoomSceneWriteRequestedEvent due)
    {
        var resumeScene = ResolveResumeScene(room, due.ResumeAutomationScene);

        if (due.ResumeAutomationAfter.HasValue && due.ResumeAutomationAfter.Value > TimeSpan.Zero)
            return new ResumePlan(resumeScene, due.ResumeAutomationAfter.Value, "delay");

        if (TryResolveResumeAtLocalDelay(due, out var localDelay))
            return new ResumePlan(resumeScene, localDelay, "local-time");

        return null;
    }

    private static bool TryResolveResumeAtLocalDelay(RoomSceneWriteRequestedEvent due, out TimeSpan delay)
    {
        delay = TimeSpan.Zero;

        if (!due.ResumeAutomationAtLocalTime.HasValue)
            return false;

        var timeOfDay = due.ResumeAutomationAtLocalTime.Value;
        if (timeOfDay < TimeSpan.Zero || timeOfDay >= TimeSpan.FromDays(1))
            return false;

        var targetLocal = due.TriggerLocalTime.Date + timeOfDay;
        if (targetLocal <= due.TriggerLocalTime)
            targetLocal = targetLocal.AddDays(1);

        delay = targetLocal - due.TriggerLocalTime;
        return delay > TimeSpan.Zero;
    }

    private static int ResolveResumeScene(RoomRuntime room, int? overrideScene)
    {
        if (overrideScene.HasValue && overrideScene.Value >= 0)
            return overrideScene.Value;

        return room.DefaultResumeAutomationScene;
    }

    private CancellationTokenSource RegisterScheduledResume(string roomKey)
    {
        CancellationTokenSource? toCancel = null;
        var replacement = new CancellationTokenSource();

        lock (_syncLock)
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

    private void CancelScheduledResume(string roomKey)
    {
        CancellationTokenSource? toCancel = null;
        lock (_syncLock)
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

    private void CancelAllScheduledResumes()
    {
        List<CancellationTokenSource> all;
        lock (_syncLock)
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

    private async Task RunScheduledResumeAsync(string roomKey, ResumePlan plan, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(plan.Delay, cancellationToken).ConfigureAwait(false);

            if (!TryGetRoom(roomKey, out var room))
                return;

            if (!HasAnySunExposedShutter(room))
            {
                _logger.LogInformation(
                    "Room {RoomKey}: skipped auto-resume scene {Scene} ({Reason}) because no shutter is sun-exposed.",
                    roomKey,
                    plan.ResumeScene,
                    plan.Reason);
                return;
            }

            if (!TryWriteNumeric(room.SceneValue, plan.ResumeScene))
            {
                _logger.LogWarning(
                    "Room {RoomKey}: failed to write auto-resume scene {Scene} ({Reason}) to '{SceneName}'.",
                    roomKey,
                    plan.ResumeScene,
                    plan.Reason,
                    room.SceneValue.Name);
            }
            else
            {
                await ClearManualOverrideAsync(roomKey).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on scene changes or shutdown.
        }
        finally
        {
            lock (_syncLock)
            {
                if (_scheduledResumeByRoom.TryGetValue(roomKey, out var current) && current.Token == cancellationToken)
                    _scheduledResumeByRoom.Remove(roomKey);
            }
        }
    }

    private bool HasAnySunExposedShutter(RoomRuntime room)
    {
        if (room.Shutters.Count == 0)
            return false;

        if (!TryGetNumericValue(room.Shadowing.SunPositionAzimuth, out var sunAzimuthDeg) ||
            !TryGetNumericValue(room.Shadowing.SunPositionElevation, out var sunElevationDeg))
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

    private bool TryResolveSceneController(string roomKey, int scene, out CfgShadowingSceneController controller)
    {
        lock (_syncLock)
        {
            return _sceneControllers.TryGetValue((roomKey, scene), out controller!);
        }
    }

    private bool TryGetRoom(string roomKey, out RoomRuntime room)
    {
        lock (_syncLock)
        {
            return _roomByRoomKey.TryGetValue(roomKey, out room!);
        }
    }

    private void ExecuteSceneControllerCommands(
        CfgShadowingSceneController controller,
        RoomRuntime room,
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

            if (!TryWriteNumeric(targetValue, command.Value))
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

    private bool TryResolveShutter(string roomKey, string targetValueReference, out ShutterRuntime shutter)
    {
        var key = BuildRoomTargetKey(roomKey, targetValueReference);

        lock (_syncLock)
        {
            return _shutterByTarget.TryGetValue(key, out shutter!);
        }
    }

    private void ExecuteShutterCommand(
        ShutterRuntime shutter,
        CfgShadowingSceneCommand command,
        RoomRuntime room,
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
        ShutterRuntime shutter,
        CfgShadowingSceneCommand command,
        RoomRuntime room,
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
        ShutterRuntime shutter,
        CfgShadowingSceneCommand command,
        RoomRuntime room,
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
        ShutterRuntime shutter,
        CfgShadowingSceneCommand command,
        RoomRuntime room,
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
        RoomRuntime room,
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

        if (!TryWriteNumeric(targetValue, value))
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

    /// <summary>
    /// Determines whether the sun exposure is blocked for a given room and target value reference.
    /// Blocked meaning that the target is not currently exposed to the sun, either because the sun is below the elevation threshold or because the facade orientation relative to the sun position is such that the sun's rays would be blocked by the building itself.
    /// It might also be blocked due to configured shadowing zones, e.g. due to neighboring buildings or trees, even if the sun is above the elevation threshold and facing the facade.
    /// </summary>
    /// <param name="room">The room runtime information.</param>
    /// <param name="targetValueReference">The target value reference.</param>
    /// <returns>True if sun exposure is blocked; otherwise, false.</returns>
    private bool IsSunExposureBlocked(RoomRuntime room, string targetValueReference)
    {
        if (!TryResolveShutter(room.RoomKey, targetValueReference, out var shutter))
            return false;

        return IsShutterSunExposureBlocked(room, shutter);
    }

    private bool IsShutterSunExposureBlocked(RoomRuntime room, ShutterRuntime shutter)
    {
        if (!TryGetNumericValue(room.Shadowing.SunPositionAzimuth, out var sunAzimuthDeg) ||
            !TryGetNumericValue(room.Shadowing.SunPositionElevation, out var sunElevationDeg))
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
        ShutterRuntime shutter,
        double sunAzimuthDeg,
        double sunElevationDeg,
        double maxIncidence)
    {
        if (IsBlockedByShadowingZones(shutter.ShadowingZones, sunAzimuthDeg, sunElevationDeg))
            return false;

        var incidence = ComputeIncidenceAngleDeg(shutter.FacadeOrientationDeg, sunAzimuthDeg, sunElevationDeg);
        return incidence <= maxIncidence;
    }

    private double ResolveEffectiveCutoverAngle(RoomRuntime room)
    {
        var baseline = room.RoomCutoverAngleOverride ?? room.GlobalCutoverAngle;
        var rules = room.RoomDynamicCutoverRules.Count > 0
            ? room.RoomDynamicCutoverRules
            : room.GlobalDynamicCutoverRules;

        if (rules.Count == 0)
            return baseline;

        var thermalMode = ShutterPolicyResolver.ResolveThermalControlMode(room.Shadowing);
        var hasOutdoorTemperature = TryGetNumericValue(room.Shadowing.OutdoorTemperature, out var outdoorTemperature);

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

    private static bool TryGetNumericValue(IValue? value, out double numeric)
    {
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

    private bool TryWriteNumeric(IValue targetValue, double value)
    {
        try
        {
            switch (targetValue)
            {
                case IValue<byte> v:
                    v.Write((byte)ClampToRange(value, byte.MinValue, byte.MaxValue), this);
                    return true;
                case IValue<sbyte> v:
                    v.Write((sbyte)ClampToRange(value, sbyte.MinValue, sbyte.MaxValue), this);
                    return true;
                case IValue<short> v:
                    v.Write((short)ClampToRange(value, short.MinValue, short.MaxValue), this);
                    return true;
                case IValue<ushort> v:
                    v.Write((ushort)ClampToRange(value, ushort.MinValue, ushort.MaxValue), this);
                    return true;
                case IValue<int> v:
                    v.Write(ClampToRange(value, int.MinValue, int.MaxValue), this);
                    return true;
                case IValue<uint> v:
                    v.Write(ClampToUInt(value), this);
                    return true;
                case IValue<long> v:
                    v.Write((long)Math.Round(value, MidpointRounding.AwayFromZero), this);
                    return true;
                case IValue<ulong> v:
                    v.Write((ulong)Math.Max(0, Math.Round(value, MidpointRounding.AwayFromZero)), this);
                    return true;
                case IValue<float> v:
                    v.Write((float)value, this);
                    return true;
                case IValue<double> v:
                    v.Write(value, this);
                    return true;
                case IValue<decimal> v:
                    v.Write((decimal)value, this);
                    return true;
                case IValue<bool> v:
                    v.Write(Math.Abs(value) >= 0.5, this);
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed writing numeric command to target value {TargetName}.", targetValue.Name);
            return false;
        }
    }

    private static int ClampToRange(double value, int min, int max)
    {
        var rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        return Math.Clamp(rounded, min, max);
    }

    private static uint ClampToUInt(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        if (rounded <= 0)
            return 0;
        if (rounded >= uint.MaxValue)
            return uint.MaxValue;
        return (uint)rounded;
    }

    private static HashSet<int> ResolveResumeAutomationScenes(CfgShadowingSpecial config)
    {
        if (config.ResumeAutomationScenes.Count == 0)
            return [.. DefaultResumeAutomationScenes];

        var valid = config.ResumeAutomationScenes
            .Where(scene => scene >= 0)
            .ToHashSet();

        return valid.Count == 0
            ? [.. DefaultResumeAutomationScenes]
            : valid;
    }

    private static int ResolveDefaultResumeAutomationScene(CfgShadowingSpecial config, HashSet<int> resolvedScenes)
    {
        if (config.ResumeAutomationScenes.Count > 0)
        {
            foreach (var scene in config.ResumeAutomationScenes)
            {
                if (scene >= 0 && resolvedScenes.Contains(scene))
                    return scene;
            }
        }

        if (resolvedScenes.Contains(52))
            return 52;

        return resolvedScenes.Min();
    }

    private bool HasActiveManualOverride(string roomKey, DateTimeOffset now)
    {
        lock (_syncLock)
        {
            if (!_manualOverrideState.RoomOverrides.TryGetValue(roomKey, out var entry))
                return false;

            if (entry.ExpiresAtUtc > now)
                return true;

            _manualOverrideState.RoomOverrides.Remove(roomKey);
            _ = PersistManualOverridesAsync();
            return false;
        }
    }

    private async Task ActivateManualOverrideAsync(string roomKey, TimeSpan duration)
    {
        var now = _timeProvider.GetUtcNow();
        lock (_syncLock)
        {
            _manualOverrideState.RoomOverrides[roomKey] = new ShutterManualOverrideEntry
            {
                CreatedAtUtc = now,
                ExpiresAtUtc = now + duration,
            };
        }

        await PersistManualOverridesAsync().ConfigureAwait(false);
    }

    private async Task ClearManualOverrideAsync(string roomKey)
    {
        var changed = false;
        lock (_syncLock)
        {
            changed = _manualOverrideState.RoomOverrides.Remove(roomKey);
        }

        if (changed)
            await PersistManualOverridesAsync().ConfigureAwait(false);
    }

    private async Task RestoreManualOverridesAsync(CancellationToken cancellationToken)
    {
        var loaded = await _stateStore
            .LoadAsync<ShutterManualOverrideStateSet>(StateSetName, TimeSpan.FromDays(30))
            .ConfigureAwait(false);

        var now = _timeProvider.GetUtcNow();
        var restored = loaded.StateSet ?? new ShutterManualOverrideStateSet();

        lock (_syncLock)
        {
            _manualOverrideState = restored;

            var expired = _manualOverrideState.RoomOverrides
                .Where(kv => kv.Value.ExpiresAtUtc <= now)
                .Select(kv => kv.Key)
                .ToArray();

            foreach (var key in expired)
                _manualOverrideState.RoomOverrides.Remove(key);
        }

        if (!cancellationToken.IsCancellationRequested)
            await PersistManualOverridesAsync().ConfigureAwait(false);
    }

    private async Task PersistManualOverridesAsync()
    {
        ShutterManualOverrideStateSet state;

        lock (_syncLock)
        {
            var filtered = new Dictionary<string, ShutterManualOverrideEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _manualOverrideState.RoomOverrides)
            {
                if (_roomByRoomKey.TryGetValue(kv.Key, out var room) && room.PersistManualOverride)
                    filtered[kv.Key] = kv.Value;
            }

            state = new ShutterManualOverrideStateSet { RoomOverrides = filtered };
        }

        await _stateStore.SaveAsync(StateSetName, state, timeoutSeconds: 30).ConfigureAwait(false);
    }

    private static bool TryGetSceneNumber(IValue sceneValue, out int scene)
    {
        scene = 0;

        if (sceneValue.OValue is null)
            return false;

        try
        {
            scene = Convert.ToInt32(sceneValue.OValue);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Represents the runtime state of a room, including its shutters, scene value, and various configuration settings.
    /// </summary>
    /// <param name="RoomKey">The unique key identifying the room. See <see cref="ShutterControl.CreateRoomKey"/> for details.</param>
    /// <param name="Room">The room model.</param>
    /// <param name="SceneValue">The current scene value of the room.</param>
    /// <param name="ManualOverrideDuration">The duration for which manual overrides are active.</param>
    /// <param name="PersistManualOverride">Indicates whether manual overrides should be persisted.</param>
    /// <param name="ResumeAutomationScenes">The set of scenes that can resume automation.</param>
    /// <param name="DefaultResumeAutomationScene">The default scene to resume automation.</param>
    /// <param name="Shadowing">The shadowing configuration for the room.</param>
    /// <param name="RoomCutoverAngleOverride">The override angle for room cutover.</param>
    /// <param name="RoomDynamicCutoverRules">The dynamic cutover rules specific to the room.</param>
    /// <param name="GlobalDynamicCutoverRules">The global dynamic cutover rules.</param>
    /// <param name="GlobalCutoverAngle">The global cutover angle.</param>
    /// <param name="MinSunElevationToConsider">The minimum sun elevation to consider for shadowing. If the sun is below this elevation, no shadowing is required and action depends on config and shutter status.</param>
    /// <param name="Shutters">The list of shutters in the room.</param>
    /// <returns></returns>
    private sealed record RoomRuntime(
        string RoomKey,
        Model.Room Room,
        IValue SceneValue,
        TimeSpan ManualOverrideDuration,
        bool PersistManualOverride,
        HashSet<int> ResumeAutomationScenes,
        int DefaultResumeAutomationScene,
        ShadowingSpecial Shadowing,
        double? RoomCutoverAngleOverride,
        List<CfgDynamicCutoverAngleRule> RoomDynamicCutoverRules,
        List<CfgDynamicCutoverAngleRule> GlobalDynamicCutoverRules,
        double GlobalCutoverAngle,
        double MinSunElevationToConsider,
        List<ShutterRuntime> Shutters);

    /// <summary>
    /// Represents the runtime state of a shutter, including its configuration and references.
    /// </summary>
    /// <param name="RoomKey">The unique key identifying the room. See <see cref="ShutterControl.CreateRoomKey"/> for details.</param>
    /// <param name="Name">The name of the shutter.</param>
    /// <param name="Shutter">The shutter model.</param>
    /// <param name="Type">The type of the shutter.</param>
    /// <param name="FacadeOrientationDeg">The orientation of the facade in degrees.</param>
    /// <param name="ShadowingZones">The shadowing zones associated with the shutter.</param>
    /// <param name="PositionValueReference">The reference for the position value.</param>
    /// <param name="AngleValueReference">The reference for the angle value.</param>
    /// <param name="OpenCloseReference">The reference for the open/close state.</param>
    /// <param name="DefaultShadowSlat">The default shadow slat position.</param>
    /// <returns></returns>
    private sealed record ShutterRuntime(
        string RoomKey,
        string Name,
        Model.Shutter Shutter,
        ShutterType Type,
        SphericVector FacadeOrientationDeg,
        Dictionary<string, CfgShadowingZone> ShadowingZones,
        string? PositionValueReference,
        string? AngleValueReference,
        string? OpenCloseReference,
        int DefaultShadowSlat);

    private readonly record struct ResumePlan(int ResumeScene, TimeSpan Delay, string Reason);

    private sealed class RoomSceneWriteRequestedEventHandler(ShutterSceneCommandControl owner)
        : EventHandlerBase<RoomSceneWriteRequestedEvent>
    {
        private readonly ShutterSceneCommandControl _owner = owner;

        public override ValueTask HandleAsync(RoomSceneWriteRequestedEvent @event, CancellationToken cancellationToken = default)
            => _owner.HandleRoomScheduleTransitionDueAsync(@event, cancellationToken);
    }
}
