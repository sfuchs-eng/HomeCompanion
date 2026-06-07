using HomeCompanion.Base.Model;
using HomeCompanion.Events;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Base.Logics.Shutters;

/// <summary>
/// Executes room-scoped shutter scene commands and is the single logic writing room scene values.
/// It does not command any shutters directly. Such is either done by KNX actors reacting to scene value changes, OpenHAB, or by <see cref="ShutterControl"/> when executing scene command targets.
/// </summary>
/// <remarks>
/// Scene semantics (partly configurable):
/// <list type="bullet">
/// <item><description>1,2,3: manual override active; actuator behavior is implemented at KNX actor level.</description></item>
/// <item><description>50,52: clear manual override and resume automation.</description></item>
/// <item><description>Other scenes: treated as manual-override scenes when a matching <see cref="CfgShadowingSceneController"/> exists for the room and scene number.</description></item>
/// </list>
/// While a room is in scene 50/52 and has no active manual override, automation requests can write room scenes,
/// after which <see cref="ShutterControl"/> executes room-scoped scene command targets.
/// </remarks>
public sealed class ShutterSceneCommandControl(
    ShutterControl shutterControl,
    IModelProvider modelProvider,
    IValueReferenceProvider valueReferenceProvider,
    TimeProvider timeProvider,
    ILogger<ShutterSceneCommandControl> logger,
    IEventPublisher publisher,
    IEventSubscriber subscriber)
    : LogicBase(publisher, subscriber)
{
    private static readonly HashSet<int> ManualActorScenes = [1, 2, 3, 4, 5];
    private static readonly HashSet<int> DefaultResumeAutomationScenes = [50, 52];

    private readonly ShutterControl _shutterControl = shutterControl;
    private readonly IModelProvider _modelProvider = modelProvider;
    private readonly IValueReferenceProvider _valueReferenceProvider = valueReferenceProvider;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<ShutterSceneCommandControl> _logger = logger;

    private readonly object _syncLock = new();
    private readonly Dictionary<IValue, RoomRuntime> _roomBySceneValue = [];
    private readonly Dictionary<string, RoomRuntime> _roomByRoomKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<IValue> _subscribedSceneValues = [];

    protected override async Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
    {
        Subscribe(new RoomSceneWriteRequestedEventHandler(this));
        await _shutterControl.InitializeAsync(cancellationToken).ConfigureAwait(false);

        if (!_modelProvider.IsInitialized)
        {
            _logger.LogWarning("Model provider is not initialized yet. ShutterSceneCommandControl skipped initialization.");
            return;
        }

        MaterializeRuntime(_modelProvider.GetModel());

        _logger.LogInformation(
            "ShutterSceneCommandControl initialized with {RoomCount} rooms.",
            _roomByRoomKey.Count);
    }

    public override Task DisableAsync(CancellationToken cancellationToken = default)
    {
        _shutterControl.CancelAllScheduledResumes();

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

        foreach (var building in model.Buildings.Values)
        {
            var shadowing = building.Specials.Values.OfType<ShadowingSpecial>().FirstOrDefault();
            if (shadowing is null)
                continue;

            var globalConfig = shadowing.Configuration;

            foreach (var floor in building.Floors.Values)
            {
                foreach (var room in floor.Rooms.Values)
                {
                    if (room.Shutters.Count == 0 || room.ShutterScene is null)
                        continue;

                    var roomKey = ShutterControl.CreateRoomKey(building.Name, floor.Name, room.Name);
                    var roomConfig = room.Configuration;
                    var manualOverrideDuration = roomConfig.ManualOverrideDuration ?? globalConfig.DefaultManualOverrideDuration;
                    var resumeAutomationScenes = ResolveResumeAutomationScenes(globalConfig);
                    var defaultResumeAutomationScene = ResolveDefaultResumeAutomationScene(globalConfig, resumeAutomationScenes);

                    var roomRuntime = new RoomRuntime(
                        roomKey,
                        room.ShutterScene,
                        manualOverrideDuration,
                        resumeAutomationScenes,
                        defaultResumeAutomationScene);
                    nextRoomByScene[room.ShutterScene] = roomRuntime;
                    nextRoomByKey[roomKey] = roomRuntime;
                }
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
        }
    }

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
            _shutterControl.CancelScheduledResume(room.RoomKey);

            if (room.ResumeAutomationScenes.Contains(scene))
            {
                await _shutterControl.ClearManualOverrideAsync(room.RoomKey).ConfigureAwait(false);
                _logger.LogInformation("Room {RoomKey}: scene {Scene} -> resume automation.", room.RoomKey, scene);
                return;
            }
            
            if (ManualActorScenes.Contains(scene))
            {
                await _shutterControl.ActivateManualOverrideAsync(room.RoomKey, room.ManualOverrideDuration).ConfigureAwait(false);
                _logger.LogInformation("Room {RoomKey}: actor-manual scene {Scene} -> manual override active.", room.RoomKey, scene);
                return;
            }

            if (_shutterControl.TryExecuteSceneCommands(room.RoomKey, scene, "manual-scene", applySunExposureGate: false))
            {
                await _shutterControl.ActivateManualOverrideAsync(room.RoomKey, room.ManualOverrideDuration).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed handling room scene change for room {RoomKey}, scene {Scene}.", room.RoomKey, scene);
        }
    }

    private ValueTask HandleRoomSceneWriteRequestedAsync(RoomSceneWriteRequestedEvent due, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (!TryGetRoom(due.RoomKey, out var room))
            return ValueTask.CompletedTask;

        if (!TryGetSceneNumber(room.SceneValue, out var currentScene) || !room.ResumeAutomationScenes.Contains(currentScene))
            return ValueTask.CompletedTask;

        var now = _timeProvider.GetUtcNow();
        if (_shutterControl.HasActiveManualOverride(room.RoomKey, now))
            return ValueTask.CompletedTask;

        if (!TryWriteRoomScene(room, due.Scene, due.ScheduleKey, "automation-request"))
            return ValueTask.CompletedTask;

        _ = _shutterControl.ActivateManualOverrideAsync(room.RoomKey, room.ManualOverrideDuration);

        _shutterControl.TryExecuteSceneCommands(room.RoomKey, due.Scene, "automation-schedule", applySunExposureGate: true);

        ScheduleResumeAutomation(room, due);

        return ValueTask.CompletedTask;
    }

    private void ScheduleResumeAutomation(RoomRuntime room, RoomSceneWriteRequestedEvent due)
    {
        var plan = ResolveResumePlan(room, due);
        if (plan is null)
            return;

        _shutterControl.ScheduleResumeAutomation(room.RoomKey, room.SceneValue, plan.Value.ResumeScene, plan.Value.Delay, plan.Value.Reason);
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

    private bool TryGetRoom(string roomKey, out RoomRuntime room)
    {
        lock (_syncLock)
        {
            return _roomByRoomKey.TryGetValue(roomKey, out room!);
        }
    }

    private bool TryWriteRoomScene(RoomRuntime room, int scene, string context, string source)
    {
        if (TryWriteRoomSceneNumeric(room.SceneValue, scene))
            return true;

        _logger.LogWarning(
            "Room {RoomKey}: {Source} could not write scene {Scene} ({Context}) to room scene value '{SceneName}'.",
            room.RoomKey,
            source,
            scene,
            context,
            room.SceneValue.Name);
        return false;
    }

    private bool TryWriteRoomSceneNumeric(IValue targetValue, double value)
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
            _logger.LogWarning(ex, "Failed writing scene value to target {TargetName}.", targetValue.Name);
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

    private HashSet<int> ResolveResumeAutomationScenes(CfgShadowingSpecial config)
    {
        if (config.ResumeAutomationScenes.Count == 0)
        {
            _logger.LogWarning(
                "No resume automation scenes configured in shadowing special. Defaulting to scenes {DefaultScenes}.",
                string.Join(", ", DefaultResumeAutomationScenes));
            return [.. DefaultResumeAutomationScenes];
        }

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

        return resolvedScenes.Min();
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
        HashSet<int> ResumeAutomationScenes,
        int DefaultResumeAutomationScene);

    private readonly record struct ResumePlan(int ResumeScene, TimeSpan Delay, string Reason);

    private sealed class RoomSceneWriteRequestedEventHandler(ShutterSceneCommandControl owner)
        : EventHandlerBase<RoomSceneWriteRequestedEvent>
    {
        private readonly ShutterSceneCommandControl _owner = owner;

        public override ValueTask HandleAsync(RoomSceneWriteRequestedEvent @event, CancellationToken cancellationToken = default)
            => _owner.HandleRoomSceneWriteRequestedAsync(@event, cancellationToken);
    }
}
