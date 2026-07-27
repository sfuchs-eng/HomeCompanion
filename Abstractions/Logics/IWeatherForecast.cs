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
    /// <summary>
    /// The timestamp when the forecast was received.
    /// </summary>
    public DateTimeOffset Received { get; }

    /// <summary>
    /// The timestamp when the forecast was created.
    /// </summary>
    public DateTimeOffset? Created { get; }

    public IReadOnlyList<IWeatherForecastDay> Forecast { get; }
}

public interface IWeatherForecastDay
{
    public DateOnly Date { get; }

    /// <summary>
    /// The average temperature in °C for the day.
    /// </summary>
    public double TemperatureAvg { get; }

    /// <summary>
    /// The minimum temperature in °C for the day.
    /// </summary>
    public double TemperatureMin { get; }

    /// <summary>
    /// The maximum temperature in °C for the day.
    /// </summary>
    public double TemperatureMax { get; }

    /// <summary>
    /// The expected (50 percentile?) precipitation in mm for the day.
    /// </summary>
    public double Precipitation { get; }

    /// <summary>
    /// The minimum (90 percentile?) precipitation in mm for the day.
    /// </summary>
    /// <value></value>
    public double PrecipitationMin { get; }

    /// <summary>
    /// The maximum (10 percentile?) precipitation in mm for the day.
    /// <value></value>
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