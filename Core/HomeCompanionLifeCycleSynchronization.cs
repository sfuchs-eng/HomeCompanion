using HomeCompanion.Abstractions;
using HomeCompanion.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace HomeCompanion.Core;

public class HomeCompanionLifeCycleSynchronization(
    IEnumerable<IConnectivityProvider> connectivityProviders,
    ILogger<HomeCompanionLifeCycleSynchronization> logger
) : BackgroundService(), IHomeCompanionLifeCycleSynchronization
{
    private readonly IEnumerable<IConnectivityProvider> connectivityProviders = connectivityProviders;
    private readonly ILogger<HomeCompanionLifeCycleSynchronization> logger = logger;
    private readonly ConcurrentDictionary<StateInitializationStage, TaskCompletionSource> _completedInitializationStages =
        new(Enum.GetValues<StateInitializationStage>().Select(stage =>
            new KeyValuePair<StateInitializationStage, TaskCompletionSource>(
                stage,
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))));

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

    /// <summary>
    /// Waits until the specified initialization stage has been completed.
    /// </summary>
    public async Task WaitForInitializationStageCompletedAsync(
        StateInitializationStage level,
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
    }

    /// <summary>
    /// Signals that the specified initialization stage has been completed.
    /// </summary>
    public Task SignalInitializationStageCompletedAsync(StateInitializationStage level)
    {
        _completedInitializationStages[level].TrySetResult();
        return Task.CompletedTask;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }
}
