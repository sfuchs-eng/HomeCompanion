using System.Collections;

namespace HomeCompanion.Values;

/// <summary>
/// Marker interface for bus specific classes keeping track of which values are mapped to which bus addresses (e.g. KNX group addresses).
/// This is needed for connectivity providers to know which bus data point (e.g. KNX Group Address) maps to a given <see cref="IValue"/> object
/// in cases where the mapping cannot be determined via convention (e.g. <see cref="IValue.Name"/> matching a named bus address/datapoint).
/// Override <see cref="ValueBusMapping{TBus, TAddress}"/> for a concrete implementation of this interface for a specific bus type (e.g. KNX).
/// </summary>
public interface IValueBusEndpointMapping : IEqualityComparer
{
    string BusId { get; }
    string Address { get; }
}

public class ValueBusMapping<TBus, TAddress> : IValueBusEndpointMapping where TBus : notnull where TAddress : notnull
{
    public ValueBusMapping(TBus bus, TAddress address)
    {
        Bus = bus;
        Address = address;
    }
    public TBus Bus { get; init; }
    public TAddress Address { get; init; }

    string IValueBusEndpointMapping.BusId => Bus?.ToString() ?? string.Empty;
    string IValueBusEndpointMapping.Address => Address?.ToString() ?? string.Empty;

    // Equality is based on bus and address, as these uniquely identify a datapoint on the bus.
    public virtual new bool Equals(object? x, object? y)
    {
        if (x is ValueBusMapping<TBus, TAddress> mappingX && y is ValueBusMapping<TBus, TAddress> mappingY)
        {
            return EqualityComparer<TBus>.Default.Equals(mappingX.Bus, mappingY.Bus) &&
                   EqualityComparer<TAddress>.Default.Equals(mappingX.Address, mappingY.Address);
        }
        return false;
    }

    public virtual int GetHashCode(object obj)
    {
        if (obj is ValueBusMapping<TBus, TAddress> mapping)
        {
            int hash = 17;
            hash = hash * 31 + EqualityComparer<TBus>.Default.GetHashCode(mapping.Bus);
            hash = hash * 31 + EqualityComparer<TAddress>.Default.GetHashCode(mapping.Address);
            return hash;
        }
        return 0;
    }
}