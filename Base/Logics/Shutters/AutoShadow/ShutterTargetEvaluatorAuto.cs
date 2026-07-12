namespace HomeCompanion.Logics.Shutters.AutoShadow;

using HomeCompanion.Base.Model;
using HomeCompanion.Base.Utilities;
using Microsoft.Extensions.Logging;

/// <summary>
/// State-less evaluator for the shutter target position based on environmental measurements and other conditions.
/// Objective is to determine the shutter target position such that constraints are respected, daylight is maximized, but excessive heating is prevented.
/// The thermal scenario incl. potential forecast is obtained from <see cref="ShutterConditionsEvaluationResult.ShadowingPolicy"/>.
/// </summary>
public class ShutterTargetEvaluatorAuto : ShutterTargetEvaluator
{
    private readonly IEnvironmentalsProvider environmentalsProvider;

    public ShutterTargetEvaluatorAuto(
        ShutterConditionsEvaluationResult cond,
        IEnvironmentalsProvider environmentalsProvider,
        TimeProvider timeProvider,
        ILogger<ShutterTargetEvaluatorAuto> logger
    ) : base(cond, timeProvider, logger)
    {
        this.environmentalsProvider = environmentalsProvider;
    }

    /// <summary>
    /// Gets the shutter and sun position specific shadow position, if available, or null if not available.
    /// Max closure and dynamic slat angle are considered, incl. max movements over the day.
    /// The prevent instabilities this should be independent of the current shutter position, and only depend on the sun position and the shutter configuration.
    /// </summary>
    /// <remarks>
    /// Realized in <see cref="ShutterTargetEvaluatorAuto"/> instead of <see cref="ShutterRuntime"/> because it's a deliberate automation choice, not a runtime state and neither a model property.
    /// </remarks>
    protected virtual ShutterPosition? GetShadowPosition()
    {
        var shutterRuntime = cond.RuntimeContext.ShutterRuntime;
        if (shutterRuntime is null)
        {
            logger.LogWarning("Cannot determine present shadow position for shutter {ShutterKey} in room {RoomKey} because the shutter runtime is not available.", cond.RuntimeContext.ShutterKey, cond.RuntimeContext.RoomKey);
            return null;
        }

        var shutterCfg = shutterRuntime.ShutterContext.Shutter.Configuration;

        var defaultShadowSlat = shutterCfg.DefaultShadowSlat;
        var close = shutterCfg.MaxClose;
        var slatAngle = defaultShadowSlat;

        if (new[] { ShadowingPolicy.AvoidShadowing, ShadowingPolicy.CautiousShadowing }.Any(p => p == cond.ShadowingPolicy))
        {
            // soft shadowers get an extra relaxation in terms of more open slat angle
            if ( cond.ShadowingSpecial.SunPosition is not null && cond.ShadowingSpecial.SunPosition.Elevation >= 0.53)
            {
                // if the sun is above the horizon, we can relax the slat angle to allow more light in
                slatAngle = 0.0;
            }
        }

        return new ShutterPosition(close, slatAngle);
    }

    /// <summary>
    /// Can we open the shuttter because there is no sun exposure?
    /// </summary>
    protected virtual ShutterPosition? AssessOpenNoSunExposure()
    {
        bool IsInSunExposureRange = cond.IsInSunExposureRange;
        bool IsAutoReopenEnabled =
                cond.RoomShutterScene == RoomShutterScene.AutoReopen
                && !cond.EffectiveShutterConstraints.HasFlag(ShutterConstraints.LeaveClosed)
                ;

        if (!IsInSunExposureRange)
        {
            if (IsAutoReopenEnabled)
            {
                logger.LogTrace("Shutter {ShutterKey} in room {RoomKey} is opening due to favorable conditions (no sun exposure or brightness below threshold, auto-reopen enabled).", cond.RuntimeContext.ShutterKey, cond.RuntimeContext.RoomKey);
                return ShutterPosition.Open; // fully open, no slat adjustment
            }
            else
            {
                // no-op
                return ShutterPosition.NoOp;
            }
        }

        return null; // no decision made
    }

