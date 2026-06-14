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
}
