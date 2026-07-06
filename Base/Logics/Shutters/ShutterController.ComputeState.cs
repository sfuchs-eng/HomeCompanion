using System.Threading.Channels;
using HomeCompanion.Base.Model;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters;

public partial class ShutterController
{
    private async Task ComputeShutterTargetStateAsync(ShutterRuntimeContext runtimeContext, ShutterAutomationComputationTriggerContext triggerContext, CancellationToken token)
    {
        #region Preps
        // this is a single-shutter consideration and there must be exactly 1 valid shutter
        if (runtimeContext.ShutterRuntime is null)
        {
            logger.LogWarning("No runtime found for shutter {ShutterKey}. Skipping target state computation.", runtimeContext.ShutterKey);
            return;
        }
        var shutterRuntime = runtimeContext.ShutterRuntime;

        // 1. compute individual criteria results for the affected shutter(s) based on the trigger context, e.g. evaluate time-based criteria, weather-based criteria, user preference criteria, etc., and determine which criteria are currently met for each affected shutter
        // 2. prioritize the criteria results based on their implied priority and derive the desired target state for the affected shutter based on the prioritized criteria results, e.g. if a time-based criterion is met that indicates that the shutter should be closed, but at the same time a user preference criterion is met that indicates that the shutter should be open, then we need to determine which criterion takes precedence based on their implied priority and set the target state accordingly, e.g. if we decide that user preferences take precedence over time-based criteria, then we would set the target state to open in this case
        // 3. File the shutter target state update into the shutter target processing loop by enqueuing a new ShutterTargetStateUpdateContext containing the affected shutter(s) and their new target state, which will then be processed by the shutter target processing loop to actually update the target state of the affected shutter(s) in the system, e.g. by writing to their position or open/close inputs, and also to update any relevant internal state in the runtimes, e.g. to track whether a shutter is currently overridden or not, etc.

        var roomScene = ResolveRoomShutterSceneForShutter(runtimeContext);
        var shadowingSpecial = runtimeContext.Building?.GetShadowingSpecial();
        if (shadowingSpecial is null)
        {
            logger.LogWarning("No shadowing special found for building {BuildingKey}. Skipping target state computation for shutter {ShutterKey}.", runtimeContext.BuildingKey, runtimeContext.ShutterKey);
            return;
        }
        #endregion

        #region Preconditions
        // Is it a "do not touch" room scene we're not owning? If yes, return without action.
        if (roomScene.GetRoomShutterScene()?.IsDoNotInterfere() ?? false)
        {
            return;
        }

        // Is it in manual override state that has not been reset yet? If yes, return without action.
        if (shutterRuntime.IsExternalOverrideActive)
        {
            logger.LogTrace("Shutter {ShutterKey} is in external override state. Skipping target state computation.", runtimeContext.ShutterKey);
            return;
        }

        #endregion

        #region Auto-exit
        if (roomScene.GetRoomShutterScene()?.IsAutomationScene() ?? false)
        {
            // It's an automation scene. Compute the target state based on the current environmental conditions, user preferences, and any other relevant criteria, and update the shutter target state accordingly.
            await ComputeAutomatedShutterTargetStateAsync(runtimeContext, triggerContext, token);
            return;
        }
        #endregion

        #region Other scenes

        ShutterTarget shutterTarget = new(runtimeContext, new ShutterPosition(-1, -1));
        async Task CommandShutterAsync(ShutterTarget target) => await shutterTargetProcessingLoop.EnqueueAsync(target, CancellationToken.None);

        var shutterCfg = runtimeContext.Shutter?.Configuration ?? throw new InvalidOperationException($"No shutter configuration found for shutter {runtimeContext.ShutterKey.Key} in room {runtimeContext.RoomKey?.Key}. Cannot compute target state.");
        var specialCfg = shadowingSpecial.Configuration;

        // Is it a hard controlled room scene that we own? Determine target state based on the scene configuration and update the shutter target state accordingly. Then return.
        switch (roomScene.GetRoomShutterScene())
        {
            case RoomShutterScene.CleanShutter:
                shutterTarget.Set(1.0, 0.0); // fully closed, slat horizontal/open, no conditions
                break;
            case RoomShutterScene.CleanWindow:
                shutterTarget.Set(0.0, 0.0); // fully open, slat horizontal/open, no conditions
                break;
            case RoomShutterScene.AwakeWaitingForNightClosureRelease:
                // wait until the room scene transitions.
                return;
            case RoomShutterScene.Deactivated:
                // room shutter control is deactivated.
                return;
            case RoomShutterScene.DryShutter:
                shutterTarget.Set(1.0, shutterCfg.DefaultShadowSlat); // fully closed, slat horizontal/open; no conditions.
                break;
            case RoomShutterScene.HardClosed:
                if (!specialCfg.ExecuteHardScenes)
                {
                    logger.LogTrace("Hard scenes execution is disabled. Skipping hard close for shutter {ShutterKey}.", runtimeContext.ShutterKey);
                    return;
                }
                if (!(runtimeContext.Shutter.ReleasedForClosureValue?.Value ?? true))
                {
                    logger.LogInformation("Shutter {ShutterKey} is not released for closure. Opening it.", runtimeContext.ShutterKey);
                    shutterTarget.Set(0.0, 0.0); // fully open, slat horizontal/open
                    break;
                }
                shutterTarget.Set(shutterCfg.MaxClose, 1.0); // fully closed, slat vertical/closed
                break;
            case RoomShutterScene.HardOpen:
                if (!specialCfg.ExecuteHardScenes)
                {
                    logger.LogTrace("Hard scenes execution is disabled. Skipping hard open for shutter {ShutterKey}.", runtimeContext.ShutterKey);
                    return;
                }
                shutterTarget.Set(0.0, 0.0); // fully open, slat horizontal/open
                break;
            case RoomShutterScene.HardShadow:
                if (!specialCfg.ExecuteHardScenes)
                {
                    logger.LogTrace("Hard scenes execution is disabled. Skipping hard shadow for shutter {ShutterKey}.", runtimeContext.ShutterKey);
                    return;
                }
                shutterTarget.Set(shutterCfg.MaxClose, shutterCfg.DefaultShadowSlat);
                break;
            case RoomShutterScene.RequestClosed:
                // closure allowed? Only reasons I know is the closure lock
                if (!(runtimeContext.Shutter.ReleasedForClosureValue?.Value ?? true))
                {
                    logger.LogInformation("Shutter {ShutterKey} is not released for closure. Opening it.", runtimeContext.ShutterKey);
                    shutterTarget.Set(0.0, 0.0); // fully open, slat horizontal/open
                    break;
                }
                shutterTarget.Set(shutterCfg.MaxClose, shutterCfg.DefaultShadowSlat);
                break;
            case RoomShutterScene.RequestOpen:
                if (IsNoiseMinimizationRequired(runtimeContext))
                {
                    logger.LogInformation("Noise minimization is required. Opening shutter {ShutterKey} only partially.", runtimeContext.ShutterKey);
                    shutterTarget.Set(-1, 0.0); // , slat horizontal/open
                    break;
                }
                shutterTarget.Set(0.0, 0.0); // fully open, slat horizontal/open
                break;
            case RoomShutterScene.RequestShadow:
                if (IsNoiseMinimizationRequired(runtimeContext))
                {
                    logger.LogInformation("Noise minimization is required. Shadowing shutter {ShutterKey} only partially.", runtimeContext.ShutterKey);
                    shutterTarget.Set(-1, shutterCfg.DefaultShadowSlat); // prevent position move, slat horizontal/open
                    break;
                }
                shutterTarget.Set(shutterCfg.MaxClose, shutterCfg.DefaultShadowSlat);
                break;
            case RoomShutterScene.RequestNightClosure:
            case RoomShutterScene.Sleeping:
                // must close despite night closure unless it's not released for closure
                if (!(runtimeContext.Shutter.ReleasedForClosureValue?.Value ?? true))
                {
                    logger.LogInformation("Shutter {ShutterKey} is not released for closure. Opening it.", runtimeContext.ShutterKey);
                    shutterTarget.Set(0.0, 0.0); // fully open, slat horizontal/open
                    break;
                }
                shutterTarget.Set(shutterCfg.MaxClose, 1.0);
                break;
            case RoomShutterScene.Undefined:
                // it's actually Undefined and not just an undefined scene. Scene numbers not reflected by enum members result in the null match.
                return;
            case null:
            default:
                // can we resolve the shutter target from a configured scene preset? If yes, update the shutter target state accordingly and return.
                if (runtimeContext.Room?.Configuration.SceneShutterPresets.TryGetValue(roomScene, out var scenePresetRoom) ?? false)
                {
                    shutterTarget.Set(scenePresetRoom.Position, scenePresetRoom.Slat);
                    break;
                }
                if (shadowingSpecial?.Configuration.SceneShutterPresets.TryGetValue(roomScene, out var scenePresetGlobal) ?? false)
                {
                    shutterTarget.Set(scenePresetGlobal.Position, scenePresetGlobal.Slat);
                    break;
                }
                break;
        }

        // gating of desired target state
        // ??
        if (shutterTarget.IsNoOp)
        {
            logger.LogTrace("Shutter {ShutterKey} target state at room scene {roomScene} is a no-op. Skipping command.", runtimeContext.ShutterKey, roomScene);
            return;
        }

        await CommandShutterAsync(shutterTarget);
        #endregion
    }

