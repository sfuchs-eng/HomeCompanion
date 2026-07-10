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
    /// Whether the sun position, irrespective of irradiation intensity, is in shuttter exposure range.
    /// The cut over angle is considered if > 0, narrowing the exposure range below +/-90° to the shutter's normal vector.
    /// </summary>
    /// <remarks>
    /// The Cut-Over Angle is only relevant if > 0 and defines the maximum angle between the shutter's plane (not the normal vector) and the sun's position.
    /// If that the angle between the sun's position and the shutter's normal vector is less than or equal to (90° - cut-over-angle), then the sun is considered to be in exposure range.
    /// </remarks>
    /// <returns></returns>
    protected virtual bool IsInSunExposureRange()
    {
        var sunPosition = cond.ShadowingSpecial.SunPosition;
        var shutterOrientation = cond.RuntimeContext.Shutter?.GetOrientationRad();

        var angleToSun = shutterOrientation is not null && sunPosition is not null
            ? SphericVector.AngleBetween(shutterOrientation, sunPosition)
            : throw new InvalidOperationException($"Cannot compute angle to sun for shutter {cond.RuntimeContext.ShutterKey} because either the shutter orientation or the sun position is not available.");

        var shutter = cond.RuntimeContext.Shutter ?? throw new InvalidOperationException($"Cannot compute angle to sun for shutter {cond.RuntimeContext.ShutterKey} because the shutter is not available in the runtime context.");
        var building = cond.RuntimeContext.Building ?? throw new InvalidOperationException($"Cannot compute angle to sun for shutter {cond.RuntimeContext.ShutterKey} because the building is not available in the runtime context.");
        var room = cond.RuntimeContext.Room ?? throw new InvalidOperationException($"Cannot compute angle to sun for shutter {cond.RuntimeContext.ShutterKey} because the room is not available in the runtime context.");

        // resolve the applicable cut over angle rule, if any
        var cutOverAngleRule = shutter.ResolveEffectiveCutoverAngleRule(building, room);
        var cutOverAngle = cutOverAngleRule?.GetCutoverAngle(room.GetRoomTemperatureOrDefault()) ?? 0.0;

        var isInExposureRange = angleToSun <= (Math.PI / 2.0 - cutOverAngle);
        return isInExposureRange;
    }

    protected override ShutterPosition? EvaluateShutterTargetInternal()
    {
        bool IsInSunExposureRange = this.IsInSunExposureRange();
        bool IsBrightnessAboveThreshold = environmentalsProvider.SunIntensityAboveThreshold;

        

        throw new NotImplementedException("EvaluateShutterTargetInternal is not implemented yet.");
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