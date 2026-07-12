using HomeCompanion.Base.Utilities;

namespace HomeCompanion.Base.Model;

/// <summary>
/// Building-wide shadowing configuration hosted as a typed special under <see cref="CfgBuilding.Specials"/>.
/// </summary>
public class CfgShadowingSpecial : CfgBuildingSpecial
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

    public AntiBurglarSettings AntiBurglar { get; set; } = new AntiBurglarSettings();

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
    public ThermalControlMode ThermalControl { get; set; } = ThermalControlMode.BalancedCooling;

    /// <summary>
    /// Optional reference to an externally managed thermal-control mode value.
    /// If bound and initialized, this value overrides <see cref="ThermalControl"/>.
    /// </summary>
    public string? ThermalControlModeReference { get; set; }

    /// <summary>
    /// Enables persistence of manual overrides by default.
    /// </summary>
    public bool PersistManualOverrides { get; set; } = true;

    /// <summary>
    /// Default duration used for temporary manual overrides if a room does not override it.
    /// </summary>
    public TimeSpan DefaultRoomSceneManualOverrideDuration { get; set; } = TimeSpan.FromHours(2);

    public TimeSpan DefaultShutterMaxManualOverrideDuration { get; set; } = TimeSpan.FromMinutes(90);

    /// <summary>
    /// Scene numbers that clear manual override and resume automation.
    /// Defaults to legacy-compatible scenes 50 and 52.
    /// </summary>
    public List<int> ResumeAutomationScenes { get; set; } = [50, 52];

    /// <summary>
    /// Reference to the global shutter scene value.
    /// </summary>
    public string? GlobalShutterSceneReference { get; set; }

    /// <summary>
    /// Reference to the global auto shadow status value.
    /// </summary>
    public string? AutoShadowStatusReference { get; set; }

    /// <summary>
    /// Default room temperature control target used for auto shadowing when no room-specific value is configured.
    /// </summary>
    public double DefaultRoomTemperatureTarget { get; set; } = 22.0;

    /// <summary>
    /// Factor by which the daily average difference between indoor and outdoor temperature is scaled to compute the daily net energy balance in p.u. (per unit).
    /// As it's merely about deciding whether to leave shutters open for daylight or to have them cast shadows, the scaling factor is set to 1/10 by default, meaning that a 10°C difference corresponds to 1.0 p.u. energy balance.
    /// </summary>
    public double EnergyBalanceTemperatureScalingFactor { get; set; } = 1.0/10.0; // 10°C difference corresponds to 1.0 p.u. energy balance

    /// <summary>
    /// If we're in cautious shadowing mode, this threshold defines wether to leave shutters open for daylight or to have them cast shadows.
    /// </summary>
    /// <value></value>
    public double CautiousShadowingEnergyBalanceThresholdPU { get; set; } = 0.5;
    
    public double CautiousShadowingEnergyBalanceThresholdHysteresisPU { get; set; } = 0.2;

    /// <summary>
    /// Reference to the global absence status value.
    /// </summary>
    public string? AbsenceReference { get; set; }

    /// <summary>
    /// Shutters are not allowed to be opened when this value is true, e.g. for noise reasons during night time.
    /// Moving slats is still allowed, e.g. to allow some light and airflow while still keeping noise minimal.
    /// </summary>
    /// <value></value>
    public string? NightModeReference { get; set; }

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

    public float SunIntensityPUNorm { get; set; } = 100000.0f;

    public string? GlobalIlluminanceReference { get; set; }

    public float GlobalIlluminancePUReference { get; set; } = 1000.0f;

    /// <summary>
    /// Optional reference to a sun azimuth value in degrees.
    /// </summary>
    public string? SunPositionAzimuthReference { get; set; }

    /// <summary>
    /// Optional reference to a sun elevation value in degrees.
    /// </summary>
    public string? SunPositionElevationReference { get; set; }

    public TimeSpan SunIntensityHysteresisDuration { get; set; } = TimeSpan.FromMinutes(30.0);
    public float SunIntensityShadowThresholdPU { get; set; } = .2f;
    public float SunIntensityRelaxationThresholdPU { get; set; } = .1f;

    /// <summary>
    /// Optional reference to UV index or UV intensity value.
    /// </summary>
    public string? UvIntensityReference { get; set; }

    public float UvIntensityPUNorm { get; set; } = 100000.0f;

    public float UvIntensityThresholdPU { get; set; } = 0.1f;
    public float UvIntensityRelaxationThresholdPU { get; set; } = 0.001f;
    public TimeSpan UvIntensityHysteresisDuration { get; set; } = TimeSpan.FromHours(3.0);

    /// <summary>
    /// Optional room-level dynamic cut-over angle rules.
    /// </summary>
    public List<CfgDynamicCutoverAngleRule> FacadeSunCutoverAngleDynamicRules { get; set; } = [];

    public bool ExecuteHardScenes { get; set; } = true;

    public Dictionary<byte, CfgRoomSceneShutterPreset> SceneShutterPresets { get; set; } = new Dictionary<byte, CfgRoomSceneShutterPreset>
    {
        [(byte)RoomShutterScene.CleanShutter] = new CfgRoomSceneShutterPreset { Label = "Clean shutters", Position = 100.0, Slat = 0.0 },
        [(byte)RoomShutterScene.CleanWindow] = new CfgRoomSceneShutterPreset { Label = "Clean window", Position = 0.0, Slat = 0.0 },
        [(byte)RoomShutterScene.DryShutter] = new CfgRoomSceneShutterPreset { Label = "Closed with slats steep: drop water and allow airflow", Position = 100.0, Slat = 60.0 },
    };
}

