using System.Text.Json.Serialization;
using SRF.Knx.Config;

namespace HomeCompanion.Support.Knx;

/// <summary>
/// A single entry in <c>HomeCompanionKnxAutoGen.json</c>, mapping a KNX group address to its
/// generated C# property name and KNX DPT.
/// </summary>
public class KnxValueConfiguration
{
    /// <summary>The C# property name to emit on <c>KnxValues</c>.</summary>
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable label for the property's XML doc summary, if set.
    /// Typically derived from the ETS export XML label for the group address.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Human-readable description for the property's XML doc remarks, if set.
    /// Typically derived from the ETS export XML description for the group address.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// KNX Data Point Type string (e.g. <c>"DPT-9"</c>, <c>"DPST-9-1"</c>),
    /// or <see langword="null"/> if unset.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Dpt { get; set; }

    /// <summary>
    /// Communication flags for the KNX bus endpoint, if set.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public KnxObjectBusCommunication Communication { get; set; }

    /// <summary>
    /// Optional OpenHAB item name for mapping the generated <c>IValue</c> not only to the KNX bus but also to OpenHAB's item registry, enabling state initialization from OpenHAB and potentially other OpenHAB bus mappings.
    /// Make sure to prevent circular loops via KNX Group Address communication by configuring the appropriate <see cref="Communication"/> flags. Defaults should be ok.
    /// </summary>
    public string? OpenHabItemName { get; set; }

    /// <summary>
    /// Whether the generated code should attempt to initialize the value from OpenHAB on startup.
    /// Only has an effect if <see cref="OpenHabItemName"/> is set.
    /// </summary>
    public bool WantsOpenHabInitialization { get; set; } = true;

    /// <summary>
    /// Whether the generated code should attempt to initialize the value from OpenHAB on startup.
    /// Only has an effect if <see cref="OpenHabItemName"/> is set and <see cref="WantsOpenHabInitialization"/> is <see langword="true"/>.
    /// If both is the case, the <c>IValue</c> gets an additional OpenHab bus mapping making it eligible for OpenHAB state initialization.
    /// </summary>
    public bool IsOpenHabInitialized => OpenHabItemName != null && WantsOpenHabInitialization;

    [JsonIgnore()]
    public bool IsUnitAware => !string.IsNullOrWhiteSpace(Unit) && !string.IsNullOrWhiteSpace(Dimension);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Dimension { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Unit { get; set; }
}
