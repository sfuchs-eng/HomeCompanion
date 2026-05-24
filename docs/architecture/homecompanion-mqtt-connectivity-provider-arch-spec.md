# Architecture Specification: MQTT Connectivity Provider for HomeCompanion

**Date:** 2026-05-23
**Revised:** 2026-05-24
**Status:** Draft
**Owner:** HomeCompanion.Integrations.Mqtt

## 1. Purpose

Define the target architecture for an MQTT connectivity integration that maps MQTT topics to `IValue` objects and bridges inbound/outbound MQTT traffic to the HomeCompanion event/value system.

The implementation is registered by `MqttExtensionRegistrar` and is the core of the `Integrations.Mqtt` extension.

## 2. Scope

In scope:
- MQTT broker connectivity through `SRF.Network.Mqtt`
- Mapping MQTT topics to `IValue` instances discovered from `IValuesContainer`
- Support for primitive value types (`bool`, numeric types, `string`, enums)
- Support for complex POCO payloads and polymorphic payloads
- Multi-broker configuration
- Configurable high-level topic subscriptions (wildcards) with wildcard-capable value routing
- Outbound writes from `ILogic` via `IValue.Write(...)` to MQTT topics

Out of scope:
- Implementing a new MQTT client stack (must reuse `SRF.Network.Mqtt`)
- Replacing HomeCompanion core event/value lifecycle responsibilities (`ValuesManager` remains central)
- Generic dynamic topic-to-value auto-discovery without explicit value mappings

## 3. Architectural Context

- `IValue` objects are bus-agnostic and are initialized/routed centrally by `ValuesManager`.
- Connectivity providers bridge external systems and HomeCompanion events.
- `IValuesContainer` exposes value instances used by logic modules.
- KNX already uses the pattern "bus endpoint mapping attached to each value" (`KnxBusEndpointMapping` in `IValue.BusMappings`).

The MQTT design follows the same value-centric pattern used by KNX, adapted to MQTT topic semantics.

## 4. Decision Summary

1. `Integrations.Mqtt` will implement an MQTT bus endpoint mapping type (`MqttBusEndpointMapping`) and one connectivity provider per configured broker (`MqttConnectivityProvider`).
2. Transport/connectivity is delegated to `SRF.Network.Mqtt` (`IMqttBrokerConnection`, `MqttOptions`, `Subscription`, publish queue).
3. Topic-to-value mapping is explicit on value level and supports both exact topics and MQTT wildcard patterns (`+`, `#`) for inbound matching.
4. Broker-level subscriptions are configured as high-level wildcard topic patterns and act as ingress filters only.
5. Payload conversion uses `System.Text.Json` for complex types; primitives/enums use fast typed conversion with invariant culture.
6. Polymorphic payloads are supported via configured and/or attributed discriminators, with safe allow-listing of derived types.
7. Keyed/named multi-broker registration is implemented in `SRF.Network.Mqtt.Hosting` and consumed by `Integrations.Mqtt`.
8. Inbound command-topic traffic emits `ValueWriteReceived` only; state-topic traffic emits `ValueUpdateReceived`.
9. Wildcard routing is single-winner only with deterministic precedence (no fan-out/broadcast mode).

## 5. High-Level Design

## 5.1 Main Components

- `MqttExtensionRegistrar`
: Binds options, registers broker connections using `SRF.Network.Mqtt`, and registers one `MqttConnectivityProvider` per broker.

- `MqttConnectivityProvider` (one instance per broker)
: Implements `IConnectivityProvider` and bridges HomeCompanion events with a single `IMqttBrokerConnection`. Handles topic routing, payload conversion, and value update/write bridging. Manages broker-level subscriptions and maintains an in-memory topic router built from value mappings.

- `MqttBusEndpointMapping`
: Value-level mapping attached to `IValue.BusMappings`; defines broker name, topic or topic pattern(s), communication flags, and payload config.

- `MqttPayloadConverter`
: Converts between MQTT payload and `IValue.ValueType` for inbound/outbound operations.

- `MqttTopicRouter`
: In-memory matcher for exact topics and wildcard topic filters, built at startup from discovered values.

## 5.2 Why One Provider Per Broker

- Aligns with existing `MqttExtensionRegistrar` intent (keyed singleton per broker).
- Isolates lifecycle, logs, and connection state per broker.
- Simplifies reconnection and fault isolation.
- Avoids coupling between unrelated broker tenants.

## 6. Integration with SRF.Network.Mqtt

The implementation must use:
- `IMqttBrokerConnection.Subscribe(...)` for inbound topics
- `IMqttBrokerConnection.Publish(...)` / `PublishJson(...)` for outbound traffic
- `MqttOptions` for broker connection settings
- `SRF.Network.Mqtt.Hosting.MqttHostingExtensions.AddMqtt(...)` as base registration mechanism

### 6.1 Multi-Broker Registration Requirement

`SRF.Network.Mqtt` currently exposes non-keyed `AddMqtt(configSection)` registrations. This architecture decides to extend registration in `SRF.Network.Mqtt.Hosting` with keyed/named broker support while still using `MqttBrokerConnection` and `MqttOptions`.

