using HomeCompanion.Base.Utilities;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters.AutoShadow;

/// <summary>
/// Represents the result of evaluating the conditions for a shutter.
/// Contains also access simplifiers to configuration, runtimes, context, trigger, and other relevant information for the shutter automation logic.
/// Represents a first step in the automation logic, before the actual target state computation and command generation.
/// <para>Core output: <see cref="ShadowingPolicy"/></para>
/// </summary>
public class ShutterConditionsEvaluationResult
{
    // inputs
    public required ShutterRuntimeContext RuntimeContext { get; init; }
    public required ShadowingSpecial ShadowingSpecial { get; init; }
    public required ShutterAutomationComputationTriggerContext TriggerContext { get; init; }

    public CfgShutter ShutterConfiguration => RuntimeContext.Shutter?.Configuration ?? throw new InvalidOperationException($"No shutter configuration found for shutter {RuntimeContext.ShutterKey.Key}. Cannot evaluate conditions.");

    public required DateTimeOffset EvaluatedTimestamp { get; init; }

    public required bool IsShutterOpen { get; init; }
    public required bool IsShutterClosed { get; init; }
    public required bool IsCastingShadow { get; init; }
    public required byte RoomShutterSceneValue { get; init; }
    public required RoomShutterScene RoomShutterScene { get; init; }
    public required ShutterConstraints EffectiveShutterConstraints { get; init; }

    public required bool NoiseMinimizationRequired { get; init; }
    public required bool AntiBurglarActive { get; init; }
    public required bool AntiBurglarTransitionIndicatesOpening { get; init; }
    public required bool ForceOpenForPassingThrough { get; init; }
    public required bool AutomationMustNotOpenShutter { get; init; }
    public required bool AutomationMustNotCloseShutter { get; init; }
    public required bool ManualOverrideActiveAndPriority { get; init; }
    public required ShadowingPolicy ShadowingPolicy { get; init; }

    /// <summary>
    /// Indicates whether the shutter is currently in a sun exposure range.
    /// Cut-over angle and zones are considered, and the result is cached for performance reasons.
    /// </summary>
    /// <remarks>
    /// The usage of <see cref="CachedValue{T}"/> allows lazy compute with caching of the result.
    /// </remarks>
    public CachedValue<bool>? IsInSunExposureRangeCache { get; set; }
    public bool IsInSunExposureRange => IsInSunExposureRangeCache?.Value ?? throw new InvalidOperationException($"IsInSunExposureRangeCache is not initialized for shutter {RuntimeContext.ShutterKey.Key}. Cannot evaluate sun exposure range.");
}

/// <summary>
/// Evaluates the conditions / situation for a shutter based on its configuration, runtime state, and the current context.
/// Creates a <see cref="ShutterConditionsEvaluationResult"/> that can be used for further automation logic processing.
/// </summary>
/// <param name="timeProvider">The time provider used for evaluating time-dependent conditions.</param>
/// <param name="logger">The logger used for logging evaluation details.</param>
public class ShutterConditionsEvaluator(IEnvironmentalsProvider environmentalsProvider, TimeProvider timeProvider, ILogger<ShutterConditionsEvaluator> logger)
{
    private readonly IEnvironmentalsProvider environmentalsProvider = environmentalsProvider;
    private readonly TimeProvider timeProvider = timeProvider;
    private readonly ILogger<ShutterConditionsEvaluator> logger = logger;

