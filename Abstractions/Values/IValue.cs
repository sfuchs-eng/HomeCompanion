using HomeCompanion.Abstractions;
using HomeCompanion.Events;
using HomeCompanion.Persistence;
using System.Globalization;

namespace HomeCompanion.Values;

/// <summary>
/// A datapoint value in the HomeCompanion system. Values are managed by the <see cref="IValuesManager"/> and can be written to by logics or connectivity providers, and read by logics or connectivity providers.
/// Values can be updated based on bus telegrams or API calls. See also <see cref="ValueWritten"/> events as well as <see cref="IValuesContainer"/> for classes
/// that contain values.<br/>
/// Implementations shall
/// <list type="bullet">
/// <item>be thread safe and handle concurrent writes and reads, as well as concurrent subscription to events. E.g. by using locks or concurrent collections as needed.</item>
/// <item>handle exceptions in event handlers gracefully, e.g. by catching exceptions from handlers and logging them without letting them propagate to other handlers or the main execution flow.</item>
/// <item>preferrably inherit from <see cref="ValueBase"/> to get a base implementation of the event handling and bus mapping logic, but this is not strictly required.</item>
/// <item>stay bus-agnostic and not implement any bus specific logic, which should be handled by the respective connectivity provider. E.g. a KNX value should not implement
/// any KNX specific logic, but rather expose the value and its bus mapping via the <see cref="IValue"/> interface and let the KNX connectivity provider handle the bus communication.
/// Use <see cref="IValuesContainer"/> to group values and manage their lifecycle.</item>
/// </list>
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Values are the core abstraction for representing state as well as events in the system</item>
/// <item>Values can be of any type (e.g. <see cref="bool"/>, <see cref="float"/>, custom classes, etc.) and are typed via the generic <see cref="IValue{T}"/> interface</item>
/// <item>Values expose events for <see cref="ILogic"/> to subscribe to changes and react accordingly</item>
/// <item>Values listen to the event bus for updates from connectivity providers (e.g. KNX bus telegrams) and update their stored value and publish change events as needed</item>
/// <item>Values can be written to by logics calling <see cref="IValue{T}.Write"/>, which triggers update propagation (e.g. writing to a bus, updating dependent values, etc.)</item>
/// <item>Values publish write requests via the <see cref="ValueWriteRequest"/> event to let connectivity providers know that a new value needs to be sent to mapped bus endpoints.</item>
/// <item><see cref="IConnectivityProvider"/> register themselves to the <see cref="IValue.BusMappings"/> via <see cref="IValue.AddBusEndpoint"/> with the bus entity identifier corresponding to the value. E.g. a KNX Group Address. This allows the provider to listen to value events and forward them to the bus with correct bus specific addressing.</item>
/// </list>
/// </remarks>
public interface IValue
{
    public Type ValueType { get; }
    public ValueStatus Status { get; }
    public string? Name { get; }
    public string? Label { get; }

    /// <summary>
    /// Formats the current value for display using an optional culture.
    /// Implementations should prefer bus specific mapping formatters where available.
    /// If no formatter is available, implementations should fall back to <see cref="object.ToString"/> behavior.
    /// </summary>
    /// <param name="culture">Culture to use for formatting. If null, current culture should be used.</param>
    /// <returns>Formatted value suitable for display.</returns>
    public virtual string? Format(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return OValue is IFormattable formattable
            ? formattable.ToString(null, culture)
            : OValue?.ToString();
    }

    /// <summary>
    /// The value as an object. The actual type of the value is given by <see cref="ValueType"/> and the strongly typed value can be accessed via <see cref="IValue{T}.Value"/>. This property is useful for generic handling of values without knowing their type at compile time, e.g. for event handlers that listen to multiple values of different types or for dynamic initialization of values based on configuration.
    /// </summary>
    public object? OValue { get; }

    /// <summary>
    /// Logic side write received: published by the value object when a value is written to it (e.g. via <see cref="IValue{T}.Write"/>).
    /// Connnectivity providers shall listen to event bus instead of subscribing to individual value events, and filter as needed.
    /// </summary>
    public event EventHandler<ValueWrittenEventArgs>? Written;

    /// <summary>
    /// Value change by whatever means (e.g. bus update, API call, logic write, etc.). Published by the value object when its stored value changes.
    /// Connnectivity providers shall listen to event bus instead of subscribing to individual value events, and filter as needed.
    /// </summary>
    public event EventHandler<ValueChangedEventArgs>? Changed;

    public void AddBusEndpoint(object busIdentifier, IValueBusEndpointMapping mapping);
    public bool TryGetBusEndpoint<TBusMapping>(object busIdentifier, out TBusMapping? mapping) where TBusMapping : IValueBusEndpointMapping;

    /// <summary>
    /// Allows for direct initialization via code and for reading the configured bus endpoint mappings.
    /// E.g. a KNX value can be initialized with its group address mapping via this property, eliminating the need for dynamic initialization of mappings.
    /// </summary>
    public Dictionary<object, IValueBusEndpointMapping> BusMappings { get; init; }

    /// <summary>
    /// Wires the value to the event bus so it can receive inbound updates (via <see cref="HomeCompanion.Events.ValueUpdateReceived"/>)
    /// and publish outbound requests (via <see cref="HomeCompanion.Events.ValueWritten"/> and <see cref="HomeCompanion.Events.ValueChanged"/>).
    /// Called at startup by the <see cref="IValuesManager"/> for discovered values. The manager also handles centralized event subscription routing.
    /// </summary>
    void Initialize(IEventPublisher publisher, IValuesManager manager);

    /// <summary>
    /// Initializes the value with the specified value and stage.
    /// The implementation must ensure that out-of-order initialization calls are handled gracefully,
    /// e.g. by ignoring calls for stages that have already passed or by storing the value and applying it
    /// when the respective stage is reached.
    /// </summary>
    /// <param name="value">The value to initialize.</param>
    /// <param name="stage">The initialization stage.</param>
    /// <returns>True if the value was successfully initialized; otherwise, false.</returns>
    bool InitializeValue(object value, AppInitializationStage stage);
}

/// <inheritdoc cref="IValue"/>
public interface IValue<T> : IValue
{
    public T Value { get; }

    /// <summary>Writes a value, triggering update propagation (e.g. to a connected bus).</summary>
    void Write(T value, object? initiator = null);

    /// <summary>
    /// Initializes the value with the specified value and stage, returning true if the value was successfully initialized.
    /// The initialization logic can be implemented in the concrete value class and can differ based on the stage,
    /// e.g. for loading from a persistent store vs. retrieving from the environment vs. receiving a bus update.<br/>
    /// The implementation must ensure that out-of-order initialization calls are handled gracefully,
    /// e.g. by ignoring calls for stages that have already passed or by storing the value and applying it when the respective stage is reached.
    /// </summary>
    /// <param name="value">The value to initialize.</param>
    /// <param name="stage">The initialization stage.</param>
    /// <returns>True if the value was successfully initialized; otherwise, false.</returns>
    bool InitializeValue(T value, AppInitializationStage stage);
}
