using HomeCompanion.Base.Utilities;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters;

public class ShutterRuntime(
    ShutterKey shutterKey,
    ILogger<ShutterRuntime> logger
) : RuntimeBase(logger)
{
    public ShutterKey ShutterKey { get; } = shutterKey;
    private readonly ILogger<ShutterRuntime> logger = logger;

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