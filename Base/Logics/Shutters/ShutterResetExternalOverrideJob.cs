using Quartz;
using Microsoft.Extensions.Logging;
using HomeCompanion.Base.Utilities;
using HomeCompanion.Base.Model;

namespace HomeCompanion.Logics.Shutters;

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

        shutterRuntime.ResetExternalOverride();

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