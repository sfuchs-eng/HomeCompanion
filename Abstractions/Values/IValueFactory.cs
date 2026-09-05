namespace HomeCompanion.Values;

/// <summary>
/// The factory may use <see cref="ValueBase{T}"/>'s method <see cref="ValueBase{T}.TryParse"/> to parse the value type from a string, and then create a new instance of <see cref="ValueBase{T}"/> with the parsed type.
/// But local applications shall be allowed to inject their own <see cref="IValueFactory"/> implementation to create custom <see cref="IValue"/> implementations, e.g. for bus specific value types.
/// </summary>
public interface IValueFactory
{
    /// <summary>
    /// Creates a new instance of <see cref="IValue"/> with the specified type.
    /// </summary>
    /// <param name="valueType">The type of the value.</param>
    /// <param name="name">The name of the value.</param>
    /// <param name="label">The label of the value.</param>
    /// <returns>A new instance of <see cref="IValue"/> with the specified type.</returns>
    IValue CreateValue(Type valueType, string? name = null, string? label = null);

    /// <summary>
    /// Creates a new instance of <see cref="IValue"/> with the specified type <typeparamref name="T"/>.
    /// </summary>
    /// <param name="name">The name of the value.</param>
    /// <param name="label">The label of the value.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>A new instance of <see cref="IValue"/> with the specified type.</returns>
    IValue CreateValue<T>(string? name = null, string? label = null) where T : notnull;

    /// <summary>
    /// Tries to infer the type from the string value and create a new instance of <see cref="IValue"/> with the inferred type.
    /// </summary>
    /// <param name="value">The string value to parse.</param>
    /// <param name="name">The name of the value.</param>
    /// <param name="label">The label of the value.</param>
    /// <returns><c>true</c> if the value was successfully parsed; otherwise, <c>false</c>.</returns>
    bool TryParseValue(string value, out IValue parsedValueInstance, out string? errorMessage, string? name = null, string? label = null);

    /// <summary>
    /// Tries to parse the string value into the specified type <typeparamref name="T"/> and create a new instance of <see cref="IValue"/> with the parsed type.
    /// </summary>
    /// <param name="value">The string value to parse.</param>
    /// <param name="name">The name of the value.</param>
    /// <param name="label">The label of the value.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>A new instance of <see cref="IValue"/> with the parsed type.</returns>
    bool TryParseValue<T>(string value, out IValue parsedValueInstance, out string? errorMessage, string? name = null, string? label = null) where T : notnull;
}