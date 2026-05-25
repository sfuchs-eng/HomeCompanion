# HomeCompanion TODO

---

Completed items are tracked in `TODO_completed.md`.

## Present work focus

### Port existing functionality from the old HomeCompanion solution into the one at hand

- [x] InfluxDB connectivity
- [x] MQTT connectivity
- [ ] e-Mail notifications
- [ ] generic alerting/notification framework

### Furthermore

- [ ] Resolve initialization bugs related to value conversions (e.g. OpenHAB sends string `""` while the target is `IValue<bool>` which cannot be converted, causing exceptions).
- [ ] IValuesContainer with OpenHabItems that are not mapped to any KNX group address. Add code-gen to SRF.Network.Cli as for KNX, same command `kc -hc` all in one go.
- [ ] Have an IValuesContainer for dynamic, internal values. This allows Logics to create/manage their own values without needing to define them in the ETS export or OpenHab item list, which is more flexible and decoupled from the bus-specific configuration. This can be a simple implementation of IValuesContainer that allows adding arbitrary `IValue<T>` properties at runtime, and can be injected into Logics for their internal state management.
- [ ] Refactor the OpenHab connectivity provider to make use of ConnectivityProviderBase.
- [ ] Refactor the KNX connectivity provider to support multiple KNX connections in parallel, each with its own configuration and set of group addresses. This involves changing the internal value mapping to consider the connection/bus context, and updating the configuration and initialization logic to handle multiple connections. This allows for more complex setups with multiple KNX systems or segments.

---

## Priority 3 — Improvements, Refactoring, Cleanup, Enhancements

### 3.1 Consistency in KNX configuration properties

There is ConnectionString in `KnxConnectionOptions` and in `SRF.Knx.Config.KnxConfiguration`.

Review for duplications of KNX related configuration classes used in `IOptions<>` and evaluate consolidation options to prevent confusion and code duplication.

Additionally, there are ConnectionString properties as well as more structured properties (e.g. MulticastAddress, Port) for the same KNX connection configuration. Review and consolidate to a single consistent approach supporting both. E.g. support parsing an optional ConnectionString while keeping the structured properties as the main configuration surface.

Make those improvements with focus on HomeCompanion.Server usage but pull the SRF.Network.Cli tool along to use the same approach.

### 3.2 Rethink ILogic testing strategy

It's foreseen that Logics inject `IValuesContainer` implementations by specific type, e.g. inject `KnxValues` directly rather than via an interface.
This allows easy access to the full set of values including context help, code completion, etc.
However, it makes testing more difficult as the logic tests must now use the concrete `KnxValues` class.

The following approach is foreseen:

Instantiate the `KnxValues` class in the test. Because the connection to the bus is done during initialization while otherwise the KnxValues class is bus agnostic, just yet another IValuesContainer implementation, the test could use the KnxValues class without any bus connection.

The test rig should even foresee fully event bus connected IValuesContainers to allow for more end-to-end testing of the logic, but the basic unit tests can be done with just the KnxValues class instantiated and used as a simple container for the values, without any bus connectivity.

Create test framework utilities to facilitate ILogic testing for logics that interact via the event bus.
Have the test framework also provide all IValuesContainer implementations, but initialized without bus connectivity, so that logics can be tested with real values containers but without needing a bus connection.

---

## Priority 4 — Cleanup

### 4.1 Extract shared `LambdaHandler<T>` test utility

`LambdaHandler<T>` is defined identically in both `EventBusTests.cs` and `KnxConnectivityProviderTests.cs`. Extract to a shared `TestHelpers.cs` in `HomeCompanion.Tests`.

### 4.2 Harden `IValuesManager` startup synchronization and diagnostics

`IValuesManager` is implemented and DI-registered. Focus on startup/routing hardening:

- gate inbound connectivity-provider processing on lifecycle stage `InitValuesRegistered`
- keep lifecycle waits non-mutating (waiting must not signal)
- improve startup/runtime diagnostics for dropped/routed events and stage transitions

---

## Priority 5 — ADR Open Issues (future iterations)

### 5.1 Per-value initialization timeout (ADR-0002 open issue #1)

If a KNX device is offline, its group address never responds and `SendInitialReadRequestsAsync` blocks all of `IsInitializationFinished` for `InitializationReadTimeout` (30 s). Implement a per-value timeout: mark unresponded values as `ValueStatus.Error` early so the rest of the system can proceed.

### 5.2 Keyed-services migration in `SRF.Network.Knx` (ADR-0002 open issue #2)

`AddKnxIpRouting` now registers `IKnxBus` and `IKnxConnection` as **keyed** singletons (keyed by connection name) plus a non-keyed forwarding registration for `IEnumerable<IKnxConnection>`. Any code that previously injected `IKnxBus` or `IKnxConnection` directly (non-keyed, single instance) must be reviewed and updated.
