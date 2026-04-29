using HomeCompanion.Base.Values;
using SRF.Knx.Core;
using System.Collections;

namespace HomeCompanion.Knx;

/// <summary>
/// Maps an <see cref="IValue"/> to a KNX group address.
/// </summary>
/// <remarks>
/// Add this mapping to <see cref="IValue.BusMappings"/> under the key <see cref="BusId"/> to register
/// a value as a KNX-backed data point. The KNX connectivity provider discovers values with this mapping
/// at startup and builds the group address ↔ value index.
/// </remarks>
public sealed class KnxBusEndpointMapping : IValueBusEndpointMapping
{
    /// <summary>
    /// The bus identifier used as the dictionary key in <see cref="IValue.BusMappings"/> for KNX mappings.
    /// </summary>
    public static readonly string BusId = "knx";

    /// <summary>The KNX group address this value is mapped to.</summary>
    public GroupAddress GroupAddress { get; }

    /// <param name="groupAddress">KNX group address in <c>"main/middle/sub"</c> format.</param>
    public KnxBusEndpointMapping(string groupAddress) => GroupAddress = new GroupAddress(groupAddress);

    /// <param name="groupAddress">KNX group address.</param>
    public KnxBusEndpointMapping(GroupAddress groupAddress) => GroupAddress = groupAddress;

    bool IEqualityComparer.Equals(object? x, object? y)
    {
        if (x is KnxBusEndpointMapping mx && y is KnxBusEndpointMapping my)
            return mx.GroupAddress == my.GroupAddress;
        return false;
    }

    int IEqualityComparer.GetHashCode(object obj)
        => obj is KnxBusEndpointMapping m ? m.GroupAddress.GetHashCode() : 0;
}
