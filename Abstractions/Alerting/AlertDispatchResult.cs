namespace HomeCompanion.Alerting;

/// <summary>
/// Aggregated dispatch result for one alert request.
/// </summary>
public sealed class AlertDispatchResult
{
    /// <summary>
    /// Overall dispatch status.
    /// </summary>
    public AlertDispatchStatus Status { get; init; }

    /// <summary>
    /// Path-level outcomes.
    /// </summary>
    public IReadOnlyList<AlertPathDispatchResult> PathResults { get; init; } = [];

    /// <summary>
    /// Optional summary message.
    /// </summary>
    public string? Message { get; init; }
}
