namespace HomeCompanion.Values;

/// <summary>
/// Resolves configured string references to concrete <see cref="IValue"/> instances.
/// References are matched against the <see cref="IValue.Name"/> which should be unique across the application, not only within a particular values container.
/// </summary>
/// <remarks>
/// Implementations should support startup-centric lookups with optional caching and a refresh path
/// for dynamic containers that add values at runtime.
/// </remarks>
public interface IValueProvider
{
    /// <summary>
    /// Resolves the specified value reference and throws when it cannot be resolved uniquely.
    /// </summary>
    /// <param name="reference">Configured value reference string referring to an <see cref="IValue.Name"/>.</param>
    /// <returns>The resolved <see cref="IValue"/> instance.</returns>
    IValue Resolve(string reference);

    /// <summary>
    /// Attempts to resolve the specified value reference.
    /// </summary>
    /// <param name="reference">Configured value reference string referring to an <see cref="IValue.Name"/>.</param>
    /// <param name="value">Resolved value if successful; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the reference resolves uniquely; otherwise <see langword="false"/>.</returns>
    bool TryResolve(string reference, out IValue? value);

    /// <summary>
    /// Attempts to resolve the specified value reference to a typed value.
    /// </summary>
    /// <typeparam name="T">Expected value type.</typeparam>
    /// <param name="reference">Configured value reference string referring to an <see cref="IValue.Name"/>.</param>
    /// <param name="value">Resolved typed value if successful; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the reference resolves uniquely and is type-compatible; otherwise <see langword="false"/>.</returns>
    bool TryResolve<T>(string reference, out IValue<T>? value);
}
