using HomeCompanion.Events;

namespace HomeCompanion.Logics;

/// <summary>
/// Intention: have some ILogic that periodically publishes a weather forecast and/or critical weather events to the system.
/// </summary>
public interface IWeatherForecast
{
    public DateTimeOffset Received { get; }

    public DateTimeOffset? Created { get; }

    public IReadOnlyList<IWeatherForecastDay> Forecast { get; }
}

public interface IWeatherForecastDay
{
    public DateOnly Date { get; }
    public double TemperatureAvg { get; }
    public double TemperatureMin { get; }
    public double TemperatureMax { get; }
    public double Precipitation { get; }
    public double PrecipitationMin { get; }
    public double PrecipitationMax { get; }
}

public class WeatherForecastEvent(IWeatherForecast forecast) : IEvent
{
    public IWeatherForecast Forecast { get; } = forecast;
    public DateTimeOffset Timestamp => Forecast.Created ?? Forecast.Received;
    DateTimeOffset IEvent.Timestamp { get => Timestamp; init => throw new NotImplementedException("The Timestamp property is read-only and derived from the contained forecast."); }
}