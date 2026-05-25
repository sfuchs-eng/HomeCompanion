namespace HomeCompanion.Alerting;

/// <summary>
/// Service for sending user alerts and controlling named-alert state transitions.
/// </summary>
public interface IAlertingService
{
    /// <summary>
    /// Dispatches a fire-and-forget alert request.
    /// </summary>
    /// <param name="request">Alert request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregated dispatch result.</returns>
    Task<AlertDispatchResult> SendAsync(AlertRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a named-alert intent and returns the transition result.
    /// </summary>
    /// <param name="intent">Named-alert lifecycle intent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Transition result and latest state snapshot.</returns>
    Task<NamedAlertTransitionResult> HandleNamedAlertIntentAsync(NamedAlertIntent intent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns current named-alert snapshot values.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current named alerts.</returns>
    Task<IReadOnlyCollection<NamedAlertState>> GetNamedAlertsSnapshotAsync(CancellationToken cancellationToken = default);
}
