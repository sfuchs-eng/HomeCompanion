using HomeCompanion.Base.Model;
using HomeCompanion.Logics.Shutters;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters.AutoShadow;

/// <summary>
/// Represents the result of evaluating the conditions for a shutter.
/// Contains also access simplifiers to configuration, runtimes, context, trigger, and other relevant information for the shutter automation logic.
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
}

public class ShutterConditionsEvaluator(TimeProvider timeProvider, ILogger<ShutterConditionsEvaluator> logger)
{
    private readonly TimeProvider timeProvider = timeProvider;
    private readonly ILogger<ShutterConditionsEvaluator> logger = logger;

    public ShutterConditionsEvaluationResult EvaluateConditions(ShutterRuntimeContext runtimeContext, ShutterAutomationComputationTriggerContext triggerContext)
    {
        var shutter = runtimeContext.Shutter;
        if (shutter is null )
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

        return new ShutterConditionsEvaluationResult
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
    }

    protected virtual ShadowingPolicy DetermineShadowingPolicy(ShutterRuntimeContext runtimeContext)
    {
        RoomShutterScene roomScene = ResolveRoomShutterSceneForShutter(runtimeContext).GetRoomShutterScene() ?? RoomShutterScene.Undefined;

        //==== building level policy
        var thermalControlMode = runtimeContext.Building?.GetShadowingSpecial()?.ResolvedThermalControlMode() ?? ThermalControlMode.Passive;
        ShadowingPolicy buildingPolicy = thermalControlMode switch
        {
            ThermalControlMode.Passive => ShadowingPolicy.NoShadowing,
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
            _ => buildingPolicy // fallback to building policy if room scene is undefined or not recognized
        };
        // room thermal mandating another policy?
        var roomTemperature = runtimeContext.Room?.Temperature?.Value;
        if (roomTemperature.HasValue)
        {
            if (roomTemperature.Value < 18.0)
            {
                roomPolicy = ShadowingPolicy.AvoidShadowing; // too cold, avoid shadowing
            }
            else if (roomTemperature.Value > (runtimeContext.Room?.Configuration.AutoShadowTemperatureThreshold ?? 25.0))
            {
                roomPolicy = ShadowingPolicy.AggressiveShadowing; // too hot, prefer shadow
            }
            else if (roomTemperature.Value >= (runtimeContext.Room?.Configuration.AutoShadowTemperatureThreshold ?? 25.0) - 4.0)
            {
                roomPolicy = ShadowingPolicy.CautiousShadowing; // moderate temperature, cautious shadowing
            }
        }

        var effectiveConstraints = runtimeContext.Shutter?.ResolveEffectiveConstraints(runtimeContext.Building, runtimeContext.Room) ?? ShutterConstraints.None;

        // does the shutter have the aggressive constraint set? If yes, it overrides the room and building policy to be aggressive shadowing.
        if ( effectiveConstraints.HasFlag(ShutterConstraints.AggressiveSunProtection) )
        {
            logger.LogTrace("Shutter {ShutterKey} has AggressiveSunProtection constraint. Overriding shadowing policy to AggressiveShadowing.", runtimeContext.ShutterKey);
            return ShadowingPolicy.AggressiveShadowing;
        }

        // If CautiousSunProtection is set, the lower policy of building and room wins. Irrelevant is sorted out.
        var policyOrderOfPriority = new List<ShadowingPolicy> { ShadowingPolicy.AggressiveShadowing, ShadowingPolicy.CautiousShadowing, ShadowingPolicy.AvoidShadowing, ShadowingPolicy.NoShadowing };
        if ( effectiveConstraints.HasFlag(ShutterConstraints.CautiousSunProtection) )
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