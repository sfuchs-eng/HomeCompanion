namespace HomeCompanion.Persistence;

/// <summary>
/// Transport-neutral representation of an internal signal measurement.
/// </summary>
public sealed class InternalSignalMeasurement
{
    /// <summary>
    /// Name of the measurement series.
    /// </summary>
    public required string Measurement { get; init; }

    /// <summary>
    /// Timestamp of the measurement.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional tags used for series dimensions.
    /// </summary>
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Scalar fields that represent measured values.
    /// </summary>
    public IReadOnlyDictionary<string, object> Fields { get; init; } = new Dictionary<string, object>();

    /// <summary>
    /// Optional bucket override. If null or empty, the default configured bucket is used.
    /// </summary>
    public string? BucketOverride { get; init; }
}
