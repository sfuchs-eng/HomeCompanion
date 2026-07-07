using System.Text.Json.Serialization;
using HomeCompanion.Abstractions.Serialization;
using HomeCompanion.Logics.Shutters;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;

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
    /// Each shutter must be assigned to a facade.
    /// </summary>
    public string? FacadeReference { get; set; }

    /// <summary>
    /// Constraints for the shutter, defining its behavior.
    /// OR'ed with room-level and building-level defaults defined in <see cref="CfgRoom.ShutterConstraints"/> and <see cref="CfgShadowingSpecial.DefaultShutterConstraints"/>.
    /// </summary>
    public ShutterConstraints Constraints { get; set; } = ShutterConstraints.None;

    /// <summary>
    /// Optional mask applied to the room-level shutter constraints before individual shutter constraints are added (set flags "clear" room level set flags before or'ing shutter level Constraints in).
    /// </summary>
    public ShutterConstraints? RoomConstraintsMask { get; set; }

    /// <summary>
    /// Optional room-level override for the facade incidence cut-over angle in degrees.
    /// </summary>
    public double? FacadeSunCutoverAngleOverride { get; set; }

    /// <summary>
    /// Optional reference to the value that carries the shutter position.
    /// Must be an IValue with numeric type and percent unit, where 0% means fully open and 100% (value 100) means fully closed.
    /// </summary>
    /// <remarks>
    /// Supports flexible formats, including <c>ContainerType[ContainerName]:ValueName</c>.
    /// </remarks>
    public string? PositionValueReference { get; set; }

    /// <summary>
    /// IVValue scaling factor for the shutter position value:
    /// PositionValue * ScaleFactorPosition is written, and PositionValue is read as PositionValue / ScaleFactorPosition.
    /// Default is suitable for a KNX DPST-5-1 and/or OpenHAB linked % value in an IValue<double> with unit percent, where 0 means fully open and 100 means fully closed.
    /// </summary>
    /// <remarks>
    /// The shadowing system uses the position value in p.u. (0.0 open, 1.0 closed) for its calculations, so the scaling factor should be set accordingly.
    /// </remarks>
    /// <value></value>
    public double ScaleFactorPosition { get; set; } = 100.0;

    /// <summary>
    /// Optional reference to the value that carries the shutter lamella angle.
    /// Required for <see cref="ShutterType.VenetianBlind"/>, ignored for other shutter types.
    /// Must be an IValue with numeric type and percent unit, where 0% means fully open/horizontal and 100% (value 100) means fully closed/vertical.
    /// </summary>
    /// <remarks>
    /// Supports flexible formats, including <c>ContainerType[ContainerName]:ValueName</c>.
    /// </remarks>
    public string? AngleValueReference { get; set; }

    /// <summary>
    /// IValue scaling factor for the shutter lamella angle:
    /// AngleValue * ScaleFactorAngle is written, and AngleValue is read as AngleValue / ScaleFactorAngle.
    /// Default is suitable for a KNX DPST-5-1 and/or OpenHAB linked % value in an IValue<double> with unit percent, where 0 means fully open/horizontal and 100 means fully closed/vertical.
    /// </summary>
    /// <remarks>
    /// The shadowing system uses the angle value in p.u. (0.0 horizontal, 1.0 vertical) for its calculations, so the scaling factor should be set accordingly.
    /// </remarks>
    /// <value></value>
    public double ScaleFactorAngle { get; set; } = 100.0;

    /// <summary>
    /// For Shutters of type <see cref="ShutterType.OpenClose"/>, a reference to the value that carries the open/close state.
    /// Must be an IValue<bool> where false means open and true means closed.
    /// </summary>
    /// <remarks>
    /// Supports flexible formats, including <c>ContainerType[ContainerName]:ValueName</c>.
    /// </remarks>
    public string? OpenCloseReference { get; set; }

    /// <summary>
    /// IValue<bool> reference that indicates whether the shutter is released for closure, e.g. by a user action or a scene.
    /// If the value is false, the shutter must be kept open: e.g. such that people can pass through.
    /// </summary>
    public string? ReleasedForClosureReference { get; set; }

    public bool InvertReleasedForClosure { get; set; } = false;

    /// <summary>
    /// Do not close shutter beyond this position in percent.
    /// p.u., where 0 means fully open and 1 means fully closed.
    /// </summary>
    public double MaxClose { get; set; } = 1.0;

    /// <summary>
    /// Default angle (in p.u., 0.0 horizontal, 1.0 vertical) for shadowing in case of missing or zero angle value, e.g. for a venetian blind.
    /// p.u.
    /// </summary>
    public double DefaultShadowSlat { get; set; } = 45.0 / 90;

    public TimeSpan? MaxManualOverrideDuration { get; set; }

    /// <summary>
    /// Shutter-local sun-position zones that affect whether this shutter should be treated as naturally shadowed.
    /// </summary>
    public Dictionary<string, CfgShadowingZone> ShadowingZones { get; set; } = [];

    /// <summary>
    /// Optional room-level dynamic cut-over angle rules.
    /// If configured, these rules override building-level dynamic cut-over rules for this room.
    /// </summary>
    public List<CfgDynamicCutoverAngleRule> FacadeSunCutoverAngleDynamicRules { get; set; } = [];
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
    /// It may be reopened when sun exposure is over unless LeaveClosed is set. E.g. rooms where no people are present but interior must be protected from direct sunlight.
    /// </summary>
    AggressiveSunProtection = 8,

    /// <summary>
    /// Shadowing only in case of strongly uncomfortable sun irradiation, e.g. in the afternoon in summer and generally warm conditions, but not during cold seasons.
    /// E.g. basement windows where it's normally cool anyhow but dailight is desired
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

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShutter.OpenCloseReference))]
    public IValue<bool>? OpenCloseValue { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShutter.ReleasedForClosureReference))]
    public IValue<bool>? ReleasedForClosureValue { get; set; }

    public Facade? Facade { get; set; }

    CfgEntity IConfigBackedModelEntity.Configuration => Configuration;

    public double GetPositionInPUnit()
    {
        if (PositionValue is null)
            return -1.0;
        var pos = PositionValue.GetNumericValueOrNull();
        if (!pos.HasValue)
            return -1.0;
        return pos.Value / Configuration.ScaleFactorPosition;
    }

    public double GetAngleInPUnit()
    {
        if (AngleValue is null)
            return -1.0;
        var angle = AngleValue.GetNumericValueOrNull();
        if (!angle.HasValue)
            return -1.0;
        return angle.Value / Configuration.ScaleFactorAngle;
    }

    public void WritePositionInPUnit(double pUnitValue, object? initiator = null, ILogger? logger = null)
    {
        if (PositionValue is null)
            throw new InvalidOperationException("Cannot write position value because PositionValue is not bound.");
        if (pUnitValue < 0.0)
            return; // no-op semantics
        if (pUnitValue > 1.0)
            throw new ArgumentOutOfRangeException(nameof(pUnitValue), "Position value must be in the range [0.0, 1.0].");
        if (!PositionValue.TryWriteNumeric(pUnitValue * Configuration.ScaleFactorPosition, initiator, logger))
        {
            logger?.LogWarning("Failed to write position value {PositionValue} for shutter {ShutterKey}.", pUnitValue, Name);
        }
    }

    public void WriteAngleInPUnit(double pUnitValue, object? initiator = null, ILogger? logger = null)
    {
        if (AngleValue is null)
            throw new InvalidOperationException("Cannot write angle value because AngleValue is not bound.");
        if (pUnitValue < 0.0)
            return; // no-op semantics
        if (pUnitValue > 1.0)
            throw new ArgumentOutOfRangeException(nameof(pUnitValue), "Angle value must be in the range [0.0, 1.0].");
        if (!AngleValue.TryWriteNumeric(pUnitValue * Configuration.ScaleFactorAngle, initiator, logger))
        {
            logger?.LogWarning("Failed to write angle value {AngleValue} for shutter {ShutterKey}.", pUnitValue, Name);
        }
    }

    public bool IsClosed => Configuration.Type switch
    {
        ShutterType.OpenClose => OpenCloseValue?.Value ?? false,
        ShutterType.Positional => PositionValue?.GetNumericValueOrNull() >= Configuration.MaxClose * Configuration.ScaleFactorPosition,
        ShutterType.VenetianBlind => PositionValue?.GetNumericValueOrNull() >= Configuration.MaxClose * Configuration.ScaleFactorPosition && AngleValue?.GetNumericValueOrNull() >= Configuration.DefaultShadowSlat * Configuration.ScaleFactorAngle,
        _ => throw new NotImplementedException($"Shutter type {Configuration.Type} not implemented."),
    };

    public bool IsShadowing => Configuration.Type switch
    {
        ShutterType.OpenClose => (OpenCloseValue?.IsValid ?? false) && (OpenCloseValue?.Value ?? false),
        ShutterType.Positional => PositionValue?.GetNumericValueOrNull() >= Configuration.MaxClose * Configuration.ScaleFactorPosition,
        ShutterType.VenetianBlind => PositionValue?.GetNumericValueOrNull() >= Configuration.MaxClose * Configuration.ScaleFactorPosition && AngleValue?.GetNumericValueOrNull() >= Configuration.DefaultShadowSlat * Configuration.ScaleFactorAngle,
        _ => throw new NotImplementedException($"Shutter type {Configuration.Type} not implemented."),
    };

    public bool IsOpen => Configuration.Type switch
    {
        ShutterType.OpenClose => (OpenCloseValue?.IsValid ?? false) && !(OpenCloseValue?.Value ?? false),
        ShutterType.Positional => PositionValue?.GetNumericValueOrNull() <= double.Epsilon,
        ShutterType.VenetianBlind => PositionValue?.GetNumericValueOrNull() <= double.Epsilon,
        _ => throw new NotImplementedException($"Shutter type {Configuration.Type} not implemented."),
    };
}
