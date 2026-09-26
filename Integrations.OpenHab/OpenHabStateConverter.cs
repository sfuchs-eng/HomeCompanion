using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using SRF.Network.OpenHab.Items;

namespace HomeCompanion.Integrations.OpenHab;

/// <summary>
/// Converts OpenHAB item state strings to typed CLR values using OpenHAB-native state semantics.
/// </summary>
public class OpenHabStateConverter
{
    private readonly OpenHabTypeConversionRegistry _registry;
    private readonly ILogger<OpenHabStateConverter> _logger;

    public OpenHabStateConverter(
        OpenHabTypeConversionRegistry registry,
        ILogger<OpenHabStateConverter> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Attempts to convert an OpenHAB state string to a typed value using OpenHAB-native type metadata.
    /// </summary>
    public bool TryConvertValue(string stateString, IValue value, out object? convertedValue)
        => TryConvertValue(stateString, value, stateType: null, itemMetadata: null, out convertedValue);

    /// <summary>
    /// Attempts to convert an OpenHAB state string to a typed value using explicit state type and cached item metadata when available.
    /// </summary>
    public bool TryConvertValue(string stateString, IValue value, string? stateType, Item? itemMetadata, out object? convertedValue)
    {
        convertedValue = null;

        OpenHabBusMappingConfiguration? localConfig = null;
        if (value.TryGetBusEndpoint<OpenHabBusEndpointMapping>(OpenHabBusEndpointMapping.BusId, out var mapping))
            localConfig = mapping?.Config;

        if (_registry.TryConvertValue(stateString, value, stateType, itemMetadata, localConfig, out convertedValue))
            return true;

        if (value.TryParseValue(stateString, out var parsedValue, out _, System.Globalization.CultureInfo.InvariantCulture))
        {
            convertedValue = parsedValue;
            return true;
        }

        _logger.LogDebug("OpenHAB state conversion failed for value '{ValueName}' with state '{State}' and state type '{StateType}'.", value.Name, stateString, stateType);

        return false;
    }
}

