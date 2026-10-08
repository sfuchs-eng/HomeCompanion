# HomeCompanion TODO

Scratchpad, don't take this all for granted.

## Present work focus

### Priority 1

ramp-up life use with what's in now, substitute legacy solution piece by piece.

### Basic features

- [x] InfluxDB connectivity
- [x] MQTT connectivity
- [x] MQTT to IValue mapping (bus mapping, routing), payload to/from `IValue<T>` transcoding, MQTT specific IValueContainer: refine `MqttPayloadConverter` to support UnitsNet.Quantity and related types. Done straight in ValueBase. Bus mapping specific encoding/decoding to be considered later, incl. sharing between MQTT and OpenHAB integrations.
- [x] e-Mail notifications
- [x] generic alerting/notification framework
- [x] port from legacy: Spheric vector
- [x] port from legacy: piece wise linear curves classes

### Logics

#### Migration from Legacy into public HomeCompanion solution

- [x] Redo: Shutter automation logic
- [x] Redo: Thermal control logic
- [x] Redo: Motorized window logic
- [x] Sun position calculation (simple)
- [x] Swiss Meteo weather forecast
- [ ] Redo: SunShade
- [ ] Rain sensor integration (ESP32 based tick counter, connected via MQTT topic on which cumulative rain quantities in mm = l/m2 are published)
- [ ] Soil humidity model
- [ ] Water distribution valves control logic (for irrigation of garden and lawn areas)
  - [ ] including "GardenWaterEvents" namespace, essentially being a dedicated event bus for irrigation and water distribution events, to decouple the logic from the bus-specific implementation (KNX, MQTT, OpenHAB, ...).
- [ ] Irrigation scheduling logic (for garden and lawn areas; bases on soil humidity model, rain sensor, weather forecast, and irrigation valve control logic)

Low prio:

- [ ] SmartMon integration

#### Local, non-public logics to be migrated / improved

These logics contain hard-coded building/location specific details.
They won't be made public as they are not generic enough to be useful for other users.
Leaves this here as my personal TODO list for the time being in order to keep the overview and segregation of public vs. private logics.

- [x] Absence status tracking
- [x] Wesco cooker hood (2016 type with extremely basic KNX interface) control logic
- [x] Automatic ventilation control logic for roof/other motorized windows
- [x] Basic heat pump control (heat/cool/off type) logic
- [ ] Floor heating valves control logic
- [ ] Thermal mode control automation based on building temperatures, outdoor temps and weather forecast, to optimize energy consumption and comfort.
- [ ] Xmas lighting automation
- [ ] Estrich zu Treppen Licht (into OpenHAB?) automation
- [ ] Halloween specific door bell response automation
- [ ] LightingMisc/WelcomeLight: Garage & house entrance light automation
- [x] LightingMisc/NachtLichtElternbadToggle, LichtElternzimmerToggle --> ToggleBtnElternBett
- [ ] LightingMisc/LichtGarageKeepOn
- [ ] more in LightingMisc namespace
- [ ] Lueftung
- [ ] MZH Rolladen & Lighting automation
- [ ] Pump monitor / alerting logic
- [x] Cleaning mode
- [x] Surveillance PIR reset job
- [ ]

#### New (ideas...)

- [ ] Hail alert (from )

### Varia

- [ ] Bus mapping based encoders/decoders for MQTT and OpenHAB payloads, overriding present default schemes and improving parsing efficiency and flexibility.
- [ ] Raise `valueBase.AddException(new ValueException(message, new FormatException(message)));` AddException method to the `IValue` interface. Simplify the usage in `OpenHabConnectivityProvider`
- [ ] Make `Model:Buildings:Main:Specials:Shadowing:SpecialScenes` effective by adding a bound property on `CfgShadowingSpecial` and wiring runtime handling around the existing scene config types in `HomeCompanion/Base/Model/ShadowingSpecial.cs` (`CfgShadowingSceneController`, `CfgShadowingSceneCommand`).
- [ ] Have an IValuesContainer for dynamic, internal values. This allows Logics to create/manage their own values without needing to define them in the ETS export or OpenHab item list, which is more flexible and decoupled from the bus-specific configuration. This can be a simple implementation of IValuesContainer that allows adding arbitrary `IValue<T>` properties at runtime, and can be injected into Logics for their internal state management.
- [ ] Refactor the KNX connectivity provider to support multiple KNX connections in parallel, each with its own configuration and set of group addresses. This involves changing the internal value mapping to consider the connection/bus context, and updating the configuration and initialization logic to handle multiple connections. This allows for more complex setups with multiple KNX systems or segments.
