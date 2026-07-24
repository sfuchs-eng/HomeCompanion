# ADR-0004: Calendar Events Framework

**Date:** 2026-07-24

## Context

HomeCompanion needs a user-facing calendar feature where users can create entries that emit events at start and end times.
The feature must support extension-defined event types, recurring schedules, metadata for forward compatibility,
and robust restart behavior.

## Decision

Introduce a calendar events framework with four layers:

1. **Event type opt-in model**

- Extensions expose schedulable event types by implementing `ICalendarEvent` and annotating with `CalendarEventTypeAttribute`.
- The framework discovers only attributed `ICalendarEvent` types; it does not expose all `IEvent` implementations.

1. **Persistence model (EF Core + SQLite)**

- Calendar entries are stored in a dedicated SQLite database via EF Core.
- Database ownership is separate from Quartz persistent store.
- Entry metadata is stored as JSON object text (`MetadataJson`) for forward compatibility.

1. **Scheduling model (Quartz.NET)**

- Quartz drives background firing and event publication.
- One-time entries produce distinct start and end triggers.
- Recurring entries use a cron start trigger; the end trigger for each occurrence is derived from entry duration and scheduled at runtime.
- Startup reconciliation removes calendar-managed triggers and rebuilds them from persisted enabled entries.

1. **Web/API model (Radzen + Minimal APIs)**

- HomeCompanion.Server provides `/api/calendar` endpoints for CRUD and event type listing.
- Blazor UI uses Radzen scheduler/calendar components for month/week/day visualization and editing.

## Consequences

### Positive

- New event types can be introduced by extensions without server changes.
- Schedules survive restarts through persisted entries and reconciliation.
- Metadata is future-compatible and can be interpreted by downstream consumers.
- UI/API and scheduler behavior are aligned through shared contracts.

### Tradeoffs

- Storing metadata as raw JSON defers strict validation to event-specific consumers.
- Recurring end triggers are materialized per firing (dynamic scheduling), which increases trigger churn for high-frequency schedules.

### Constraints and Invariants

- Use `TimeProvider.System` for framework time acquisition.
- Keep HomeCompanion public/shared projects decoupled from HomeCompanion.Local.
- Calendar framework publishes to the existing event bus via `IEventPublisher`.
- Event types must expose a parameterless constructor for runtime materialization.

## Implementation References

- `Abstractions/Calendar/*`
- `Abstractions/Events/ICalendarEvent.cs`
- `Abstractions/Events/CalendarEventTypeAttribute.cs`
- `Core/Calendar/*`
- `Server/Calendar/CalendarEndpointExtensions.cs`
- `Server/Components/Pages/Calendar.razor`
