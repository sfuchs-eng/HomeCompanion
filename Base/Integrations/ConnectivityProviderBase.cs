using HomeCompanion.Abstractions;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Integrations;

public abstract class ConnectivityProviderBase<TAddresses, TEndPointMapping>
    : IConnectivityProvider
    where TEndPointMapping : IValueBusEndpointMapping
    where TAddresses : notnull, IEquatable<TAddresses>
{
    private DateTimeOffset? _inboundGateReleasedAt;
    private long _inboundEventsSeen;

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

    protected Dictionary<TAddressKey, ValueMapping<TEndPointMapping>> BuildValueMap<TAddressKey>(
        IReadOnlyList<IValuesContainer> valuesContainers,
        object expectedBusId,
        Func<TEndPointMapping, TAddressKey> keySelector,
        IEqualityComparer<TAddressKey>? comparer = null)
        where TAddressKey : notnull
    {
        var result = comparer is null
            ? new Dictionary<TAddressKey, ValueMapping<TEndPointMapping>>()
            : new Dictionary<TAddressKey, ValueMapping<TEndPointMapping>>(comparer);

        foreach (var mapping in FindValueMappings(valuesContainers))
        {
            if (!Equals(mapping.BusId, expectedBusId))
                continue;

            var key = keySelector(mapping.Mapping);
            if (result.TryGetValue(key, out var existing))
            {
                throw new InvalidOperationException(
                    $"Duplicate mapping key '{key}' for bus '{expectedBusId}': values '{existing.Value.Name}' and '{mapping.Value.Name}'.");
            }

            result[key] = mapping;
        }

        return result;
    }

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

    protected async Task WaitForStartupGateAsync(
        IHomeCompanionLifeCycleSynchronization lifeCycleSynchronization,
        ILogger logger,
        string providerName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var waitStartedAt = DateTimeOffset.UtcNow;
        logger.LogDebug(
            "{ProviderName} waiting for stage {Stage} before enabling inbound processing. Timeout={Timeout}.",
            providerName,
            AppInitializationStage.InitValuesRegistered,
            timeout);

        await lifeCycleSynchronization.WaitForInitializationStageCompletedAsync(
            AppInitializationStage.InitValuesRegistered,
            timeout,
            cancellationToken);

        _inboundGateReleasedAt = DateTimeOffset.UtcNow;
        Interlocked.Exchange(ref _inboundEventsSeen, 0);

        logger.LogInformation(
            "{ProviderName} startup gate released after {ElapsedMs} ms (stage {Stage}).",
            providerName,
            (_inboundGateReleasedAt.Value - waitStartedAt).TotalMilliseconds,
            AppInitializationStage.InitValuesRegistered);
    }

    protected void LogFirstInboundAfterStartupGate(
        ILogger logger,
        string providerName,
        string inboundType)
    {
        if (Interlocked.Increment(ref _inboundEventsSeen) == 1 && _inboundGateReleasedAt is { } gateReleasedAt)
        {
            logger.LogDebug(
                "{ProviderName} received first inbound {InboundType} {ElapsedMs} ms after startup gate release.",
                providerName,
                inboundType,
                (DateTimeOffset.UtcNow - gateReleasedAt).TotalMilliseconds);
        }
    }

    protected void SubscribeValueWriteRequests(
        IEventSubscriber subscriber,
        Func<ValueWriteRequest, CancellationToken, Task> writeHandler)
    {
        subscriber.Subscribe(new DelegatingValueWriteRequestHandler(writeHandler));
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

    private sealed class DelegatingValueWriteRequestHandler(Func<ValueWriteRequest, CancellationToken, Task> writeHandler)
        : IEventHandler<ValueWriteRequest>
    {
        public ValueTask HandleAsync(ValueWriteRequest @event, CancellationToken cancellationToken = default)
            => new(writeHandler(@event, cancellationToken));
    }
}
