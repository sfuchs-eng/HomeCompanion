using HomeCompanion.Base.Utilities;
using HomeCompanion.Values;

namespace HomeCompanion.Base.Model;

/// <summary>
/// Configuration for a building.
/// </summary>
public class CfgBuilding : CfgEntity
{
    /// <summary>
    /// The geographical location of the building.
    /// </summary>
    public GeodeticCoordinateWGS84? Location { get; set; }

    /// <summary>
    /// Facades keyed by their configured name.
    /// </summary>
    public Dictionary<string, CfgFacade> Facades { get; set; } = [];

    /// <summary>
    /// Floors keyed by their configured name.
    /// </summary>
    public Dictionary<string, CfgFloor> Floors { get; set; } = [];

    /// <summary>
    /// Specials keyed by their configured name.
    /// This is for any customization that doesn't fit into the facade or floor categories.
    /// Such might be heating systems, solar panels, or other building-wide features that
    /// require configuration and runtime representation.<br/>
    /// Consider whether to use <see cref="ILogic"/> or <see cref="IConfigBackedModelEntity"/> for these.
    /// </summary>
    public Dictionary<string, CfgSpecial> Specials { get; set; } = [];
}

/// <summary>
/// Runtime representation of a building.
/// </summary>
public class Building : ModelEntity
{
    /// <summary>
    /// Facades keyed by their configured name.
    /// </summary>
    public Dictionary<string, Facade> Facades { get; set; } = [];

    /// <summary>
    /// Floors keyed by their configured name.
    /// </summary>
    public Dictionary<string, Floor> Floors { get; set; } = [];

    /// <summary>
    /// Specials keyed by their configured name.
    /// </summary>
    public Dictionary<string, Special> Specials { get; set; } = [];
}

public class CfgSpecial : CfgEntity
{
}

/// <summary>
/// Building-wide shadowing configuration hosted as a typed special under <see cref="CfgBuilding.Specials"/>.
/// </summary>
public class CfgShadowingSpecial : CfgSpecial
{
    /// <summary>
    /// Default slat position in percent used when no shutter-specific value is configured.
    /// </summary>
    public int DefaultShadowSlat { get; set; } = 50;

    /// <summary>
    /// Default maximum close limit in percent used when no shutter-specific value is configured.
    /// </summary>
    public int DefaultMaxClose { get; set; } = 100;

    /// <summary>
    /// Default constraints applied to shutters in the building.
    /// Room-level and shutter-level constraints are layered on top of this value.
    /// </summary>
    public ShutterConstraints DefaultShutterConstraints { get; set; } = ShutterConstraints.None;

    /// <summary>
    /// Default room temperature threshold used for auto shadowing when no room-specific value is configured.
    /// </summary>
    public double DefaultTemperatureThreshold { get; set; } = 25.0;

    /// <summary>
    /// Do not assess shading if sun elevation is below this value.
    /// </summary>
    public double MinSunElevationToConsider { get; set; } = 4.0;

    /// <summary>
    /// Default facade incidence cut-over angle in degrees.
    /// <para>
    /// 0deg means sun must be perpendicular to the facade normal to count as exposed.
    /// 20deg means sun beyond 70deg incidence to facade normal is treated as not exposed.
    /// </para>
    /// </summary>
    public double DefaultFacadeSunCutoverAngle { get; set; } = 20.0;

    /// <summary>
    /// Optional dynamic cut-over angle rules.
    /// The first matching rule wins.
    /// </summary>
    public List<CfgDynamicCutoverAngleRule> DynamicFacadeSunCutoverRules { get; set; } = [];

    /// <summary>
    /// Global thermal-control mode used to derive default room objectives.
    /// </summary>
    public ThermalControlMode ThermalControl { get; set; } = ThermalControlMode.Balanced;

    /// <summary>
    /// Optional reference to an externally managed thermal-control mode value.
    /// If bound and initialized, this value overrides <see cref="ThermalControl"/>.
    /// </summary>
    public string? ThermalControlModeReference { get; set; }

    /// <summary>
    /// Default automation level used when rooms do not define an override.
    /// </summary>
    public ShadowingAutomationLevel DefaultAutomationLevel { get; set; } = ShadowingAutomationLevel.AutomaticWithTemporaryManualOverride;

    /// <summary>
    /// Enables persistence of manual overrides by default.
    /// </summary>
    public bool PersistManualOverrides { get; set; } = true;

