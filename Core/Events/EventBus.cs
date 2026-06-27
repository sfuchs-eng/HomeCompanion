using System.Threading.Channels;
using HomeCompanion.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Core.Events;

/// <summary>
/// Singleton event bus. Implements both <see cref="IEventPublisher"/> and <see cref="IEventSubscriber"/>.
/// </summary>
/// <remarks>
/// Events are queued in a FIFO <see cref="Channel{T}"/> and dispatched by a single background loop.
/// Each registered handler is invoked in subscription order. If a handler throws, the exception is caught
/// and logged; subsequent handlers are still invoked.
/// Handlers registered for a base event type also receive all derived event types (type hierarchy walk).
/// </remarks>
internal sealed class EventBus : BackgroundService, IEventPublisher, IEventSubscriber
{
    private readonly Channel<IEvent> _channel = Channel.CreateUnbounded<IEvent>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly Dictionary<Type, List<Func<IEvent, CancellationToken, ValueTask>>> _handlers = [];
    private readonly ILogger<EventBus> _logger;

    public EventBus(ILogger<EventBus> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public ValueTask PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
    {
        if (_channel.Writer.TryWrite(@event))
            return ValueTask.CompletedTask;

        return WriteWithShutdownHandlingAsync(@event, cancellationToken);
    }

    /// <inheritdoc/>
    public void Subscribe<T>(IEventHandler<T> handler) where T : IEvent
    {
        var type = typeof(T);
        if (!_handlers.TryGetValue(type, out var list))
        {
            list = [];
            _handlers[type] = list;
        }

        list.Add((e, ct) => handler.HandleAsync((T)e, ct));
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var @event in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                await DispatchAsync(@event, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("Event bus dispatch loop stopped due to host shutdown.");
        }
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();

        try
        {
            await base.StopAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Event bus shutdown was canceled by host timeout.");
        }
    }

    private async ValueTask WriteWithShutdownHandlingAsync(IEvent @event, CancellationToken cancellationToken)
    {
        try
        {
            await _channel.Writer.WriteAsync(@event, cancellationToken);
        }
        catch (ChannelClosedException)
        {
            _logger.LogDebug("Event bus is already closed; dropping event {EventType}.", @event.GetType().Name);
        }
    }

    private async ValueTask DispatchAsync(IEvent @event, CancellationToken cancellationToken)
    {
        // Walk type hierarchy so handlers registered on base types also fire.
        Type? type = @event.GetType();
        while (type is not null && type != typeof(object))
        {
            if (_handlers.TryGetValue(type, out var handlers))
            {
                foreach (var handler in handlers)
                {
                    try
                    {
                        await handler(@event, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Event handler for {EventType} threw an unhandled exception.", type.Name);
                    }
                }
            }

            type = type.BaseType;
        }
    }

    public void Subscribe<T>(EventHandlerDelegate<T> handler) where T : IEvent
    {
        var type = typeof(T);
        if (!_handlers.TryGetValue(type, out var list))
        {
            list = [];
            _handlers[type] = list;
        }

        list.Add((e, ct) => handler((T)e, ct));
    }
}
