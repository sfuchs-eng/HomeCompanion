using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters;

/// <summary>
/// <para>Encapsulates room level runtime state and logic for shutter scene management.</para>
/// <list type="bullet">
/// <item>Manages the room shutter scene state machine.</item>
/// <item>Enqueues recomputation triggers for the room shutter scene state machine when inputs change.</item>
/// <item>Provides scene writing methods that ensure the room shutter scene state machine is aware of the changes to distinguish between internal and external scene changes.</item>
/// </list>
/// </summary>
/// <param name="roomKey"></param>
/// <param name="queueFeeder"></param>
/// <param name="logger"></param> <summary>
/// </summary>
/// <typeparam name="ShutterAutomationComputationTriggerContext"></typeparam>
public class RoomRuntime : RuntimeBase, IDisposable
{
    public RoomContext RoomContext { get; }
    public RoomKey RoomKey => RoomContext.RoomKey;

    private readonly IQueueFeeder<ShutterAutomationComputationTriggerContext> queueFeeder;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<RoomRuntime> logger;

    public virtual required RoomSceneConditionsAssessor SceneConditionsAssessor { get; init; }
    public virtual required RoomSceneResolver SceneResolver { get; init; }

    public virtual byte LastSceneCommanded { get; protected set; } = (byte)RoomShutterScene.Undefined;