    public ShutterConditionsEvaluationResult EvaluateConditions(ShutterRuntimeContext runtimeContext, ShutterAutomationComputationTriggerContext triggerContext)
    {
        var shutter = runtimeContext.Shutter;
        if (shutter is null)
        {
            logger.LogWarning("Shutter {ShutterKey} not found in context. Cannot evaluate conditions.", runtimeContext.ShutterKey);
            throw new InvalidOperationException($"Shutter {runtimeContext.ShutterKey} not found in context.");
        }

        var shadowingSpecial = runtimeContext.Building?.GetShadowingSpecial() ?? throw new InvalidOperationException($"No shadowing special found for building {runtimeContext.BuildingKey?.Key}. Cannot evaluate conditions for shutter {runtimeContext.ShutterKey.Key}.");

        ShutterRuntime shutterRuntime = runtimeContext.ShutterRuntime ?? throw new InvalidOperationException($"No runtime found for shutter {runtimeContext.ShutterKey.Key}. Cannot compute target state.");
        ShutterConstraints shutterConstraints = shutter.ResolveEffectiveConstraints(runtimeContext.Building, runtimeContext.Room);

        bool shutterIsClosed = shutter.IsClosed;
        bool shutterIsShadowing = shutter.IsShadowing;
        bool shutterIsOpen = shutter.IsOpen;

        bool noisePreventionActive = IsNoiseMinimizationRequired(runtimeContext);
        bool antiBurglarActive = shutterRuntime.EvaluateAntiBurglarState(runtimeContext, timeProvider.GetUtcNow(), out bool lastAntiBurglarState, out bool antiBurglarIndicatesOpening);
        bool antiBurglarHasTransitionedAndIndicatesOpening = lastAntiBurglarState && !antiBurglarActive && antiBurglarIndicatesOpening;
        bool forceOpenForPassingThrough = IsShutterForcedOpenForPassingThrough(runtimeContext);
        bool automationMustNotCloseShutter = shutterConstraints.HasFlag(ShutterConstraints.KeepOpen);
        bool automationMustNotOpenShutter = shutterConstraints.HasFlag(ShutterConstraints.LeaveClosed);
        bool manualOverrideActiveAndPriority = (runtimeContext.ShutterRuntime?.IsExternalOverrideActive ?? false) && shutterConstraints.HasFlag(ShutterConstraints.ManualOverride);

        byte roomShutterSceneValue = ResolveRoomShutterSceneForShutter(runtimeContext);
        RoomShutterScene roomShutterScene = roomShutterSceneValue.GetRoomShutterScene() ?? RoomShutterScene.Undefined;

        var res = new ShutterConditionsEvaluationResult
        {
            RuntimeContext = runtimeContext,
            TriggerContext = triggerContext,
            ShadowingSpecial = shadowingSpecial,
            EvaluatedTimestamp = timeProvider.GetUtcNow(),

            IsShutterOpen = shutterIsOpen,
            IsShutterClosed = shutterIsClosed,
            IsCastingShadow = shutterIsShadowing,
            RoomShutterSceneValue = roomShutterSceneValue,
            RoomShutterScene = roomShutterScene,
            EffectiveShutterConstraints = shutter.ResolveEffectiveConstraints(runtimeContext.Building, runtimeContext.Room),

            NoiseMinimizationRequired = noisePreventionActive,
            AntiBurglarActive = antiBurglarActive,
            AntiBurglarTransitionIndicatesOpening = antiBurglarHasTransitionedAndIndicatesOpening,
            ForceOpenForPassingThrough = forceOpenForPassingThrough,
            AutomationMustNotOpenShutter = automationMustNotOpenShutter,
            AutomationMustNotCloseShutter = automationMustNotCloseShutter,
            ManualOverrideActiveAndPriority = manualOverrideActiveAndPriority,

            ShadowingPolicy = DetermineShadowingPolicy(runtimeContext)
        };
        // Some properties requiere preliminary results as inputs:
        res.IsInSunExposureRangeCache = new CachedValue<bool>(false, () => CalculateIsInSunExposureRange(res));
        return res;
    }

    /// <summary>
    /// Determines whether there is a Shadowing Zone defined that matches the current sun position and overrides the exposure/shadowing decision.
    /// <para>Called as part of <see cref="IsInSunExposureRange"/>.</para>
    /// </summary>
    /// <returns>True if the zone requires shadowing, false if it requires exposure, null if none of the zones is overriding the shutter level decision.</returns>
    protected virtual bool? CalculateIsZoneLevelExposure(ShutterConditionsEvaluationResult cond)
    {

        var sunPosition = cond.ShadowingSpecial.SunPosition;
        var shutterCfg = cond.RuntimeContext.Shutter?.Configuration ?? throw new InvalidOperationException($"No shutter configuration found for shutter {cond.RuntimeContext.ShutterKey.Key}. Cannot evaluate zone level exposure.");

        if (sunPosition is null || shutterCfg.ShadowingZones is null || shutterCfg.ShadowingZones.Count == 0)
        {
            return null; // no shadowing zones defined, so we cannot determine if any zone requires shadowing
        }

        var matchingZones = shutterCfg.ShadowingZones.Values.Where(zone => zone.IsMatchWithSunPosition(sunPosition)).ToList();

        if (matchingZones.Count == 0)
        {
            return null; // no matching shadowing zones, so we cannot determine if any zone requires shadowing
        }

        // is there a mixed match of inside and outside zones? If so, we have a configuration error and should log a warning
        if (matchingZones.Count > 1 && matchingZones.Any(zone => zone.Mode == ShadowingZoneMode.Inside) && matchingZones.Any(zone => zone.Mode == ShadowingZoneMode.Outside))
        {
            logger.LogWarning("Multiple matching shadowing zones found for shutter {ShutterKey} in room {RoomKey}. Using the first matching zone.", cond.RuntimeContext.ShutterKey, cond.RuntimeContext.RoomKey);
            return matchingZones.First().Mode == ShadowingZoneMode.Inside; // use the first matching zone: match means inside, so return true if it's Mode Inside, false if it's Mode Outside
        }

        // if we have a single matching zone, we can determine the exposure range based on the zone's mode
        var matchingZone = matchingZones.First();
        if (matchingZone.Mode == ShadowingZoneMode.Inside)
        {
            return true; // we are in exposure range if the sun is inside the zone
        }
        if (matchingZone.Mode == ShadowingZoneMode.Outside)
        {
            return false; // we are not in exposure range if the sun is inside the zone
        }

        return null; // no overriding decision, return null to indicate that the shutter level decision must be kept.
    }

