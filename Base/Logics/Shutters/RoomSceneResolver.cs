using Microsoft.Extensions.Logging;
using HomeCompanion.Base.Model;

namespace HomeCompanion.Logics.Shutters;

/// <summary>
/// Determines the effective room shutter scene based on the results of <see cref="RoomSceneConditionsAssessor"/>, the current room scene, and potentially arbitrary other factors.
/// Any room scene state machines and related logic is expected to be implemented in this class or its subclasses.
/// </summary>
public class RoomSceneResolver
{
    protected readonly RoomContext roomContext;
    protected readonly ILogger<RoomSceneResolver> logger;

    #region Room shutter scene state machine
    private byte _lastPermittedScene = (byte)RoomShutterScene.Undefined;
    protected byte LastPermittedScene
    {
        get => _lastPermittedScene;
        set
        {
            LastPermittedPreviousScene = _lastPermittedScene;
            _lastPermittedScene = value;
            LastPermittedSceneTimestamp = DateTimeOffset.UtcNow;
        }
    }
    protected DateTimeOffset LastPermittedSceneTimestamp { get; private set; } = DateTimeOffset.MinValue;
    protected byte LastPermittedPreviousScene { get; private set; } = (byte)RoomShutterScene.Undefined;
    #endregion

    public RoomSceneResolver(RoomContext roomContext, ILogger<RoomSceneResolver> logger)
    {
        this.roomContext = roomContext;
        this.logger = logger;
    }

    public virtual byte? ResolveTargetRoomShutterScene(RoomSceneConditionsAssessor conditionsAssessor)
    {
        //=== acquire key information for the room scene resolution ===

        // current requested scene
        RoomShutterScene currentSceneRequest = RoomShutterScene.Undefined;
        if (!roomContext.Room.ShutterScene?.TryGetEnumValue<RoomShutterScene>(out currentSceneRequest) ?? false)
        {
            currentSceneRequest = RoomShutterScene.Undefined;
        }

        // last permitted scene as RoomShutterScene if defined, otherwise Undefined
        RoomShutterScene lastPermittedScene = Enum.IsDefined(typeof(RoomShutterScene), LastPermittedScene) ? (RoomShutterScene)LastPermittedScene : RoomShutterScene.Undefined;

        // Room thermal profile?
        var roomObjectiveProfile = conditionsAssessor.ResolveCurrentRoomObjectiveProfile();

        // Building absence mode active? --> enter automation
        var buildingAbsenceModeActive = roomContext.Building.GetShadowingSpecial().Absence?.Value == true;


        //=== global shutter scene override? ===

        var globalShutterScene = RoomShutterScene.Undefined;
        if (roomContext.Building.GetShadowingSpecial().GlobalShutterScene?.TryGetEnumValue<RoomShutterScene>(out var scene) ?? false)
        {
            globalShutterScene = scene;
        }

        // building wide force deactivation of room shutter scene control?
        if (globalShutterScene.IsDeactivated())
        {
            return (byte)globalShutterScene;
        }

        // force override rooms with global scene?
        if (globalShutterScene.IsCleaningScene() || globalShutterScene.IsHardScene() || globalShutterScene.IsRequestScene())
        {
            // global shutter scene override is a special scene, which is always to be accepted and overrides any other scene.
            return (byte)globalShutterScene;
        }

        if (globalShutterScene.IsDoNotInterfere())
        {
            // global shutter scene override is a do-not-interfere scene, which means that we shall not mess with the last permitted scene.
            return LastPermittedScene;
        }

        if (globalShutterScene.IsAutomationScene())
        {
            // global mandates automation. Ensure we're transferring to an automation scene. If the current scene request is a manual scene, we need to override it with an automation scene.
            // which automation scene to choose depends on the room objective profile and other room configurations.
            return (byte)conditionsAssessor.ResolvePreferredAutomationSceneForRoomObjectiveProfile(roomObjectiveProfile, presence: buildingAbsenceModeActive);
        }

        // Do not interfere may only exit via manual requested scenes
        if ( ((RoomShutterScene)LastPermittedScene).IsDoNotInterfere() )
        {
            if (currentSceneRequest.IsAutomationScene())
            {
                // last requested scene was a do-not-interfere scene, but the current scene is an automation scene. This means that the last requested scene was not accepted and we are now in an automation scene. In this case, we should not interfere and exit via the last requested scene.
                return LastPermittedScene;
            }
            // current request is manual, proceed
        }

        // Cleaning modes must exit via Hard* or Disabled or global automation override. Never via Automation or Request* scenes. If the last permitted scene was a cleaning scene, we must stick to it until a hard scene or disabled scene is requested.
        if (lastPermittedScene.IsCleaningScene() && (currentSceneRequest.IsRequestScene() || currentSceneRequest.IsAutomationScene()))
        {
            // stick to it
            return LastPermittedScene;
        }

        // manual scenes except Request* are always to be accepted
        if (!currentSceneRequest.IsAutomationScene() && !currentSceneRequest.IsRequestScene())
        {
            return (byte)currentSceneRequest;
        }

        //=== can we follow the requested scene Open? ===
        if ( roomObjectiveProfile == RoomObjectiveProfile.ThermalPriority && currentSceneRequest.HasFlag(RoomShutterScene.RequestOpen) )
        {
            //TODO: check sun situation and temperature thresholds to determine whether we can allow the requested scene or not. If not, we need to block it and exit via the last permitted scene.

            //TODO: if house in sleeping mode, put to shadow instead of opening

            // thermal priority profile blocks manual scenes
            return LastPermittedScene;
        }

        return (byte)currentSceneRequest;
     }
}