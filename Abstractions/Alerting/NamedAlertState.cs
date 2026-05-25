namespace HomeCompanion.Alerting;

/// <summary>
/// Current state of one named alert.
/// </summary>
/// <param name="AlertKey">Stable alert key.</param>
/// <param name="Status">Current lifecycle status.</param>
/// <param name="LastChangeUtc">Timestamp of the latest status update.</param>
/// <param name="LastMessage">Latest associated message.</param>
/// <param name="NextReminderDueUtc">Next reminder due timestamp when in alert status.</param>
public sealed record NamedAlertState(
    string AlertKey,
    NamedAlertStatus Status,
    DateTimeOffset LastChangeUtc,
    string? LastMessage,
    DateTimeOffset? NextReminderDueUtc);
