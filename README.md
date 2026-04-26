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
- **KNX connectivity**: connect via KNX/net IP routing (UDP multicast) to a KNX system and receive/transmit Group Address write, read and read response telegrams
- **OpenHAB connectivity**: connect via OpenHAB REST API (item commands) and Websocket (event bus) to an OpenHAB instance and receive/transmit item state changes and commands
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

    Abstractions[HomeCompanion.Abstractions]
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
