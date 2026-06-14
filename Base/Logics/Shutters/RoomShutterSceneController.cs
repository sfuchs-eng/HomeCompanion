using HomeCompanion.Base.Model;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters;

public class RoomShutterSceneController(
    IValueProvider valuesProvider,
    IEventPublisher eventPublisher,
    IEventSubscriber eventSubscriber,
    TimeProvider timeProvider,
    IModelProvider modelProvider,
    ILoggerFactory loggerFactory,
    ILogger<RoomShutterSceneController> logger
) : LogicBase(eventPublisher, eventSubscriber)
{
    private readonly IValueProvider valuesProvider = valuesProvider;
    private readonly IEventPublisher eventPublisher = eventPublisher;
    private readonly IEventSubscriber eventSubscriber = eventSubscriber;
    private readonly TimeProvider timeProvider = timeProvider;
    private readonly IModelProvider modelProvider = modelProvider;
    private readonly ILoggerFactory loggerFactory = loggerFactory;
    private readonly ILogger<RoomShutterSceneController> logger = logger;

    private readonly Dictionary<RoomKey, RoomRuntime> roomRuntimes = new();

    protected override async Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
    {
        var model = modelProvider.GetModel();
        CheckConfiguration(model);
        await MaterializeRuntime(model);
    }

    private async Task MaterializeRuntime(Model model)
    {
        var newRuntimes = CreateRoomRuntimes(model);

        // Stop runtimes that are not needed anymore
        foreach (var oldRuntime in roomRuntimes.Values)
        {
            if (!newRuntimes.ContainsKey(oldRuntime.RoomKey))
                await oldRuntime.StopAsync();
        }

        // Start new runtimes
        foreach (var newRuntime in newRuntimes.Values)
        {
            if (!roomRuntimes.ContainsKey(newRuntime.RoomKey))
                await newRuntime.StartAsync();
        }

        roomRuntimes.Clear();
        foreach (var kvp in newRuntimes)
            roomRuntimes[kvp.Key] = kvp.Value;
    }

    /// <summary>
    /// TODO: move the runtime creation & provisioning into a dedicated provider/factory class, and inject the runtimes into the controller, so that the controller is not responsible for creating the runtimes and can be more easily tested in isolation.
    /// <see cref="RoomShutterSceneController"/> and <see cref="ShutterController"/> both need to use the same runtimes, so it would be good to have a shared provider/factory for them that ensures that they use the same runtime instances, and that the runtimes are properly started and stopped when the model changes.
    /// </summary>
    /// <param name="model"></param>
    /// <returns></returns>
    private Dictionary<RoomKey, RoomRuntime> CreateRoomRuntimes(Model model)
    {
        var runtimes = new Dictionary<RoomKey, RoomRuntime>();
        foreach (var building in model.Buildings.Values)
        {
            var shadowing = building.Specials.Values.OfType<ShadowingSpecial>().FirstOrDefault();
            if (shadowing is null)
                continue;
            foreach (var floor in building.Floors.Values)
            {
                foreach (var room in floor.Rooms.Values)
                {
                    var roomKey = new RoomKey(building, floor, room);
                    if (roomRuntimes.TryGetValue(roomKey, out var existingRuntime))
                    {
                        runtimes[roomKey] = existingRuntime;
                        continue;
                    }

                    var runtime = new RoomRuntime(roomKey, loggerFactory.CreateLogger<RoomRuntime>());
                    runtimes[roomKey] = runtime;
                }
            }
        }
        return runtimes;
    }

    /// <summary>
    /// Check the aspects in the model that would result in errors or warning when <see cref="MaterializeRuntime"/> is executed, and log them as warnings or errors.
    /// This allows to detect and fix model issues before they manifest as incorrect or missing shutter commands or failed scene changes at runtime.
    /// </summary>
    /// <param name="model"></param>
    private void CheckConfiguration(Model model)
    {
        foreach (var building in model.Buildings.Values)
        {
            var shadowing = building.Specials.Values.OfType<ShadowingSpecial>().FirstOrDefault();
            if (shadowing is null)
            {
                logger.LogWarning(
                    "Building '{BuildingName}' has no shadowing special configured. All rooms in this building might be ignored by {LogicName}.",
                    building.Name, nameof(RoomShutterSceneController));
                continue;
            }

            foreach (var floor in building.Floors.Values)
            {
                foreach (var room in floor.Rooms.Values)
                {
                    if (room.Shutters.Count == 0 || room.ShutterScene is null)
                        continue;

                    var roomKey = new RoomKey(building, floor, room);

                    foreach (var shutter in room.Shutters.Values)
                    {
                        if (string.IsNullOrWhiteSpace(shutter.Configuration.FacadeReference))
                        {
                            logger.LogWarning(
                                "Room {RoomKey} shutter {ShutterName} has no facade reference configured.",
                                roomKey,
                                shutter.Name);
                            continue;
                        }

                        if (!building.Facades.TryGetValue(shutter.Configuration.FacadeReference, out var facade))
                        {
                            logger.LogWarning(
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
}