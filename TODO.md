# HomeCompanion TODO

> **Status:** Build clean, 30/30 tests passing. Core framework (IValue, ValueBase, EventBus, KnxConnectivityProvider, LogicManager) is complete and tested. Gaps below block the first end-to-end KNX → IValue → Logic run.

---

## Present work focus

### ~~Extension registrations, particularly OpenHAB~~ ✓

OpenHabExtensionRegistration is instanciating many services, yet doing so on a temporary service collection that is not used for the actual application DI container. This means that none of the OpenHab related services are actually registered in the real DI container and thus not available at runtime. This needs to be fixed to have the OpenHab integration working.

ExtensionsRegistration constructors must not consume DI services except configuration ... but no bus services or values containers.
There needs to be a app start coordination mechanism "DI container built" event or similar to allow Extensions to initilize before other stages.
Use IHomeCompanionLifeCycleSynchronization for this purpose.

Introduce a scheme to resolve Extension dependencies on other Extensions (via constructor parameters or similar) to allow for correct initialization order of Extensions. E.g. OpenHabExtension might depend on KnxExtension to be initialized first, so it can consume the KNX values and bus services.

### Furthermore

- [ ] IValues initialization framework to be finished and tested
  - [x] Implement `KnxValues` source generator to emit properties from ETS export
  - [x] Implement `TestCounterLogic` and `ITestCounterValues` as a first logic using real values container properties, with unit tests
  - [x] Import IStateStore and get it working / tested with a simple file-based implementation in HomeCompanion.Server
  - [x] Implement saving values upon termination to json storage and loading them on startup
  - [x] Integrate SRF.Network.OpenHab
  - [x] Implement an IConnnectionProvider for OpenHab to manage connectivity and event reception
  - [x] Test OpenHab connectivity and item value as well as event reception
  - [x] Batch-load values from OpenHab, initializing those which map by name (or other logic? Bus maping? General permission via attribute on the IValuesContainer implementation?)
  - [x] Check whether the new OpenHab integration filters item write events correctly to only those items that are mapped to IValue properties, and that it raises events with the correct Target (mapped IValue) and Value (converted from OpenHab state string to the correct type using the OpenHabStateConverter).
- [x] Establish correct communication with the KNX bus, receiving telegrams and raising events accordingly (KNX → event bus)
- [ ] TestLogic, test switch: switch works, Int32 counter works, but KNX devices are not receiving or discarding the telegrams for the float duration (TestValueFloat). The byte count (TestCounter) is yet to be verified.
- [x] The log message "SRF.Network.OpenHab.Client.EventBusClient[0] Starting WatchDog..." is logged only upon application shutdown. Find & fix the root cause.
- [ ] Ensure KNX only sends read requests for values permitting so (read flag set in KNX DomainConfiguration oder similar)
- [ ] Get the first end-to-end KNX → IValue → Logic flow running with the test logic and real values container KnxValues generated.
- [ ] IValue.Initialize approach doesn't seem used. Initialization framework as implemented uses direct method calls to initialize the IValue.Value and ValueStatus. Review and clean-up, likely removing the initialization via bus events and just doing it directly from the connectivity provider calling IValue value initialization methods.

---

## Priority 1 — Blockers (system cannot run without these)

### ~~1.1 Register `IValuesContainer` implementations in DI~~ ✓

`AddValuesContainers()` added to `HostingExtensions.cs` — assembly-scans for concrete `IValuesContainer` types and registers each as its own type + as `IValuesContainer`. Called from `AddHomeCompanionCore()`.

### ~~1.2 Add KNX network config to `appsettings.json`~~ ✓

`Knx:Connections` is now a dictionary (`name → UdpMulticastOptions`) rather than a string array.
`AddKnxConnections` enumerates its children and passes each section path (`Knx:Connections:{name}`)
directly to `AddKnxIpRouting` as the `configSection` override, so no separate `Udp:Connections`
section is needed. `appsettings.json` now contains the minimal required entry:

```json
"Knx": {
  "Connections": {
    "default": {
      "MulticastAddress": "224.0.23.12",
      "Port": 3671
    }
  }
}
```

Add a `ConnectionManager` sub-key inside the connection object for reconnect tuning if needed.

### ~~1.3 Provide ETS group address export file~~ ✓

`KnxDptResolver` requires a `DomainConfiguration` loaded from the ETS XML export (`Knx:EtsGAExportFile`, default `GroupAddressExport.xml`). Without it, every inbound and outbound telegram triggers `KnxException: Group address X not found in ETS export`, silently dropping all values.

Use the `SRF.Knx.Config` library functionality to access ETS export files and other KNX master data and configuration information.
There's a user config file "~/.config/HomeCompanion.json" containing the required configuration to locate the existing ETS export file and other, generated KNX configuration files.
This should all be already available via DI. The only missing piece is to inject the `DomainConfiguration` into `KnxDptResolver` and use its properties which reflect the ETS export content.

### ~~1.4 Add `HomeCompanion.Knx` reference to `HomeCompanion.Logics`~~ ✓

`HomeCompanion.Logics.csproj` only references `HomeCompanion.Base`. Any values container declaring `KnxBusEndpointMapping` properties must reference `HomeCompanion.Knx`. Add the project reference.

### ~~1.5 Create a first `IValuesContainer` implementation~~ ✓

`KnxValues` is a `partial class` in `HomeCompanion.Knx` implementing `IValuesContainer`. Properties are
generated by the `HomeCompanion.Knx.CodeGen` Roslyn source generator from the ETS group address export XML.

- `[KnxValuesFromEtsExportAttribute]` in `HomeCompanion.Knx` carries the ETS export file path; applied on
  a git-ignored `KnxValues.local.cs` to keep developer-local paths out of version control.
