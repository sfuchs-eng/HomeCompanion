# Shutter control logic

## Overall principles of controlling shutters this way

- Each room has a room shutter scene (KNX DPT scene control) that defines the room-level policy for how shutters should be controlled.
- Each shutter is controlled individually, considering the room shutter scene and other inputs (for example sun position, room temperature, building state, and thermal-control mode). 
- The model layer contains the global, building, floor, room, and facade-level configuration for shutters, including the room shutter scene and the shutter type.
- Different shutter types vary by capabilities and actuator kind. `ShutterType` defines the shutter type and its relevant capabilities.

## Configuration

- `CfgBuilding`, `CfgFloor`, `CfgRoom`, and `CfgShutter` define building-, floor-, room-, and shutter-level control behavior.
- `CfgShadowingSpecial` defines building-level shadowing and temperature-handling behavior.
- The `Model` is built from the configuration classes and is used by the shutter automation to determine control behavior.
- `HomeCompanion.Core.Model.ModelValueBinder` binds IValue references in the configuration to the actual values in the model.

## Current folder and namespace structure

The shutter logic is intentionally split to match the actual responsibility boundaries. The folder layout and C# namespaces are kept aligned so future work remains easy to navigate.

```text
HomeCompanion/Logics/Shutters/
├── AutoShadow/                  -> HomeCompanion.Logics.Shutters.AutoShadow
├── Model/                       -> HomeCompanion.Logics.Shutters.Model
├── RoomScene/                   -> HomeCompanion.Logics.Shutters.RoomScene
├── Runtimes/                    -> HomeCompanion.Logics.Shutters.Runtimes
├── ShutterAutomation/            -> HomeCompanion.Logics.Shutters.ShutterAutomation
├── README.md
└── TODO.ShutterAutomation.md
```

### Bucket responsibilities

- `Model`: configuration-driven model extensions, key types, and the model-layer logic that is not purely runtime state.
- `Runtimes`: runtime state holders, runtime context objects, runtime controllers, and events that materialize the running system into automatable state.
- `RoomScene`: room-scene evaluation and room-level scene decisions.
- `ShutterAutomation`: shutter target evaluation, trigger batching, coalescing, and shutter-level automation coordination.
- `AutoShadow`: environment and shadowing decision logic that feeds the automation computation.

### Contribution rule

When adding new shutter logic, keep the file path and namespace in sync:

- `RoomScene/...` files must live under `HomeCompanion.Logics.Shutters.RoomScene`.
- `Runtimes/...` files must live under `HomeCompanion.Logics.Shutters.Runtimes`.
- `Model/...` files must live under `HomeCompanion.Logics.Shutters.Model`.
- `ShutterAutomation/...` files must live under `HomeCompanion.Logics.Shutters.ShutterAutomation`.
- `AutoShadow/...` files must live under `HomeCompanion.Logics.Shutters.AutoShadow`.

Do not add new shutter logic to the flat root area unless it is explicitly a cross-cutting helper that is meant to be shared across all buckets and has no clear home.

## Architecture

The shutter control logic is implemented in the `ShutterController` ILogic class, which is responsible for controlling shutters based on the room shutter scene and additional inputs.

The room-shutter scene is governed by the `RoomShutterSceneLogic` ILogic approach, which determines the effective room scene from room configuration and other inputs.

The runtime classes are managed by `ShadowingRuntimesController`, which is responsible for the runtime state of shutters and related environment inputs.

The runtime classes react to system events and publish `ShutterAutomationComputationTriggerEvent` instances to trigger recomputation of shutter targets and room-scene decisions.

### Trigger flow and queue architecture

The shutter automation pipeline uses a layered design:

1. `BuildingRuntime`, `RoomRuntime`, and `ShutterRuntime` create `ShutterAutomationComputationTriggerContext` instances when a relevant input changes.
2. Those contexts are handed to a generic `IQueueFeeder<ShutterAutomationComputationTriggerContext>`.
3. The concrete implementation is an event-bus adapter that converts the context into a `ShutterAutomationComputationTriggerEvent` and publishes it via `IEventPublisher`.
4. `ShutterController` subscribes to `ShutterAutomationComputationTriggerEvent` and feeds those events into the trigger collector.
5. The collector batches and prioritizes pending triggers based on urgency and age, grouping them by scope (global, room-specific, shutter-specific).
6. Only the matured batch is forwarded to the state-computation loop for the actual decision logic.

This means the event bus is not the batching mechanism. It is the distribution mechanism for trigger intents. The pacing and coalescing policy remains in `ShutterController` through queueing and urgency windows.

Key rule: the queue feeder is intentionally generic and does not encode a specific target key. The target selection happens in the trigger `Context`, which carries the affected thing keys. Quartz jobs such as `ShutterResetExternalOverrideJob` use the same queue abstraction to enqueue recomputation without a dedicated queue per job.

This is why the service is registered as a singleton for `IQueueFeeder<ShutterAutomationComputationTriggerContext>`. All trigger producers publish to the same logical queue, and the event subscribers decide which scope(s) and runtime(s) consume the event.

### Scope dispatch contract

- The trigger context remains generic and may carry mixed thing keys and mixed scope flags.
- `RoomShutterSceneLogic` processes only `RoomSpecific` scope and only `RoomKey` entries.
- `ShutterController` processes shutter targets from:
	- explicit `ShutterKey` entries when `ShutterSpecific` is present,
	- shutters of referenced rooms when `RoomSpecific` is present,
	- all shutters when `Global` is present.
- Mixed `RoomSpecific | ShutterSpecific` triggers are valid and intentionally fan out to both consumers.
- Global triggers do not imply room-scene recomputation by default; room-scene recomputation stays conditional by trigger origin and attached room keys.
