namespace HomeCompanion.Base;

/// <summary>
/// Convenience base class for event handlers inside logic modules.
/// </summary>
/// <remarks>
/// Derive from this class and implement <see cref="HandleAsync"/> to handle events of type <typeparamref name="T"/>.
/// Register instances with <see cref="IEventSubscriber.Subscribe{T}"/> — typically done from <see cref="LogicBase"/>
/// constructor helpers.
/// </remarks>
/// <typeparam name="T">The event type to handle.</typeparam>
public abstract class EventHandlerBase<T> : IEventHandler<T> where T : IEvent
{
    /// <inheritdoc/>
    public abstract ValueTask HandleAsync(T @event, CancellationToken cancellationToken = default);
}
