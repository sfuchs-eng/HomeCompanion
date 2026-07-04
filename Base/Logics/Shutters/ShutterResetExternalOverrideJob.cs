using Quartz;
using Microsoft.Extensions.Logging;
using HomeCompanion.Base.Utilities;
using HomeCompanion.Base.Model;

namespace HomeCompanion.Logics.Shutters;

public class ShutterResetExternalOverrideJob : IJob
{
    private readonly ILogger<ShutterResetExternalOverrideJob> logger;
    private readonly IQueueFeeder<ShutterAutomationComputationTriggerContext> queueFeeder;

    public ShutterResetExternalOverrideJob(ILogger<ShutterResetExternalOverrideJob> logger, IQueueFeeder<ShutterAutomationComputationTriggerContext> queueFeeder)
    {
        this.logger = logger;
        this.queueFeeder = queueFeeder;
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

        // get the shutter and reset the external override state

        // enqueue a recomputation trigger

        var triggerContext = new ShutterAutomationComputationTriggerContext(shutterKey);
        await queueFeeder.EnqueueAsync(triggerContext, new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
    }
}