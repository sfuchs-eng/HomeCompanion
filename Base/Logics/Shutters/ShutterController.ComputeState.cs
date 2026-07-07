using System.Threading.Channels;
using HomeCompanion.Base.Model;
using HomeCompanion.Logics.Shutters.AutoShadow;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters;

public partial class ShutterController
{
    private async Task ComputeShutterTargetStateAsync(ShutterRuntimeContext runtimeContext, ShutterAutomationComputationTriggerContext triggerContext, CancellationToken token)
    {
        #region Preps
        // this is a single-shutter consideration and there must be exactly 1 valid shutter
        if (runtimeContext.ShutterRuntime is null || runtimeContext.Shutter is null)
        {
            logger.LogWarning("No runtime or model found for shutter {ShutterKey}. Skipping target state computation.", runtimeContext.ShutterKey);
            return;
        }
        var shutterRuntime = runtimeContext.ShutterRuntime;
        var shutter = runtimeContext.Shutter;

        // 1. compute individual criteria results for the affected shutter(s) based on the trigger context, e.g. evaluate time-based criteria, weather-based criteria, user preference criteria, etc., and determine which criteria are currently met for each affected shutter
        // 2. prioritize the criteria results based on their implied priority and derive the desired target state for the affected shutter based on the prioritized criteria results, e.g. if a time-based criterion is met that indicates that the shutter should be closed, but at the same time a user preference criterion is met that indicates that the shutter should be open, then we need to determine which criterion takes precedence based on their implied priority and set the target state accordingly, e.g. if we decide that user preferences take precedence over time-based criteria, then we would set the target state to open in this case
        // 3. File the shutter target state update into the shutter target processing loop by enqueuing a new ShutterTargetStateUpdateContext containing the affected shutter(s) and their new target state, which will then be processed by the shutter target processing loop to actually update the target state of the affected shutter(s) in the system, e.g. by writing to their position or open/close inputs, and also to update any relevant internal state in the runtimes, e.g. to track whether a shutter is currently overridden or not, etc.

        // put null checks etc. into the evaluator, so we can just call it here and get a result object with all the relevant info.
        ShutterConditionsEvaluationResult cond = new ShutterConditionsEvaluator(timeProvider, loggerFactory.CreateLogger<ShutterConditionsEvaluator>())
            .EvaluateConditions(runtimeContext, triggerContext);
        #endregion

        #region Preconditions
        // Is it a "do not touch" room scene we're not owning? If yes, return without action.
        if (cond.RoomShutterScene.IsDoNotInterfere())
            return;

        // Is it in manual override state that has not been reset yet? If yes, return without action.
        if (shutterRuntime.IsExternalOverrideActive)
        {
            logger.LogTrace("Shutter {ShutterKey} is in external override state. Skipping target state computation.", runtimeContext.ShutterKey);
            return;
        }

        #endregion

        #region Auto-scenes
        if (cond.RoomShutterScene.IsAutomationScene())
        {
            // It's an automation scene. Take the complex path.
            await ComputeAutomatedShutterTargetStateAsync(cond, token);
            return;
        }
        #endregion

        #region Other scenes

        ShutterTarget shutterTarget = new(runtimeContext, new ShutterPosition(-1, -1));
        async Task CommandShutterAsync(ShutterTarget target) => await shutterTargetProcessingLoop.EnqueueAsync(target, CancellationToken.None);

        var shutterCfg = cond.ShutterConfiguration;
        var specialCfg = cond.ShadowingSpecial.Configuration;

        // Is it a hard controlled room scene that we own? Determine target state based on the scene configuration and update the shutter target state accordingly. Then return.
        switch (cond.RoomShutterSceneValue)
        {
            case (byte)RoomShutterScene.CleanShutter:
                shutterTarget.Set(1.0, 0.0); // fully closed, slat horizontal/open, no conditions
                break;
            case (byte)RoomShutterScene.CleanWindow:
                shutterTarget.Set(0.0, 0.0); // fully open, slat horizontal/open, no conditions
                break;
            case (byte)RoomShutterScene.AwakeWaitingForNightClosureRelease:
                // wait until the room scene transitions.
                return;
            case (byte)RoomShutterScene.Deactivated:
                // room shutter control is deactivated.
                return;
            case (byte)RoomShutterScene.DryShutter:
                shutterTarget.Set(1.0, shutterCfg.DefaultShadowSlat); // fully closed, slat horizontal/open; no conditions.
                break;
            case (byte)RoomShutterScene.HardClosed:
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
            case (byte)RoomShutterScene.HardOpen:
                if (!specialCfg.ExecuteHardScenes)
                {
                    logger.LogTrace("Hard scenes execution is disabled. Skipping hard open for shutter {ShutterKey}.", runtimeContext.ShutterKey);
                    return;
                }
                shutterTarget.Set(0.0, 0.0); // fully open, slat horizontal/open
                break;
            case (byte)RoomShutterScene.HardShadow:
                if (!specialCfg.ExecuteHardScenes)
                {
                    logger.LogTrace("Hard scenes execution is disabled. Skipping hard shadow for shutter {ShutterKey}.", runtimeContext.ShutterKey);
                    return;
                }
                shutterTarget.Set(shutterCfg.MaxClose, shutterCfg.DefaultShadowSlat);
                break;
            case (byte)RoomShutterScene.RequestClosed:
                // closure allowed? Only reasons I know is the closure lock
                if (!(runtimeContext.Shutter.ReleasedForClosureValue?.Value ?? true))
                {
                    logger.LogInformation("Shutter {ShutterKey} is not released for closure. Opening it.", runtimeContext.ShutterKey);
                    shutterTarget.Set(0.0, 0.0); // fully open, slat horizontal/open
                    break;
                }
                shutterTarget.Set(shutterCfg.MaxClose, shutterCfg.DefaultShadowSlat);
                break;
            case (byte)RoomShutterScene.RequestOpen:
                if (cond.NoiseMinimizationRequired)
                {
                    logger.LogInformation("Noise minimization is required. Opening shutter {ShutterKey} only partially.", runtimeContext.ShutterKey);
                    shutterTarget.Set(-1, 0.0); // , slat horizontal/open
                    break;
                }
                shutterTarget.Set(0.0, 0.0); // fully open, slat horizontal/open
                break;
            case (byte)RoomShutterScene.RequestShadow:
                if (cond.NoiseMinimizationRequired)
                {
                    logger.LogInformation("Noise minimization is required. Shadowing shutter {ShutterKey} only partially.", runtimeContext.ShutterKey);
                    shutterTarget.Set(-1, shutterCfg.DefaultShadowSlat); // prevent position move, slat horizontal/open
                    break;
                }
                shutterTarget.Set(shutterCfg.MaxClose, shutterCfg.DefaultShadowSlat);
                break;
            case (byte)RoomShutterScene.RequestNightClosure:
            case (byte)RoomShutterScene.Sleeping:
                // must close despite night closure unless it's not released for closure
                if (!(runtimeContext.Shutter.ReleasedForClosureValue?.Value ?? true))
                {
                    logger.LogInformation("Shutter {ShutterKey} is not released for closure. Opening it.", runtimeContext.ShutterKey);
                    shutterTarget.Set(0.0, 0.0); // fully open, slat horizontal/open
                    break;
                }
                shutterTarget.Set(shutterCfg.MaxClose, 1.0);
                break;
            case (byte)RoomShutterScene.Undefined:
                // it's actually Undefined and not just an undefined scene.
                return;
            default:
                // can we resolve the shutter target from a configured scene preset? If yes, update the shutter target state accordingly and return.
                if (runtimeContext.Room?.Configuration.SceneShutterPresets.TryGetValue(cond.RoomShutterSceneValue, out var scenePresetRoom) ?? false)
                {
                    shutterTarget.Set(scenePresetRoom.Position, scenePresetRoom.Slat);
                    break;
                }
                if (cond.ShadowingSpecial.Configuration.SceneShutterPresets.TryGetValue(cond.RoomShutterSceneValue, out var scenePresetGlobal))
                {
                    shutterTarget.Set(scenePresetGlobal.Position, scenePresetGlobal.Slat);
                    break;
                }
                break;
        }

        // gating of desired target state (burst limiting occurs later in the shutter target processing loop, so we don't need to do it here)
        // ??

        // finally, if the computed shutter target state is a no-op (i.e. it doesn't require any action), then we can skip the command and return early.
        if (shutterTarget.IsNoOp)
        {
            logger.LogTrace("Shutter {ShutterKey} target state at room scene {roomSceneValue} ({roomScene}) is a no-op. Skipping command.", runtimeContext.ShutterKey, cond.RoomShutterSceneValue, cond.RoomShutterScene);
            return;
        }

        // enqueue the computed shutter target state into the shutter target processing loop for execution.
        await CommandShutterAsync(shutterTarget);
        #endregion
    }

