using HomeCompanion.Abstractions;
using HomeCompanion.Core;
using HomeCompanion.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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

        return new HomeCompanionLifeCycleSynchronization(
            services.BuildServiceProvider(),
            NullLogger<HomeCompanionLifeCycleSynchronization>.Instance,
            timeProvider ?? TimeProvider.System);
    }

    [Test]
    public async Task WaitForInitializationStageCompletedAsync_DoesNotSignalStageOnTimeout()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.Zero));
        var sync = CreateSync(timeProvider);

        Assert.That(sync.IsInitializationStageCompleted(AppInitializationStage.InitValuesRegistered), Is.False);

        var ex = Assert.ThrowsAsync<TimeoutException>(async () =>
            await sync.WaitForInitializationStageCompletedAsync(
                AppInitializationStage.InitValuesRegistered,
                TimeSpan.FromMilliseconds(20),
                CancellationToken.None));

        Assert.That(ex, Is.Not.Null);
        Assert.That(sync.IsInitializationStageCompleted(AppInitializationStage.InitValuesRegistered), Is.False);

        var diagnosis = await sync.GetDiagnosisAsync(CancellationToken.None);
        var stages = GetChild(diagnosis, "Stages");
        var initValuesRegistered = GetChild(stages, AppInitializationStage.InitValuesRegistered.ToString());

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

        await sync.SignalInitializationStageCompletedAsync(AppInitializationStage.InitValuesRegistered);
        timeProvider.Advance(TimeSpan.FromMinutes(5));
        await sync.SignalInitializationStageCompletedAsync(AppInitializationStage.InitValuesRegistered);

        Assert.That(sync.IsInitializationStageCompleted(AppInitializationStage.InitValuesRegistered), Is.True);

        var diagnosis = await sync.GetDiagnosisAsync(CancellationToken.None);
        var stages = GetChild(diagnosis, "Stages");
        var initValuesRegistered = GetChild(stages, AppInitializationStage.InitValuesRegistered.ToString());

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
