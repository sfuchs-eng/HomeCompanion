using System.Collections;
using HomeCompanion.Abstractions;

namespace HomeCompanion.Base.Values;

/// <summary>
/// A datapoint value in the HomeCompanion system. Values are managed by the <see cref="IValuesManager"/> and can be written to by logics or connectivity providers, and read by logics or connectivity providers.
/// Values can be initialized with a default value or updated based on bus telegrams or API calls. See also <see cref="ValueInitialization"/> and <see cref="ValueWritten"/> events as well as <see cref="IValuesContainer"/> for classes
/// that contain values.
/// </summary>
/// <remarks>
/// - Values are the core abstraction for representing state as well as events in the system
/// - Values can be of any type (e.g. <see cref="bool"/>, <see cref="float"/>, custom classes, etc.) and are typed via the generic <see cref="IValue{T}"/> interface
/// - Values expose events for <see cref="ILogic"/> to subscribe to changes and react accordingly
/// - Values listen to the event bus for updates from connectivity providers (e.g. KNX bus telegrams) and update their stored value and publish change events as needed
/// - Values can be written to by logics or connectivity providers, which triggers update propagation (e.g. writing to a bus, updating dependent values, etc.)
/// </remarks>
public interface IValue
{
    public Type ValueType { get; }
    public ValueStatus Status { get; }
    public string? Name { get; }
    public string? Label { get; }

    /// <summary>
    /// Logic side write received: published by the value object when a value is written to it (e.g. via <see cref="IValue{T}.Write"/>).
    /// Connnectivity providers shall listen to event bus instead of subscribing to individual value events, and filter as needed.
    /// </summary>
    public event EventHandler<ValueWrittenEventArgs>? Written;
    public event EventHandler<ValueChangedEventArgs>? Changed;

    public void AddBusEndpoint(object busIdentifier, IValueBusEndpointMapping mapping);
    public bool TryGetBusEndpoint<TBusMapping>(object busIdentifier, out TBusMapping? mapping) where TBusMapping : IValueBusEndpointMapping;

    /// <summary>
    /// Allows for direct initialization via code.
    /// E.g. a KNX value can be initialized with its group address mapping via this property, eliminating the need for dynamic initialization of mappings.
    /// </summary>
    public Dictionary<object, IValueBusEndpointMapping> BusMappings { init; }

    /// <summary>
    /// Wires the value to the event bus so it can receive inbound updates (via <see cref="HomeCompanion.Base.Events.ValueWriteReceived"/>)
    /// and publish outbound requests (via <see cref="HomeCompanion.Base.Events.ValueWritten"/> and <see cref="HomeCompanion.Base.Events.ValueChanged"/>).
    /// Called at startup by a values manager or connectivity provider for each discovered value.
    /// </summary>
    void Initialize(IEventPublisher publisher, IEventSubscriber subscriber);
}

public interface IValue<T> : IValue
{
    public T Value { get; }

    /// <summary>Writes a value, triggering update propagation (e.g. to a connected bus).</summary>
    void Write(T value);
}