    #region Auto-scenes

    /// <summary>
    /// Handles automation scenarios spanning across several different room scenes, incl. and beyond shadowing-automation scenes
    /// E.g. anti-burglar, force open, manual override, etc.
    /// </summary>
    /// <param name="runtimeContext"></param>
    /// <param name="triggerContext"></param>
    /// <param name="token"></param>
    /// <returns>true if a soft automation case was handled and no further automated processing should occur, false otherwise.</returns>
    private async Task<bool> HandleSimpleAutomationCasesAsync(ShutterConditionsEvaluationResult cond, CancellationToken token)
    {
        var runtimeContext = cond.RuntimeContext;
        Shutter shutter = runtimeContext.Shutter ?? throw new InvalidOperationException($"No shutter found for shutter {runtimeContext.ShutterKey.Key} in room {runtimeContext.RoomKey?.Key}. Cannot compute target state.");

        async Task CommandShutterAsync(double pos, double slat) => await shutterTargetProcessingLoop.EnqueueAsync(new ShutterTarget(runtimeContext, new ShutterPosition(pos, slat)), CancellationToken.None);

        // manual override has priority over all automation, so if it's active and has priority, we skip any automated target state computation and return early.
        bool manualOverrideActiveAndPriority = (runtimeContext.ShutterRuntime?.IsExternalOverrideActive ?? false) && cond.EffectiveShutterConstraints.HasFlag(ShutterConstraints.ManualOverride);
        if (manualOverrideActiveAndPriority)
        {
            logger.LogTrace("Shutter {ShutterKey} is in manual override state with priority. Skipping automated target state computation.", runtimeContext.ShutterKey);
            return true;
        }

        // Force Open deserves priority, also for cases where anti-burglar is not set.
        if ( cond.ForceOpenForPassingThrough )
        {
            logger.LogTrace("Shutter {ShutterKey} is forced open for passing through. Opening it.", runtimeContext.ShutterKey);
            await CommandShutterAsync(0.0, -1); // fully open
            return true;
        }

        // Anti-burglar prevention deserves priority over noise minimization, but not over force open for passing through.
        if ( cond.AntiBurglarActive )
        {
            logger.LogTrace("Anti-burglar prevention is active for shutter {ShutterKey}. Closing it.", runtimeContext.ShutterKey);
            await CommandShutterAsync(shutter.Configuration.MaxClose, 1.0); // fully closed
            return true;
        }
        else if ( cond.AntiBurglarTransitionIndicatesOpening && !cond.AutomationMustNotOpenShutter)
        {
            logger.LogTrace("Anti-burglar prevention has transitioned to inactive and indicates opening for shutter {ShutterKey}. Opening it.", runtimeContext.ShutterKey);
            // a subsequent closure command in the same processing chain should (normally, if timing isn't odd) override this opening command, so we don't need to check for that here (queue handling ok?).
            await CommandShutterAsync(0.0, -1); // fully open
            return true;
        }
        
        return false;
    }

