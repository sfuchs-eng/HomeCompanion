using HomeCompanion.Alerting;
using HomeCompanion.Persistence;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Integrations.Alerting.Named;

/// <summary>
/// Persists and restores named-alert state snapshots.
/// </summary>
public sealed class NamedAlertPersistenceAdapter
{
    private const string StateSetName = "named-alerts";

    private readonly IStateStore _stateStore;
    private readonly NamedAlertStateMachine _stateMachine;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NamedAlertPersistenceAdapter> _logger;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public NamedAlertPersistenceAdapter(
        IStateStore stateStore,
        NamedAlertStateMachine stateMachine,
        TimeProvider timeProvider,
        ILogger<NamedAlertPersistenceAdapter> logger)
    {
        _stateStore = stateStore;
        _stateMachine = stateMachine;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Loads persisted named-alert states.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var result = await _stateStore.LoadAsync<NamedAlertStateSet>(StateSetName);
        if (!result.IsSuccess)
        {
            _logger.LogInformation("No named-alert state available in state set '{StateSetName}'.", StateSetName);
            return;
        }

        _stateMachine.Restore(result.StateSet.Alerts);

        _logger.LogInformation(
            "Restored {AlertCount} named-alert states from state set '{StateSetName}'.",
            result.StateSet.Alerts.Count,
            StateSetName);
    }

    /// <summary>
    /// Saves current named-alert states.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task.</returns>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = _stateMachine.GetSnapshot();
        var stateSet = new NamedAlertStateSet
        {
            Version = 1,
            SavedUtc = _timeProvider.GetUtcNow(),
            Alerts = [.. snapshot],
        };

        await _stateStore.SaveAsync(StateSetName, stateSet, cancellationToken);

        _logger.LogInformation(
            "Saved {AlertCount} named-alert states to state set '{StateSetName}'.",
            stateSet.Alerts.Count,
            StateSetName);
    }
}
