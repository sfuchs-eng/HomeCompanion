namespace HomeCompanion.Base.Model;

/// <summary>
/// Declares metadata for binding a runtime model <see cref="HomeCompanion.Values.IValue"/> property from a config reference property.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ModelValueBindingAttribute : Attribute
{
    /// <summary>
    /// Optional explicit config property name containing the reference string.
    /// If omitted, the binder falls back to the convention <c>{TargetPropertyName}Reference</c>.
    /// </summary>
    public string? SourceConfigPropertyName { get; init; }

    /// <summary>
    /// When true, requires the resolved value type to be numeric.
    /// </summary>
    public bool RequireNumeric { get; init; }

    /// <summary>
    /// Optional required CLR type for the resolved value (compared to <see cref="HomeCompanion.Values.IValue.ValueType"/>).
    /// </summary>
    public Type? RequiredValueType { get; init; }
}
