namespace HomeCompanion.Logics;

/// <summary>
/// Restricts automatic logic registration to the specified host environments.
/// </summary>
/// <remarks>
/// This attribute participates in discovery-time filtering in <c>AddLogics</c>.
/// When no attribute and no configuration rule are present for a logic type,
/// the logic is registered in all environments (default behavior).
/// Configuration-based rules are merged with attribute values by set union.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class LoadInEnvironmentsAttribute(params string[] environments) : Attribute
{
    /// <summary>
    /// Gets the configured environment names for this logic type.
    /// </summary>
    public IReadOnlyList<string> Environments { get; } = environments;
}
