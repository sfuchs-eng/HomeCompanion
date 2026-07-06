using System.Collections.Concurrent;
using HomeCompanion;
using HomeCompanion.Abstractions;
using HomeCompanion.Core;
using HomeCompanion.Core.Events;
using HomeCompanion.Events;
using HomeCompanion.Integrations.Knx;
using HomeCompanion.Persistence;
using HomeCompanion.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SRF.Knx.Config;
using SRF.Knx.Config.Domain;
using SRF.Knx.Config.ETS5;
using SRF.Knx.Core;
using SRF.Knx.Core.DPT;
using SRF.Knx.Core.Master;
using SRF.Network.Knx;
using SRF.Network.Knx.Connection;
using SRF.Network.Knx.Dpt;
using SRF.Network.Knx.Messages;

namespace HomeCompanion.Tests.Values;

[TestFixture]
public class KnxDpstScaledNumericIValueTests
{
    private const string ScaledGa = "2/1/1";
    private const string NonScaledGa = "2/1/2";
    private const double Dpst5_1Coefficient = 100.0 / 255.0;

    [TestCase(255, 100.0)]
    [TestCase(0, 0.0)]
    [TestCase(127, 127.0 * Dpst5_1Coefficient)]
    public async Task InboundWrite_Dpst5_1_UpdatesIValueDoubleUsingCoefficient(byte raw, double expected)
    {
        var bus = CreateBus();
        var resolver = CreateResolver(new Dictionary<string, string> { [ScaledGa] = "DPST-5-1" });
        var knxBus = new StubKnxBus();
        var connection = CreateConnection(knxBus, resolver);
        var container = new DoubleKnxValueContainer(ScaledGa, "DPST-5-1");
        var provider = CreateProvider(connection, bus, container, resolver);

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);

            knxBus.RaiseMessageReceived(new GroupEventArgs
            {
                DestinationAddress = new GroupAddress(ScaledGa),
                SourceAddress = new IndividualAddress("1.1.10"),
                EventType = GroupEventType.ValueWrite,
                Value = new GroupValue([raw]),
            });

