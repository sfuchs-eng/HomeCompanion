using HomeCompanion.Base.Quartz;
using HomeCompanion.Diagnostics;
using HomeCompanion.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace HomeCompanion.Logics.MeteoSchweiz;

/// <summary>
/// Subscribe to <see cref="WeatherForecastEvent"/> to receive the latest weather forecast data.
/// The logic at hand is responsible for scheduling the <see cref="MeteoSchweizPollingJob"/> to run at the configured polling instants, publishing those events.
/// It also serves as a cache for the latest forecast data, which can be accessed via the <see cref="LatestForecast"/> property.
/// </summary>
/// <typeparam name="MeteoSchweizOptions"></typeparam>
public class MeteoSchweizLogic(
    IOptions<MeteoSchweizOptions> options,
    ISchedulerFactory schedulerFactory,
    IEventSubscriber subscriber,
    ILogger<MeteoSchweizLogic> logger
) : LogicBase(logger), IDiagnosable
{
    private readonly IOptions<MeteoSchweizOptions> options = options;
    private readonly ISchedulerFactory schedulerFactory = schedulerFactory;
    private readonly IEventSubscriber subscriber = subscriber;
    private readonly ILogger<MeteoSchweizLogic> logger = logger;

    private IWeatherForecast? latestForecast;
    public IWeatherForecast? LatestForecast => latestForecast;

    // semaphore to await the completion of the MeteoSchweizPollingJob when no forecast is available yet
    private readonly SemaphoreSlim forecastSemaphore = new(0, 1);

    /// <summary>
    /// Return the latest forecast available, or trigger a forecast retrieval if none is available yet and await its completion.
    /// </summary>
    public async Task<IWeatherForecast> GetForecastAsync(CancellationToken cancellationToken = default)
    {
        if (latestForecast is not null)
        {
            return latestForecast;
        }

        // retrieve the forecast from the MeteoSchweizPollingJob if none is available yet.
        // Approach: trigger the job manually and await its completion, then return the latest forecast after receiving the event.
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        var jobKey = typeof(MeteoSchweizPollingJob).GetJobKeyFromType()
            ?? throw new Exception("Failed to get job key for MeteoSchweizPollingJob.");
        // run the job on the background queue
        _ = Task.Run(async () =>
        {
            try
            {
                logger.LogTrace("Triggering MeteoSchweizPollingJob to retrieve the latest forecast.");
                await scheduler.TriggerJob(jobKey, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to trigger MeteoSchweizPollingJob.");
                forecastSemaphore.Release();
            }
        }, cancellationToken);

        // await the completion of the job and the reception of the event; use a linked cancellation token with 1 min timeout to avoid deadlocks in case of misconfiguration or other issues
        logger.LogTrace("Awaiting the completion of the MeteoSchweizPollingJob and the reception of the WeatherForecastEvent.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(1));
        await forecastSemaphore.WaitAsync(cts.Token);

        if (latestForecast is not null)
        {
            return latestForecast;
        }

        throw new Exception("Failed to retrieve the weather forecast.");
    }

    protected override async Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
    {
        // Listen to weather forecast events
        subscriber.Subscribe<WeatherForecastEvent>(HandleWeatherForecastEventAsync);

        // Schedule the MeteoSchweizPollingJob to run at the specified polling instants
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        var jobKey = typeof(MeteoSchweizPollingJob).GetJobKeyFromType()
            ?? throw new Exception("Failed to get job key for MeteoSchweizPollingJob.");

        var cnt = 1;
        foreach (var pollingInstant in options.Value.PollingInstants)
        {
            var trigger = TriggerBuilder.Create()
                .WithIdentity($"MeteoSchweizPollingTrigger_{cnt++}", "MeteoSchweizPollingTriggers")
                .ForJob(jobKey)
                .WithCronSchedule(pollingInstant)
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(trigger, cancellationToken);
        }
    }

    protected async ValueTask HandleWeatherForecastEventAsync(WeatherForecastEvent weatherForecastEvent, CancellationToken cancellationToken = default)
    {
        // Handle the received weather forecast event
        var forecast = weatherForecastEvent.Forecast;
        latestForecast = forecast;
        forecastSemaphore.Release();
    }

    protected override async Task<DiagnosticResultNode> PopulateDiagnosticResultsAsync(DiagnosticResultNode parentNode, CancellationToken cancellationToken)
    {
        // Populate diagnostic results with the latest forecast data
        var node = parentNode;
        if (latestForecast is not null)
        {
            node.AddRecord("LatestForecast", latestForecast.ToString());
        }
        else
        {
            node.AddRecord("LatestForecast", "No forecast available yet. Triggering task to retrieve the forecast.");
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            await GetForecastAsync(cts.Token)
            .ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    node.AddRecord("LatestForecast", t.Result.ToString());
                }
                else
                {
                    node.AddRecord("LatestForecast", $"Failed to retrieve forecast: {t.Exception?.GetBaseException().Message}");
                }
            }, cts.Token);
        }
        return node;
    }
}