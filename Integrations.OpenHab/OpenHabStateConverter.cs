using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using SRF.Knx.Config;
using SRF.Knx.Core;
using SRF.Knx.Core.DPT;
using SRF.Knx.Core.Master;

namespace HomeCompanion.Integrations.OpenHab;

/// <summary>
/// Converts OpenHAB item state strings to typed CLR values using KNX DPT semantics and master data.
/// Supports automatic conversion based on FormatElement metadata, with fallback to generic parsing.
/// To be reconsidered: is this an OpenHAB-specific converter or rather a KNX specific one? It's rather about converting from bus representation to internal values, hence OpenHAB in this case even though there are KNX semantics behind.
/// </summary>
public class OpenHabStateConverter
{
    private readonly IKnxSystemConfiguration _knxConfig;
    private readonly IKnxMasterDataProvider _masterDataProvider;
    private readonly ILogger<OpenHabStateConverter> _logger;

    public OpenHabStateConverter(
        IKnxSystemConfiguration knxConfig,
        IKnxMasterDataProvider masterDataProvider,
        ILogger<OpenHabStateConverter> logger)
    {
        _knxConfig = knxConfig;
        _masterDataProvider = masterDataProvider;
        _logger = logger;
    }

    /// <summary>
    /// Attempts to convert an OpenHAB state string to a typed value using KNX DPT metadata.
    /// </summary>
    /// <param name="stateString">The OpenHAB item state string to convert.</param>
    /// <param name="value">The IValue with bus mappings that might include KNX group address mapping.</param>
    /// <param name="convertedValue">The converted value on success; null on failure.</param>
    /// <returns>True if conversion succeeded; false if no KNX mapping or conversion failed.</returns>
    public bool TryConvertValue(
        string stateString,
        IValue value,
        out object? convertedValue)
    {
        convertedValue = null;

        // Try to get KNX bus mapping from the value
        // If value has KNX mapping, we can do DPT-aware conversion
        // KnxBusEndpointMapping is resolved via reflection to avoid direct dependency
        var knxMapping = TryGetKnxBusMapping(value);
        if (knxMapping is null)
            return false;

        try
        {
            // Resolve DPT for the group address using reflection
            var knxMappingAddress = GetGroupAddressFromMapping(knxMapping);
            if (knxMappingAddress is null)
                return false;

            var dpt = _knxConfig.GetDpt((SRF.Knx.Core.GroupAddress)knxMappingAddress);
            var dptId = dpt.Id;

            // Resolve DPT metadata from master data
            var masterData = _masterDataProvider.GetMasterData();
            var dptMetadata = DptMetadata.FromMasterData(dptId, masterData);

            // Get the format specification from DatapointSubtype or parent DatapointType
            Format? format = dptMetadata.Dpst?.Format ?? dptMetadata.Dpt?.DatapointSubtypes?.DatapointSubtype.FirstOrDefault()?.Format;
            if (format is null)
            {
                _logger.LogDebug(
                    "No format specification found for DPT {DptId}. Falling back to generic parsing.",
                    dptId);
                return false;
            }

            // Dispatch to type-specific converter based on FormatElement type
            convertedValue = ConvertByFormatElement(stateString, format.Elements, value.ValueType);
            return convertedValue is not null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DPT-aware conversion failed for state '{State}'. Falling back to generic parsing.", stateString);
            return false;
        }
    }

    /// <summary>
    /// Converts a state string using the first applicable FormatElement found in the format list.
    /// </summary>
    private object? ConvertByFormatElement(string stateString, List<FormatElement> elements, Type targetType)
    {
        // Find the primary format element (non-reserved, non-reftype elements)
        var primaryElement = elements.FirstOrDefault(e => e is not ReservedFormat and not RefTypeFormat);
        if (primaryElement is null)
            return null;

        return primaryElement switch
        {
            EnumerationFormat enumFormat => ConvertEnumerationValue(stateString, enumFormat),
            BitFormat bitFormat => ConvertBitValue(stateString, bitFormat),
            UnsignedIntegerFormat uintFormat => ConvertNumericValue(stateString, uintFormat, signed: false),
            SignedIntegerFormat sintFormat => ConvertNumericValue(stateString, sintFormat, signed: true),
            FloatFormat floatFormat => ConvertFloatValue(stateString, floatFormat),
            StringFormat => stateString, // Return string as-is
            _ => null,
        };
    }

    /// <summary>
    /// Converts an OpenHAB state string to an enumeration value using case-insensitive text matching.
    /// </summary>
    private object? ConvertEnumerationValue(string stateString, EnumerationFormat enumFormat)
    {
        foreach (var enumValue in enumFormat.EnumValues)
        {
            if (string.Equals(stateString, enumValue.Text, StringComparison.OrdinalIgnoreCase))
                return enumValue.Value;
        }

