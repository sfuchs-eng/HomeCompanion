namespace HomeCompanion.Values;

public interface IParametersProvider
{
    /// <summary>
    /// Gets the collection of parameter containers provided by this provider.
    /// The parameter containers can be used to expose parameters that can be configured by the user through the Web UI or other user interface components.
    /// <see cref="ILogic"/> and <see cref="LogicBase"/> in HomeCompanion.Base implement this interface to allow exposing their parameters to the Web UI and other user interface components.
    /// If additional types of <see cref="IParametersContainer"/> are needed, they can be implemented and returned by this property to allow exposing their parameters to the Web UI and other user interface components.
    /// The <see cref="IParametersProvider"/> instances are typically registered with the dependency injection container and can be injected into other logics or components that need to access their parameters.
    /// </summary>
    IEnumerable<IParametersContainer> ParameterContainers { get; }
}