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
            await ScheduleEntryAsync(scheduler, jobKey, entry, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Calendar schedule reconciled for {Count} enabled entries.", enabledEntries.Length);
    }

    private static async Task ScheduleEntryAsync(
        IScheduler scheduler,
        JobKey jobKey,
        CalendarEntry entry,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (entry.IsRecurring)
        {
            if (string.IsNullOrWhiteSpace(entry.RecurrenceCronExpression))
                return;

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
            var startTrigger = TriggerBuilder.Create()
                .WithIdentity($"CalendarStart_{entry.Id:N}", TriggerGroup)
                .ForJob(jobKey)
                .StartAt(entry.StartTime)
                .UsingJobData(CalendarEventDispatchJob.EntryIdKey, entry.Id.ToString("D"))
                .UsingJobData(CalendarEventDispatchJob.PhaseKey, (int)CalendarEventPhase.Start)
                .Build();
            await scheduler.ScheduleJob(startTrigger, cancellationToken).ConfigureAwait(false);
        }

        if (entry.EndTime > nowUtc)
        {
            var endTrigger = TriggerBuilder.Create()
                .WithIdentity($"CalendarEnd_{entry.Id:N}", TriggerGroup)
                .ForJob(jobKey)
                .StartAt(entry.EndTime)
                .UsingJobData(CalendarEventDispatchJob.EntryIdKey, entry.Id.ToString("D"))
                .UsingJobData(CalendarEventDispatchJob.PhaseKey, (int)CalendarEventPhase.End)
                .Build();
            await scheduler.ScheduleJob(endTrigger, cancellationToken).ConfigureAwait(false);
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
