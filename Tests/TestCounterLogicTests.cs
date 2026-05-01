/*
using HomeCompanion.Abstractions;
using HomeCompanion.Base.Values;
using HomeCompanion.Logics;

namespace HomeCompanion.Tests;

[TestFixture]
public class TestCounterLogicTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class NullPublisher : IEventPublisher
    {
        public ValueTask PublishAsync(IEvent @event, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class NullSubscriber : IEventSubscriber
    {
        public void Subscribe<T>(IEventHandler<T> handler) where T : IEvent { }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    private sealed class StubTestCounterValues(
        IValue<bool> testSwitch,
        IValue<int> testCount,
        IValue<double> testDuration) : ITestCounterValues
    {
        public IValue<bool> TestSwitch { get; } = testSwitch;
        public IValue<int> TestCount { get; } = testCount;
        public IValue<double> TestDuration { get; } = testDuration;
    }

    // ── Factory ───────────────────────────────────────────────────────────────

    private static (TestCounterLogic logic, ValueBase<bool> sw, ValueBase<int> count, ValueBase<double> duration, FakeTimeProvider time)
        CreateSut(DateTimeOffset? startTime = null)
    {
        var sw = new ValueBase<bool>();
        var count = new ValueBase<int>();
        var duration = new ValueBase<double>();
        var time = new FakeTimeProvider(startTime ?? new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var logic = new TestCounterLogic(
            new StubTestCounterValues(sw, count, duration),
            new NullPublisher(),
            new NullSubscriber(),
            time);
        return (logic, sw, count, duration, time);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task OnOff_cycle_increments_count_and_writes_duration()
    {
        var (logic, sw, count, duration, time) = CreateSut();
        await logic.InitializeAsync();

        sw.Write(true);
        time.Advance(TimeSpan.FromSeconds(10));
        sw.Write(false);

        Assert.Multiple(() =>
        {
            Assert.That(count.Value, Is.EqualTo(1));
            Assert.That(duration.Value, Is.EqualTo(10.0));
        });
    }

    [Test]
    public async Task Second_cycle_accumulates_count_correctly()
    {
        var (logic, sw, count, duration, time) = CreateSut();
        await logic.InitializeAsync();

        sw.Write(true);
        time.Advance(TimeSpan.FromSeconds(5));
        sw.Write(false);

        sw.Write(true);
        time.Advance(TimeSpan.FromSeconds(20));
        sw.Write(false);

        Assert.Multiple(() =>
        {
            Assert.That(count.Value, Is.EqualTo(2));
            Assert.That(duration.Value, Is.EqualTo(20.0));
        });
    }

    [Test]
    public async Task Off_without_prior_on_is_ignored()
    {
        var (logic, sw, count, duration, _) = CreateSut();
        await logic.InitializeAsync();

        // Write false when already false — no Changed event fires (old == new),
        // but even if it did, _switchOnAt would be null and nothing should be written.
        sw.Write(false);

        Assert.Multiple(() =>
        {
            Assert.That(count.Value, Is.EqualTo(0));
            Assert.That(duration.Value, Is.EqualTo(0.0));
        });
    }

    [Test]
    public async Task InitializeAsync_is_idempotent()
    {
        var (logic, sw, count, _, time) = CreateSut();
        await logic.InitializeAsync();
        await logic.InitializeAsync(); // second call must not double-subscribe

        sw.Write(true);
        time.Advance(TimeSpan.FromSeconds(3));
        sw.Write(false);

        // count must be 1, not 2 (would be 2 if handler was registered twice)
        Assert.That(count.Value, Is.EqualTo(1));
    }

    [Test]
    public async Task Duration_reflects_actual_on_time()
    {
        var (logic, sw, count, duration, time) = CreateSut();
        await logic.InitializeAsync();

        sw.Write(true);
        time.Advance(TimeSpan.FromSeconds(7.5));
        sw.Write(false);

        Assert.That(duration.Value, Is.EqualTo(7.5).Within(0.001));
    }
}
*/