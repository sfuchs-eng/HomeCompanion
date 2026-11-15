# TODO: Shutter Automation Trigger Architecture

## Goal

Document the current design and the current structural contract for shutter automation, batching, and scoping.

## Current architecture

The trigger path is intentionally layered:

1. Runtime inputs create `ShutterAutomationComputationTriggerContext` instances.
2. Producers push them through a generic `IQueueFeeder<ShutterAutomationComputationTriggerContext>`.
3. The feeder publishes a `ShutterAutomationComputationTriggerEvent` onto the event bus.
4. `ShutterController` subscribes to the event bus and batches triggers by urgency, age, and scope.
5. Room-scene logic and shutter-position logic consume the relevant subset of the trigger.

This separation avoids expensive automation work directly in runtime callbacks while keeping the event bus as a distribution mechanism rather than the batching mechanism.

## Current structure and namespace contract

The shutter code is now organized to reflect the responsibility split directly in file layout and namespace names.

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

This structure is part of the architecture and must be kept in sync when new code is added.

### Explicit rules for future additions

- Folder names and namespaces must stay aligned.
- Model-only types belong in `HomeCompanion.Logics.Shutters.Model` and under `Model/`.
- Runtime state and runtime context belong in `HomeCompanion.Logics.Shutters.Runtimes` and under `Runtimes/`.
- Room-scene logic belongs in `HomeCompanion.Logics.Shutters.RoomScene` and under `RoomScene/`.
- Shutter automation and target-evaluation logic belongs in `HomeCompanion.Logics.Shutters.ShutterAutomation` and under `ShutterAutomation/`.
- Auto-shadow/environmental logic belongs in `HomeCompanion.Logics.Shutters.AutoShadow` and under `AutoShadow/`.
- Do not reintroduce flat root-level shutter classes unless they are genuinely cross-cutting helpers with no clear bucket.

## What is already working well

- Trigger creation is localized to runtime classes.
- Event-bus publication is generic and decoupled from concrete runtime types.
- `ShutterController` is the correct place for pacing and coalescing logic.
- The queue abstraction supports Quartz jobs and runtime producers alike.
- Folder and namespace boundaries are now consistent with the architectural intent.

## Architectural concern to keep in mind

`ShutterAutomationComputationTriggerContext` still mixes room-scene and shutter-position semantics under one generic type. This remains workable because scope flags and `ThingKeys` isolate the affected set, but it is still worth keeping explicit in tests and docs.

The intended model is:

- room-scoped triggers affect room-scene decision logic
- shutter-scoped triggers affect actual shutter target computation
- global triggers remain a broad recomputation entry point

This is currently enforced by convention and by scope handling, not by a separate type system.

## Planned follow-up / improvement

### High-priority clarifications

- [x] Keep scope handling explicit in the dispatcher when room-scene requests and shutter-target requests are handled differently.
- [~] Make the room-scene versus shutter automation boundary obvious in tests and docs.
- [x] Keep the trigger type generic but avoid overloading it with unrelated responsibilities as the code grows.

Status note (2026-11-15):

- Scope now uses explicit flag semantics and consumer-side filtering helpers in trigger context.
- Trigger batching and affected-shutter resolution now use explicit room/shutter/global interpretation.
- Room-scene consumer now accepts only room-scoped trigger intent.
- Initial boundary tests and README scope-dispatch contract have been added.

### Medium-priority hardening

- Add focused tests for trigger batching by urgency and scope.
- Add tests covering mixed global/room/shutter triggers to validate ordering and de-duplication.
- Add diagnostics to visualize queue backlog and dropped/merged trigger counts.

### Optional future refactor

A more type-safe version of the architecture would use distinct trigger types for:

- room-scene recomputation
- shutter-target recomputation
- global building-level recomputation

This would improve readability and validation without changing the current event-bus + batching concept.

## Acceptance criteria

The architecture is considered healthy when:

- trigger producers remain small and side-effect free
- the event bus remains a fan-out mechanism only
- batching logic remains in `ShutterController`
- room and shutter computations remain correctly scoped
- folder and namespace boundaries remain aligned
- a future contributor can understand the flow without reading both runtime and controller implementations in detail

## Related files

- `HomeCompanion/Logics/Shutters/README.md`
- `HomeCompanion/Logics/Shutters/ShutterAutomation/`
- `HomeCompanion/Logics/Shutters/RoomScene/`
- `HomeCompanion/Logics/Shutters/Runtimes/`
- `HomeCompanion/Logics/Shutters/AutoShadow/`
- `HomeCompanion/Logics/Shutters/Model/`
- `HomeCompanion/docs/architecture/homecompanion-shutter-computation-trigger-flow-arch-spec.md`
