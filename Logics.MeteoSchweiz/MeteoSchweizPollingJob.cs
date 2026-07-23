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
        var forecast = await meteoSwissWeatherService.GetForecastAsync(plz: options.Value.PLZ);
        var weatherForecast = new WeatherForecast<SwissMeteoWeatherForecastDay>
        {
            Received = timeProvider.GetUtcNow(),
            Created = forecast.CurrentWeather.Time,
            Forecast = [.. forecast.Forecast.Select(f => new SwissMeteoWeatherForecastDay(f))]
        };

        eventPublisher.Publish(new WeatherForecastEvent(weatherForecast));
    }
}

public class WeatherForecast<T> : IWeatherForecast where T : IWeatherForecastDay
{
    public DateTimeOffset Received { get; set; }
    public DateTimeOffset? Created { get; set; }
    public List<T> Forecast { get; set; } = [];
    IReadOnlyList<IWeatherForecastDay> IWeatherForecast.Forecast => (IReadOnlyList<IWeatherForecastDay>)Forecast;
}

public class SwissMeteoWeatherForecastDay : IWeatherForecastDay
{
    private Forecast f;

    public SwissMeteoWeatherForecastDay(Forecast f)
    {
        this.f = f;
    }

    public double TemperatureAvg => (TemperatureMin + TemperatureMax)/2;
    public double TemperatureMin => f.TemperatureMin.DegreesCelsius;
    public double TemperatureMax => f.TemperatureMax.DegreesCelsius;
    public double Precipitation => f.Precipitation.Millimeters;
    public double PrecipitationMin => f.PrecipitationMin.Millimeters;
    public double PrecipitationMax => f.PrecipitationMax.Millimeters;

    public DateOnly Date => DateOnly.FromDateTime(f.DayDate);
}