using System.Text.Json;
using System.Text.Json.Serialization;
using HomeCompanion.Events;
using MeteoSwissApi;
using MeteoSwissApi.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace HomeCompanion.Logics.MeteoSchweiz;

[RegisterQuartzJob(nameof(MeteoSchweizPollingJob), nameof(HomeCompanion.Logics.MeteoSchweiz))]
public class MeteoSchweizPollingJob(
    IOptions<MeteoSchweizOptions> options,
    IMeteoSwissWeatherService meteoSwissWeatherService,
    IEventPublisher eventPublisher,
    TimeProvider timeProvider,
    ILogger<MeteoSchweizPollingJob> logger
) : IJob
{
    private readonly IOptions<MeteoSchweizOptions> options = options;
    private readonly IMeteoSwissWeatherService meteoSwissWeatherService = meteoSwissWeatherService;
    private readonly TimeProvider timeProvider = timeProvider;
    private readonly IEventPublisher eventPublisher = eventPublisher;
    private readonly ILogger<MeteoSchweizPollingJob> logger = logger;

    public async Task Execute(IJobExecutionContext context)
    {
        var weatherForecast = await GetLatestForecastAsync();
        eventPublisher.Publish(new WeatherForecastEvent(weatherForecast));
        logger.LogTrace("Published weather forecast event: {Forecast}", weatherForecast);
    }

    public async Task<WeatherForecast<SwissMeteoWeatherForecastDay>> GetLatestForecastAsync(CancellationToken cancellationToken = default)
    {
        logger.LogTrace("Retrieving latest weather forecast for PLZ {PLZ} from MeteoSwiss API.", options.Value.PLZ);
        var forecast = await meteoSwissWeatherService.GetForecastAsync(plz: options.Value.PLZ);
        logger.LogTrace("Retrieved latest weather forecast for PLZ {PLZ} from MeteoSwiss API: {Forecast}", options.Value.PLZ, forecast);
        return new WeatherForecast<SwissMeteoWeatherForecastDay>
        {
            Received = timeProvider.GetUtcNow(),
            Created = forecast.CurrentWeather.Time,
            Forecast = [.. forecast.Forecast.Select(f => new SwissMeteoWeatherForecastDay(f))]
        };
    }
}

public class WeatherForecast<T> : IWeatherForecast where T : IWeatherForecastDay
{
    public DateTimeOffset Received { get; set; }
    public DateTimeOffset? Created { get; set; }
    public List<T> Forecast { get; set; } = [];
    
    [JsonIgnore]
    IReadOnlyList<IWeatherForecastDay> IWeatherForecast.Forecast => (IReadOnlyList<IWeatherForecastDay>)Forecast;

    public override string ToString()
    {
        // use json serialization to produce a human-readable representation of the forecast
        return JsonSerializer.Serialize(this);
    }
}

public class SwissMeteoWeatherForecastDay : IWeatherForecastDay
{
    private Forecast f;

    public SwissMeteoWeatherForecastDay(Forecast f)
    {
        this.f = f;
    }

    public DateOnly Date => DateOnly.FromDateTime(f.DayDate);
    public double TemperatureAvg => (TemperatureMin + TemperatureMax)/2;
    public double TemperatureMin => f.TemperatureMin.DegreesCelsius;
    public double TemperatureMax => f.TemperatureMax.DegreesCelsius;
    public double Precipitation => f.Precipitation.Millimeters;
    public double PrecipitationMin => f.PrecipitationMin.Millimeters;
    public double PrecipitationMax => f.PrecipitationMax.Millimeters;
}