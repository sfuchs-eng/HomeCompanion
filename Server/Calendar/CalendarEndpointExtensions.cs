using HomeCompanion.Calendar;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace HomeCompanion.Server.Calendar;

public static class CalendarEndpointExtensions
{
    /// <summary>
    /// Maps calendar API endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapHomeCompanionCalendar(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/calendar");
        group.DisableAntiforgery();

        group.MapGet("/event-types", (ICalendarEventTypeRegistry registry) =>
            TypedResults.Ok(registry.ListEventTypes()));

        group.MapGet("/entries", async (ICalendarEntryStore store, CancellationToken cancellationToken) =>
            TypedResults.Ok(await store.ListAsync(cancellationToken).ConfigureAwait(false)));

        group.MapGet("/entries/{id:guid}", async Task<Results<Ok<CalendarEntry>, NotFound>> (
            Guid id,
            ICalendarEntryStore store,
            CancellationToken cancellationToken) =>
        {
            var entry = await store.GetAsync(id, cancellationToken).ConfigureAwait(false);
            return entry is null ? TypedResults.NotFound() : TypedResults.Ok(entry);
        });

        group.MapPost("/entries", async Task<Results<Created<CalendarEntry>, ValidationProblem>> (
            CalendarEntry entry,
            ICalendarEntryStore store,
            ICalendarEventTypeRegistry registry,
            ICalendarScheduler scheduler,
            CancellationToken cancellationToken) =>
        {
            var validation = Validate(entry, registry);
            if (validation is not null)
                return TypedResults.ValidationProblem(validation);

            if (entry.Id == Guid.Empty)
                entry.Id = Guid.NewGuid();

            var saved = await store.UpsertAsync(entry, cancellationToken).ConfigureAwait(false);
            await scheduler.ReconcileAsync(cancellationToken).ConfigureAwait(false);
            return TypedResults.Created($"/api/calendar/entries/{saved.Id:D}", saved);
        });

        group.MapPut("/entries/{id:guid}", async Task<Results<Ok<CalendarEntry>, ValidationProblem, NotFound>> (
            Guid id,
            CalendarEntry entry,
            ICalendarEntryStore store,
            ICalendarEventTypeRegistry registry,
            ICalendarScheduler scheduler,
            CancellationToken cancellationToken) =>
        {
            var existing = await store.GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (existing is null)
                return TypedResults.NotFound();

            entry.Id = id;
            var validation = Validate(entry, registry);
            if (validation is not null)
                return TypedResults.ValidationProblem(validation);

            var saved = await store.UpsertAsync(entry, cancellationToken).ConfigureAwait(false);
            await scheduler.ReconcileAsync(cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(saved);
        });

        group.MapDelete("/entries/{id:guid}", async Task<Results<NoContent, NotFound>> (
            Guid id,
            ICalendarEntryStore store,
            ICalendarScheduler scheduler,
            CancellationToken cancellationToken) =>
        {
            var deleted = await store.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
            if (!deleted)
                return TypedResults.NotFound();

            await scheduler.ReconcileAsync(cancellationToken).ConfigureAwait(false);
            return TypedResults.NoContent();
        });

        return endpoints;
    }

    private static Dictionary<string, string[]>? Validate(CalendarEntry entry, ICalendarEventTypeRegistry registry)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(entry.Title))
            errors[nameof(entry.Title)] = ["Title is required."];

        if (entry.StartTime >= entry.EndTime)
            errors[nameof(entry.EndTime)] = ["EndTime must be after StartTime."];

        if (entry.IsRecurring && string.IsNullOrWhiteSpace(entry.RecurrenceCronExpression))
            errors[nameof(entry.RecurrenceCronExpression)] = ["RecurrenceCronExpression is required for recurring entries."];

        if (registry.ResolveEventType(entry.EventType) is null)
            errors[nameof(entry.EventType)] = ["EventType is not registered as calendar-capable."];

        if (string.IsNullOrWhiteSpace(entry.MetadataJson))
            entry.MetadataJson = "{}";

        return errors.Count == 0 ? null : errors;
    }
}
