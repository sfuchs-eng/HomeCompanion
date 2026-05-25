namespace HomeCompanion.Alerting;

/// <summary>
/// Severity level of an alert message.
/// </summary>
public enum AlertSeverity
{
    /// <summary>
    /// Diagnostic detail for development and troubleshooting.
    /// </summary>
    Debug,

    /// <summary>
    /// Informational user message.
    /// </summary>
    Info,

    /// <summary>
    /// Warning condition that may require attention.
    /// </summary>
    Warning,

    /// <summary>
    /// Critical condition that needs immediate attention.
    /// </summary>
    Critical,

    /// <summary>
    /// Highest priority emergency condition.
    /// </summary>
    Emergency,
}
