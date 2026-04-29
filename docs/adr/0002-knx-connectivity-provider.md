# ADR-0002: KNX Connectivity Provider Design

**Date:** 2026-04-29  

---

## Context

HomeCompanion needs to bridge the KNX bus to its internal event system and provide a typed, property-based way for logic modules to read from and write to KNX group addresses. The existing `IConnectivityProvider` interface defines the lifecycle contract; `SRF.Network.Knx` provides the low-level KNX/IP Routing transport. The design must support:

- Multiple KNX/IP connections (e.g. redundant interfaces or independent bus segments)
- Logic modules declaring KNX-backed values as plain C# properties (no XML config, no string lookups at runtime)
- Inbound KNX telegrams propagating to the HC event bus for any subscriber
- Outbound writes from logic modules flowing to the KNX bus without requiring a direct dependency on SRF.Network.Knx
- Offline unit testing (no real KNX bus)

---

## Decision

### 1. New `HomeCompanion.Knx` library project

A new project was introduced between `HomeCompanion.Base` and `HomeCompanion.Core` in the dependency graph. It contains:

- **`IKnxValue` / `IKnxValue<T>`** — typed value interfaces that extend `IValue<T>` with `GroupAddress`, `Initialize(IEventPublisher)`, `UpdateFromBus(object?)`, and `Write(T)`
- **`KnxValue<T>`** — concrete implementation; the group address is passed in the constructor (e.g. `new KnxValue<bool>("1/0/0")`)
- **KNX-specific HC events** — `KnxGroupWriteReceived`, `KnxGroupReadReceived`, `KnxGroupResponseReceived` (inbound), and `KnxGroupWriteRequested<T>` (outbound)

This keeps the KNX-specific vocabulary out of the generic abstractions while making it available to any project referencing `HomeCompanion.Knx` (including `HomeCompanion.Logics`).

### 2. `KnxValue<T>` as the GA binding mechanism (no attribute)

**Considered alternatives:**

| Approach | Notes |
|---|---|
| `[KnxGroupAddress("1/0/0")]` on `IValue<T>` property | Requires runtime attribute reflection; GA is detached from the value object |
| `KnxValue<T>` with GA in constructor | GA is co-located with the value; type itself is the discovery marker; no attribute infrastructure needed |
| String-keyed dictionary in config | Runtime-only binding; no compile-time safety |

**Decision:** Use `KnxValue<T>` with the group address in its constructor. The type doubles as the binding marker — the provider discovers `IKnxValue` properties via reflection on `IValuesContainer` instances at startup.

### 3. Outbound flow: HC event bus (not direct C# events on `KnxValue<T>`)

When a logic calls `knxValue.Write(value)`, `KnxValue<T>` publishes a `KnxGroupWriteRequested<T>` event on the HC event bus. The `KnxConnectivityProvider` subscribes to the base type `KnxGroupWriteRequested` (the EventBus walks the type hierarchy) and forwards it as a `GroupValueWrite` telegram.

**Why the HC event bus rather than C# events on `KnxValue<T>`:**
- Logics already depend on `IEventPublisher`; no new coupling introduced
- Any other subscriber (logging, auditing, another logic) can observe writes without hooking into the value object
- Consistent with the rest of the system's event-driven architecture
- Testable: swap the bus for a mock in unit tests

**Two-phase init:** `KnxValue<T>` holds a nullable `IEventPublisher` that is injected by `KnxConnectivityProvider.StartAsync` calling `Initialize(publisher)` on each discovered value. Writing before init throws `InvalidOperationException`.

### 4. Stored value is NOT updated on `Write()` — bus echo model

`Write()` sends to the bus but does not update the local `Value` property. The value is updated when the bus echoes the telegram back as a `GroupValueWrite` (or `GroupValueResponse`). This matches KNX semantics: the bus is the source of truth.

### 5. `ValueChanged<T>` in `HomeCompanion.Base`

A bus-agnostic `ValueChanged<T>` event (carrying `OldValue`, `NewValue`, `Source`) was added to `HomeCompanion.Base.Events`. It is published by `KnxValue<T>.UpdateFromBus` when the stored value actually changes (or on first initialization). Placing it in Base means any connectivity provider or logic can produce or consume it without depending on `HomeCompanion.Knx`.

### 6. `Write(T)` added to `IValue<T>`

`IValue<T>` gained `void Write(T value)` to allow generic write support across value types. `ValueBase<T>` provides a virtual no-op default so existing subclasses are not broken.

**Known concern:** This adds write semantics to a read-oriented interface. An `IWritableValue<T> : IValue<T>` segregation would be cleaner. Left as a future refactor to avoid over-engineering at this stage.

### 7. Multi-connection via keyed DI singletons

`SRF.Network.Knx.ExtensionsHosting.AddKnxIpRouting(name)` was updated to register `IKnxBus` and `IKnxConnection` as **keyed singletons** (keyed by connection name). Each call also registers a non-keyed `IKnxConnection` forwarding, so `IEnumerable<IKnxConnection>` accumulates all named connections and is injected into `KnxConnectivityProvider`.

Connection names are configured under `Knx:Connections` (string array). If absent, a single connection named `"default"` is used.

**Broadcast outbound:** all registered connections receive every outbound `GroupValueWrite`. No GA-to-connection routing in v1.

### 8. `IValuesContainer` stays a marker interface

The provider discovers values via reflection over `IValuesContainer` instances. No base class or registration callback is required from container implementations.

---

## Consequences

- Logic modules declare KNX-backed values as `KnxValue<T>` properties on an `IValuesContainer` implementation — no bus-level plumbing needed in the logic itself
- Adding a new KNX value requires only declaring a property; no config file changes
- The KNX provider handles all reflection, initialization, and routing transparently
- Multi-connection support works out of the box via `Knx:Connections` config
- Unit tests are fully offline: stub `IKnxConnection` + `IDptResolver` suffice
- `IValue<T>.Write()` is a breaking change for any `IValue<T>` implementors outside this codebase

---

## Open Issues

1. **`IWritableValue<T>` segregation** — consider splitting `IValue<T>` into read and write interfaces to avoid forcing write semantics on all value types.
2. **`IsInitializationFinished` under partial availability** — if a device is offline, read requests go unanswered and initialization blocks until `InitializationReadTimeout` (30 s). A per-value timeout with `ValueStatus.Error` marking is recommended for a future iteration.
3. **SRF.Network.Knx keyed-services change** — the change to `ExtensionsHosting` is a breaking change within the `SRF.Network.sln` sub-solution. Any code that was registering `IKnxBus` or `IKnxConnection` as non-keyed singletons and injecting them directly must be updated.
