using HomeCompanion.Base.Quartz;
using HomeCompanion.Calendar;
using HomeCompanion.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl.Matchers;

namespace HomeCompanion.Core.Calendar;

internal sealed class CalendarQuartzScheduler(
    IServiceScopeFactory scopeFactory,
    ISchedulerFactory schedulerFactory,
    TimeProvider timeProvider,
    ILogger<CalendarQuartzScheduler> logger) : ICalendarScheduler, IQuartzSchedulerConfigurator
{
    internal const string TriggerGroup = "HomeCompanion.CalendarEntries";

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ISchedulerFactory _schedulerFactory = schedulerFactory;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<CalendarQuartzScheduler> _logger = logger;

    public async ValueTask ConfigureAsync(IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        await ReconcileInternalAsync(scheduler, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken).ConfigureAwait(false);
        await ReconcileInternalAsync(scheduler, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReconcileInternalAsync(IScheduler scheduler, CancellationToken cancellationToken)
    {
        var triggerKeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(TriggerGroup), cancellationToken)
            .ConfigureAwait(false);
        foreach (var triggerKey in triggerKeys)
            await scheduler.UnscheduleJob(triggerKey, cancellationToken).ConfigureAwait(false);

        var jobKey = typeof(CalendarEventDispatchJob).GetJobKeyFromType()
            ?? throw new InvalidOperationException("CalendarEventDispatchJob is missing RegisterQuartzJobAttribute.");

        using var scope = _scopeFactory.CreateScope();
        var entryStore = scope.ServiceProvider.GetRequiredService<ICalendarEntryStore>();
        var entries = await entryStore.ListAsync(cancellationToken).ConfigureAwait(false);
        var enabledEntries = entries.Where(e => e.IsEnabled).ToArray();

        foreach (var entry in enabledEntries)
        {
            _logger.LogTrace(
                "Reconciling calendar entry {EntryId} ({Title}) start={StartTime:o} end={EndTime:o} recurring={IsRecurring} timezone={TimeZoneId} nowUtc={NowUtc:o}",
                entry.Id,
                entry.Title,
                entry.StartTime,
                entry.EndTime,
                entry.IsRecurring,
                entry.TimeZoneId,
                _timeProvider.GetLocalNow());
            await ScheduleEntryAsync(scheduler, jobKey, entry, _timeProvider.GetLocalNow(), cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Calendar schedule reconciled for {Count} enabled entries.", enabledEntries.Length);
    }

    private async Task ScheduleEntryAsync(
        IScheduler scheduler,
        JobKey jobKey,
        CalendarEntry entry,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (entry.IsRecurring)
        {
            if (string.IsNullOrWhiteSpace(entry.RecurrenceCronExpression))
            {
                _logger.LogTrace("Skipping recurring calendar entry {EntryId} because no cron expression is configured.", entry.Id);
                return;
            }

            _logger.LogTrace(
                "Scheduling recurring calendar entry {EntryId} with cron '{CronExpression}' in timezone {TimeZoneId}.",
                entry.Id,
                entry.RecurrenceCronExpression,
                entry.TimeZoneId);

            var triggerBuilder = TriggerBuilder.Create()
                .WithIdentity($"CalendarStart_{entry.Id:N}", TriggerGroup)
                .ForJob(jobKey)
                .UsingJobData(CalendarEventDispatchJob.EntryIdKey, entry.Id.ToString("D"))
                .UsingJobData(CalendarEventDispatchJob.PhaseKey, (int)CalendarEventPhase.Start);

            var timeZone = ResolveTimeZone(entry.TimeZoneId);
            var trigger = triggerBuilder
                .WithCronSchedule(entry.RecurrenceCronExpression, x => x.InTimeZone(timeZone))
                .Build();

            await scheduler.ScheduleJob(trigger, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (entry.StartTime > nowUtc)
        {
            _logger.LogTrace("Scheduling one-time calendar start trigger for entry {EntryId} at {StartTime:o} (nowUtc={NowUtc:o}).", entry.Id, entry.StartTime, nowUtc);
            var startTrigger = TriggerBuilder.Create()
                .WithIdentity($"CalendarStart_{entry.Id:N}", TriggerGroup)
                .ForJob(jobKey)
                .StartAt(entry.StartTime)
                .UsingJobData(CalendarEventDispatchJob.EntryIdKey, entry.Id.ToString("D"))
                .UsingJobData(CalendarEventDispatchJob.PhaseKey, (int)CalendarEventPhase.Start)
                .Build();
            await scheduler.ScheduleJob(startTrigger, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogTrace("Skipping one-time calendar start trigger for entry {EntryId} because start={StartTime:o} is not in the future (nowUtc={NowUtc:o}).", entry.Id, entry.StartTime, nowUtc);
        }

        if (entry.EndTime > nowUtc)
        {
            _logger.LogTrace("Scheduling one-time calendar end trigger for entry {EntryId} at {EndTime:o} (nowUtc={NowUtc:o}).", entry.Id, entry.EndTime, nowUtc);
            var endTrigger = TriggerBuilder.Create()
                .WithIdentity($"CalendarEnd_{entry.Id:N}", TriggerGroup)
                .ForJob(jobKey)
                .StartAt(entry.EndTime)
                .UsingJobData(CalendarEventDispatchJob.EntryIdKey, entry.Id.ToString("D"))
                .UsingJobData(CalendarEventDispatchJob.PhaseKey, (int)CalendarEventPhase.End)
                .Build();
            await scheduler.ScheduleJob(endTrigger, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogTrace("Skipping one-time calendar end trigger for entry {EntryId} because end={EndTime:o} is not in the future (nowUtc={NowUtc:o}).", entry.Id, entry.EndTime, nowUtc);
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return TimeZoneInfo.Local;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }
}
