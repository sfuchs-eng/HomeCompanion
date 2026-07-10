using HomeCompanion.Base.Model;
using HomeCompanion.Base.Quartz;
using HomeCompanion.Base.Utilities;
using Microsoft.Extensions.Logging;
using Quartz;

namespace HomeCompanion.Logics.Shutters;

public class ShutterRuntime(
    ShutterContext shutterContext,
    RuntimeCreationContext<ShutterKey, ShutterRuntime> runtimeCreationContext
) : RuntimeBase(runtimeCreationContext.LoggerFactory.CreateLogger<RuntimeBase>())
{
    public ShutterContext ShutterContext { get; } = shutterContext;
    public ShutterKey ShutterKey => ShutterContext.ShutterKey;
    private readonly ILogger<ShutterRuntime> logger = runtimeCreationContext.LoggerFactory.CreateLogger<ShutterRuntime>();
    private readonly IQueueFeeder<ShutterAutomationComputationTriggerContext> queueFeeder = runtimeCreationContext.ComputationTriggerQueueFeeder;
    private readonly TimeProvider timeProvider = runtimeCreationContext.TimeProvider;

    /// <summary>
    /// The methods <see cref="StartExternalOverrideAsync"/> and <see cref="ResetExternalOverrideAsync"/> manage the external override state for this shutter runtime.
    /// See <see cref="ShutterResetExternalOverrideJob"/> for the entity that resets the external override state after a certain duration.
    /// This property indicates whether an external override is currently active, which can be used by other logics to determine whether to respect the external override or not.
    /// </summary>
    public bool IsExternalOverrideActive { get; private set; } = false;
    public DateTimeOffset? ExternalOverrideStartTime { get; private set; } = null;

    public override Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Register event handlers for shutter level inputs.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var shutter = ShutterContext.Shutter;

        shutter.AngleValue?.Written += HandleShutterCommanded;
        shutter.PositionValue?.Written += HandleShutterCommanded;
        shutter.OpenCloseValue?.Written += HandleShutterCommanded;

        await Task.CompletedTask;
    }

    public event EventHandler<ShutterExternalOverrideEventArgs>? ShutterExternalOverride;

    private void HandleShutterCommanded(object? sender, ValueWrittenEventArgs e)
    {
        if (e.Initiator is ShutterRuntime shutterRuntime && ReferenceEquals(shutterRuntime, this))
        {
            // we're not handling this as it's coming from ourselves.
            return;
        }
        var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
        Task.Run(async () => await StartExternalOverrideAsync(sender, e, cancel), cancel);
    }

    private TimeSpan GetPermittedManualOverrideDuration()
    {
        var shutter = ShutterContext.Shutter;

        // check for shutter-specific override duration
        if (shutter.Configuration.MaxManualOverrideDuration.HasValue)
        {
            return shutter.Configuration.MaxManualOverrideDuration.Value;
        }

        // check for room-specific override duration
        if (ShutterContext.Room.Configuration.ShutterMaxManualOverrideDuration.HasValue)
        {
            return ShutterContext.Room.Configuration.ShutterMaxManualOverrideDuration.Value;
        }

        // fall back to building-specific override duration
        return ShutterContext.Building.GetShadowingSpecial().Configuration.DefaultShutterMaxManualOverrideDuration;
    }

    private async Task StartExternalOverrideAsync(object? sender, ValueWrittenEventArgs e, CancellationToken cancellationToken)
    {
        IsExternalOverrideActive = true;
        ExternalOverrideStartTime = timeProvider.GetLocalNow();

        // schedule a job to reset the external override after a certain duration
        try
        {
            // install a quartz trigger to reset the external override after a certain duration, e.g. 30 minutes, to avoid leaving the shutter in an external override state indefinitely
            var scheduler = await runtimeCreationContext.SchedulerFactory.GetScheduler();
            var jobKey = typeof(ShutterResetExternalOverrideJob).GetJobKeyFromType()
                ?? throw new InvalidOperationException($"Could not get job key for job type {typeof(ShutterResetExternalOverrideJob).FullName}.");

            var triggerKey = ShutterResetExternalOverrideJob.GetTriggerKey(ShutterKey);

            // is there already a trigger? if there is a trigger, cancel the trigger and schedule a new trigger
            if (await scheduler.CheckExists(triggerKey))
            {
                await scheduler.UnscheduleJob(triggerKey);
            }

            var jobDetail = JobBuilder.Create<ShutterResetExternalOverrideJob>()
                .WithIdentity(jobKey)
                .UsingJobData("ShutterKey", ShutterKey.Key)
                .Build();
            var trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .StartAt(DateTimeOffset.UtcNow.Add(GetPermittedManualOverrideDuration())) // reset after the permitted manual override duration
                .Build();
            await scheduler.ScheduleJob(jobDetail, trigger);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error while scheduling reset of external override for shutter {ShutterKey}.", ShutterKey);
        }

        // call event handlers, if any
        try
        {
            ShutterExternalOverride?.Invoke(this, new ShutterExternalOverrideEventArgs(ShutterKey, e));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error while invoking ShutterExternalOverride event for shutter {ShutterKey}.", ShutterKey);
        }
    }

    public async Task ResetExternalOverrideAsync()
    {
        IsExternalOverrideActive = false;
        ExternalOverrideStartTime = null;
        // cancel any remaining trigger for resetting the external override, if any
        try
        {
            var scheduler = await runtimeCreationContext.SchedulerFactory.GetScheduler();
            var triggerKey = ShutterResetExternalOverrideJob.GetTriggerKey(ShutterKey);
            if (await scheduler.CheckExists(triggerKey))
            {
                await scheduler.UnscheduleJob(triggerKey);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error while cancelling reset of external override for shutter {ShutterKey}.", ShutterKey);
        }

        // trigger a re-computation of the shutter target state to resume automation for this shutter, if applicable
        IEnumerable<IThingKey> affectedShutterKeys = [ShutterKey];
        queueFeeder.Enqueue(new ShutterAutomationComputationTriggerContext(
                affectedShutterKeys,
                ShutterAutomationComputationScope.ShutterSpecific,
                null,
                null,
                timeProvider.GetLocalNow(),
                ShutterAutomationComputationTriggerUrgency.Slow
            ));
    }

    /// <summary>
    /// Clears any active external override for all shutters and triggers a recomputation of the shutter automation for all affected shutters.
    /// </summary>
    /// <param name="runtimesProvider"></param>
    /// <returns></returns>
    public static async Task ResetAllExternalOverridesAsync(IRuntimesProvider runtimesProvider)
    {
        var resets = runtimesProvider
            .ShutterRuntimes
            .Where(kvp => kvp.Value.IsExternalOverrideActive)
            .Select(kvp => kvp.Value.ResetExternalOverrideAsync());
        await Task.WhenAll(resets);
    }

    /// <summary>
    /// Stop the shutter runtime and unregister event handlers for all relevant inputs.
    /// </summary>
    public override Task StopAsync(CancellationToken cancellationToken = default)
    {
        var shutter = ShutterContext.Shutter;

        shutter.AngleValue?.Written -= HandleShutterCommanded;
        shutter.PositionValue?.Written -= HandleShutterCommanded;
        shutter.OpenCloseValue?.Written -= HandleShutterCommanded;

        return Task.CompletedTask;
    }

    internal async Task ExecuteShutterTargetAsync(ShutterTarget shutterTarget)
    {
        if (!shutterTarget.ShutterKey.Equals(this.ShutterKey))
            throw new ArgumentException($"The provided shutter target {shutterTarget} does not match the shutter key {this.ShutterKey} of this runtime.", nameof(shutterTarget));
        var shutter = ShutterContext.Shutter;
        // switch:      ShutterType.OpenClose => shutter.OpenCloseValue.TryWriteNumeric(shutterTarget.OpenClose, shutterTarget.Duration),

        bool success = true;
        switch (shutter.Configuration.Type)
        {
            case ShutterType.VenetianBlind:
                if (!shutterTarget.TargetPosition.PreventPositionChange)
                    success = success && (shutter.PositionValue?.TryWriteNumeric(shutterTarget.TargetPosition.LiftPosition, this, logger) ?? false);
                if (!shutterTarget.TargetPosition.PreventTiltChange)
                    success = success && (shutter.AngleValue?.TryWriteNumeric(shutterTarget.TargetPosition.TiltAngle, this, logger) ?? false);
                break;
            case ShutterType.Positional:
                if (!shutterTarget.TargetPosition.PreventPositionChange)
                    success = success && (shutter.PositionValue?.TryWriteNumeric(shutterTarget.TargetPosition.LiftPosition, this, logger) ?? false);
                break;
            case ShutterType.OpenClose:
                if (!shutterTarget.TargetPosition.PreventPositionChange)
                    success = success && (shutter.OpenCloseValue?.TryWriteNumeric(shutterTarget.TargetPosition.LiftPosition, this, logger) ?? false);
                break;
            default:
                logger.LogWarning("Unsupported shutter type {ShutterType} for shutter {ShutterKey}.", shutter.Configuration.Type, ShutterKey);
                break;
        }
        if (!success)
        {
            logger.LogWarning("Failed to write target {ShutterTarget} to shutter {ShutterKey} of type {ShutterType}.", shutterTarget, ShutterKey, shutter.Configuration.Type);
        }
    }

    internal static Dictionary<ShutterKey, ShutterRuntime> Create(RuntimeCreationContext<ShutterKey, ShutterRuntime> runtimeCreationContext)
    {
        var model = runtimeCreationContext.Model;
        var existingRuntimes = runtimeCreationContext.ExistingRuntimes;
        var queueFeeder = runtimeCreationContext.ComputationTriggerQueueFeeder;
        var loggerFactory = runtimeCreationContext.LoggerFactory;

        var newRuntimes = new Dictionary<ShutterKey, ShutterRuntime>();

        foreach (var shutterContext in model.EnumerateShutterContexts())
        {
            var shutterKey = shutterContext.ShutterKey;
            if (existingRuntimes != null && existingRuntimes.ContainsKey(shutterKey))
            {
                continue;
            }

            var runtime = new ShutterRuntime(shutterContext, runtimeCreationContext);
            newRuntimes[shutterKey] = runtime;
        }

        return newRuntimes;
    }

    private DateTimeOffset? lastAntiBurglarClosureTime = null;
    private bool isAntiBurglarClosureActive = false;

    public bool EvaluateAntiBurglarState(ShutterRuntimeContext runtimeContext, DateTimeOffset now, out bool lastAntiBurglarState, out bool indicatesOpening)
    {
        lastAntiBurglarState = isAntiBurglarClosureActive;
        indicatesOpening = false;

        var resolvedShutterConstraints = runtimeContext.Shutter?.ResolveEffectiveConstraints(runtimeContext.Building, runtimeContext.Room) ?? ShutterConstraints.None;
        if (!resolvedShutterConstraints.HasFlag(ShutterConstraints.AntiBurglar))
        {
            return false;
        }

        var nowTime = now.TimeOfDay;
        var shadowingSpecial = runtimeContext.Building?.GetShadowingSpecial();
        CfgShadowingSpecial shadowingConfig = shadowingSpecial?.Configuration ?? new CfgShadowingSpecial();
        bool isAbsenceModeActive = shadowingSpecial?.IsAbsenceModeActive ?? false;

        // Time based open/close shutters
        bool isWithinClosedMaxTimeWindow = nowTime >= shadowingConfig.AntiBurglar.EarliestClosureTime || nowTime <= shadowingConfig.AntiBurglar.LatestOpeningTime;
        bool isWithinClosedMinTimeWindow = nowTime >= shadowingConfig.AntiBurglar.LatestClosureTime && nowTime <= shadowingConfig.AntiBurglar.EarliestOpeningTime;
        bool isWithinOpenMaxTimeWindow = nowTime >= shadowingConfig.AntiBurglar.EarliestOpeningTime && nowTime <= shadowingConfig.AntiBurglar.LatestOpeningTime;
        bool isWithinOpenMinTimeWindow = nowTime >= shadowingConfig.AntiBurglar.LatestOpeningTime || nowTime <= shadowingConfig.AntiBurglar.EarliestOpeningTime;

        bool timeRequiresClosure = isWithinClosedMinTimeWindow || (isAbsenceModeActive && isWithinClosedMaxTimeWindow);
        bool timePermitsOpening = isWithinOpenMinTimeWindow && !timeRequiresClosure;

        // Global illuminance / dusk based open/close shutters
        var duskLowerThreshold = shadowingConfig.AntiBurglar.DuskTriggerLowerThresholdLux;
        var duskUpperThreshold = shadowingConfig.AntiBurglar.DuskTriggerUpperThresholdLux;
        var globalIlluminance = shadowingSpecial?.GlobalIlluminance?.Value ?? double.NaN;

        bool triggerDuskClosure = !double.IsNaN(globalIlluminance) && globalIlluminance < duskLowerThreshold;
        // time windows sind last closure must have passed too
        bool triggerDuskOpening =
                !double.IsNaN(globalIlluminance) && globalIlluminance > duskUpperThreshold
                && (!lastAntiBurglarClosureTime.HasValue || (now - lastAntiBurglarClosureTime.Value) >= shadowingConfig.AntiBurglar.DuskRelaxationDuration);

        if (timeRequiresClosure || triggerDuskClosure)
        {
            isAntiBurglarClosureActive = true;
            lastAntiBurglarClosureTime = now;
        }
        else if (timePermitsOpening || triggerDuskOpening)
        {
            isAntiBurglarClosureActive = false;
            indicatesOpening = shadowingConfig.AntiBurglar.EnableAutomaticReopening;
        }

        return isAntiBurglarClosureActive;
    }

    public ShutterPosition CurrentPosition => new ShutterPosition(
        ShutterContext.Shutter.GetPositionInPUnit(),
        ShutterContext.Shutter.GetAngleInPUnit()
    );

    public bool IsMoving(ShutterPosition targetPosition, double tolerances = 0.01)
    {
        var shutter = ShutterContext.Shutter;
        if (shutter == null)
            return false;

        var curPos = shutter.GetPositionInPUnit();
        var curAngle = shutter.GetAngleInPUnit();

        if (curPos < 0.0 || curAngle < 0.0)
        {
            //logger.LogDebug("Shutter {ShutterKey} has invalid current position or angle: Position={Position}, Angle={Angle}.", ShutterKey, curPos, curAngle);
            // could be that the IValue's aren't initialized yet, so we can't determine if it's moving or not. We'll assume it's moving to be safe.
            return true;
        }

        bool isMoving = false;
        switch (shutter.Configuration.Type)
        {
            case ShutterType.VenetianBlind:
                isMoving |= Math.Abs(curPos - targetPosition.LiftPosition) > tolerances;
                isMoving |= Math.Abs(curAngle - targetPosition.TiltAngle) > tolerances;
                break;
            case ShutterType.Positional:
                isMoving |= Math.Abs(curPos - targetPosition.LiftPosition) > tolerances;
                break;
            case ShutterType.OpenClose:
                if (curPos > 0.5 && targetPosition.LiftPosition <= 0.5)
                {
                    isMoving = true; // opening
                }
                else if (curPos <= 0.5 && targetPosition.LiftPosition > 0.5)
                {
                    isMoving = true; // closing
                }
                break;
            default:
                logger.LogWarning("Unsupported shutter type {ShutterType} for shutter {ShutterKey}.", shutter.Configuration.Type, ShutterKey);
                break;
        }

        return isMoving;
    }

    public bool IsOpening(ShutterPosition targetPosition, double tolerances = 0.01)
    {
        var shutter = ShutterContext.Shutter;
        if (shutter == null)
            return false;

        var curPos = shutter.GetPositionInPUnit();
        var curAngle = shutter.GetAngleInPUnit();

        if (curPos < 0.0 || curAngle < 0.0)
        {
            //logger.LogDebug("Shutter {ShutterKey} has invalid current position or angle: Position={Position}, Angle={Angle}.", ShutterKey, curPos, curAngle);
            // could be that the IValue's aren't initialized yet, so we can't determine if it's moving or not. We'll assume it's moving to be safe.
            return true;
        }

        bool isOpening = false;
        switch (shutter.Configuration.Type)
        {
            case ShutterType.VenetianBlind:
                isOpening |= targetPosition.LiftPosition + tolerances < curPos;
                isOpening |= targetPosition.TiltAngle + tolerances < curAngle;
                break;
            case ShutterType.Positional:
            case ShutterType.OpenClose:
                isOpening |= targetPosition.LiftPosition + tolerances < curPos;
                break;
            default:
                logger.LogWarning("Unsupported shutter type {ShutterType} for shutter {ShutterKey}.", shutter.Configuration.Type, ShutterKey);
                break;
        }

        return isOpening;
    }
    
    public bool IsClosing(ShutterPosition targetPosition, double tolerances = 0.01)
    {
        var shutter = ShutterContext.Shutter;
        if (shutter == null)
            return false;

        var curPos = shutter.GetPositionInPUnit();
        var curAngle = shutter.GetAngleInPUnit();

        if (curPos < 0.0 || curAngle < 0.0)
        {
            //logger.LogDebug("Shutter {ShutterKey} has invalid current position or angle: Position={Position}, Angle={Angle}.", ShutterKey, curPos, curAngle);
            // could be that the IValue's aren't initialized yet, so we can't determine if it's moving or not. We'll assume it's moving to be safe.
            return true;
        }

        bool isClosing = false;
        switch (shutter.Configuration.Type)
        {
            case ShutterType.VenetianBlind:
                isClosing |= targetPosition.LiftPosition - tolerances > curPos;
                isClosing |= targetPosition.TiltAngle - tolerances > curAngle;
                break;
            case ShutterType.Positional:
            case ShutterType.OpenClose:
                isClosing |= targetPosition.LiftPosition - tolerances > curPos;
                break;
            default:
                logger.LogWarning("Unsupported shutter type {ShutterType} for shutter {ShutterKey}.", shutter.Configuration.Type, ShutterKey);
                break;
        }

        return isClosing;
    }
}

public class ShutterExternalOverrideEventArgs(ShutterKey shutterKey, ValueWrittenEventArgs valueWrittenEventArgs) : EventArgs
{
    public ShutterKey ShutterKey { get; } = shutterKey;
    public ValueWrittenEventArgs ValueWrittenEventArgs { get; } = valueWrittenEventArgs;
}