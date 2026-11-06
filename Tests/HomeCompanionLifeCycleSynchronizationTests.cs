using HomeCompanion.Abstractions;
using HomeCompanion.Base.Utilities;
using HomeCompanion.Core;
using HomeCompanion.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Reflection;

namespace HomeCompanion.Tests;

[TestFixture]
public class HomeCompanionLifeCycleSynchronizationTests
{
    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _now = utcNow;

        public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }

    private static HomeCompanionLifeCycleSynchronization CreateSync(TimeProvider? timeProvider = null, params IConnectivityProvider[] providers)
    {
        var services = new ServiceCollection();
        foreach (var provider in providers)
            services.AddSingleton(provider);

        var lifeTimeMock = new Mock<IHostApplicationLifetime>();
        //var cts = new CancellationTokenSource();

        return new HomeCompanionLifeCycleSynchronization(
            services.BuildServiceProvider(),
            lifeTimeMock.Object,
            NullLogger<HomeCompanionLifeCycleSynchronization>.Instance,
            timeProvider ?? TimeProvider.System);
    }

    [Test]
    public async Task WaitForInitializationStageCompletedAsync_DoesNotSignalStageOnTimeout()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.Zero));
        var sync = CreateSync(timeProvider);

        Assert.That(sync.IsLifeCycleStageCompleted(AppLifeCycleStage.InitValuesRegistered), Is.False);

        var ex = Assert.ThrowsAsync<TimeoutException>(async () =>
            await sync.WaitForInitializationStageCompletedAsync(
                AppLifeCycleStage.InitValuesRegistered,
                TimeSpan.FromMilliseconds(20),
                CancellationToken.None));

        Assert.That(ex, Is.Not.Null);
        Assert.That(sync.IsLifeCycleStageCompleted(AppLifeCycleStage.InitValuesRegistered), Is.False);

        var diagnosis = await sync.GetDiagnosisAsync(CancellationToken.None);
        var stages = GetChild(diagnosis, "Stages");
        var initValuesRegistered = GetChild(stages, AppLifeCycleStage.InitValuesRegistered.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(GetRecordValue(diagnosis, "WaitTimeouts"), Is.EqualTo("1"));
            Assert.That(GetRecordValue(initValuesRegistered, "Completed"), Is.EqualTo("False"));
            Assert.That(GetRecordValue(initValuesRegistered, "CompletedAt"), Is.EqualTo("<null>"));
        });
    }

    [Test]
    public async Task SignalInitializationStageCompletedAsync_IsIdempotent()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 31, 11, 0, 0, TimeSpan.Zero));
        var sync = CreateSync(timeProvider);
        var signaller = new object();

        sync.RegisterRequiredSignaller(AppLifeCycleStage.InitValuesRegistered, signaller);

        await sync.SignalInitializationStageCompletedAsync(AppLifeCycleStage.InitValuesRegistered, signaller);
        timeProvider.Advance(TimeSpan.FromMinutes(5));
        await sync.SignalInitializationStageCompletedAsync(AppLifeCycleStage.InitValuesRegistered, signaller);

        var bgRunner = sync.StartAsync(CancellationToken.None); // start the background runner to process the stage completion
        await sync.WaitForInitializationStageCompletedAsync(
            AppLifeCycleStage.InitValuesRegistered,
            TimeSpan.FromMilliseconds(200),
            CancellationToken.None);

        Assert.That(sync.IsLifeCycleStageCompleted(AppLifeCycleStage.InitValuesRegistered), Is.True);

        var diagnosis = await sync.GetDiagnosisAsync(CancellationToken.None);
        var stages = GetChild(diagnosis, "Stages");
        var initValuesRegistered = GetChild(stages, AppLifeCycleStage.InitValuesRegistered.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(GetRecordValue(diagnosis, "SignalCalls"), Is.EqualTo("2"));
            Assert.That(GetRecordValue(diagnosis, "DuplicateSignalCalls"), Is.EqualTo("1"));
            Assert.That(GetRecordValue(initValuesRegistered, "Completed"), Is.EqualTo("True"));
            Assert.That(GetRecordValue(initValuesRegistered, "CompletedAt"), Does.Contain("2026-07-31"));
            Assert.That(GetRecordValue(initValuesRegistered, "CompletedAt"), Does.Contain("11:00:00"));
        });
    }

    [Test]
    public async Task StopAsync_CompletesTerminationStageEvenWhenRequiredSignallerNeverSignals()
    {
        var sync = CreateSync();
        var signaller = new object();
        sync.RegisterRequiredSignaller(AppLifeCycleStage.TerminateLogics, signaller);

        await sync.StartAsync(CancellationToken.None);
        await sync.StopAsync(CancellationToken.None);

        Assert.That(sync.IsLifeCycleStageCompleted(AppLifeCycleStage.TerminateLogics), Is.True);
    }

    [Test]
    public async Task WaitForExecutionLoopTriggerAsync_TimeoutDoesNotLeaveSemaphoreWaiters()
    {
        var trigger = new SemaphoreSlim(0, 1);
        var method = typeof(HomeCompanionLifeCycleSynchronization)
            .GetMethod("WaitForExecutionLoopTriggerAsync", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);

        await (Task)method.Invoke(null, [trigger, TimeSpan.FromMilliseconds(50), CancellationToken.None])!;

        var waitCountField = typeof(SemaphoreSlim).GetField("m_waitCount", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(waitCountField, Is.Not.Null);
        Assert.That(waitCountField.GetValue(trigger), Is.EqualTo(0));
    }

    [Test]
    public async Task InspectableAsyncQueue_DequeueAsyncTimeout_DoesNotLeaveSemaphoreWaiters()
    {
        var queue = new InspectableAsyncQueue<int>();
        var result = await queue.DequeueAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.That(result.Success, Is.False);

        var semaphoreField = typeof(InspectableAsyncQueue<int>).GetField("_semaphore", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(semaphoreField, Is.Not.Null);

        var semaphore = (SemaphoreSlim)semaphoreField!.GetValue(queue)!;
        var waitCountField = typeof(SemaphoreSlim).GetField("m_waitCount", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(waitCountField, Is.Not.Null);
        Assert.That(waitCountField.GetValue(semaphore), Is.EqualTo(0));
    }

    [Test]
    public async Task AwaitBusesConnectedAsync_NoEnabledProviders_Completes()
    {
        var sync = CreateSync();

        Assert.DoesNotThrowAsync(async () =>
            await sync.AwaitBusesConnectedAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None));
    }

    private static IDiagnosticResultNode GetChild(IDiagnosticResultNode node, string name)
        => node.Children.Single(child => child.Name == name);

    private static string? GetRecordValue(IDiagnosticResultNode node, string name)
        => node.Records.Single(record => record.Name == name).Value?.FormattedValue;
}
