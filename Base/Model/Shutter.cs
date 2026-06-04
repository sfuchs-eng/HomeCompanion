using System.Text.Json.Serialization;
using HomeCompanion.Abstractions.Serialization;
using HomeCompanion.Values;

namespace HomeCompanion.Base.Model;

/// <summary>
/// Configuration for a shutter.
/// </summary>
public class CfgShutter : CfgEntity
{
    /// <summary>
    /// The type of the shutter, defining its machinal properties, basic features and control method.
    /// </summary>
    public ShutterType Type { get; set; } = ShutterType.VenetianBlind;

    /// <summary>
    /// Reference to the facade the shutter is part of for object linkage in the model
    /// </summary>
    /// <value></value>
    public string? FacadeReference { get; set; }

    /// <summary>
    /// Constraints for the shutter, defining its behavior.
    /// OR'ed with room-level and building-level defaults defined in <see cref="CfgRoom.ShutterConstraints"/> and <see cref="CfgShadowingSpecial.DefaultShutterConstraints"/>.
    /// </summary>
    public ShutterConstraints Constraints { get; set; } = ShutterConstraints.None;

    /// <summary>
    /// The actual shutter constraints are derived from <see cref="EffectiveConstraints(ShutterConstraints)"/>
    /// </summary>
    public ShutterConstraints? RoomConstraintsMask { get; set; }

    public ShutterConstraints EffectiveConstraints(ShutterConstraints roomConstraints)
    {
        var mask = RoomConstraintsMask ?? ShutterConstraints.None;
        return (roomConstraints & ~mask) | Constraints;
    }

    /// <summary>
    /// Optional reference to the value that carries the shutter position.
    /// </summary>
    /// <remarks>
    /// Supports flexible formats, including <c>ContainerType[ContainerName]:ValueName</c>.
    /// </remarks>
    public string? PositionValueReference { get; set; }

    /// <summary>
    /// Optional reference to the value that carries the shutter lamella angle.
    /// Required for <see cref="ShutterType.VenetianBlind"/>, ignored for other shutter types.
    /// </summary>
    /// <remarks>
    /// Supports flexible formats, including <c>ContainerType[ContainerName]:ValueName</c>.
    /// </remarks>
    public string? AngleValueReference { get; set; }

    /// <summary>
    /// For Shutters of type <see cref="ShutterType.OpenClose"/>, a reference to the value that carries the open/close state.
    /// </summary>
    public string? OpenCloseReference { get; set; }

    /// <summary>
    /// Do not close shutter beyond this position in percent.
    /// [%]
    /// </summary>
    public int MaxClose { get; set; } = 100;

    /// <summary>
    /// Default angle for shadowing in case of missing or zero angle value, e.g. for a venetian blind.
    /// [%]
    /// </summary>
    public int DefaultShadowSlat { get; set; } = 45;

    /// <summary>
    /// Shutter-local sun-position zones that affect whether this shutter should be treated as naturally shadowed.
    /// </summary>
    public Dictionary<string, CfgShadowingZone> ShadowingZones { get; set; } = [];
}

/// <summary>
/// Defines a sun-position box that changes shutter behavior when the sun is inside or outside the box.
/// </summary>
public class CfgShadowingZone
{
    /// <summary>
    /// Zone matching mode.
    /// </summary>
    public ShadowingZoneMode Mode { get; set; } = ShadowingZoneMode.Inside;

    /// <summary>
    /// Optional azimuth lower bound in degrees.
    /// </summary>
    public double? AzimuthMin { get; set; }

    /// <summary>
    /// Optional azimuth upper bound in degrees.
    /// </summary>
    public double? AzimuthMax { get; set; }

    /// <summary>
    /// Optional elevation lower bound in degrees.
    /// </summary>
    public double? ElevationMin { get; set; }

    /// <summary>
    /// Optional elevation upper bound in degrees.
    /// </summary>
    public double? ElevationMax { get; set; }
}

