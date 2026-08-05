using HomeCompanion.Base.Quartz;
using HomeCompanion.Diagnostics;
using Microsoft.Extensions.Logging;
using Quartz;

namespace HomeCompanion.Logics.Sun;

/// <summary>
/// Manages a periodic job that computes the sun position for each building and publishes an event with the updated sun positions.
/// The job takes care to update the sun position in any <see cref="ShadowingSpecial"/> referencing IValues for the sun position, if present in the <see cref="Model"/>.
/// </summary>
public class SunLogic(
    ISchedulerFactory schedulerFactory,
    IModelProvider modelProvider,
    TimeProvider timeProvider,
    IEventSubscriber subscriber,
    ILogger<SunLogic> logger
    ) : LogicBase(logger)
{
    private readonly ISchedulerFactory schedulerFactory = schedulerFactory;
    private readonly IModelProvider modelProvider = modelProvider;
    private readonly TimeProvider timeProvider = timeProvider;
    private readonly IEventSubscriber subscriber = subscriber;
    private readonly ILogger<SunLogic> logger = logger;

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
        subscriber.Subscribe<SunPositionPerBuildingUpdateEvent>(HandleSunPositionUpdateEvent);
    }

    public IReadOnlyDictionary<BuildingKey, SphericVector> LastPublishedSunPositions { get; private set; } = new Dictionary<BuildingKey, SphericVector>();

    private async ValueTask HandleSunPositionUpdateEvent(SunPositionPerBuildingUpdateEvent @event, CancellationToken cancellationToken)
    {
        logger.LogTrace("Received SunPositionUpdateEvent at {Time} with {Count} sun positions.", @event.Timestamp, @event.SunPositions.Count);
        LastPublishedSunPositions = @event.SunPositions;
    }

    private async Task<DiagnosticResultNode> FillBuildingSunPositionDiagnosticsAsync(DiagnosticResultNode parentNode, IReadOnlyDictionary<BuildingKey, SphericVector> sunPositions, CancellationToken cancellationToken)
    {
        var node = parentNode;
        var model = modelProvider.GetModel();
        foreach (var kvp in sunPositions)
        {
            try
            {
                var buildingKey = kvp.Key;
                var sunPosition = kvp.Value;
                var building = model.GetBuilding(buildingKey);
                var bnode = node.AddChild($"Building: {buildingKey} ({building.Name})");
                bnode.AddRecord("Location", $"Latitude: {building.Configuration.Location?.Latitude}, Longitude: {building.Configuration.Location?.Longitude}, Altitude: {building.Configuration.Location?.Altitude}");
                bnode.AddRecord("Sun Position", $"Azimuth: {sunPosition.Azimuth}, Elevation: {sunPosition.Elevation}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while populating diagnostic results for building {BuildingKey}.", kvp.Key);
                node.AddChild($"Building: {kvp.Key}").AddRecord("Error", ex.Message);
            }
        }
        return node;
    }

    protected override async Task<DiagnosticResultNode> PopulateDiagnosticResultsAsync(DiagnosticResultNode parentNode, CancellationToken cancellationToken)
    {
        await FillBuildingSunPositionDiagnosticsAsync(parentNode.AddChild("Last published sun positions per building"), LastPublishedSunPositions, cancellationToken);

        // compute present sun positions for all buildings in the model, regardless of whether they have a shadowing special or not
        var model = modelProvider.GetModel();
        var currentSunPositions = model.Buildings.Values
            .Where(b => b.Configuration.Location is not null)
            .Select(b => new { BuildingKey = new BuildingKey(b), SunPosition = SunPosition.GetPosition(timeProvider.GetLocalNow(), b.Configuration.Location!) })
            .ToDictionary(x => x.BuildingKey, x => x.SunPosition);
        await FillBuildingSunPositionDiagnosticsAsync(parentNode.AddChild("Current sun positions per building"), currentSunPositions, cancellationToken);
        return parentNode;
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
                SunPosition = SunPosition.GetPosition(timeProvider.GetLocalNow(), bk.Building.Configuration.Location!)
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
