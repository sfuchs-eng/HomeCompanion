using HomeCompanion.Abstractions;
using HomeCompanion.Diagnostics;
using HomeCompanion.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace HomeCompanion.Core;

public class HomeCompanionLifeCycleSynchronization : BackgroundService, IHomeCompanionLifeCycleSynchronization, IDiagnosable
{
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<HomeCompanionLifeCycleSynchronization> logger;
    private readonly TimeProvider timeProvider;
    private readonly ConcurrentDictionary<AppInitializationStage, TaskCompletionSource> _completedInitializationStages =
        new(Enum.GetValues<AppInitializationStage>().Select(stage =>
            new KeyValuePair<AppInitializationStage, TaskCompletionSource>(
                stage,
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))));
    private readonly ConcurrentDictionary<AppInitializationStage, DateTimeOffset?> _completedInitializationStageTimestamps =
        new(Enum.GetValues<AppInitializationStage>().Select(stage =>
            new KeyValuePair<AppInitializationStage, DateTimeOffset?>(stage, null)));
    private long _waitCalls;
    private long _waitTimeouts;
    private long _signalCalls;
    private long _duplicateSignalCalls;

    public string Name => nameof(HomeCompanionLifeCycleSynchronization);

    public HomeCompanionLifeCycleSynchronization(
        IServiceProvider serviceProvider,
        ILogger<HomeCompanionLifeCycleSynchronization> logger,
        TimeProvider timeProvider
    ) : base()
    {
        this.serviceProvider = serviceProvider;
        this.logger = logger;
        this.timeProvider = timeProvider;
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

        logger.LogDebug("Waiting for {Count} enabled connectivity provider(s) to connect. Timeout={Timeout}.", connectivityProviders.Length, timeout);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(timeout);

        while (!cts.Token.IsCancellationRequested)
        {
            var allConnected = connectivityProviders.All(provider => provider.IsConnected);
            if (allConnected)
            {
                logger.LogDebug("All enabled connectivity providers are connected.");
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
        Interlocked.Increment(ref _waitCalls);
        var stageCompletionSource = _completedInitializationStages[level];
        if (stageCompletionSource.Task.IsCompleted)
        {
            logger.LogTrace("Initialization stage {Stage} is already completed.", level);
            return;
        }

        var waitStartedAt = timeProvider.GetUtcNow();
        logger.LogDebug("Waiting for initialization stage {Stage} to complete. Timeout={Timeout}.", level, timeout);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(timeout);

        try
        {
            await stageCompletionSource.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            logger.LogDebug(
                "Initialization stage {Stage} completed after {ElapsedMs} ms.",
                level,
                (timeProvider.GetUtcNow() - waitStartedAt).TotalMilliseconds);
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        {
            Interlocked.Increment(ref _waitTimeouts);
            logger.LogWarning("Timeout while waiting for initialization stage {Stage} to complete.", level);
            throw new TimeoutException($"Initialization stage {level} was not completed within the specified timeout.", ex);
        }
    }

    /// <summary>
    /// Signals that the specified initialization stage has been completed.
    /// </summary>
    public Task SignalInitializationStageCompletedAsync(AppInitializationStage level)
    {
        Interlocked.Increment(ref _signalCalls);
        var tcs = _completedInitializationStages[level];
        if (tcs.TrySetResult())
        {
            var completedAtUtc = timeProvider.GetUtcNow();
            _completedInitializationStageTimestamps[level] = completedAtUtc;
            logger.LogDebug("Signaled initialization stage completion: {Stage} at {CompletedAtUtc}.", level, completedAtUtc);
        }
        else
        {
            Interlocked.Increment(ref _duplicateSignalCalls);
            logger.LogTrace("Initialization stage {Stage} was already completed when signal was received.", level);
        }

        return Task.CompletedTask;
    }

    public Task<IDiagnosticResultNode> GetDiagnosisAsync(CancellationToken cancellationToken)
    {
        var root = DiagnosticResultNode.Create(Name);
        root.Records.Add(new DiagnosticRecord("WaitCalls", Interlocked.Read(ref _waitCalls)));
        root.Records.Add(new DiagnosticRecord("WaitTimeouts", Interlocked.Read(ref _waitTimeouts)));
        root.Records.Add(new DiagnosticRecord("SignalCalls", Interlocked.Read(ref _signalCalls)));
        root.Records.Add(new DiagnosticRecord("DuplicateSignalCalls", Interlocked.Read(ref _duplicateSignalCalls)));

        var stagesNode = root.AddChild("Stages");
        foreach (var stage in Enum.GetValues<AppInitializationStage>())
        {
            var child = stagesNode.AddChild(stage.ToString());
            child.Records.Add(new DiagnosticRecord("Completed", _completedInitializationStages[stage].Task.IsCompleted));
            child.Records.Add(new DiagnosticRecord("CompletedAtUtc", _completedInitializationStageTimestamps[stage]));
        }

        return Task.FromResult<IDiagnosticResultNode>(root);
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