/// <summary>
/// Determines if a zone applies when sun is inside or outside the configured box.
/// </summary>
public enum ShadowingZoneMode
{
    /// <summary>
    /// The shutter is affected by sun exposure if the sun position is within 90°deg spheric range of the shutter's normal.
    /// Box bounds are ignored in this mode, if configured at all.
    /// </summary>
    Default,

    /// <summary>
    /// There can only be sun exposure when the Sun is inside this box.
    /// </summary>
    Inside,

    /// <summary>
    /// There might be sun exposure when the Sun is outside this box yet 90°deg in spheric range of the shutter's normal.
    /// </summary>
    Outside,
}

public enum ShutterType
{
    /// <summary>
    /// only open/close supported, e.g. for a garage door
    /// </summary>
    OpenClose,

    /// <summary>
    /// 0% fully open, 100% fully closed, e.g. for a roller blind without tilt angle
    /// </summary>
    Positional,

    /// <summary>
    /// 0% fully open, 100% fully closed, with tilt angle for lamellae (0% horizontal/open, 100% vertical/closed), e.g. for a venetian blind
    /// </summary>
    VenetianBlind,
}

[JsonConverter(typeof(CommaSeparatedFlagsEnumJsonConverter<ShutterConstraints>))]
[Flags]
public enum ShutterConstraints
{
    None = 0,

    /// <summary>
    /// The shutter should never be closed by automation, e.g. for a window that should always be open for daylight or ventilation reasons.
    /// Manual closing by the user is still possible and should be respected by automation until the next manual opening.
    /// </summary>
    KeepOpen = 1,

    /// <summary>
    /// The shutter should not be reopened by automation after being closed / shadowed, not even over night.
    /// <summary>
    LeaveClosed = 2,

    /// <summary>
    /// Shuttter used for burglar protection, should be closed when the house is empty and/or during dusk/night.
    /// It may have a special triggers assigned, e.g. NightActive or AbsenceActive, and should react on these triggers by closing.
    /// It shall be left closed in case LeaveClosed is set, otherwise it may be reopened by automation when the trigger condition is over (e.g. in the morning or when presence is detected).
    /// </summary>
    AntiBurglar = 4,

    /// <summary>
    /// Aggressive shadowing for sun protection: the shutter should be closed whenever there is sun irradiation, even if shadowing is not desired or presence is detected in the house.
    /// It may be reopened when sun exposure is over unless LeaveClosed is set.
    /// </summary>
    AggressiveSunProtection = 8,

    /// <summary>
    /// Shadowing only in case of strongly uncomfortable sun irradiation, e.g. in the afternoon in summer and generally warm conditions, but not during cold seasons.
    /// </summary>
    CautiousSunProtection = 16,

    /// <summary>
    /// Always permit manual operation of the shutter regardless of automation rules.
    /// </summary>
    ManualOverride = 32,
}

/// <summary>
/// Runtime representation of a shutter.
/// </summary>
public class Shutter : ModelEntity, IConfigBackedModelEntity
{
    public Shutter(string name, CfgShutter config)
    {
        Name = name;
        Configuration = config;
    }

    /// <summary>
    /// Source configuration used to create this runtime model instance.
    /// </summary>
    public CfgShutter Configuration { get; set; }

    /// <summary>
    /// Bound runtime position value resolved from <see cref="CfgShutter.PositionValueReference"/>.
    /// </summary>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShutter.PositionValueReference), RequireNumeric = true)]
    public IValue? PositionValue { get; set; }

    /// <summary>
    /// Bound runtime angle value resolved from <see cref="CfgShutter.AngleValueReference"/>.
    /// </summary>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShutter.AngleValueReference), RequireNumeric = true)]
    public IValue? AngleValue { get; set; }

    CfgEntity IConfigBackedModelEntity.Configuration => Configuration;
}
