# Shutter and Shadow Control

## Purpose

The shutter control subsystem provides a modular policy foundation for room-aware shadowing.
It harmonizes:

- global governance (building-wide constraints and external conditions),
- room-local intent (manual operation, room objective profile),
- wear minimization (reduce unnecessary moves),
- and persistence (manual override state survives restarts).

This logic replaces legacy monolithic behavior with policy building blocks designed for incremental extension.

## Current Feature Set (Implemented)

- Policy model primitives for automation level, thermal-control mode, room objective profile, objective-selector rules, and schedule transition config.
- Building-level shadowing defaults in `CfgShadowingSpecial`:
  - thermal-control mode,
  - default automation level,
  - persisted-manual-override default,
  - default manual override duration,
  - facade sun exposure defaults and dynamic cut-over rules,
  - optional UV intensity value binding.
- Room-level policy fields in `CfgRoom`:
  - objective profile override,
  - automation level override,
  - manual override persistence/duration override,
  - room-local facade cut-over override and dynamic cut-over rules,
  - UV minimal shadow defaults,
  - optional objective selector inputs,
  - cron-style schedule transition definitions.
- Objective resolution helper (`ShutterPolicyResolver`) with the following precedence:
  1. explicit room objective,
  2. first matching selector-input rule,
  3. thermal-control-based default mapping.
- Fixed rule: manual operation has priority over UV-protection.
- Initial `ShutterControl` bootstrap:
  - materializes per-room policy snapshots,
  - subscribes to dynamic input value changes and reevaluates from full runtime model,
  - evaluates cron schedules in-process via a swappable scheduler abstraction,
  - loads persisted manual override state,
  - prunes expired entries.
- Schedule due transitions are published as `RoomScheduleTransitionDueEvent` on the event bus.

## Architecture Overview

### Main Components

- `ShutterControl`
  - orchestration entry point for shutter policy runtime initialization.
  - currently focuses on policy materialization and persisted state restore.
- `ShutterSceneCommandControl`
  - dedicated room-scene command executor.
  - applies scene semantics for manual override and automation resume.
  - executes room-scoped command sets for schedule-driven automation while room scene is `50`/`52`.
- `ShutterPolicyResolver`
  - pure/stateless policy decisions for objective selection and UV/manual precedence.
- `IRoomScheduleEvaluator` / `InProcessCronRoomScheduleEvaluator`
  - cron schedule evaluation abstraction with lightweight in-process implementation.
  - designed to allow later replacement by a Quartz-backed evaluator without touching policy orchestration.
- Model configuration layer
  - `CfgShadowingSpecial` for global defaults and value references.
  - `CfgRoom` for room-level overrides and schedule/objective configuration.
- Persistence
  - `IStateStore` is used by `ShutterControl` to load manual override state (`ShutterControlManualOverrides`).

### Layering and Direction

- Policy code remains bus-agnostic.
- Value access is through model bindings (`IValue`) and core lifecycle components.
- No KNX/OpenHAB transport logic exists in shutter policy code.

## Design Decisions

- Manual override persistence: enabled and supported by design.
- Manual operation vs UV-protection: manual always wins.
- Global vs room conflict strategy: bounded envelope model.
  - Global policy constrains what is allowed.
  - Room policy resolves inside that envelope.
- Default room objective is derived from thermal-control mode when no explicit room objective is set.
- Schedule-based room transitions are represented as cron-style config objects and integrated into the same policy flow (not direct actuator shortcuts).

## Policy Concepts

### Automation Levels

- `ManualOnly`
  - no automatic actions.
- `AutomaticWithTemporaryManualOverride`
  - automation runs, manual override is temporary.
- `AutomaticStrict`
  - automation ignores manual scene overrides except safety/interlock paths.

### Room Objective Profiles

- `InheritFromThermalControl`
- `BalancedDefault`
- `DaylightPriority`
- `ThermalPriority`
- `UvProtectionPriority`

Thermal-control mapping (default behavior):

- `Disabled` -> `DaylightPriority`
- `Balanced` -> `BalancedDefault`
- `CoolingPriority` -> `ThermalPriority`

## Configuration Guide

### Building-Level (`CfgShadowingSpecial`)

Key fields:

- `ThermalControl`
- `ThermalControlModeReference`
- `DefaultAutomationLevel`
- `PersistManualOverrides`
- `DefaultManualOverrideDuration`
- `ResumeAutomationScenes` (default: `[50, 52]`)
- `DefaultFacadeSunCutoverAngle`
- `DynamicFacadeSunCutoverRules`
- `UvIntensityReference`
- `ScheduleEngine` (`InProcess` default, `Quartz` optional)
- `SpecialScenes[*].RoomReference` (room key format: `Building/Floor/Room`)
- `SunPositionAzimuthReference`
- `SunPositionElevationReference`

### Room-Level (`CfgRoom`)

Key fields:

- `ObjectiveProfile`
- `AutomationLevelOverride`
- `PersistManualOverride`
- `ManualOverrideDuration`
- `FacadeSunCutoverAngleOverride`
- `FacadeSunCutoverAngleDynamicRules`
- `UvProtectionShadowPosition`
- `UvProtectionShadowSlat`
- `ObjectiveSelectorInputs`
- `ScheduleTransitions`

### Example (JSON)

