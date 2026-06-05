namespace HomeCompanion.Base.Model;

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
