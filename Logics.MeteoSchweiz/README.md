# Weatherforecast based on MeteoSchweiz data

References:

- [MeteoSchweiz](https://www.meteoschweiz.admin.ch/home.html)
- [MeteoSchweiz Local forecast data](https://opendatadocs.meteoswiss.ch/e-forecast-data/e4-local-forecast-data)
- [MeteoSwissApi](https://github.com/thomasgalliker/MeteoSwissApi)
- [MeteoSchweiz Open Data API](https://www.meteoschweiz.admin.ch/home/services-and-publications/produkte/opendata/api.html)
- [API access examples](https://github.com/MeteoSwiss/opendata-localforecast-demos)

For my needs the MeteoSwissApi from Thomas Galliker comes just handy.

## Notes

Package: `MeteoSwissApi` (NuGet)

Sample code:

```csharp
using MeteoSwissApi;

IMeteoSwissWeatherService weatherService = new MeteoSwissWeatherService(logger, weatherServiceConfiguration);

var weatherInfo = await weatherService.GetCurrentWeatherAsync(plz: 6330);
```