- The generator resolves DPT main number → C# type via a static table and emits one `ValueBase<T>` property
  per group address with a `KnxBusEndpointMapping` initializer.
- Without the attribute file the build succeeds cleanly (CI-safe).

See `docs/adr/0003-knx-values-source-generator.md` for the full decision record.

### ~~1.6 Create a first `ILogic` implementation~~ ✓

`TestCounterLogic` and `ITestCounterValues` added to `HomeCompanion.Logics`.

- `ITestCounterValues` — interface with `TestSwitch` (`IValue<bool>`), `TestCount` (`IValue<int>`), `TestDuration` (`IValue<double>`). Implement on any values container class and register in DI to wire the logic to real bus-backed values.
- `TestCounterLogic` extends `LogicBase`, injects `ITestCounterValues` and `TimeProvider`.
  - Rising edge of `TestSwitch`: records start time via `TimeProvider`.
  - Falling edge: writes on-duration in seconds to `TestDuration`; increments `TestCount` by 1.
- Subscribes to `IValue<bool>.Changed` (C# event) in `InitializeAsyncLatched` — bus-agnostic.
- 5 unit tests added to `HomeCompanion.Tests` (`TestCounterLogicTests`). All 38 tests pass.

### ~~1.7 Fix `KnxValues` property names to match the ones in `DomainConfiguration`~~ ✓

Issue: the names in DomainConfiguration to not fully match the labels in the ETS export XML.
The generator currently uses the XML labels as property names in camelized form. Those must change to match the DomainConfiguration property names which can be correlated to the ETS export via the Group Address ID (e.g. `1/2/3`).

Present standard developer workflow:

- Export updated group address configuration from ETS to XML
- SRF.Network.Cli is used to generate the master data and config files from the ETS export
- Manual editing of the resulting config files is possible and allowed
- Resulting config files are committed to version control and used by HomeCompanion at runtime using SRF.Knx.Config

Needed extension / channge:

- Option A: SRF.Network.Cli not only generates the config files, but also a json file to be loaded by the source generator to correlate ETS export XML labels to DomainConfiguration property names. This would allow the generator to emit property names matching the DomainConfiguration, which is the actual source of truth for the runtime configuration.
- Option B: SRF.Network.Cli generates the `KnxValues` source file directly, bypassing the need for a source generator. This would be a more straightforward approach, but it would couple the code generation more tightly to the CLI tool and reduce flexibility.

Tendency is option A. We shall aim to keep the KnxValuesAttribute-driven source generator approach but feed it with a richer mapping file generated by the CLI tool, containing the information needed from the ETS export file as well as the DomainConfiguration contents to correlate the two.
The SRF.Network.Cli tool uses SRF.Knx.Connfig where the generation logic for the new config file HomeCompanionKnxAutoGen.json can be implemented. The serialization/deserialization classes underlying the new file might need to be in a separate shared library (e.g. HomeCompanion.Knx.Shared) to avoid a circular dependency between the CLI tool and the source generator.

---

## Priority 2 — Bugs / Correctness

### ~~2.1 `ValueBase.ValueType` returns wrapper type, not `T`~~ ✓

```csharp
// Current (in ValueBase, non-generic):
public Type ValueType => GetType();  // returns ValueBase<bool>, not typeof(bool)
```

Override in `ValueBase<T>` to return `typeof(T)`. Any diagnostic or UI code querying `IValue.ValueType` expects the value's data type, not the wrapper class.

### ~~2.2 Thread safety of `_pendingInitialReads`~~ ✓

`_pendingInitialReads` replaced with `ConcurrentDictionary<GroupAddress, bool>`. Uses `TryAdd`/`TryRemove`/`IsEmpty` for safe concurrent access between `SendInitialReadRequestsAsync` and the network-thread `OnMessageReceived`.

---

## Priority 3 — Improvements, Refactoring, Cleanup, Enhancements

### 3.1 Test for type matching `IValue<T>` vs DPT numeric property type

Add a test that verifies that for every `IValue<T>` with a numeric type `T` (e.g. `int`, `double`), the corresponding DPT property in the ETS export has a matching numeric PDT (e.g. `PDT_INT`, `PDT_FLOAT`) that can be resolved to the same .NET type by the DPT resolver. This ensures that the generated `IValue<T>` properties have compatible types with their DPT definitions, preventing runtime errors when formatting or parsing values.

### 3.1 Refactor `DptBase.Format` to include unit information

Currently, `DptBase.Format` returns only the raw value string (e.g. "22.5"). Refactor it to include unit information for numeric DPTs (e.g. "22.5 °C") by checking for `NumericInfo` and appending the unit if available.

### 3.2 Enhance IValue to support unit-aware formatting

Add an optional `Format` method to `IValue` that takes a `CultureInfo` and returns a formatted string including unit information if available. The default implementation can call the existing `ToString()` for backward compatibility.

Bus connectors and bus specific code generators can then implement this `Format` method to provide enriched display strings for values, improving the user experience in UIs and logs without requiring consumers to manually append units without having to implement a bus-specific IValue - the IValue framework must remain bus-agnostic and not require bus-specific interfaces or implementations.

Update the KNX code generator incl. the SRF.Network.Cli tool to implement this new `Format` method for KNX values, using the DPT's `NumericInfo` to include units in the formatted string.

### ~~3.4 Enhance ETS context information available in XML comments for generated properties~~ ✓

HomeCompanionAutoGenEntry currently includes the ETS export name (from DomainConfiguration) and group address, which are included in the generated `KnxValues` property XML comments.
Add the Label (to property's XML comment summary) and Description (to property's XML comment remarks) from the ETS export.

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
