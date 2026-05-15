# HomeCompanion

HomeCompanion is a C# application designed to run in parallel to other home/building automation solutions, in particular KNX and OpenHAB.

It's built around the idea of

- having basic functionality at the KNX layer
- use OpenHAB for different UI/mobile scenarios as well as simple automation scripting
- use HomeCompanion for more complex or parametrized automation logic that is not easily implemented in OpenHAB's scripting languages or KNX's logic blocks.

Further devices in the home automation system are integrated either via MQTT and/or using OpenHAB's large number of supported bridges. The HomeCompanion framework provides modularity for adding further connectivity options as well as for implementing the automation logic itself. This provides an integration
path not only for other devices but also for integration with other home automation solutions.

## Features

### Framework features

- **Modular automation logic**: implement your automation logic as `ILogic` modules, which are loaded at runtime and can be enabled/disabled via configuration
- **Centralized value lifecycle**: `ValuesManager` initializes all discovered `IValue` instances at startup and routes `ValueUpdateReceived` / `ValueWriteReceived` events by `Target` to the owning value instance
- **KNX connectivity**: connect via KNX/net IP routing (UDP multicast) to a KNX system and receive/transmit Group Address write, read and read response telegrams
- **OpenHAB connectivity**: connect via OpenHAB REST API (item commands) and Websocket (event bus) to an OpenHAB instance and receive/transmit item state changes and commands. Inbound OpenHAB `ItemState*` events are mapped to `ValueUpdateReceived`-based events, while inbound `ItemCommandEvent` is mapped to `ValueWriteReceived`-based events.
- **MQTT connectivity**: connect to a MQTT broker and receive/transmit messages on specified topics
- **InfluxDB connectivity**: connect to an InfluxDB instance and write data points to specified measurements
- **User alerting**: send user notifications via email or channel them to an MQTT topic for using OpenHAB push notifications

### Contained logic modules

- ...

## Structure

The solution is organized into several projects:

- `HomeCompanion.Server`: the main Blazor server application
- `HomeCompanion.Core`: contains the core functionality of the framework, mostly used by the server application. This includes for example the `LogicManager` which is responsible for loading and managing the `ILogic` modules, as well as the connectivity managers for KNX, OpenHAB, MQTT and InfluxDB
- `HomeCompanion.Base`: contains the base classes and for the framework, such as for example `LogicBase` which implements the `ILogic` interface with common basic functionality for the logic modules
- `HomeCompanion.Abstraction`: contains the abstractions for the framework, such as for example `ILogic` and `IDiagnostic` as well as the interfaces for the connectivity providers. These are used by the server application as well as the logic modules, and are implemented in the `HomeCompanion.Core` project and provisioned for use in the logic modules via dependency injection
- `HomeCompanion.Logic`: contains a selection of built-in logic modules, implementing the `ILogic` interface
- `HomeCompanion.Tests`: contains unit tests for the framework and the logic modules, using NUnit

### Value event architecture

`IValue` instances are initialized once by `ValuesManager` during startup. Connectivity providers do not call `IValue.Initialize`.

Responsibilities are split as follows:

- `ValuesManager`: discovers values from registered `IValuesContainer` instances, calls `IValue.Initialize`, and performs centralized target-based routing for inbound `ValueUpdateReceived` / `ValueWriteReceived`
- Connectivity providers (KNX/OpenHAB/...): discover bus-mapped values for endpoint lookup and bridge bus traffic to/from event bus events
- Values (`ValueBase<T>`): remain bus-agnostic and only process routed payloads plus publish value change/write events

This avoids per-value event bus subscriptions and keeps bus-specific logic in connectivity providers.

### Dependencies

The application uses the following main dependencies:

- .NET 10.0 for the main application, including
  - `Microsoft.Extensions.DependencyInjection`
  - `Microsoft.Extensions.Logging`
  - `Microsoft.Extensions.Options`
- `NUnit` for unit testing
- `SRF.Network` for KNX, OpenHAB and MQTT connectivity

The internal project dependencies are as follows:

