using HomeCompanion.Values;
using System.Text;

namespace HomeCompanion.Integrations.OpenHab;

public enum OpenHabOutboundQuantityUnitMode
{
    PreserveQuantityUnit,
    MappingConfiguredUnit,
}

/// <summary>
/// OpenHAB-specific mapping configuration for one value endpoint.
/// </summary>
public class OpenHabBusMappingConfiguration : IBusMappingConfiguration
{
    /// <summary>
    /// Optional OpenHAB item type hint such as Switch, Contact, Number or Number:Temperature.
    /// </summary>
    public string? ItemType { get; init; }

    /// <summary>
    /// Optional OpenHAB state type hint such as OnOff, OpenClosed, Decimal or Quantity.
    /// </summary>
    public string? StateType { get; init; }

    /// <summary>
    /// Optional raw-state to CLR-literal mapping applied before target parsing.
    /// </summary>
    public Dictionary<string, string> LiteralMappings { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional custom true literals for boolean targets.
    /// </summary>
    public List<string> TrueLiterals { get; init; } = [];

    /// <summary>
    /// Optional custom false literals for boolean targets.
    /// </summary>
    public List<string> FalseLiterals { get; init; } = [];

    /// <summary>
    /// Controls outbound quantity formatting for quantity targets.
    /// </summary>
    public OpenHabOutboundQuantityUnitMode OutboundQuantityUnitMode { get; init; } = OpenHabOutboundQuantityUnitMode.MappingConfiguredUnit;

    /// <inheritdoc/>
    public string? ValueFormat { get; init; }

    /// <inheritdoc/>
    public string? FormatConfiguration()
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(ItemType))
            builder.Append($"item={ItemType}");

        if (!string.IsNullOrWhiteSpace(StateType))
        {
            if (builder.Length > 0)
                builder.Append(", ");
            builder.Append($"state={StateType}");
        }

        if (LiteralMappings.Count > 0)
        {
            if (builder.Length > 0)
                builder.Append(", ");
            builder.Append($"literals={LiteralMappings.Count}");
        }

        if (TrueLiterals.Count > 0 || FalseLiterals.Count > 0)
        {
            if (builder.Length > 0)
                builder.Append(", ");
            builder.Append("bool-literals");
        }

        if (builder.Length > 0)
            builder.Append(", ");
        builder.Append($"quantity={OutboundQuantityUnitMode}");

        return builder.ToString();
    }
}

/// <summary>
/// Shared registry rule loaded from configuration file.
/// </summary>
public sealed class OpenHabTypeMappingDefinition : OpenHabBusMappingConfiguration
{
    /// <summary>
    /// Optional OpenHAB item name selector for exact matching (case-insensitive).
    /// </summary>
    public string? ItemName { get; init; }

    /// <summary>
    /// Optional OpenHAB item name selector as pattern (wildcard or regex).
    /// </summary>
    public string? ItemNamePattern { get; init; }

    /// <summary>
    /// Optional OpenHAB item type selector as pattern (wildcard or regex).
    /// </summary>
    public string? ItemTypePattern { get; init; }

    /// <summary>
    /// Optional override for generated value type, e.g. "double" or "global::UnitsNet.Temperature".
    /// </summary>
    public string? GeneratedValueType { get; init; }

    /// <summary>
    /// Optional quantity name for generated value unit metadata, e.g. "Temperature".
    /// Requires <see cref="GeneratedUnitName"/> to be set as well.
    /// </summary>
    public string? GeneratedUnitQuantityName { get; init; }

    /// <summary>
    /// Optional unit name for generated value unit metadata, e.g. "DegreeCelsius".
    /// Requires <see cref="GeneratedUnitQuantityName"/> to be set as well.
    /// </summary>
    public string? GeneratedUnitName { get; init; }

    /// <summary>
    /// Optional display symbol for generated value unit metadata, e.g. "°C".
    /// </summary>
    public string? GeneratedUnitSymbol { get; init; }

    /// <summary>
    /// Optional CLR target type selector.
    /// Supports aliases such as bool, int, long, double, string and full CLR type names.
    /// </summary>
    public string? TargetType { get; init; }
}

public sealed class OpenHabTypeMappingFile
{
    public List<OpenHabTypeMappingDefinition> Mappings { get; init; } = [];
}