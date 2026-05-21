using HomeCompanion.Abstractions;
using HomeCompanion.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests;

[TestFixture]
public class HomeCompanionLifeCycleSynchronizationTests
{
    private static HomeCompanionLifeCycleSynchronization CreateSync(params IConnectivityProvider[] providers)
    {
        var services = new ServiceCollection();
        foreach (var provider in providers)
            services.AddSingleton(provider);

        return new HomeCompanionLifeCycleSynchronization(
            services.BuildServiceProvider(),
            NullLogger<HomeCompanionLifeCycleSynchronization>.Instance);
    }

    [Test]
    public async Task WaitForInitializationStageCompletedAsync_DoesNotSignalStageOnTimeout()
    {
        var sync = CreateSync();

        Assert.That(sync.IsInitializationStageCompleted(AppInitializationStage.InitValuesRegistered), Is.False);

        var ex = Assert.ThrowsAsync<TimeoutException>(async () =>
            await sync.WaitForInitializationStageCompletedAsync(
                AppInitializationStage.InitValuesRegistered,
                TimeSpan.FromMilliseconds(20),
                CancellationToken.None));

        Assert.That(ex, Is.Not.Null);
        Assert.That(sync.IsInitializationStageCompleted(AppInitializationStage.InitValuesRegistered), Is.False);
    }

    [Test]
    public async Task SignalInitializationStageCompletedAsync_IsIdempotent()
    {
        var sync = CreateSync();

        await sync.SignalInitializationStageCompletedAsync(AppInitializationStage.InitValuesRegistered);
        await sync.SignalInitializationStageCompletedAsync(AppInitializationStage.InitValuesRegistered);

        Assert.That(sync.IsInitializationStageCompleted(AppInitializationStage.InitValuesRegistered), Is.True);
    }

    [Test]
    public async Task AwaitBusesConnectedAsync_NoEnabledProviders_Completes()
    {
        var sync = CreateSync();

        Assert.DoesNotThrowAsync(async () =>
            await sync.AwaitBusesConnectedAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None));
    }
}
