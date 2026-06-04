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

    /// <summary>
    /// Default constraints for shutters in this room.
    /// Layered on top of building-level defaults and combined with individual shutter constraints, if any.
    /// </summary>
    /// <value></value>
    public ShutterConstraints ShutterConstraints { get; set; } = ShutterConstraints.None;

    /// <summary>
    /// Optional mask applied to the building-level shutter defaults before room-level defaults are added.
    /// </summary>
    public ShutterConstraints? BuildingConstraintsMask { get; set; }

    /// <summary>
    /// Resolves the room-level default shutter constraints from the building-level defaults.
    /// </summary>
    public ShutterConstraints EffectiveShutterConstraints(ShutterConstraints buildingConstraints)
    {
        var mask = BuildingConstraintsMask ?? ShutterConstraints.None;
        return (buildingConstraints & ~mask) | ShutterConstraints;
    }

    public string? ShutterSceneReference { get; set; }

    public string? TemperatureReference { get; set; }

    public double AutoShadowTemperatureThreshold { get; set; } = 25.0;

    /// <summary>
    /// Optional room-level override for the facade incidence cut-over angle in degrees.
    /// </summary>
    public double? FacadeSunCutoverAngleOverride { get; set; }

    /// <summary>
    /// Optional room-level dynamic cut-over angle rules.
    /// If configured, these rules override building-level dynamic cut-over rules for this room.
    /// </summary>
    public List<CfgDynamicCutoverAngleRule> FacadeSunCutoverAngleDynamicRules { get; set; } = [];

    /// <summary>
    /// Room objective profile. If set to <see cref="RoomObjectiveProfile.InheritFromThermalControl"/>,
    /// the objective is derived from building-level thermal control settings.
    /// </summary>
    public RoomObjectiveProfile ObjectiveProfile { get; set; } = RoomObjectiveProfile.InheritFromThermalControl;

    /// <summary>
    /// Optional room-level automation level override.
    /// </summary>
    public ShadowingAutomationLevel? AutomationLevelOverride { get; set; }

    /// <summary>
    /// Optional room-level override for manual override persistence.
    /// </summary>
    public bool? PersistManualOverride { get; set; }

    /// <summary>
    /// Optional room-level override for temporary manual override duration.
    /// </summary>
    public TimeSpan? ManualOverrideDuration { get; set; }

    /// <summary>
    /// Minimum shadow position used for UV-protection objective.
    /// </summary>
    public int UvProtectionShadowPosition { get; set; } = 20;

    /// <summary>
    /// Optional slat angle used for UV-protection objective.
    /// </summary>
    public int UvProtectionShadowSlat { get; set; } = 45;

    /// <summary>
    /// Optional objective-selector input rules for future IValue-driven objective adaptation.
    /// </summary>
    public Dictionary<string, CfgObjectiveSelectorInput> ObjectiveSelectorInputs { get; set; } = [];

    /// <summary>
    /// Cron-style schedule transitions for room-scoped shutter scene changes.
    /// </summary>
    public Dictionary<string, CfgRoomScheduleTransition> ScheduleTransitions { get; set; } = [];
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
    /// Bound room shutter scene value resolved from <see cref="CfgRoom.ShutterSceneReference"/>.
    /// </summary>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgRoom.ShutterSceneReference))]
    public IValue? ShutterScene { get; set; }

    /// <summary>
    /// Bound room temperature value resolved from <see cref="CfgRoom.TemperatureReference"/>.
    /// </summary>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgRoom.TemperatureReference), RequireNumeric = true)]
    public IValue? Temperature { get; set; }

    /// <summary>
    /// Shutters keyed by their configured name.
    /// </summary>
    public Dictionary<string, Shutter> Shutters { get; set; } = [];
}
