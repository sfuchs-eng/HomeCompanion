using System;

namespace HomeCompanion.Base.Events;

/// <summary>
/// Value objects are initialized several times:<br/>
/// 1. When the system starts, all values are initialized with their default value.<br/>
/// 2. Values persisted prior last shutdown are initialized with the persisted value on system start.<br/>
/// 3. Values might be further initialized based on bus telegrams received or API calls to the value owner (e.g. KNX goup address write/read-anser or OpenHAB item reads).<br/>
/// See also <see cref="Abstractions.IValuesContainer"/> and <see cref="ValueEvent"/> derived classes as well as <see cref="ValueStatus"/>.
/// </summary>
public class ValueInitialization : ValueEvent
{
}

public class ValueInitialization<T> : ValueInitialization
{
    public T Value { get; }

    public ValueInitialization(T value)
    {
        Value = value;
    }
}