# TODO: Shutter Automation Trigger Architecture

## Goal

Document the current design and the remaining architectural cleanup for shutter automation recomputation, batching, and scoping.

## Current architecture

The trigger path is intentionally layered:

1. Runtime inputs create `ShutterAutomationComputationTriggerContext` instances.
2. Producers push them through a generic `IQueueFeeder<ShutterAutomationComputationTriggerContext>`.
3. The feeder publishes a `ShutterAutomationComputationTriggerEvent` onto the event bus.
4. `ShutterController` subscribes to the event bus and batches triggers by urgency, age, and scope.
5. Room-scene logic and shutter-position logic consume the relevant subset of the trigger.

This separation is good because it avoids expensive automation work directly in runtime event callbacks while keeping the event bus as a distribution mechanism rather than the batching mechanism.

## What is already working well

- Trigger creation is cleanly localized to runtime classes.
- Event-bus publication is generic and decoupled from the concrete runtime types.
- `ShutterController` is the right place for pacing/coalescing logic.
- The queue abstraction supports Quartz jobs and runtime producers alike.

## Architectural concern to keep in mind

`ShutterAutomationComputationTriggerContext` currently mixes room-scene and shutter-position semantics under one generic type. This remains workable because scope flags and `ThingKeys` isolate the affected set, but it is a little too overloaded for long-term clarity.

The intended model is:

- room-scoped triggers affect room scene decision logic
- shutter-scoped triggers affect actual shutter target computation
- global triggers are a rare, broad recomputation entrypoint

This is currently enforced by convention, not by a strongly separated type system.

## Planned cleanup / improvement

### High-priority clarifications

- Separate room-scene trigger and shutter automation trigger contracts if the codebase grows further.
- Make scope handling explicit in the dispatcher, especially where room-specific triggers are handled differently from shutter-specific triggers.
- Ensure the “room-scene logic does not do shutter work” and “shutter logic does not evaluate room scene decisions” boundaries remain obvious in tests and docs.

### Medium-priority hardening

- Add focused tests for trigger batching by urgency and scope.
- Add tests covering mixed global/room/shutter triggers to validate ordering and de-duplication.
- Add diagnostics to visualize queue backlog and dropped/merged trigger counts.

### Optional future refactor

A more type-safe version of the architecture would use distinct trigger types for:

- room scene recomputation
- shutter target recomputation
- global building-level recomputation

This would improve readability and validation without changing the current event-bus + batching concept.

## Acceptance criteria

The architecture is considered healthy when:

- trigger producers remain small and side-effect free
- the event bus remains a fan-out mechanism only
- batching logic remains in `ShutterController`
- room and shutter computations remain correctly scoped
- a future contributor can understand the flow without reading both runtime and controller implementations in detail

## Related files

- `HomeCompanion/Logics/Shutters/README.md`
- `HomeCompanion/Logics/Shutters/ShutterController.cs`
- `HomeCompanion/Logics/Shutters/RoomShutterSceneLogic.cs`
- `HomeCompanion/Logics/Shutters/ShadowingRuntimesController.cs`
- `HomeCompanion/Logics/Shutters/ShutterResetExternalOverrideJob.cs`
- `HomeCompanion/Logics/Shutters/ShutterAutomationComputationTriggerEvent.cs`
- `HomeCompanion/docs/architecture/homecompanion-shutter-computation-trigger-flow-arch-spec.md`
