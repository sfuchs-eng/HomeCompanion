# HomeCompanion Configuration Support for KNX

## Overview

This library provides support for generating KNX configuration files and value containers based on the HomeCompanion model,
a ETS Group Address export file (version 5 as of now).

Intermediate configuration files are generated in order to allow for manual review and adjustments before generating the final HomeCompanion KNX values container and OpenHAB configuration files.

## Usage

Register services:

```csharp
using Microsoft.Extensions.DependencyInjection;
using HomeCompanion.Support.Knx;

var services = new ServiceCollection();
services.AddKnxConfigSupport();
```

Inject the required services into your application:

```csharp
public class MyApp
(
    // Knx.Core
    IDptFactory dptFactory, // DPT metadata factory, by default caching DPT objects
    IDptNumericInfoFactory dptNumericInfoFactory, // get the numeric information for a DPT, e.g. min/max values, units, etc.
    IPdtEncoderFactory pdtEncoderFactory, // get the encoder for a DPT, e.g. to encode/decode values to/from KNX telegrams

    // Knx.Config
    IOptions<KnxSystemConfigOptions> knxSystemConfigOptions, // configuration options for the KNX context, e.g. connection string, multicast address, file paths, etc.
    DomainConfiguration knxDomainConfiguration, // the Group Address configuration as read from the ETS export file enriched with additional information, e.g. property names, ...
    IKnxSystemConfigurationResolver knxSystemConfigurationResolver, // get the configuration metadata for a group address, e.g. DPT, name, description, etc., retrieved from the DomainConfiguration and cached for efficient lookup, includes the IDptResolver functionality
    IKnxMasterDataProvider knxMasterDataProvider, // get the KNX master data
)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Use the injected services to generate KNX configuration files and value containers
    }
}
```
