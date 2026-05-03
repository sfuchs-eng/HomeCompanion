using System;

namespace HomeCompanion.Base.Values;

/// <summary>
/// Classes implementing this interface can contain <see cref="IValue"/> instances.
/// Implementing classes are responsible for initializing and updating the contained values, e.g. based on bus telegrams received or API calls to the value owner (e.g. KNX goup address write/read-anser or OpenHAB item reads).
/// </summary>
public interface IValuesContainer
{
    IEnumerable<IValue> GetValues();
}