`Integrations.Mqtt` consumes these keyed services and remains focused on value mapping, routing, and event translation.

## 7. Value Mapping Model

## 7.1 Mapping Rules

- Mapping is defined on each `IValue` through `IValue.BusMappings`.
- Bus id incorporates protocol and broker: `MqttBusEndpointMapping.BusId = "mqtt://<BrokerName>"`.
- Each mapping targets one broker and defines one or more MQTT topic filters.
- Value mapping topic filters may use wildcards (`+`, `#`) for inbound matching.
- Outbound publish topic must resolve to a concrete topic string (no wildcard) at send time.
- Broker subscriptions may use wildcards and are configured separately.
- If multiple mappings match the same inbound topic, deterministic precedence applies (see Section 8.2).

## 7.2 Mapping Shape (Conceptual)

`MqttBusEndpointMapping` (conceptual fields):
- `BrokerName` (string): configured broker key
- `StateTopicFilter` (string): exact topic or wildcard filter for inbound state updates
- `CommandTopic` (string?): optional command topic for outbound writes and optional inbound command handling semantics
- `Communication` (`BusCommunication`): Receive/Transmit/Initialize flags
- `Config` (`MqttBusMappingConfiguration`): payload format and MQTT publish metadata

`MqttBusMappingConfiguration` (conceptual fields):
- `PayloadFormat`: `RawUtf8 | Json | JsonScalar`
- `JsonPath` (optional): select sub-property from JSON payload
- `TypeDiscriminatorProperty` (optional): e.g. `$type`
- `DerivedTypes` (optional): allow-list for polymorphic deserialization
- `TopicParameters` (optional): extraction of wildcard segments (e.g. `site`, `room`, `device`) for mapping context
- `OutboundTopicTemplate` (optional): concrete publish topic built from value context and configured placeholders
- `Qos` / `Retain` / `ContentType` (optional outbound defaults)
- `IgnoreOwnPublishes` (optional, default true): loop prevention strategy

## 7.3 Example Value Declaration Pattern

```csharp
public ValueBase<float> RoomTemperature { get; } = new(loggerFactory.CreateLogger<ValueBase<float>>())
{
    Name = "RoomTemperature",
    BusMappings =
    {
        [MqttBusEndpointMapping.BusId] = new MqttBusEndpointMapping(
            brokerName: "main",
          stateTopicFilter: "home/+/temperature/state",
            commandTopic: "home/living/temperature/set")
        {
            Communication = BusCommunication.Receive | BusCommunication.Transmit,
            Config = new MqttBusMappingConfiguration
            {
                PayloadFormat = MqttPayloadFormat.JsonScalar,
                Qos = 1,
                Retain = false
            }
        }
    }
};
```

## 8. Topic Subscription and Routing

## 8.1 Broker-Level Subscriptions

Each broker defines one or more high-level topic patterns, for example:
- `home/+/+/state`
- `home/events/#`

The provider subscribes to these patterns once connected.

## 8.2 Topic Routing and Match Precedence

For every inbound message:
1. Message arrives because it matched a high-level subscription.
2. Provider evaluates all value mappings for the broker scope using MQTT topic filter matching and resolves the best match.
3. Matching precedence is deterministic:
  - Exact topic match first.
  - Then wildcard matches ordered by higher specificity (more fixed segments, fewer wildcards).
  - If still tied, explicit `Priority` in mapping config (higher wins).
  - If still tied, startup registration order (stable fallback).
4. Routing is single-winner only: exactly one mapping is selected, no fan-out to multiple values.
5. If the selected mapping allows `Receive`, payload is decoded and emitted by semantics:
  - Matches `StateTopicFilter` => `ValueUpdateReceived`.
  - Matches inbound command topic semantics => `ValueWriteReceived` only.
6. If no mapping exists, message is ignored (trace log only).

This keeps broker load manageable while allowing controlled wildcard-based value binding.

## 9. Data Flow

## 9.1 Inbound (MQTT -> HomeCompanion)

1. `IMqttBrokerConnection` receives message.
2. `MqttConnectivityProvider` callback executes.
3. Provider resolves `(broker, topic)` mapping.
4. Payload conversion based on target `IValue.ValueType` and mapping config.
5. Provider publishes:
  - `ValueUpdateReceived` for state-topic matches
  - `ValueWriteReceived` for command-topic matches (if configured)
  - Command-topic matches do not additionally emit `ValueUpdateReceived`
6. `ValuesManager` routes to target `IValue`.

## 9.2 Outbound (HomeCompanion -> MQTT)

1. `ILogic` calls `IValue<T>.Write(...)`.
2. Value emits `ValueWriteRequest` on event bus.
3. `MqttConnectivityProvider` handles `ValueWriteRequest`.
4. Provider resolves mapping for source `IValue`.
5. If mapping allows `Transmit`, value is serialized.
6. Message published through `IMqttBrokerConnection.Publish(...)` / `PublishJson(...)`.

## 10. Type Conversion Strategy

## 10.1 Primitive and Enum Types

