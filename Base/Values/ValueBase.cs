using HomeCompanion.Abstractions;
using HomeCompanion.Diagnostics;
using HomeCompanion.Persistence;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using UnitsNet;

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
    public ValueUnitInfo? Unit { get; set; }

    /// <summary>
    /// Assigns unit metadata to this value.
    /// </summary>
    public ValueBase WithUnit(ValueUnitInfo unitInfo)
    {
        Unit = unitInfo;
        return this;
    }

    /// <summary>
    /// Assigns unit metadata to this value from a UnitsNet unit enum.
    /// </summary>
    public ValueBase WithUnit<TUnit>(TUnit unit, string? unitSymbol = null) where TUnit : struct, Enum
    {
        Unit = CreateUnitInfo(unit, unitSymbol);
        return this;
    }

    /// <summary>
    /// Tries to assign unit metadata to this value from a UnitsNet unit enum.
    /// Returns false when the unit is unknown to UnitsNet metadata.
    /// </summary>
    public bool TrySetUnit<TUnit>(TUnit unit, string? unitSymbol = null) where TUnit : struct, Enum
    {
        if (!TryCreateUnitInfo(unit, out var unitInfo, unitSymbol))
            return false;

        Unit = unitInfo;
        return true;
    }

    /// <summary>
    /// Creates unit metadata from a UnitsNet unit enum.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the unit is not known to UnitsNet metadata.</exception>
    public static ValueUnitInfo CreateUnitInfo<TUnit>(TUnit unit, string? unitSymbol = null) where TUnit : struct, Enum
    {
        if (!TryCreateUnitInfo(unit, out var unitInfo, unitSymbol))
            throw new ArgumentException($"Unit '{unit}' ({typeof(TUnit).Name}) is not registered in UnitsNet metadata.", nameof(unit));

        return unitInfo;
    }

    /// <summary>
    /// Tries to create unit metadata from a UnitsNet unit enum.
    /// </summary>
    public static bool TryCreateUnitInfo<TUnit>(TUnit unit, out ValueUnitInfo unitInfo, string? unitSymbol = null) where TUnit : struct, Enum
    {
        if (Quantity.TryGetUnitInfo((Enum)(object)unit, out var resolvedInfo))
        {
            var quantityName = resolvedInfo.QuantityName;
            if (!string.IsNullOrWhiteSpace(quantityName))
            {
                unitInfo = new ValueUnitInfo(quantityName, resolvedInfo.Name, unitSymbol);
                return true;
            }
        }

        unitInfo = default!;
        return false;
    }

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

    protected bool FailInitialization(string message, Exception? innerException = null)
    {
        Status |= ValueStatus.Error;
        AddException(innerException is null ? new ValueException(message) : new ValueException(message, innerException));
        return false;
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
            return FormatRawValue(culture);
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

        return FormatRawValue(culture);
    }

    private string? FormatRawValue(CultureInfo culture)
    {
        if (OValue is null)
            return null;

        if (OValue is IQuantity quantity)
        {
            if (Unit is not null && TryResolveUnitEnum(Unit, out var targetUnit))
            {
                try
                {
                    return quantity.ToUnit(targetUnit).ToString(culture);
                }
                catch
                {
                    // Fall back to the quantity's current unit representation.
                }
            }

            return quantity.ToString(culture);
        }

        var formatted = OValue is IFormattable formattable
            ? formattable.ToString(null, culture)
            : OValue.ToString();

        if (Unit is null || string.IsNullOrWhiteSpace(formatted))
            return formatted;

        if (TryCreateQuantityFromScalar(OValue, Unit, out var scalarQuantity))
            return scalarQuantity.ToString(culture);

        return $"{formatted} {Unit.DisplayUnit}";
    }

    protected static bool TryResolveUnitEnum(ValueUnitInfo unitInfo, out Enum unit)
    {
        unit = default!;

        if (!TryResolveQuantityInfo(unitInfo.QuantityName, out var quantityInfo))
            return false;

        try
        {
            var parsedUnit = Enum.Parse(quantityInfo.UnitType, unitInfo.UnitName, ignoreCase: true);
            if (parsedUnit is not Enum parsedEnum)
                return TryResolveUnitEnumByAbbreviation(unitInfo, quantityInfo, out unit);

            unit = parsedEnum;
            return true;
        }
        catch
        {
            return TryResolveUnitEnumByAbbreviation(unitInfo, quantityInfo, out unit);
        }
    }

    private static bool TryResolveUnitEnumByAbbreviation(ValueUnitInfo unitInfo, QuantityInfo quantityInfo, out Enum unit)
    {
        unit = default!;

        try
        {
            foreach (var candidateUnitInfo in quantityInfo.UnitInfos)
            {
                var abbreviations = UnitAbbreviationsCache.Default.GetAbbreviations(candidateUnitInfo, CultureInfo.InvariantCulture);
                if (!abbreviations.Any(abbreviation => string.Equals(abbreviation, unitInfo.UnitName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (candidateUnitInfo.Value is not Enum unitEnum)
                    continue;

                unit = unitEnum;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    protected static bool TryResolveQuantityInfo(string quantityName, out QuantityInfo quantityInfo)
    {
        var found = Quantity.Infos.FirstOrDefault(info => string.Equals(info.Name, quantityName, StringComparison.OrdinalIgnoreCase));
        if (found is null)
        {
            quantityInfo = default!;
            return false;
        }

        quantityInfo = found;
        return true;
    }

    protected static bool TryCreateQuantityFromScalar(object value, ValueUnitInfo unitInfo, out IQuantity quantity)
    {
        quantity = default!;

        try
        {
            var magnitude = Convert.ToDouble(value, CultureInfo.InvariantCulture);

            if (TryResolveUnitEnum(unitInfo, out var resolvedUnit)
                && Quantity.TryFrom(magnitude, resolvedUnit, out var parsedByEnum)
                && parsedByEnum is not null)
            {
                quantity = parsedByEnum;
                return true;
            }

            if (Quantity.TryFrom(magnitude, unitInfo.QuantityName, unitInfo.UnitName, out var parsedQuantity) && parsedQuantity is not null)
            {
                quantity = parsedQuantity;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Converts a quantity to a numeric value of the specified target type, optionally using a preferred unit.
    /// </summary>
    /// <param name="quantity">The quantity to convert.</param>
    /// <param name="targetType">The target numeric type.</param>
    /// <param name="unitInfo">Optional unit information for conversion.</param>
    /// <param name="numericValue">The resulting numeric value if conversion succeeds.</param>
    /// <returns>True if the conversion was successful; otherwise, false.</returns>
    protected static bool TryConvertQuantityToNumericTarget(IQuantity quantity, Type targetType, ValueUnitInfo? unitInfo, out object? numericValue)
    {
        numericValue = null;
        var nonNullableType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            var value = quantity.As(quantity.Unit);
            if (unitInfo is not null && TryResolveUnitEnum(unitInfo, out var preferredUnit))
            {
                value = quantity.As(preferredUnit);
            }

            numericValue = Convert.ChangeType(value, nonNullableType, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
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

    public ValueBase(string? name, string? label, ILogger<ValueBase<T>> logger, TimeProvider? timeProvider = null) : base(logger, timeProvider)
    {
        Name = name;
        Label = label;
    }

    /// <summary>
    /// Assigns unit metadata to this typed value.
    /// </summary>
    public new ValueBase<T> WithUnit(ValueUnitInfo unitInfo)
    {
        base.WithUnit(unitInfo);
        return this;
    }

    /// <summary>
    /// Assigns unit metadata from a UnitsNet unit enum to this typed value.
    /// </summary>
    public new ValueBase<T> WithUnit<TUnit>(TUnit unit, string? unitSymbol = null) where TUnit : struct, Enum
    {
        base.WithUnit(unit, unitSymbol);
        return this;
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
        if ((Status.HasFlag(ValueStatus.Live) || Status.HasFlag(ValueStatus.Used)) && EqualityComparer<T>.Default.Equals(Value, value))
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

            const string message = "Received null value during initialization for a non-nullable value.";
            logger.LogDebug("{Message} Value {ValueName} expects {ExpectedType} at stage {Stage}.", message, Name, typeof(T), stage);
            return FailInitialization($"{message} Value '{Name}' expects {typeof(T)} at stage {stage}.");
        }

        // direct type match? This is the most common case and should be handled first for performance reasons.
        if (value is T typed)
        {
            return InitializeValue(typed, stage);
        }

        // Use TryParse for string to non-string
        if (value is string strValue && typeof(T) != typeof(string))
        {
            if (TryParseValue(strValue, out var parsedValue, out var errorMessage))
            {
                if (parsedValue is T parsedTyped)
                {
                    return InitializeValue(parsedTyped, stage);
                }
                else
                {
                    logger.LogDebug("Parsed value '{ParsedValue}' (type {ActualType}) from string '{OriginalString}' is not of the expected type {ExpectedType} for value {ValueName} at stage {Stage}.", parsedValue, parsedValue?.GetType(), strValue, typeof(T), Name, stage);
                    return FailInitialization($"Parsed value '{strValue}' to '{parsedValue}' (type {parsedValue?.GetType()}) from string '{strValue}' is not of the expected type {typeof(T)} for value '{Name}' at stage {stage}.");
                }
            }
            else
            {
                const string message = "Failed to parse string value during initialization.";
                logger.LogDebug("{Message} Value {ValueName} expects {ExpectedType} at stage {Stage}. Error: {ErrorMessage}", message, Name, typeof(T), stage, errorMessage);
                return FailInitialization($"{message} Value '{Name}' expects {typeof(T)} at stage {stage}. Error: {errorMessage}", new FormatException(errorMessage));
            }
        }

        // catch numeric conversions, e.g. int to double, float to decimal, etc.
        if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(typeof(T)))
        {
            try
            {
                var converted = (T)Convert.ChangeType(value, typeof(T));
                return InitializeValue(converted, stage);
            }
            catch (Exception ex)
            {
                const string message = "Failed to convert value using IConvertible during initialization.";
                logger.LogDebug(ex, "{Message} Value {ValueName} expects {ExpectedType}, received {ActualType} at stage {Stage}.", message, Name, typeof(T), value.GetType(), stage);
                return FailInitialization($"{message} Value '{Name}' expects {typeof(T)}, but received {value.GetType()} at stage {stage}.", ex);
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
                const string message = "Failed to convert value using TypeConverter during initialization.";
                logger.LogDebug(ex, "{Message} Value {ValueName} expects {ExpectedType}, received {ActualType} at stage {Stage}.", message, Name, typeof(T), value.GetType(), stage);
                return FailInitialization($"{message} Value '{Name}' expects {typeof(T)}, but received {value.GetType()} at stage {stage}.", ex);
            }
        }

        const string typeMismatchMessage = "Failed to initialize value with an incompatible type.";
        logger.LogDebug("{Message} Value {ValueName} expects {ExpectedType}, but got {ActualType} at stage {Stage}.", typeMismatchMessage, Name, typeof(T), value?.GetType(), stage);
        return FailInitialization($"{typeMismatchMessage} Value '{Name}' expects {typeof(T)}, but received {value?.GetType()} at stage {stage}.");
    }

    /// <summary>
    /// Initializes the value with the specified value and application life cycle stage.
    /// App life-cycle stage is used to track the initialization progress of the value and to prevent downgrading the initialization stage.
    /// If the value is already initialized for a later stage, the method will return false and the value will not be updated. If the value is initialized for an earlier stage, it will be updated and the initialization stage will be set to the new stage.
    /// If the value is not initialized yet, it will be initialized with the specified value and stage.
    /// If the value is null and the type T is not nullable, the method will return false and the value will not be updated. If the value is null and the type T is nullable, the value will be set to null and the initialization stage will be set to the specified stage.
    /// If the value is of an incorrect type, the method will return false and the value will not be updated.
    /// If the value is successfully initialized, the method will return true and the value will be updated.
    /// </summary>
    /// <param name="value">The value to initialize.</param>
    /// <param name="stage">The application life cycle stage at which the value is being initialized.</param>
    /// <returns>True if the value was successfully initialized; otherwise, false.</returns>
    public virtual bool InitializeValue(T value, AppLifeCycleStage stage)
    {
        if (Status.HasFlag(ValueStatus.Initialized))
        {
            if (InitializationStage >= stage)
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
            const string message = "Attempted to initialize a non-nullable value with null.";
            logger.LogDebug("{Message} Value {ValueName} expects {ExpectedType} at stage {Stage}.", message, Name, typeof(T), stage);
            return FailInitialization($"{message} Value '{Name}' expects {typeof(T)} at stage {stage}.");
        }
        Value = value ?? default(T)!;
        Status = (Status & ~(ValueStatus.Error | ValueStatus.Live | ValueStatus.Used)) | ValueStatus.Initialized;
        InitializationStage = stage;
        return true;
    }

    /// <summary>
    /// Sets the value and the initialization stage.
    /// Use it only when the IValue object is created and initialized for the first time.
    /// For regular initialization, use <see cref="InitializeValue(T, AppLifeCycleStage)"/> instead.
    /// </summary>
    /// <param name="value">The value to set.</param>
    /// <param name="stage">The application life cycle stage at which the value is being set.</param>
    /// <returns>The current instance with the updated value and stage.</returns>
    public virtual ValueBase<T> WithValue(T value, AppLifeCycleStage stage = AppLifeCycleStage.Default)
    {
        Value = value;
        Status = (Status & ~(ValueStatus.Error | ValueStatus.Live | ValueStatus.Used)) | ValueStatus.Initialized;
        InitializationStage = stage;
        return this;
    }

    /// <summary>
    /// Attempts to parse the provided string value into the value's type and returns true if successful, false otherwise. If parsing fails, an error message is returned.
    /// The internal value is not changed by this method. Use <see cref="IValue{T}.Write"/> or related methods to write a new value after parsing
    /// or use <see cref="InitializeValue(T, AppLifeCycleStage)"/> or <see cref="WithValue(T, AppLifeCycleStage)"/> to initialize the value with the parsed value.
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
        formatProvider ??= CultureInfo.CurrentCulture;

        // string? use straight.
        if (typeof(T) == typeof(string))
        {
            parsedValue = value;
            return true;
        }

        if (TryParseUnitsNetQuantity(value, formatProvider, out parsedValue, out errorMessage))
            return true;

        if (TryParseNumericWithUnitMetadata(value, formatProvider, out parsedValue, out errorMessage))
            return true;

        // check whether T implements IParsable<T> and use its TryParse method if available
        if (typeof(T).GetInterface("IParsable`1") is not null)
        {
            var method = typeof(T).GetMethod("TryParse", [typeof(string), typeof(IFormatProvider), typeof(T).MakeByRefType()]);
            if (method is not null)
            {
                var parameters = new object?[] { value, formatProvider, null };
                bool success = (bool)method.Invoke(null, parameters)!;
                parsedValue = parameters[2];
                if (!success)
                    errorMessage = $"Failed to parse value '{value}' as type {typeof(T).Name}.";
                return success;
            }
            else
            {
                method = typeof(T).GetMethod("TryParse", [typeof(string), typeof(IFormatProvider), typeof(T).MakeByRefType(), typeof(string).MakeByRefType()]);
                if (method != null)
                {
                    var parameters = new object?[] { value, formatProvider, null, null };
                    bool success = (bool)method.Invoke(null, parameters)!;
                    parsedValue = parameters[2];
                    errorMessage = parameters[3] as string;
                    return success;
                }

                // handle types that implement IParsable<T> but do not have a valid TryParse method (e.g. Single, Byte, ...)
                var defaultNumStyle = NumberStyles.Float | NumberStyles.AllowThousands | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.Integer;
                errorMessage = $"Failed to parse value '{value}' as type {typeof(T).Name}.";
                parsedValue = null;
                switch (Type.GetTypeCode(typeof(T)))
                {
                    case TypeCode.Boolean:
                        if (bool.TryParse(value, out var boolResult))
                        {
                            parsedValue = boolResult;
                            errorMessage = null;
                            return true;
                        }
                        parsedValue = null;
                        return false;
                    case TypeCode.Double:
                        if (double.TryParse(value, defaultNumStyle | NumberStyles.AllowLeadingSign, formatProvider, out var doubleResult))
                        {
                            parsedValue = doubleResult;
                            errorMessage = null;
                            return true;
                        }
                        else
                        {
                            parsedValue = null;
                            return false;
                        }
                    case TypeCode.Single:
                        if (float.TryParse(value, defaultNumStyle | NumberStyles.AllowLeadingSign, formatProvider, out var floatResult))
                        {
                            parsedValue = floatResult;
                            errorMessage = null;
                            return true;
                        }
                        else
                        {
                            parsedValue = null;
                            return false;
                        }
                    case TypeCode.Byte:
                        if (byte.TryParse(value, defaultNumStyle | NumberStyles.AllowLeadingSign, formatProvider, out var byteResult))
                        {
                            parsedValue = byteResult;
                            errorMessage = null;
                            return true;
                        }
                        else
                        {
                            parsedValue = null;
                            return false;
                        }
                    case TypeCode.Int16:
                        if (short.TryParse(value, defaultNumStyle | NumberStyles.AllowLeadingSign, formatProvider, out var shortResult))
                        {
                            parsedValue = shortResult;
                            errorMessage = null;
                            return true;
                        }
                        parsedValue = null;
                        return false;
                    case TypeCode.Int32:
                        if (int.TryParse(value, defaultNumStyle | NumberStyles.AllowLeadingSign, formatProvider, out var intResult))
                        {
                            parsedValue = intResult;
                            errorMessage = null;
                            return true;
                        }
                        parsedValue = null;
                        return false;
                    case TypeCode.Int64:
                        if (long.TryParse(value, defaultNumStyle | NumberStyles.AllowLeadingSign, formatProvider, out var longResult))
                        {
                            parsedValue = longResult;
                            errorMessage = null;
                            return true;
                        }
                        parsedValue = null;
                        return false;
                    case TypeCode.UInt16:
                        if (ushort.TryParse(value, defaultNumStyle, formatProvider, out var ushortResult))
                        {
                            parsedValue = ushortResult;
                            errorMessage = null;
                            return true;
                        }
                        parsedValue = null;
                        return false;
                    case TypeCode.UInt32:
                        if (uint.TryParse(value, defaultNumStyle, formatProvider, out var uintResult))
                        {
                            parsedValue = uintResult;
                            errorMessage = null;
                            return true;
                        }
                        parsedValue = null;
                        return false;
                    case TypeCode.UInt64:
                        if (ulong.TryParse(value, defaultNumStyle, formatProvider, out var ulongResult))
                        {
                            parsedValue = ulongResult;
                            errorMessage = null;
                            return true;
                        }
                        parsedValue = null;
                        return false;
                    case TypeCode.Decimal:
                        if (decimal.TryParse(value, defaultNumStyle | NumberStyles.AllowLeadingSign, formatProvider, out var decimalResult))
                        {
                            parsedValue = decimalResult;
                            errorMessage = null;
                            return true;
                        }
                        parsedValue = null;
                        return false;
                        // Add more cases for other types as needed
                }
            }
            errorMessage = $"Type {typeof(T).Name} implements IParsable<T> but does not have a valid TryParse method.";
            return false;
        }

        // enum
        if (typeof(T).IsEnum)
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

        if (typeof(IConvertible).IsAssignableFrom(typeof(T)))
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

        parsedValue = null;
        errorMessage = $"Failed to parse value '{value}' as type {typeof(T).Name}.";
        return false;
    }

    private bool TryParseUnitsNetQuantity(string rawValue, IFormatProvider formatProvider, out object? parsedValue, out string? errorMessage)
    {
        parsedValue = null;
        errorMessage = null;

        if (!typeof(IQuantity).IsAssignableFrom(typeof(T)))
            return false;

        if (Quantity.TryParse(formatProvider, typeof(T), rawValue, out var parsedQuantity))
        {
            parsedValue = parsedQuantity;
            return true;
        }

        if (Unit is not null && double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, formatProvider, out var magnitude)
            && Quantity.TryFrom(magnitude, Unit.QuantityName, Unit.UnitName, out var inferredQuantity)
            && inferredQuantity.GetType() == typeof(T))
        {
            parsedValue = inferredQuantity;
            return true;
        }

        errorMessage = $"Failed to parse value '{rawValue}' as UnitsNet quantity type {typeof(T).Name}.";
        return true;
    }

    private bool TryParseNumericWithUnitMetadata(string rawValue, IFormatProvider formatProvider, out object? parsedValue, out string? errorMessage)
    {
        parsedValue = null;
        errorMessage = null;

        if (Unit is null)
            return false;

        var nonNullableType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (!typeof(IConvertible).IsAssignableFrom(nonNullableType) || nonNullableType == typeof(bool) || nonNullableType.IsEnum)
            return false;

        if (!TryResolveQuantityInfo(Unit.QuantityName, out var quantityInfo))
        {
            errorMessage = $"Unknown UnitsNet quantity '{Unit.QuantityName}' configured for value {Name ?? "(unnamed)"}.";
            return true;
        }

        if (Quantity.TryParse(formatProvider, quantityInfo.ValueType, rawValue, out var parsedQuantity)
            && TryConvertQuantityToNumericTarget(parsedQuantity, typeof(T), Unit, out var convertedWithSuffix))
        {
            parsedValue = convertedWithSuffix;
            return true;
        }

        if (double.TryParse(rawValue, NumberStyles.Float | NumberStyles.Integer | NumberStyles.Number | NumberStyles.AllowThousands, formatProvider, out var magnitude)
            && TryCreateQuantityFromScalar(magnitude, Unit, out var quantity)
            && TryConvertQuantityToNumericTarget(quantity, typeof(T), Unit, out var convertedWithoutSuffix))
        {
            parsedValue = convertedWithoutSuffix;
            return true;
        }

        errorMessage = $"Failed to parse value '{rawValue}' as numeric type {typeof(T).Name} using configured unit {Unit}.";
        return true;
    }
}
