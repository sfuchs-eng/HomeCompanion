using HomeCompanion.Base.Quartz;
using HomeCompanion.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace HomeCompanion.Logics.MeteoSchweiz;

public class MeteoSchweizLogic(
    IOptions<MeteoSchweizOptions> options,
    ISchedulerFactory schedulerFactory,
    IEventSubscriber subscriber,
    ILogger<MeteoSchweizLogic> logger
) : LogicBase(logger)
{
    private readonly IOptions<MeteoSchweizOptions> options = options;
    private readonly ISchedulerFactory schedulerFactory = schedulerFactory;
    private readonly IEventSubscriber subscriber = subscriber;
    private readonly ILogger<MeteoSchweizLogic> logger = logger;

    protected override async Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
    {
        // Schedule the MeteoSchweizPollingJob to run at the specified polling instants
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        var jobKey = typeof(MeteoSchweizPollingJob).GetJobKeyFromType()
            ?? throw new Exception("Failed to get job key for MeteoSchweizPollingJob.");

        foreach (var pollingInstant in options.Value.PollingInstants)
        {
            var trigger = TriggerBuilder.Create()
                .WithIdentity($"MeteoSchweizPollingTrigger_{pollingInstant}")
                .ForJob(jobKey)
                .WithCronSchedule(pollingInstant)
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(trigger, cancellationToken);
        }
    }
}