```json
{
  "Model": {
    "Buildings": {
      "Main": {
        "Specials": {
          "Shadowing": {
            "Kind": "ShadowingSpecial",
            "ThermalControl": "Balanced",
            "ThermalControlModeReference": "ThermalControlMode",
            "DefaultAutomationLevel": "AutomaticWithTemporaryManualOverride",
            "PersistManualOverrides": true,
            "DefaultManualOverrideDuration": "02:00:00",
            "ResumeAutomationScenes": [50, 52],
            "DefaultFacadeSunCutoverAngle": 20,
            "DynamicFacadeSunCutoverRules": [
              {
                "ThermalControlMode": "CoolingPriority",
                "OutdoorTemperatureMin": 28,
                "CutoverAngle": 35
              }
            ],
            "ScheduleEngine": "InProcess",
            "GlobalShutterSceneReference": "SzeneLamellenGanzesHaus",
            "AutoShadowStatusReference": "BeschattungsautomatikStatus",
            "AbsenceReference": "LangzeitAbwesenheitAktiviert",
            "DisableAutoShadowAssessmentReference": "DisableAssessmentAutoshadowEntireHouse",
            "OutdoorTemperatureReference": "AussentemperaturVorneDach",
            "SunIntensityEastReference": "HelligkeitE",
            "SunIntensitySouthReference": "HelligkeitS",
            "SunIntensityWestReference": "HelligkeitW",
            "SunPositionAzimuthReference": "SunPositionAzimuthDeg",
            "SunPositionElevationReference": "SunPositionElevationDeg",
            "SpecialScenes": {
              "NightCloseCustom": {
                "RoomReference": "Main/EG/StubeEssenKueche",
                "Number": 20,
                "Commands": {
                  "SetEssenLinksPos": {
                    "TargetValueReference": "SWE111LamellenEssenVorneLinksPosition",
                    "Value": 100
                  }
                }
              }
            }
          }
        },
        "Floors": {
          "Upper": {
            "Rooms": {
              "KidsRoom1": {
                "ShutterSceneReference": "SzeneLamellenZimmerI",
                "FacadeSunCutoverAngleOverride": 25,
                "ObjectiveProfile": "InheritFromThermalControl",
                "ScheduleTransitions": {
                  "NightClose": {
                    "CronExpression": "30 19 * * *",
                    "Scene": 3,
                    "CloseOnly": true,
                    "ManualOpenGracePeriod": "00:45:00",
                    "EnableShadowTranslationAfterManualOpen": true
                  }
                }
              }
            }
          }
        }
      }
    }
  }
}
```

Notes:

- Side-by-side schedule evaluators are available and selected by `ScheduleEngine`.
- `InProcess`: Cronos-based evaluator for lightweight in-process operation.
- `Quartz`: Quartz-based evaluator for cron compatibility with future durable scheduler adoption.
- `UvIntensityReference` is optional; if no dedicated UV channel exists in `KnxValues`, omit it and rely on room/objective defaults.
- Room scene semantics in `ShutterSceneCommandControl`:
  - `1`, `2`, `3`: manual override active (actor-level actions).
  - configured `ResumeAutomationScenes` (default `50`, `52`): clear manual override and resume automation.
  - other scenes: manual override scenes only when a matching room-scoped `SpecialScenes` controller exists.
- Schedule-driven automation commands are executed only while room scene is in `ResumeAutomationScenes` and no manual override is active.
- During schedule-driven automation, room shutter targets are filtered by facade sun exposure using:
  - facade orientation,
  - live sun azimuth/elevation values,
  - minimum sun elevation,
  - effective cut-over angle (room override > dynamic rule > global default),
  - optional shutter `ShadowingZones` (`Inside` / `Outside`).
- First evaluator call primes internal state and avoids catch-up firing on startup.
- Schedule transitions are designed for room scene intents (for example nightly close), not direct low-level actuator writes.

## Runtime State and Persistence

- State set name: `ShutterControlManualOverrides`.
- Stored entries are room-scoped and keyed as `Building/Floor/Room`.
- Expired entries are removed at startup restore.

## Usage in Development

### Where to Extend Next

- Add durable Quartz job/trigger hosting if misfire handling, persisted schedule state, or advanced calendars are required.
- Add policy arbiter that combines:
  - global envelope,
  - room objective result,
  - schedule intents,
  - manual override state,
  - thermal/UV conditions.
- Add movement planner and actuator write dedup/rate-limit logic.
- Add save-path for manual overrides (currently load/prune path is present).

### Testing Strategy

- Keep policy logic in pure helper/service classes for deterministic unit tests.
- Use integration tests for model binding and persisted state behavior.
- Existing policy resolver tests are in:
  - `Tests/Shutters/ShutterPolicyResolverTests.cs`
- Schedule evaluator tests are in:
  - `Tests/Shutters/RoomScheduleEvaluatorTests.cs`

## Related Source Files

- `Base/Logics/Shutters/ShutterControl.cs`
- `Base/Logics/Shutters/ShutterSceneCommandControl.cs`
- `Base/Logics/Shutters/ShutterPolicyResolver.cs`
- `Base/Logics/Shutters/RoomScheduleEvaluator.cs`
- `Base/Model/ShadowingPolicy.cs`
- `Base/Model/Building.cs`
- `Base/Model/Room.cs`
