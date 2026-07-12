using HomeCompanion.Base.Model;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters;

/// <summary>
/// Controls the shutter scenes of each room:
/// <list type="bullet">
/// <item>Ensures automatic transition into automated shadowing scenes if environmental conditions require it.</item>
/// <item>Manages configured scene transitions.</item>
/// <item>When a scene is manually activated temporarily, it manages the transition back into automation if required.</item>
/// </list>
/// Does NOT manage the shutter positions directly, but rather manages the scene transitions.
/// </summary>
/// <typeparam name="RoomShutterSceneLogic"></typeparam>
public class RoomShutterSceneLogic(
    IValueProvider valuesProvider,
    IEventPublisher eventPublisher,
    IEventSubscriber eventSubscriber,
    TimeProvider timeProvider,
    IModelProvider modelProvider,
    IRuntimesProvider runtimesProvider,
    ShadowingRuntimesController runtimesController,
    ILoggerFactory loggerFactory,
    ILogger<RoomShutterSceneLogic> logger
) : LogicBase(eventPublisher, eventSubscriber)
{
    private readonly IValueProvider valuesProvider = valuesProvider;
    private readonly IEventPublisher eventPublisher = eventPublisher;
    private readonly IEventSubscriber eventSubscriber = eventSubscriber;
    private readonly TimeProvider timeProvider = timeProvider;
    private readonly IModelProvider modelProvider = modelProvider;
    private readonly IRuntimesProvider runtimesProvider = runtimesProvider;
    private readonly ShadowingRuntimesController runtimesController = runtimesController;
    private readonly ILoggerFactory loggerFactory = loggerFactory;
    private readonly ILogger<RoomShutterSceneLogic> logger = logger;

    protected override async Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
    {
        eventSubscriber.Subscribe<ShutterAutomationComputationTriggerEvent>(HandleShutterAutomationComputationTriggerEventAsync);
    }

    private async ValueTask HandleShutterAutomationComputationTriggerEventAsync(ShutterAutomationComputationTriggerEvent @event, CancellationToken cancellationToken = default)
    {
        // Only handle events that are relevant to room keys.
        if (!@event.Context.ThingKeys.Any(k => k is RoomKey))
            return;

        var roomKeys = @event.Context.ThingKeys.OfType<RoomKey>().Distinct();

        // iterate each room key. Have each room runtime handle the event, if it exists.
        foreach (var roomKey in roomKeys)
        {
            try
            {
                await HandleShutterAutomationComputationTriggerEventForRoomAsync(roomKey, @event, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error handling shutter automation computation trigger event for room {RoomKey}.", roomKey.Key);
            }
        }
    }

    private async Task HandleShutterAutomationComputationTriggerEventForRoomAsync(RoomKey roomKey, ShutterAutomationComputationTriggerEvent @event, CancellationToken cancellationToken)
    {
        if (runtimesProvider.RoomRuntimes.TryGetValue(roomKey, out var roomRuntime))
        {
            await roomRuntime.HandleShutterAutomationComputationTriggerEvent(@event.Context, cancellationToken);
        }
        else
        {
            logger.LogWarning("No runtime found for room {RoomKey}. Cannot handle shutter automation computation trigger event.", roomKey.Key);
        }
    }
}