```mermaid
graph TD
    Tests[HomeCompanion.Tests] --> Server
    Tests --> Core
    Tests --> Base
    Tests --> Logics

    Server[HomeCompanion.Server] --> Core

    Core[HomeCompanion.Core] --> Base

    Logics[HomeCompanion.Logics] --> Base

    Base[HomeCompanion.Base] --> Abstractions

    Abstractions[HomeCompanion]
```

## Development approach

### Testing modes

The following maturity approach is foreseen:

1. unit testing based on NUnit tests which must run offline without access to the home automation environment
2. manual integration testing in the real environment, yet on a separate instance of the application
3. diagnostic features built into the application based on `IDiagnostic`, running in either version, real or testing
4. production operation in the real environment on a separate instance of the application

The application supports to run multiple innstances in parallel at once on the same home automation environment. This allows to have a "testing mode" instance, which can be used for testing new logic modules or changes to existing ones without affecting the "production mode" instance, which is running the stable automation logic for the home. Reason is that there's rarely a test environment for home automation and hands-on testing / pilot operation happens in the real environment. Avoiding interference between stable/production and testing instances is realized by enabling/disabling logic modules via configuration in either system.

## Getting started

### Prerequisites

- .NET 10.0 SDK
- Access to a KNX system (optional, for KNX connectivity)
- Access to an OpenHAB instance (optional, for OpenHAB connectivity)
- Access to a MQTT broker (optional, for MQTT connectivity)
- Access to an InfluxDB v2 instance (optional, for InfluxDB connectivity)

### Installation

The application to run it all is `HomeCompanion.Server`, which is a Blazor server application.
The other projects in the solution are class libraries for the framework and the logic modules, which are loaded at runtime by the server application.

1. Clone the repository: ...
2. Build the application: ...
3. Configure the application: ...
4. Run for testing: ...

### Configuration

HomeCompanion reads configuration from the normal ASP.NET Core sources and additionally from these optional JSON files:

- `/etc/HomeCompanion.json` for system-wide defaults
- `~/.config/HomeCompanion.json` for per-user overrides on Linux

Those files are loaded after `appsettings.json` and `appsettings.{Environment}.json`, but before environment variables. In practice this means:

- repository defaults live in `Server/appsettings.json`
- machine-specific settings belong in `/etc/HomeCompanion.json`
- user-specific or development overrides belong in `~/.config/HomeCompanion.json`
- environment variables still have the highest precedence

For a KNX/IP Routing setup, a minimal user configuration can look like this:

```json
{
  "Knx": {
    "ConnectionString": "Type=IpRouting;KnxAddress=1.1.10;LocalIpAddress=192.168.200.0/24",
    "EtsGAExportFile": "/path/to/GroupAddresses.xml",
    "KnxMasterFolder": "/path/to/knx-master",
    "KnxDomainConfigFile": "/path/to/KnxDomainConfig.json"
  }
}
```

Notes for KNX configuration:

- `Knx:ConnectionString` uses Falcon-style `key=value` pairs separated by `;` or `,`.
- `KnxAddress` sets the local KNX individual address used in outbound cEMI frames.
- `LocalIpAddress` selects the local network interface for KNX multicast. This is useful on hosts with multiple NICs where the default multicast route would otherwise use the wrong interface.
- `LocalIpAddress` accepts an exact host IP, a subnet base address, or CIDR notation. Examples: `192.168.200.23`, `192.168.200.0`, `192.168.200.0/24`, `fd00:1234::/64`.
- If multiple interfaces match the same subnet hint, Ethernet is preferred over Wi-Fi.
- If `LocalIpAddress` is omitted, HomeCompanion uses the operating system's default multicast interface.

The UDP multicast settings for KNX/IP Routing default to the standard multicast endpoint and usually do not need to be configured explicitly:

```json
{
  "Knx": {
    "Connections": {
      "default": {
        "MulticastAddress": "224.0.23.12",
        "Port": 3671
      }
    }
  }
}
```

Use `Knx:Connections` when you need to override UDP defaults per connection or connect to multiple KNX/IP Routing segments. If `Knx:Connections` is omitted, HomeCompanion registers a single connection named `default` and falls back to the library defaults for KNX/IP Routing.
