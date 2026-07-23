using HomeCompanion.Events;

namespace HomeCompanion.Logics;

/// <summary>
/// <see cref="ILogic"/> and other consumers of weather forecast data can
/// subscribe to <see cref="WeatherForecastEvent"/> to receive the latest forecast data.
/// </summary>
/// <remarks>
/// The sender is realized by <see cref="HomeCompanion.Logics.MeteoSchweiz"/> as example for a concrete implementation
/// of the sender of the weather forecast event.
/// </remarks>
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

/// <summary>
/// Event published when a new weather forecast is available.
/// See <see cref="HomeCompanion.Logics.MeteoSchweiz"/> for an example implementation.
/// </summary>
public class WeatherForecastEvent(IWeatherForecast forecast) : IEvent
{
    public IWeatherForecast Forecast { get; } = forecast;
    public DateTimeOffset Timestamp => Forecast.Created ?? Forecast.Received;
    DateTimeOffset IEvent.Timestamp { get => Timestamp; init => throw new NotImplementedException("The Timestamp property is read-only and derived from the contained forecast."); }
}