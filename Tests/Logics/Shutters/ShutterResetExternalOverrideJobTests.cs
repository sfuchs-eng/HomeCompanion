using HomeCompanion.Base.Model;
using HomeCompanion.Base.Utilities;
using HomeCompanion.Logics.Shutters;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Quartz;

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
}
