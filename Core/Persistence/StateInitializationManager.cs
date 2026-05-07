using HomeCompanion.Abstractions;
using HomeCompanion.Persistence;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Core.Persistence;

public class StateInitializationManager : IStateInitializationManager
{
    protected readonly ILogger<StateInitializationManager> Logger;
    private readonly IHomeCompanionLifeCycleSynchronization lifeCycleSynchronization;
    protected readonly IStateStore StateStore;

    private readonly Dictionary<StateInitializationStage, List<StateInitializationDelegate>> Initializations;
    private readonly object _initializationLock = new();

    public StateInitializationManager(
            IHomeCompanionLifeCycleSynchronization lifeCycleSynchronization,
            IStateStore stateStore,
            ILogger<StateInitializationManager> logger
    )
    {
        Logger = logger;
        this.lifeCycleSynchronization = lifeCycleSynchronization;
        StateStore = stateStore;
        Initializations = Enum.GetValues<StateInitializationStage>()
            .ToDictionary(stage => stage, stage => new List<StateInitializationDelegate>());
        Initializations[StateInitializationStage.InitLoadFromStore].Add(InitializeValuesFromStoreAsync);
        Initializations[StateInitializationStage.ShutDownSave].Add(SaveValuesStateAsync);
    }

    public void RegisterInitialization(StateInitializationStage stage, StateInitializationDelegate initialization)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        lock (_initializationLock)
        {
            Initializations[stage].Add(initialization);
        }
    }

    public void RemoveInitialization(StateInitializationStage stage, StateInitializationDelegate initialization)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        lock (_initializationLock)
        {
            Initializations[stage].Remove(initialization);
        }
    }

    protected virtual async Task ExecuteInitializationDelegatesAsync(IAsyncEnumerable<StateInitializationDelegate> initializations, CancellationToken token = default)
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

    /// <summary>
    /// Trigger initialization
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task InitializeStateAsync(CancellationToken token = default)
    {
        Logger.LogInformation("Starting values initialization.");
        var skipStages = new HashSet<StateInitializationStage>()
        {
            StateInitializationStage.Default,
            StateInitializationStage.ShutDownSave
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

    /// <summary>
    /// Trigger saving
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task SaveStateAsync(CancellationToken token = default)
    {
        await ExecuteInitializationDelegatesAsync(Initializations[StateInitializationStage.ShutDownSave].ToAsyncEnumerable(), token).ConfigureAwait(false);
    }

    protected virtual async Task InitializeValuesFromStoreAsync(CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    protected virtual async Task SaveValuesStateAsync(CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public void RegisterSave(StateInitializationDelegate save)
    {
        ArgumentNullException.ThrowIfNull(save);
        lock (_initializationLock)
        {
            Initializations[StateInitializationStage.ShutDownSave].Add(save);
        }
    }

    public void RemoveSave(StateInitializationDelegate save)
    {
        ArgumentNullException.ThrowIfNull(save);
        lock (_initializationLock)
        {
            Initializations[StateInitializationStage.ShutDownSave].Remove(save);
        }
    }

    public StateInitializationStage CurrentStage { get; private set; }
}
