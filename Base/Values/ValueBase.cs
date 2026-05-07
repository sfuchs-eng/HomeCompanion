using HomeCompanion.Persistence;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Values;

/// <summary>
/// The value type agnostic part for <see cref="ValueBase{T}"/>.
/// </summary>
public abstract class ValueBase(ILogger<ValueBase> logger, TimeProvider? timeProvider = null) : IValue
{
    private IEventPublisher? _publisher;
    protected readonly ILogger<ValueBase> logger = logger;
    protected readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public abstract Type ValueType { get; }
    public ValueStatus Status { get; protected set; } = ValueStatus.Default;
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
                logger.LogWarning(ex, "Exception in ValueWritten event handler");
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
                logger.LogWarning(ex, "Exception in ValueChanged event handler");
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
        subscriber.Subscribe(new ValueReadAnswerReceivedHandler(this)); // also listen to read answers to update the value based on bus events, if needed
    }

    /// <summary>
    /// Updates the stored value from a bus event payload. Called by the internal <see cref="ValueReadAnswerReceivedHandler"/> when a value read answer event is received for this value.
    /// The raw value from the event is passed in and the method is responsible for parsing it and updating the stored value accordingly.
    /// </summary>
    protected abstract void ReceiveUpdate(object? rawValue);

    /// <summary>
    /// Handles a write received from the event bus (e.g. from a logic or an API call). Called by the internal <see cref="WriteReceivedHandler"/> when a value write event is received for this value.
    /// </summary>
    /// <param name="newValue"></param>
    protected abstract void ReceiveWrite(object? newValue);

    /// <summary>Publishes an event to the event bus if <see cref="Initialize"/> has been called.</summary>
    protected virtual void Publish(IEvent @event) => _publisher?.PublishAsync(@event).GetAwaiter().GetResult();

    public abstract bool InitializeValue(object value, StateInitializationStage stage);

    private sealed class WriteReceivedHandler(ValueBase owner) : IEventHandler<ValueWriteReceived>
    {
        public ValueTask HandleAsync(ValueWriteReceived e, CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(e.Target, owner)) return ValueTask.CompletedTask;
            owner.ReceiveWrite(e.NewValue);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ValueReadAnswerReceivedHandler(ValueBase owner) : IEventHandler<ValueReadAnswerReceived>
    {
        public ValueTask HandleAsync(ValueReadAnswerReceived e, CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(e.Target, owner)) return ValueTask.CompletedTask;
            owner.ReceiveUpdate(e.Value);
            return ValueTask.CompletedTask;
        }
    }
}

public class ValueBase<T> : ValueBase, IValue<T>
{
    public T Value { get; protected set; } = default!;

    public override Type ValueType { get => typeof(T); }

    public ValueBase(ILogger<ValueBase<T>> logger, TimeProvider? timeProvider = null) : base(logger, timeProvider)
    {
    }

    /// <inheritdoc/>
    public virtual void Write(T value, object? initiator = null)
    {
        var old = Value;
        Value = value;
        Status = (Status & ~ValueStatus.Error) | ValueStatus.Initialized;

        Publish(new ValueWriteRequest<T> { Source = this, NewValue = value, Timestamp = _timeProvider.GetUtcNow() });
        RaiseWritten(new ValueWrittenEventArgs(this, this, initiator));
        Publish(new ValueWritten<T> { Source = this, Value = value, Initiator = initiator, Timestamp = _timeProvider.GetUtcNow() });

        if (!EqualityComparer<T>.Default.Equals(old, value))
        {
            RaiseChanged(new ValueChangedEventArgs(this, this, initiator));
            Publish(new ValueChanged<T> { Source = this, Initiator = initiator, OldValue = old, NewValue = value, Timestamp = _timeProvider.GetUtcNow() });
        }
    }

    /// <inheritdoc/>
    protected override void ReceiveUpdate(object? rawValue)
    {
        if (rawValue is null)
        {
            Status |= ValueStatus.Error;
            logger.LogDebug("Received null value for {ValueName}, which is not allowed. Ignoring the update.", Name);
            return;
        }
        if (rawValue is not T typed)
        {
            Status |= ValueStatus.Error;
            logger.LogDebug("Received value of incorrect type for {ValueName}. Expected {ExpectedType}, but got {ActualType}. Ignoring the update.", Name, typeof(T), rawValue.GetType());
            return;
        }

        bool isFirst = !Status.HasFlag(ValueStatus.Initialized);
        var old = Value;
        Value = typed;
        Status = (Status & ~ValueStatus.Error) | ValueStatus.Initialized;

        if (isFirst || !EqualityComparer<T>.Default.Equals(old, typed))
        {
            RaiseChanged(new ValueChangedEventArgs(this, this, null));
            Publish(new ValueChanged<T> { Source = this, OldValue = old, NewValue = typed, Initiator = null, Timestamp = _timeProvider.GetUtcNow() });
        }
    }

    /// <inheritdoc/>
    protected override void ReceiveWrite(object? newValue)
    {
        if (newValue is null)
        {
            Status |= ValueStatus.Error;
            logger.LogDebug("Received null value for {ValueName}, which is not allowed. Ignoring the update.", Name);
            return;
        }
        if (newValue is not T typed)
        {
            Status |= ValueStatus.Error;
            logger.LogDebug("Received value of incorrect type for {ValueName}. Expected {ExpectedType}, but got {ActualType}. Ignoring the update.", Name, typeof(T), newValue.GetType());
            return;
        }

        // Write(typed) is for internal write that go towards the bus. Here we handle a bus write to wards internal.
        bool isFirst = !Status.HasFlag(ValueStatus.Initialized);
        var old = Value;
        Value = typed;
        Status = (Status & ~ValueStatus.Error) | ValueStatus.Initialized;

        // Raise the same events as Write to ensure that entities subscribed to Written or Changed get notified
        // regardless of whether the update came from an internal Write call or an external bus event.
        Publish(new ValueWritten<T> { Source = this, Value = typed, Initiator = null, Timestamp = _timeProvider.GetUtcNow() });
        RaiseWritten(new ValueWrittenEventArgs(this, this, null));

        if (isFirst || !EqualityComparer<T>.Default.Equals(old, typed))
        {
            Publish(new ValueChanged<T> { Source = this, OldValue = old, NewValue = typed, Initiator = null, Timestamp = _timeProvider.GetUtcNow() });
            RaiseChanged(new ValueChangedEventArgs(this, this, null));
        }
    }

    public override bool InitializeValue(object value, StateInitializationStage stage)
    {
        if ( value is T typed)
        {
            return InitializeValue(typed, stage);
        }
        Status |= ValueStatus.Error;
        logger.LogDebug("Failed to initialize {ValueName} with value of incorrect type. Expected {ExpectedType}, but got {ActualType}.", Name, typeof(T), value?.GetType());
        return false;
    }

    public bool InitializeValue(T value, StateInitializationStage stage)
    {
        Value = value;
        Status = (Status & ~ValueStatus.Error) | ValueStatus.Initialized;
        return true;
    }
}
