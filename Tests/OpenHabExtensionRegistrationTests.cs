using HomeCompanion.Abstractions;
using HomeCompanion.Integrations.OpenHab;
using HomeCompanion.Persistence;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SRF.Knx.Config;
using SRF.Knx.Core;
using SRF.Knx.Core.DPT;
using SRF.Knx.Core.Master;
using SRF.Network.OpenHab;
using SRF.Network.OpenHab.Client;
using SRF.Network.OpenHab.Items;

namespace HomeCompanion.Tests;

[TestFixture]
public class OpenHabExtensionRegistrationTests
{
    [Test]
    public async Task InitializeValues_UsesBusMappingAndPropertyNameMatching_WithMappingPriority()
    {
        var tempDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "OpenHabStateMapping.json"), "{\"ON\":\"true\",\"OFF\":\"false\"}");

            var container = new TestContainer();
            var stateManager = new CapturingStateInitializationManager();
            _ = CreateBackgroundService(
                stateManager,
                [container],
                new StubRestApiClient(
                [
                    new Item { Name = "MappedBoolItem", State = "ON" },
                    new Item { Name = "PropertyMatchOnly", State = "21" },
                    new Item { Name = nameof(TestContainer.SameNameAndMapping), State = "OFF" },
                ]),
                enableOpenHab: true,
                integrationOptions: new OpenHabIntegrationOptions
                {
                    EnablePropertyNameMatching = true,
                    StateMapFile = "OpenHabStateMapping.json",
                },
                new KnxSystemConfigOptions
                {
                    OpenHab = new KnxSystemConfigOptions.OpenHabOptions
                    {
                        TemplatesFolder = tempDir,
                    },
                });

            Assert.That(stateManager.Initialization, Is.Not.Null);

            await stateManager.Initialization!(CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(container.MappedBool.Value, Is.True);
                Assert.That(container.PropertyMatchOnly.Value, Is.EqualTo(21));
                Assert.That(container.SameNameAndMapping.Value, Is.False);
                Assert.That(container.SameNameAndMapping.InitializeCalls, Is.EqualTo(1));
            });
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public async Task InitializeValues_SkipsNullAndUndefStates()
    {
        var tempDir = CreateTempDir();
        try
        {
            var container = new NullAndUndefContainer();
            var stateManager = new CapturingStateInitializationManager();
            _ = CreateBackgroundService(
                stateManager,
                [container],
                new StubRestApiClient(
                [
                    new Item { Name = "NullValueItem", State = "NULL" },
                    new Item { Name = "UndefValueItem", State = "UNDEF" },
                ]),
                enableOpenHab: true,
                integrationOptions: new OpenHabIntegrationOptions(),
                new KnxSystemConfigOptions
                {
                    OpenHab = new KnxSystemConfigOptions.OpenHabOptions
                    {
                        TemplatesFolder = tempDir,
                    },
                });

            Assert.That(stateManager.Initialization, Is.Not.Null);

            await stateManager.Initialization!(CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(container.NullValue.Status.HasFlag(ValueStatus.Initialized), Is.False);
                Assert.That(container.UndefValue.Status.HasFlag(ValueStatus.Initialized), Is.False);
                Assert.That(container.NullValue.InitializeCalls, Is.EqualTo(0));
                Assert.That(container.UndefValue.InitializeCalls, Is.EqualTo(0));
            });
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public async Task InitializeValues_SkipsWhenOpenHabDisabled()
    {
        var tempDir = CreateTempDir();
        try
        {
            var container = new TestContainer();
            var stateManager = new CapturingStateInitializationManager();
            var restClient = new StubRestApiClient([new Item { Name = "MappedBoolItem", State = "ON" }]);

            _ = CreateBackgroundService(
                stateManager,
                [container],
                restClient,
                enableOpenHab: false,
                integrationOptions: new OpenHabIntegrationOptions { EnablePropertyNameMatching = true },
                new KnxSystemConfigOptions
                {
                    OpenHab = new KnxSystemConfigOptions.OpenHabOptions
                    {
                        TemplatesFolder = tempDir,
                    },
                });

            Assert.That(stateManager.Initialization, Is.Not.Null);

            await stateManager.Initialization!(CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(restClient.GetItemsCalls, Is.EqualTo(0));
                Assert.That(container.MappedBool.Status.HasFlag(ValueStatus.Initialized), Is.False);
                Assert.That(container.PropertyMatchOnly.Status.HasFlag(ValueStatus.Initialized), Is.False);
                Assert.That(container.SameNameAndMapping.Status.HasFlag(ValueStatus.Initialized), Is.False);
            });
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"homecompanion-openhab-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static OpenHabExtensionRegistrationBackgroundService CreateBackgroundService(
        CapturingStateInitializationManager stateManager,
        IEnumerable<IValuesContainer> containers,
        StubRestApiClient restApiClient,
        bool enableOpenHab,
        OpenHabIntegrationOptions integrationOptions,
        KnxSystemConfigOptions knxConfiguration)
    {
        // Create a stub converter - won't be used in these tests since there's no KNX integration
        var converter = new OpenHabStateConverter(
            new StubKnxSystemConfiguration(),
            new StubMasterDataProvider(),
            NullLogger<OpenHabStateConverter>.Instance);

        return new OpenHabExtensionRegistrationBackgroundService(
            new StubLifeCycleSync(),
            stateManager,
            containers,
            restApiClient,
            Options.Create(new EventBusClientOptions { Enable = enableOpenHab }),
            Options.Create(integrationOptions),
            Options.Create(knxConfiguration),
            converter,
            NullLogger<OpenHabExtensionRegistrationBackgroundService>.Instance);
    }

    private class StubKnxSystemConfiguration : IKnxSystemConfiguration
    {
        public DptBase GetDpt(GroupAddress groupAddress) => throw new NotImplementedException();
        public void ClearCache() { }
        public DptBase GetDptFromId(string dptId) => throw new NotImplementedException();
        public GroupAddressMeta GetGroupAddressMeta(GroupAddress groupAddress) => throw new NotImplementedException();
        public GroupAddressMeta GetGroupAddressMeta(string name) => throw new NotImplementedException();
        public GroupAddressMeta? GetGroupAddressMetaOrNull(GroupAddress groupAddress) => null;
        public GroupAddressMeta? GetGroupAddressMetaOrNull(string name) => null;
        public bool TryGetGroupAddressMeta(GroupAddress ga, out GroupAddressMeta? gaConfig) { gaConfig = null; return false; }
    }

    private class StubMasterDataProvider : IKnxMasterDataProvider
    {
        public KnxMasterData GetMasterData() => new KnxMasterData();
    }

    private sealed class StubLifeCycleSync : IHomeCompanionLifeCycleSynchronization
    {
        public Task AwaitBusesConnectedAsync(TimeSpan timeout, CancellationToken token = default) => Task.CompletedTask;

        public Task WaitForInitializationStageCompletedAsync(AppInitializationStage level, TimeSpan timeout, CancellationToken token = default)
            => Task.CompletedTask;

        public Task SignalInitializationStageCompletedAsync(AppInitializationStage level) => Task.CompletedTask;

        public bool IsInitializationStageCompleted(AppInitializationStage level)
        {
            throw new NotImplementedException();
        }

        public bool IsAllUpToStageCompleted(AppInitializationStage level)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class CapturingStateInitializationManager : HomeCompanion.Persistence.IStateInitializationManager
    {
        public StateInitializationDelegate? Initialization { get; private set; }

        public AppInitializationStage CurrentStage => AppInitializationStage.Default;

        public Task InitializeStateAsync(CancellationToken token = default) => Task.CompletedTask;

        public void RegisterInitialization(AppInitializationStage stage, StateInitializationDelegate initialization)
        {
            if (stage == AppInitializationStage.InitRetrieveFromEnvironment)
                Initialization = initialization;
        }

        public void RemoveInitialization(AppInitializationStage stage, StateInitializationDelegate initialization) { }

        public void RegisterSave(StateInitializationDelegate save) { }

        public void RemoveSave(StateInitializationDelegate save) { }

        public Task SaveStateAsync(CancellationToken token = default) => Task.CompletedTask;
    }

    private sealed class StubRestApiClient(Item[] items) : IRestApiClient
    {
        public int GetItemsCalls { get; private set; }

        public Task<Item[]> GetItemsAsync(CancellationToken cancel)
        {
            GetItemsCalls++;
            return Task.FromResult(items);
        }

        public Task SetItemStateAsync(string itemName, string state, CancellationToken cancel = default)
            => Task.CompletedTask;
    }

    private sealed class CountingValue<T>(Microsoft.Extensions.Logging.ILogger<ValueBase<T>> logger) : ValueBase<T>(logger)
    {
        public int InitializeCalls { get; private set; }

        public override bool InitializeValue(object value, AppInitializationStage stage)
        {
            InitializeCalls++;
            return base.InitializeValue(value, stage);
        }
    }

    private sealed class TestContainer : IValuesContainer
    {
        public CountingValue<bool> MappedBool { get; } = new(NullLogger<ValueBase<bool>>.Instance)
        {
            BusMappings = new() { [OpenHabBusEndpointMapping.BusId] = new OpenHabBusEndpointMapping("MappedBoolItem") },
        };

        public CountingValue<int> PropertyMatchOnly { get; } = new(NullLogger<ValueBase<int>>.Instance);

        public CountingValue<bool> SameNameAndMapping { get; } = new(NullLogger<ValueBase<bool>>.Instance)
        {
            BusMappings = new() { [OpenHabBusEndpointMapping.BusId] = new OpenHabBusEndpointMapping(nameof(SameNameAndMapping)) },
        };

        public IEnumerable<IValue> GetValues() => [MappedBool, PropertyMatchOnly, SameNameAndMapping];
    }

    private sealed class NullAndUndefContainer : IValuesContainer
    {
        public CountingValue<bool> NullValue { get; } = new(NullLogger<ValueBase<bool>>.Instance)
        {
            BusMappings = new() { [OpenHabBusEndpointMapping.BusId] = new OpenHabBusEndpointMapping("NullValueItem") },
        };

        public CountingValue<bool> UndefValue { get; } = new(NullLogger<ValueBase<bool>>.Instance)
        {
            BusMappings = new() { [OpenHabBusEndpointMapping.BusId] = new OpenHabBusEndpointMapping("UndefValueItem") },
        };

        public IEnumerable<IValue> GetValues() => [NullValue, UndefValue];
    }
}
