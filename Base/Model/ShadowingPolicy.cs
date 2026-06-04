namespace HomeCompanion.Base.Model;

/// <summary>
/// Global shadowing automation level.
/// </summary>
public enum ShadowingAutomationLevel
{
    /// <summary>
    /// No automatic shadowing actions are performed.
    /// </summary>
    ManualOnly,

    /// <summary>
    /// Automatic shadowing is active; manual overrides are temporary.
    /// </summary>
    AutomaticWithTemporaryManualOverride,

    /// <summary>
    /// Automatic shadowing is strict and ignores manual scene overrides except safety/interlocks.
    /// </summary>
    AutomaticStrict,
}

/// <summary>
/// Building-level thermal control setting used to derive default room objectives.
/// There's always a thermal control mode in effect, even if it's just the default mode "Passive".
/// This is typically a dynamic value that may be driven by an external input, but it falls back to static configuration when no input is available.
/// </summary>
/// <remarks>
/// mappings=[0="Undefined", 10="Heating", 20="Light heating", 25="Passive", 30="Cooling", 40="Heat protect"]
/// </remarks>
public enum ThermalControlMode
{
    /// <summary>
    /// Thermal control mode is not defined, normally due to a lack of proper initialization or configuration.
    /// </summary>
    Undefined = 0,

    /// <summary>
    /// Cold season, thermal control prioritizes heating and cold protection.
    /// Sun irradiation is welcome to heat the building, shutters are kept open unless special conditions apply, e.g. to prevent uv irradiation.
    /// </summary>
    Winter = 10,

    /// <summary>
    /// Thermal control prioritizes light heating.
    /// Sun irradiation is welcome to heat the building which easily cools down again, over night or when windows are opened.
    /// </summary>
    LightHeating = 20,

    /// <summary>
    /// Thermal control is disabled; daylight is preferred by default.
    /// </summary>
    Passive = 25,

    /// <summary>
    /// Thermal control is active in balanced mode.
    /// The system tries to balance daylight and thermal protection, e.g. by only closing when the sun is strong or when outdoor temperature is high.
    /// </summary>
    BalancedCooling = 30,

    /// <summary>
    /// Thermal control prioritizes cooling and overheating prevention.
    /// </summary>
    CoolingPriority = 40,
}

/// <summary>
/// Effective room objective profile used by policy resolution.
/// </summary>
public enum RoomObjectiveProfile
{
    /// <summary>
    /// Inherit objective from building-level thermal control mode.
    /// </summary>
    InheritFromThermalControl,

    /// <summary>
    /// Balanced default objective.
    /// </summary>
    BalancedDefault,

    /// <summary>
    /// Prefer daylight, accepting higher thermal load.
    /// </summary>
    DaylightPriority,

    /// <summary>
    /// Prefer thermal protection and cooling.
    /// </summary>
    ThermalPriority,

    /// <summary>
    /// Prefer preserving a minimal shadow position for UV protection.
    /// </summary>
    UvProtectionPriority,
}

/// <summary>
/// Scheduler engine used for room schedule evaluation.
/// </summary>
public enum ShadowingScheduleEngine
{
    /// <summary>
    /// Lightweight in-process schedule evaluation.
    /// </summary>
    InProcess,

    /// <summary>
    /// Quartz-based schedule evaluation.
    /// </summary>
    Quartz,
}

/// <summary>
/// Input-driven objective selector rule for future room-level objective adaptation.
/// </summary>
public class CfgObjectiveSelectorInput
{
    /// <summary>
    /// Reference to the input value used by this rule.
    /// </summary>
    public string? ValueReference { get; set; }

    /// <summary>
    /// Rule threshold compared against input value.
    /// </summary>
    public double Threshold { get; set; }

    /// <summary>
    /// Objective selected when input value is greater than or equal to <see cref="Threshold"/>.
    /// </summary>
    public RoomObjectiveProfile ProfileAtOrAboveThreshold { get; set; } = RoomObjectiveProfile.ThermalPriority;

    /// <summary>
    /// Objective selected when input value is below <see cref="Threshold"/>.
    /// </summary>
    public RoomObjectiveProfile ProfileBelowThreshold { get; set; } = RoomObjectiveProfile.BalancedDefault;
}

/// <summary>
/// Dynamic rule for resolving effective facade incidence cut-over angle.
/// </summary>
public class CfgDynamicCutoverAngleRule
{
    /// <summary>
    /// Optional thermal-control mode filter.
    /// When set, the rule only applies in this thermal-control mode.
    /// </summary>
    public ThermalControlMode? ThermalControlMode { get; set; }

    /// <summary>
    /// Optional inclusive lower outdoor-temperature bound in degrees Celsius.
    /// </summary>
    public double? OutdoorTemperatureMin { get; set; }

    /// <summary>
    /// Optional inclusive upper outdoor-temperature bound in degrees Celsius.
    /// </summary>
    public double? OutdoorTemperatureMax { get; set; }

    /// <summary>
    /// Cut-over angle in degrees to apply when this rule matches.
    /// </summary>
    public double CutoverAngle { get; set; }
}

/// <summary>
/// Cron-style schedule transition for room shutter scene changes.
/// </summary>
public class CfgRoomScheduleTransition
{
    /// <summary>
    /// Cron-style expression defining when this transition fires.
    /// </summary>
    public string CronExpression { get; set; } = string.Empty;

    /// <summary>
    /// Scene number to write when this transition is triggered.
    /// </summary>
    public int Scene { get; set; }

    /// <summary>
    /// When true, this schedule is treated as close-only and does not imply automatic reopening.
    /// </summary>
    public bool CloseOnly { get; set; } = true;

    /// <summary>
    /// Grace period after manual opening before automatic shadow translation may occur.
    /// </summary>
    public TimeSpan ManualOpenGracePeriod { get; set; } = TimeSpan.FromMinutes(45);

    /// <summary>
    /// Enables translation from manual-open to automatic shadow after the grace period.
    /// </summary>
    public bool EnableShadowTranslationAfterManualOpen { get; set; } = true;

    /// <summary>
    /// Optional relative delay after the transition trigger when the room scene is moved back
    /// to an automation scene.
    /// </summary>
    public TimeSpan? ResumeAutomationAfter { get; set; }

    /// <summary>
    /// Optional daily local time-of-day when the room scene is moved back to an automation scene.
    /// If the configured time is before the transition trigger time, the next day is used.
    /// </summary>
    public TimeSpan? ResumeAutomationAtLocalTime { get; set; }

    /// <summary>
    /// Optional target scene used for auto-resume. When omitted, the room uses its first configured
    /// resume-automation scene.
    /// </summary>
    public int? ResumeAutomationScene { get; set; }
}