    #region Auto-scenes
    private async Task ComputeAutomatedShutterTargetStateAsync(ShutterRuntimeContext runtimeContext, ShutterAutomationComputationTriggerContext triggerContext, CancellationToken token)
    {
        bool shutterIsClosed = runtimeContext.Shutter?.IsClosed ?? false;
        bool shutterIsShadowing = runtimeContext.Shutter?.IsShadowing ?? false;
        throw new NotImplementedException();
    }
    #endregion

    /// <summary>
    /// Noise minimization is required if
    /// <list type="bullet">
    /// <item>Night mode is configured and active and absence is false</item>
    /// <item>Any rooms are still in night closure scene or waiting for night closure release scene, and absence is false</item>
    /// </list>
    /// </summary>
    /// <param name="runtimeContext"></param>
    private bool IsNoiseMinimizationRequired(ShutterRuntimeContext runtimeContext)
    {
        var buildingRuntime = runtimeContext.Building;
        if ( buildingRuntime is null)
        {
            logger.LogWarning("No runtime found for building {BuildingKey}. Cannot determine night mode state for shutter {ShutterKey}.", runtimeContext.BuildingKey, runtimeContext.ShutterKey);
            return false;
        }
        var shadowingSpecial = buildingRuntime.GetShadowingSpecial();
        if (shadowingSpecial is null)
        {
            logger.LogWarning("No shadowing special found for building {BuildingKey}. Cannot determine night mode state for shutter {ShutterKey}.", runtimeContext.BuildingKey, runtimeContext.ShutterKey);
            return false;
        }

        var absenceActive = shadowingSpecial.Absence?.Value ?? false;
        var nightModeActive = shadowingSpecial.NightMode?.IsActive ?? false;
        var anyRoomInNightClosureScene = buildingRuntime.GetAllRooms()
            .Any(room => room.ShutterScene?.TryGetValue(out byte scene) ?? false && (scene.GetRoomShutterScene() == RoomShutterScene.RequestNightClosure || scene.GetRoomShutterScene() == RoomShutterScene.AwakeWaitingForNightClosureRelease));

        return (nightModeActive && !absenceActive) || (anyRoomInNightClosureScene && !absenceActive);
    }

