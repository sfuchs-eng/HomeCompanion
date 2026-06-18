namespace HomeCompanion.Events;

/// <summary>
/// Registers <see cref="IEventHandler{T}"/> instances with the event bus.
/// </summary>
/// <remarks>
/// Separated from <see cref="IEventPublisher"/> (Interface Segregation): logics subscribe, bus infrastructure publishes. Logics and other components may also publish events.
/// Handlers registered for a base event type also receive all derived event types.
/// </remarks>
public interface IEventSubscriber
{
    /// <summary>
    /// Registers <paramref name="handler"/> to be invoked for every event of type <typeparamref name="T"/> (including derived types).
    /// </summary>
    void Subscribe<T>(IEventHandler<T> handler) where T : IEvent;
}