public class AntiBurglarSettings
{
    public TimeSpan EarliestClosureTime { get; set; } = TimeSpan.FromHours(16.0);
    public TimeSpan LatestClosureTime { get; set; } = TimeSpan.FromHours(22.0);
    public TimeSpan EarliestOpeningTime { get; set; } = TimeSpan.FromHours(6.0);
    public TimeSpan LatestOpeningTime { get; set; } = TimeSpan.FromHours(9.0);
    public bool EnableAutomaticReopening { get; set; } = false;
    public double DuskTriggerLowerThresholdLux { get; set; } = 300.0;
    public double DuskTriggerHysteresisLux { get; set; } = 200.0;

    public double DuskTriggerUpperThresholdLux => DuskTriggerLowerThresholdLux + DuskTriggerHysteresisLux;

    /// <summary>
    /// GlobalIlluminance must be above <see cref="DuskTriggerUpperThresholdLux"/> for this duration before anti-burglar closure is lifted.
    /// </summary>
    public TimeSpan DuskRelaxationDuration { get; set; } = TimeSpan.FromMinutes(30.0);
}

public class CfgRoomSceneShutterPreset
{
    public string? Label { get; set; }

    /// <summary>
    /// A shutter position value, 0 = fully open, 1.0 = fully closed.
    /// </summary>
    public double Position { get; set; }

    /// <summary>
    /// A shutter slat angle value, 0 = horizontal/open, 1.0 = vertical/closed.
    /// </summary>
    public double Slat { get; set; }

    /// <summary>
    /// Readonly, derived from <see cref="Position"/>.
    /// <see cref="Open"/> and <see cref="Closed"/> properties translate the position and slat values into boolean states for convenient use with open/close capable shutters.
    /// </summary>
    /// <value>True if fully open</value>
    public bool Open => Position < Double.Epsilon;

    /// <summary>
    /// Readonly, derived from <see cref="Position"/> and <see cref="Slat"/>.
    /// <see cref="Open"/> and <see cref="Closed"/> properties translate the position and slat values into boolean states for convenient use with open/close capable shutters.
    /// </summary>
    /// <value>True if fully closed incl Slat >= 100%</value>
    public bool Closed => Position > 1.0 - Double.Epsilon && Slat > 1.0 - Double.Epsilon;
}

/// <summary>
/// Scene controller configuration that maps one trigger scene value to command writes.
/// </summary>
[Obsolete("to be replaced / redesigned")]
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
[Obsolete("to be replaced / redesigned")]
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

