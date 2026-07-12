using Microsoft.Extensions.Logging;
using HomeCompanion.Logics.ThermalControl;
using HomeCompanion.Base.Model;

namespace HomeCompanion.Logics.Shutters;

/// <summary>
/// <para>Assesses the conditions which may lead to a change in the room shutter scene and or prevent it from being changed.</para>
/// <para>This class serves as inheritable, polymorph container for the calculation of inputs needed by <see cref="RoomSceneResolver"/>.</para>
/// <para>Ensure following this sequence:</para>
/// <list type="number">
/// <item>Determine the room objective profile</item>
/// <item>Determine the preferred automation scene for the room objective profile</item>
/// <item>By <see cref="RoomSceneResolver"/>: Determine the effective room shutter scene based on the preferred automation scene, the current room shutter scene, and other factors.</item>
/// </list>
/// </summary>
public class RoomSceneConditionsAssessor
{
    protected readonly RoomContext roomContext;
    protected readonly ILogger<RoomSceneConditionsAssessor> logger;

    public RoomSceneConditionsAssessor(RoomContext roomContext, ILogger<RoomSceneConditionsAssessor> logger)
    {
        this.roomContext = roomContext;
        this.logger = logger;
    }

    public virtual RoomObjectiveProfile ResolveCurrentRoomObjectiveProfile()
    {
        return roomContext.Room.Configuration.ObjectiveProfile switch
        {
            RoomObjectiveProfile.InheritFromThermalControl => ResolveRoomThermalControlModeObjectiveProfile(),
            _ => roomContext.Room.Configuration.ObjectiveProfile
        };
    }

    protected virtual RoomObjectiveProfile ResolveRoomThermalControlModeObjectiveProfile()
    {
        var buildingThermalControlMode = ThermalControl.ThermalControlMode.Undefined;
        if (roomContext.Building.TryGetShadowingSpecial(out var shadowingSpecial))
        {
            buildingThermalControlMode = shadowingSpecial.ThermalControlMode?.TryGetThermalControlMode() ?? ThermalControl.ThermalControlMode.Undefined;
        }

        if (buildingThermalControlMode == ThermalControl.ThermalControlMode.Undefined)
        {
            // building is not defined, revert back to room-level default.
            return roomContext.Room.Configuration.ObjectiveProfile;
        }

        return buildingThermalControlMode switch
        {
            ThermalControl.ThermalControlMode.Cooling => RoomObjectiveProfile.BalancedDefault,
            ThermalControl.ThermalControlMode.HeatProtect => RoomObjectiveProfile.ThermalPriority,
            ThermalControl.ThermalControlMode.LightHeating => RoomObjectiveProfile.DaylightPriority,
            ThermalControl.ThermalControlMode.Passive => RoomObjectiveProfile.DaylightPriority,
            ThermalControl.ThermalControlMode.Winter => RoomObjectiveProfile.DaylightPriority,
            _ => RoomObjectiveProfile.BalancedDefault
        };
    }

    public virtual RoomShutterScene ResolvePreferredAutomationSceneForRoomObjectiveProfile(RoomObjectiveProfile roomObjectiveProfile, bool presence = false)
    {
        return roomObjectiveProfile switch
        {
            RoomObjectiveProfile.DaylightPriority => presence ? RoomShutterScene.AutoReopen : RoomShutterScene.AutoNoReopen,
            RoomObjectiveProfile.ThermalPriority => RoomShutterScene.AutoNoReopen,
            RoomObjectiveProfile.BalancedDefault => presence ? RoomShutterScene.AutoReopen : RoomShutterScene.AutoNoReopen,
            RoomObjectiveProfile.NightClosure => RoomShutterScene.HardClosed,
            RoomObjectiveProfile.NoisePrevention => RoomShutterScene.Deactivated,
            _ => RoomShutterScene.AutoReopen
        };
    }
}
