using HomeCompanion.Logics.Garden;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;
using Moq;

namespace HomeCompanion.Tests;

[TestFixture]
public class RainServiceTests
{
    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    [Test]
    public void PositiveTick_EmitsEvent_AndRecomputesAggregates()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var service = new RainService(time);
        RainEventArgs? raised = null;
        service.RainTick += (_, args) => raised = args;

        service.ProcessRainTick(0.5, time.GetUtcNow());

        Assert.That(raised, Is.Not.Null);
        Assert.That(raised!.Quantity, Is.EqualTo(0.5).Within(0.0001));
        Assert.That(service.GetAggregate(RainAggregateWindow.Hours24), Is.EqualTo(0.5).Within(0.0001));
        Assert.That(service.GetAggregate(RainAggregateWindow.Hours48), Is.EqualTo(0.5).Within(0.0001));
    }

    [Test]
    public async Task RainAggregateRefreshJob_RecomputesAggregates_WithoutNewRainTicks()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var service = new RainService(time);
        var job = new RainAggregateRefreshJob(service, time, NullLogger<RainAggregateRefreshJob>.Instance);

        service.ProcessRainTick(2.0, time.GetUtcNow().AddHours(-60));
        time.Advance(TimeSpan.FromHours(2));

        await job.Execute(Mock.Of<IJobExecutionContext>());

        Assert.That(service.GetAggregate(RainAggregateWindow.Hours24), Is.EqualTo(0.0).Within(0.0001));
        Assert.That(service.GetAggregate(RainAggregateWindow.Hours48), Is.EqualTo(0.0).Within(0.0001));
    }

    [Test]
    public void ZeroAndNegativeValues_AreIgnored()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var service = new RainService(time);

        service.ProcessRainTick(0, time.GetUtcNow());
        service.ProcessRainTick(-1, time.GetUtcNow());

        Assert.That(service.AcceptedTickCount, Is.EqualTo(0));
        Assert.That(service.RejectedTickCount, Is.EqualTo(2));
        Assert.That(service.GetAggregate(RainAggregateWindow.Hours24), Is.EqualTo(0));
    }

    [Test]
    public void CorrectionFactor_IsAppliedBeforeStorageAndAggregation()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var service = new RainService(time)
        {
            CorrectionFactor = 2.0,
        };

        service.ProcessRainTick(1.5, time.GetUtcNow());

        Assert.That(service.GetAggregate(RainAggregateWindow.Hours24), Is.EqualTo(3.0).Within(0.0001));
        Assert.That(service.LastTick?.Quantity, Is.EqualTo(3.0).Within(0.0001));
    }

    [Test]
    public void History_IsPruned_AfterTwoWeeks_AndAggregateWindows_RemainCorrect()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(start);
        var service = new RainService(time);

        service.ProcessRainTick(2.0, start.AddDays(-15));
        service.ProcessRainTick(3.0, start.AddHours(-12));
        service.ProcessRainTick(4.0, start.AddHours(-2));

        Assert.That(service.GetAggregate(RainAggregateWindow.Hours24), Is.EqualTo(7.0).Within(0.0001));
        Assert.That(service.GetAggregate(RainAggregateWindow.Hours48), Is.EqualTo(7.0).Within(0.0001));
        Assert.That(service.GetAggregate(RainAggregateWindow.Hours72), Is.EqualTo(7.0).Within(0.0001));
        Assert.That(service.GetAggregate(RainAggregateWindow.Days7), Is.EqualTo(7.0).Within(0.0001));
        Assert.That(service.GetAggregate(RainAggregateWindow.Days14), Is.EqualTo(7.0).Within(0.0001));
        Assert.That(service.QuantCount, Is.EqualTo(2));
    }

    [Test]
    public void SubscriberException_IsIsolated()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var service = new RainService(time);
        RainEventArgs? observed = null;

        service.RainTick += (_, _) => throw new InvalidOperationException("broken subscriber");
        service.RainTick += (_, args) => observed = args;

        service.ProcessRainTick(1.25, time.GetUtcNow());

        Assert.That(observed, Is.Not.Null);
        Assert.That(service.AcceptedTickCount, Is.EqualTo(1));
    }
}
