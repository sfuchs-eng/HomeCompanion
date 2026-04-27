using System;

namespace HomeCompanion.Abstractions;

/// <summary>
/// Classes implementing this interface can contain <see cref="IValue"/> instances.
/// Implementing classes are responsible for initializing and updating the contained values, e.g. based on bus telegrams received or API calls to the value owner (e.g. KNX goup address write/read-anser or OpenHAB item reads).<br/>
/// See also <see cref="Base.Events.ValueInitialization"/>.
/// </summary>
public interface IValuesContainer
{

}
