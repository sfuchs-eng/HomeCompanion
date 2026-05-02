using Microsoft.Extensions.Logging;

namespace HomeCompanion.Base.Values;

/// <summary>
/// The value type agnostic part for <see cref="ValueBase{T}"/>.
/// </summary>
public abstract class ValueBase(ILogger<ValueBase> logger) : IValue
{
    private IEventPublisher? _publisher;
    protected readonly ILogger<ValueBase> logger = logger;

    public abstract Type ValueType { get; }
    public ValueStatus Status { get; protected set; }
    public string? Name { get; set; }
    public string? Label { get; set; }

    public event EventHandler<ValueWrittenEventArgs>? Written;
    public event EventHandler<ValueChangedEventArgs>? Changed;

    protected virtual void RaiseWritten(ValueWrittenEventArgs args)
    {
        // call the event handlers individually and catch exceptions to ensure that one misbehaving handler doesn't prevent others from being notified
        var handlers = Written?.GetInvocationList().Cast<EventHandler<ValueWrittenEventArgs>>().ToArray() ?? [];
        foreach (var handler in handlers)
        {
            try
            {
                handler(this, args);
            }
            catch (Exception ex)
            {
                // log the exception and continue with the next handler
                Console.Error.WriteLine($"Exception in ValueWritten event handler: {ex}");
            }
        }
    }
    
    protected virtual void RaiseChanged(ValueChangedEventArgs args)
    {
        var handlers = Changed?.GetInvocationList().Cast<EventHandler<ValueChangedEventArgs>>().ToArray() ?? [];
        foreach (var handler in handlers)
        {
            try
            {
                handler(this, args);
            }
            catch (Exception ex)
            {
                // log the exception and continue with the next handler
                Console.Error.WriteLine($"Exception in ValueChanged event handler: {ex}");
            }
        }
    }

    /// <summary>
    /// See <see cref="IValueBusEndpointMapping"/> for details on the purpose of this property.
    /// Use <see cref="ValueBusMapping{TBus, TAddress}"/> for a concrete implementation of <see cref="IValueBusEndpointMapping"/> for a specific bus type (e.g. KNX).
    /// </summary>
    protected Dictionary<object, IValueBusEndpointMapping> _busMappings { get; private set; } = [];
    public Dictionary<object, IValueBusEndpointMapping> BusMappings { get => _busMappings; init => _busMappings = value ?? []; }

    public bool TryGetBusEndpoint<TBusMapping>(object busIdentifier, out TBusMapping? mapping) where TBusMapping : IValueBusEndpointMapping
    {
        if (_busMappings.TryGetValue(busIdentifier, out var value) && value is TBusMapping typedValue)
        {
            mapping = typedValue;
            return true;
        }
        mapping = default;
        return false;
    }

    public virtual void AddBusEndpoint(object busIdentifier, IValueBusEndpointMapping mapping)
    {
        _busMappings[busIdentifier] = mapping;
    }

    /// <inheritdoc/>
    public virtual void Initialize(IEventPublisher publisher, IEventSubscriber subscriber)
    {
        _publisher = publisher;
        subscriber.Subscribe(new WriteReceivedHandler(this));
    }

    /// <summary>
    /// Updates the stored value from a bus event payload. Called by the internal <see cref="WriteReceivedHandler"/>.
    /// </summary>
    protected virtual void ReceiveUpdate(object? rawValue) { }

    /// <summary>Publishes an event to the event bus if <see cref="Initialize"/> has been called.</summary>
    protected virtual void Publish(IEvent @event) => _publisher?.PublishAsync(@event);

    private sealed class WriteReceivedHandler(ValueBase owner) : IEventHandler<ValueWriteReceived>
    {
        public ValueTask HandleAsync(ValueWriteReceived e, CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(e.Target, owner)) return ValueTask.CompletedTask;
            owner.ReceiveUpdate(e.NewValue);
            return ValueTask.CompletedTask;
        }
    }
}

public class ValueBase<T>(ILogger<ValueBase<T>> logger) : ValueBase(logger), IValue<T>
{
    public T Value { get; protected set; } = default!;

    public override Type ValueType { get => typeof(T); }

    /// <inheritdoc/>
    public virtual void Write(T value)
    {
        var old = Value;
        Value = value;
        Status = (Status & ~ValueStatus.Error) | ValueStatus.Initialized;

        RaiseWritten(new ValueWrittenEventArgs(this, this));
        Publish(new ValueWritten<T> { Source = this, Value = value });

        if (!EqualityComparer<T>.Default.Equals(old, value))
        {
            RaiseChanged(new ValueChangedEventArgs(this, this));
            Publish(new ValueChanged<T> { Source = this, OldValue = old, NewValue = value });
        }
    }

    /// <inheritdoc/>
    protected override void ReceiveUpdate(object? rawValue)
    {
        if (rawValue is null)
        {
            Status |= ValueStatus.Error;
            return;
        }
        if (rawValue is not T typed)
        {
            Status |= ValueStatus.Error;
            return;
        }

        bool isFirst = !Status.HasFlag(ValueStatus.Initialized);
        var old = Value;
        Value = typed;
        Status = (Status & ~ValueStatus.Error) | ValueStatus.Initialized;

        if (isFirst || !EqualityComparer<T>.Default.Equals(old, typed))
        {
            RaiseChanged(new ValueChangedEventArgs(this, this));
            Publish(new ValueChanged<T> { Source = this, OldValue = old, NewValue = typed });
        }
    }
}