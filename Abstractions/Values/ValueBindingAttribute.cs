namespace HomeCompanion.Values;

/// <summary>
/// Declares a value reference binding for a writable logic property of type <see cref="IValue"/> or <see cref="IValue{T}"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ValueBindingAttribute : Attribute
{
    /// <summary>
    /// Creates a new binding attribute.
    /// </summary>
    /// <param name="reference">Value reference string.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reference"/> is null, empty, or whitespace.</exception>
    public ValueBindingAttribute(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("Value reference must not be empty.", nameof(reference));

        Reference = reference;
    }

    /// <summary>
    /// Gets the configured value reference string.
    /// </summary>
    public string Reference { get; }
}