/// <summary>
/// Runtime model representation of <see cref="CfgShadowingSpecial"/>.
/// </summary>
public class ShadowingSpecial(string name, CfgShadowingSpecial config) : Special<CfgShadowingSpecial>(name, config), IBuildingSpecial
{
    /// <summary>
    /// <see cref="RoomShutterScene"/> values or others
    /// </summary>
    /// <value></value>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.GlobalShutterSceneReference))]
    public IValue<byte>? GlobalShutterScene { get; set; }

    /// <summary>
    /// Shadowing output: set to true when the building is in auto shadowing mode, false when manual override is active.
    /// </summary>
    /// <value></value>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.AutoShadowStatusReference))]
    public IValue<bool>? AutoShadowStatus { get; set; }

    /// <summary>
    /// Shadowing input: True when the building is in absence mode, false otherwise.
    /// </summary>
    /// <value></value>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.AbsenceReference))]
    public IValue<bool>? Absence { get; set; }

    /// <summary>
    /// Shadowing input: True when the building is in night mode, false otherwise.
    /// </summary>
    /// <value></value>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.NightModeReference))]
    public IValue<bool>? NightMode { get; set; }

    /// <summary>
    /// Shadowing input: if true, despite <see cref="ThermalControlMode.CoolingPriority"/>, automatic shadowing must not be started (do not automatically transition into auto-shadowing room scenes).
    /// </summary>
    /// <value></value>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.DisableAutoShadowAssessmentReference))]
    public IValue<bool>? DisableAutoShadowAssessment { get; set; }

    /// <summary>
    /// Shadowing input: Outdoor temperature in degrees Celsius.
    /// </summary>
    /// <value></value>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.OutdoorTemperatureReference), RequireNumeric = true)]
    public IValue<float>? OutdoorTemperature { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.SunIntensityEastReference), RequireNumeric = true)]
    public IValue<float>? SunIntensityEast { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.SunIntensitySouthReference), RequireNumeric = true)]
    public IValue<float>? SunIntensitySouth { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.SunIntensityWestReference), RequireNumeric = true)]
    public IValue<float>? SunIntensityWest { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.GlobalIlluminanceReference), RequireNumeric = true)]
    public IValue<float>? GlobalIlluminance { get; set; }

    /// <summary>
    /// Shadowing input: Sun position azimuth in degrees.
    /// </summary>
    /// <value></value>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.SunPositionAzimuthReference), RequireNumeric = true)]
    public IValue<float>? SunPositionAzimuth { get; set; }

    /// <summary>
    /// Shadowing input: Sun position elevation in degrees.
    /// </summary>
    /// <value></value>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.SunPositionElevationReference), RequireNumeric = true)]
    public IValue<float>? SunPositionElevation { get; set; }

    public SphericVector? SunPosition => (SunPositionAzimuth?.IsValid ?? false) && (SunPositionElevation?.IsValid ?? false)
        ? SphericVector.FromDegrees(SunPositionAzimuth!.Value, SunPositionElevation!.Value) : null;

    /// <summary>
    /// See <see cref="HomeCompanion.Logics.ThermalControl.ThermalControlMode"/> for the meaning of this value and valid ranges.
    /// Defines whether the building is in cooling-priority, heating-priority or balanced mode.
    /// Affects whether there are automatic transitions into room shutter scenes for automatic shadowing and whether certain manual overrides might be prevented.
    /// </summary>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.ThermalControlModeReference), RequireNumeric = true)]
    public IValue<byte>? ThermalControlMode { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.UvIntensityReference), RequireNumeric = true)]
    public IValue<float>? UvIntensity { get; set; }

    #region Interpretation support

    public bool IsAbsenceModeActive => (Absence?.IsValid ?? false) && (Absence?.Value ?? false);

    public ThermalControlMode ResolvedThermalControlMode()
    {
        // dynamic valid value?
        var dynamicMode = ThermalControlMode?.IsValid ?? false ? ThermalControlMode?.Value : null;
        if ( dynamicMode.HasValue && Enum.IsDefined(typeof(ThermalControlMode), dynamicMode.Value))
        {
            return (ThermalControlMode)dynamicMode.Value;
        }
        else
        {
            return Configuration.ThermalControl;
        }
    }

    #endregion
}
