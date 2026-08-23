namespace HomeCompanion.Values;

/// <summary>
/// TODO: implement <see cref="IValueFactory"/>
/// The factory may use <see cref="ValueBase{T}"/>'s method <see cref="ValueBase{T}.TryParse"/> to parse the value type from a string, and then create a new instance of <see cref="ValueBase{T}"/> with the parsed type.
/// But local applications shall be allowed to inject their own <see cref="IValueFactory"/> implementation to create custom <see cref="IValue"/> implementations, e.g. for bus specific value types.
/// </summary>
public interface IValueFactory
{
    IValue CreateValue(Type valueType, string? name = null, string? label = null);
}