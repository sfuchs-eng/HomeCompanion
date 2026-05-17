using HomeCompanion.Values;
using SRF.Knx.Core;
using SRF.Knx.Core.DPT;
using System.Collections;
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
    /// <summary>
    /// The bus identifier used as the dictionary key in <see cref="IValue.BusMappings"/> for KNX mappings.
    /// </summary>
    public static readonly string BusId = "knx";

    /// <summary>The KNX group address this value is mapped to.</summary>
    public GroupAddress GroupAddress => (GroupAddress)Address;

    /// <param name="groupAddress">KNX group address in <c>"main/middle/sub"</c> format.</param>
    /// <param name="DPTs">Data point type(s) for the KNX group address.</param>
    public KnxBusEndpointMapping(string groupAddress, string DPTs) : base(BusId, new GroupAddress(groupAddress), new KnxBusMappingConfiguration(DPTs)) { }

    /// <param name="groupAddress">KNX group address.</param>
    public KnxBusEndpointMapping(GroupAddress groupAddress, string DPTs) : base(BusId, groupAddress, new KnxBusMappingConfiguration(DPTs)) { }
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
