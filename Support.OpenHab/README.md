# HomeCompanion Configuration Support for OpenHAB

## Purpose

This library provides support for generating OpenHAB configuration files and value containers based on the HomeCompanion model and a production instance of OpenHAB.
It can exclude OpenHAB items that are already mapped to KNX group addresses, so that the generated value container only contains OpenHAB items that are not mapped to KNX.

See `HomeCompanion.Support.Knx` for KNX configuration support, which also includes a code generator for KNX values containers and can generate OpenHAB items and things configuration files for KNX group addresses.

## Usage

Register services:

```csharp
using Microsoft.Extensions.DependencyInjection;
using HomeCompanion.Support.OpenHab;

var services = new ServiceCollection();
services.AddHomeCompanionOpenHabSupport();
```

Inject the `IHomeCompanionOpenHabConfigFactory` into your application:

```csharp
public class MyApp(IHomeCompanionOpenHabConfigFactory openHabConfigFactory)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await openHabConfigFactory.GenerateOpenHabItemsValueContainerAsync(cancellationToken);
    }
}
```
