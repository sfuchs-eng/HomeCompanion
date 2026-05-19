using HomeCompanion.Abstractions;
using HomeCompanion.Persistence;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace HomeCompanion.Values;

/// <summary>
/// The value type agnostic part for <see cref="ValueBase{T}"/>.
/// </summary>
public abstract class ValueBase(ILogger<ValueBase> logger, TimeProvider? timeProvider = null) : IValue, IValueEventReceiver
{
    private IEventPublisher? _publisher;
    private readonly object _busMappingsLock = new();
    protected readonly ILogger<ValueBase> logger = logger;
    protected readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public abstract Type ValueType { get; }
    public abstract object? OValue { get; }
    public ValueStatus Status { get; protected set; } = ValueStatus.Default;
    public AppInitializationStage InitializationStage { get; protected set; } = AppInitializationStage.Default;
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
    public Dictionary<object, IValueBusEndpointMapping> BusMappings
    {
        get => _busMappings;
        init
        {
            lock (_busMappingsLock)
            {
                _busMappings = value ?? [];
            }
        }
    }

    /// <inheritdoc/>
    public virtual string? Format(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;

        List<IValueBusEndpointMapping> mappings;
        lock (_busMappingsLock)
        {
            mappings = _busMappings.Values.ToList();
        }

        var busMapping = mappings.FirstOrDefault(m => m.CanFormatValueForDisplay)
                         ?? mappings.FirstOrDefault();

        if (busMapping is null)
        {
            return OValue is IFormattable formattable
                ? formattable.ToString(null, culture)
                : OValue?.ToString();
        }

        try
        {
            var formatted = busMapping.FormatValueForDisplay(OValue, culture);
            if (!string.IsNullOrWhiteSpace(formatted))
                return formatted;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to format display value for {ValueName} via bus mapping {BusId}:{Address}.", Name, busMapping.BusId, busMapping.Address);
        }

        return OValue is IFormattable fallbackFormattable
            ? fallbackFormattable.ToString(null, culture)
            : OValue?.ToString();
    }

    public bool TryGetBusEndpoint<TBusMapping>(object busIdentifier, out TBusMapping? mapping) where TBusMapping : IValueBusEndpointMapping
    {
        lock (_busMappingsLock)
        {
            if (_busMappings.TryGetValue(busIdentifier, out var value) && value is TBusMapping typedValue)
            {
                mapping = typedValue;
                return true;
            }
        }
        mapping = default;
        return false;
    }

    public virtual void AddBusEndpoint(object busIdentifier, IValueBusEndpointMapping mapping)
    {
        lock (_busMappingsLock)
        {
            _busMappings[busIdentifier] = mapping;
        }
    }

    /// <inheritdoc/>
    public virtual void Initialize(IEventPublisher publisher, IValuesManager manager)
    {
        _publisher = publisher;
        manager.RegisterValue(this);
    }

    /// <summary>
    /// Updates the stored value from a bus event payload. Called by the <see cref="IValuesManager"/> when an update event is received for this value.
    /// The raw value from the event is passed in and the method is responsible for parsing it and updating the stored value accordingly.
    /// </summary>
    protected abstract void ReceiveUpdateCore(object? rawValue);

    /// <summary>
    /// Handles a write received from the event bus (e.g. from a logic or an API call). Called by the <see cref="IValuesManager"/> when a value write event is received for this value.
    /// </summary>
    /// <param name="newValue"></param>
    protected abstract void ReceiveWriteCore(object? newValue);

    void IValueEventReceiver.ReceiveUpdate(object? rawValue) => ReceiveUpdateCore(rawValue);

    void IValueEventReceiver.ReceiveWrite(object? newValue) => ReceiveWriteCore(newValue);

    /// <summary>Publishes an event to the event bus if <see cref="Initialize"/> has been called.</summary>
    protected virtual void Publish(IEvent @event) => _publisher?.PublishAsync(@event).GetAwaiter().GetResult();

    protected virtual bool TryParse(string str, out object? value)
    {
        value = null;
        return false;
    }

    public abstract bool InitializeValue(object value, AppInitializationStage stage);
}

public class ValueBase<T> : ValueBase, IValue<T>
{
    public T Value { get; protected set; } = default!;
    public override object? OValue { get => Value; }

    public override Type ValueType { get => typeof(T); }

    public ValueBase(ILogger<ValueBase<T>> logger, TimeProvider? timeProvider = null) : base(logger, timeProvider)
    {
    }

