using HomeCompanion.Abstractions;
using HomeCompanion.Diagnostics;
using HomeCompanion.Persistence;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
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
    public AppLifeCycleStage InitializationStage { get; protected set; } = AppLifeCycleStage.Default;
    public string? Name { get; set; }
    public string? Label { get; set; }

    public event EventHandler<ValueWrittenEventArgs>? Written;
    public event EventHandler<ValueChangedEventArgs>? Changed;
    public event EventHandler<ValueExceptionEventArgs>? ExceptionOccurred;

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
                logger.LogWarning(ex, "Exception in ValueChanged event handler {HandlerType}", handler.GetType().FullName);
            }
        }
    }

    protected virtual void RaiseExceptionOccurred(ValueException exception)
    {
        var handlers = ExceptionOccurred?.GetInvocationList().Cast<EventHandler<ValueExceptionEventArgs>>().ToArray() ?? [];
        foreach (var handler in handlers)
        {
            try
            {
                handler(this, new ValueExceptionEventArgs(exception));
            }
            catch (Exception ex)
            {
                // log the exception and continue with the next handler
                logger.LogWarning(ex, "Exception in ValueExceptionOccurred event handler {HandlerType}", handler.GetType().FullName);
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

    public bool IsValid => Status.IsValidAndInitialized();

    public bool IsActive => (Status & (ValueStatus.Live | ValueStatus.Used)) != 0;

    protected int _exceptionsRetentionCount = 1;
    
    public int ExceptionsRetentionCount
    {
        get => _exceptionsRetentionCount;
        set
        {
            _exceptionsRetentionCount = Math.Max(0, value);
            lock (_exceptionsQueue)
            {
                foreach (var queue in _exceptionsQueue.Values)
                {
                    while (queue.Count > _exceptionsRetentionCount)
                    {
                        queue.Dequeue();
                    }
                }
            }
        }
    }

    // use a FIFO queue to store the most recent exceptions, up to the retention count
    // but: we keep a queue per busmapping and retain per busmapping, so that we can keep track of the most recent exceptions for each busmapping separately
    private readonly IDictionary<object, Queue<ValueException>> _exceptionsQueue = new Dictionary<object, Queue<ValueException>>();

    public void AddException(ValueException exception)
    {
        if (ExceptionsRetentionCount <= 0)
        {
            RaiseExceptionOccurred(exception);
            return;
        }
        
        lock (_exceptionsQueue)
        {
            object queueKey = exception is ValueReceptionException vre ? vre.EndpointMapping : this;
            if (!_exceptionsQueue.TryGetValue(queueKey, out var queue))
            {
                queue = new Queue<ValueException>();
                _exceptionsQueue[queueKey] = queue;
            }
            queue.Enqueue(exception);
            while (queue.Count > _exceptionsRetentionCount)
            {
                queue.Dequeue();
            }
        }
        RaiseExceptionOccurred(exception);
    }

    public IReadOnlyList<ValueException> Exceptions
    {
        get
        {
            lock (_exceptionsQueue)
            {
                return _exceptionsQueue.Values.SelectMany(q => q).ToList();
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

    public abstract bool InitializeValue(object value, AppLifeCycleStage stage);

    /// <summary>
    /// Try to determine the value type from the provided string and parse it into the correct type. Returns true if successful, false otherwise. If parsing fails, an error message is returned.
    /// The internal value is not changed by this method. Use <see cref="IValue{T}.Write"/> or related methods to write a new value after parsing.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="parsedValue"></param>
    /// <param name="errorMessage"></param>
    /// <returns></returns>
    public abstract bool TryParseValue(string value, out object? parsedValue, out string? errorMessage, IFormatProvider? formatProvider = null);

    public virtual string ToString(string? format, IFormatProvider? formatProvider)
    {
        throw new NotImplementedException();
    }

    public virtual void ClearExceptions()
    {
        lock (_exceptionsQueue)
        {
            _exceptionsQueue.Clear();
        }
    }
}

public class ValueBase<T> : ValueBase, IValue<T> where T : notnull
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
        var timeStamp = _timeProvider.GetLocalNow();

        Publish(new ValueWriteRequest<T> { Source = this, NewValue = value, Timestamp = timeStamp });
        RaiseWritten(new ValueWrittenEventArgs(this, this, timeStamp, initiator));
        Publish(new ValueWritten<T> { Source = this, Value = value, Initiator = initiator, Timestamp = timeStamp });

        if (!EqualityComparer<T>.Default.Equals(old, value))
        {
            RaiseChanged(new ValueChangedEventArgs(this, this, timeStamp, initiator));
            Publish(new ValueChanged<T> { Source = this, Initiator = initiator, OldValue = old, NewValue = value, Timestamp = timeStamp });
        }
    }

    public virtual void WriteLocked(T value, object? initiator = null)
    {
        if ((Status.HasFlag(ValueStatus.Live) | Status.HasFlag(ValueStatus.Used)) && EqualityComparer<T>.Default.Equals(Value, value))
        {
            return;
        }
        Write(value, initiator);
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
        var timeStamp = _timeProvider.GetLocalNow();

        if (isFirst || !EqualityComparer<T>.Default.Equals(old, typed))
        {
            RaiseChanged(new ValueChangedEventArgs(this, this, timeStamp, null));
            Publish(new ValueChanged<T> { Source = this, OldValue = old, NewValue = typed, Initiator = null, Timestamp = timeStamp });
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
        var timeStamp = _timeProvider.GetLocalNow();

        // Raise the same events as Write to ensure that entities subscribed to Written or Changed get notified
        // regardless of whether the update came from an internal Write call or an external bus event.
        Publish(new ValueWritten<T> { Source = this, Value = typed, Initiator = null, Timestamp = timeStamp });
        RaiseWritten(new ValueWrittenEventArgs(this, this, timeStamp, null));

        if (isFirst || !EqualityComparer<T>.Default.Equals(old, typed))
        {
            Publish(new ValueChanged<T> { Source = this, OldValue = old, NewValue = typed, Initiator = null, Timestamp = timeStamp });
            RaiseChanged(new ValueChangedEventArgs(this, this, timeStamp, null));
        }
    }

    /// <summary>
    /// Implemented as type aware proxy to <see cref="ValueBase{T}.InitializeValue(T, AppLifeCycleStage)"/> incl. typical conversion paths.
    /// Yet missing: abilitty to register custom conversion functions, e.g. for complex types or special string formats.
    /// </summary>
    public override bool InitializeValue(object value, AppLifeCycleStage stage)
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

    public virtual bool InitializeValue(T value, AppLifeCycleStage stage)
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
        Value = value ?? default(T)!;
        Status = (Status & ~(ValueStatus.Error | ValueStatus.Live | ValueStatus.Used)) | ValueStatus.Initialized;
        InitializationStage = stage;
        return true;
    }

    /// <summary>
    /// Attempts to parse the provided string value into the value's type and returns true if successful, false otherwise. If parsing fails, an error message is returned.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="parsedValue"></param>
    /// <param name="errorMessage"></param>
    /// <param name="formatProvider"></param>
    /// <returns></returns>
    public override bool TryParseValue(string value, [MaybeNullWhen(false)] out object? parsedValue, [NotNullWhen(false)] out string? errorMessage, IFormatProvider? formatProvider = null)
    {
        parsedValue = null;
        errorMessage = null;

        // string? use straight.
        if (typeof(T) == typeof(string))
        {
            parsedValue = value;
            return true;
        }
        // check whether T implements IParsable<T> and use its TryParse method if available
        else if (typeof(T).GetInterface("IParsable`1") is not null)
        {
            var method = typeof(T).GetMethod("TryParse", [typeof(string), typeof(IFormatProvider), typeof(T).MakeByRefType(), typeof(string).MakeByRefType()]);
            if (method != null)
            {
                var parameters = new object?[] { value, formatProvider, null, null };
                bool success = (bool)method.Invoke(null, parameters)!;
                parsedValue = parameters[2];
                errorMessage = parameters[3] as string;
                return success;
            }
            errorMessage = $"Type {typeof(T).Name} implements IParsable<T> but does not have a valid TryParse method.";
            return false;
        }
        // enum
        else if (typeof(T).IsEnum)
        {
            if (Enum.TryParse(typeof(T), value, true, out var enumValue))
            {
                parsedValue = enumValue;
                errorMessage = null;
                return true;
            }
            else
            {
                parsedValue = null;
                errorMessage = $"Failed to parse value '{value}' as enum type {typeof(T).Name}.";
                return false;
            }
        }
        else if (typeof(IConvertible).IsAssignableFrom(typeof(T)))
        {
            try
            {
                parsedValue = Convert.ChangeType(value, typeof(T), formatProvider);
                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                parsedValue = null;
                errorMessage = $"Failed to convert value '{value}' to type {typeof(T).Name}: {ex.Message}";
                return false;
            }
        }
        else
        {
            parsedValue = null;
            errorMessage = $"Failed to parse value '{value}' as type {typeof(T).Name}.";
            return false;
        }
    }
}
