using HomeCompanion.Persistence;
using HomeCompanion.Values;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Core.Values;

public class ValuesInitializationManager : IValuesInitializationManager
{
    protected readonly ILogger<ValuesInitializationManager> Logger;

    protected readonly IStateStore StateStore;

    private readonly Dictionary<ValuesInitializationStage, List<ValuesInitializationDelegate>> Initializations;
    private readonly object _initializationLock = new();

    public ValuesInitializationManager(
            IStateStore stateStore,
            ILogger<ValuesInitializationManager> logger
    )
    {
        Logger = logger;
        StateStore = stateStore;
        Initializations = Enum.GetValues<ValuesInitializationStage>()
            .ToDictionary(stage => stage, stage => new List<ValuesInitializationDelegate>());
        Initializations[ValuesInitializationStage.InitLoadFromStore].Add(InitializeFromStoreAsync);
        Initializations[ValuesInitializationStage.InitRetrieveFromEnvironment].Add(InitializeFromOpenHabAsync);
        
    }

    public void RegisterInitialization(ValuesInitializationStage stage, ValuesInitializationDelegate initialization)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        lock (_initializationLock)
        {
            Initializations[stage].Add(initialization);
        }
    }

    public void RemoveInitialization(ValuesInitializationStage stage, ValuesInitializationDelegate initialization)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        lock (_initializationLock)
        {
            Initializations[stage].Remove(initialization);
        }
    }

    protected virtual async Task ExecuteInitializationDelegatesAsync(IAsyncEnumerable<ValuesInitializationDelegate> initializations, CancellationToken token = default)
    {
        await foreach (var initialization in initializations.WithCancellation(token).ConfigureAwait(false))
        {
            try
            {
                Logger.LogInformation("Executing {DeclaringType}.{MethodName} for stage {Stage}", initialization.Method.DeclaringType?.Name, initialization.Method.Name, CurrentStage);
                await initialization(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected when the cancellation token is triggered, no action needed
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
            {
                // expected when the task is canceled, no action needed
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Unhandled failure during invocation of {DeclaringType}.{MethodName} for stage {Stage}.", initialization.Method.DeclaringType?.Name, initialization.Method.Name, CurrentStage);
            }
        }
    }

    public async Task InitializeValuesAsync(CancellationToken token = default)
    {
        Logger.LogInformation("Starting values initialization.");
        var skipStages = new HashSet<ValuesInitializationStage>()
        {
            ValuesInitializationStage.Default,
            ValuesInitializationStage.ShutDownSave
        };

        await ExecuteInitializationDelegatesAsync(
            Initializations.Where(kvp => !skipStages.Contains(kvp.Key))
                .SelectMany(kvp => kvp.Value.Select(init => (Stage: kvp.Key, Init: init)))
                .OrderBy(tuple => tuple.Stage)
                .Select(tuple => tuple.Init)
                .ToAsyncEnumerable(),
            token).ConfigureAwait(false);
            
        Logger.LogInformation("Finished values initialization.");
    }

    public async Task SaveValuesAsync(CancellationToken token = default)
    {
        await ExecuteInitializationDelegatesAsync(Initializations[ValuesInitializationStage.ShutDownSave].ToAsyncEnumerable(), token).ConfigureAwait(false);
    }

    protected virtual Task InitializeFromStoreAsync(CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    protected virtual Task InitializeFromOpenHabAsync(CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    public ValuesInitializationStage CurrentStage { get; private set; }
}

public class ValuesInitializationManagerHostedService(
    IValuesInitializationManager valuesInitializationManager,
    ILogger<ValuesInitializationManagerHostedService> logger) : IHostedService
{
    private readonly IValuesInitializationManager _valuesInitializationManager = valuesInitializationManager;
    private readonly ILogger<ValuesInitializationManagerHostedService> _logger = logger;

    private CancellationTokenSource? _initializationCancellationTokenSource;
    private Task? _initializationTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _initializationCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _initializationTask = _valuesInitializationManager.InitializeValuesAsync(_initializationCancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled failure while initializing the IValue framework. State reached before failure: {State}", _valuesInitializationManager.CurrentStage);
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Cancel the initialization task and wait for it to complete, with a timeout to avoid hanging indefinitely during shutdown
        _initializationCancellationTokenSource?.Cancel();
        try
        {
            _initializationTask?.Wait(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // expected when the cancellation token is triggered, no action needed
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
        {
            // expected when the task is canceled, no action needed
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled failure while stopping the ValuesInitializationManagerHostedService.");
        }
        _initializationCancellationTokenSource?.Dispose();
        _initializationCancellationTokenSource = null;

        // Save the values during shutdown to persist changes across restarts of the application
        try
        {
            await _valuesInitializationManager.SaveValuesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected when the cancellation token is triggered, no action needed
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
        {
            // expected when the task is canceled, no action needed
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unhandled failure while saving values during shutdown in ValuesInitializationManagerHostedService.");
        }
    }
}
