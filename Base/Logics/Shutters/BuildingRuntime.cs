using HomeCompanion.Base.Model;
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

    /// <summary>
    /// Creates new runtimes for all buildings in the model unless there's already a matching key in the provided existing runtimes.
    /// </summary>
    /// <param name="runtimeCreationContext"></param>
    /// <returns>Only newly created runtimes</returns>
    public static Dictionary<BuildingKey, BuildingRuntime> Create(RuntimeCreationContext<BuildingKey, BuildingRuntime> runtimeCreationContext)
    {
        var model = runtimeCreationContext.Model;
        var existingRuntimes = runtimeCreationContext.ExistingRuntimes;
        var queueFeeder = runtimeCreationContext.ComputationTriggerQueueFeeder;
        var loggerFactory = runtimeCreationContext.LoggerFactory;
        var newRuntimes = new Dictionary<BuildingKey, BuildingRuntime>();

        foreach (var buildingKey in model.EnumerateBuildingKeys())
        {
            if (existingRuntimes != null && existingRuntimes.ContainsKey(buildingKey))
            {
                continue;
            }

            var runtime = new BuildingRuntime(buildingKey, queueFeeder, loggerFactory.CreateLogger<BuildingRuntime>());
            newRuntimes[buildingKey] = runtime;
        }
        return newRuntimes;
    }
}
