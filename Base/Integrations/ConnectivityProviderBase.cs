namespace HomeCompanion.Integrations;

public abstract class ConnectivityProviderBase<TAddresses, TEndPointMapping>
    : IConnectivityProvider
    where TEndPointMapping : IValueBusEndpointMapping
    where TAddresses : notnull, IEquatable<TAddresses>
{
    public abstract bool IsEnabled { get; }
    public abstract bool IsConnected { get; }
    public abstract bool IsInitializationFinished { get; }

    public abstract Task StartAsync(CancellationToken cancellationToken);
    public abstract Task StopAsync(CancellationToken cancellationToken);

    public record ValueMapping<TMapping>(object BusId, IValue Value, TMapping Mapping);

    /// <summary>Group address → registered value map, built at startup.</summary>
    /// <remarks>In case of full extention to supporting multiple KNX systems in parallel with overlapping group addresses,
    /// this would need to be changed to also consider the bus/connection, e.g. by using a composite key of (busId, groupAddress) or by maintaining a separate map per bus.</remarks>
    protected Dictionary<TAddresses, ValueMapping<TEndPointMapping>> _valueMap = [];

    public IEnumerable<ValueMapping<TEndPointMapping>> FindValueMappings(IReadOnlyList<IValuesContainer> valuesContainers)
    {
        foreach (var container in valuesContainers)
        {
            foreach (var value in container.GetValues())
            {
                foreach (var mapping in value.BusMappings)
                {
                    if (mapping.Value is not TEndPointMapping typedMapping)
                        continue;

                    yield return new ValueMapping<TEndPointMapping>(mapping.Key, value, typedMapping);
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Inbound: Bus → EventBus
    // -------------------------------------------------------------------------

    protected ValueMapping<TEndPointMapping>? ResolveTarget(TAddresses destinationAddress)
    {
        if (_valueMap.TryGetValue(destinationAddress, out var target))
            return target;

        // Fallback for rare cases where a GroupAddress (address dix key: KNX group address, MQTT topics, etc.) instance used as dictionary key
        // was mutated after insertion, causing hash-based lookup to miss.
        foreach (var (address, value) in _valueMap)
        {
            if (address.Equals(destinationAddress))
            {
                // force update hash key by re-inserting with the same key instance, so that future lookups will find it
                _valueMap[address] = value;
                return value;
            }
        }

        return null;
    }
}
