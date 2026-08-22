namespace HomeCompanion.Values;

/// <summary>
/// Allows the Web UI, other user interface components, and other logics to expose parameters that can be configured by the user.
/// Also <see cref="IValue"/>s can be mapped to <see cref="IParameter"/>s to allow writing to <see cref="IValue"/>s from the Web UI.
/// An <see cref="IParameter"/> aims towards GUI exposure, while an <see cref="IValue"/> aims towards logic and general smart home system exposure. The two interfaces are similar, but have different purposes and requirements.
/// <see cref="IValue"/> implements <see cref="IParameter"/> to allow mapping <see cref="IValue"/>s to <see cref="IParameter"/>s for writing to <see cref="IValue"/>s from the Web UI.
/// </summary>
public interface IParameter
{
    /// <summary>
    /// Human-readable label for the parameter, to be displayed on user interfaces.
    /// </summary>
    string Label { get; }

    /// <summary>
    /// The name of the parameter, used as an identifier in the code.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// A brief description of the parameter, to be displayed on user interfaces.
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// Sets the value of the parameter from a string representation.
    /// </summary>
    /// <param name="value">The string representation of the value to set.</param>
    /// <param name="errorMessage">An error message if the value could not be set.</param>
    /// <returns>True if the value was successfully set; otherwise, false.</returns>
    bool SetValueFromString(string value, out string? errorMessage);

    /// <summary>
    /// Formats the current value of the parameter as a string. The formatting should be suitable for display in user interfaces and include any necessary units or context. For example, a temperature parameter might format its value as "22.5 °C".
    /// </summary>
    string FormatValue();

    /// <summary>
    /// Registers a callback to be invoked when the value of the parameter changes.
    /// </summary>
    /// <param name="callback">The callback to invoke when the parameter value changes.</param>
    /// <returns>A registration object that can be used to unregister the callback.</returns>
    IParameterCallbackRegistration RegisterChangedCallback(Action<IParameter> callback);
}

/// <summary>
/// Ensure that the callback is unregistered when the registration is disposed.
/// </summary>
public interface IParameterCallbackRegistration : IDisposable
{
    void Unregister();
}