using HomeCompanion.Base.Model;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace HomeCompanion.Base.Logics.Shutters.AIAttempt;

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
        IValueProvider? valueReferenceProvider = null,
        ILogger? logger = null)
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

                if (!inputValueValue.TryGetValue<double>(out var inputValue, logger))
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
    public static ThermalControlMode ResolveThermalControlMode(ShadowingSpecial globalShadowing, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(globalShadowing);

        if (globalShadowing.ThermalControlMode is IValue thermalModeValue &&
            thermalModeValue.TryGetValue<double>(out var rawMode, logger))
        {
            var mode = (int)Math.Round(rawMode, MidpointRounding.AwayFromZero);

            // Thermal control mode is an external input encoding the same values as <see cref="ThermalControlMode"/>
            if (Enum.IsDefined(typeof(ThermalControlMode), mode))
                return (ThermalControlMode)mode;
            
            // fallback to the closest defined mode if the value is out of range
            var definedModes = Enum.GetValues<ThermalControlMode>();
            var closestMode = definedModes.OrderBy(m => Math.Abs((int)m - mode)).First();
            return closestMode;
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
            ThermalControlMode.Undefined => RoomObjectiveProfile.DaylightPriority,
            ThermalControlMode.Winter => RoomObjectiveProfile.DaylightPriority,
            ThermalControlMode.LightHeating => RoomObjectiveProfile.DaylightPriority,
            ThermalControlMode.Passive => RoomObjectiveProfile.DaylightPriority,
            ThermalControlMode.BalancedCooling => RoomObjectiveProfile.BalancedDefault,
            ThermalControlMode.CoolingPriority => RoomObjectiveProfile.ThermalPriority,
            _ => RoomObjectiveProfile.BalancedDefault,
        };
}
