namespace HomeCompanion.Persistence;

/// <summary>
/// Stores internal time-series signals produced by logic modules and other in-process components.
/// </summary>
/// <remarks>
/// The contract is transport-neutral so implementations can target different backends.
/// </remarks>
public interface ISignalStore
{
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