    /// <inheritdoc/>
    public virtual void Write(T value, object? initiator = null)
    {
        var old = Value;
        Value = value;
        Status = (Status & ~ValueStatus.Error) | ValueStatus.Initialized | ValueStatus.Used;

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
    protected override void ReceiveUpdateCore(object? rawValue)
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
        Status = (Status & ~ValueStatus.Error) | ValueStatus.Initialized | ValueStatus.Live;

        if (isFirst || !EqualityComparer<T>.Default.Equals(old, typed))
        {
            RaiseChanged(new ValueChangedEventArgs(this, this, null));
            Publish(new ValueChanged<T> { Source = this, OldValue = old, NewValue = typed, Initiator = null, Timestamp = _timeProvider.GetUtcNow() });
        }
    }

    /// <inheritdoc/>
    protected override void ReceiveWriteCore(object? newValue)
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
        Status = (Status & ~ValueStatus.Error) | ValueStatus.Initialized | ValueStatus.Live;

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

    /// <summary>
    /// Implemented as type aware proxy to <see cref="ValueBase{T}.InitializeValue(T, AppInitializationStage)"/> incl. typical conversion paths.
    /// Yet missing: abilitty to register custom conversion functions, e.g. for complex types or special string formats.
    /// </summary>
    public override bool InitializeValue(object value, AppInitializationStage stage)
    {
        // nullable?
        if (value is null)
        {
            // Null is a valid payload for nullable value types and reference types.
            if (default(T) is null)
                return InitializeValue(default!, stage);

            Status |= ValueStatus.Error;
            logger.LogDebug("Received null value for {ValueName} during initialization, but {ExpectedType} is not nullable.", Name, typeof(T));
            return false;
        }

        // direct type match? This is the most common case and should be handled first for performance reasons.
        if (value is T typed)
        {
            return InitializeValue(typed, stage);
        }

        // IConvertible from string?
        if (value is string str && typeof(T) != typeof(string) && !string.IsNullOrEmpty(str) && typeof(IConvertible).IsAssignableFrom(typeof(T)))
        {
            try
            {
                var converted = (T)Convert.ChangeType(str, typeof(T));
                return InitializeValue(converted, stage);
            }
            catch (Exception ex)
            {
                Status |= ValueStatus.Error;
                logger.LogDebug(ex, "Failed to convert string value for {ValueName} during initialization. Expected type {ExpectedType}.", Name, typeof(T));
                return false;
            }
        }

        // Try generic conversion via ComponentModel.TypeConverter
        var converter = System.ComponentModel.TypeDescriptor.GetConverter(typeof(T));
        if (converter != null && converter.CanConvertFrom(value.GetType()))
        {
            try
            {
                var converted = (T)converter.ConvertFrom(value)!;
                return InitializeValue(converted, stage);
            }
            catch (Exception ex)
            {
                Status |= ValueStatus.Error;
                logger.LogDebug(ex, "Failed to convert value using TypeConverter for {ValueName} during initialization. Expected type {ExpectedType}, but got {ActualType}.", Name, typeof(T), value.GetType());
                return false;
            }
        }

        Status |= ValueStatus.Error;
        logger.LogDebug("Failed to initialize {ValueName} with value of incorrect type. Expected {ExpectedType}, but got {ActualType}.", Name, typeof(T), value?.GetType());
        return false;
    }

    public virtual bool InitializeValue(T value, AppInitializationStage stage)
    {
        if (Status.HasFlag(ValueStatus.Initialized))
        {
            if ( InitializationStage >= stage )
            {
                logger.LogTrace("Attempted to initialize {ValueName} at stage {Stage}, but it is already initialized for stage {InitializationStage}. Skipping downgrade.", Name, stage, InitializationStage);
                return false;
            }
            else
            {
                logger.LogTrace("Re-initializing {ValueName} with new value. Previous initialization stage: {PreviousStage}, new initialization stage: {NewStage}.", Name, InitializationStage, stage);
            }
        }
        if (value is null && default(T) is not null)
        {
            Status |= ValueStatus.Error;
            logger.LogDebug("Attempted to initialize {ValueName} with null, but {ExpectedType} is not nullable.", Name, typeof(T));
            return false;
        }
        Value = value;
        Status = (Status & ~(ValueStatus.Error | ValueStatus.Live | ValueStatus.Used)) | ValueStatus.Initialized;
        InitializationStage = stage;
        return true;
    }
}
