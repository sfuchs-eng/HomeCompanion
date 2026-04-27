namespace HomeCompanion.Base.Events;

/// <summary>
/// A Logic has written a value to a value object.
/// Connected busses should write the value to the bus, and other logics should react to the new value as needed.
/// </summary>
public class ValueWritten : ValueEvent
{

}
