# Weatherforecast based on MeteoSchweiz data

## Weatherforecast (Temperature, Precipitation)

References:

- [MeteoSchweiz](https://www.meteoschweiz.admin.ch/home.html)
- [MeteoSchweiz Local forecast data](https://opendatadocs.meteoswiss.ch/e-forecast-data/e4-local-forecast-data)
- [Datensaetze Bund](https://data.geo.admin.ch/)
- [MeteoSwissApi](https://github.com/thomasgalliker/MeteoSwissApi)
- [MeteoSchweiz Open Data API](https://www.meteoschweiz.admin.ch/home/services-and-publications/produkte/opendata/api.html)
- [API access examples](https://github.com/MeteoSwiss/opendata-localforecast-demos)

For my needs the MeteoSwissApi from Thomas Galliker comes just handy.

The `MeteoSchweizLogic` is a wrapper around the `MeteoSwissApi` and publishes `IWeatherForecast` events to the HomeCompanion event bus.

## Hail

This is **not implemented yet**

References for `ch.meteoschweiz.ogd-radar-hail`:

- [download](https://data.geo.admin.ch/browser/index.html#/collections/ch.meteoschweiz.ogd-radar-hail)
- [API](https://data.geo.admin.ch/api/stac/v0.9/collections/ch.meteoschweiz.ogd-radar-hail)
- [metadata](http://www.geocat.ch/geonetwork/srv/ger/catalog.search#/search?any=ch.meteoschweiz.ogd-radar-hail)
  - [Radarprodukte Hagel - POH (probability of hail) und MESHS (maximum hail size)](https://www.geocat.ch/geonetwork/srv/ger/catalog.search#/metadata/c6d530c8-e003-43c2-bb2d-578e06f29cd0)
