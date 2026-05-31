namespace HomeCompanion.Base.Model;

/// <summary>
/// Configuration for a floor.
/// </summary>
public class CfgFloor : CfgEntity
{
    /// <summary>
    /// Rooms keyed by their configured name.
    /// </summary>
    public Dictionary<string, CfgRoom> Rooms { get; set; } = [];
}

/// <summary>
/// Runtime representation of a floor.
/// </summary>
public class Floor : ModelEntity
{
    /// <summary>
    /// Rooms keyed by their configured name.
    /// </summary>
    public Dictionary<string, Room> Rooms { get; set; } = [];
}
