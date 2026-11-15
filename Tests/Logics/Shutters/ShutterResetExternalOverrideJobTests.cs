using HomeCompanion.Base.Model;
using HomeCompanion.Base.Utilities;
using HomeCompanion.Logics.Shutters.Runtimes;
using HomeCompanion.Logics.Shutters.ShutterAutomation;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Quartz;
using UnitsNet;

namespace HomeCompanion.Tests.Logics.Shutters;

[TestFixture]
public class ShutterResetExternalOverrideJobTests
{
    [Test]
    public void Execute_WhenJobDataMapDoesNotContainShutterKey_DoesNotThrow()
    {
        var job = new ShutterResetExternalOverrideJob(
            Mock.Of<IRuntimesProvider>(),
            NullLogger<ShutterResetExternalOverrideJob>.Instance,
            TimeProvider.System,
            Mock.Of<IQueueFeeder<ShutterAutomationComputationTriggerContext>>());

        var context = new Mock<IJobExecutionContext>();
        context.SetupGet(c => c.MergedJobDataMap).Returns(new JobDataMap());
        context.SetupGet(c => c.JobDetail).Returns(Mock.Of<IJobDetail>(d => d.Key == new JobKey("TestJob", "TestGroup")));
        context.SetupGet(c => c.Trigger).Returns(Mock.Of<ITrigger>(t => t.Key == new TriggerKey("TestTrigger", "TestGroup")));

        Assert.DoesNotThrowAsync(async () => await job.Execute(context.Object));
    }

    [Test]
    public async Task Execute_WithInvalidShutterKey_DoesNotEnqueue()
    {
        var queueFeeder = new Mock<IQueueFeeder<ShutterAutomationComputationTriggerContext>>();
        var runtimesProvider = Mock.Of<IRuntimesProvider>();
        var job = new ShutterResetExternalOverrideJob(
            runtimesProvider,
            NullLogger<ShutterResetExternalOverrideJob>.Instance,
            TimeProvider.System,
            queueFeeder.Object);

        var context = new Mock<IJobExecutionContext>();
        context.SetupGet(c => c.MergedJobDataMap).Returns(new JobDataMap
        {
            ["ShutterKey"] = "invalid-shutter-key"
        });
        context.SetupGet(c => c.JobDetail).Returns(Mock.Of<IJobDetail>(d => d.Key == new JobKey("TestJob", "TestGroup")));
        context.SetupGet(c => c.Trigger).Returns(Mock.Of<ITrigger>(t => t.Key == new TriggerKey("TestTrigger", "TestGroup")));

        await job.Execute(context.Object);

        queueFeeder.Verify(q => q.EnqueueAsync(It.IsAny<ShutterAutomationComputationTriggerContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Execute_WithValidShutterKey_ResetsOverrideAndEnqueuesShutterSpecificSlowTrigger()
    {
        var fixture = ShutterAutomationTestFixture.Create();
        await fixture.StartAsync();

        var runtime = fixture.RuntimesProvider.ShutterRuntimes.Values.First();
        var shutter = runtime.ShutterContext.Shutter;
        if (shutter.PositionValue is not IValue<Ratio> positionValue)
        {
            Assert.Fail("Test requires a positional shutter value.");
            return;
        }

        var captured = new List<ShutterAutomationComputationTriggerContext>();
        var queueFeeder = new CapturingQueueFeeder(captured);

        var job = new ShutterResetExternalOverrideJob(
            fixture.RuntimesProvider,
            NullLogger<ShutterResetExternalOverrideJob>.Instance,
            fixture.TimeProvider,
            queueFeeder);

        positionValue.Write(Ratio.FromPercent(23));
        var overrideActivated = SpinWait.SpinUntil(() => runtime.IsExternalOverrideActive, TimeSpan.FromSeconds(2));
        Assert.That(overrideActivated, Is.True, "Expected external override to activate after external shutter write.");

        var context = new Mock<IJobExecutionContext>();
        context.SetupGet(c => c.MergedJobDataMap).Returns(new JobDataMap
        {
            ["ShutterKey"] = runtime.ShutterKey.Key
        });
        context.SetupGet(c => c.JobDetail).Returns(Mock.Of<IJobDetail>(d => d.Key == new JobKey("TestJob", "TestGroup")));
        context.SetupGet(c => c.Trigger).Returns(Mock.Of<ITrigger>(t => t.Key == new TriggerKey("TestTrigger", "TestGroup")));

        await job.Execute(context.Object);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(runtime.IsExternalOverrideActive, Is.False, "Job should reset manual external override state.");
            Assert.That(captured.Count, Is.EqualTo(1), "Job should enqueue exactly one recomputation trigger.");
            Assert.That(captured[0].IsShutterScoped, Is.True);
            Assert.That(captured[0].IsRoomScoped, Is.False);
            Assert.That(captured[0].IsGlobalScope, Is.False);
            Assert.That(captured[0].Urgency, Is.EqualTo(ShutterAutomationComputationTriggerUrgency.Slow));
            Assert.That(captured[0].GetShutterKeys().Single(), Is.EqualTo(runtime.ShutterKey));
        }
    }

    private sealed class CapturingQueueFeeder(List<ShutterAutomationComputationTriggerContext> captured)
        : IQueueFeeder<ShutterAutomationComputationTriggerContext>
    {
        public void Enqueue(ShutterAutomationComputationTriggerContext trigger)
        {
            captured.Add(trigger);
        }

        public Task EnqueueAsync(ShutterAutomationComputationTriggerContext trigger, CancellationToken token)
        {
            captured.Add(trigger);
            return Task.CompletedTask;
        }
    }
}