    private enum AutoEvalShutterTargetState
    {
        Open,
        Shadow,
        Close,
        NoAction
    }

    delegate AutoEvalShutterTargetState AutoShutterTargetStateEvaluator(ShutterConditionsEvaluationResult cond);

    private async Task ComputeAutomatedShutterTargetStateAsync(ShutterConditionsEvaluationResult cond, CancellationToken token)
    {
        if (await HandleSimpleAutomationCasesAsync(cond, token))
        {
            // Soft automation case handled, no further automated processing should occur.
            return;
        }


        Shutter shutter = cond.RuntimeContext.Shutter ?? throw new InvalidOperationException($"No shutter found for shutter {cond.RuntimeContext.ShutterKey.Key} in room {cond.RuntimeContext.RoomKey?.Key}. Cannot compute target state.");
        ShutterRuntime shutterRuntime = cond.RuntimeContext.ShutterRuntime ?? throw new InvalidOperationException($"No runtime found for shutter {cond.RuntimeContext.ShutterKey.Key}. Cannot compute target state.");
        ShutterConstraints shutterConstraints = cond.EffectiveShutterConstraints;

        bool shutterIsClosed = shutter.IsClosed;
        bool shutterIsShadowing = shutter.IsShadowing;
        bool shutterIsOpen = shutter.IsOpen;

        bool noisePreventionActive = cond.NoiseMinimizationRequired;
        bool antiBurglarActive = cond.AntiBurglarActive;
        bool antiBurglarHasTransitionedAndIndicatesOpening = cond.AntiBurglarTransitionIndicatesOpening;
        bool forceOpenForPassingThrough = cond.ForceOpenForPassingThrough;
        bool automationMustNotCloseShutter = cond.AutomationMustNotCloseShutter;
        bool automationMustNotOpenShutter = cond.AutomationMustNotOpenShutter;
        bool manualOverrideActiveAndPriority = cond.ManualOverrideActiveAndPriority;

        if ( manualOverrideActiveAndPriority )
        {
            logger.LogTrace("Shutter {ShutterKey} is in manual override state with priority. Skipping automated target state computation.", cond.RuntimeContext.ShutterKey);
            return;
        }

        ShadowingPolicy shadowingPolicy = cond.ShadowingPolicy;

        // resolve conditions & policy to an actual shutter target state (Open, Shadow, Close)
        AutoShutterTargetStateEvaluator resolver = shadowingPolicy switch
        {
            ShadowingPolicy.NoShadowing => ResolveShutterTargetState_NoShadowing,
            ShadowingPolicy.AvoidShadowing => ResolveShutterTargetState_AvoidShadowing,
            ShadowingPolicy.CautiousShadowing => ResolveShutterTargetState_CautiousShadowing,
            ShadowingPolicy.AggressiveShadowing => ResolveShutterTargetState_AggressiveShadowing,
            ShadowingPolicy.PolicyIrrelevant => ResolveShutterTargetState_PolicyIrrelevant,
            _ => throw new InvalidOperationException($"Unknown shadowing policy {shadowingPolicy} for shutter {cond.RuntimeContext.ShutterKey.Key}. Cannot compute target state.")
        };
        var targetState = resolver(cond);

        // filter target: enforce the general conditions

        if (targetState == AutoEvalShutterTargetState.Open)
        {
            if (automationMustNotOpenShutter)
            {
                logger.LogTrace("Shutter {ShutterKey} is not allowed to open due to automation constraints. Skipping open command.", cond.RuntimeContext.ShutterKey);
                return;
            }
            // open it. Override is handled aboved.
            await CommandShutterAsync(0.0, -1); // fully open, slat irrelevant
            return;
        }

        if (targetState == AutoEvalShutterTargetState.Close)
        {
            if (automationMustNotCloseShutter)
            {
                logger.LogTrace("Shutter {ShutterKey} is not allowed to close due to automation constraints. Skipping close command.", cond.RuntimeContext.ShutterKey);
                return;
            }
            // close it. Override is handled aboved.
            await CommandShutterAsync(shutter.Configuration.MaxClose, 1.0); // fully closed, slat vertical/closed
            return;
        }

        if (targetState == AutoEvalShutterTargetState.NoAction)
        {
            logger.LogTrace("Shutter {ShutterKey} target state is NoAction. Skipping command.", cond.RuntimeContext.ShutterKey);
            return;
        }

        if ( targetState != AutoEvalShutterTargetState.Shadow)
        {
            throw new InvalidOperationException($"Unexpected target state {targetState} for shutter {cond.RuntimeContext.ShutterKey.Key}. Cannot compute target state.");
        }
        // here is targetState == AutoEvalShutterTargetState.Shadow
        // --> sun irradiation? angle of irradiation? cut-over angle? aggressive or not? But it's clearly shadowing if there's irradiation due to policy.


        // filter & gate as required, finally enqueue the shutter target state into the shutter target processing loop for execution.
        async Task CommandShutterAsync(double pos, double slat) => await shutterTargetProcessingLoop.EnqueueAsync(new ShutterTarget(cond.RuntimeContext, new ShutterPosition(pos, slat)), CancellationToken.None);

        throw new NotImplementedException();
    }

