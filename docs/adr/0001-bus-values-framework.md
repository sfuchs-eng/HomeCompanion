# ADR-0001: Bus Values Framework

**Date:** 2026-04-28

## Context

HomeCompanion's core event system revolves around values that represent the state of data points in the home automation environment. These values can be read from or written to by logic modules, and they can also receive updates from external sources (e.g. KNX bus, OpenHAB items). A consistent framework for defining, updating, and writing these values is needed to:

- Provide a clear contract for logic modules to interact with values
- Support multiple connectivity providers (KNX, OpenHAB, MQTT, etc.) without coupling logic modules to specific implementations
- Enable unit testing of logic modules without requiring a real bus connection
- Allow values to be discoverable and manageable by the connectivity providers

## Decision

A new framework for "bus values" was introduced in `HomeCompanion.Base` that defines the following key concepts:

- **`IValue<T>`**: A generic interface representing a value of type `T` that can be read and written. It has a `Value` property and a `Write(T value)` method.
- **`IValuesContainer`**: An interface for classes that contain `IValue<T>` properties. This allows connectivity providers to discover values via reflection.

`IValuesContainer` is implemented as singletons that are registered in DI and can be injected into logic modules.

`IValue<T>` properties are defined in these container classes. Each property is identified by its name and parent class name.
IValue connect to the event bus in order to receive updates (BusWriteReceived, BusReadReceived, BusReadResponseReceived) and
to request transmissions to buses on which the value resides (BusWriteRequested, BusReadRequested, BusReadResponseRequested).

The `IValue<T>` implementations shall remain bus-agnostic and not contain any bus-specific logic or dependencies. The connectivity providers will subscribe to the event bus for value write requests and propagate them to the actual bus (e.g. KNX telegrams).

## Consequences

### General value framework

- Logic modules can interact with values as plain C# properties (e.g. `myValues.Temperature.Value`) and write to them via the `Write()` method (e.g. `myValues.Temperature.Write(22.5)`).
- Connectivity providers can discover all `IValue<T>` properties in registered `IValuesContainer` classes via reflection at startup and subscribe to the event bus to update their values based on incoming bus telegrams.
- Values publish events when they are written to, allowing connectivity providers to listen to the bus for write requests and propagate them to the actual bus (e.g. KNX telegrams).
- Unit testing of logic modules is possible by using mock implementations of `IValuesContainer` and `IValue<T>` that do not require a real bus connection.

### Bus value mapping

Because `IValue<T>` implementations are bus-agnostic, the mapping between bus values and actual bus endpoints is handled by the connectivity providers. This allows the same value framework to be used across different bus technologies without modifying the core logic modules.
Given a `IValue` object, connectivity providers can determine which bus endpoint it corresponds to (e.g. KNX group address, OpenHAB item name) based on

- the property name and parent class name, and subscribe to the event bus for updates to that value.
- using the `IValue.TryGetBusEndpointMapping<TBusMapping>(object busIdentifier, out TBusMapping? mapping)` method can be used for explicit mappings.

An `IValueContainer` class with their `IValue<T>` properties could be

- source-generated from a bus configuration (KNX), mappings inserted during startup by the connectivity provider or manually coded.
- dynamically initialized by the connectivity provider at startup by matching values to bus endpoints based on naming conventions or explicit configuration.
- dynamically added to an existing container instance at runtime by the connectivity provider when a new bus endpoint is discovered