    /// <summary>
    /// Whether the sun position, irrespective of irradiation intensity, is in shutter exposure range.
    /// The cut over angle is considered if > 0, narrowing the exposure range below +/-90° to the shutter's normal vector.
    /// </summary>
    /// <remarks>
    /// The Cut-Over Angle is only relevant if > 0 and defines the maximum angle between the shutter's plane (not the normal vector) and the sun's position.
    /// If that the angle between the sun's position and the shutter's normal vector is less than or equal to (90° - cut-over-angle), then the sun is considered to be in exposure range.
    /// </remarks>
    /// <returns></returns>
    protected virtual bool CalculateIsInSunExposureRange(ShutterConditionsEvaluationResult cond)
    {
        bool SetInSunExposureRange(bool value)
        {
            return value;
        }

        if (environmentalsProvider.IsSunAboveHorizon == false)
        {
            return SetInSunExposureRange(false); // sun is below horizon, so we are not in exposure range
        }

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
        var cutOverAngle = cutOverAngleRule?.GetCutoverAngle(room.GetRoomTemperatureOrDefault(), cond.ShadowingPolicy) ?? 0.0; // consider shadowing policy here, not only room temp.

        var isInExposureRange = angleToSun <= (Math.PI / 2.0 - cutOverAngle);

        if (!isInExposureRange)
        {
            return SetInSunExposureRange(false);
        }

        return SetInSunExposureRange(CalculateIsZoneLevelExposure(cond) ?? true); // if no zone is overriding the decision, we are in exposure range
    }

