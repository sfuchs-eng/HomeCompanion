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
    /// Default automation level used when rooms do not define an override.
    /// </summary>
    [Obsolete("solve via room scene semantics")]
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
    [Obsolete("solve differently")]
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

    public float UvIntensityThreshold { get; set; } = 300.0f;

    public Dictionary<byte, CfgRoomSceneShutterPreset> SceneShutterPresets { get; set; } = new Dictionary<byte, CfgRoomSceneShutterPreset>
    {
        [(byte)RoomShutterScene.CleanShutter] = new CfgRoomSceneShutterPreset { Label = "Clean shutters", Position = 100.0, Slat = 0.0 },
        [(byte)RoomShutterScene.CleanWindow] = new CfgRoomSceneShutterPreset { Label = "Clean window", Position = 0.0, Slat = 0.0 },
        [(byte)RoomShutterScene.DryShutter] = new CfgRoomSceneShutterPreset { Label = "Closed with slats steep: drop water and allow airflow", Position = 100.0, Slat = 60.0 },
    };

    /// <summary>
    /// Scene controllers keyed by scene key for explicit multi-command room or facade presets.
    /// </summary>
    [Obsolete("to be replaced / redesigned")]
    public Dictionary<string, CfgShadowingSceneController> SpecialScenesAIAttempt { get; set; } = [];
}

public class CfgRoomSceneShutterPreset
{
    public string? Label { get; set; }

    /// <summary>
    /// Reference to a shutter position value.
    /// </summary>
    public double Position { get; set; }

    /// <summary>
    /// Reference to a shutter slat angle value.
    /// </summary>
    public double Slat { get; set; }

    /// <summary>
    /// <see cref="Open"/> and <see cref="Closed"/> properties translate the position and slat values into boolean states for convenient use with open/close capable shutters.
    /// </summary>
    public bool Open => Position < Double.Epsilon;

    /// <summary>
    /// <see cref="Open"/> and <see cref="Closed"/> properties translate the position and slat values into boolean states for convenient use with open/close capable shutters.
    /// </summary>
    public bool Closed => Position > 100.0 - Double.Epsilon && Slat > 100.0 - Double.Epsilon;
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

    /// <summary>
    /// See <see cref="HomeCompanion.Logics.ThermalControl.ThermalControlMode"/> for the meaning of this value and valid ranges.
    /// Defines whether the building is in cooling-priority, heating-priority or balanced mode.
    /// Affects whether there are automatic transitions into room shutter scenes for automatic shadowing and whether certain manual overrides might be prevented.
    /// </summary>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.ThermalControlModeReference), RequireNumeric = true)]
    public IValue<byte>? ThermalControlMode { get; set; }

    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgShadowingSpecial.UvIntensityReference), RequireNumeric = true)]
    public IValue<float>? UvIntensity { get; set; }
}

public static class ShadowingSpecialExtensions
{
    public static IEnumerable<ShadowingSpecial> EnumerateShadowingSpecials(this Model model)
    {
        foreach (var building in model.Buildings.Values)
        {
            foreach (var special in building.Specials.Values)
            {
                if (special is ShadowingSpecial shadowingSpecial)
                    yield return shadowingSpecial;
            }
        }
    }

    /// <summary>
    /// Gets a single shadowing special for a given building.
    /// There must be only 1 shadowing special per building, otherwise false is returned and the out parameter is null.
    /// </summary>
    /// <param name="building"></param>
    /// <param name="shadowingSpecial"></param>
    /// <returns></returns>
    public static bool TryGetShadowingSpecial(this Building building, out ShadowingSpecial shadowingSpecial)
    {
        shadowingSpecial = building.Specials.Values.OfType<ShadowingSpecial>().SingleOrDefault()!;
        return shadowingSpecial != null;
    }

    public static ShadowingSpecial GetShadowingSpecial(this Building building)
    {
        if (!building.TryGetShadowingSpecial(out var special))
            throw new InvalidOperationException($"Building '{building.Name}' does not contain a shadowing special.");
        return special;
    }

    public static bool TryGetRoomSceneShutterPreset(this RoomKey roomKey, byte scene, out CfgRoomSceneShutterPreset? preset)
    {
        // get building level, override with room level if available
        if (roomKey.Room.Configuration.SceneShutterPresets.TryGetValue(scene, out var roomPreset))
        {
            preset = roomPreset;
            return true;
        }
        if (roomKey.Building.TryGetShadowingSpecial(out var shadowingSpecial) && shadowingSpecial.Configuration.SceneShutterPresets.TryGetValue(scene, out var buildingPreset))
        {
            preset = buildingPreset;
            return true;
        }
        preset = null;
        return false;
    }
}