    /// <summary>
    /// Can we open the shutter because there is low sunlight?
    /// </summary>
    protected virtual ShutterPosition? AssessOpenLowSunlight()
    {
        var shutterConstraints = cond.EffectiveShutterConstraints;
        bool IsBrightnessAboveThreshold = environmentalsProvider.SunIntensityAboveThreshold;
        bool IsAutoReopenEnabled =
                cond.RoomShutterScene == RoomShutterScene.AutoReopen
                && !shutterConstraints.HasFlag(ShutterConstraints.LeaveClosed)
                ;


        // if sun not intensive enough, open?
        if (!IsBrightnessAboveThreshold)
        {
            if (IsAutoReopenEnabled)
            {
                logger.LogTrace("Shutter {ShutterKey} in room {RoomKey} is opening due to favorable conditions (brightness below threshold, auto-reopen enabled).", cond.RuntimeContext.ShutterKey, cond.RuntimeContext.RoomKey);
                return ShutterPosition.Open; // fully open, no slat adjustment
            }
            else
            {
                // no-op
                return ShutterPosition.NoOp; // no-op
            }
        }
        return null; // no decision made
    }

    /// <summary>
    /// Can we open the shutter because the shadowing policy allows sunlight under present conditions despite sun exposure?
    /// </summary>
    protected virtual ShutterPosition? AssessOpenAsPolicyAllowsSunlight()
    {
        bool sceneIsReopen = cond.RoomShutterScene == RoomShutterScene.AutoReopen || cond.RoomShutterScene == RoomShutterScene.AutoMaxLight;
        bool sceneIsMaxLight = cond.RoomShutterScene == RoomShutterScene.AutoMaxLight;

        switch (cond.ShadowingPolicy)
        {
            case ShadowingPolicy.NoShadowing:
                // open if auto-reopen enabled
                if (sceneIsReopen && !cond.EffectiveShutterConstraints.HasFlag(ShutterConstraints.LeaveClosed))
                {
                    logger.LogTrace("Shutter {ShutterKey} in room {RoomKey} is opening due to NoShadowing policy and favorable conditions (auto-reopen enabled).", cond.RuntimeContext.ShutterKey, cond.RuntimeContext.RoomKey);
                    return ShutterPosition.Open; // fully open, no slat adjustment
                }
                break;
            case ShadowingPolicy.AvoidShadowing:
            case ShadowingPolicy.CautiousShadowing:
                // open if auto-reopen enabled and energy balance limit not exceeded
                if (sceneIsReopen && !cond.EffectiveShutterConstraints.HasFlag(ShutterConstraints.LeaveClosed) && !environmentalsProvider.CautiousShadowingEnergyBalanceLimitExceeded)
                {
                    logger.LogTrace("Shutter {ShutterKey} in room {RoomKey} is opening due to AvoidShadowing/CautiousShadowing policy and favorable conditions (auto-reopen enabled).", cond.RuntimeContext.ShutterKey, cond.RuntimeContext.RoomKey);
                    return ShutterPosition.Open; // fully open, no slat adjustment
                }
                break;
            case ShadowingPolicy.PolicyIrrelevant:
            case ShadowingPolicy.AggressiveShadowing:
                // do not open, aggressive shadowing policy requires shutters to be closed
                break;
            default:
                logger.LogWarning("No shadowing policy found for shutter {ShutterKey} in room {RoomKey}. Using default scene.", cond.RuntimeContext.ShutterKey, cond.RuntimeContext.RoomKey);
                break;
        }
        return null; // no decision made
    }

    protected virtual ShutterPosition? AssessRequireUVProtection()
    {
        if ( cond.EffectiveShutterConstraints.HasFlag(ShutterConstraints.UVProtection) && environmentalsProvider.UvIntensityAboveThreshold && cond.IsInSunExposureRange )
        {
            logger.LogTrace("Shutter {ShutterKey} in room {RoomKey} is closing due to UV protection requirement.", cond.RuntimeContext.ShutterKey, cond.RuntimeContext.RoomKey);
            return GetShadowPosition(); // fully closed, default slat position
        }
        return null; // no decision made
    }

