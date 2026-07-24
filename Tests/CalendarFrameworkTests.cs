using HomeCompanion.Calendar;
using HomeCompanion.Core.Calendar;
using HomeCompanion.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Quartz;

namespace HomeCompanion.Tests;

[TestFixture]
public class CalendarFrameworkTests
{
    [Test]
    public async Task CalendarQuartzScheduler_ReconcileAsync_SchedulesStartAndEnd_ForEnabledOneTimeEntry()
    {
        var now = TimeProvider.System.GetUtcNow();
        var entry = new CalendarEntry
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            EventType = typeof(CalendarSignalEvent).AssemblyQualifiedName!,
            StartTime = now.AddMinutes(10),
            EndTime = now.AddMinutes(20),
            IsRecurring = false,
            TimeZoneId = TimeZoneInfo.Local.Id,
            IsEnabled = true,
            MetadataJson = "{}",
        };

        var store = new Mock<ICalendarEntryStore>();
        store.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([entry]);

        var scheduledTriggers = new List<ITrigger>();
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.GetTriggerKeys(It.IsAny<Quartz.Impl.Matchers.GroupMatcher<TriggerKey>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        scheduler.Setup(s => s.ScheduleJob(It.IsAny<ITrigger>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTimeOffset.UtcNow)
            .Callback<ITrigger, CancellationToken>((trigger, _) => scheduledTriggers.Add(trigger));

        var schedulerFactory = new Mock<ISchedulerFactory>();
        schedulerFactory.Setup(f => f.GetScheduler(It.IsAny<CancellationToken>()))
            .ReturnsAsync(scheduler.Object);

        var scopeFactory = CreateScopeFactory(store.Object);

        var sut = new CalendarQuartzScheduler(
            scopeFactory.Object,
            schedulerFactory.Object,
            TimeProvider.System,
            NullLogger<CalendarQuartzScheduler>.Instance);

        await sut.ReconcileAsync();

        Assert.That(scheduledTriggers.Count, Is.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(scheduledTriggers.Any(t => t.Key.Name == $"CalendarStart_{entry.Id:N}"), Is.True);
            Assert.That(scheduledTriggers.Any(t => t.Key.Name == $"CalendarEnd_{entry.Id:N}"), Is.True);
            Assert.That(scheduledTriggers.All(t => t.JobDataMap.GetString(CalendarEventDispatchJob.EntryIdKey) == entry.Id.ToString("D")), Is.True);
        });
    }

