using System.Text.Json.Serialization;

namespace HomeCompanion.Base.Model;

/// <summary>
/// Configuration for a shutter.
/// </summary>
public class CfgShutter : CfgEntity
{
    public ShutterType Type { get; set; } = ShutterType.VenetianBlind;
    public ShutterConstraints Constraints { get; set; } = new();
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

[JsonConverter(typeof(JsonStringEnumConverter))]
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
public class Shutter : ModelEntity
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
}
