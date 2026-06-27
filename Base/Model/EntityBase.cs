using HomeCompanion.Base.Utilities;

namespace HomeCompanion.Base.Model;

/// <summary>
/// Base configuration node supporting optional kind-based polymorphism.
/// </summary>
public abstract class CfgEntity
{
    public CfgEntity()
    {
        // skip the Cfg from the typename in case the first part of the name is Cfg.
        // Kind must be the model type, e.g. Shutter, not CfgShutter.
        var typeName = this.GetType().Name;
        Kind = typeName.StartsWith("Cfg", StringComparison.Ordinal) ? typeName[3..] : typeName;
    }

    /// <summary>
    /// Default discriminator value used by base types.
    /// </summary>
    public const string KindDefault = "default";

    /// <summary>
    /// Type discriminator for polymorphic materialization of this configuration node.
    /// It's to be the name of the model type to instantiate for this configuration, e.g. "Shutter", "ShadowingSpecial", etc.
    /// The corresponding configuration model type is derived by convention by prefixing "Cfg" to the kind, e.g. "CfgShutter", "CfgShadowingSpecial", etc.
    /// The constructor considers this by default and removes a leading "Cfg" from the kind to determine the model type name, but this can be overridden by explicitly setting the Kind property.
    /// </summary>
    public string Kind { get; set; }
}

public interface IModelEntity
{
    string Name { get; }
}

/// <summary>
/// Base runtime model node that carries dictionary-derived name.
/// </summary>
public abstract class ModelEntity : IModelEntity
{
    /// <summary>
    /// Name from the configuration dictionary key.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

public abstract class ModelEntityWithConfig<TConfig> : ModelEntity, IConfigBackedModelEntity where TConfig : CfgEntity
{
    public ModelEntityWithConfig(string name, TConfig config)
    {
        Name = name;
        Configuration = config;
    }

    public TConfig Configuration { get; set; } = default!;
    CfgEntity IConfigBackedModelEntity.Configuration => Configuration;
}

/// <summary>
/// Runtime model entity that exposes its originating configuration object.
/// </summary>
public interface IConfigBackedModelEntity
{
    /// <summary>
    /// Source configuration used to create this runtime model instance.
    /// </summary>
    CfgEntity Configuration { get; }
}
