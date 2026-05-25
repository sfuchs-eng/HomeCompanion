namespace HomeCompanion.Alerting;

/// <summary>
/// Result of applying one named-alert intent.
/// </summary>
public sealed class NamedAlertTransitionResult
{
    /// <summary>
    /// Alert key that was processed.
    /// </summary>
    public required string AlertKey { get; init; }

    /// <summary>
    /// Status before the transition.
    /// </summary>
    public NamedAlertStatus PreviousStatus { get; init; }

    /// <summary>
    /// Status after the transition.
    /// </summary>
    public NamedAlertStatus CurrentStatus { get; init; }

    /// <summary>
    /// True if state changed.
    /// </summary>
    public bool StateChanged { get; init; }

    /// <summary>
    /// Applied intent type.
    /// </summary>
    public NamedAlertIntentType IntentType { get; init; }

    /// <summary>
    /// Latest state snapshot.
    /// </summary>
    public required NamedAlertState State { get; init; }
}
