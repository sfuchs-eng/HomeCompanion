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
        StartExternalOverride(sender, e);
    }

    private void StartExternalOverride(object? sender, ValueWrittenEventArgs e)
    {
        IsExternalOverrideActive = true;
        ExternalOverrideStartTime = timeProvider.GetLocalNow();

        // schedule a job to reset the external override after a certain duration
        try
        {
            // install a quartz trigger to reset the external override after a certain duration, e.g. 30 minutes, to avoid leaving the shutter in an external override state indefinitely
            var scheduler = runtimeCreationContext.SchedulerFactory.GetScheduler().GetAwaiter().GetResult();
            var jobKey = typeof(ShutterResetExternalOverrideJob).GetJobKeyFromType()
                ?? throw new InvalidOperationException($"Could not get job key for job type {typeof(ShutterResetExternalOverrideJob).FullName}.");

            var triggerKey = new TriggerKey($"ResetExternalOverrideTrigger_{ShutterKey.Key}", "ShutterExternalOverride");

            // is there already a trigger? if there is a trigger, cancel the trigger and schedule a new trigger
            if (scheduler.CheckExists(triggerKey).GetAwaiter().GetResult())
            {
                scheduler.UnscheduleJob(triggerKey).GetAwaiter().GetResult();
            }
            
            var jobDetail = JobBuilder.Create<ShutterResetExternalOverrideJob>()
                .WithIdentity(jobKey)
                .UsingJobData("ShutterKey", ShutterKey.Key)
                .Build();
            var trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .StartAt(DateTimeOffset.UtcNow.AddMinutes(90)) // reset after 90 minutes
                .Build();
            scheduler.ScheduleJob(jobDetail, trigger).GetAwaiter().GetResult();
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

    public void ResetExternalOverride()
    {
        IsExternalOverrideActive = false;
        ExternalOverrideStartTime = null;
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
        if ( !shutterTarget.ShutterKey.Equals(this.ShutterKey) )
            throw new ArgumentException($"The provided shutter target {shutterTarget} does not match the shutter key {this.ShutterKey} of this runtime.", nameof(shutterTarget));
        var shutter = ShutterContext.Shutter;
        // switch:      ShutterType.OpenClose => shutter.OpenCloseValue.TryWriteNumeric(shutterTarget.OpenClose, shutterTarget.Duration),

        bool success = true;
        switch (shutter.Configuration.Type)
        {
            case ShutterType.VenetianBlind:
                success = success && (shutter.PositionValue?.TryWriteNumeric(shutterTarget.TargetPosition.LiftPosition, this, logger) ?? false);
                success = success && (shutter.AngleValue?.TryWriteNumeric(shutterTarget.TargetPosition.TiltAngle, this, logger) ?? false);
                break;
            case ShutterType.Positional:
                success = success && (shutter.PositionValue?.TryWriteNumeric(shutterTarget.TargetPosition.LiftPosition, this, logger) ?? false);
                break;
            case ShutterType.OpenClose:
                success = success && (shutter.OpenCloseValue?.TryWriteNumeric(shutterTarget.TargetPosition.LiftPosition, this, logger) ?? false);
                break;
            default:
                logger.LogWarning("Unsupported shutter type {ShutterType} for shutter {ShutterKey}.", shutter.Configuration.Type, ShutterKey);
                break;
        }
        if ( !success )
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
}

public class ShutterExternalOverrideEventArgs(ShutterKey shutterKey, ValueWrittenEventArgs valueWrittenEventArgs) : EventArgs
{
    public ShutterKey ShutterKey { get; } = shutterKey;
    public ValueWrittenEventArgs ValueWrittenEventArgs { get; } = valueWrittenEventArgs;
}