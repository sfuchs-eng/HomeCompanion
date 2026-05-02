using HomeCompanion.Abstractions;
using HomeCompanion.Base.Events;
using HomeCompanion.Base.Values;
using HomeCompanion.Core;
using HomeCompanion.Integrations.Knx;
using HomeCompanion.Integrations.Knx.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SRF.Knx.Config;
using SRF.Knx.Core;
using SRF.Knx.Core.DPT;
using SRF.Network.Knx;
using SRF.Network.Knx.Connection;
using SRF.Network.Knx.Messages;

namespace HomeCompanion.Tests;

[TestFixture]
public class KnxConnectivityProviderTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

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

    private static KnxConnectivityProvider CreateProvider(
        IKnxConnection connection,
        EventBus bus,
        IValuesContainer? container = null,
        IDptResolver? dptResolver = null)
    {
        var containers = container is not null
            ? [container]
            : Array.Empty<IValuesContainer>();
        return new KnxConnectivityProvider(
            [connection],
            bus,
            bus,
            containers,
            dptResolver ?? new StubDptResolver(),
            NullLogger<KnxConnectivityProvider>.Instance);
    }

    private static KnxConnection CreateConnection(StubKnxBus knxBus, IDptResolver? dptResolver = null)
        => new KnxConnection(
            new KnxLibraryInitializationStub(),
            knxBus,
            Options.Create(new KnxConfiguration()),
            NullLogger<KnxConnection>.Instance,
            dptResolver ?? new StubDptResolver());

    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class StubKnxBus : IKnxBus
    {
        public bool IsConnected { get; private set; }
        public BusConnectionState ConnectionState { get; private set; } = BusConnectionState.Closed;
        public event EventHandler<KnxConnectionEventArgs>? ConnectionStateChanged;
        public event EventHandler<KnxMessageReceivedEventArgs>? MessageReceived;
        public Task ConnectAsync(CancellationToken cancellationToken = default) { IsConnected = true; ConnectionState = BusConnectionState.Connected; return Task.CompletedTask; }
        public Task DisconnectAsync(CancellationToken cancellationToken = default) { IsConnected = false; ConnectionState = BusConnectionState.Closed; return Task.CompletedTask; }

        public List<IKnxMessage> SentMessages { get; } = [];
        public Task SendGroupMessageAsync(IKnxMessage message, CancellationToken cancellationToken = default) { SentMessages.Add(message); return Task.CompletedTask; }

        public void RaiseMessageReceived(GroupEventArgs args, DateTimeOffset? at = null)
            => MessageReceived?.Invoke(this, new KnxMessageReceivedEventArgs(args, at ?? DateTimeOffset.UtcNow));

        internal void RaiseConnectionStatusChanged(bool isConnected)
        {
            IsConnected = isConnected;
            ConnectionState = isConnected ? BusConnectionState.Connected : BusConnectionState.Closed;
            ConnectionStateChanged?.Invoke(this, new KnxConnectionEventArgs());
        }
    }

    /// <summary>A DPT resolver that maps any group address to a <see cref="BoolDpt"/>.</summary>
    private sealed class StubDptResolver : IDptResolver
    {
        public DptBase GetDpt(GroupAddress groupAddress) => new BoolDpt
        {
            Id = new DataPointTypeId { Main = 1, Sub = 1 },
        };
    }

    /// <summary>Minimal DPT for <see cref="bool"/> (DPT-1.x): 1 byte, non-zero = true.</summary>
    private sealed class BoolDpt : DptBase
    {
        public override object ToValue(GroupValue groupValue)
            => groupValue.Value.Length > 0 && groupValue.Value[^1] != 0;

        public override GroupValue ToGroupValue(object value)
            => new([Convert.ToBoolean(value) ? (byte)1 : (byte)0]);
    }

    private sealed class TestContainer : IValuesContainer
    {
        public ValueBase<bool> Light { get; } = new(NullLoggerFactory.Instance.CreateLogger<ValueBase<bool>>())
        {
            BusMappings = new() { [KnxBusEndpointMapping.BusId] = new KnxBusEndpointMapping("1/0/0") },
        };

        public IEnumerable<IValue> GetValues() => [Light];
    }

    // ── Tests: inbound KNX → EventBus ────────────────────────────────────────

    [Test]
    public async Task InboundWrite_PublishesKnxGroupWriteReceived()
    {
        var bus = CreateBus();
        var knxBus = new StubKnxBus();
        var connection = CreateConnection(knxBus);
        var provider = CreateProvider(connection, bus);

        KnxGroupWriteReceived? received = null;
        bus.Subscribe<KnxGroupWriteReceived>(new LambdaHandler<KnxGroupWriteReceived>(e => received = e));

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);

            knxBus.RaiseMessageReceived(new GroupEventArgs
            {
                DestinationAddress = new GroupAddress("1/0/0"),
                SourceAddress      = new IndividualAddress("1.1.1"),
                EventType          = GroupEventType.ValueWrite,
                Value              = new GroupValue([0x01]),
            });

            await Task.Delay(100);
        });

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.DestinationAddress.ToString(), Is.EqualTo("1/0/0"));
    }

    [Test]
    public async Task InboundRead_PublishesKnxGroupReadReceived()
    {
        var bus = CreateBus();
        var knxBus = new StubKnxBus();
        var connection = CreateConnection(knxBus);
        var provider = CreateProvider(connection, bus);

        KnxGroupReadReceived? received = null;
        bus.Subscribe<KnxGroupReadReceived>(new LambdaHandler<KnxGroupReadReceived>(e => received = e));

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);

            knxBus.RaiseMessageReceived(new GroupEventArgs
            {
                DestinationAddress = new GroupAddress("1/0/1"),
                SourceAddress      = new IndividualAddress("1.1.2"),
                EventType          = GroupEventType.ValueRead,
                Value              = new GroupValue([]),
            });

            await Task.Delay(100);
        });

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.DestinationAddress.ToString(), Is.EqualTo("1/0/1"));
    }

    [Test]
    public async Task InboundResponse_PublishesKnxGroupResponseReceived()
    {
        var bus = CreateBus();
        var knxBus = new StubKnxBus();
        var connection = CreateConnection(knxBus);
        var provider = CreateProvider(connection, bus);

        KnxGroupResponseReceived? received = null;
        bus.Subscribe<KnxGroupResponseReceived>(new LambdaHandler<KnxGroupResponseReceived>(e => received = e));

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);

            knxBus.RaiseMessageReceived(new GroupEventArgs
            {
                DestinationAddress = new GroupAddress("1/0/2"),
                SourceAddress      = new IndividualAddress("1.1.3"),
                EventType          = GroupEventType.ValueResponse,
                Value              = new GroupValue([0x00]),
            });

            await Task.Delay(100);
        });

        Assert.That(received, Is.Not.Null);
    }

    // ── Tests: IValuesContainer scanning + value update via event bus ─────────

    [Test]
    public async Task InboundWrite_WithRegisteredGA_UpdatesValue()
    {
        var bus = CreateBus();
        var knxBus = new StubKnxBus();
        var connection = CreateConnection(knxBus);
        var container = new TestContainer();
        var provider = CreateProvider(connection, bus, container);

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);

            // Simulate bus writing "true" to 1/0/0
            knxBus.RaiseMessageReceived(new GroupEventArgs
            {
                DestinationAddress = new GroupAddress("1/0/0"),
                SourceAddress      = new IndividualAddress("1.1.1"),
                EventType          = GroupEventType.ValueWrite,
                Value              = new GroupValue([0x01]),
            });

            await Task.Delay(200);
            await provider.StopAsync(CancellationToken.None);
        });

        Assert.That(container.Light.Value, Is.True);
        Assert.That(container.Light.Status.HasFlag(ValueStatus.Initialized), Is.True);
    }

    [Test]
    public async Task InboundResponse_WithRegisteredGA_UpdatesValueAndSetsInitialized()
    {
        var bus = CreateBus();
        var knxBus = new StubKnxBus();
        var connection = CreateConnection(knxBus);
        var container = new TestContainer();
        var provider = CreateProvider(connection, bus, container);

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);

            knxBus.RaiseMessageReceived(new GroupEventArgs
            {
                DestinationAddress = new GroupAddress("1/0/0"),
                SourceAddress      = new IndividualAddress("1.1.1"),
                EventType          = GroupEventType.ValueResponse,
                Value              = new GroupValue([0x00]),
            });

            await Task.Delay(200);
            await provider.StopAsync(CancellationToken.None);
        });

        Assert.That(container.Light.Value, Is.False);
        Assert.That(container.Light.Status.HasFlag(ValueStatus.Initialized), Is.True);
    }

    // ── Tests: outbound value.Write → KNX ────────────────────────────────────

    [Test]
    public async Task ValueWrite_SendsGroupMessageWriteOnAllConnections()
    {
        var bus = CreateBus();
        var knxBus1 = new StubKnxBus();
        var knxBus2 = new StubKnxBus();
        var conn1 = CreateConnection(knxBus1);
        var conn2 = CreateConnection(knxBus2);
        var container = new TestContainer();

        // Build provider with two connections
        var provider = new KnxConnectivityProvider(
            [conn1, conn2],
            bus, bus,
            [container],
            new StubDptResolver(),
            NullLogger<KnxConnectivityProvider>.Instance);

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);

            // Write via ValueBase<T> — publishes ValueWritten on the event bus
            container.Light.Write(true);

            await Task.Delay(200);
        });

        Assert.That(knxBus1.SentMessages, Has.Some.Matches<IKnxMessage>(m =>
            m.EventType == GroupEventType.ValueWrite &&
            m.DestinationAddress.ToString() == "1/0/0"));
        Assert.That(knxBus2.SentMessages, Has.Some.Matches<IKnxMessage>(m =>
            m.EventType == GroupEventType.ValueWrite &&
            m.DestinationAddress.ToString() == "1/0/0"));
    }

    [Test]
    public async Task ValueWrite_PublishesValueWrittenEvent()
    {
        var bus = CreateBus();
        var knxBus = new StubKnxBus();
        var connection = CreateConnection(knxBus);
        var container = new TestContainer();
        var provider = CreateProvider(connection, bus, container);

        ValueWritten? received = null;
        bus.Subscribe<ValueWritten>(new LambdaHandler<ValueWritten>(e => received = e));

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);
            container.Light.Write(true);
            await Task.Delay(200);
        });

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Source, Is.SameAs(container.Light));
        Assert.That(received.Value, Is.EqualTo(true));
    }

    [Test]
    public async Task InboundWrite_WithValueChange_PublishesValueChangedEvent()
    {
        var bus = CreateBus();
        var knxBus = new StubKnxBus();
        var connection = CreateConnection(knxBus);
        var container = new TestContainer();
        var provider = CreateProvider(connection, bus, container);

        ValueChanged? changed = null;
        bus.Subscribe<ValueChanged>(
            new LambdaHandler<ValueChanged>(e => changed = e));

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);

            // First inbound write: default is false, sending true — value changes
            knxBus.RaiseMessageReceived(new GroupEventArgs
            {
                DestinationAddress = new GroupAddress("1/0/0"),
                SourceAddress      = new IndividualAddress("1.1.1"),
                EventType          = GroupEventType.ValueWrite,
                Value              = new GroupValue([0x01]),
            });

            await Task.Delay(200);
        });

        Assert.That(changed, Is.Not.Null);
    }

    // ── Tests: IsConnected / IsInitializationFinished ────────────────────────

    [Test]
    public async Task IsConnected_True_AfterStartAsync()
    {
        var bus = CreateBus();
        var connection = CreateConnection(new StubKnxBus());
        var provider = CreateProvider(connection, bus);

        await provider.StartAsync(CancellationToken.None);

        Assert.That(provider.IsConnected, Is.True);

        await provider.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task IsConnected_False_AfterStopAsync()
    {
        var bus = CreateBus();
        var connection = CreateConnection(new StubKnxBus());
        var provider = CreateProvider(connection, bus);

        await provider.StartAsync(CancellationToken.None);
        await provider.StopAsync(CancellationToken.None);

        Assert.That(provider.IsConnected, Is.False);
    }

    [Test]
    public async Task NoContainers_IsInitializationFinished_SetImmediately()
    {
        var bus = CreateBus();
        var connection = CreateConnection(new StubKnxBus());
        var provider = CreateProvider(connection, bus);

        await provider.StartAsync(CancellationToken.None);
        await Task.Delay(50);

        Assert.That(provider.IsInitializationFinished, Is.True);

        await provider.StopAsync(CancellationToken.None);
    }

    // ── Helper: inline event handler ─────────────────────────────────────────

    private sealed class LambdaHandler<T>(Action<T> action) : IEventHandler<T> where T : IEvent
    {
        public ValueTask HandleAsync(T @event, CancellationToken cancellationToken = default)
        {
            action(@event);
            return ValueTask.CompletedTask;
        }
    }
}
