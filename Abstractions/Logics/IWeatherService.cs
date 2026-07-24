namespace HomeCompanion.Logics;

/// <summary>
/// Defines the contract for a weather service that provides the latest weather forecast.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Example implementation: <see cref="HomeCompanion.Logics.MeteoSchweiz.MeteoSchweizLogic"/></item>
/// <item>Consumers of weather forecast data can subscribe to <see cref="WeatherForecastEvent"/> to receive the latest forecast data.</item>
/// </list>
/// </remarks>
public interface IWeatherService
{
    /// <summary>
    /// Retrieve the latest weather forecast for the configured location.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The latest weather forecast.</returns>
    Task<IWeatherForecast> GetForecastAsync(CancellationToken cancellationToken = default);
}