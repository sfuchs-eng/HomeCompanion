# Shutter control logic

## Overall principles of controlling shuttters this way

- Each room has a room shutter scene (KNX DPT scene control) that is used to configure according what principles the shutters should be controlled.
- Each shutter is controlled individually, considering the room shuttter scene and other inputs (e.g. sun position, room scene, global inputs like "ThermalControl", etc.).
- `HomeCompanion.Model` contains global, building, floor, room and facade level configuration for shutters, including the room shutter scene and the shutter type.
- There are different shutter types, which differ by their capabilities and actuator types. `ShutterType` defines the shutter type and its capabilities.

## Configuration

- `CfgBuilding`, `CfgFloor`, `CfgRoom`, `CfgShutter` are building, floor, room and shutter level configuration for shutter control behavior.
- `CfgShadowingSpecial` is a building level configuration for shutter control behavior in context of shadowing.
- `Model` is built dynamically from the configuration classes and is used by the shutter control logic to determine the shutter control behavior.
- `HomeCompanion.Core.Model.ModelValueBinder` is used to bind IValue references in to configuration to the actual values in the model.

## Architecture

The shutter control logic is implemented in `ShutterController` (ILogic) class, which is responsible for controlling the shutters based on the room shutter scene and other inputs.

The room shutter scene is governed by `RoomShutterSceneLogic` (ILogic) class, which is responsible for determining the room shutter scene based on the room configuration and other inputs.

Both ILogic classes base on the runtimes classes managed by `ShadowingRuntimesController` (IRuntimeController, ILogic) class, which is responsible for managing the runtime state of the shutters and other inputs.

The runtime classes react to system events and publish `ShutterAutomationComputationTriggerEvent` events to trigger the shutter control logic to compute the new shutter positions.
The events are published by `ShadowingRuntimesController` which implements time window gating, prioritization and aggregation of events. The events are consumed mostly by `ShutterController` and `RoomShutterSceneLogic`.