            await Task.Delay(200);
            await provider.StopAsync(CancellationToken.None);
        });

        Assert.Multiple(() =>
        {
            Assert.That(container.Percent.Value, Is.EqualTo(expected).Within(0.51),
                $"Raw value {raw} for DPST-5-1 should be converted to percentage via coefficient {Dpst5_1Coefficient}.");
            Assert.That(container.Percent.Status.HasFlag(ValueStatus.Initialized), Is.True);
            Assert.That(container.Percent.Status.HasFlag(ValueStatus.Live), Is.True);
        });
    }

    [TestCase(100.0, 255)]
    [TestCase(0.0, 0)]
    [TestCase(49.8, 127)]
    public async Task ValueWrite_Dpst5_1_SendsExpectedRawKnxGroupValue(double valueToWrite, byte expectedRaw)
    {
        var bus = CreateBus();
        var resolver = CreateResolver(new Dictionary<string, string> { [ScaledGa] = "DPST-5-1" });
        var knxBus = new StubKnxBus();
        var connection = CreateConnection(knxBus, resolver);
        var container = new DoubleKnxValueContainer(ScaledGa, "DPST-5-1");
        var provider = CreateProvider(connection, bus, container, resolver);

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);
            knxBus.SentMessages.Clear();

            container.Percent.Write(valueToWrite);

            await Task.Delay(200);
            await provider.StopAsync(CancellationToken.None);
        });

        var writeMessage = knxBus.SentMessages.LastOrDefault(m =>
            m.EventType == GroupEventType.ValueWrite &&
            m.DestinationAddress.ToString() == ScaledGa);

        Assert.That(writeMessage, Is.Not.Null,
            $"Expected outbound KNX ValueWrite for group address {ScaledGa}.");
        Assert.That(writeMessage!.Value.Value, Is.EqualTo(new byte[] { expectedRaw }),
            $"Writing {valueToWrite} on DPST-5-1 should encode to raw byte {expectedRaw}.");
    }

    [Test]
    public async Task NonScaledDpst5_4_WithIValueDouble_DoesNotRouteAsTypedDouble()
    {
        var bus = CreateBus();
        var resolver = CreateResolver(new Dictionary<string, string> { [NonScaledGa] = "DPST-5-4" });
        var knxBus = new StubKnxBus();
        var connection = CreateConnection(knxBus, resolver);
        var container = new DoubleKnxValueContainer(NonScaledGa, "DPST-5-4");
        var provider = CreateProvider(connection, bus, container, resolver);

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);

            // Inbound: DPST-5-4 decodes to byte (non-scaled), so ValueBase<double> rejects the update type.
            knxBus.RaiseMessageReceived(new GroupEventArgs
            {
                DestinationAddress = new GroupAddress(NonScaledGa),
                SourceAddress = new IndividualAddress("1.1.11"),
                EventType = GroupEventType.ValueWrite,
                Value = new GroupValue([128]),
            });

            await Task.Delay(200);

            Assert.Multiple(() =>
            {
                Assert.That(container.Percent.Value, Is.EqualTo(0.0),
                    "Value should remain unchanged because non-scaled DPST-5-4 inbound value is byte, not double.");
                Assert.That(container.Percent.Status.HasFlag(ValueStatus.Error), Is.True,
                    "Type mismatch should set error status on the value.");
            });

            // Outbound: provider cannot encode double directly to non-scaled byte DPT and skips send.
            knxBus.SentMessages.Clear();
            container.Percent.Write(128.0);

            await Task.Delay(200);
            await provider.StopAsync(CancellationToken.None);
        });

        Assert.That(knxBus.SentMessages, Has.None.Matches<IKnxMessage>(m =>
            m.EventType == GroupEventType.ValueWrite &&
            m.DestinationAddress.ToString() == NonScaledGa),
            "Non-scaled DPST-5-4 with IValue<double> should not produce outbound KNX write due to DPT type mismatch.");
    }

    private static EventBus CreateBus() => new(NullLogger<EventBus>.Instance);

    private static async Task RunWithBusAsync(EventBus bus, Func<Task> action, int drainMs = 200)
    {
        using var cts = new CancellationTokenSource();
        await bus.StartAsync(cts.Token);
        await action();
        await Task.Delay(drainMs);
        await cts.CancelAsync();
        try { await bus.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }
    }

    private static KnxConnection CreateConnection(StubKnxBus knxBus, IDptResolver resolver)
        => new(
            new KnxLibraryInitializationStub(),
            knxBus,
            Options.Create(new KnxConnectionOptions()),
            NullLogger<KnxConnection>.Instance,
            resolver);

    private static KnxConnectivityProvider CreateProvider(
        IKnxConnection connection,
        EventBus bus,
        IValuesContainer container,
        IDptResolver resolver)
    {
        var provider = new KnxConnectivityProvider(
            Options.Create(new KnxIntegrationOptions()),
            new StubKnxSystemConfiguration(resolver),
            [connection],
            bus,
            bus,
            [container],
            new StubLifecycleSync(),
            new StubStateInitializationManager(),
            resolver,
            NullLogger<KnxConnectivityProvider>.Instance);

        InitializeValues([container], bus, new TestValuesManager(bus));
        return provider;
    }

    private static void InitializeValues(IEnumerable<IValuesContainer> containers, IEventPublisher publisher, IValuesManager manager)
    {
        foreach (var container in containers)
        {
            foreach (var property in container.GetType().GetIValueProperties())
            {
                if (property.GetValue(container) is IValue value)
                    value.Initialize(publisher, manager);
            }
        }
    }

    private static IDptResolver CreateResolver(Dictionary<string, string> dptsByGa)
    {
        var domainConfig = new DomainConfiguration();
        foreach (var (gaText, dptId) in dptsByGa)
        {
            var ga = new GroupAddress(gaText);
            domainConfig.GroupAddresses[ga.Address] = new EtsGroupAddressConfig
            {
                Address = ga,
                Label = $"Fixture-{gaText}",
                DPTs = dptId,
            };
        }

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddKnxCore();
        services.AddSingleton<IKnxMasterDataProvider>(KnxMasterDataProviderStub.Create());
        services.AddSingleton(domainConfig);
        services.TryAddSingleton<IDptResolver, KnxDptResolver>();

        return services.BuildServiceProvider(validateScopes: false).GetRequiredService<IDptResolver>();
    }

    private sealed class DoubleKnxValueContainer : IValuesContainer
    {
        public ValueBase<double> Percent { get; }

        public DoubleKnxValueContainer(string groupAddress, string dpt)
        {
            Percent = new ValueBase<double>(NullLoggerFactory.Instance.CreateLogger<ValueBase<double>>())
            {
                BusMappings = new()
                {
                    [KnxBusEndpointMapping.BusId] = new KnxBusEndpointMapping(groupAddress, dpt)
                    {
                        Communication = BusCommunication.RegularCommunication | BusCommunication.AnswerReadRequests,
                    },
                },
            };
        }

        public IEnumerable<IValue> GetValues() => [Percent];
    }

    private sealed class StubKnxBus : IKnxBus
    {
        public bool IsConnected { get; private set; }

        public BusConnectionState ConnectionState { get; private set; } = BusConnectionState.Closed;

        public event EventHandler<KnxConnectionEventArgs>? ConnectionStateChanged;

        public event EventHandler<KnxMessageReceivedEventArgs>? MessageReceived;

        public List<IKnxMessage> SentMessages { get; } = [];

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            ConnectionState = BusConnectionState.Connected;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = false;
            ConnectionState = BusConnectionState.Closed;
            return Task.CompletedTask;
        }

        public Task SendGroupMessageAsync(IKnxMessage message, CancellationToken cancellationToken = default)
        {
            SentMessages.Add(message);
            return Task.CompletedTask;
        }

        public void RaiseMessageReceived(GroupEventArgs args, DateTimeOffset? at = null)
            => MessageReceived?.Invoke(this, new KnxMessageReceivedEventArgs(args, at ?? DateTimeOffset.UtcNow));

        public void RaiseConnectionStatusChanged(bool isConnected)
        {
            IsConnected = isConnected;
            ConnectionState = isConnected ? BusConnectionState.Connected : BusConnectionState.Closed;
            ConnectionStateChanged?.Invoke(this, new KnxConnectionEventArgs());
        }
    }

    private sealed class TestValuesManager : IValuesManager
    {
        private readonly ConcurrentDictionary<IValue, bool> _values = [];

        public TestValuesManager(IEventSubscriber subscriber)
        {
            subscriber.Subscribe(new ValueUpdateHandler(this));
            subscriber.Subscribe(new ValueWriteHandler(this));
        }

        public void RegisterValue(IValue value) => _values.TryAdd(value, true);

        public void UnregisterValue(IValue value) => _values.TryRemove(value, out _);

        private void Route(ValueUpdateReceived @event)
        {
            if (@event.Target is IValueEventReceiver receiver && _values.ContainsKey(@event.Target))
                receiver.ReceiveUpdate(@event.Value);
        }

        private void Route(ValueWriteReceived @event)
        {
            if (@event.Target is IValueEventReceiver receiver && _values.ContainsKey(@event.Target))
                receiver.ReceiveWrite(@event.NewValue);
        }

        private sealed class ValueUpdateHandler(TestValuesManager owner) : IEventHandler<ValueUpdateReceived>
        {
            public ValueTask HandleAsync(ValueUpdateReceived @event, CancellationToken cancellationToken = default)
            {
                owner.Route(@event);
                return ValueTask.CompletedTask;
            }
        }

        private sealed class ValueWriteHandler(TestValuesManager owner) : IEventHandler<ValueWriteReceived>
        {
            public ValueTask HandleAsync(ValueWriteReceived @event, CancellationToken cancellationToken = default)
            {
                owner.Route(@event);
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class StubKnxSystemConfiguration(IDptResolver resolver) : IKnxSystemConfiguration
    {
        public DptBase GetDpt(GroupAddress groupAddress) => resolver.GetDpt(groupAddress);

        public void ClearCache() { }

        public DptBase GetDptFromId(string dptId) => throw new NotImplementedException();

        public GroupAddressMeta GetGroupAddressMeta(GroupAddress groupAddress) => throw new NotImplementedException();

        public GroupAddressMeta GetGroupAddressMeta(string name) => throw new NotImplementedException();

        public GroupAddressMeta? GetGroupAddressMetaOrNull(GroupAddress groupAddress) => null;

        public GroupAddressMeta? GetGroupAddressMetaOrNull(string name) => null;

        public bool TryGetGroupAddressMeta(GroupAddress ga, out GroupAddressMeta? gaConfig)
        {
            gaConfig = null;
            return false;
        }
    }

    private sealed class StubLifecycleSync : IHomeCompanionLifeCycleSynchronization
    {
        public Task AwaitBusesConnectedAsync(TimeSpan timeout, CancellationToken token = default) => Task.CompletedTask;

        public Task WaitForInitializationStageCompletedAsync(AppInitializationStage level, TimeSpan timeout, CancellationToken token = default)
            => Task.CompletedTask;

        public Task SignalInitializationStageCompletedAsync(AppInitializationStage level) => Task.CompletedTask;

        public bool IsInitializationStageCompleted(AppInitializationStage level) => false;

        public bool IsAllUpToStageCompleted(AppInitializationStage level) => false;
    }

    private sealed class StubStateInitializationManager : IStateInitializationManager
    {
        public AppInitializationStage CurrentStage => AppInitializationStage.Default;

        public Task InitializeStateAsync(CancellationToken token = default) => Task.CompletedTask;

        public void RegisterInitialization(AppInitializationStage stage, StateInitializationDelegate initialization) { }

        public void RemoveInitialization(AppInitializationStage stage, StateInitializationDelegate initialization) { }

        public void RegisterSave(StateInitializationDelegate save) { }

        public void RemoveSave(StateInitializationDelegate save) { }

        public Task SaveStateAsync(CancellationToken token = default) => Task.CompletedTask;
    }

    private sealed class KnxMasterDataProviderStub(KnxMasterData masterData) : IKnxMasterDataProvider
    {
        public KnxMasterData GetMasterData() => masterData;

        public static KnxMasterDataProviderStub Create()
        {
            var baseDir = Path.GetDirectoryName(typeof(KnxMasterDataProviderStub).Assembly.Location) ?? "";
            var path = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..",
                "SRF.Network", "Subs", "SRF.Knx", "SRF.Knx.Config", "Resources", "knx_master.xml"));

            if (!File.Exists(path))
            {
                Assert.Fail($"knx_master.xml not found at: {path}");
            }

            return new KnxMasterDataProviderStub(KnxMasterDataLoader.LoadFromFile(path));
        }
    }
}
