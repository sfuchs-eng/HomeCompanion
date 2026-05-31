namespace HomeCompanion.Base.Model;

/// <summary>
/// Configuration for a room.
/// </summary>
public class CfgRoom : CfgEntity
{
    /// <summary>
    /// Shutters keyed by their configured name.
    /// </summary>
    public Dictionary<string, CfgShutter> Shutters { get; set; } = [];
}

/// <summary>
/// Runtime representation of a room.
/// </summary>
public class Room : ModelEntity, IConfigBackedModelEntity
{
    public Room(string name, CfgRoom config)
    {
        Name = name;
        Configuration = config;
    }

    /// <summary>
    /// Source configuration used to create this runtime model instance.
    /// </summary>
    public CfgRoom Configuration { get; set; }

    CfgEntity IConfigBackedModelEntity.Configuration => Configuration;

    /// <summary>
    /// Shutters keyed by their configured name.
    /// </summary>
    public Dictionary<string, Shutter> Shutters { get; set; } = [];
}
