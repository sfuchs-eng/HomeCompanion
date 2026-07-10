using HomeCompanion.Base.Model;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters.AutoShadow;

/// <summary>
/// Evaluates the target shutter position for most scene numbers, hard-coded ones and configurable scene presets, but not for the Auto scene (which is handled by <see cref="ShutterTargetEvaluatorAuto"/> and derived classes).
/// </summary>
/// <typeparam name="ShutterTargetEvaluatorSimpleScenes"></typeparam>
public class ShutterTargetEvaluatorSimpleScenes : ShutterTargetEvaluator
{
    public ShutterTargetEvaluatorSimpleScenes(ShutterConditionsEvaluationResult cond, TimeProvider timeProvider, ILogger<ShutterTargetEvaluator> logger)
        : base(cond, timeProvider, logger)
    {
    }

    protected override ShutterPosition? EvaluateShutterTargetInternal()
    {
        var runtimeContext = cond.RuntimeContext;
        Shutter shutter = runtimeContext.Shutter ?? throw new InvalidOperationException($"No shutter found for shutter {runtimeContext.ShutterKey.Key} in room {runtimeContext.RoomKey?.Key}. Cannot compute target state.");

        var shutterCfg = cond.ShutterConfiguration;
        var specialCfg = cond.ShadowingSpecial.Configuration;

        // Is it a hard controlled room scene that we own? Determine target state based on the scene configuration and update the shutter target state accordingly. Then return.
        switch (cond.RoomShutterSceneValue)
        {
            case (byte)RoomShutterScene.CleanShutter:
                return new(1.0, 0.0); // fully closed, slat horizontal/open, no conditions

            case (byte)RoomShutterScene.CleanWindow:
                return new(0.0, 0.0); // fully open, slat horizontal/open, no conditions

            case (byte)RoomShutterScene.AwakeWaitingForNightClosureRelease:
                // wait until the room scene transitions.
                return ShutterPosition.NoOp;

            case (byte)RoomShutterScene.Deactivated:
                // room shutter control is deactivated.
                return ShutterPosition.NoOp;

            case (byte)RoomShutterScene.DryShutter:
                return new(1.0, shutterCfg.DefaultShadowSlat); // fully closed, slat horizontal/open; no conditions.

            case (byte)RoomShutterScene.HardClosed:
                if (!specialCfg.ExecuteHardScenes)
                {
                    logger.LogTrace("Hard scenes execution is disabled. Skipping hard close for shutter {ShutterKey}.", runtimeContext.ShutterKey);
                    return ShutterPosition.NoOp;
                }
                if (!(shutter.ReleasedForClosureValue?.Value ?? true))
                {
                    logger.LogInformation("Shutter {ShutterKey} is not released for closure. Opening it.", runtimeContext.ShutterKey);
                    return new(0.0, 0.0); // fully open, slat horizontal/open
                }
                return new(shutterCfg.MaxClose, 1.0); // fully closed, slat vertical/closed

            case (byte)RoomShutterScene.HardOpen:
                if (!specialCfg.ExecuteHardScenes)
                {
                    logger.LogTrace("Hard scenes execution is disabled. Skipping hard open for shutter {ShutterKey}.", runtimeContext.ShutterKey);
                    return ShutterPosition.NoOp;
                }
                return new(0.0, 0.0); // fully open, slat horizontal/open

            case (byte)RoomShutterScene.HardShadow:
                if (!specialCfg.ExecuteHardScenes)
                {
                    logger.LogTrace("Hard scenes execution is disabled. Skipping hard shadow for shutter {ShutterKey}.", runtimeContext.ShutterKey);
                    return ShutterPosition.NoOp;
                }
                return new(shutterCfg.MaxClose, shutterCfg.DefaultShadowSlat);

            case (byte)RoomShutterScene.RequestClosed:
                // closure allowed? Only reasons I know is the closure lock
                if (!(shutter.ReleasedForClosureValue?.Value ?? true))
                {
                    logger.LogInformation("Shutter {ShutterKey} is not released for closure. Opening it.", runtimeContext.ShutterKey);
                    return new(0.0, 0.0); // fully open, slat horizontal/open
                }
                return new(shutterCfg.MaxClose, shutterCfg.DefaultShadowSlat);

            case (byte)RoomShutterScene.RequestOpen:
                if (cond.NoiseMinimizationRequired)
                {
                    logger.LogInformation("Noise minimization is required. Opening shutter {ShutterKey} only partially.", runtimeContext.ShutterKey);
                    return new(-1, 0.0); // , slat horizontal/open
                }
                return new(0.0, 0.0); // fully open, slat horizontal/open

            case (byte)RoomShutterScene.RequestShadow:
                if (cond.NoiseMinimizationRequired)
                {
                    logger.LogInformation("Noise minimization is required. Shadowing shutter {ShutterKey} only partially.", runtimeContext.ShutterKey);
                    return new(-1, shutterCfg.DefaultShadowSlat); // prevent position move, slat horizontal/open
                }
                return new(shutterCfg.MaxClose, shutterCfg.DefaultShadowSlat);

            case (byte)RoomShutterScene.RequestNightClosure:
            case (byte)RoomShutterScene.Sleeping:
                // must close despite night closure unless it's not released for closure
                if (!(shutter.ReleasedForClosureValue?.Value ?? true))
                {
                    logger.LogInformation("Shutter {ShutterKey} is not released for closure. Opening it.", runtimeContext.ShutterKey);
                    return new(0.0, 0.0); // fully open, slat horizontal/open
                }
                return new(shutterCfg.MaxClose, 1.0);

            case (byte)RoomShutterScene.Undefined:
                // it's actually Undefined and not just an undefined scene.
                return ShutterPosition.NoOp;

            default:
                // can we resolve the shutter target from a configured scene preset? If yes, update the shutter target state accordingly and return.
                if (runtimeContext.Room?.Configuration.SceneShutterPresets.TryGetValue(cond.RoomShutterSceneValue, out var scenePresetRoom) ?? false)
                {
                    return new(scenePresetRoom.Position, scenePresetRoom.Slat);
                }
                if (cond.ShadowingSpecial.Configuration.SceneShutterPresets.TryGetValue(cond.RoomShutterSceneValue, out var scenePresetGlobal))
                {
                    return new(scenePresetGlobal.Position, scenePresetGlobal.Slat);
                }
                break;
        }
        return null; // no specific target determined for this scene, do not prevent further evaluation.
    }
}