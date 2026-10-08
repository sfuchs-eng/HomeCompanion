# Standard logics for HomeCompanion

## Overview

This file describes the standard logics provided by HomeCompanion, which are implemented in the `HomeCompanion.Logics` namespace.
For documentation about writing your own logics, see [HomeCompanion.Logics](../README.md).

## Logic-specific options and configuration

Logic modules can declare a strongly typed options class and bind it directly to a configuration section via the `LogicOptionsAttribute`.
This is the preferred pattern for logic-specific settings that should live under the application's JSON configuration.

```csharp
public class PutzmodusOptions
{
    public TimeSpan AutoOffDuration { get; set; } = new(4, 30, 0);
    public TimeSpan LightDuration { get; set; } = new(0, 30, 0);
}

[LogicOptions(typeof(PutzmodusOptions), "Logics:Putzmodus")]
public class PutzmodusLogic(
    KnxValues knxValues,
    IOptions<PutzmodusOptions> options,
    ILogger<PutzmodusLogic> logger) : LogicBase(logger)
{
    private readonly PutzmodusOptions _options = options.Value;
}
```

The runtime registers `TOptions` and binds it to the section named by the attribute. The same pattern works with `IOptions<T>`, `IOptionsMonitor<T>`, and `IOptionsSnapshot<T>`, as long as the constructor requests one of these interfaces for the matching `TOptions`.

Example JSON:

```json
{
  "Logics": {
    "Putzmodus": {
      "AutoOffDuration": "04:30:00",
      "LightDuration": "00:30:00"
    }
  }
}
```

This keeps logic defaults and runtime settings close to the module without hardcoding configuration keys in the logic itself.

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