    [Test]
    public async Task CalendarQuartzScheduler_ReconcileAsync_SchedulesCronStart_ForRecurringEntry()
    {
        var entry = new CalendarEntry
        {
            Id = Guid.NewGuid(),
            Title = "Recurring",
            EventType = typeof(CalendarSignalEvent).AssemblyQualifiedName!,
            StartTime = TimeProvider.System.GetUtcNow().AddHours(1),
            EndTime = TimeProvider.System.GetUtcNow().AddHours(2),
            IsRecurring = true,
            RecurrenceCronExpression = "0 0/15 * * * ?",
            TimeZoneId = TimeZoneInfo.Local.Id,
            IsEnabled = true,
            MetadataJson = "{}",
        };

        var store = new Mock<ICalendarEntryStore>();
        store.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([entry]);

        ITrigger? scheduled = null;
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.GetTriggerKeys(It.IsAny<Quartz.Impl.Matchers.GroupMatcher<TriggerKey>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        scheduler.Setup(s => s.ScheduleJob(It.IsAny<ITrigger>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTimeOffset.UtcNow)
            .Callback<ITrigger, CancellationToken>((trigger, _) => scheduled = trigger);

        var schedulerFactory = new Mock<ISchedulerFactory>();
        schedulerFactory.Setup(f => f.GetScheduler(It.IsAny<CancellationToken>()))
            .ReturnsAsync(scheduler.Object);

        var scopeFactory = CreateScopeFactory(store.Object);

        var sut = new CalendarQuartzScheduler(
            scopeFactory.Object,
            schedulerFactory.Object,
            TimeProvider.System,
            NullLogger<CalendarQuartzScheduler>.Instance);

        await sut.ReconcileAsync();

        Assert.That(scheduled, Is.Not.Null);
        Assert.That(scheduled!.Key.Name, Is.EqualTo($"CalendarStart_{entry.Id:N}"));
        Assert.That(scheduled, Is.AssignableTo<ICronTrigger>());
        Assert.That(scheduled.JobDataMap.GetInt(CalendarEventDispatchJob.PhaseKey), Is.EqualTo((int)CalendarEventPhase.Start));
    }

    [Test]
    public async Task CalendarEventDispatchJob_ExecuteAsync_PublishesAttributedCalendarEvent()
    {
        var entry = new CalendarEntry
        {
            Id = Guid.NewGuid(),
            Title = "Publish test",
            EventType = typeof(CalendarSignalEvent).AssemblyQualifiedName!,
            StartTime = TimeProvider.System.GetUtcNow().AddMinutes(5),
            EndTime = TimeProvider.System.GetUtcNow().AddMinutes(10),
            IsRecurring = false,
            IsEnabled = true,
            TimeZoneId = TimeZoneInfo.Local.Id,
            MetadataJson = "{\"source\":\"test\"}",
        };

        var store = new StubCalendarEntryStore([entry]);
        var registry = new AttributedCalendarEventTypeRegistry();
        var publisher = new RecordingPublisher();

        var scheduler = new Mock<IScheduler>();
        var schedulerFactory = new Mock<ISchedulerFactory>();
        schedulerFactory.Setup(f => f.GetScheduler(It.IsAny<CancellationToken>()))
            .ReturnsAsync(scheduler.Object);

        var dataMap = new JobDataMap
        {
            [CalendarEventDispatchJob.EntryIdKey] = entry.Id.ToString("D"),
            [CalendarEventDispatchJob.PhaseKey] = (int)CalendarEventPhase.Start,
        };

        var context = new Mock<IJobExecutionContext>();
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        context.SetupGet(c => c.MergedJobDataMap).Returns(dataMap);

        var sut = new CalendarEventDispatchJob(
            store,
            registry,
            publisher,
            schedulerFactory.Object,
            TimeProvider.System,
            NullLogger<CalendarEventDispatchJob>.Instance);

        await sut.Execute(context.Object);

        var evt = publisher.PublishedEvents.OfType<CalendarSignalEvent>().SingleOrDefault();
        Assert.That(evt, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(evt!.CalendarEntryId, Is.EqualTo(entry.Id));
            Assert.That(evt.CalendarEntryTitle, Is.EqualTo(entry.Title));
            Assert.That(evt.Phase, Is.EqualTo(CalendarEventPhase.Start));
            Assert.That(evt.MetadataJson, Is.EqualTo(entry.MetadataJson));
        });
    }

    private sealed class StubCalendarEntryStore(IEnumerable<CalendarEntry> seed) : ICalendarEntryStore
    {
        private readonly Dictionary<Guid, CalendarEntry> _entries = seed.ToDictionary(e => e.Id, e => e);

        public Task<IReadOnlyList<CalendarEntry>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CalendarEntry>>(_entries.Values.ToArray());

        public Task<CalendarEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_entries.TryGetValue(id, out var entry) ? entry : null);

        public Task<CalendarEntry> UpsertAsync(CalendarEntry entry, CancellationToken cancellationToken = default)
        {
            _entries[entry.Id] = entry;
            return Task.FromResult(entry);
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_entries.Remove(id));
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<IEvent> PublishedEvents { get; } = [];

        public ValueTask PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(@event);
            return ValueTask.CompletedTask;
        }

        public void Publish(IEvent @event)
        {
            PublishedEvents.Add(@event);
        }
    }

    private static Mock<IServiceScopeFactory> CreateScopeFactory(ICalendarEntryStore store)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(store);
        var serviceProvider = serviceCollection.BuildServiceProvider();

        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        return scopeFactory;
    }
}
