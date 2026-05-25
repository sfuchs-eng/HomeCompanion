namespace HomeCompanion.Alerting;

/// <summary>
/// Request object for fire-and-forget alert dispatch.
/// </summary>
public sealed class AlertRequest
{
    /// <summary>
    /// Severity used for channel/path mapping.
    /// </summary>
    public AlertSeverity Severity { get; init; }

    /// <summary>
    /// Short user-facing message.
    /// </summary>
    public required string MessageShort { get; init; }

    /// <summary>
    /// Optional detailed message body.
    /// </summary>
    public string? MessageLong { get; init; }

    /// <summary>
    /// Optional named-alert key related to this message.
    /// </summary>
    public string? AlertKey { get; init; }

    /// <summary>
    /// Optional correlation id for tracing across systems.
    /// </summary>
    public Guid? CorrelationId { get; init; }

    /// <summary>
    /// Optional metadata attached to the alert.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Optional recipient override used by user-info style email alerts.
    /// </summary>
    public IReadOnlyList<string>? RecipientOverride { get; init; }

    /// <summary>
    /// Marks this request as a user-info message that must specify recipients.
    /// </summary>
    public bool IsUserInfo { get; init; }
}
