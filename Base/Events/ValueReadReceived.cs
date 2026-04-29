namespace HomeCompanion.Base.Events;

/// <summary>
/// Published by a connectivity provider when a bus read request is received for a registered value.
/// </summary>
public class ValueReadReceived : ValueEvent
{
    /// <summary>The value object that was requested.</summary>
    public required IValue Target { get; init; }
}
