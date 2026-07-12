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

    public double TargetRoomTemperature { get; set; } = 22.0;

    public double DefaultRoomTemperature { get; set; } = 22.0;

    /// <summary>
    /// Enable automatic shadowing for this room if the temperature exceeds this threshold in degrees Celsius.
    /// May trigger a change of the room scene to a shadowing scene if the temperature exceeds this threshold.
    /// </summary>
    public double AutoShadowTemperatureThreshold { get; set; } = 23.0;

    /// <summary>
    /// Avoid shadowing for this room if the temperature is below this threshold in degrees Celsius.
    /// Acts on the shadowing policy evaluation, not on the room scene evaluation.
    /// </summary>
    /// <value></value>
    public double PolicyAvoidShadowingTemperatureThreshold { get; set; } = 21.0;

    /// <summary>
    /// Aggressive shadowing for this room if the temperature exceeds this threshold in degrees Celsius.
    /// Acts on the shadowing policy evaluation, not on the room scene evaluation.
    /// </summary>
    public double PolicyAggressiveShadowingTemperatureThreshold { get; set; } = 24.5;

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
    [Obsolete("solve via room scnene semantics")]
    public ShadowingAutomationLevel? AutomationLevelOverride { get; set; }

    /// <summary>
    /// Optional room-level override for manual override persistence.
    /// </summary>
    public bool? PersistManualOverride { get; set; }

    /// <summary>
    /// Optional room-level override for temporary manual override duration of room scene.
    /// </summary>
    public TimeSpan? RoomSceneManualOverrideDuration { get; set; }

    public TimeSpan? ShutterMaxManualOverrideDuration { get; set; }

    /// <summary>
    /// Minimum shadow position used for UV-protection objective.
    /// </summary>
    public int UvProtectionShadowPosition { get; set; } = 100;

    /// <summary>
    /// Optional slat angle used for UV-protection objective.
    /// </summary>
    public int UvProtectionShadowSlat { get; set; } = 45;

    /// <summary>
    /// Optional reference to a boolean input value which enables or disables Anti-Glare objective, extending the shadowing even to irradiation angles beyond direct sunlight.
    /// </summary>
    public string? AntiGlareEnableReference { get; set; }

    /// <summary>
    /// Shutter positions and slat angles can be defined in the building special overall, but can be overridden at room level.
    /// </summary>
    public Dictionary<byte, CfgRoomSceneShutterPreset> SceneShutterPresets { get; set; } = [];

    /// <summary>
    /// Optional objective-selector input rules for future IValue-driven objective adaptation.
    /// </summary>
    [Obsolete("to be reconsidered")]
    public Dictionary<string, CfgObjectiveSelectorInput> ObjectiveSelectorInputs { get; set; } = [];

    /// <summary>
    /// Cron-style schedule transitions for room-scoped shutter scene changes.
    /// </summary>
    [Obsolete("to be reconsidered")]
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
    public IValue<byte>? ShutterScene { get; set; }

    /// <summary>
    /// Bound room temperature value resolved from <see cref="CfgRoom.TemperatureReference"/>.
    /// </summary>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgRoom.TemperatureReference))]
    public IValue<float>? Temperature { get; set; }

    /// <summary>
    /// Bound room anti-glare enable value resolved from <see cref="CfgRoom.AntiGlareEnableReference"/>.
    /// </summary>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgRoom.AntiGlareEnableReference))]
    public IValue<bool>? AntiGlareEnable { get; set; }

    /// <summary>
    /// Shutters keyed by their configured name.
    /// </summary>
    public Dictionary<string, Shutter> Shutters { get; set; } = [];

    public double GetRoomTemperatureOrDefault()
    {
        if (Temperature?.TryGetNumericValue(out double roomTemperature) ?? false)
            return roomTemperature;
        return Configuration.DefaultRoomTemperature;
    }
}
