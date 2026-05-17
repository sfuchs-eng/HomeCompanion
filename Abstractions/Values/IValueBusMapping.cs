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

    /// <summary>
    /// Must be managed by the corresponding <see cref="IConnectivityProvider"/>. The <see cref="IValue"/> generally ignores this property.
    /// </summary>
    /// <value></value>
    BusCommunication Communication { get; init; }

    IBusMappingConfiguration? Config { get; init; }

    virtual bool CanFormatValueForDisplay => false;

    string? FormatValueForDisplay(object? value);
}

/// <summary>
/// To capture bus specific configuration parameters for a bus mapping.
/// </summary>
public interface IBusMappingConfiguration
{
    /// <summary>
    /// Formats the configuration for display purposes.
    /// JSON formatting is possible as it's display for technical users, but it should be concise and highlight the relevant configuration parameters for the bus mapping.
    /// E.g. for a KNX mapping, the formatted config could include the data point type. The Address is to be skipped as it's already in the separate property <see cref="IValueBusEndpointMapping.Address"/>.
    /// </summary>
    /// <remarks>
    /// Implementations must not throw exceptions. If formatting fails, the method should return null or an appropriate fallback string.
    /// </remarks>
    /// <returns>A string representation of the configuration.</returns>
    string? FormatConfiguration();

    /// <summary>
    /// .NET format string to format the value for display purposes, e.g. in the UI. The formatting can be based on the value type and/or the bus mapping configuration.
    /// </summary>
    string? ValueFormat { get; }
}

public class ValueBusMapping<TBus, TAddress> : IValueBusEndpointMapping where TBus : notnull where TAddress : notnull
{
    public ValueBusMapping(TBus bus, TAddress address, IBusMappingConfiguration? config)
    {
        Bus = bus;
        Address = address;
        Config = config;
    }
    public TBus Bus { get; init; }
    public TAddress Address { get; init; }
    public BusCommunication Communication { get; init; } = BusCommunication.RegularCommunication;
    public IBusMappingConfiguration? Config { get; init; }

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

    public virtual string? FormatValueForDisplay(object? value)
    {
        try
        {
            if (Config != null && Config.ValueFormat is string format)
            {
                // For simplicity, we use string.Format with the provided format string.
                // In a real implementation, the formatting logic might be more complex and bus-specific.
                return string.Format(format, value);
            }
            else
            {
                // If no specific format is provided, we can use the default ToString() representation of the value.
                return value?.ToString();
            }
        }
        catch
        {
            // If formatting fails, we can choose to return null or a fallback string.
            // For now, we return null to indicate that formatting was not successful.
        }
        return null;
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