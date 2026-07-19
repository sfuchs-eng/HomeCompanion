using Microsoft.Extensions.Logging;
using HomeCompanion.Base.Model;
using HomeCompanion.Diagnostics;

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

    protected string _lastDecisionMessage = $"{nameof(ResolveTargetRoomShutterScene)} has not been called yet.";

    public RoomSceneResolver(RoomContext roomContext, ILogger<RoomSceneResolver> logger)
    {
        this.roomContext = roomContext;
        this.logger = logger;
    }

    public virtual byte? ResolveTargetRoomShutterScene(RoomSceneConditionsAssessor conditionsAssessor)
    {
        byte permit(RoomShutterScene scene)
        {
            LastPermittedScene = (byte)scene;
            return (byte)scene;
        }

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
            _lastDecisionMessage = $"Global shutter scene is {globalShutterScene}, which is a deactivation scene.";
            return permit(globalShutterScene);
        }

        // force override rooms with global scene?
        if (globalShutterScene.IsCleaningScene() || globalShutterScene.IsHardScene() || globalShutterScene.IsRequestScene())
        {
            _lastDecisionMessage = $"Global shutter scene is {globalShutterScene}, which is a special override scene (Cleaning, Hard, Request).";
            return permit(globalShutterScene);
        }

        if (globalShutterScene.IsDoNotInterfere())
        {
            _lastDecisionMessage = $"Global shutter scene is {globalShutterScene}, which is a do-not-interfere scene.";
            // global shutter scene override is a do-not-interfere scene, which means that we shall not mess with the last permitted scene.
            return LastPermittedScene;
        }

        if (globalShutterScene.IsAutomationScene())
        {
            _lastDecisionMessage = $"Global shutter scene is {globalShutterScene}, which mandates automation.";
            // global mandates automation. Ensure we're transferring to an automation scene. If the current scene request is a manual scene, we need to override it with an automation scene.
            // which automation scene to choose depends on the room objective profile and other room configurations.
            return permit(conditionsAssessor.ResolvePreferredAutomationSceneForRoomObjectiveProfile(roomObjectiveProfile, presence: buildingAbsenceModeActive));
        }

        // Do not interfere may only exit via manual requested scenes
        if ( ((RoomShutterScene)LastPermittedScene).IsDoNotInterfere() )
        {
            if (currentSceneRequest.IsAutomationScene())
            {
                _lastDecisionMessage = $"Last permitted scene was {LastPermittedScene}, which is a do-not-interfere scene, but the current requested scene is {currentSceneRequest}, which is an automation scene. We will not interfere and exit via the last permitted scene.";
                // last requested scene was a do-not-interfere scene, but the current scene is an automation scene. This means that the last requested scene was not accepted and we are now in an automation scene. In this case, we should not interfere and exit via the last requested scene.
                return LastPermittedScene;
            }
            // current request is manual, proceed
        }

        // Cleaning modes must exit via Hard* or Disabled or global automation override. Never via Automation or Request* scenes. If the last permitted scene was a cleaning scene, we must stick to it until a hard scene or disabled scene is requested.
        if (lastPermittedScene.IsCleaningScene() && (currentSceneRequest.IsRequestScene() || currentSceneRequest.IsAutomationScene()))
        {
            _lastDecisionMessage = $"Last permitted scene was {LastPermittedScene}, which is a cleaning scene, and the current requested scene is {currentSceneRequest}, which is a request or automation scene. We will stick to the last permitted scene.";
            // stick to it
            return LastPermittedScene;
        }

        // manual scenes except Request* are always to be accepted
        if (!currentSceneRequest.IsAutomationScene() && !currentSceneRequest.IsRequestScene())
        {
            _lastDecisionMessage = $"Current requested scene is {currentSceneRequest}, which is neither Automation nor Request. We will accept it.";
            return permit(currentSceneRequest);
        }

        // RequestOpen is not a persistent scene itself. Translate to an automation target based on objective profile.
        if (currentSceneRequest == RoomShutterScene.RequestOpen)
        {
            if (roomObjectiveProfile == RoomObjectiveProfile.ThermalPriority)
            {
                // In thermal-priority mode, opening requests are constrained.
                // Keep current automation mode if already in one, otherwise use the strict automation mode.
                if (lastPermittedScene.IsAutomationScene())
                {
                    _lastDecisionMessage = $"Request is {currentSceneRequest}, room objective is {roomObjectiveProfile}. Last permitted scene is {lastPermittedScene}, which is an automation scene. We will stick to it.";
                    return permit(lastPermittedScene);
                }

                _lastDecisionMessage = $"Request is {currentSceneRequest}, room objective is {roomObjectiveProfile}. We will not allow opening requests in thermal-priority mode and will switch to the strict automation mode.";
                return permit(RoomShutterScene.AutoNoReopen);
            }

            if (roomObjectiveProfile == RoomObjectiveProfile.BalancedDefault)
            {
                _lastDecisionMessage = $"Request is {currentSceneRequest}, room objective is {roomObjectiveProfile}. We will allow opening requests in balanced mode via the brightest automation mode.";
                return permit(RoomShutterScene.AutoMaxLight);
            }

            // Daylight-priority and other profiles can reopen.
            _lastDecisionMessage = $"Request is {currentSceneRequest}, room objective is {roomObjectiveProfile}. We will allow opening requests in daylight-priority and other profiles via the reopen automation mode.";
            return permit(RoomShutterScene.AutoReopen);
        }

        //=== can we follow the requested scene Open? ===
        if ( roomObjectiveProfile == RoomObjectiveProfile.ThermalPriority && currentSceneRequest.HasFlag(RoomShutterScene.RequestOpen) )
        {
            //TODO: check sun situation and temperature thresholds to determine whether we can allow the requested scene or not. If not, we need to block it and exit via the last permitted scene.

            //TODO: if house in sleeping mode, put to shadow instead of opening

            // thermal priority profile blocks manual scenes
            _lastDecisionMessage = $"Current requested scene is {currentSceneRequest}, which is a manual scene, but the room objective profile is {roomObjectiveProfile}, which blocks manual scenes. We will not allow it and exit via the last permitted scene.";
            return LastPermittedScene;
        }

        _lastDecisionMessage = $"Current requested scene is {currentSceneRequest}, which is an automation or request scene. We will accept it.";
        return permit(currentSceneRequest);
     }

    internal async Task<IDiagnosticResultNode> GetDiagnosisAsync(CancellationToken cancellationToken)
    {
        var lps = Enum.IsDefined(typeof(RoomShutterScene), LastPermittedScene) ? (RoomShutterScene)LastPermittedScene : RoomShutterScene.Undefined;
        var lpps = Enum.IsDefined(typeof(RoomShutterScene), LastPermittedPreviousScene) ? (RoomShutterScene)LastPermittedPreviousScene : RoomShutterScene.Undefined;

        var node = DiagnosticResultNode.Create(nameof(RoomSceneResolver));
        node.Records = [
            LastPermittedScene.AsDiagnosticRecord("LastPermittedScene[byte]", "The last permitted room shutter scene, which is the last scene that was accepted by the room scene resolver."),
            lps.AsDiagnosticRecord("LastPermittedScene[enum]", "The last permitted room shutter scene as enum, which is the last scene that was accepted by the room scene resolver."),
            new DiagnosticRecord(nameof(_lastDecisionMessage), _lastDecisionMessage),
            LastPermittedPreviousScene.AsDiagnosticRecord("LastPermittedPreviousScene[byte]", "The previous last permitted room shutter scene, which is the scene that was accepted before the last permitted scene."),
            lpps.AsDiagnosticRecord("LastPermittedPreviousScene[enum]", "The previous last permitted room shutter scene as enum, which is the scene that was accepted before the last permitted scene."),
            LastPermittedSceneTimestamp.AsDiagnosticRecord("LastPermittedSceneTimestamp", "The timestamp of the last permitted room shutter scene, which is the time when the last permitted scene was accepted.")
        ];
        return node;
    }
}