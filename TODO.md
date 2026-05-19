# HomeCompanion TODO


---

## Present work focus

### Port existing functionality from the old HomeCompanion solution into the one at hands

- [ ] InfluxDB connectivity
- [ ] MQTT connectivity
- [ ] e-Mail notifications
- [ ] generic alerting/notification framework

### Furthermore

- [ ] Refactor `KnxBusEndpointMapping` to provide a proper override of `FormatValueForDisplay` that uses the DPT information to format the value for display purposes, instead of relying on a generic JSON serialization of the mapping configuration. This will allow for more user-friendly display of KNX values in the UI and logs, showing the actual value with appropriate formatting based on its DPT (e.g. showing "22.5 °C" instead of just "22.5" for a temperature value).
- [ ] Resolve initialization bugs related to value conversions (e.g. OpenHAB sends string `""` while the target is `IValue<bool>` which cannot be converted, causing exceptions).
- [ ] IValuesContainer with OpenHabItems that are not mapped to any KNX group address. Add code-gen to SRF.Network.Cli as for KNX, same command `kc -hc` all in one go.
- [ ] Have an IValuesContainer for dynamic, internal values. This allows Logics to create/manage their own values without needing to define them in the ETS export or OpenHab item list, which is more flexible and decoupled from the bus-specific configuration. This can be a simple implementation of IValuesContainer that allows adding arbitrary IValue<T> properties at runtime, and can be injected into Logics for their internal state management.
- [ ] Refactor the OpenHab connectivity provider to make use of ConnectivityProviderBase.
- [ ] Refactor the KNX connectivity provider to support multiple KNX connections in parallel, each with its own configuration and set of group addresses. This involves changing the internal value mapping to consider the connection/bus context, and updating the configuration and initialization logic to handle multiple connections. This allows for more complex setups with multiple KNX systems or segments.

---

## Priority 3 — Improvements, Refactoring, Cleanup, Enhancements

### 3.1 Refactor `DptBase.Format` to include unit information

Currently, `DptBase.Format` returns only the raw value string (e.g. "22.5"). Refactor it to include unit information for numeric DPTs (e.g. "22.5 °C") by checking for `NumericInfo` and appending the unit if available.

### 3.2 Enhance IValue to support unit-aware formatting

Add an optional `Format` method to `IValue` that takes a `CultureInfo` and returns a formatted string including unit information if available. The default implementation can call the existing `ToString()` for backward compatibility.

Bus connectors and bus specific code generators can then implement this `Format` method to provide enriched display strings for values, improving the user experience in UIs and logs without requiring consumers to manually append units without having to implement a bus-specific IValue - the IValue framework must remain bus-agnostic and not require bus-specific interfaces or implementations.

Update the KNX code generator incl. the SRF.Network.Cli tool to implement this new `Format` method for KNX values, using the DPT's `NumericInfo` to include units in the formatted string.

### 3.5 Consistency in KNX configuration properties

There is ConnectionString in `KnxConnectionOptions` and in `SRF.Knx.Config.KnxConfiguration`.

Review for duplications of KNX related configuration classes used in `IOptions<>` and evaluate consolidation options to prevent confusion and code duplication.

Additionally, there are ConnectionString properties as well as more structured properties (e.g. MulticastAddress, Port) for the same KNX connection configuration. Review and consolidate to a single consistent approach supporting both. E.g. support parsing an optional ConnectionString while keeping the structured properties as the main configuration surface.

Make those improvements with focus on HomeCompanion.Server usage but pull the SRF.Network.Cli tool along to use the same approach.

### 3.6 Rethink ILogic testing strategy

It's foreseen that Logics inject `IValuesContainer` implementations by specific type, e.g. inject `KnxValues` directly rather than via an interface.
This allows easy acccess to the full set of values including context help, code completion, etc.
However, it makes testing more difficult as the logic tests must now use the concrete `KnxValues` class.

The following approach is foreseen:

Instantiate the `KnxValues` class in the test. Because the connection to the bus is done during initialization while otherwise the KnxValues class is bus agnostic, just yet another IValuesContainer implementation, the test could use the KnxValues class without any bus connection.

The test rig should even foresee fully event bus connected IValuesContainers to allow for more end-to-end testing of the logic, but the basic unit tests can be done with just the KnxValues class instantiated and used as a simple container for the values, without any bus connectivity.

Craete test framework utilities to facilitate ILogic testing for logics that interact via the event bus.
Have the test framework also provide all IValuesContainer implementations, but initialized without bus connectivity, so that logics can be tested with real values containers but without needing a bus connection.

---

## Priority 4 — Cleanup

### 3.1 Extract shared `LambdaHandler<T>` test utility

`LambdaHandler<T>` is defined identically in both `EventBusTests.cs` and `KnxConnectivityProviderTests.cs`. Extract to a shared `TestHelpers.cs` in `HomeCompanion.Tests`.

### 3.2 Resolve `IValuesManager` stub

`IValuesManager` (in `Abstractions/Values/`) is an empty interface, never implemented or registered. Either define its contract and implement it, or remove it to avoid dead API surface.

---

## Priority 5 — ADR Open Issues (future iterations)

### 4.1 Per-value initialization timeout (ADR-0002 open issue #1)

If a KNX device is offline, its group address never responds and `SendInitialReadRequestsAsync` blocks all of `IsInitializationFinished` for `InitializationReadTimeout` (30 s). Implement a per-value timeout: mark unresponded values as `ValueStatus.Error` early so the rest of the system can proceed.

### 4.2 Keyed-services migration in `SRF.Network.Knx` (ADR-0002 open issue #2)

`AddKnxIpRouting` now registers `IKnxBus` and `IKnxConnection` as **keyed** singletons (keyed by connection name) plus a non-keyed forwarding registration for `IEnumerable<IKnxConnection>`. Any code that previously injected `IKnxBus` or `IKnxConnection` directly (non-keyed, single instance) must be reviewed and updated.
