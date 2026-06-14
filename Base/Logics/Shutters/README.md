# Shutter control logic

## Overall principles of controlling shuttters this way

- Each room has a room shutter scene (KNX DPT scene control) that is used to configure according what principles the shutters should be controlled.
- Each shutter is controlled individually, considering the room shuttter scene and other inputs (e.g. sun position, room scene, global inputs like "ThermalControl", etc.).
- `HomeCompanion.Model` contains global, building, floor, room and facade level configuration for shutters, including the room shutter scene and the shutter type.
- There are different shutter types, which differ by their capabilities and actuator types. `ShutterType` defines the shutter type and its capabilities.

## Configuration

- `CfgBuilding`, `CfgFloor`, `CfgRoom`, `CfgShutter` are building, floor, room and shutter level configuration for shutter control behavior.
- `CfgShadowingSpecial` is a building level configuration for shutter control behavior in context of shadowing.

## Scene control

Room shutter scenes define how shutters should react to different triggers and requests.
See `HomeCompanion.Base.Model.RoomShutterScenes` for details.
