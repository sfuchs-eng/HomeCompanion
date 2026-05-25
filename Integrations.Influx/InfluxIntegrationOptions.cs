using System.ComponentModel.DataAnnotations;

namespace HomeCompanion.Integrations.Influx;

/// <summary>
/// Configuration options for internal signal persistence to InfluxDB OSS v2.
/// </summary>
public sealed class InfluxIntegrationOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Influx";

    /// <summary>
    /// InfluxDB server URL.
    /// </summary>
    [Required]
    public string Url { get; init; } = string.Empty;

    /// <summary>
    /// InfluxDB organization.
    /// </summary>
    [Required]
    public string Organization { get; init; } = string.Empty;

    /// <summary>
    /// InfluxDB API token.
    /// </summary>
    [Required]
    public string Token { get; init; } = string.Empty;

    /// <summary>
    /// Default bucket for measurements without explicit override.
    /// </summary>
    [Required]
    public string DefaultBucket { get; init; } = string.Empty;

    /// <summary>
    /// Periodic flush interval in seconds.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int FlushIntervalSeconds { get; init; } = 10;

    /// <summary>
    /// Maximum buffered measurements before immediate flush.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxQueueSize { get; init; } = 500;

    /// <summary>
    /// Number of retry attempts for failed flushes.
    /// </summary>
    [Range(0, 100)]
    public int RetryCount { get; init; } = 3;

    /// <summary>
    /// Delay in seconds between flush retries.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int RetryDelaySeconds { get; init; } = 2;
}