Inbound conversion order:
1. `string` passthrough (`PayloadUtf8`)
2. Boolean literals (`true/false`, '>0/0', transparent "ON/OFF", "CLOSED/OPEN", "ENABLE/DISABLE", optional other value pairs via config)
3. Numeric types with invariant culture (`byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `decimal`)
4. Enum by name (case-insensitive), fallback numeric value

Outbound conversion:
- `string`: publish UTF-8 string
- primitives: invariant string representation unless `JsonScalar`
- enum: configured name (preferred default) or numeric representation

## 10.2 POCO Types

- Use `System.Text.Json` serialization/deserialization.
- Respect nullability and required properties.
- Default behavior is lenient: unknown JSON fields are ignored.
- Strict mode is opt-in per mapping/config when a hard schema contract is required.

## 10.3 Polymorphic Types

Support both approaches:
1. Attribute-based (`JsonDerivedType`, `JsonPolymorphic`) on domain models.
2. Mapping-config based discriminator setup (for models that cannot be annotated).

Safety requirements:
- No unrestricted type name activation.
- Derived types must be explicitly allow-listed.
- Conversion failures do not crash provider loop; they are logged (warning level) and dropped.

## 11. Configuration Contract (Draft)

```json
{
  "Mqtt": {
    "Brokers": {
      "main": {
        "Connection": {
          "Host": "mqtt-main.local",
          "ClientID": "homecompanion-main",
          "UseTls": true,
          "User": "hc",
          "Pass": "***"
        },
        "Subscriptions": [
          "home/+/+/state",
          "home/events/#"
        ]
      },
      "lab": {
        "Connection": {
          "Host": "mqtt-lab.local",
          "ClientID": "homecompanion-lab",
          "UseTls": false
        },
        "Subscriptions": [
          "lab/+/state"
        ]
      }
    }
  }
}
```

Notes:
- `Connection` maps to `SRF.Network.Mqtt.MqttOptions`.
- Value-to-topic mapping is not in this section; it is declared on `IValue` objects (same pattern as KNX mapping).
- Subscription patterns are broad selectors, not direct value maps.

## 12. Lifecycle and Readiness

For each `MqttConnectivityProvider` instance:
- `IsEnabled`: true if broker config exists and not disabled
- `IsConnected`: reflects underlying `IMqttBrokerConnection.IsConnected`
- `IsInitializationFinished`: true when value map built and initial subscriptions registered

Startup sequence:
1. Wait for `InitValuesRegistered` gate.
2. Discover mapped values from all `IValuesContainer` instances.
3. Build topic router for current broker.
4. Register subscriptions.
5. Mark initialization finished.

## 13. Error Handling and Reliability

- Message handler exceptions are caught and logged per message.
- Deserialization/conversion errors are non-fatal; message is dropped.
- Publish failures are logged; optional retry policy can use SRF.Network.Mqtt queue semantics.
- Connection loss relies on SRF.Network.Mqtt reconnect behavior.
- Duplicate or retained messages are tolerated; value layer handles idempotent updates by change detection.

## 14. Observability

Required logs and metrics (per broker):
- connection up/down transitions
- subscription registration results
- inbound message count
- mapped vs unmapped topic count
- conversion failures by type/topic
- publish attempts and failures

## 15. Testing Strategy

- Unit tests for topic routing (exact match, unmapped topic, broker scoping)
- Unit tests for wildcard routing precedence and tie-break behavior
- Unit tests proving single-winner routing (no fan-out/broadcast)
- Unit tests for inbound command semantics (`ValueWriteReceived` only)
- Unit tests for type conversion matrix (primitive, enum, POCO, polymorphic)
- Unit tests for outbound publish formatting and topic selection
- Integration tests with fake/stub `IMqttBrokerConnection` (offline)
- No tests require live MQTT broker by default

## 16. Security and Safety

- Credentials loaded from secure configuration providers.
- TLS on broker connections where available.
- No sensitive payload logging by default at info level.
- Polymorphic conversion uses allow-list only.

## 17. Resolved Implementation Decisions

1. Keyed/named broker registration is implemented in `SRF.Network.Mqtt.Hosting`; `Integrations.Mqtt` consumes keyed `IMqttBrokerConnection` services.
2. `MqttBusEndpointMapping` uses split topic semantics: `StateTopicFilter` for inbound state and optional `CommandTopic` for command semantics.
3. Inbound command-topic traffic emits `ValueWriteReceived` only.
4. POCO JSON deserialization is lenient by default, with opt-in strict mode per mapping/config.
5. Wildcard routing is single-winner only with deterministic precedence; fan-out/broadcast mode is not supported.

## 18. Acceptance Criteria

- Multiple brokers can be configured and run in parallel.
- High-level topic pattern subscriptions are configurable per broker.
- Value mappings support exact topics and wildcard topic filters with deterministic single-winner precedence.
- Inbound command-topic matches emit `ValueWriteReceived` only (not both write and update events).
- POCO deserialization is lenient by default, with opt-in strict mode.
- `ILogic` can read and write MQTT-backed values solely via `IValue` objects.
- Primitive, enum, POCO, and polymorphic payloads are supported with deterministic conversion behavior.
- Transport implementation uses `SRF.Network.Mqtt` rather than a custom MQTT stack.
