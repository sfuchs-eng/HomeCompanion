using HomeCompanion.Base.Utilities;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters;

public class BuildingRuntime(
    BuildingKey buildingKey,
    ILogger<BuildingRuntime> logger
) : RuntimeBase(logger)
{
    private readonly BuildingKey buildingKey = buildingKey;
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
