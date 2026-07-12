using HomeCompanion.Base.Model;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters.AutoShadow;

public class ShutterTargetEvaluationResult
{
    public required ShutterConditionsEvaluationResult ConditionsEvaluationResult { get; init; }
    public required ShutterPosition TargetPosition { get; init; }

    public static ShutterTargetEvaluationResult CreateNoOp(ShutterConditionsEvaluationResult cond)
    {
        return new ShutterTargetEvaluationResult
        {
            ConditionsEvaluationResult = cond,
            TargetPosition = new ShutterPosition(-1.0, -1.0) // No-op target position
        };
    }
}

public abstract class ShutterTargetEvaluator(
    ShutterConditionsEvaluationResult cond,
    TimeProvider timeProvider,
    ILogger<ShutterTargetEvaluator> logger
)
{
    protected readonly ShutterConditionsEvaluationResult cond = cond;
    protected readonly TimeProvider timeProvider = timeProvider;
    protected readonly ILogger<ShutterTargetEvaluator> logger = logger;

    protected CfgShutter ShutterCfg => cond.RuntimeContext.Shutter?.Configuration ?? throw new InvalidOperationException($"No shutter configuration found for shutter {cond.RuntimeContext.ShutterKey.Key} in room {cond.RuntimeContext.RoomKey?.Key}. Cannot compute target state.");
    protected Shutter Shutter => cond.RuntimeContext.Shutter ?? throw new InvalidOperationException($"No shutter found for shutter {cond.RuntimeContext.ShutterKey.Key} in room {cond.RuntimeContext.RoomKey?.Key}. Cannot compute target state.");
    protected ShutterRuntime ShutterRuntime => cond.RuntimeContext.ShutterRuntime ?? throw new InvalidOperationException($"No shutter runtime found for shutter {cond.RuntimeContext.ShutterKey.Key} in room {cond.RuntimeContext.RoomKey?.Key}. Cannot compute target state.");

    public virtual async Task<ShutterTargetEvaluationResult?> EvaluateShutterTargetAsync()
    {
        if (await EvaluatePreconditionalCasesAsync() is ShutterPosition preconditionTarget)
        {
            logger.LogTrace("Shutter {ShutterKey} preconditional case matched. Target position: {TargetPosition}.", cond.RuntimeContext.ShutterKey, preconditionTarget);
            return new ShutterTargetEvaluationResult
            {
                ConditionsEvaluationResult = cond,
                TargetPosition = preconditionTarget
            };
        }

        // Evaluate the shutter target based on the conditions and runtime context.
        // This is a placeholder for the actual evaluation logic.
        // The evaluation logic would consider factors such as:
        // - Current room shutter scene
        // - Room temperature
        // - Shadowing policy
        // - User preferences
        // - Time of day
        // - Other relevant conditions

        return new ShutterTargetEvaluationResult
        {
            ConditionsEvaluationResult = cond,
            TargetPosition = FilterShutterTargetByConstraints(EvaluateShutterTargetInternal()) ?? ShutterPosition.NoOp // Default to NoOp if no specific target is determined
        };
    }

    protected abstract ShutterPosition? EvaluateShutterTargetInternal();

    protected virtual ShutterPosition? FilterShutterTargetByConstraints(ShutterPosition? targetPosition)
    {
        if (targetPosition == null || targetPosition == ShutterPosition.NoOp)
        {
            return targetPosition; // No-op or null, no filtering needed
        }

        // filtering?

        return targetPosition; // return the original target position if no filtering was applied
    }

    /// <summary>
    /// Evaluates preconditional cases for the shutter, such as manual override, force open, and anti-burglar prevention.
    /// </summary>
    /// <returns>The target shutter position if a preconditional case applies; otherwise, null.</returns>
    protected virtual async Task<ShutterPosition?> EvaluatePreconditionalCasesAsync()
    {
        var runtimeContext = cond.RuntimeContext;
        Shutter shutter = runtimeContext.Shutter ?? throw new InvalidOperationException($"No shutter found for shutter {runtimeContext.ShutterKey.Key} in room {runtimeContext.RoomKey?.Key}. Cannot compute target state.");

        // manual override has priority over all automation, so if it's active and has priority, we skip any automated target state computation and return early.
        bool manualOverrideActiveAndPriority = (runtimeContext.ShutterRuntime?.IsExternalOverrideActive ?? false) && cond.EffectiveShutterConstraints.HasFlag(ShutterConstraints.ManualOverride);
        if (manualOverrideActiveAndPriority)
        {
            logger.LogTrace("Shutter {ShutterKey} is in manual override state with priority. Skipping room scene or automated target state computation.", runtimeContext.ShutterKey);
            return ShutterPosition.NoOp; // it's no-op to indicate that no move AND no further evaluation should be done, because manual override has priority over automation.
        }

        // Force Open deserves priority, also for cases where anti-burglar is not set.
        if (cond.ForceOpenForPassingThrough)
        {
            logger.LogTrace("Shutter {ShutterKey} is forced open for passing through. Opening it.", runtimeContext.ShutterKey);
            return new ShutterPosition(0.0, -1); // fully open
        }

        // Anti-burglar prevention deserves priority over noise minimization, but not over force open for passing through.
        if (cond.AntiBurglarActive)
        {
            logger.LogTrace("Anti-burglar prevention is active for shutter {ShutterKey}. Closing it.", runtimeContext.ShutterKey);
            return new ShutterPosition(shutter.Configuration.MaxClose, 1.0); // fully closed
        }
        else if (cond.AntiBurglarTransitionIndicatesOpening && !cond.AutomationMustNotOpenShutter)
        {
            logger.LogTrace("Anti-burglar prevention has transitioned to inactive and indicates opening for shutter {ShutterKey}. Opening it.", runtimeContext.ShutterKey);
            // a subsequent closure command in the same processing chain should (normally, if timing isn't odd) override this opening command, so we don't need to check for that here (queue handling ok?).
            return new ShutterPosition(0.0, -1); // fully open
        }

        return null;
    }
}