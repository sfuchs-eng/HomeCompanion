using HomeCompanion.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests.TestUtilities;

internal sealed class PassThroughEventBus(ILogger<PassThroughEventBus> logger = null) : IEventSubscriber, IEventPublisher
{
    private readonly Dictionary<Type, List<object>> _handlersByType = [];
    private readonly ILogger<PassThroughEventBus> logger = logger ?? NullLoggerFactory.Instance.CreateLogger<PassThroughEventBus>();

    public void Subscribe<T>(IEventHandler<T> handler) where T : IEvent
    {
        if (!_handlersByType.TryGetValue(typeof(T), out var handlers))
        {
            handlers = [];
            _handlersByType[typeof(T)] = handlers;
        }

        handlers.Add(handler);
        logger.LogTrace("Subscribed handler {HandlerType} for event type {EventType}.", handler.GetType().Name, typeof(T).Name);
    }

    public async Task PublishAsync<T>(T @event) where T : IEvent
    {
        if (!_handlersByType.TryGetValue(typeof(T), out var handlers))
            return;

        foreach (var handler in handlers.Cast<IEventHandler<T>>())
        {
            logger.LogTrace("Publishing event {EventType} to handler {HandlerType}.", typeof(T).Name, handler.GetType().Name);
            await handler.HandleAsync(@event);
        }
    }

    public void Subscribe<T>(EventHandlerDelegate<T> handler) where T : IEvent
    {
        if (!_handlersByType.TryGetValue(typeof(T), out var handlers))
        {
            handlers = [];
            _handlersByType[typeof(T)] = handlers;
        }

        handlers.Add(handler);
        logger.LogTrace("Subscribed delegate handler for event type {EventType}.", typeof(T).Name);
    }

    public async ValueTask PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
    {
        await PublishAsync((dynamic)@event);
    }
}
