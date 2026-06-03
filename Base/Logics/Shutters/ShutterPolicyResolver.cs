using HomeCompanion.Base.Model;
using System.Globalization;

namespace HomeCompanion.Base.Logics.Shutters;

/// <summary>
/// Stateless policy helper for room objective resolution and UV/manual precedence decisions.
/// </summary>
public static class ShutterPolicyResolver
{
    /// <summary>
    /// Resolves the effective room objective profile.
    /// </summary>
    /// <remarks>
    /// Explicit room objective wins. If room objective is set to inherit, thermal control mapping is used.
    /// If objective-selector inputs are available and input values are provided, the first matching rule wins.
    /// </remarks>
    public static RoomObjectiveProfile ResolveRoomObjective(
        ShadowingSpecial globalShadowing,
        Room room,
        IValueReferenceProvider? valueReferenceProvider = null)
    {
        ArgumentNullException.ThrowIfNull(globalShadowing);
        ArgumentNullException.ThrowIfNull(room);

        var globalConfig = globalShadowing.Configuration;
        var roomConfig = room.Configuration;

        if (roomConfig.ObjectiveProfile != RoomObjectiveProfile.InheritFromThermalControl)
            return roomConfig.ObjectiveProfile;

        if (valueReferenceProvider is not null)
        {
            foreach (var rule in roomConfig.ObjectiveSelectorInputs.Values)
            {
                if (string.IsNullOrWhiteSpace(rule.ValueReference))
                    continue;

                if (!valueReferenceProvider.TryResolve(rule.ValueReference, out var inputValueValue) || inputValueValue is null)
                    continue;

                if (!TryGetNumericValue(inputValueValue, out var inputValue))
                    continue;

                return inputValue >= rule.Threshold
                    ? rule.ProfileAtOrAboveThreshold
                    : rule.ProfileBelowThreshold;
            }
        }

        return ResolveObjectiveFromThermalControl(ResolveThermalControlMode(globalShadowing));
    }

    /// <summary>
    /// Resolves effective thermal-control mode from runtime values, falling back to static config.
    /// </summary>
    public static ThermalControlMode ResolveThermalControlMode(ShadowingSpecial globalShadowing)
    {
        ArgumentNullException.ThrowIfNull(globalShadowing);

        if (globalShadowing.ThermalControlMode is IValue thermalModeValue &&
            TryGetNumericValue(thermalModeValue, out var rawMode))
        {
            var mode = (int)Math.Round(rawMode, MidpointRounding.AwayFromZero);

            // Accept both 0-based and 1-based external numeric encodings.
            if (mode is >= 0 and <= 2 && Enum.IsDefined(typeof(ThermalControlMode), mode))
                return (ThermalControlMode)mode;

            if (mode is >= 1 and <= 3 && Enum.IsDefined(typeof(ThermalControlMode), mode - 1))
                return (ThermalControlMode)(mode - 1);
        }

        return globalShadowing.Configuration.ThermalControl;
    }

    /// <summary>
    /// Returns whether UV protection may apply given current manual override state.
    /// </summary>
    /// <remarks>
    /// Manual operation always has priority over UV-protection.
    /// </remarks>
    public static bool ShouldApplyUvProtection(bool hasManualOverride)
        => !hasManualOverride;

    internal static RoomObjectiveProfile ResolveObjectiveFromThermalControl(ThermalControlMode thermalControl)
        => thermalControl switch
        {
            ThermalControlMode.Disabled => RoomObjectiveProfile.DaylightPriority,
            ThermalControlMode.Balanced => RoomObjectiveProfile.BalancedDefault,
            ThermalControlMode.CoolingPriority => RoomObjectiveProfile.ThermalPriority,
            _ => RoomObjectiveProfile.BalancedDefault,
        };

    private static bool TryGetNumericValue(IValue value, out double numericValue)
    {
        numericValue = 0;

        if (value.OValue is null)
            return false;

        try
        {
            numericValue = Convert.ToDouble(value.OValue, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
