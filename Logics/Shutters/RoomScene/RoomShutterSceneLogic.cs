using HomeCompanion.Base.Model;
using HomeCompanion.Diagnostics;
using HomeCompanion.Logics.Shutters.Runtimes;
using HomeCompanion.Logics.Shutters.ShutterAutomation;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters.RoomScene;

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
    IEventSubscriber eventSubscriber,
    IRuntimesProvider runtimesProvider,
    ILogger<RoomShutterSceneLogic> logger
) : LogicBase(logger), IDiagnosable
{
    private readonly IEventSubscriber eventSubscriber = eventSubscriber;
    private readonly IRuntimesProvider runtimesProvider = runtimesProvider;
    private readonly ILogger<RoomShutterSceneLogic> logger = logger;

    protected override async Task InitializeLatchedAsync(CancellationToken cancellationToken = default)
    {
        eventSubscriber.Subscribe<ShutterAutomationComputationTriggerEvent>(HandleShutterAutomationComputationTriggerEventAsync);
    }

    private async ValueTask HandleShutterAutomationComputationTriggerEventAsync(ShutterAutomationComputationTriggerEvent @event, CancellationToken cancellationToken = default)
    {
        // Room-scene handling is explicit: room-scoped triggers target selected rooms,
        // and global triggers target all room runtimes.
        if (!@event.Context.IsRoomScoped && !@event.Context.IsGlobalScope)
            return;

        var roomKeys = @event.Context.IsGlobalScope
            ? runtimesProvider.GetAllRoomRuntimes().Select(rr => rr.RoomKey)
            : @event.Context.GetRoomKeys();

        if (!roomKeys.Any())
            return;

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
        if (runtimesProvider.TryGetRoomRuntime(roomKey, out var roomRuntime) && roomRuntime != null)
        {
            await roomRuntime.HandleShutterAutomationComputationTriggerEvent(@event.Context, cancellationToken);
        }
        else
        {
            logger.LogWarning("No runtime found for room {RoomKey}. Cannot handle shutter automation computation trigger event.", roomKey.Key);
        }
    }

    protected override async Task<DiagnosticResultNode> PopulateDiagnosticResultsAsync(DiagnosticResultNode parentNode, CancellationToken cancellationToken)
    {
        parentNode.AddRecord("RoomShutterSceneLogic", $"See {nameof(RoomRuntime.HandleShutterAutomationComputationTriggerEvent)} for details.", "The RoomShutterSceneLogic does not manage shutter positions directly, but rather delegates to the RoomRuntime for each room.");
        return parentNode;
    }
}
