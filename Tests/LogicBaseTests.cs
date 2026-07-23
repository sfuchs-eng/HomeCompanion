using HomeCompanion.Events;
using HomeCompanion.Logics;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests;

[TestFixture]
public class LogicBaseTests
{
    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class NullEventPublisher : IEventPublisher
    {
        public ValueTask PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public void Publish(IEvent @event)
        {
        }
    }

    private sealed class RecordingSubscriber : IEventSubscriber
    {
        public List<object> Handlers { get; } = [];

        public void Subscribe<T>(IEventHandler<T> handler) where T : IEvent
            => Handlers.Add(handler);

        public void Subscribe<T>(EventHandlerDelegate<T> handler) where T : IEvent
        {
            Handlers.Add(handler);
        }
    }

    /// <summary>
    /// Minimal concrete <see cref="LogicBase"/> that counts how many times
    /// <see cref="InitializeAsyncLatched"/> is invoked.
    /// </summary>
    private sealed class CountingLogic(IEventPublisher publisher, IEventSubscriber subscriber)
        : LogicBase(publisher, subscriber)
    {
        public int InitCount { get; private set; }

        protected override Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
        {
            InitCount++;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Logic that subscribes a handler during <see cref="InitializeAsyncLatched"/>.
    /// </summary>
    private sealed class SubscribingLogic(IEventPublisher publisher, IEventSubscriber subscriber)
        : LogicBase(publisher, subscriber)
    {
        private sealed class DummyEvent : IEvent
        {
            public DateTimeOffset Timestamp { get; init; }
        }

        private sealed class DummyHandler : IEventHandler<DummyEvent>
        {
            public ValueTask HandleAsync(DummyEvent @event, CancellationToken cancellationToken = default)
                => ValueTask.CompletedTask;
        }

        protected override Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
        {
            //Subscribe(new DummyHandler());
            return Task.CompletedTask;
        }
    }

    private static (CountingLogic logic, RecordingSubscriber subscriber) CreateLogic()
    {
        var subscriber = new RecordingSubscriber();
        var logic = new CountingLogic(new NullEventPublisher(), subscriber);
        return (logic, subscriber);
    }

    // ── Tests: InitializeAsync latching ──────────────────────────────────────

    [Test]
    public async Task InitializeAsync_CallsInitializeAsyncLatchedExactlyOnce()
    {
        var (logic, _) = CreateLogic();

        await logic.InitializeAsync();

        Assert.That(logic.InitCount, Is.EqualTo(1));
    }

    [Test]
    public async Task InitializeAsync_CalledTwice_InvokesLatchedOnlyOnce()
    {
        var (logic, _) = CreateLogic();

        await logic.InitializeAsync();
        await logic.InitializeAsync();

        Assert.That(logic.InitCount, Is.EqualTo(1));
    }

    [Test]
    public async Task InitializeAsync_ConcurrentCalls_LatchedCalledOnce()
    {
        var (logic, _) = CreateLogic();

        // Fire 5 concurrent initializations
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => logic.InitializeAsync())
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.That(logic.InitCount, Is.EqualTo(1));
    }

    [Test]
    public async Task InitializeAsync_ConcurrentCalls_AllTasksComplete()
    {
        var (logic, _) = CreateLogic();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => logic.InitializeAsync())
            .ToArray();

        // Verify no deadlock: all tasks complete within a reasonable time
        var allCompleted = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5))
            .ContinueWith(t => !t.IsFaulted && !t.IsCanceled);

        Assert.That(allCompleted, Is.True);
    }

    // ── Tests: IsEnabled state ────────────────────────────────────────────────

    [Test]
    public async Task InitializeAsync_SetsIsEnabledToTrue()
    {
        var (logic, _) = CreateLogic();

        Assert.That(logic.IsEnabled, Is.False); // initial state

        await logic.InitializeAsync();

        Assert.That(logic.IsEnabled, Is.True);
    }

    [Test]
    public async Task EnableAsync_SetsIsEnabledTrue()
    {
        var (logic, _) = CreateLogic();
        await logic.DisableAsync(); // ensure starting from disabled

        await logic.EnableAsync();

        Assert.That(logic.IsEnabled, Is.True);
    }

    [Test]
    public async Task DisableAsync_SetsIsEnabledFalse()
    {
        var (logic, _) = CreateLogic();
        await logic.InitializeAsync(); // enables as part of init

        await logic.DisableAsync();

        Assert.That(logic.IsEnabled, Is.False);
    }

    [Test]
    public async Task EnableDisable_CanToggleRepeatedly()
    {
        var (logic, _) = CreateLogic();

        await logic.EnableAsync();
        Assert.That(logic.IsEnabled, Is.True);

        await logic.DisableAsync();
        Assert.That(logic.IsEnabled, Is.False);

        await logic.EnableAsync();
        Assert.That(logic.IsEnabled, Is.True);
    }

    // ── Tests: Subscribe ──────────────────────────────────────────────────────

    [Test]
    public async Task Subscribe_RegistersHandlerWithSubscriber()
    {
        var subscriber = new RecordingSubscriber();
        var logic = new SubscribingLogic(new NullEventPublisher(), subscriber);

        await logic.InitializeAsync();

        Assert.That(subscriber.Handlers, Has.Count.EqualTo(1));
    }
}
