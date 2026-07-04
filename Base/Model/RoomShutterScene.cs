namespace HomeCompanion.Base.Model;

/// <summary>
/// The room shutter scene numbers steer how the shutters in a room should react to different triggers and requests, e.g. manual scene recalls, automation triggers or safety conditions.
/// They also coordinate the interaction between manual and automatic control or other systems, e.g. by defining scenes which should not be interfered with by automation or which should not be reset after a certain time.
/// Rooms may use scene numbers beyond the defined ones within the KNX DPT 17.001 or 18.001 range. Such are to be treated as temporary manual overrides subject to configuration settings.
/// </summary>
/// <remarks>
/// - Hard*: refers to scene numbers which would typically be controlled directly by a KNX scene controller. HomeCompanion shall not interfere.
/// - Request*: refers to scene numbers which are intended as requests from the user to HomeCompanion. HomeCompanion should react to these requests by moving the shutters accordingly, but may ignore them if constraints or safety conditions apply.
/// - Reserved*: refers to scene numbers used by another system, e.g. a KNX scene controller, for automation. HomeCompanion must not react at all to these scenes and neither reset them nor interfere with them in any way.
/// - Auto*: refers to scene numbers used by HomeCompanion for automation. HomeCompanion should use these scenes for corresponding automation actions according configuration, constraints and other inputs.
/// - Clean*: refers to scene numbers used by HomeCompanion for cleaning modes. HomeCompanion should use these scenes for corresponding cleaning actions according configuration, constraints and other inputs, and ignore all shadowing triggers and constraints while these scenes are active.
/// - Deactivated: refers to a scene number which shall not cause any shutter command. HomeCompanion should treat this scene like a permanent manual override.
/// </remarks>
public enum RoomShutterScene : byte
{
    /// <summary>
    /// KNX scene 1:
    /// Unused scene number, can be used to reflect undefined / unavailable / unknown last scene recall/store.
    /// </summary>
    Undefined = 0,

    /// <summary>
    /// KNX scene 2:
    /// Fully open position, typically 0% closed.
    /// KNX actuated, HomeCompanion should not interfere with this scene.
    /// </summary>
    HardOpen = 1,

    /// <summary>
    /// KNX scene 3:
    /// Scene number for a predefined shadow position, e.g. 30% closed, all shutters connected to this scene.
    /// KNX actuated, HomeCompanion should not interfere with this scene.
    /// </summary>
    HardShadow = 2,

    /// <summary>
    /// KNX scene 4:
    /// Scene number for fully closed shutters.
    /// KNX actuated, HomeCompanion should not interfere with this scene.
    /// </summary>
    HardClosed = 3,

    /// <summary>
    /// KNX scene 22:
    /// Person asks for open shutters in a room.
    /// HomeCompanion should react to this request by opening the shutters, but may ignore it if constraints or safety conditions apply.
    /// </summary>
    RequestOpen = 21,

    /// <summary>
    /// KNX scene 23:
    /// Person asks for shadow position in a room.
    /// HomeCompanion should react to this request by moving the shutters to the shadow position, but may ignore it if constraints or safety conditions apply.
    /// </summary>
    RequestShadow = 22,

    /// <summary>
    /// KNX scene 24:
    /// Person asks for closed shutters in a room.
    /// HomeCompanion should react to this request by closing the shutters, but may ignore it if constraints or safety conditions apply.
    /// </summary>
    RequestClosed = 23,

    /// <summary>
    /// KNX scene 51:
    /// Automation like <see cref="AutoNoReopen"/> but controlled by another system, e.g. a KNX scene controller, rather than HomeCompanion.
    /// HomeCompanion must not react act all to this scene and neither reset it nor interfere with it in any way.
    /// </summary>
    Reserved50 = 50,

    /// <summary>
    /// KNX scene 53:
    /// Automation like <see cref="AutoReopen"/> but controlled by another system, e.g. a KNX scene controller, rather than HomeCompanion.
    /// HomeCompanion must not react act all to this scene and neither reset it nor interfere with it in any way.
    /// </summary>
    Reserved52 = 52,

    /// <summary>
    /// KNX scene 55:
    /// Scene number for the first automation mode in which shutters may be put to shadow position but aren't opened any more, e.g. 54 for "Auto" in the KNX DPT 17.001.
    /// HomeCompanion should use this scene for corresponding automation actions according configuration, constraints and other inputs.
    /// </summary>
    AutoNoReopen = 54,

    /// <summary>
    /// KNX scene 56:
    /// Scene number for the second automation mode in which shuttters would be reopened as soon as the shadowing trigger condition is over, e.g. 56 for "Auto reopen" in the KNX DPT 17.001.
    /// HomeCompanion should use this scene for corresponding automation actions according configuration, constraints and other inputs.
    /// </summary>
    AutoReopen = 55,

    /// <summary>
    /// KNX scene 57:
    /// Like <see cref="AutoReopen"/> but with maximum light conditions, tolerating higher sun exposure and room temperature prior shadowing.
    /// HomeCompanion should use this scene for corresponding automation actions according configuration, constraints and other inputs.
    /// </summary>
    AutoMaxLight = 56,

    /// <summary>
    /// KNX scene 58:
    /// Scene number for when people in the room are sleeping.
    /// Keep shutters closed to prevent noise and early light, but allow manual override from closed to shadow position.
    /// </summary>
    Sleeping = 57,

    /// <summary>
    /// KNX scene 59:
    /// Scene number for when people in the room are awake but waiting for other rooms to be released from night closure.
    /// If shutters are closed, move them to shadow position, but do not allow opening shutters in this room if other rooms are still in night closure mode.
    /// Once all rooms are released from night closure, transition the room scene automatically to a suitable automatic scene (use the <see cref="RoomObjectiveProfile"/> based auto-scene determination).
    /// </summary>
    AwakeWaitingForNightClosureRelease = 58,

