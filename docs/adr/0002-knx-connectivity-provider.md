# ADR-0002: KNX Connectivity Provider Design

**Date:** 2026-04-29  
**Revised:** 2026-04-30

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

### 1. `HomeCompanion.Knx` library project

A library project sits between `HomeCompanion.Base` and `HomeCompanion.Core` in the dependency graph. It contains:

- **`KnxBusEndpointMapping`** — maps an `IValue` to a KNX group address; placed in `IValue.BusMappings` under the key `KnxBusEndpointMapping.BusId`
- **KNX-specific HC events** — `KnxGroupWriteReceived`, `KnxGroupReadReceived`, `KnxGroupResponseReceived` (inbound, bus-level detail)

This keeps the KNX-specific vocabulary out of the generic abstractions while making it available to any project referencing `HomeCompanion.Knx`.

### 2. `KnxBusEndpointMapping` as the GA binding mechanism

Logic modules declare plain `IValue<T>` (or `ValueBase<T>`) properties on an `IValuesContainer` and attach a `KnxBusEndpointMapping` to `IValue.BusMappings` at construction time:

```csharp
public ValueBase<bool> Light { get; } = new()
{
    BusMappings = { [KnxBusEndpointMapping.BusId] = new KnxBusEndpointMapping("1/0/0") }
};
```

**Considered alternatives:**

| Approach | Notes |
|---|---|
| `[KnxGroupAddress("1/0/0")]` on `IValue<T>` property | Requires runtime attribute reflection; GA is detached from the value object |
| `KnxValue<T>` subclass with GA in constructor | Couples value type to KNX; leaks bus technology into the value framework |
| `KnxBusEndpointMapping` in `BusMappings` | GA co-located with the value; no KNX-specific type required on the property; value stays bus-agnostic |

**Decision:** Use `KnxBusEndpointMapping` in `IValue.BusMappings`. The mapping is the discovery marker — the provider checks for it via reflection and `TryGetBusEndpoint<KnxBusEndpointMapping>` at startup.

### 3. Inbound flow: KNX → EventBus → IValue

On each inbound telegram, the provider publishes two layers of events:

1. **KNX-level** (`HomeCompanion.Knx.Events`): `KnxGroupWriteReceived`, `KnxGroupReadReceived`, `KnxGroupResponseReceived` — carry raw bus detail (group address, physical source address, raw payload, decoded value).
2. **Value-level** (`HomeCompanion.Base.Events`): `ValueWriteReceived`, `ValueReadReceived`, `ValueReadAnswerReceived` — carry a reference to the `IValue` target and the decoded value.

`ValueBase<T>` subscribes to `ValueWriteReceived` on the event bus (via `IValue.Initialize`) and updates its stored value when the event targets it. It then publishes `ValueChanged<T>` if the value actually changed.

`GroupValueResponse` is treated as both a read answer (`ValueReadAnswerReceived`) and a write (`ValueWriteReceived`) so that the stored value is updated in both cases.

### 4. Outbound flow: IValue.Write() → EventBus → KNX

`ValueBase<T>.Write(value)` updates the stored value immediately and publishes `ValueWritten<T>` on the HC event bus. The `KnxConnectivityProvider` subscribes to the base `ValueWritten` event; when the source value carries a `KnxBusEndpointMapping`, it encodes the value via `IDptResolver` and broadcasts a `GroupValueWrite` telegram to all connections.

**Why the HC event bus rather than a direct callback:**
- No coupling between `IValue` and `KnxConnectivityProvider`
- Any subscriber (logging, auditing, another logic) can observe writes
- Consistent with the rest of the system's event-driven architecture
- Testable: swap the event bus for a mock in unit tests

### 5. `IValue.Initialize(IEventPublisher, IEventSubscriber)`

Two-phase initialization: the connectivity provider calls `Initialize` on each discovered value at startup, injecting the event bus references the value needs to subscribe to `ValueWriteReceived` and publish `ValueChanged<T>`. Values cannot receive bus updates before `Initialize` is called.

### 6. `ValueChanged<T>` in `HomeCompanion.Base`

A bus-agnostic `ValueChanged<T>` event (carrying `OldValue`, `NewValue`, `Source`) is published by `ValueBase<T>` whenever the stored value changes. Placing it in Base means any connectivity provider or logic can produce or consume it without depending on `HomeCompanion.Knx`.

### 7. Multi-connection via keyed DI singletons

`SRF.Network.Knx.ExtensionsHosting.AddKnxIpRouting(name)` registers `IKnxBus` and `IKnxConnection` as **keyed singletons** (keyed by connection name). `IEnumerable<IKnxConnection>` accumulates all named connections and is injected into `KnxConnectivityProvider`.

Connection names are configured under `Knx:Connections` (string array). If absent, a single connection named `"default"` is used.

**Broadcast outbound:** all registered connections receive every outbound `GroupValueWrite`. No GA-to-connection routing in v1.

### 8. `IValuesContainer` stays a marker interface

The provider discovers values via reflection over `IValuesContainer` instances. No base class or registration callback is required from container implementations.

---

## Consequences

- Logic modules declare values as plain `ValueBase<T>` (or any `IValue<T>` impl) with a `KnxBusEndpointMapping` attached — no KNX-specific type required on the property
- The value framework (`IValue`, `ValueBase`) is fully bus-agnostic; KNX is entirely in the mapping and provider layer
- Adding a new KNX value requires only declaring a property and attaching a `KnxBusEndpointMapping`; no config file changes
- The KNX provider handles all reflection, initialization, and routing transparently
- Multi-connection support works out of the box via `Knx:Connections` config
- Unit tests are fully offline: stub `IKnxConnection` + `IDptResolver` suffice

---

## Open Issues

1. **`IsInitializationFinished` under partial availability** — if a device is offline, read requests go unanswered and initialization blocks until `InitializationReadTimeout` (30 s). A per-value timeout with `ValueStatus.Error` marking is recommended for a future iteration.
2. **SRF.Network.Knx keyed-services change** — the change to `ExtensionsHosting` is a breaking change within the `SRF.Network.sln` sub-solution. Any code that was registering `IKnxBus` or `IKnxConnection` as non-keyed singletons and injecting them directly must be updated.

