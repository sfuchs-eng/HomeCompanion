using System.Text.Json.Serialization;

namespace HomeCompanion.Knx.Shared;

/// <summary>
/// A single entry in <c>HomeCompanionKnxAutoGen.json</c>, mapping a KNX group address to its
/// generated C# property name and KNX DPT.
/// </summary>
public class HomeCompanionAutoGenEntry
{
    /// <summary>The C# property name to emit on <c>KnxValues</c>.</summary>
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>
    /// KNX Data Point Type string (e.g. <c>"DPT-9"</c>, <c>"DPST-9-1"</c>),
    /// or <see langword="null"/> if unset.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Dpt { get; set; }
}
