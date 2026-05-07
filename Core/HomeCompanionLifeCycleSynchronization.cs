using HomeCompanion.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Core;

public class HomeCompanionLifeCycleSynchronization(
    IEnumerable<IConnectivityProvider> connectivityProviders,
    ILogger<HomeCompanionLifeCycleSynchronization> logger
) : BackgroundService(), IHomeCompanionLifeCycleSynchronization
{
    private readonly IEnumerable<IConnectivityProvider> connectivityProviders = connectivityProviders;
    private readonly ILogger<HomeCompanionLifeCycleSynchronization> logger = logger;

    /// <summary>
    /// Waits until all buses are connected or reconnected.
    /// </summary>
    public async Task AwaitBusesConnectedAsync(TimeSpan timeout, CancellationToken token = default)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(timeout);

        while (!cts.Token.IsCancellationRequested)
        {
            var allConnected = connectivityProviders.All(provider => provider.IsConnected);
            if (allConnected)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(2), cts.Token).ConfigureAwait(false);
        }
        logger.LogWarning("Timeout or cancellation while waiting for all connectivity providers to be connected.");
        throw new TimeoutException("Not all connectivity providers could connect within the specified timeout or prior cancellation.");
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }
}
