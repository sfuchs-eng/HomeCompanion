# HomeCompanion TODO

Scratchpad, don't take this all for granted.

## Present work focus

### Priority 1 — Shutter automation logic & other logics

... production testing & fixing

### Port existing functionality from the old HomeCompanion solution (not disclosed) into the one at hand

- [x] InfluxDB connectivity
- [x] MQTT connectivity
- [ ] MQTT to IValue mapping (bus mapping, routing), payload to/from `IValue<T>` transcoding, MQTT specific IValueContainer: refine `MqttPayloadConverter` to support UnitsNet.Quantity and related types.
- [x] e-Mail notifications
- [x] generic alerting/notification framework
- [x] port from legacy: Spheric vector
- [x] port from legacy: piece wise linear curves classes

### Furthermore

- [ ] Have an IValuesContainer for dynamic, internal values. This allows Logics to create/manage their own values without needing to define them in the ETS export or OpenHab item list, which is more flexible and decoupled from the bus-specific configuration. This can be a simple implementation of IValuesContainer that allows adding arbitrary `IValue<T>` properties at runtime, and can be injected into Logics for their internal state management.
- [ ] Refactor the KNX connectivity provider to support multiple KNX connections in parallel, each with its own configuration and set of group addresses. This involves changing the internal value mapping to consider the connection/bus context, and updating the configuration and initialization logic to handle multiple connections. This allows for more complex setups with multiple KNX systems or segments.
