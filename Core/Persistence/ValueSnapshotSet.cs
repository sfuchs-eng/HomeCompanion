namespace HomeCompanion.Core.Persistence;

/// <summary>
/// Snapshot root persisted by <see cref="StateInitializationManager"/> to bridge short restart intervals.
/// </summary>
public sealed class ValueSnapshotSet
{
    /// <summary>
    /// Schema version of this snapshot payload.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// UTC timestamp when this snapshot set was created.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>
    /// Stored value entries keyed by deterministic container/property key.
    /// </summary>
    public Dictionary<string, ValueSnapshotEntry> Values { get; set; } = [];
}

/// <summary>
/// Persisted state entry for a single <see cref="HomeCompanion.Values.IValue"/>.
/// </summary>
public sealed class ValueSnapshotEntry
{
    /// <summary>
    /// Deterministic key for this value snapshot entry.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Optional logical name of the value for fallback mapping.
    /// </summary>
    public string? ValueName { get; set; }

    /// <summary>
    /// Optional display label of the value.
    /// </summary>
    public string? ValueLabel { get; set; }

    /// <summary>
    /// Runtime value type as assembly-qualified type name.
    /// </summary>
    public string? ValueType { get; set; }

    /// <summary>
    /// JSON payload of the current value.
    /// </summary>
    public string PayloadJson { get; set; } = "null";

    /// <summary>
    /// UTC timestamp when this entry was captured.
    /// </summary>
    public DateTimeOffset CapturedUtc { get; set; }
}
