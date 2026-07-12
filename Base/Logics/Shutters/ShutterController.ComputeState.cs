using System.Threading.Channels;
using HomeCompanion.Base.Model;
using HomeCompanion.Logics.Shutters.AutoShadow;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters;

public partial class ShutterController
{
    #region Shutter Automation Computation Loop
    private async Task ComputeShutterTargetStateAsync(ShutterRuntimeContext runtimeContext, ShutterAutomationComputationTriggerContext triggerContext, CancellationToken token)
    {
        //==== Preps ====
        // this is a single-shutter consideration and there must be exactly 1 valid shutter
        if (runtimeContext.ShutterRuntime is null || runtimeContext.Shutter is null)
        {
            logger.LogWarning("No runtime or model found for shutter {ShutterKey}. Skipping target state computation.", runtimeContext.ShutterKey);
            return;
        }
        var shutterRuntime = runtimeContext.ShutterRuntime;

        // 1. compute individual criteria results for the affected shutter(s) based on the trigger context, e.g. evaluate time-based criteria, weather-based criteria, user preference criteria, etc., and determine which criteria are currently met for each affected shutter
        // 2. prioritize the criteria results based on their implied priority and derive the desired target state for the affected shutter based on the prioritized criteria results, e.g. if a time-based criterion is met that indicates that the shutter should be closed, but at the same time a user preference criterion is met that indicates that the shutter should be open, then we need to determine which criterion takes precedence based on their implied priority and set the target state accordingly, e.g. if we decide that user preferences take precedence over time-based criteria, then we would set the target state to open in this case
        // 3. File the shutter target state update into the shutter target processing loop by enqueuing a new ShutterTargetStateUpdateContext containing the affected shutter(s) and their new target state, which will then be processed by the shutter target processing loop to actually update the target state of the affected shutter(s) in the system, e.g. by writing to their position or open/close inputs, and also to update any relevant internal state in the runtimes, e.g. to track whether a shutter is currently overridden or not, etc.

        // put null checks etc. into the evaluator, so we can just call it here and get a result object with all the relevant info.
        ShutterConditionsEvaluationResult cond = new ShutterConditionsEvaluator(environmentalsProvider, timeProvider, loggerFactory.CreateLogger<ShutterConditionsEvaluator>())
            .EvaluateConditions(runtimeContext, triggerContext);

        //==== Preconditions / early exit checks ====

        // Is it a "do not touch" room scene we're not owning? If yes, return without action.
        if (cond.RoomShutterScene.IsDoNotInterfere())
        {
            logger.LogTrace("Shutter {ShutterKey} is in a 'do not interfere' room scene {roomSceneValue} ({roomScene}). Skipping target state computation.", runtimeContext.ShutterKey, cond.RoomShutterSceneValue, cond.RoomShutterScene);
            return;
        }

        // Is it in manual override state that has not been reset yet? If yes, return without action.
        if (shutterRuntime.IsExternalOverrideActive)
        {
            logger.LogTrace("Shutter {ShutterKey} is in external override state. Skipping target state computation.", runtimeContext.ShutterKey);
            return;
        }

        //==== Compute shutter target state ====
        ShutterTargetEvaluator evaluator = cond.RoomShutterScene switch
        {
            RoomShutterScene.AutoMaxLight => new ShutterTargetEvaluatorAutoMaxLight(cond, environmentalsProvider, timeProvider, loggerFactory.CreateLogger<ShutterTargetEvaluatorAutoMaxLight>()),
            RoomShutterScene.AutoReopen => new ShutterTargetEvaluatorAutoReopen(cond, environmentalsProvider, timeProvider, loggerFactory.CreateLogger<ShutterTargetEvaluatorAutoReopen>()),
            RoomShutterScene.AutoNoReopen => new ShutterTargetEvaluatorAutoNoReopen(cond, environmentalsProvider, timeProvider, loggerFactory.CreateLogger<ShutterTargetEvaluatorAutoNoReopen>()),
            _ => new ShutterTargetEvaluatorSimpleScenes(cond, timeProvider, loggerFactory.CreateLogger<ShutterTargetEvaluatorSimpleScenes>())
        };
        var shutterTargetEvaluationResult = await evaluator.EvaluateShutterTargetAsync();

        //==== Post-evaluation checks ====
        if (shutterTargetEvaluationResult is null)
        {
            logger.LogTrace("Shutter {ShutterKey} target state evaluation returned null. Skipping command.", runtimeContext.ShutterKey);
            return;
        }

        if (shutterTargetEvaluationResult.TargetPosition.IsNoOp)
        {
            logger.LogTrace("Shutter {ShutterKey} target state at room scene {roomSceneValue} ({roomScene}) is a no-op. Skipping command.", runtimeContext.ShutterKey, cond.RoomShutterSceneValue, cond.RoomShutterScene);
            return;
        }

        if ( shutterRuntime.IsMoving(shutterTargetEvaluationResult.TargetPosition))
        {
            logger.LogTrace("Shutter {ShutterKey} is already set to target position {TargetPosition}. Skipping command.", runtimeContext.ShutterKey, shutterTargetEvaluationResult.TargetPosition);
            return;
        }

        //==== enqueue the computed shutter target state into the shutter target processing loop for execution.
        await shutterTargetProcessingLoop.EnqueueAsync(new ShutterTarget(runtimeContext, shutterTargetEvaluationResult.TargetPosition), token);
    }
    #endregion

}
