using HomeCompanion.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests.TestUtilities;

internal sealed class PassThroughEventBus(ILogger<PassThroughEventBus>? logger = null) : IEventSubscriber, IEventPublisher
{
    private readonly Dictionary<Type, List<Func<IEvent, CancellationToken, ValueTask>>> _handlersByType = [];
    private readonly ILogger<PassThroughEventBus> logger = logger ?? NullLoggerFactory.Instance.CreateLogger<PassThroughEventBus>();

    public void Subscribe<T>(IEventHandler<T> handler) where T : IEvent
    {
        if (!_handlersByType.TryGetValue(typeof(T), out var handlers))
        {
            handlers = [];
            _handlersByType[typeof(T)] = handlers;
        }

        handlers.Add((evt, ct) => handler.HandleAsync((T)evt, ct));
        logger.LogTrace("Subscribed handler {HandlerType} for event type {EventType}.", handler.GetType().Name, typeof(T).Name);
    }

    public async ValueTask PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
    {
        Type? type = @event.GetType();
        while (type is not null && type != typeof(object))
        {
            if (_handlersByType.TryGetValue(type, out var handlers))
            {
                foreach (var handler in handlers)
                {
                    logger.LogTrace("Publishing event {EventType} to test handler.", type.Name);
                    try
                    {
                        await handler(@event, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Test event handler for {EventType} threw an exception.", type.Name);
                    }
                }
            }
            type = type.BaseType;
        }
    }

    public void Subscribe<T>(EventHandlerDelegate<T> handler) where T : IEvent
    {
        if (!_handlersByType.TryGetValue(typeof(T), out var handlers))
        {
            handlers = [];
            _handlersByType[typeof(T)] = handlers;
        }

        handlers.Add((evt, ct) => handler((T)evt, ct));
        logger.LogTrace("Subscribed delegate handler for event type {EventType}.", typeof(T).Name);
    }
}