    protected virtual ShadowingPolicy DetermineShadowingPolicy(ShutterRuntimeContext runtimeContext)
    {
        RoomShutterScene roomScene = ResolveRoomShutterSceneForShutter(runtimeContext).GetRoomShutterScene() ?? RoomShutterScene.Undefined;

        //==== building level policy
        var thermalControlMode = runtimeContext.Building?.GetShadowingSpecial()?.ResolvedThermalControlMode() ?? ThermalControlMode.Passive;
        // rather factor temp in later to prevent contradictions and oscillations!
        // --> AvoidShadowing in case cool-down over night or even by ventilation day over is no issue
        // --> opt for CautiousShadowing if room temperature is the dominant factor.
        // --> go for AggressiveShadowing if building temperature / overall heat is the dominant factor.
        ShadowingPolicy buildingPolicy = thermalControlMode switch
        {
            ThermalControlMode.Passive => ShadowingPolicy.AvoidShadowing,
            ThermalControlMode.BalancedCooling => ShadowingPolicy.CautiousShadowing,
            ThermalControlMode.CoolingPriority => ShadowingPolicy.AggressiveShadowing,
            ThermalControlMode.LightHeating => ShadowingPolicy.AvoidShadowing,
            ThermalControlMode.Winter => ShadowingPolicy.AvoidShadowing,
            ThermalControlMode.Undefined => ShadowingPolicy.AvoidShadowing,
            _ => throw new InvalidOperationException($"Unknown thermal control mode {thermalControlMode} for building {runtimeContext.BuildingKey?.Key}. Cannot determine shadowing policy for shutter {runtimeContext.ShutterKey.Key}.")
        };

        //==== room level policy
        ShadowingPolicy roomPolicy = roomScene switch
        {
            RoomShutterScene.RequestShadow => ShadowingPolicy.AggressiveShadowing,
            RoomShutterScene.RequestClosed => ShadowingPolicy.AggressiveShadowing,
            RoomShutterScene.RequestOpen => ShadowingPolicy.NoShadowing,
            RoomShutterScene.AutoMaxLight => ShadowingPolicy.AvoidShadowing,
            _ => buildingPolicy // fallback to building policy if room scene is undefined or not recognized
        };

        // room thermal mandating another policy?
        var roomTemperature = runtimeContext.Room?.Temperature?.Value;
        var filteredRoomTemperature = runtimeContext.RoomRuntime?.FilteredRoomTemperature;
        if (roomTemperature.HasValue && filteredRoomTemperature.HasValue)
        {
            if (filteredRoomTemperature.Value <= (runtimeContext.Room?.Configuration.PolicyAvoidShadowingTemperatureThreshold ?? 21.0)) // 5°C below threshold
            {
                roomPolicy = roomPolicy.LimitToMax(ShadowingPolicy.AvoidShadowing); // too cold, avoid shadowing
            }
            else if (filteredRoomTemperature.Value >= (runtimeContext.Room?.Configuration.PolicyAggressiveShadowingTemperatureThreshold ?? 24.5))
            {
                roomPolicy = roomPolicy.EnsureAtLeast(ShadowingPolicy.AggressiveShadowing); // too hot, enforce shadow
            }
        }

        var effectiveConstraints = runtimeContext.Shutter?.ResolveEffectiveConstraints(runtimeContext.Building, runtimeContext.Room) ?? ShutterConstraints.None;

        // does the shutter have the aggressive constraint set? If yes, it overrides the room and building policy to be aggressive shadowing.
        if (effectiveConstraints.HasFlag(ShutterConstraints.AggressiveSunProtection))
        {
            logger.LogTrace("Shutter {ShutterKey} has AggressiveSunProtection constraint. Overriding shadowing policy to AggressiveShadowing.", runtimeContext.ShutterKey);
            return ShadowingPolicy.AggressiveShadowing;
        }

        // If CautiousSunProtection is set, the lower policy of building and room wins. Irrelevant is sorted out.
        var policyOrderOfPriority = new List<ShadowingPolicy> { ShadowingPolicy.AggressiveShadowing, ShadowingPolicy.CautiousShadowing, ShadowingPolicy.AvoidShadowing, ShadowingPolicy.NoShadowing };
        if (effectiveConstraints.HasFlag(ShutterConstraints.CautiousSunProtection))
        {
            // the lower policy of building, room, and shutter wins. Irrelevant is sorted out.
            policyOrderOfPriority.Reverse();
            int buildingPolicyIndex = policyOrderOfPriority.IndexOf(buildingPolicy);
            int roomPolicyIndex = policyOrderOfPriority.IndexOf(roomPolicy);
            int lowerPolicyIndex = Math.Max(0, Math.Max(buildingPolicyIndex, roomPolicyIndex));
            var resultingPolicy = policyOrderOfPriority[lowerPolicyIndex];
            logger.LogTrace("Shutter {ShutterKey} has CautiousSunProtection constraint. Lowering shadowing policy to {LowerPolicy}.", runtimeContext.ShutterKey, resultingPolicy);
            return resultingPolicy;
        }

        // If no special constraints are set, the higher policy of building and room wins. Irrelevant is sorted out.
        int buildingPolicyIndexNormal = policyOrderOfPriority.IndexOf(buildingPolicy);
        int roomPolicyIndexNormal = policyOrderOfPriority.IndexOf(roomPolicy);
        int higherPolicyIndex = Math.Min(buildingPolicyIndexNormal, roomPolicyIndexNormal);
        var resultingPolicyNormal = policyOrderOfPriority[higherPolicyIndex];
        logger.LogTrace("Shutter {ShutterKey} has no special constraints. Resulting shadowing policy is {ResultingPolicy}.", runtimeContext.ShutterKey, resultingPolicyNormal);

        return resultingPolicyNormal;
    }

    protected virtual bool IsShutterForcedOpenForPassingThrough(ShutterRuntimeContext runtimeContext)
    {
        var reg = !(runtimeContext.Shutter?.ReleasedForClosureValue?.Value ?? false);
        if (runtimeContext.Shutter?.Configuration.InvertReleasedForClosure ?? false)
        {
            reg = !reg;
        }
        return reg;
    }
    /// <summary>
    /// Noise minimization is required if
    /// <list type="bullet">
    /// <item>Night mode is configured and active and absence is false</item>
    /// <item>Any rooms are still in night closure scene or waiting for night closure release scene, and absence is false</item>
    /// </list>
    /// </summary>
    /// <param name="runtimeContext"></param>
    protected virtual bool IsNoiseMinimizationRequired(ShutterRuntimeContext runtimeContext)
    {
        var buildingRuntime = runtimeContext.Building;
        if (buildingRuntime is null)
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
        var nightModeActive = shadowingSpecial.NightMode?.IsValid ?? false;
        var anyRoomInNightClosureScene = buildingRuntime.GetAllRooms()
            .Any(room => room.ShutterScene?.TryGetValue(out byte scene) ?? false && (scene.GetRoomShutterScene() == RoomShutterScene.RequestNightClosure || scene.GetRoomShutterScene() == RoomShutterScene.AwakeWaitingForNightClosureRelease));

        return (nightModeActive && !absenceActive) || (anyRoomInNightClosureScene && !absenceActive);
    }

    protected virtual byte ResolveRoomShutterSceneForShutter(ShutterRuntimeContext runtimeContext)
    {
        return runtimeContext.Shutter?.ResolveRoomShutterScene(runtimeContext) ?? (byte)RoomShutterScene.Undefined;
    }
}