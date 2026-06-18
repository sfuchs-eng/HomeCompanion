using Microsoft.Extensions.Logging;
using HomeCompanion.Base.Utilities;
using HomeCompanion.Base.Model;

namespace HomeCompanion.Logics.Shutters;

public class RoomRuntime(
    RoomKey roomKey,
    IQueueFeeder<ShutterAutomationComputationTriggerContext> queueFeeder,
    ILogger<RoomRuntime> logger
) : RuntimeBase(logger)
{
    public RoomKey RoomKey { get; } = roomKey;
    private readonly IQueueFeeder<ShutterAutomationComputationTriggerContext> queueFeeder = queueFeeder;
    private readonly ILogger<RoomRuntime> logger = logger;

    public override Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override Task StartAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override Task StopAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
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

        foreach (var roomKey in model.EnumerateRooms())
        {
            if (roomRuntimes?.ContainsKey(roomKey) == true)
            {
                continue;
            }

            var runtime = new RoomRuntime(roomKey, queueFeeder, loggerFactory.CreateLogger<RoomRuntime>());
            newRuntimes[roomKey] = runtime;
        }
        return newRuntimes;
    }
}
