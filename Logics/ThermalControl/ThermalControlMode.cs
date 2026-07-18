namespace HomeCompanion.Logics.ThermalControl;

public enum ThermalControlMode : byte
{
    /// <summary>
    /// Not initialized. Determine appropriate status and set it.
    /// </summary>
    Undefined = 0,

    /// <summary>
    /// It's really cold.
    /// Heat, use all sources to heat the house - e.g. no auto-shadowing
    /// </summary>
    Winter = 10,

    /// <summary>
    /// Passive with a trend to cool-off without slight heating measure be taken.
    /// E.g. don't auto-enable auto-shadow in order to use sun.
    /// </summary>
    LightHeating = 20,

    /// <summary>
    /// No measures to cool or heat
    /// heat pump / cooling expected to be off,
    /// shadowing merely manual unless absence mode active
    /// </summary>
    Passive = 25,

    /// <summary>
    /// Cooling active but ambient temperatures in average such that
    /// house can be kept cool easily
    /// </summary>
    Cooling = 30,

    /// <summary>
    /// Do everything possible to keep the house as cool as possible
    /// </summary>
    HeatProtect = 40,

    InternalConversionFailure = 100,
}

public static class ThermalControlModeExtensions
{
    public static ThermalControlMode TryGetThermalControlMode(this IValue<byte>? value)
    {
        try
        {
            if (value == null)
                return ThermalControlMode.Undefined;

            var modeByte = value.Value;
            if (!Enum.IsDefined(typeof(ThermalControlMode), modeByte))
                return ThermalControlMode.Undefined;

            return (ThermalControlMode)modeByte;
        }
        catch
        {
            return ThermalControlMode.InternalConversionFailure;
        }
    }
}