    private AutoEvalShutterTargetState ResolveShutterTargetState_PolicyIrrelevant(ShutterConditionsEvaluationResult cond)
    {
        if (cond.ForceOpenForPassingThrough)
        {
            return AutoEvalShutterTargetState.Open;
        }
        if (cond.AntiBurglarActive)
        {
            return AutoEvalShutterTargetState.Close;
        }
        if (cond.AntiBurglarTransitionIndicatesOpening)
        {
            var isOpen = cond.IsShutterOpen;
            if (!cond.NoiseMinimizationRequired || isOpen)
                return AutoEvalShutterTargetState.Open;
            // noise minization required, shutter not open
            return AutoEvalShutterTargetState.NoAction;
        }

        throw new NotImplementedException();
    }

    private AutoEvalShutterTargetState ResolveShutterTargetState_AggressiveShadowing(ShutterConditionsEvaluationResult cond)
    {
        throw new NotImplementedException();
    }

    private AutoEvalShutterTargetState ResolveShutterTargetState_CautiousShadowing(ShutterConditionsEvaluationResult cond)
    {
        throw new NotImplementedException();
    }

    private AutoEvalShutterTargetState ResolveShutterTargetState_AvoidShadowing(ShutterConditionsEvaluationResult cond)
    {
        throw new NotImplementedException();
    }

    private AutoEvalShutterTargetState ResolveShutterTargetState_NoShadowing(ShutterConditionsEvaluationResult cond)
    {
        throw new NotImplementedException();
    }

    #endregion

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
