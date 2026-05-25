namespace HomeCompanion.Alerting;

/// <summary>
/// Dispatch result for one alert path.
/// </summary>
public sealed class AlertPathDispatchResult
{
    /// <summary>
    /// Path that was attempted.
    /// </summary>
    public AlertPath Path { get; init; }

    /// <summary>
    /// Indicates path-level success.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Optional human-readable result message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Optional error code if path failed.
    /// </summary>
    public string? ErrorCode { get; init; }
}
