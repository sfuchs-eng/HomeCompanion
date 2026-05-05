namespace HomeCompanion.Events;

/// <summary>
/// Handles events of type <typeparamref name="T"/> dispatched by the event bus.
/// </summary>
/// <typeparam name="T">The concrete event type to handle. Handlers registered for a base type also receive derived events.</typeparam>
public interface IEventHandler<in T> where T : IEvent
{
    /// <summary>Invoked by the event bus for each matching event. Must not propagate exceptions.</summary>
    ValueTask HandleAsync(T @event, CancellationToken cancellationToken = default);
}
