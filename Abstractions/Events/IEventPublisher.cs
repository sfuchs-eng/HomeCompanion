namespace HomeCompanion.Events;

/// <summary>
/// Publishes events to the event bus for asynchronous, FIFO dispatch to all registered handlers.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Enqueues <paramref name="event"/> for dispatch. Returns immediately after the event is queued; handlers are invoked asynchronously.
    /// </summary>
    ValueTask PublishAsync(IEvent @event, CancellationToken cancellationToken = default);
}
