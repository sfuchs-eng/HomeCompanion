namespace HomeCompanion.Alerting;

/// <summary>
/// Intent sent to the named-alert state machine.
/// </summary>
public enum NamedAlertIntentType
{
    /// <summary>
    /// Trigger an alert condition.
    /// </summary>
    Trigger,

    /// <summary>
    /// Reset the alert condition and return to monitoring.
    /// </summary>
    Reset,

    /// <summary>
    /// Mark an alert as acknowledged by user.
    /// </summary>
    Acknowledge,

    /// <summary>
    /// Disable a named alert by user control.
    /// </summary>
    Disable,

    /// <summary>
    /// Enable a previously disabled named alert.
    /// </summary>
    Enable,
}
