using Microsoft.Extensions.Logging.Abstractions;
using HomeCompanion.Core;
using HomeCompanion.Events;
using HomeCompanion.Core.Events;

namespace HomeCompanion.Tests;

[TestFixture]
public class EventBusTests
{
    // ── Minimal test event types ─────────────────────────────────────────────

    private record TestEvent(int Sequence) : IEvent
    {
        public DateTimeOffset Timestamp { get; init; }
    }
    private record DerivedEvent(int Sequence) : TestEvent(Sequence);

    // ── Helper: run the bus dispatch loop for the duration of an async action ─

    private static async Task RunWithBusAsync(EventBus bus, Func<Task> action, int drainMs = 200)
    {
        using var cts = new CancellationTokenSource();
        var busTask = bus.StartAsync(cts.Token);

        await action();

        // Allow the background loop time to drain the channel.
        await Task.Delay(drainMs);

        await cts.CancelAsync();
        try { await bus.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }
    }

    private static EventBus CreateBus() => new(NullLogger<EventBus>.Instance);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task Events_AreDispatched_InFifoOrder()
    {
        var bus = CreateBus();
        var received = new List<int>();

        bus.Subscribe(new LambdaHandler<TestEvent>(e =>
        {
            received.Add(e.Sequence);
            return ValueTask.CompletedTask;
        }));

        await RunWithBusAsync(bus, async () =>
        {
            for (int i = 1; i <= 5; i++)
                await bus.PublishAsync(new TestEvent(i));
        });

        Assert.That(received, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
    }

    [Test]
    public async Task FailingHandler_DoesNotPreventNextHandler_FromBeingCalled()
    {
        var bus = CreateBus();
        var secondHandlerCalled = false;

        bus.Subscribe(new LambdaHandler<TestEvent>(_ => throw new InvalidOperationException("boom")));
        bus.Subscribe(new LambdaHandler<TestEvent>(_ =>
        {
            secondHandlerCalled = true;
            return ValueTask.CompletedTask;
        }));

        await RunWithBusAsync(bus, async () => await bus.PublishAsync(new TestEvent(1)));

        Assert.That(secondHandlerCalled, Is.True);
    }

    [Test]
    public async Task Handler_ForBaseType_ReceivesDerivedEvents()
    {
        var bus = CreateBus();
        var received = new List<int>();

        // Subscribe to the base type — should also fire for DerivedEvent.
        bus.Subscribe(new LambdaHandler<TestEvent>(e =>
        {
            received.Add(e.Sequence);
            return ValueTask.CompletedTask;
        }));

        await RunWithBusAsync(bus, async () =>
        {
            await bus.PublishAsync(new DerivedEvent(1));
            await bus.PublishAsync(new TestEvent(2));
        });

        Assert.That(received, Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public async Task Handler_ForDerivedType_DoesNotReceiveBaseEvents()
    {
        var bus = CreateBus();
        var derivedHandlerCalled = false;

        bus.Subscribe(new LambdaHandler<DerivedEvent>(_ =>
        {
            derivedHandlerCalled = true;
            return ValueTask.CompletedTask;
        }));

        await RunWithBusAsync(bus, async () => await bus.PublishAsync(new TestEvent(1)));

        Assert.That(derivedHandlerCalled, Is.False);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class LambdaHandler<T>(Func<T, ValueTask> fn) : IEventHandler<T> where T : IEvent
    {
        public ValueTask HandleAsync(T @event, CancellationToken cancellationToken = default) => fn(@event);
    }
}
