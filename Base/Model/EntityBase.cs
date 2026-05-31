namespace HomeCompanion.Base.Model;

/// <summary>
/// Base configuration node supporting optional kind-based polymorphism.
/// </summary>
public abstract class CfgEntity
{
    /// <summary>
    /// Default discriminator value used by base types.
    /// </summary>
    public const string KindDefault = "default";

    /// <summary>
    /// Optional kind discriminator.
    /// </summary>
    public string? Kind { get; set; }
}

/// <summary>
/// Base runtime model node that carries dictionary-derived name.
/// </summary>
public abstract class ModelEntity
{
    /// <summary>
    /// Name from the configuration dictionary key.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}