    private byte ResolveRoomShutterSceneForShutter(ShutterRuntimeContext runtimeContext)
    {
        // Room level
        if (runtimeContext.Room?.ShutterScene?.TryGetValue(out byte scene) ?? false)
        {
            return scene;
        }

        // see whether we can use the building global scene as fallback
        if ((runtimeContext.Building?.TryGetShadowingSpecial(out var shadowingSpecial) ?? false) && (shadowingSpecial.GlobalShutterScene?.TryGetValue(out byte buildingScene) ?? false))
        {
            return buildingScene;
        }

        logger.LogWarning("No shutter scene found for shutter {ShutterKey} in room {RoomKey}. Using default scene.", runtimeContext.ShutterKey, runtimeContext.RoomKey);

        return (byte)RoomShutterScene.AutoNoReopen; // default scene
    }

    /// <summary>
    /// The <b>shutter automation computation loop</b> processes the collected triggers and determines the desired target state for each shutter, e.g. based on time of day, weather conditions, user preferences, etc., and enqueues the resulting shutter targets into the <b>shutter target processing loop</b>.
    /// There's no need for batching/debouncinng. Just process one trigger at a time and update the target state for the affected shutter(s) accordingly, as each trigger is expected to potentially change the target state of one or more shutters, and we want to react to changes as quickly as possible, e.g. when a trigger is indicating that a shutter was manually overridden via a wall switch or remote control, we want to immediately update the target state for that shutter in order to pause automation for it and avoid unwanted automatic movements.
    /// </summary>
    /// <param name="channel"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    private async Task ProcessShutterAutomationComputationAsync(Channel<ShutterAutomationComputationTriggerContext> channel, CancellationToken token)
    {
        while (await channel.Reader.WaitToReadAsync(token))
        {
            while (channel.Reader.TryRead(out var triggerContext))
            {
                try
                {
                    var shutterKeys = DetermineAffectedShutters(triggerContext);
                    logger.LogTrace("Processing shutter automation computation trigger for shutter(s) {ShutterKeys} (batch of {BatchSize})", string.Join(", ", shutterKeys.Select(k => k.Key)), shutterKeys.Count());
                    foreach (var shutterKey in shutterKeys)
                    {
                        try
                        {
                            logger.LogTrace("Processing shutter automation computation trigger for shutter {ShutterKey}", shutterKey.Key);
                            var buildingRuntime = buildingRuntimes.GetValueOrDefault(shutterKey.RoomKey.BuildingKey) ?? throw new InvalidOperationException($"No runtime found for building {shutterKey.RoomKey.BuildingKey.Key} affected by automation computation trigger for shutter {shutterKey.Key}");
                            var roomRuntime = roomRuntimes.GetValueOrDefault(shutterKey.RoomKey) ?? throw new InvalidOperationException($"No runtime found for room {shutterKey.RoomKey.Key} affected by automation computation trigger for shutter {shutterKey.Key}");
                            var shutterRuntime = shutterRuntimes.GetValueOrDefault(shutterKey) ?? throw new InvalidOperationException($"No runtime found for shutter {shutterKey.Key} affected by automation computation trigger");
                            var runtimeContext = new ShutterRuntimeContext(shutterKey, buildingRuntime, roomRuntime, shutterRuntime);
                            await ComputeShutterTargetStateAsync(runtimeContext, triggerContext, token);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Error processing shutter automation computation trigger for shutter {ShutterKey}: {Message}",
                                shutterKey.Key, ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error processing shutter automation computation trigger for shutter {ShutterKey} (batch of {BatchSize}): {Message}",
                        triggerContext.ThingKeys.First().Key, triggerContext.ThingKeys.Count(), ex.Message);
                }
            }
        }
    }

    private IEnumerable<ShutterKey> DetermineAffectedShutters(ShutterAutomationComputationTriggerContext triggerContext)
    {
        // determine which shutter(s) are affected by the given trigger context, e.g. if the trigger is related to a specific shutter input, then the affected shutter is the one associated with that input, but if the trigger is related to a time-based criterion, then the affected shutters might be all shutters that have a time-based criterion depending on the current time, and similarly for weather-based criteria, user preference criteria, etc.
        switch ( triggerContext.Scope )
        {
            case ShutterAutomationComputationScope.ShutterSpecific:
                return triggerContext.ThingKeys.Where(k => k is ShutterKey).Cast<ShutterKey>();
            case ShutterAutomationComputationScope.Global:
            case ShutterAutomationComputationScope.Undefined:
            default:
                return shutterRuntimes.Keys; // if the scope is global or undefined, we conservatively assume that all shutters could potentially be affected and return all shutter keys, which ensures that we don't miss any updates but might result in some unnecessary computations for unaffected shutters, but that's an acceptable trade-off for simplicity and correctness
        }
    }
}
