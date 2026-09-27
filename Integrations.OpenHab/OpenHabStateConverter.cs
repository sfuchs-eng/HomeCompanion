using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using SRF.Network.OpenHab.Items;

namespace HomeCompanion.Integrations.OpenHab;

/// <summary>
/// Converts OpenHAB item state strings to typed CLR values using OpenHAB-native state semantics.
/// TODO: replace entirely by <see cref="OpenHabTypeConversionRegistry"/> and remove this class.
/// </summary>
public class OpenHabStateConverter(
    OpenHabTypeConversionRegistry registry,
    ILogger<OpenHabStateConverter> logger)
{
    private readonly OpenHabTypeConversionRegistry _registry = registry;
    private readonly ILogger<OpenHabStateConverter> _logger = logger;

    /// <summary>
    /// Attempts to convert an OpenHAB state string to a typed value using OpenHAB-native type metadata.
    /// </summary>
    public bool TryConvertValue(string stateString, IValue value, out object? convertedValue)
        => TryConvertValue(stateString, value, stateType: null, itemMetadata: null, out convertedValue);

    /// <summary>
    /// Attempts to convert an OpenHAB state string to a typed value using explicit state type and cached item metadata when available.
    /// </summary>
    public bool TryConvertValue(string stateString, IValue value, string? stateType, Item? itemMetadata, out object? convertedValue)
        => _registry.TryConvertValue(stateString, value, stateType, itemMetadata, localConfig: null, out convertedValue);
}