        _logger.LogDebug(
            "State '{State}' did not match any enum value in enumeration format. Available: {Values}",
            stateString,
            string.Join(", ", enumFormat.EnumValues.Select(e => e.Text)));

        return null;
    }

    /// <summary>
    /// Converts an OpenHAB state string to a boolean value using BitFormat Set/Cleared labels.
    /// Case-insensitive matching against semantic labels from master data.
    /// </summary>
    private object? ConvertBitValue(string stateString, BitFormat bitFormat)
    {
        // Try to match against "Set" label (represents true/1)
        if (!string.IsNullOrEmpty(bitFormat.Set) && 
            string.Equals(stateString, bitFormat.Set, StringComparison.OrdinalIgnoreCase))
            return true;

        // Try to match against "Cleared" label (represents false/0)
        if (!string.IsNullOrEmpty(bitFormat.Cleared) && 
            string.Equals(stateString, bitFormat.Cleared, StringComparison.OrdinalIgnoreCase))
            return false;

        // Try common OpenHAB boolean representations
        return stateString switch
        {
            "ON" or "on" or "true" or "True" or "TRUE" => true,
            "OFF" or "off" or "false" or "False" or "FALSE" => false,
            "OPEN" or "open" => true,   // For contact/door sensors (often inverted)
            "CLOSED" or "closed" => false,
            _ => null,
        };
    }

    /// <summary>
    /// Converts an OpenHAB state string to a numeric value (integer or unsigned) with coefficient scaling.
    /// </summary>
    private object? ConvertNumericValue(string stateString, NumericFormat numericFormat, bool signed)
    {
        // Parse the string as a double to handle both int and float representations
        if (!double.TryParse(stateString, System.Globalization.CultureInfo.InvariantCulture, out double parsedValue))
        {
            _logger.LogDebug("Failed to parse numeric state '{State}' as double.", stateString);
            return null;
        }

        // Apply coefficient scaling
        double scaledValue = parsedValue * numericFormat.Coefficient;

        // Cast to appropriate integer type based on width
        return numericFormat switch
        {
            UnsignedIntegerFormat u => u.Width switch
            {
                <= 8 => (byte)scaledValue,
                <= 16 => (ushort)scaledValue,
                <= 32 => (uint)scaledValue,
                <= 64 => (ulong)scaledValue,
                _ => null,
            },
            SignedIntegerFormat s => s.Width switch
            {
                <= 8 => (sbyte)scaledValue,
                <= 16 => (short)scaledValue,
                <= 32 => (int)scaledValue,
                <= 64 => (long)scaledValue,
                _ => null,
            },
            _ => null,
        };
    }

    /// <summary>
    /// Converts an OpenHAB state string to a floating-point value with coefficient scaling.
    /// </summary>
    private object? ConvertFloatValue(string stateString, FloatFormat floatFormat)
    {
        if (!double.TryParse(stateString, System.Globalization.CultureInfo.InvariantCulture, out double parsedValue))
        {
            _logger.LogDebug("Failed to parse float state '{State}' as double.", stateString);
            return null;
        }

        double scaledValue = parsedValue * floatFormat.Coefficient;

        // FloatFormat uses PreferredCSharpType which is float
        return (float)scaledValue;
    }

    /// <summary>
    /// Attempts to get the KNX bus mapping from an IValue using reflection.
    /// This avoids a hard dependency on HomeCompanion.Integrations.Knx in the OpenHab integration.
    /// </summary>
    private object? TryGetKnxBusMapping(IValue value)
    {
        try
        {
            // KnxBusEndpointMapping.BusId = "knx"
            if (value.BusMappings.TryGetValue("knx", out var mapping))
                return mapping;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to retrieve KNX bus mapping from value.");
        }

        return null;
    }

    /// <summary>
    /// Extracts the GroupAddress from a KnxBusEndpointMapping using reflection.
    /// </summary>
    private object? GetGroupAddressFromMapping(object mapping)
    {
        try
        {
            // Get the Address property (which is a GroupAddress for KnxBusEndpointMapping)
            var addressProperty = mapping.GetType().GetProperty("Address");
            if (addressProperty is not null)
                return addressProperty.GetValue(mapping);

            // Alternative: try GroupAddress property directly
            var groupAddressProperty = mapping.GetType().GetProperty("GroupAddress");
            if (groupAddressProperty is not null)
                return groupAddressProperty.GetValue(mapping);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract GroupAddress from KNX mapping.");
        }

        return null;
    }
}

