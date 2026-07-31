using HomeCompanion.Abstractions;
using HomeCompanion.Core;
using HomeCompanion.Core.Events;
using HomeCompanion.Diagnostics;
using HomeCompanion.Events;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests.Values;

[TestFixture]
public class ValuesManagerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EventBus CreateBus() => new(NullLogger<EventBus>.Instance);

    private static async Task RunWithBusAsync(EventBus bus, Func<Task> action, int drainMs = 150)
    {
        using var cts = new CancellationTokenSource();
        await bus.StartAsync(cts.Token);
        await action();
        await Task.Delay(drainMs);
        await cts.CancelAsync();
        try { await bus.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }
    }

    private static ValueBase<T> MakeValue<T>()
        => new(NullLoggerFactory.Instance.CreateLogger<ValueBase<T>>());

    private static ValuesManager CreateManager(
        EventBus bus,
        IHomeCompanionLifeCycleSynchronization? lifecycle = null,
        TimeProvider? timeProvider = null,
        params IValuesContainer[] containers)
        => new(bus, bus, containers, lifecycle ?? new StubLifecycleSync(), timeProvider ?? TimeProvider.System, NullLogger<ValuesManager>.Instance);

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    private sealed class StubLifecycleSync : IHomeCompanionLifeCycleSynchronization
    {
        public Task AwaitBusesConnectedAsync(TimeSpan timeout, CancellationToken token = default) => Task.CompletedTask;

        public Task WaitForInitializationStageCompletedAsync(AppInitializationStage level, TimeSpan timeout, CancellationToken token = default)
            => Task.CompletedTask;

        public Task SignalInitializationStageCompletedAsync(AppInitializationStage level) => Task.CompletedTask;

        public bool IsInitializationStageCompleted(AppInitializationStage level) => true;

        public bool IsAllUpToStageCompleted(AppInitializationStage level) => true;
    }

    private sealed class BlockingSignalLifecycleSync : IHomeCompanionLifeCycleSynchronization
    {
        private readonly TaskCompletionSource _signalEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _allowSignalCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _initValuesRegisteredCompleted;

        public Task AwaitBusesConnectedAsync(TimeSpan timeout, CancellationToken token = default) => Task.CompletedTask;

        public Task WaitForInitializationStageCompletedAsync(AppInitializationStage level, TimeSpan timeout, CancellationToken token = default)
            => Task.CompletedTask;

        public async Task SignalInitializationStageCompletedAsync(AppInitializationStage level)
        {
            if (level != AppInitializationStage.InitValuesRegistered)
                return;

            _signalEntered.TrySetResult();
            await _allowSignalCompletion.Task.WaitAsync(CancellationToken.None);
            _initValuesRegisteredCompleted = true;
        }

        public bool IsInitializationStageCompleted(AppInitializationStage level)
            => level != AppInitializationStage.InitValuesRegistered || _initValuesRegisteredCompleted;

        public bool IsAllUpToStageCompleted(AppInitializationStage level)
            => level < AppInitializationStage.InitValuesRegistered || _initValuesRegisteredCompleted;

        public Task WaitUntilSignalRequestedAsync() => _signalEntered.Task;

        public void ReleaseSignal() => _allowSignalCompletion.TrySetResult();
    }

    // ── Fixture containers ────────────────────────────────────────────────────

    /// <summary>Container with a single recorded value that tracks ReceiveUpdate/ReceiveWrite calls.</summary>
    private sealed class RecordingContainer : IValuesContainer
    {
        public ValueBase<bool> Switch { get; } = MakeValue<bool>();
        public IEnumerable<IValue> GetValues() => [Switch];
    }

    /// <summary>Container with two values.</summary>
    private sealed class TwoValueContainer : IValuesContainer
    {
        public ValueBase<bool> A { get; } = MakeValue<bool>();
        public ValueBase<int> B { get; } = MakeValue<int>();
        public IEnumerable<IValue> GetValues() => [A, B];
    }

    /// <summary>Container holding a nested object with IValue properties (tests recursive discovery).</summary>
    private sealed class NestedContainer : IValuesContainer
    {
        /// <summary>
        /// Inner class with IValue properties — not itself an IValuesContainer.
        /// ValuesManager should discover its values via recursive property traversal.
        /// </summary>
        public sealed class InnerObject
        {
            public ValueBase<int> Counter { get; } = MakeValue<int>();
        }

        public ValueBase<bool> TopLevel { get; } = MakeValue<bool>();
        public InnerObject Nested { get; } = new();

        public IEnumerable<IValue> GetValues() => [TopLevel];
    }

    // ── Tests: initialization ─────────────────────────────────────────────────

    [Test]
    public async Task StartAsync_InitializesAllValuesFromContainers()
    {
        var bus = CreateBus();
        var container = new TwoValueContainer();
        var manager = CreateManager(bus, containers: [container]);

        await manager.StartAsync(CancellationToken.None);

        // After initialization, writing to the value should publish events (publisher is wired up)
        var published = new List<IEvent>();
        bus.Subscribe(new LambdaHandler<ValueWriteRequest>(e => published.Add(e)));

        await RunWithBusAsync(bus, async () =>
        {
            container.A.Write(true);
            await Task.Delay(50);
        });

        Assert.That(published, Has.Count.GreaterThan(0), "Value should be initialized and publish write requests.");
    }

    [Test]
    public async Task RouteValueUpdateReceived_RoutesToTargetValue()
    {
        var bus = CreateBus();
        var container = new RecordingContainer();
        var manager = CreateManager(bus, containers: [container]);
        await manager.StartAsync(CancellationToken.None);

        await RunWithBusAsync(bus, async () =>
        {
            await bus.PublishAsync(new ValueUpdateReceived { Target = container.Switch, Value = true });
            await Task.Delay(50);
        });

        Assert.That(container.Switch.Value, Is.EqualTo(true));
        Assert.That(container.Switch.Status.HasFlag(ValueStatus.Initialized), Is.True);
        Assert.That(container.Switch.Status.HasFlag(ValueStatus.Live), Is.True);
    }

    [Test]
    public async Task RouteValueWriteReceived_RoutesToTargetValue()
    {
        var bus = CreateBus();
        var container = new RecordingContainer();
        var manager = CreateManager(bus, containers: [container]);
        await manager.StartAsync(CancellationToken.None);

        await RunWithBusAsync(bus, async () =>
        {
            await bus.PublishAsync(new ValueWriteReceived { Target = container.Switch, NewValue = true });
            await Task.Delay(50);
        });

        Assert.That(container.Switch.Value, Is.EqualTo(true));
        Assert.That(container.Switch.Status.HasFlag(ValueStatus.Live), Is.True);
    }

    [Test]
    public async Task RouteValueUpdateReceived_WithNullTarget_DoesNotThrow()
    {
        var bus = CreateBus();
        var manager = CreateManager(bus);
        await manager.StartAsync(CancellationToken.None);

        Assert.DoesNotThrowAsync(async () =>
        {
            await RunWithBusAsync(bus, async () =>
            {
                await bus.PublishAsync(new ValueUpdateReceived { Target = null, Value = true });
                await Task.Delay(50);
            });
        });
    }

    [Test]
    public async Task RouteValueUpdateReceived_MultipleContainers_RoutesCorrectly()
    {
        var bus = CreateBus();
        var c1 = new RecordingContainer();
        var c2 = new TwoValueContainer();
        var manager = CreateManager(bus, containers: [c1, c2]);
        await manager.StartAsync(CancellationToken.None);

        await RunWithBusAsync(bus, async () =>
        {
            await bus.PublishAsync(new ValueUpdateReceived { Target = c2.B, Value = 77 });
            await Task.Delay(50);
        });

        Assert.That(c2.B.Value, Is.EqualTo(77));
        Assert.That(c1.Switch.Value, Is.EqualTo(false)); // unaffected
    }

    // ── Tests: registration / unregistration ─────────────────────────────────

    [Test]
    public async Task UnregisterValue_StopsRoutingToThatValue()
    {
        var bus = CreateBus();
        var container = new RecordingContainer();
        var manager = CreateManager(bus, containers: [container]);
        await manager.StartAsync(CancellationToken.None);

        manager.UnregisterValue(container.Switch);

        await RunWithBusAsync(bus, async () =>
        {
            await bus.PublishAsync(new ValueUpdateReceived { Target = container.Switch, Value = true });
            await Task.Delay(50);
        });

        Assert.That(container.Switch.Value, Is.EqualTo(false), "Value should not be updated after unregistration.");
    }

    [Test]
    public async Task RegisterValue_AfterUnregister_ResumesRouting()
    {
        var bus = CreateBus();
        var container = new RecordingContainer();
        var manager = CreateManager(bus, containers: [container]);
        await manager.StartAsync(CancellationToken.None);

        manager.UnregisterValue(container.Switch);
        manager.RegisterValue(container.Switch);

        await RunWithBusAsync(bus, async () =>
        {
            await bus.PublishAsync(new ValueUpdateReceived { Target = container.Switch, Value = true });
            await Task.Delay(50);
        });

        Assert.That(container.Switch.Value, Is.EqualTo(true));
    }

    // ── Tests: recursive discovery ────────────────────────────────────────────

    [Test]
    public async Task DiscoverValues_FindsNestedObjectIValueProperties()
    {
        var bus = CreateBus();
        var container = new NestedContainer();
        var manager = CreateManager(bus, containers: [container]);
        await manager.StartAsync(CancellationToken.None);

        // The nested Counter should also be initialized (publisher wired up)
        // Verify by routing an update to it
        await RunWithBusAsync(bus, async () =>
        {
            await bus.PublishAsync(new ValueUpdateReceived { Target = container.Nested.Counter, Value = 42 });
            await Task.Delay(50);
        });

        Assert.That(container.Nested.Counter.Value, Is.EqualTo(42));
    }

    // ── Tests: Dispose ────────────────────────────────────────────────────────

    [Test]
    public void Dispose_AllowsMultipleCalls()
    {
        var bus = CreateBus();
        var manager = CreateManager(bus);

        Assert.DoesNotThrow(() =>
        {
            manager.Dispose();
            manager.Dispose(); // second dispose must not throw
        });
    }

    [Test]
    public async Task Dispose_ThrowsOnSubsequentRegisterValue()
    {
        var bus = CreateBus();
        var manager = CreateManager(bus);
        await manager.StartAsync(CancellationToken.None);
        manager.Dispose();

        var orphan = MakeValue<bool>();
        Assert.Throws<ObjectDisposedException>(() => manager.RegisterValue(orphan));
    }

    [Test]
    public async Task RouteValueUpdateReceived_BeforeInitValuesRegistered_IsDroppedAndReportedInDiagnostics()
    {
        var bus = CreateBus();
        var container = new RecordingContainer();
        var lifecycle = new BlockingSignalLifecycleSync();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 31, 8, 0, 0, TimeSpan.Zero));
        var manager = CreateManager(bus, lifecycle, timeProvider, container);

        var startTask = manager.StartAsync(CancellationToken.None);
        await lifecycle.WaitUntilSignalRequestedAsync();

        using var cts = new CancellationTokenSource();
        await bus.StartAsync(cts.Token);
        try
        {
            await bus.PublishAsync(new ValueUpdateReceived { Target = container.Switch, Value = true });
            await Task.Delay(50);
            Assert.That(container.Switch.Value, Is.False, "Pre-stage inbound updates must be dropped.");

            lifecycle.ReleaseSignal();
            await startTask;

            timeProvider.Advance(TimeSpan.FromSeconds(2));
            await bus.PublishAsync(new ValueUpdateReceived { Target = container.Switch, Value = true });
            await Task.Delay(50);
        }
        finally
        {
            await cts.CancelAsync();
            try { await bus.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }
        }

        Assert.That(container.Switch.Value, Is.True, "Routing must resume after InitValuesRegistered completes.");

        var diagnosis = await manager.GetDiagnosisAsync(CancellationToken.None);
        var routing = GetChild(diagnosis, "Routing");

        Assert.Multiple(() =>
        {
            Assert.That(GetRecordValue(diagnosis, "InitValuesRegisteredCompleted"), Is.EqualTo("True"));
            Assert.That(GetRecordValue(routing, "DroppedPreStageUpdates"), Is.EqualTo("1"));
            Assert.That(GetRecordValue(routing, "RoutedUpdates"), Is.EqualTo("1"));
            Assert.That(GetRecordValue(diagnosis, "ValuesRegisteredStageCompletedAtUtc"), Does.Contain("2026-07-31"));
        });
    }

    [Test]
    public async Task GetDiagnosisAsync_ReportsInitializationCounts()
    {
        var bus = CreateBus();
        var container = new TwoValueContainer();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 31, 9, 0, 0, TimeSpan.Zero));
        var manager = CreateManager(bus, timeProvider: timeProvider, containers: [container]);

        await manager.StartAsync(CancellationToken.None);

        var diagnosis = await manager.GetDiagnosisAsync(CancellationToken.None);
        var initialization = GetChild(diagnosis, "Initialization");

        Assert.Multiple(() =>
        {
            Assert.That(GetRecordValue(initialization, "ContainerCount"), Is.EqualTo("1"));
            Assert.That(GetRecordValue(initialization, "DiscoveredCount"), Is.EqualTo("2"));
            Assert.That(GetRecordValue(initialization, "InitializedCount"), Is.EqualTo("2"));
            Assert.That(GetRecordValue(initialization, "RegisteredCount"), Is.EqualTo("2"));
            Assert.That(GetRecordValue(diagnosis, "StartRequestedAtUtc"), Does.Contain("2026-07-31"));
        });
    }

    private static IDiagnosticResultNode GetChild(IDiagnosticResultNode node, string name)
        => node.Children.Single(child => child.Name == name);

    private static string? GetRecordValue(IDiagnosticResultNode node, string name)
        => node.Records.Single(record => record.Name == name).Value?.FormattedValue;

    // ── Shared helper ─────────────────────────────────────────────────────────

    private sealed class LambdaHandler<T>(Action<T> action) : IEventHandler<T> where T : IEvent
    {
        public ValueTask HandleAsync(T @event, CancellationToken cancellationToken = default)
        {
            action(@event);
            return ValueTask.CompletedTask;
        }
    }
}
