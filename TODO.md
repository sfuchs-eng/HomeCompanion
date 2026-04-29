# HomeCompanion TODO

> **Status:** Build clean, 30/30 tests passing. Core framework (IValue, ValueBase, EventBus, KnxConnectivityProvider, LogicManager) is complete and tested. Gaps below block the first end-to-end KNX → IValue → Logic run.

---

## Priority 1 — Blockers (system cannot run without these)

### 1.1 Register `IValuesContainer` implementations in DI

`KnxConnectivityProvider` receives `IEnumerable<IValuesContainer>` via constructor injection, but `AddHomeCompanionCore()` / `HostingExtensions` never registers any implementations. The provider will always discover **zero values** until this is fixed.

Add `AddValuesContainers()` to `HostingExtensions.cs` — assembly-scan for concrete `IValuesContainer` types (same pattern as `AddLogics()`) and register each as its own type + as `IValuesContainer`. Call it from `AddHomeCompanionCore()`.

### 1.2 Add KNX network config to `appsettings.json`

`AddKnxIpRouting("default")` binds the UDP transport from `Udp:Connections:default`. Without it, the socket uses library defaults and will not reach the bus. Minimal required section:

```json
"Udp": {
  "Connections": {
    "default": {
      "MulticastAddress": "224.0.23.12",
      "Port": 3671
    }
  }
}
```

Also consider adding `Knx:Connections: ["default"]` explicitly (currently falls back silently).

### 1.3 Provide ETS group address export file

`KnxDptResolver` requires a `DomainConfiguration` loaded from the ETS XML export (`Knx:EtsGAExportFile`, default `GroupAddressExport.xml`). Without it, every inbound and outbound telegram triggers `KnxException: Group address X not found in ETS export`, silently dropping all values.

- Export the group address list from ETS as `GroupAddressExport.xml` and place it in the working directory (or configure the path in `appsettings.json` under `Knx:EtsGAExportFile`).
- Alternatively, implement a fallback `IDptResolver` that infers DPT from the `IValue<T>`'s generic type argument — removes the hard dependency on the ETS file for simple use cases.

### 1.4 Add `HomeCompanion.Knx` reference to `HomeCompanion.Logics`

`HomeCompanion.Logics.csproj` only references `HomeCompanion.Base`. Any values container declaring `KnxBusEndpointMapping` properties must reference `HomeCompanion.Knx`. Add the project reference.

### 1.5 Create a first `IValuesContainer` implementation

`HomeCompanion.Logics` is empty. Add at least one concrete values container (e.g. `HomeValues`) with typed `ValueBase<T>` properties and `KnxBusEndpointMapping` entries. This is the first piece a logic module needs to interact with the KNX bus.

```csharp
public class HomeValues : IValuesContainer
{
    public ValueBase<bool> LivingRoomLight { get; } = new()
    {
        BusMappings = { [KnxBusEndpointMapping.BusId] = new KnxBusEndpointMapping("1/0/0") }
    };
}
```

### 1.6 Create a first `ILogic` implementation

`HomeCompanion.Logics` has no logic modules. Implement one minimal logic that:
- Extends `LogicBase`
- Injects a values container
- Subscribes to `ValueChanged<T>` or a container's `Changed` event in `InitializeAsyncLatched`
- Performs a simple reactive action (e.g. log or write another value)

This is the minimal proof that the full path KNX → `IValue.Changed` → logic reaction works end to end.

---

## Priority 2 — Bugs / Correctness

### 2.1 `ValueBase.ValueType` returns wrapper type, not `T`

```csharp
// Current (in ValueBase, non-generic):
public Type ValueType => GetType();  // returns ValueBase<bool>, not typeof(bool)
```

Override in `ValueBase<T>` to return `typeof(T)`. Any diagnostic or UI code querying `IValue.ValueType` expects the value's data type, not the wrapper class.

### 2.2 Thread safety of `_pendingInitialReads`

`KnxConnectivityProvider._pendingInitialReads` is a `HashSet<GroupAddress>` mutated from both the async initialization task (`SendInitialReadRequestsAsync`) and the network thread (`OnMessageReceived → _pendingInitialReads.Remove`). Replace with `ConcurrentDictionary<GroupAddress, bool>` or add a `lock`.

---

## Priority 3 — Cleanup

### 3.1 Extract shared `LambdaHandler<T>` test utility

`LambdaHandler<T>` is defined identically in both `EventBusTests.cs` and `KnxConnectivityProviderTests.cs`. Extract to a shared `TestHelpers.cs` in `HomeCompanion.Tests`.

### 3.2 Resolve `IValuesManager` stub

`IValuesManager` (in `Abstractions/Values/`) is an empty interface, never implemented or registered. Either define its contract and implement it, or remove it to avoid dead API surface.

---

## Priority 4 — ADR Open Issues (future iterations)

### 4.1 Per-value initialization timeout (ADR-0002 open issue #1)

If a KNX device is offline, its group address never responds and `SendInitialReadRequestsAsync` blocks all of `IsInitializationFinished` for `InitializationReadTimeout` (30 s). Implement a per-value timeout: mark unresponded values as `ValueStatus.Error` early so the rest of the system can proceed.

### 4.2 Keyed-services migration in `SRF.Network.Knx` (ADR-0002 open issue #2)

`AddKnxIpRouting` now registers `IKnxBus` and `IKnxConnection` as **keyed** singletons (keyed by connection name) plus a non-keyed forwarding registration for `IEnumerable<IKnxConnection>`. Any code that previously injected `IKnxBus` or `IKnxConnection` directly (non-keyed, single instance) must be reviewed and updated.
