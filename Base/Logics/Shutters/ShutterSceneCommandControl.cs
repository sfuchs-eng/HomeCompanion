using HomeCompanion.Base.Model;
using HomeCompanion.Base.Utilities;
using HomeCompanion.Events;
using HomeCompanion.Persistence;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace HomeCompanion.Base.Logics.Shutters;

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
    IValueReferenceProvider valueReferenceProvider,
    IStateStore stateStore,
    TimeProvider timeProvider,
    ILogger<ShutterSceneCommandControl> logger,
    IEventPublisher publisher,
    IEventSubscriber subscriber)
    : LogicBase(publisher, subscriber)
{
    private const string StateSetName = "ShutterControlManualOverrides";
    private static readonly HashSet<int> ManualActorScenes = [1, 2, 3];
    private static readonly HashSet<int> DefaultResumeAutomationScenes = [50, 52];

    private readonly IModelProvider _modelProvider = modelProvider;
    private readonly IValueReferenceProvider _valueReferenceProvider = valueReferenceProvider;
    private readonly IStateStore _stateStore = stateStore;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<ShutterSceneCommandControl> _logger = logger;

    private readonly object _syncLock = new();
    private readonly Dictionary<IValue, RoomRuntime> _roomBySceneValue = [];
    private readonly Dictionary<string, RoomRuntime> _roomByRoomKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string RoomKey, int Scene), CfgShadowingSceneController> _sceneControllers = [];
    private readonly Dictionary<string, FacadeExposureBinding> _facadeExposureByTarget = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<IValue> _subscribedSceneValues = [];
    private readonly Dictionary<string, CancellationTokenSource> _scheduledResumeByRoom = new(StringComparer.OrdinalIgnoreCase);

    private ShutterManualOverrideStateSet _manualOverrideState = new();

    protected override async Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
    {
        Subscribe(new RoomScheduleTransitionDueEventHandler(this));

        if (!_modelProvider.IsInitialized)
        {
            _logger.LogWarning("Model provider is not initialized yet. ShutterSceneCommandControl skipped initialization.");
            return;
        }

        MaterializeRuntime(_modelProvider.GetModel());
        await RestoreManualOverridesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "ShutterSceneCommandControl initialized with {RoomCount} rooms and {ControllerCount} scene controllers.",
            _roomByRoomKey.Count,
            _sceneControllers.Count);
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

    private void MaterializeRuntime(HomeCompanion.Base.Model.Model model)
    {
        var nextRoomByScene = new Dictionary<IValue, RoomRuntime>();
        var nextRoomByKey = new Dictionary<string, RoomRuntime>(StringComparer.OrdinalIgnoreCase);
        var nextControllers = new Dictionary<(string RoomKey, int Scene), CfgShadowingSceneController>();
        var nextFacadeExposureByTarget = new Dictionary<string, FacadeExposureBinding>(StringComparer.OrdinalIgnoreCase);

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

                    var roomRuntime = new RoomRuntime(
                        roomKey,
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
                        globalConfig.MinSunElevationToConsider);
                    nextRoomByScene[room.ShutterScene] = roomRuntime;
                    nextRoomByKey[roomKey] = roomRuntime;

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

                        RegisterFacadeTarget(nextFacadeExposureByTarget, roomKey, shutterConfig.PositionValueReference, facade, shutterConfig.ShadowingZones);
                        RegisterFacadeTarget(nextFacadeExposureByTarget, roomKey, shutterConfig.AngleValueReference, facade, shutterConfig.ShadowingZones);
                        RegisterFacadeTarget(nextFacadeExposureByTarget, roomKey, shutterConfig.OpenCloseReference, facade, shutterConfig.ShadowingZones);
                    }

                    if (!string.IsNullOrWhiteSpace(roomConfig.ShutterSceneReference))
                        roomBySceneReference[roomConfig.ShutterSceneReference] = roomKey;
                }
            }

            foreach (var controller in globalConfig.SpecialScenes.Values)
            {
                var roomKey = ResolveControllerRoomKey(controller, roomBySceneReference);
                if (string.IsNullOrWhiteSpace(roomKey))
                    continue;

                nextControllers[(roomKey, controller.Number)] = controller;
            }
        }

        lock (_syncLock)
        {
            foreach (var old in _subscribedSceneValues.Except(nextRoomByScene.Keys).ToArray())
            {
                old.Changed -= OnRoomSceneChanged;
                _subscribedSceneValues.Remove(old);
            }

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

            _facadeExposureByTarget.Clear();
            foreach (var kv in nextFacadeExposureByTarget)
                _facadeExposureByTarget[kv.Key] = kv.Value;
        }
    }

    private static void RegisterFacadeTarget(
        Dictionary<string, FacadeExposureBinding> bindings,
        string roomKey,
        string? targetReference,
        Facade facade,
        Dictionary<string, CfgShadowingZone> shadowingZones)
    {
        if (string.IsNullOrWhiteSpace(targetReference))
            return;

        bindings[BuildRoomTargetKey(roomKey, targetReference)] = new FacadeExposureBinding(facade.OrientationRad, shadowingZones);
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
        _ = e;

        if (sender is not IValue sceneValue)
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

    private ValueTask HandleRoomScheduleTransitionDueAsync(RoomScheduleTransitionDueEvent due, CancellationToken cancellationToken)
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
            ExecuteSceneControllerCommands(controller, room, due.Scene, "automation-schedule", applySunExposureGate: false);

        ScheduleResumeAutomation(room, due);

        return ValueTask.CompletedTask;
    }

    private void ScheduleResumeAutomation(RoomRuntime room, RoomScheduleTransitionDueEvent due)
    {
        var plan = ResolveResumePlan(room, due);
        if (plan is null)
            return;

        var cancellation = RegisterScheduledResume(room.RoomKey);
        _ = RunScheduledResumeAsync(room.RoomKey, plan.Value, cancellation.Token);
    }

    private ResumePlan? ResolveResumePlan(RoomRuntime room, RoomScheduleTransitionDueEvent due)
    {
        var resumeScene = ResolveResumeScene(room, due.ResumeAutomationScene);

        if (due.ResumeAutomationAfter.HasValue && due.ResumeAutomationAfter.Value > TimeSpan.Zero)
            return new ResumePlan(resumeScene, due.ResumeAutomationAfter.Value, "delay");

        if (TryResolveResumeAtLocalDelay(due, out var localDelay))
            return new ResumePlan(resumeScene, localDelay, "local-time");

        return null;
    }

    private static bool TryResolveResumeAtLocalDelay(RoomScheduleTransitionDueEvent due, out TimeSpan delay)
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

            if (!IsRoomSunExposed(room))
            {
                _logger.LogInformation(
                    "Room {RoomKey}: skipped auto-resume scene {Scene} ({Reason}) because sun exposure is inactive.",
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

    private bool IsRoomSunExposed(RoomRuntime room)
    {
        if (!TryGetNumericValue(room.Shadowing.SunPositionAzimuth, out var sunAzimuthDeg) ||
            !TryGetNumericValue(room.Shadowing.SunPositionElevation, out var sunElevationDeg))
        {
            return false;
        }

        if (sunElevationDeg < room.MinSunElevationToConsider)
            return false;

        var bindings = GetRoomFacadeBindings(room.RoomKey);
        if (bindings.Count == 0)
            return true;

        var cutover = ResolveEffectiveCutoverAngle(room);
        var maxIncidence = 90.0 - ClampCutoverAngle(cutover);

        foreach (var binding in bindings)
        {
            if (IsBlockedByShadowingZones(binding.ShadowingZones, sunAzimuthDeg, sunElevationDeg))
                continue;

            var incidence = ComputeIncidenceAngleDeg(binding.FacadeOrientationDeg, sunAzimuthDeg, sunElevationDeg);
            if (incidence <= maxIncidence)
                return true;
        }

        return false;
    }

    private IReadOnlyList<FacadeExposureBinding> GetRoomFacadeBindings(string roomKey)
    {
        var prefix = roomKey + "|";
        var result = new List<FacadeExposureBinding>();

        lock (_syncLock)
        {
            foreach (var kv in _facadeExposureByTarget)
            {
                if (!kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!result.Contains(kv.Value))
                    result.Add(kv.Value);
            }
        }

        return result;
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
        var key = BuildRoomTargetKey(room.RoomKey, targetValueReference);
        if (!_facadeExposureByTarget.TryGetValue(key, out var binding))
            return false;

        if (!TryGetNumericValue(room.Shadowing.SunPositionAzimuth, out var sunAzimuthDeg) ||
            !TryGetNumericValue(room.Shadowing.SunPositionElevation, out var sunElevationDeg))
        {
            _logger.LogWarning(
                "Room {RoomKey}: sun position values are missing or invalid; skipping automation command for facade-scoped target '{TargetValueReference}'.",
                room.RoomKey,
                targetValueReference);
            return true;
        }

        if (sunElevationDeg < room.MinSunElevationToConsider)
            return true;

        if (IsBlockedByShadowingZones(binding.ShadowingZones, sunAzimuthDeg, sunElevationDeg))
            return true;

        var cutover = ResolveEffectiveCutoverAngle(room);
        var maxIncidence = 90.0 - ClampCutoverAngle(cutover);
        var incidence = ComputeIncidenceAngleDeg(binding.FacadeOrientationDeg, sunAzimuthDeg, sunElevationDeg);

        return incidence > maxIncidence;
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

    private sealed record RoomRuntime(
        string RoomKey,
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
        double MinSunElevationToConsider);

    private sealed record FacadeExposureBinding(
        SphericVector FacadeOrientationDeg,
        Dictionary<string, CfgShadowingZone> ShadowingZones);

    private readonly record struct ResumePlan(int ResumeScene, TimeSpan Delay, string Reason);

    private sealed class RoomScheduleTransitionDueEventHandler(ShutterSceneCommandControl owner)
        : EventHandlerBase<RoomScheduleTransitionDueEvent>
    {
        private readonly ShutterSceneCommandControl _owner = owner;

        public override ValueTask HandleAsync(RoomScheduleTransitionDueEvent @event, CancellationToken cancellationToken = default)
            => _owner.HandleRoomScheduleTransitionDueAsync(@event, cancellationToken);
    }
}
