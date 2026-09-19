using HomeCompanion.Values;
using SRF.Knx.Core;
using SRF.Knx.Core.DPT;
using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace HomeCompanion.Integrations.Knx;

/// <summary>
/// Maps an <see cref="IValue"/> to a KNX group address.
/// </summary>
/// <remarks>
/// Add this mapping to <see cref="IValue.BusMappings"/> under the key <see cref="BusId"/> to register
/// a value as a KNX-backed data point. The KNX connectivity provider discovers values with this mapping
/// at startup and builds the group address ↔ value index.
/// </remarks>
public sealed class KnxBusEndpointMapping : ValueBusMapping<string, GroupAddress>
{
    private readonly IDptFactory? _dptFactory;

    /// <summary>
    /// The bus identifier used as the dictionary key in <see cref="IValue.BusMappings"/> for KNX mappings.
    /// </summary>
    public static readonly string BusId = "knx";

    /// <summary>The KNX group address this value is mapped to.</summary>
    public GroupAddress GroupAddress => (GroupAddress)Address;

    /// <inheritdoc/>
    public override bool CanFormatValueForDisplay => true;

    /// <param name="groupAddress">KNX group address in <c>"main/middle/sub"</c> format.</param>
    /// <param name="DPTs">Data point type(s) for the KNX group address.</param>
    public KnxBusEndpointMapping(string groupAddress, string DPTs, IDptFactory? dptFactory = null)
        : base(BusId, new GroupAddress(groupAddress), new KnxBusMappingConfiguration(DPTs))
    {
        _dptFactory = dptFactory;
    }

    /// <param name="groupAddress">KNX group address.</param>
    public KnxBusEndpointMapping(GroupAddress groupAddress, string DPTs, IDptFactory? dptFactory = null)
        : base(BusId, groupAddress, new KnxBusMappingConfiguration(DPTs))
    {
        _dptFactory = dptFactory;
    }

    /// <inheritdoc/>
    public override string? FormatValueForDisplay(object? value, CultureInfo? culture = null, IFormatProvider? formatProvider = null, string? format = null)
    {
        if (value is null)
            return null;

        if (Config is not KnxBusMappingConfiguration knxConfig || _dptFactory is null)
            return base.FormatValueForDisplay(value, culture, formatProvider, format);

        try
        {
            var dpt = _dptFactory.Get(knxConfig.DPT);
            var displayValue = value;
            if (TryConvertScalarToDptQuantity(value, dpt, out var convertedDisplayValue))
                displayValue = convertedDisplayValue;

            var groupValue = dpt.ToGroupValue(displayValue);
            var effectiveFormatProvider = formatProvider ?? culture ?? CultureInfo.CurrentCulture;
            return dpt.Format(groupValue, culture?.TwoLetterISOLanguageName, effectiveFormatProvider, format);
        }
        catch
        {
            return base.FormatValueForDisplay(value, culture, formatProvider, format);
        }
    }

    private static bool TryConvertScalarToDptQuantity(object value, DptBase dpt, out object converted)
    {
        converted = value;

        if (!IsNumericScalar(value) || !typeof(UnitsNet.IQuantity).IsAssignableFrom(dpt.ApplicationType))
            return false;

        var unitProperty = dpt.GetType().GetProperty("KnxUnit");
        if (unitProperty?.GetValue(dpt) is not Enum knxUnit)
            return false;

        try
        {
            var magnitude = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            var quantity = UnitsNet.Quantity.From(magnitude, knxUnit);
            if (!dpt.ApplicationType.IsInstanceOfType(quantity))
                return false;

            converted = quantity;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsNumericScalar(object value)
        => value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
}

internal class KnxBusMappingConfiguration : IBusMappingConfiguration
{
    public DataPointTypeId DPT { get; init; }

    public KnxBusMappingConfiguration(string DPTs)
    {
        DPT = new DataPointTypeId(DPTs);
    }

    public string? FormatConfiguration()
    {
        return DPT.EtsFormat;
    }

    public string? ValueFormat => null;
}
