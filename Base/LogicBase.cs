namespace HomeCompanion.Base;

/// <summary>
/// Base class for all logic modules. Provides access to the event bus for publishing and subscribing to events.
/// </summary>
/// <remarks>
/// Subclasses should call <see cref="Subscribe{T}"/> from their constructor to register event handlers.
/// Use <see cref="Publisher"/> to publish events.
/// </remarks>
public abstract class LogicBase : ILogic
{
    private readonly IEventSubscriber _subscriber;

    /// <summary>The event publisher for dispatching events onto the event bus.</summary>
    protected IEventPublisher Publisher { get; }

    /// <summary>
    /// Initializes the logic with the required event bus services.
    /// </summary>
    protected LogicBase(IEventPublisher publisher, IEventSubscriber subscriber)
    {
        Publisher = publisher;
        _subscriber = subscriber;
    }

    /// <summary>
    /// Registers <paramref name="handler"/> to receive events of type <typeparamref name="T"/> from the event bus.
    /// </summary>
    protected void Subscribe<T>(IEventHandler<T> handler) where T : IEvent
        => _subscriber.Subscribe(handler);

    /// <inheritdoc/>
    /// <remarks>Enables the logic upon initialization. Override to add custom initialization; call <c>base.InitializeAsync</c> to retain default enabled behaviour.</remarks>
    public virtual Task InitializeAsync(CancellationToken cancellationToken = default)
        => EnableAsync(cancellationToken);

    /// <inheritdoc/>
    public virtual Task EnableAsync(CancellationToken cancellationToken = default)
    {
        IsEnabled = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual Task DisableAsync(CancellationToken cancellationToken = default)
    {
        IsEnabled = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public bool IsEnabled { get; private set; }
}

