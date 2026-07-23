# Standard logics for HomeCompanion

## Shutter automation logic

In `HomeCompanion.Logics.Shutters`

Handles shutter / blinds related room scenes and automates shutter opening, closing and shadowing for complex scenarios in family homes.

Consists of a set of logics working together:

- `AutoShadow.EnvironmentalsEvaluatorLogic` performs signal processing of environmental measurements for use by other shadowing logics.
- `ShadowingRuntimesController` manages the runtime state management for Buildings, Rooms and Shutters.
- `RoomShutterSceneLogic` implements the room scene logic for shutter automation. Only particular scenes relate to fully automated shutter control, others serve for user interaction and manual control.
- `ShutterController` implements the actual shutter control logic, including the logic for shadowing and sun protection.

These logics heavily base on `HomeCompanion.Model` for configuration and `IValue` interaction.

## Motorized window

In `HomeCompanion.Logics.MotorizedWindow`

Operates window and shutter combination which can be controlled by 3 wires: command open (to actor), command close (to actor), command acknowledge (feedback from actor). Such are for example Velux roof windows interfaced by a KLF200 using the wired interface functions, not the Ethernet based API.

Any shutters would be included in auto-shadowing via `IValue`s, but the window itself is not part of the shadowing logic.

## ThermalControl

Determines the present thermal governance policy for an entire building. The results goes as input via an IValue to the automatic shadowing and room shutter scene automation logic.

## SunShade

Automation of garden sun shades, e.g. for pergolas or patio awnings. The logic is similar to the shutter automation logic, but with different configuration and control parameters as it serves different needs and purpose.

## Sun

Computes the present sun position relative to defined locations, in particular the buildings configured in the model.

It updates the sun position `IValue`s found in `ShadowingSpecial` of any configured building in the model.

