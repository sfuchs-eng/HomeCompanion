namespace HomeCompanion.Alerting;

/// <summary>
/// Intent request for named-alert lifecycle handling.
/// </summary>
public sealed class NamedAlertIntent
{
    /// <summary>
    /// Stable key of the named alert.
    /// </summary>
    public required string AlertKey { get; init; }

    /// <summary>
    /// Lifecycle intent to apply.
    /// </summary>
    public NamedAlertIntentType IntentType { get; init; }

    /// <summary>
    /// Optional message associated with this intent.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Optional correlation id for tracing.
    /// </summary>
    public Guid? CorrelationId { get; init; }

    /// <summary>
    /// Optional metadata associated with this intent.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
