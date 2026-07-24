using HomeCompanion.Calendar;
using HomeCompanion.Base.Quartz;
using HomeCompanion.Events;
using Microsoft.Extensions.Logging;
using Quartz;
using System.Reflection;

namespace HomeCompanion.Core.Calendar;

[RegisterQuartzJob("CalendarEventDispatchJob", "HomeCompanion.Calendar")]
internal sealed class CalendarEventDispatchJob(
    ICalendarEntryStore entryStore,
    ICalendarEventTypeRegistry eventTypeRegistry,
    IEventPublisher eventPublisher,
    ISchedulerFactory schedulerFactory,
    TimeProvider timeProvider,
    ILogger<CalendarEventDispatchJob> logger) : IJob
{
    internal const string EntryIdKey = "EntryId";
    internal const string PhaseKey = "Phase";
    internal const string OccurrenceStartUtcTicksKey = "OccurrenceStartUtcTicks";

    private readonly ICalendarEntryStore _entryStore = entryStore;
    private readonly ICalendarEventTypeRegistry _eventTypeRegistry = eventTypeRegistry;
    private readonly IEventPublisher _eventPublisher = eventPublisher;
    private readonly ISchedulerFactory _schedulerFactory = schedulerFactory;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<CalendarEventDispatchJob> _logger = logger;

    public async Task Execute(IJobExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
        if (!TryReadEntryId(context.MergedJobDataMap, out var entryId))
        {
            _logger.LogWarning("Calendar job fired without a valid entry id.");
            return;
        }

        var phase = ReadPhase(context.MergedJobDataMap);
        var entry = await _entryStore.GetAsync(entryId, cancellationToken).ConfigureAwait(false);
        if (entry is null || !entry.IsEnabled)
            return;

        var eventType = _eventTypeRegistry.ResolveEventType(entry.EventType);
        if (eventType is null)
        {
            _logger.LogWarning("Calendar entry {EntryId} references unknown event type '{EventType}'.", entry.Id, entry.EventType);
            return;
        }

        if (Activator.CreateInstance(eventType) is not ICalendarEvent calendarEvent)
        {
            _logger.LogWarning("Calendar event type {EventType} cannot be created or does not implement ICalendarEvent.", entry.EventType);
            return;
        }

        SetTimestamp(calendarEvent, _timeProvider.GetUtcNow());
        calendarEvent.CalendarEntryId = entry.Id;
        calendarEvent.CalendarEntryTitle = entry.Title;
        calendarEvent.Phase = phase;
        calendarEvent.MetadataJson = entry.MetadataJson;

        await _eventPublisher.PublishAsync(calendarEvent, cancellationToken).ConfigureAwait(false);

        if (phase == CalendarEventPhase.Start && entry.IsRecurring)
            await ScheduleRecurringEndEventAsync(entry, context, cancellationToken).ConfigureAwait(false);
    }

    private async Task ScheduleRecurringEndEventAsync(
        CalendarEntry entry,
        IJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        var duration = entry.EndTime - entry.StartTime;
        if (duration <= TimeSpan.Zero)
            return;

        var fireAtUtc = context.FireTimeUtc.UtcDateTime + duration;
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken).ConfigureAwait(false);
        var jobKey = typeof(CalendarEventDispatchJob).GetJobKeyFromType()
            ?? throw new InvalidOperationException("CalendarEventDispatchJob is missing RegisterQuartzJobAttribute.");

        var trigger = TriggerBuilder.Create()
            .WithIdentity(
                $"CalendarRecurringEnd_{entry.Id:N}_{fireAtUtc.Ticks}",
                CalendarQuartzScheduler.TriggerGroup)
            .ForJob(jobKey)
            .StartAt(DateBuilder.DateOf(
                fireAtUtc.Second,
                fireAtUtc.Minute,
                fireAtUtc.Hour,
                fireAtUtc.Day,
                fireAtUtc.Month,
                fireAtUtc.Year))
            .UsingJobData(EntryIdKey, entry.Id.ToString("D"))
            .UsingJobData(PhaseKey, (int)CalendarEventPhase.End)
            .Build();

        await scheduler.ScheduleJob(trigger, cancellationToken).ConfigureAwait(false);
    }

    private static bool TryReadEntryId(JobDataMap map, out Guid id)
    {
        var value = map.GetString(EntryIdKey);
        return Guid.TryParse(value, out id);
    }

    private static CalendarEventPhase ReadPhase(JobDataMap map)
    {
        var raw = map.GetInt(PhaseKey);
        return Enum.IsDefined(typeof(CalendarEventPhase), raw)
            ? (CalendarEventPhase)raw
            : CalendarEventPhase.Start;
    }

    private static void SetTimestamp(ICalendarEvent calendarEvent, DateTimeOffset timestamp)
    {
        var property = calendarEvent.GetType().GetProperty(
            nameof(IEvent.Timestamp),
            BindingFlags.Instance | BindingFlags.Public);
        property?.SetValue(calendarEvent, timestamp);
    }
}
