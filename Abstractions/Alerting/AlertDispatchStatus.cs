namespace HomeCompanion.Alerting;

/// <summary>
/// Overall result classification for alert dispatch.
/// </summary>
public enum AlertDispatchStatus
{
    /// <summary>
    /// Alerting integration is disabled.
    /// </summary>
    Disabled,

    /// <summary>
    /// Request is rejected before dispatch.
    /// </summary>
    Rejected,

    /// <summary>
    /// All selected paths succeeded.
    /// </summary>
    Succeeded,

    /// <summary>
    /// At least one selected path succeeded and at least one failed.
    /// </summary>
    PartiallySucceeded,

    /// <summary>
    /// No selected path succeeded.
    /// </summary>
    Failed,
}
