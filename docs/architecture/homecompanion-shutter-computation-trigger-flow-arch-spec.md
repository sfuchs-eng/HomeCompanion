# HomeCompanion Shutter Computation Trigger Flow Architecture

## Purpose

This document describes how shutter automation trigger requests are produced, distributed, batched, and consumed in the shutter control stack.

The purpose is to make the event/queue layering explicit so the architecture remains understandable when new producers or consumers are added.

## Scope

In scope:

- `ShutterAutomationComputationTriggerContext`
- `ShutterAutomationComputationTriggerEvent`
- `IQueueFeeder<T>` and the event-bus-backed implementation
- `ShadowingRuntimesController`
- `ShutterController`
- room and shutter runtime trigger emission
- Quartz-triggered re-evaluation such as `ShutterResetExternalOverrideJob`

Out of scope:

- individual shutter actuator commands
- room-scene state machine specifics beyond trigger routing
- external KNX/OpenHAB transport semantics

## Key Design Idea

The system separates three concerns:

1. Trigger creation
   - runtimes decide that a value or state change requires recomputation
2. Trigger distribution
   - the generic queue feeder publishes a trigger event on the event bus
3. Trigger batching and scheduling
   - `ShutterController` coalesces incoming events by urgency, age, and scope before doing the expensive automation evaluation

This keeps runtime code simple while preserving responsiveness and preventing noisy, high-frequency recomputation cascades.

## Trigger Model

A trigger is represented as `ShutterAutomationComputationTriggerContext` and contains:

- `ThingKeys` — the shutters and/or rooms affected
- `Scope` — `Global`, `RoomSpecific`, `ShutterSpecific`, or a combination
- `TriggeringValue` and `ValueEventArgs` — diagnostic and context metadata
- `Timestamp` — event time for ordering and urgency
- `Urgency` — `Immediate`, `Normal`, or `Slow`

The context is intentionally lightweight and generic. It does not require a specific producer or consumer to know how the final state computation will be evaluated.

## Queue Feeder Contract

The queue abstraction is:

```csharp
public interface IQueueFeeder<in TChannelItem>
{
    Task EnqueueAsync(TChannelItem trigger, CancellationToken token);
    void Enqueue(TChannelItem trigger);
}
```

This contract is intentionally generic and can be used by:

- runtime code
- Quartz jobs
- manual trigger producers
- any future component that needs to request shutter recomputation

The actual implementation is a generic event-bus adapter:

```csharp
new EventBusQueueFeeder<ShutterAutomationComputationTriggerContext>(
    eventPublisher,
    trigger => new ShutterAutomationComputationTriggerEvent { Context = trigger });
```

This adapter converts the trigger context into a `ShutterAutomationComputationTriggerEvent` and publishes it through `IEventPublisher`.

## Why the queue is not keyed

The queue is not keyed because it represents a logical channel for shutter-computation requests, not a per-producer or per-job destination.

The routing information is already contained in the trigger `Context` itself:

- affected `ThingKeys`
- scope flags
- urgency
- timestamp

A keyed service would only make sense if there were multiple independent queue implementations competing for the same logical purpose. That is not the current model. The architecture intentionally uses one queue type for all shutter recomputation producers.

This is also why `ShutterResetExternalOverrideJob` can inject the same `IQueueFeeder<ShutterAutomationComputationTriggerContext>` and enqueue a new recomputation trigger after resetting the external override state.

## Distribution Path

Runtime code emits triggers in response to model or value changes:

- `BuildingRuntime` triggers when building-level shadowing or scene inputs change
- `RoomRuntime` triggers when room temperature or room-scene state changes
- `ShutterRuntime` triggers when shutter-local conditions change
- `ShutterResetExternalOverrideJob` also emits a trigger to cause recomputation after override expiry

These producers all depend on the same generic feeder contract. The feeder publishes a `ShutterAutomationComputationTriggerEvent` onto the event bus.

## Event Bus Role

The event bus is the distribution fabric, not the scheduler.

It allows multiple subscribers to react to the same trigger without coupling the producers to specific consumers. In current code, the main subscribers are:

- `ShutterController`
- `RoomShutterSceneLogic`

The event bus is also important for maintainability because unrelated modules can observe trigger events without the producer having to know about them.

## Batching and Pacing in `ShutterController`

`ShutterController` subscribes to `ShutterAutomationComputationTriggerEvent` and forwards each event into its `BackgroundRunner<ShutterAutomationComputationTriggerContext>` collector.

The collector then:

1. reads trigger contexts from a channel
2. measures their urgency and age
3. keeps collecting while the remaining time window allows more triggers to arrive
4. groups by scope (global vs room vs shutter)
5. sorts by urgency and forwards a merged batch to the computation loop

This is the real throttling layer. It prevents excessive evaluation churn when a value changes rapidly or when multiple inputs change in a short burst.

The effect is that trigger producers remain cheap and fire frequently, while the compute-heavy automation logic only runs at the right cadence.

## Quartz Job Interaction

Quartz jobs are not a separate queue subsystem. They participate in the same trigger model by injecting the generic queue feeder and enqueuing a `ShutterAutomationComputationTriggerContext`.

That means a scheduler-driven recomputation request is treated like any other runtime trigger request and follows the same event-bus + batching path.

This is the correct architecture because it keeps Quartz decoupled from the shutter runtime internals while still enabling scheduled re-evaluation.

## Related Source Files

- `HomeCompanion/Logics/Shutters/README.md`
- `HomeCompanion/Logics/Shutters/ShutterController.cs`
- `HomeCompanion/Logics/Shutters/RoomShutterSceneLogic.cs`
- `HomeCompanion/Logics/Shutters/ShadowingRuntimesController.cs`
- `HomeCompanion/Logics/Shutters/ShutterResetExternalOverrideJob.cs`
- `HomeCompanion/Base/Utilities/Queuing.cs`

## Decision Summary

1. Trigger producers use the generic `IQueueFeeder<ShutterAutomationComputationTriggerContext>`.
2. The default implementation publishes `ShutterAutomationComputationTriggerEvent` on the event bus.
3. The event bus is distribution layer only; batching remains in `ShutterController`.
4. Quartz jobs participate via the same queue protocol instead of creating their own custom queue implementation.
5. No keyed queue service is required unless the project later introduces deliberately parallel trigger channels with different semantics.

## Testing Guidance

Architecture tests and regression tests should cover:

- DI resolution of `IQueueFeeder<ShutterAutomationComputationTriggerContext>`
- trigger publication to the event bus
- room-specific and shutter-specific routing
- urgency ordering and batching behavior in `ShutterController`
- Quartz-triggered recomputation after time-based reset scenarios
