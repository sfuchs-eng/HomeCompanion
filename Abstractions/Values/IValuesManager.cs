namespace HomeCompanion.Values;

/// <summary>
/// Centralized manager for routing value events (<see cref="HomeCompanion.Events.ValueUpdateReceived"/>, <see cref="HomeCompanion.Events.ValueWriteReceived"/>)
/// to their target <see cref="IValue"/> instances based on the event's <see cref="HomeCompanion.Events.ValueUpdateReceived.Target"/> field.
/// </summary>
/// <remarks>
/// <para>
/// The ValuesManager maintains a registry of all active <see cref="IValue"/> instances and subscribes once to the event bus for
/// <see cref="HomeCompanion.Events.ValueUpdateReceived"/> and <see cref="HomeCompanion.Events.ValueWriteReceived"/> events.
/// Instead of each value registering its own handlers (O(N) dispatch), events are routed directly to their target value via dictionary lookup (O(1) dispatch).
/// </para>
/// <para>
/// <see cref="IValue"/> instances register themselves with the manager via <see cref="RegisterValue"/> during their <see cref="IValue.Initialize"/> call,
/// typically initiated by a connectivity provider during value discovery.
/// </para>
/// <para>See <see cref="IValueProvider"/> for an interface that supports finding/retrieving particular values by their <see cref="IValue.Id"/>.</para>
/// </remarks>
public interface IValuesManager
{
    /// <summary>
    /// Registers a value to receive targeted <see cref="HomeCompanion.Events.ValueUpdateReceived"/> and <see cref="HomeCompanion.Events.ValueWriteReceived"/> events.
    /// </summary>
    /// <remarks>
    /// This method should be called during <see cref="IValue.Initialize"/> so that the value starts receiving events from the event bus.
    /// </remarks>
    /// <param name="value">The value to register.</param>
    void RegisterValue(IValue value);

    /// <summary>
    /// Unregisters a value from receiving targeted events.
    /// </summary>
    /// <remarks>
    /// This method can be called when a value is being disposed or removed from the system.
    /// </remarks>
    /// <param name="value">The value to unregister.</param>
    void UnregisterValue(IValue value);
}
