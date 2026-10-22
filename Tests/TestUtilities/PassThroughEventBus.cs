using HomeCompanion.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests.TestUtilities;

internal sealed class PassThroughEventBus(ILogger<PassThroughEventBus>? logger = null) : IEventSubscriber, IEventPublisher
{
    private readonly Dictionary<Type, List<Func<IEvent, CancellationToken, ValueTask>>> _handlersByType = [];
    private readonly ILogger<PassThroughEventBus> logger = logger ?? NullLoggerFactory.Instance.CreateLogger<PassThroughEventBus>();

    public IDisposable Subscribe<T>(IEventHandler<T> handler) where T : IEvent
    {
        if (!_handlersByType.TryGetValue(typeof(T), out var handlers))
        {
            handlers = [];
            _handlersByType[typeof(T)] = handlers;
        }

        Func<IEvent, CancellationToken, ValueTask> registration = (evt, ct) => handler.HandleAsync((T)evt, ct);
        handlers.Add(registration);
        return new Subscription(() => handlers.Remove(registration));
        //logger.LogTrace("Subscribed handler {HandlerType} for event type {EventType}.", handler.GetType().Name, typeof(T).Name);
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
                    //logger.LogTrace("Publishing event {EventType} to test handler.", type.Name); // log causes test warnings
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

    public void Publish(IEvent @event)
    {
        PublishAsync(@event, CancellationToken.None).GetAwaiter().GetResult();
    }

    public IDisposable Subscribe<T>(EventHandlerDelegate<T> handler) where T : IEvent
    {
        if (!_handlersByType.TryGetValue(typeof(T), out var handlers))
        {
            handlers = [];
            _handlersByType[typeof(T)] = handlers;
        }

        Func<IEvent, CancellationToken, ValueTask> registration = (evt, ct) => handler((T)evt, ct);
        handlers.Add(registration);
        return new Subscription(() => handlers.Remove(registration));
        //logger.LogTrace("Subscribed delegate handler for event type {EventType}.", typeof(T).Name);
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private readonly Action _unsubscribe = unsubscribe;
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _unsubscribe();
        }
    }
}
