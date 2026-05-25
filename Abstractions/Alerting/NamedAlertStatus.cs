namespace HomeCompanion.Alerting;

/// <summary>
/// Current lifecycle status of a named alert.
/// </summary>
public enum NamedAlertStatus
{
    /// <summary>
    /// No active alert, normal monitoring mode.
    /// </summary>
    Monitoring,

    /// <summary>
    /// Active alert requiring user attention.
    /// </summary>
    Alert,

    /// <summary>
    /// Active alert was acknowledged by user.
    /// </summary>
    Acknowledged,

    /// <summary>
    /// Alert was disabled by user control.
    /// </summary>
    Disabled,
}
