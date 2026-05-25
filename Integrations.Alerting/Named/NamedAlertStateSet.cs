using HomeCompanion.Alerting;

namespace HomeCompanion.Integrations.Alerting.Named;

/// <summary>
/// Serializable state-set payload for named-alert persistence.
/// </summary>
public sealed class NamedAlertStateSet
{
    /// <summary>
    /// Serialization format version.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Timestamp when this snapshot was saved.
    /// </summary>
    public DateTimeOffset SavedUtc { get; set; }

    /// <summary>
    /// Named alert entries.
    /// </summary>
    public List<NamedAlertState> Alerts { get; set; } = [];
}
