using HomeCompanion.Base.Utilities;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters;

public class BuildingRuntime(
    BuildingKey buildingKey,
    IQueueFeeder<ShutterAutomationComputationTriggerContext> queueFeeder,
    ILogger<BuildingRuntime> logger
) : RuntimeBase(logger)
{
    public BuildingKey BuildingKey { get; } = buildingKey;
    private readonly IQueueFeeder<ShutterAutomationComputationTriggerContext> queueFeeder = queueFeeder;
    private readonly ILogger<BuildingRuntime> logger = logger;

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
