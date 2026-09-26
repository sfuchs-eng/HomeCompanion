# OpenHAB connector type conversion

## Situation

The `OpenHabStateConverter` is responsible for converting between OpenHAB state values and HomeCompanion `IValue` types. The current implementation is hard-coded and does not support all possible OpenHAB state types, which can lead to issues when integrating with OpenHAB.
Furthermore it depends on KNX DPTs for the conversion even though OpenHAB and KNX should be decoupled. They are separate integrations, which may be connected to common IValue types via their bus mapping, but they should not depend on each other for type conversion.

## Solution

### Principle

The OpenHAB integration shall provide type mappings for the OpenHAB state types to HomeCompanion `IValue` types.
The mapping shall be configurable and extensible, allowing for custom mappings to be added as needed.
The mapping shall be integrated into OpenHabBusEndpointMapping, so that IValueContainers can be created for OpenHAB topics with the correct IValue type based on the configured mapping.
The same approach allows to do what KnxValues is doing, which is a double connection where normally communication takes place via the KNX bus, while the OpenHAB integration can be used to initialize the values when the system is started.

Separation of concerns: The OpenHAB integration shall not depend on the KNX integration for type conversion. Instead, it shall provide its own type mappings and conversion logic, which can be used independently of the KNX integration.

Extensibility: The type mappings shall be configurable and extensible, allowing for custom mappings to be added as needed. This can be achieved by providing a registration mechanism for custom type mappings, which can be used to add new mappings or override existing ones. Other extensions or local, generated IValueContainers using the corresponding bus mapping can be used to add new mappings or override existing ones.

### OpenHAB types and their values

The OpenHAB integration connected to OpenHAB 5 and later `Items` and their `State` values.
The basic types that are to be supported are listed in the [OpenHAB Items documentation](https://www.openhab.org/docs/concepts/items.html#items)
Download that page and evaluate the item types and their state values, and create a mapping to HomeCompanion IValue types. Consider the following guidance and deviations.

### Mapping logic

#### Enum types

The following enum types are mapped to `IValue<bool>` by default:

IncreaseDecreaseType	INCREASE (true), DECREASE (false)
NextPreviousType	NEXT (true), PREVIOUS (false)
OnOffType	ON (true), OFF (false)
OpenClosedType	OPEN (false), CLOSED (true) [deviation: note the reversed mapping here!]
PlayPauseType	PLAY (true), PAUSE (false)
RewindFastforwardType	REWIND (true), FASTFORWARD (false)
RefreshType	REFRESH (true)
StopMoveType	STOP (true), MOVE (false)
UpDownType	UP (true), DOWN (false)

More complex enums might be mapped to an `IValue<int>`, `IValue<string>`, or an `IValue<T>` where `T` is a custom type that implemented the enum (e.g. a C# Enum type that is mapped to the OpenHAB enum strings, camel cased though instead of upper case).

#### Numeric types

Numeric types with quantity are mapped by default to `IValue<UnitsNet.IQuantity>` with the appropriate quantity type, e.g. `IValue<UnitsNet.Length>` for LengthType, `IValue<UnitsNet.Temperature>` for TemperatureType, etc. Mismatches between the OpenHAB type and the UnitsNet quantity type should be reported as a warning in the diagnostics with a suggestion to add a custom mapping for the type.

Numeric types without quantity are mapped by default to `IValue<double>`, but can be mapped to `IValue<int>` or `IValue<long>` if the values are known to be integer values. Mismatches between the OpenHAB type and the IValue type should be reported as a warning in the diagnostics with a suggestion to add a custom mapping for the type.

Encoders and decoders shall be performance-optimized yet be robust against invalid input, e.g. by returning a default value or throwing an exception with a clear error message.
They shall gracefully handle slight mismatches, e.g. conversion of a counter value to a given `IValue<byte>` or `IValue<ushort>` type, and report a warning in the diagnostics if the value is out of range.
