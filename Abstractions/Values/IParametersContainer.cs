namespace HomeCompanion.Values;

/// <summary>
/// A container for <see cref="IParameter"/>s, allowing the Web UI and other user interface components to access and configure parameters exposed by logics.
/// </summary>
public interface IParametersContainer
{
    /// <summary>
    /// Gets the collection of parameters contained in this container.
    /// </summary>
    IEnumerable<IParameter> Parameters { get; }
}