    /// <summary>
    /// Default duration used for temporary manual overrides if a room does not override it.
    /// </summary>
    public TimeSpan DefaultManualOverrideDuration { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Scene numbers that clear manual override and resume automation.
    /// Defaults to legacy-compatible scenes 50 and 52.
    /// </summary>
    public List<int> ResumeAutomationScenes { get; set; } = [50, 52];

    /// <summary>
    /// Schedule evaluation engine used for room cron transitions.
    /// </summary>
    public ShadowingScheduleEngine ScheduleEngine { get; set; } = ShadowingScheduleEngine.InProcess;

    /// <summary>
    /// Reference to the global shutter scene value.
    /// </summary>
    public string? GlobalShutterSceneReference { get; set; }

    /// <summary>
    /// Reference to the global auto shadow status value.
    /// </summary>
    public string? AutoShadowStatusReference { get; set; }

    /// <summary>
    /// Reference to the global absence status value.
    /// </summary>
    public string? AbsenceReference { get; set; }

    /// <summary>
    /// Reference to the value that disables auto-shadow assessment for the entire house.
    /// </summary>
    public string? DisableAutoShadowAssessmentReference { get; set; }

    /// <summary>
    /// Reference to the outdoor temperature value used by shadowing rules.
    /// </summary>
    public string? OutdoorTemperatureReference { get; set; }

    /// <summary>
    /// Reference to the east irradiation intensity value.
    /// </summary>
    public string? SunIntensityEastReference { get; set; }

    /// <summary>
    /// Reference to the south irradiation intensity value.
    /// </summary>
    public string? SunIntensitySouthReference { get; set; }

    /// <summary>
    /// Reference to the west irradiation intensity value.
    /// </summary>
    public string? SunIntensityWestReference { get; set; }

    /// <summary>
    /// Optional reference to a sun azimuth value in degrees.
    /// </summary>
    public string? SunPositionAzimuthReference { get; set; }

    /// <summary>
    /// Optional reference to a sun elevation value in degrees.
    /// </summary>
    public string? SunPositionElevationReference { get; set; }

    /// <summary>
    /// Optional reference to UV index or UV intensity value.
    /// </summary>
    public string? UvIntensityReference { get; set; }

    /// <summary>
    /// Scene controllers keyed by scene key for explicit multi-command room or facade presets.
    /// </summary>
    public Dictionary<string, CfgShadowingSceneController> SpecialScenes { get; set; } = [];
}

/// <summary>
/// Scene controller configuration that maps one trigger scene value to command writes.
/// </summary>
public class CfgShadowingSceneController
{
    /// <summary>
    /// Optional room key in the format <c>Building/Floor/Room</c>.
    /// When set, this controller is scoped to the referenced room.
    /// </summary>
    public string? RoomReference { get; set; }

    /// <summary>
    /// Optional explicit reference to the scene value that triggers the command set.
    /// Prefer <see cref="RoomReference"/> for room-specific mappings.
    /// </summary>
    public string? SceneReference { get; set; }

    /// <summary>
    /// Scene number that activates this command set.
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// Write commands keyed by command name.
    /// </summary>
    public Dictionary<string, CfgShadowingSceneCommand> Commands { get; set; } = [];
}

/// <summary>
/// One scene-command write definition.
/// </summary>
public class CfgShadowingSceneCommand
{
    /// <summary>
    /// Reference to the target value to write.
    /// </summary>
    public string? TargetValueReference { get; set; }

    /// <summary>
    /// Numeric command payload.
    /// </summary>
    public double Value { get; set; }
}

public class Special : ModelEntity, IConfigBackedModelEntity
{
    public Special(string name, CfgSpecial config)
    {
        Name = name;
        Configuration = config;
    }

    public CfgSpecial Configuration { get; set; }

    CfgEntity IConfigBackedModelEntity.Configuration => Configuration;
}

/// <summary>
/// Runtime model representation of <see cref="CfgShadowingSpecial"/>.
/// </summary>
public class ShadowingSpecial : Special
{
    public ShadowingSpecial(string name, CfgShadowingSpecial config)
        : base(name, config)
    {
    }

    /// <summary>
    /// Typed source configuration.
    /// </summary>
    public new CfgShadowingSpecial Configuration => (CfgShadowingSpecial)base.Configuration;

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.GlobalShutterSceneReference))]
    public IValue? GlobalShutterScene { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.AutoShadowStatusReference))]
    public IValue? AutoShadowStatus { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.AbsenceReference))]
    public IValue? Absence { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.DisableAutoShadowAssessmentReference))]
    public IValue? DisableAutoShadowAssessment { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.OutdoorTemperatureReference), RequireNumeric = true)]
    public IValue? OutdoorTemperature { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.SunIntensityEastReference), RequireNumeric = true)]
    public IValue? SunIntensityEast { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.SunIntensitySouthReference), RequireNumeric = true)]
    public IValue? SunIntensitySouth { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.SunIntensityWestReference), RequireNumeric = true)]
    public IValue? SunIntensityWest { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.SunPositionAzimuthReference), RequireNumeric = true)]
    public IValue? SunPositionAzimuth { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.SunPositionElevationReference), RequireNumeric = true)]
    public IValue? SunPositionElevation { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.ThermalControlModeReference), RequireNumeric = true)]
    public IValue? ThermalControlMode { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.UvIntensityReference), RequireNumeric = true)]
    public IValue? UvIntensity { get; set; }
}