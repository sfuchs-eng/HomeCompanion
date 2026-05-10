using HomeCompanion.Abstractions;
using HomeCompanion.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace HomeCompanion.Core;

public class HomeCompanionLifeCycleSynchronization : BackgroundService, IHomeCompanionLifeCycleSynchronization
{
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<HomeCompanionLifeCycleSynchronization> logger;
    private readonly ConcurrentDictionary<AppInitializationStage, TaskCompletionSource> _completedInitializationStages =
        new(Enum.GetValues<AppInitializationStage>().Select(stage =>
            new KeyValuePair<AppInitializationStage, TaskCompletionSource>(
                stage,
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))));

    public HomeCompanionLifeCycleSynchronization(
        IServiceProvider serviceProvider,
        ILogger<HomeCompanionLifeCycleSynchronization> logger
    ) : base()
    {
        this.serviceProvider = serviceProvider;
        this.logger = logger;
    }

    /// <summary>
    /// Waits until all buses are connected or reconnected.
    /// </summary>
    public async Task AwaitBusesConnectedAsync(TimeSpan timeout, CancellationToken token = default)
    {
        var connectivityProviders = serviceProvider
            .GetServices<IConnectivityProvider>()
            .Where(provider => provider.IsEnabled)
            .ToArray();

        if (connectivityProviders.Length == 0)
        {
            logger.LogInformation("No enabled connectivity providers registered. Treating buses as connected.");
            return;
        }

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

    /// <summary>
    /// Waits until the specified initialization stage has been completed.
    /// </summary>
    public async Task WaitForInitializationStageCompletedAsync(
        AppInitializationStage level,
        TimeSpan timeout,
        CancellationToken token = default)
    {
        var stageCompletionSource = _completedInitializationStages[level];
        if (stageCompletionSource.Task.IsCompleted)
            return;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(timeout);

        try
        {
            await stageCompletionSource.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        {
            logger.LogWarning("Timeout while waiting for initialization stage {Stage} to complete.", level);
            throw new TimeoutException($"Initialization stage {level} was not completed within the specified timeout.", ex);
        }
        await SignalInitializationStageCompletedAsync(level);
    }

    /// <summary>
    /// Signals that the specified initialization stage has been completed.
    /// </summary>
    public Task SignalInitializationStageCompletedAsync(AppInitializationStage level)
    {
        _completedInitializationStages[level].TrySetResult();
        return Task.CompletedTask;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SignalInitializationStageCompletedAsync(AppInitializationStage.Default); // we're running, so whatever is constructed is also in init stage default.
        return Task.CompletedTask;
    }

    public bool IsInitializationStageCompleted(AppInitializationStage level)
    {
        return _completedInitializationStages[level].Task.IsCompleted;
    }

    public bool IsAllUpToStageCompleted(AppInitializationStage level)
    {
        return Enum.GetValues<AppInitializationStage>()
            .Where(stage => stage <= level)
            .All(stage => _completedInitializationStages[stage].Task.IsCompleted);
    }
}