    /// <summary>
    /// Performs only closures and no openings, based on the shadowing policy and other conditions.
    /// </summary>
    protected virtual ShutterPosition? AssessShadowingBasedOnPolicy()
    {
        if (!cond.IsInSunExposureRange)
        {
            return null; // no decision made, because we are not in sun exposure range
        }
        
        switch (cond.ShadowingPolicy)
        {
            case ShadowingPolicy.AvoidShadowing:
                // shadow only in case room temperature is above threshold
                if (cond.RuntimeContext.RoomRuntime?.IsRoomTemperatureAboveAutoShadowThreshold ?? false)
                {
                    logger.LogTrace("Shutter {ShutterKey} in room {RoomKey} is closing due to AvoidShadowing policy and room temperature above threshold.", cond.RuntimeContext.ShutterKey, cond.RuntimeContext.RoomKey);
                    return GetShadowPosition(); // fully closed, default slat position
                }
                break;
            case ShadowingPolicy.NoShadowing:
                // no shadowing policy, so we do not close the shutter based on shadowing policy
                break;
            case ShadowingPolicy.CautiousShadowing:
                // close if energy balance limit exceeded or room temperature is above threshold
                if (environmentalsProvider.CautiousShadowingEnergyBalanceLimitExceeded || (cond.RuntimeContext.RoomRuntime?.IsRoomTemperatureAboveAutoShadowThreshold ?? false))
                {
                    logger.LogTrace("Shutter {ShutterKey} in room {RoomKey} is closing due to CautiousShadowing policy and energy balance limit exceeded or room temperature above threshold.", cond.RuntimeContext.ShutterKey, cond.RuntimeContext.RoomKey);
                    return GetShadowPosition(); // fully closed, default slat position
                }
                break;
            case ShadowingPolicy.AggressiveShadowing:
                // aggressive shadowing policy requires shutters to be closed
                logger.LogTrace("Shutter {ShutterKey} in room {RoomKey} is closing due to AggressiveShadowing policy.", cond.RuntimeContext.ShutterKey, cond.RuntimeContext.RoomKey);
                return GetShadowPosition(); // fully closed, default slat position
            case ShadowingPolicy.PolicyIrrelevant:
            default:
                logger.LogWarning("No shadowing policy found for shutter {ShutterKey} in room {RoomKey}. Using default scene.", cond.RuntimeContext.ShutterKey, cond.RuntimeContext.RoomKey);
                break;
        }
        return null; // no decision made
    }

    protected override ShutterPosition? EvaluateShutterTargetInternal()
    {
        Func<ShutterPosition?>[] stepAssessments = new Func<ShutterPosition?>[]
        {
            AssessRequireUVProtection,
            AssessOpenNoSunExposure,
            AssessOpenLowSunlight,
            AssessOpenAsPolicyAllowsSunlight,
            AssessShadowingBasedOnPolicy,
        };

        var stepResult = stepAssessments
            .Select(step => new { Name = step.Method.Name, Result = step() })
            .FirstOrDefault(x => x.Result is not null);
        if (stepResult is not null)
        {
            logger.LogTrace("Shutter {ShutterKey} in room {RoomKey} target position determined by step {StepName}: {TargetPosition}.", cond.RuntimeContext.ShutterKey, cond.RuntimeContext.RoomKey, stepResult.Name, stepResult.Result);
            return stepResult.Result;
        }

        throw new NotImplementedException("Conditional closure triggers to be added here or above");
    }

    protected override ShutterPosition? FilterShutterTargetByConstraints(ShutterPosition? targetPosition)
    {
        var res = targetPosition;
        res = base.FilterShutterTargetByConstraints(targetPosition);

        if (res is null)
            return null;

        if (cond.AutomationMustNotOpenShutter && (cond.RuntimeContext.ShutterRuntime?.IsOpening(res) ?? false))
        {
            logger.LogTrace("Shutter {ShutterKey} is not allowed to open due to automation constraints. Target: {TargetPosition}, Current: {CurrentPosition}. Adjusting target position to no-op.", cond.RuntimeContext.ShutterKey, res, cond.RuntimeContext.ShutterRuntime?.CurrentPosition);
            return ShutterPosition.NoOp;
        }

        if (cond.AutomationMustNotCloseShutter && (cond.RuntimeContext.ShutterRuntime?.IsClosing(res) ?? false))
        {
            logger.LogTrace("Shutter {ShutterKey} is not allowed to close due to automation constraints. Target: {TargetPosition}, Current: {CurrentPosition}. Adjusting target position to no-op.", cond.RuntimeContext.ShutterKey, res, cond.RuntimeContext.ShutterRuntime?.CurrentPosition);
            return ShutterPosition.NoOp;
        }

        return res;
    }
}