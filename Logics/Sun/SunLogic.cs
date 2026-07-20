using HomeCompanion.Base.Quartz;
using Microsoft.Extensions.Logging;
using Quartz;

namespace HomeCompanion.Logics.Sun;

/// <summary>
/// Manages a periodic job that computes the sun position for each building and publishes an event with the updated sun positions.
/// The job takes care to update the sun position in any <see cref="ShadowingSpecial"/> referencing IValues for the sun position, if present in the <see cref="Model"/>.
/// </summary>
public class SunLogic : LogicBase
{
    private readonly ISchedulerFactory schedulerFactory;
    private readonly ILogger<SunLogic> logger;

    public SunLogic(
        IEventPublisher eventPublisher,
        IEventSubscriber eventSubscriber,
        ISchedulerFactory schedulerFactory,
        ILogger<SunLogic> logger
    ) : base(eventPublisher, eventSubscriber)
    {
        this.schedulerFactory = schedulerFactory;
        this.logger = logger;
    }

    protected override async Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
    {
        // install a periodic trigger for the sun position update job, firing every 5 minutes and upfront
        var jobKey = typeof(SunPositionPerBuildingUpdateJob).GetJobKeyFromType()
            ?? throw new InvalidOperationException($"Could not get job key for job type {typeof(SunPositionPerBuildingUpdateJob).FullName}.");
        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{nameof(SunPositionPerBuildingUpdateJob)}_PeriodicTrigger", jobKey.Group)
            .ForJob(jobKey)
            .WithSimpleSchedule(x => x.WithIntervalInMinutes(5).RepeatForever())
            .StartNow() // Ensure it fires upfront
            .Build();
        await (await schedulerFactory.GetScheduler(cancellationToken)).ScheduleJob(trigger, cancellationToken);

        // subscribe to the sun position update event
        Subscriber.Subscribe<SunPositionPerBuildingUpdateEvent>(HandleSunPositionUpdateEvent);
    }

    private async ValueTask HandleSunPositionUpdateEvent(SunPositionPerBuildingUpdateEvent @event, CancellationToken cancellationToken)
    {
        logger.LogTrace("Received SunPositionUpdateEvent at {Time} with {Count} sun positions.", @event.Timestamp, @event.SunPositions.Count);
    }
}

[RegisterQuartzJob(
    jobName: nameof(SunPositionPerBuildingUpdateJob),
    jobGroup: nameof(HomeCompanion.Logics.Sun)
)]
public class SunPositionPerBuildingUpdateJob(
        IEventPublisher eventPublisher,
        IModelProvider modelProvider,
        TimeProvider timeProvider,
        ILogger<SunPositionPerBuildingUpdateJob> logger
) : IJob
{
    private readonly IEventPublisher eventPublisher = eventPublisher;
    private readonly IModelProvider modelProvider = modelProvider;
    private readonly TimeProvider timeProvider = timeProvider;
    private readonly ILogger<SunPositionPerBuildingUpdateJob> logger = logger;

    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogTrace("Executing Sun Position Update Job at {Time}", timeProvider.GetLocalNow());

        // 1. Compute the sun position for each building.
        var x = modelProvider
            .GetModel().Buildings.Values.Select(b => new { Building = b, Key = new BuildingKey(b) })
            .Where(bk => bk.Building.Configuration.Location is not null)
            .Select(bk => new
            {
                BuildingKey = bk.Key,
                Building = bk.Building,
                SunPosition = SunPosition.GetPosition(timeProvider.GetUtcNow(), bk.Building.Configuration.Location!)
            }).ToList();

        // 2. If the building has a shadowing special referencing IValues for the sun position, update the sun position in that special.
        foreach (var item in x)
        {
            if (item.Building.TryGetShadowingSpecial(out var shadowingSpecial))
            {
                if ( !shadowingSpecial.TrySetSunPosition(item.SunPosition, out var ex) )
                {
                    logger.LogWarning(ex, "Failed to set sun position for building {BuildingKey} in shadowing special.", item.BuildingKey);
                }
            }
        }

        // 3. Publish an event indicating that the sun position has been updated.
        await eventPublisher.PublishAsync(new SunPositionPerBuildingUpdateEvent
        {
            Timestamp = timeProvider.GetLocalNow(),
            SunPositions = x.ToDictionary(b => b.BuildingKey, b => b.SunPosition)
        });
    }
}

public sealed class SunPositionPerBuildingUpdateEvent : IEvent
{
    public required DateTimeOffset Timestamp { get; init; }
    public required IReadOnlyDictionary<BuildingKey, SphericVector> SunPositions { get; init; }
}
