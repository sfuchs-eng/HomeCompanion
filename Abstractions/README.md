# HomeCompanion.Abstractions

This project contains core interfaces that define the contracts for the HomeCompanion application.
It includes:

- `ILogic`: The main interface for logic modules that process events and perform actions.
- `IDiagnostic`: An interface for components that provide diagnostic functionality (e.g. health checks, metrics, debug endpoints).
- Connectivity provider interfaces, building the links between busses like KNX, OpenHAB, MQTT to the `IValuesContainer` with their `IValue` data points via the event system.
- Event bus interfaces for publishing and subscribing to events in a decoupled way.
- ...

## Special conventions

- Namespaces follow their counterparts in `HomeCompanion.Core` and `HomeCompanion.Base` (e.g. `HomeCompanion.Logics` for logic-related interfaces, `HomeCompanion.Values` for value-related interfaces, etc.) to keep the vocabulary consistent across projects while avoiding circular dependencies.
