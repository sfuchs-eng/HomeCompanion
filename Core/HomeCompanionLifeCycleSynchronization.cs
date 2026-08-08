using HomeCompanion.Abstractions;
using HomeCompanion.Diagnostics;
using HomeCompanion.Persistence;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
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

    private readonly ConcurrentDictionary<AppInitializationStage, DateTimeOffset?> _firstSignalTimestamps =
        new(Enum.GetValues<AppInitializationStage>().Select(stage =>
            new KeyValuePair<AppInitializationStage, DateTimeOffset?>(stage, null)));

    private readonly ConcurrentDictionary<AppInitializationStage, List<Func<AppInitializationStage, CancellationToken, Task>>> _requiredExecutionsPerStage =
        new(Enum.GetValues<AppInitializationStage>().Select(stage =>
            new KeyValuePair<AppInitializationStage, List<Func<AppInitializationStage, CancellationToken, Task>>>(stage, [])));

    private long _waitCalls;
    private long _waitTimeouts;
    private long _signalCalls;
    private long _duplicateSignalCalls;

    // tracker for required signallers per stage
    private readonly ConcurrentDictionary<AppInitializationStage, HashSet<object>> _requiredSignallersPerStage =
        new(Enum.GetValues<AppInitializationStage>().Select(stage =>
            new KeyValuePair<AppInitializationStage, HashSet<object>>(stage, [])));

    // Tracks whether at least one explicit signal was received for a stage.
    private readonly ConcurrentDictionary<AppInitializationStage, bool> _hasSignalPerStage =
        new(Enum.GetValues<AppInitializationStage>().Select(stage =>
            new KeyValuePair<AppInitializationStage, bool>(stage, false)));

    public string Name => nameof(HomeCompanionLifeCycleSynchronization);

    private AppInitializationStage _lastCompletedStage = AppInitializationStage.Default;
    public AppInitializationStage LastCompletedStage { get => _lastCompletedStage; }

    public HomeCompanionLifeCycleSynchronization(
        IServiceProvider serviceProvider,
        IHostApplicationLifetime applicationLifetime,
        ILogger<HomeCompanionLifeCycleSynchronization> logger,
        TimeProvider timeProvider
    ) : base()
    {
        this.serviceProvider = serviceProvider;
        this.logger = logger;
        this.timeProvider = timeProvider;
        applicationLifetime.ApplicationStopping.Register(() =>
        {
            logger.LogInformation("Initiating shutdown sequence.");
            StartTerminationSequence();
        });
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

        var waitStartedAt = timeProvider.GetLocalNow();
        logger.LogDebug("Waiting for initialization stage {Stage} to complete. Timeout={Timeout}.", level, timeout);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(timeout);

        try
        {
            await stageCompletionSource.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            logger.LogDebug(
                "Initialization stage {Stage} completed after {ElapsedMs} ms.",
                level,
                (timeProvider.GetLocalNow() - waitStartedAt).TotalMilliseconds);
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
    public Task SignalInitializationStageCompletedAsync(AppInitializationStage level, object? sender = null)
    {
        Interlocked.Increment(ref _signalCalls);

        var isDuplicateSignal = _hasSignalPerStage[level];
        if (!isDuplicateSignal)
        {
            _hasSignalPerStage[level] = true;
            _firstSignalTimestamps[level] = timeProvider.GetLocalNow();
        }
        else
        {
            Interlocked.Increment(ref _duplicateSignalCalls);
        }

        // do we have required signallers for this stage? If so, check if the sender is one of them and remove it from the list.
        var requiredSignallers = _requiredSignallersPerStage[level];

        if (sender is not null)
        {
            lock (requiredSignallers)
            {
                requiredSignallers.Remove(sender);
            }
        }

        TriggerExecutionLoop(); // trigger the execution loop to check for stage completion
        return Task.CompletedTask;
    }

    /// <summary>
    /// Executes all required executions for the specified initialization stage, and then signals that the stage has been completed.
    /// </summary>
    protected async Task ExecuteRequiredExecutionsAsync(AppInitializationStage level, object? sender = null)
    {
        var requiredExecutions = _requiredExecutionsPerStage[level];
        if (requiredExecutions.Count > 0)
        {
            logger.LogTrace("Executing {Count} required executions for initialization stage {Stage}.", requiredExecutions.Count, level);
            var executionTasks = requiredExecutions.Select(execution => execution(level, CancellationToken.None));
            try
            {
                await Task.WhenAll(executionTasks).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while executing required executions for initialization stage {Stage}.", level);
            }
        }
    }

    /// <summary>
    /// Marks the stage as completed and raises the event signalling that the specified initialization stage has been completed.
    /// </summary>
    private async Task OnStageCompletedAsync(AppInitializationStage level, object? sender = null)
    {
        var tcs = _completedInitializationStages[level];
        if (tcs.TrySetResult())
        {
            _lastCompletedStage = level;
            var completedAt = _firstSignalTimestamps[level] ?? timeProvider.GetLocalNow();
            _completedInitializationStageTimestamps[level] = completedAt;
            logger.LogInformation("Life cycle stage {Stage} completed at {CompletedAt}.", level, completedAt);

            // call the event handlers in a fire-and-forget manner, as we don't want to block the signaling of the stage completion.
            // ensure failure of any does not affect the signaling of the stage completion and neither the other handlers.
            await Task.Run(() =>
            {
                foreach (var handler in InitializationStageCompleted?.GetInvocationList() ?? [])
                {
                    try
                    {
                        handler.DynamicInvoke(this, new AppInitializationStageCompletedEventArgs(level));
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Error while invoking InitializationStageCompleted event handler for stage {Stage}.", level);
                    }
                }
            });
        }
    }

    // where's the runner to chain up the stages reliably? We need to ensure that the stages are completed in order, and that the required signallers and executions are respected. This is a complex problem, and we need to ensure that we have a robust solution.

    public event EventHandler<AppInitializationStageCompletedEventArgs>? InitializationStageCompleted;

    /// <summary>
    /// A stage is only completed when all required signallers have signaled completion of the stage. If no required signaller is registered, the stage is considered completed automatically after all required executions have been completed.
    /// If no required execution is registered either, the stage is considered completed automatically.
    /// </summary>
    public void RegisterRequiredSignaller(AppInitializationStage level, object signaller)
    {
        var signallers = _requiredSignallersPerStage[level];
        lock (signallers)
        {
            signallers.Add(signaller);
        }
        TriggerExecutionLoop(); // trigger the execution loop to check for stage completion
    }

    /// <summary>
    /// A stage is only completed when all required executions have been completed AND all required signallers have signaled completion of the stage.
    /// If no required execution is registered and signallers have either signaled completion or none are registered, the stage is considered completed automatically.
    /// </summary>
    /// <param name="level"></param>
    /// <param name="execution"></param>
    public void RegisterRequiredExecution(AppInitializationStage level, Func<AppInitializationStage, CancellationToken, Task> execution)
    {
        var executions = _requiredExecutionsPerStage[level];
        lock (executions)
        {
            executions.Add(execution);
        }
        TriggerExecutionLoop(); // trigger the execution loop to check for stage completion
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
            child.Records.Add(new DiagnosticRecord("CompletedAt", _completedInitializationStageTimestamps[stage]));
            child.Records.Add(new DiagnosticRecord("Remaining RequiredSignallers", _requiredSignallersPerStage[stage].Count));
        }

        return Task.FromResult<IDiagnosticResultNode>(root);
    }

    // awaitable external trigger for the background service to check for stage completion. This is a simple loop that checks for stages that can be completed.
    private readonly SemaphoreSlim _executionLoopTrigger = new(0);
    private bool _executionLoopRunTerminationSequence = false;

    protected void TriggerExecutionLoop()
    {
        if (_executionLoopTrigger.CurrentCount == 0)
            _executionLoopTrigger.Release();
    }

    protected void StartTerminationSequence()
    {
        _executionLoopRunTerminationSequence = true;
        TriggerExecutionLoop();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await OnStageCompletedAsync(AppInitializationStage.Default).ConfigureAwait(false); // bootstrap internal default stage without counting as external signal.

        while (!stoppingToken.IsCancellationRequested && !LastCompletedStage.IsLastStage())
        {
            // wait for a verification trigger to check for signallers and executions to complete stages. This is a simple loop that checks for stages that can be completed.
            await _executionLoopTrigger.WaitAsync(stoppingToken).ConfigureAwait(false);
            foreach (var stage in Enum.GetValues<AppInitializationStage>())
            {
                if (IsLifeCycleStageCompleted(stage))
                    continue;

                // if the ramp-up stages are completed and we're not in termination sequence, we don't want to complete any further stages until the termination sequence is started.
                if (stage.IsTerminationStage() && !_executionLoopRunTerminationSequence)
                    break;

                // executions to be run?
                var requiredExecutions = _requiredExecutionsPerStage[stage];
                if (requiredExecutions.Count > 0)
                {
                    await ExecuteRequiredExecutionsAsync(stage).ConfigureAwait(false);
                }

                // any remaining required signallers?
                var requiredSignallers = _requiredSignallersPerStage[stage];
                lock (requiredSignallers)
                {
                    if (requiredSignallers.Count > 0)
                        break; // wait for signallers to signal completion before we can complete the stage.
                }

                // perform the stage completion
                await OnStageCompletedAsync(stage).ConfigureAwait(false);
            }
        }
        logger.LogTrace("Background service execution loop completed. StoppingToken.IsCancellationRequested={IsCancellationRequested}, LastCompletedStage={LastCompletedStage}", stoppingToken.IsCancellationRequested, LastCompletedStage);
    }

    public bool IsLifeCycleStageCompleted(AppInitializationStage level)
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
