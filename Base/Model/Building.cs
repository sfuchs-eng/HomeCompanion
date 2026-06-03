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
    /// Default room temperature threshold used for auto shadowing when no room-specific value is configured.
    /// </summary>
    public double DefaultTemperatureThreshold { get; set; } = 25.0;

    /// <summary>
    /// Do not assess shading if sun elevation is below this value.
    /// </summary>
    public double MinSunElevationToConsider { get; set; } = 4.0;

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
    /// Reference to the scene value that triggers the command set.
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
}