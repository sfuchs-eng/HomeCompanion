using Quartz;
using Microsoft.Extensions.Logging;
using HomeCompanion.Base.Utilities;
using HomeCompanion.Base.Model;
using HomeCompanion.Base.Quartz;

namespace HomeCompanion.Logics.Shutters;

/// <summary>
/// Resets the temporary external override for a shutter and triggers a recomputation of the shutter automation.
/// </summary>
/// <remarks>
/// Provide the shutter key in the JobDataMap with the key "ShutterKey" to specify which shutter's external override should be reset.
/// </remarks>
/// <typeparam name="ShutterResetExternalOverrideJob"></typeparam>
[RegisterQuartzJob(nameof(ShutterResetExternalOverrideJob), nameof(HomeCompanion.Logics.Shutters))]
public class ShutterResetExternalOverrideJob(
    IRuntimesProvider runtimesProvider,
    ILogger<ShutterResetExternalOverrideJob> logger,
    TimeProvider timeProvider,
    IQueueFeeder<ShutterAutomationComputationTriggerContext> queueFeeder
) : IJob
{
    private readonly IQueueFeeder<ShutterAutomationComputationTriggerContext> queueFeeder = queueFeeder;
    private readonly IRuntimesProvider runtimesProvider = runtimesProvider;
    private readonly ILogger<ShutterResetExternalOverrideJob> logger = logger;
    private readonly TimeProvider timeProvider = timeProvider;

    /// <summary>
    /// Provide the shutter key in the JobDataMap with the key "ShutterKey" to specify which shutter's external override should be reset.
    /// </summary>
    [Obsolete("Use typeof(...).GetJobKeyFromType() directly instead.")]
    public static JobKey GetJobKey()
    {
        return typeof(ShutterResetExternalOverrideJob).GetJobKeyFromType()
                ?? throw new InvalidOperationException($"Could not get job key for job type {typeof(ShutterResetExternalOverrideJob).FullName}.");
    }

    public static TriggerKey GetTriggerKey(ShutterKey shutterKey)
    {
        return new TriggerKey($"{nameof(ShutterResetExternalOverrideJob)}_{shutterKey}", nameof(HomeCompanion.Logics.Shutters));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var shutterKeyString = context.MergedJobDataMap.GetString("ShutterKey");
        if (shutterKeyString == null)
        {
            logger.LogError("ShutterKey not found in job data map.");
            return;
        }

        var shutterKey = ShutterKey.Parse(shutterKeyString);
        var shutterRuntime = runtimesProvider.GetShutterRuntime(shutterKey);

        await shutterRuntime.ResetExternalOverrideAsync();

        // enqueue a recomputation trigger
        var triggerContext = new ShutterAutomationComputationTriggerContext(
            thingKeys: [shutterKey],
            scope: ShutterAutomationComputationScope.ShutterSpecific,
            triggeringValue: null,
            valueEventArgs: null,
            timestamp: timeProvider.GetUtcNow(),
            urgency: ShutterAutomationComputationTriggerUrgency.Slow
        );
        await queueFeeder.EnqueueAsync(triggerContext, new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
    }
}