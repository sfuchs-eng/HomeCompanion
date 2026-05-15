namespace HomeCompanion.Values;

/// <summary>
/// Receives value update and write payloads routed from the event bus.
/// </summary>
/// <remarks>
/// Implemented by value types that can process inbound bus events routed by <see cref="IValuesManager"/>.
/// </remarks>
public interface IValueEventReceiver
{
    /// <summary>
    /// Applies an inbound update payload to the value.
    /// </summary>
    /// <param name="rawValue">The decoded event payload.</param>
    void ReceiveUpdate(object? rawValue);

    /// <summary>
    /// Applies an inbound write payload to the value.
    /// </summary>
    /// <param name="newValue">The decoded event payload.</param>
    void ReceiveWrite(object? newValue);
}