# HomeCompanion Calendar Events Architecture

## Purpose

This document specifies the calendar events framework that allows users to schedule extension-defined event types and publish them through the HomeCompanion event bus.

## Scope

- Calendar entry persistence
- Event type discovery and opt-in
- Quartz scheduling and reconciliation
- Minimal API surface
- Radzen-based web UI integration

## Key Concepts

### Calendar Entry

`CalendarEntry` is the persisted and API-exposed aggregate.

Core fields:

- `Id`, `Title`, `EventType`
- `StartTime`, `EndTime`
- `IsRecurring`, `RecurrenceCronExpression`
- `TimeZoneId`, `IsEnabled`
- `MetadataJson`

### Schedulable Event Type

A schedulable event type:

- implements `ICalendarEvent`
- is annotated with `CalendarEventTypeAttribute`
- has a parameterless constructor

The registry (`AttributedCalendarEventTypeRegistry`) discovers these types from loaded assemblies.

### Calendar Event Phases

Each calendar publication carries phase information:

- `CalendarEventPhase.Start`
- `CalendarEventPhase.End`

Both phases include the same entry identity and metadata payload.

## Runtime Flow

1. User creates/updates/deletes entries through `/api/calendar/entries`.
2. Entry is persisted via `ICalendarEntryStore` (EF Core).
3. `ICalendarScheduler.ReconcileAsync()` is triggered after changes.
4. Quartz triggers are rebuilt for enabled entries.
5. `CalendarEventDispatchJob` fires and publishes an `ICalendarEvent` implementation via `IEventPublisher`.

## Scheduling Semantics

### One-time Entries

- Start trigger at `StartTime`.
- End trigger at `EndTime`.

### Recurring Entries

- Start trigger uses `RecurrenceCronExpression` in `TimeZoneId`.
- End trigger is computed from `(EndTime - StartTime)` and scheduled per start occurrence.

### Reconciliation

`CalendarQuartzScheduler` clears the calendar trigger group and re-installs triggers from persisted enabled entries.

## Persistence and Configuration

Persistence is EF Core + SQLite, dedicated database file independent from Quartz store.

Configuration section:

```json
{
  "HomeCompanion": {
    "CalendarPersistence": {
      "DbPath": "state/calendar/calendar.db"
    }
  }
}
```

`DbPath` may be absolute or relative to `AppContext.BaseDirectory`.

## API Surface

Base route: `/api/calendar`

- `GET /event-types`
- `GET /entries`
- `GET /entries/{id}`
- `POST /entries`
- `PUT /entries/{id}`
- `DELETE /entries/{id}`

Validation highlights:

- `Title` required
- `StartTime < EndTime`
- recurring entries require `RecurrenceCronExpression`
- `EventType` must resolve to an attributed `ICalendarEvent`

## UI Integration

The server UI page `/calendar` uses Radzen:

- Scheduler views: day/week/month
- Form-based CRUD editor
- Event type dropdown from registry
- Metadata JSON text area

## Extending With New Event Types

Minimal extension example:

```csharp
[CalendarEventType("Window Ventilation", Category = "HVAC")]
public sealed class WindowVentilationEvent : HomeCompanionEvent, ICalendarEvent
{
    public Guid CalendarEntryId { get; set; }
    public string CalendarEntryTitle { get; set; } = string.Empty;
    public CalendarEventPhase Phase { get; set; }
    public string? MetadataJson { get; set; }
}
```

No extra calendar registration is required beyond loading the assembly.

## Testing Guidance

Focused tests should cover:

- trigger reconciliation for one-time and recurring entries
- dispatch job publication payload mapping
- event type discovery opt-in behavior
- API validation and scheduler refresh on writes