    /// <summary>
    /// KNX scene 61:
    /// Scene number for cleaning mode.
    /// For venetian blinds this would move the slats to a position allowing easy cleaning, e.g. 100% closed with 100% tilt angle reflecting a horizontal position.
    /// Special in this mode:
    /// - shutters actuated only once, afterwards the user may set position and angle directly (e.g. via manual control or direct write to position/angle group addresses) without interference by HomeCompanion
    /// - valid for an entire day, i.e. HomeCompanion should not move the shutters out of this position once set, even if there is strong sun irradiation or high outdoor temperature.
    /// - ignore all shadowing triggers and constraints, i.e. allow the user to clean the shutters even if there is strong sun irradiation or high outdoor temperature.
    /// - position constraints are ignored in this mode, i.e. the shutter may be moved to this position even if it would normally be constrained by position constraints.
    /// </summary>
    CleanShutter = 60,

    /// <summary>
    /// KNX scene 62:
    /// Scene number for drying the shutters after washing them.
    /// Move to fully closed and an almost vertical but not fully closed tilt angle, e.g. 100% closed with 80% tilt angle for venetian blinds to maximize dripping and airflow for drying.
    /// Special in this mode:
    /// - shutters actuated only once, afterwards the user may set position and angle directly (e.g. via manual control or direct write to position/angle group addresses) without interference by HomeCompanion
    /// - valid for an entire day, i.e. HomeCompanion should not move the shutters out of this position once set, even if there is strong sun irradiation or high outdoor temperature.
    /// - ignore all shadowing triggers and constraints, i.e. allow the shutters to dry even if there is strong sun irradiation or high outdoor temperature.
    /// - position constraints are ignored in this mode, i.e. the shutter may be moved to this position even if it would normally be constrained by position constraints.
    /// </summary>
    DryShutter = 61,

    /// <summary>
    /// KNX scene 63:
    /// Scene number for cleaning the window behind the shutter.
    /// Move to fully open position, e.g. 0% closed for roller blinds or 0% closed with 0% tilt angle for venetian blinds to allow easy access to the window for cleaning.
    /// Special in this mode:
    /// - shutters actuated only once, afterwards the user may set position and angle directly (e.g. via manual control or direct write to position/angle group addresses) without interference by HomeCompanion
    /// - valid for an entire day, i.e. HomeCompanion should not move the shutters out of this position once set, even if there is strong sun irradiation or high outdoor temperature.
    /// - ignore all shadowing triggers and constraints, i.e. allow the user to clean the window even if there is strong sun irradiation or high outdoor temperature.
    /// - position constraints are ignored in this mode, i.e. the shutter may be moved to this position even if it would normally be constrained by position constraints.
    /// </summary>
    CleanWindow = 62,

    /// <summary>
    /// KNX scene 64:
    /// Scene number that shall not cause any shutter command. It's a valid scene though.
    /// HomeCompanion should treat this scene like a temporary manual override, resetting it back to automation after a configurable period if such is configured, or ignoring it until the next manual open if not.
    /// </summary>
    Deactivated = 63,
}

public static class RoomShutterSceneExtensions
{
    public static bool TryWrite(this IValue value, RoomShutterScene scene, object? source = null)
    {
        if (value is ValueBase<RoomShutterScene> roomShutterSceneValue)
        {
            roomShutterSceneValue.Write(scene, source);
            return true;
        }
        if (value is ValueBase<byte> byteValue)
        {
            byteValue.Write((byte)scene, source);
            return true;
        }
        if (value?.TryWriteNumeric((byte)scene, source) == true)
        {
            return true;
        }
        return false;
    }

    public static RoomShutterScene? GetRoomShutterScene(this byte value)
    {
        if (Enum.IsDefined(typeof(RoomShutterScene), value))
        {
            return (RoomShutterScene)value;
        }
        return null;
    }

    public static bool IsAutomationScene(this RoomShutterScene scene)
    {
        return scene == RoomShutterScene.AutoNoReopen || scene == RoomShutterScene.AutoReopen || scene == RoomShutterScene.AutoMaxLight;
    }

    public static bool IsRequestScene(this RoomShutterScene scene)
    {
        return scene == RoomShutterScene.RequestOpen || scene == RoomShutterScene.RequestShadow || scene == RoomShutterScene.RequestClosed;
    }

    public static bool IsHardScene(this RoomShutterScene scene)
    {
        return scene == RoomShutterScene.HardOpen || scene == RoomShutterScene.HardShadow || scene == RoomShutterScene.HardClosed;
    }

    public static bool IsCleaningScene(this RoomShutterScene scene)
    {
        return scene == RoomShutterScene.CleanShutter || scene == RoomShutterScene.DryShutter || scene == RoomShutterScene.CleanWindow;
    }

    public static bool IsDeactivated(this RoomShutterScene scene)
    {
        return scene == RoomShutterScene.Deactivated;
    }

    public static bool IsValid(this RoomShutterScene scene)
    {
        return scene >= RoomShutterScene.Undefined;
    }

    public static bool IsDoNotInterfere(this RoomShutterScene scene)
    {
        return scene.IsHardScene() || scene == RoomShutterScene.Reserved50 || scene == RoomShutterScene.Reserved52;
    }

    public static bool IsDoNotReset(this RoomShutterScene scene)
    {
        return scene.IsDoNotInterfere() || scene.IsCleaningScene();
    }
}
