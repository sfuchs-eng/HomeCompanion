using Microsoft.Extensions.DependencyInjection;

namespace HomeCompanion.Persistence;

/// <summary>
/// Stores internal time-series signals produced by logic modules and other in-process components.
/// Use `TryGetSignalStore` extension method of `IServiceProvider` to check if a signal store is available in the current service provider.
/// </summary>
/// <remarks>
/// The contract is transport-neutral so implementations can target different backends.
/// First implementations targets InfluxDB, implemented by the Influx extension.
/// </remarks>
public interface ISignalStore
{
    bool IsEnabled { get; }

    /// <summary>
    /// Enqueues a single measurement for asynchronous persistence.
    /// </summary>
    /// <param name="measurement">Measurement to enqueue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when enqueueing has finished.</returns>
    ValueTask EnqueueAsync(InternalSignalMeasurement measurement, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues multiple measurements for asynchronous persistence.
    /// </summary>
    /// <param name="measurements">Measurements to enqueue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when enqueueing has finished.</returns>
    ValueTask EnqueueRangeAsync(IEnumerable<InternalSignalMeasurement> measurements, CancellationToken cancellationToken = default);
}

public static class SignalStoreExtensions
{
    public static bool TryGetSignalStore(this IServiceProvider serviceProvider, out ISignalStore? signalStore)
    {
        signalStore = serviceProvider.GetService<ISignalStore>();
        return signalStore is not null;
    }
}