    public RoomRuntime(RoomContext roomContext, IQueueFeeder<ShutterAutomationComputationTriggerContext> queueFeeder, TimeProvider timeProvider, ILogger<RoomRuntime> logger)
        : base(logger)
    {
        RoomContext = roomContext;
        this.queueFeeder = queueFeeder;
        this.timeProvider = timeProvider;
        this.logger = logger;
        filteredRoomTemperatureObservableCached = new CachedValue<IObservable<float>>(Observable.Empty<float>(), () => {
            return GetFilteredRoomTemperatureObservable() ?? Observable.Return<float>(20.0f);
        });
    }

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken);
    }

    public override void Dispose(bool disposing)
    {
        if (disposing)
        {
            roomTemperatureAboveAutoShadowThresholdObservableSubscription?.Dispose();
            filteredRoomTemperatureSubscription?.Dispose();
        }
        base.Dispose(disposing);
    }

    IDisposable? roomTemperatureAboveAutoShadowThresholdObservableSubscription = null;
    public bool IsRoomTemperatureAboveAutoShadowThreshold { get; private set; } = false;

    IDisposable? filteredRoomTemperatureSubscription = null;
    public float FilteredRoomTemperature { get; protected set; } = 20.0f;

    protected CachedValue<IObservable<float>> filteredRoomTemperatureObservableCached;

    protected virtual IObservable<float>? GetFilteredRoomTemperatureObservable()
    {
        // throttle to max 15 min and put a hysteresis of 0.2°C to avoid rapid toggling of the above/below threshold state due to small fluctuations around the threshold value
        var filteredRoomTemperatureObservable = RoomContext.Room.Temperature?.AsObservable<float>()
            .DistinctUntilChangedWithHysteresis(0.2f)
            .Throttle(TimeSpan.FromMinutes(15))
            .Publish()
            .RefCount();
        return filteredRoomTemperatureObservable;
    }

    protected virtual IDisposable? ObserveFilteredRoomTemperature()
    {
        var filteredRoomTemperatureObservable = filteredRoomTemperatureObservableCached.Value;

        if (filteredRoomTemperatureObservable == null)
        {
            logger.LogWarning("Filtered room temperature observable is null for room {RoomKey}. Cannot monitor filtered temperature changes.", RoomKey.Key);
            filteredRoomTemperatureObservable = Observable.Return<float>((float)RoomContext.Room.Configuration.TargetRoomTemperature); // fallback to an empty observable to avoid null reference exceptions
        }

        return filteredRoomTemperatureObservable?
            .Subscribe(filteredTemp =>
            {
                FilteredRoomTemperature = filteredTemp;
                // enqueue a recomputation trigger for the room shutter scene state machine to evaluate the new filtered temperature and determine whether to accept it or not
                IEnumerable<IThingKey> thingKeys = [RoomKey, .. this.RoomContext.Room.Shutters.Values.Select(shutter => new ShutterKey(RoomKey, shutter))];
                queueFeeder.EnqueueAsync(new ShutterAutomationComputationTriggerContext(
                    thingKeys: thingKeys,
                    scope: ShutterAutomationComputationScope.RoomSpecific | ShutterAutomationComputationScope.ShutterSpecific,
                    triggeringValue: RoomContext.Room.Temperature != null ? [RoomContext.Room.Temperature] : [],
                    valueEventArgs: [],
                    timestamp: timeProvider.GetLocalNow(),
                    urgency: ShutterAutomationComputationTriggerUrgency.Slow
                ), CancellationToken.None).ConfigureAwait(false);
            });
    }

    protected virtual IDisposable? ObserveRoomTemperatureAboveAutoShadowThreshold()
    {
        // threshold with relaxation hysteresis to avoid rapid toggling of the above/below threshold state due to small fluctuations around the threshold value
        var roomTemperatureAboveAutoShadowThresholdObservable = filteredRoomTemperatureObservableCached.Value?
            .Select(temp => temp >= RoomContext.Room.Configuration.AutoShadowTemperatureThreshold)
            .DistinctUntilChanged();

        if (roomTemperatureAboveAutoShadowThresholdObservable == null)
        {
            logger.LogWarning("Room temperature observable is null for room {RoomKey}. Cannot monitor temperature threshold crossings.", RoomKey.Key);
            roomTemperatureAboveAutoShadowThresholdObservable = Observable.Return<bool>(false); // fallback to an empty observable to avoid null reference exceptions
        }

        return roomTemperatureAboveAutoShadowThresholdObservable?
            .Subscribe(isAboveThreshold =>
            {
                // update the property
                IsRoomTemperatureAboveAutoShadowThreshold = isAboveThreshold;
                // enqueue a recomputation trigger for the room shutter scene state machine to evaluate the new temperature condition and determine whether to accept it or not
                IEnumerable<IThingKey> thingKeys = [RoomKey, .. this.RoomContext.Room.Shutters.Values.Select(shutter => new ShutterKey(RoomKey, shutter))];
                queueFeeder.EnqueueAsync(new ShutterAutomationComputationTriggerContext(
                    thingKeys: thingKeys,
                    scope: ShutterAutomationComputationScope.RoomSpecific,
                    triggeringValue: RoomContext.Room.Temperature != null ? [RoomContext.Room.Temperature] : [],
                    valueEventArgs: [],
                    timestamp: timeProvider.GetLocalNow(),
                    urgency: ShutterAutomationComputationTriggerUrgency.Slow
                ), CancellationToken.None).ConfigureAwait(false);
            });
    }

    public override Task StartAsync(CancellationToken cancellationToken = default)
    {
        RoomContext.Room.Temperature?.Changed += HandleRoomTemperatureChanged;
        RoomContext.Room.ShutterScene?.Changed += HandleRoomShutterSceneChanged;
        roomTemperatureAboveAutoShadowThresholdObservableSubscription = ObserveRoomTemperatureAboveAutoShadowThreshold();
        filteredRoomTemperatureSubscription = ObserveFilteredRoomTemperature();

        return Task.CompletedTask;
    }

    private object _sceneWriteLock = new();

    private DateTimeOffset _lastSceneNonBurstWriteTimestamp = DateTimeOffset.MinValue;
    private TimeSpan _sceneBurstWriteWindow = TimeSpan.FromSeconds(20);
    private int _sceneBurstWriteLimit = 5;
    private int _sceneBurstWriteCount = 0;

    /// <summary>
    /// Sets a room shutter scene while preventing to handle it thereafter.
    /// Performs also burst write protection to avoid flooding the bus with too many scene writes in a short time.
    /// The burst write protection is implemented by allowing a maximum number of writes within a given time window. If the limit is reached, further writes are ignored until the time window has passed.
    /// Using this method ensures setting the LastSceneCommanded property which then can be used to discover user overrides / scene requests written by other systems or logics.
    /// </summary>
    public void SetRoomShutterScene(byte scene)
    {
        if (scene == (byte)RoomShutterScene.Undefined)
        {
            logger.LogWarning("Cannot set room shutter scene to Undefined for room {RoomKey}. LastCommanded: {LastCommanded}", RoomKey.Key, LastSceneCommanded);
            return;
        }

        if (RoomContext.Room.ShutterScene is not ValueBase<byte> sceneValue)
        {
            logger.LogWarning("Cannot set room shutter scene for room {RoomKey} because the ShutterScene value is not a ValueBase<byte>. LastCommanded: {LastCommanded}", RoomKey.Key, LastSceneCommanded);
            return;
        }

        lock (_sceneWriteLock)
        {
            var now = DateTimeOffset.UtcNow;
            if (_lastSceneNonBurstWriteTimestamp + _sceneBurstWriteWindow < now)
            {
                _sceneBurstWriteCount = 0;
                _lastSceneNonBurstWriteTimestamp = now;
            }
            if (_sceneBurstWriteCount >= _sceneBurstWriteLimit)
            {
                logger.LogWarning("Room shutter scene burst write limit reached for room {RoomKey}. LastCommanded: {LastCommanded}, Ignoring new scene command to {NewScene}", RoomKey.Key, LastSceneCommanded, scene);
                return;
            }
            _sceneBurstWriteCount++;
            sceneValue.Write(scene, this);
            LastSceneCommanded = scene;
            ExecuteRoomShuttterScene(scene);
        }
    }

    /// <summary>
    /// Check for definitions of the room shutter scene and execute the corresponding actions for the scene.
    /// This method is called after the room shutter scene has been set and is responsible for executing the actions associated with the scene, such as setting shutter positions or triggering other logics.
    /// </summary>
    private void ExecuteRoomShuttterScene(byte scene)
    {
        RoomShutterScene? roomScene = Enum.IsDefined(typeof(RoomShutterScene), scene) ? (RoomShutterScene)scene : null;

        // is it a don't touch scene or externally handled one? Then do nothing.
        if (roomScene?.IsDoNotInterfere() == true || roomScene?.IsDeactivated() == true)
        {
            logger.LogTrace("Room shutter scene is set to {RoomScene} for room {RoomKey}. No actions will be executed.", roomScene, RoomKey.Key);
            return;
        }

        // is it an automation scene? cause a shutter automation computation trigger event to be enqueued for the room runtime to handle it and determine whether to accept the requested scene or not.
        if (roomScene?.IsAutomationScene() == true)
        {
            logger.LogTrace("Room shutter scene is set to {RoomScene} for room {RoomKey}. Enqueuing a recomputation trigger for the room runtime to handle it.", roomScene, RoomKey.Key);
            IEnumerable<IThingKey> thingKeys = RoomContext.Room.Shutters.Values.Select(shutter => new ShutterKey(RoomKey, shutter));
            queueFeeder.EnqueueAsync(new ShutterAutomationComputationTriggerContext(
                thingKeys: thingKeys,
                scope: ShutterAutomationComputationScope.ShutterSpecific,
                triggeringValue: RoomContext.Room.ShutterScene != null ? [RoomContext.Room.ShutterScene] : [],
                valueEventArgs: [],
                timestamp: timeProvider.GetLocalNow(),
                urgency: ShutterAutomationComputationTriggerUrgency.Slow
            ), CancellationToken.None).ConfigureAwait(false);
            return;
        }

        // is it a defined scene with shutter presets? then enqueue a trigger for the shutters to discover and react to the new scene.
        if (RoomContext.TryGetRoomSceneShutterPreset(scene, out var _))
        {
            logger.LogTrace("Room shutter scene is set to {RoomScene} for room {RoomKey}. Enqueuing a recomputation trigger for the shutters to handle it.", roomScene, RoomKey.Key);
            IEnumerable<IThingKey> thingKeys = RoomContext.Room.Shutters.Values.Select(shutter => new ShutterKey(RoomKey, shutter));
            queueFeeder.EnqueueAsync(new ShutterAutomationComputationTriggerContext(
                thingKeys: thingKeys,
                scope: ShutterAutomationComputationScope.ShutterSpecific,
                triggeringValue: RoomContext.Room.ShutterScene != null ? [RoomContext.Room.ShutterScene] : [],
                valueEventArgs: [],
                timestamp: timeProvider.GetLocalNow(),
                urgency: ShutterAutomationComputationTriggerUrgency.Normal
            ), CancellationToken.None).ConfigureAwait(false);
            return;
        }
    }

    public void SetRoomShutterScene(RoomShutterScene scene)
    {
        SetRoomShutterScene((byte)scene);
    }

    /// <summary>
    /// Idea: IValue<byte> for room shutter scene is handled as requested scene. The runtime determines whether to accept it or not.
    /// Actuation of room scene depends on the accepted room scene which is handled only internally and not exposed to the model. The accepted room scene is determined by the room scene resolver which considers the requested scene, the current scene, and other factors.
    /// The room scene resolver is fed by the room scene conditions assessor which considers the room objective profile, the thermal control mode, the shutter constraints, the temperature thresholds, and other factors.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void HandleRoomShutterSceneChanged(object? sender, ValueChangedEventArgs e)
    {
        try
        {
            // must not handle our own changes to the room shutter scene, otherwise we would get into an infinite loop of handling our own changes
            if (sender == this)
                return;

            // handle changes in the room shutter scene originating from other logics or busses, e.g. manual override, schedule transitions, or other logics that set the room shutter scene directly
            logger.LogTrace("Room shutter scene changed for room {RoomKey} from {OldValue} to {NewValue} by {Sender}. LastCommanded: {LastCommanded}", RoomKey.Key, e.PreviousValue, e.NewValue, sender?.GetType().Name ?? "null", LastSceneCommanded);

            // enqueue a recomputation trigger for the room shutter scene state machine to evaluate the new requested scene and determine whether to accept it or not
            IEnumerable<IThingKey> thingKeys = [RoomKey];
            queueFeeder.EnqueueAsync(new ShutterAutomationComputationTriggerContext(
                thingKeys: thingKeys,
                scope: ShutterAutomationComputationScope.RoomSpecific,
                triggeringValue: e.NewValue != null ? [e.NewValue] : [],
                valueEventArgs: [e],
                timestamp: e.Timestamp,
                urgency: ShutterAutomationComputationTriggerUrgency.Immediate
            ), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error while handling room shutter scene change for room {RoomKey}.", RoomKey.Key);
        }
    }

    private void HandleRoomTemperatureChanged(object? sender, ValueChangedEventArgs e)
    {
        try
        {
            // handle changes in the room temperature originating from sensors
            logger.LogTrace("Room temperature changed for room {RoomKey} from {OldValue} to {NewValue} by {Sender}. LastCommanded: {LastCommanded}", RoomKey.Key, e.PreviousValue, e.NewValue, sender?.GetType().Name ?? "null", LastSceneCommanded);
            IEnumerable<IThingKey> thingKeys = [RoomKey];
            queueFeeder.EnqueueAsync(new ShutterAutomationComputationTriggerContext(
                thingKeys: thingKeys,
                scope: ShutterAutomationComputationScope.RoomSpecific,
                triggeringValue: e.NewValue != null ? [e.NewValue] : [],
                valueEventArgs: [e],
                timestamp: e.Timestamp,
                urgency: ShutterAutomationComputationTriggerUrgency.Slow
            ), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error while handling room temperature change for room {RoomKey}.", RoomKey.Key);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken = default)
    {
        RoomContext.Room.Temperature?.Changed -= HandleRoomTemperatureChanged;
        RoomContext.Room.ShutterScene?.Changed -= HandleRoomShutterSceneChanged;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates new runtimes for all rooms in the model unless there's already a matching key in the provided existing runtimes.
    /// </summary>
    /// <param name="runtimeCreationContext"></param>
    /// <returns>Only newly created runtimes</returns>
    public static Dictionary<RoomKey, RoomRuntime> Create(RuntimeCreationContext<RoomKey, RoomRuntime> runtimeCreationContext)
    {
        var model = runtimeCreationContext.Model;
        var roomRuntimes = runtimeCreationContext.ExistingRuntimes;
        var queueFeeder = runtimeCreationContext.ComputationTriggerQueueFeeder;
        var loggerFactory = runtimeCreationContext.LoggerFactory;

        var newRuntimes = new Dictionary<RoomKey, RoomRuntime>();

        foreach (var roomContext in model.EnumerateRoomContexts())
        {
            var roomKey = roomContext.RoomKey;
            if (roomRuntimes?.ContainsKey(roomKey) == true)
            {
                continue;
            }

            var runtime = new RoomRuntime(roomContext, queueFeeder, runtimeCreationContext.TimeProvider, loggerFactory.CreateLogger<RoomRuntime>())
            {
                SceneConditionsAssessor = new RoomSceneConditionsAssessor(roomContext, loggerFactory.CreateLogger<RoomSceneConditionsAssessor>()),
                SceneResolver = new RoomSceneResolver(roomContext, loggerFactory.CreateLogger<RoomSceneResolver>())
            };
            newRuntimes[roomKey] = runtime;
        }
        return newRuntimes;
    }

    /// <summary>
    /// Runs the full room shutter scene automation loop: from assessing the room scene conditions, to resolving the room scene, to setting the room shutter scene.
    /// Is typically called by an external logic that handles the recomputation triggers for the room shutter scene state machine, e.g. the RoomShutterSceneLogic.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="cancellationToken"></param>
    internal async Task HandleShutterAutomationComputationTriggerEvent(ShutterAutomationComputationTriggerContext context, CancellationToken cancellationToken)
    {
        var targetScene = SceneResolver.ResolveTargetRoomShutterScene(SceneConditionsAssessor);
        if ( targetScene == null || targetScene == (byte)RoomShutterScene.Undefined)
        {
            logger.LogWarning("Room shutter scene resolver returned {targetScene} for room {RoomKey}. LastCommanded: {LastCommanded}", targetScene, RoomKey.Key, LastSceneCommanded);
            return;
        }
        SetRoomShutterScene(targetScene.Value);
    }
}
