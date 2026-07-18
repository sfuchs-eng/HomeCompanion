using HomeCompanion;
using HomeCompanion.Abstractions;
using HomeCompanion.Core.Events;
using HomeCompanion.Events;
using HomeCompanion.Integrations.OpenHab;
using HomeCompanion.Integrations.OpenHab.Events;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SRF.Knx.Config;
using SRF.Knx.Core;
using SRF.Knx.Core.DPT;
using SRF.Knx.Core.Master;
using SRF.Network.OpenHab;
using SRF.Network.OpenHab.Client;
using SRF.Network.OpenHab.EventBus;
using SRF.Network.OpenHab.EventBus.Events;
using SRF.Network.OpenHab.Items;
using System.Collections.Concurrent;
using System.Reflection;

namespace HomeCompanion.Tests;

[TestFixture]
public class OpenHabConnectivityProviderTests
{
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

    private static OpenHabConnectivityProvider CreateProvider(
        IEventPublisher publisher,
        IEventSubscriber subscriber,
        StubEventBusClient eventBusClient,
        StubRestApiClient restApiClient,
        IValuesContainer? container = null,
        IHomeCompanionLifeCycleSynchronization? lifeCycleSynchronization = null)
    {
        var converter = new OpenHabStateConverter(
            new StubKnxSystemConfiguration(),
            new StubMasterDataProvider(),
            NullLogger<OpenHabStateConverter>.Instance);

        var containers = container is not null ? [container] : Array.Empty<IValuesContainer>();
        var lifecycle = lifeCycleSynchronization ?? new StubLifecycleSync();
        var valuesManager = new TestValuesManager(subscriber);
        InitializeValues(containers, publisher, valuesManager);
        return new OpenHabConnectivityProvider(
            Options.Create(new OpenHabIntegrationOptions { Enable = true }),
            publisher,
            subscriber,
            eventBusClient,
            restApiClient,
            containers,
            lifecycle,
            converter,
            NullLogger<OpenHabConnectivityProvider>.Instance);
    }

    private sealed class StubLifecycleSync : IHomeCompanionLifeCycleSynchronization
    {
        public Task AwaitBusesConnectedAsync(TimeSpan timeout, CancellationToken token = default) => Task.CompletedTask;

        public Task WaitForInitializationStageCompletedAsync(AppInitializationStage level, TimeSpan timeout, CancellationToken token = default)
        {
            if (level == AppInitializationStage.InitValuesRegistered)
                return Task.CompletedTask;

            return Task.CompletedTask;
        }

        public Task SignalInitializationStageCompletedAsync(AppInitializationStage level) => Task.CompletedTask;

        public bool IsInitializationStageCompleted(AppInitializationStage level) => true;

        public bool IsAllUpToStageCompleted(AppInitializationStage level) => true;
    }

    private sealed class GateLifecycleSync : IHomeCompanionLifeCycleSynchronization
    {
        private readonly TaskCompletionSource _valuesRegistered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task AwaitBusesConnectedAsync(TimeSpan timeout, CancellationToken token = default) => Task.CompletedTask;

        public async Task WaitForInitializationStageCompletedAsync(AppInitializationStage level, TimeSpan timeout, CancellationToken token = default)
        {
            if (level != AppInitializationStage.InitValuesRegistered)
                return;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeout);
            try
            {
                await _valuesRegistered.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException ex)
            {
                throw new TimeoutException("InitValuesRegistered was not signaled in time.", ex);
            }
        }

        public Task SignalInitializationStageCompletedAsync(AppInitializationStage level)
        {
            if (level == AppInitializationStage.InitValuesRegistered)
                _valuesRegistered.TrySetResult();

            return Task.CompletedTask;
        }

        public bool IsInitializationStageCompleted(AppInitializationStage level)
            => level != AppInitializationStage.InitValuesRegistered || _valuesRegistered.Task.IsCompleted;

        public bool IsAllUpToStageCompleted(AppInitializationStage level) => IsInitializationStageCompleted(level);
    }

    [Test]
    public async Task StartAsync_WaitsForInitValuesRegisteredStage()
    {
        var bus = CreateBus();
        var eventBusClient = new StubEventBusClient { IsActive = true };
        var restApiClient = new StubRestApiClient();
        var lifecycle = new GateLifecycleSync();
        var provider = CreateProvider(bus, bus, eventBusClient, restApiClient, null, lifecycle);

        var startTask = provider.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.That(startTask.IsCompleted, Is.False);

        await lifecycle.SignalInitializationStageCompletedAsync(AppInitializationStage.InitValuesRegistered);
        await startTask;

        Assert.That(provider.IsConnected, Is.True);
        Assert.That(provider.IsInitializationFinished, Is.True);
    }

    [Test]
    public void StartAsync_ThrowsOnDuplicateOpenHabItemMappings()
    {
        var bus = CreateBus();
        var eventBusClient = new StubEventBusClient();
        var restApiClient = new StubRestApiClient();
        var container = new DuplicateItemNameContainer();
        var provider = CreateProvider(bus, bus, eventBusClient, restApiClient, container);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.StartAsync(CancellationToken.None));
    }

    private static void InitializeValues(IEnumerable<IValuesContainer> containers, IEventPublisher publisher, IValuesManager manager)
    {
        foreach (var container in containers)
        {
            var properties = container.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(p => p.CanRead && typeof(IValue).IsAssignableFrom(p.PropertyType));

            foreach (var property in properties)
            {
                if (property.GetValue(container) is IValue value)
                    value.Initialize(publisher, manager);
            }
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

    private sealed class StubEventBusClient : IEventBusClient
    {
        public bool IsActive { get; set; }
        public IEventFactory EventFactory => throw new NotSupportedException();
        public event EventHandler<EventReceivedEventArgs>? EventReceived;

        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void EnqueueTransmit(SRF.Network.OpenHab.IEvent sendEvent) { }
        public void Command<ItemStateType>(string itemName, ItemStateType state) where ItemStateType : struct { }
        public Task CloseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendAsync(SRF.Network.OpenHab.IEvent sendEvent, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendAsync(string packet, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Raise(SRF.Network.OpenHab.IEvent evt) => EventReceived?.Invoke(this, new EventReceivedEventArgs(evt, DateTimeOffset.UtcNow));
    }

    private sealed class StubRestApiClient : IRestApiClient
    {
        public List<(string ItemName, string State)> SetCalls { get; } = [];

        public Task<Item[]> GetItemsAsync(CancellationToken cancel) => Task.FromResult(Array.Empty<Item>());

        public Task SetItemStateAsync(string itemName, string state, CancellationToken cancel = default)
        {
            SetCalls.Add((itemName, state));
            return Task.CompletedTask;
        }
    }

    private sealed class StubKnxSystemConfiguration : IKnxSystemConfiguration
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

    private sealed class StubMasterDataProvider : IKnxMasterDataProvider
    {
        public KnxMasterData GetMasterData() => new();
    }

    private sealed class TestContainer : IValuesContainer
    {
        public ValueBase<bool> Light { get; } = new(NullLoggerFactory.Instance.CreateLogger<ValueBase<bool>>())
        {
            BusMappings = new() { [OpenHabBusEndpointMapping.BusId] = new OpenHabBusEndpointMapping("MyLight") },
        };

        public IEnumerable<IValue> GetValues() => [Light];
    }

    private sealed class DuplicateItemNameContainer : IValuesContainer
    {
        public ValueBase<bool> LightA { get; } = new(NullLoggerFactory.Instance.CreateLogger<ValueBase<bool>>())
        {
            BusMappings = new() { [OpenHabBusEndpointMapping.BusId] = new OpenHabBusEndpointMapping("DuplicateLight") },
        };

        public ValueBase<bool> LightB { get; } = new(NullLoggerFactory.Instance.CreateLogger<ValueBase<bool>>())
        {
            BusMappings = new() { [OpenHabBusEndpointMapping.BusId] = new OpenHabBusEndpointMapping("DuplicateLight") },
        };

        public IEnumerable<IValue> GetValues() => [LightA, LightB];
    }

    [Test]
    public async Task InboundItemStateChanged_PublishesOpenHabItemStateChangedAndValueUpdateReceived()
    {
        var bus = CreateBus();
        var eventBusClient = new StubEventBusClient();
        var restApi = new StubRestApiClient();
        var container = new TestContainer();
        var provider = CreateProvider(bus, bus, eventBusClient, restApi, container);

        OpenHabItemStateChanged? specific = null;
        ValueUpdateReceived? baseUpdate = null;
        bus.Subscribe<OpenHabItemStateChanged>(new LambdaHandler<OpenHabItemStateChanged>(e => specific = e));
        bus.Subscribe<ValueUpdateReceived>(new LambdaHandler<ValueUpdateReceived>(e => baseUpdate = e));

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);

            var evt = new ItemStateChangedEvent();
            evt.Configure(EventType.ItemStateChangedEvent);
            evt.ItemName = "MyLight";
            evt.StateChange = new ItemStateChangedEventPayload { Value = "ON", OldValue = "OFF", Type = "OnOff", OldType = "OnOff" };

            eventBusClient.Raise(evt);
            await Task.Delay(100);
        });

        Assert.That(specific, Is.Not.Null);
        Assert.That(specific!.Target, Is.SameAs(container.Light));
        Assert.That(specific.Value, Is.EqualTo(true));
        Assert.That(baseUpdate, Is.Not.Null);
    }

    [Test]
    public async Task InboundItemStateEvent_PublishesOpenHabItemStateAndValueUpdateReceived()
    {
        var bus = CreateBus();
        var eventBusClient = new StubEventBusClient();
        var restApi = new StubRestApiClient();
        var container = new TestContainer();
        var provider = CreateProvider(bus, bus, eventBusClient, restApi, container);

        OpenHabItemState? specific = null;
        ValueUpdateReceived? baseUpdate = null;
        bus.Subscribe<OpenHabItemState>(new LambdaHandler<OpenHabItemState>(e => specific = e));
        bus.Subscribe<ValueUpdateReceived>(new LambdaHandler<ValueUpdateReceived>(e => baseUpdate = e));

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);

            var evt = new ItemEventTypeValue();
            evt.Configure(EventType.ItemStateEvent);
            evt.ItemName = "MyLight";
            evt.State = new TypeValuePayload().Set(ItemStateSwitch.ON);

            eventBusClient.Raise(evt);
            await Task.Delay(100);
        });

        Assert.That(specific, Is.Not.Null);
        Assert.That(specific!.RawState, Is.EqualTo("ON"));
        Assert.That(specific.Value, Is.EqualTo(true));
        Assert.That(baseUpdate, Is.Not.Null);
    }

    [Test]
    public async Task InboundItemCommandEvent_PublishesOpenHabItemCommandReceivedAndValueWriteReceived()
    {
        var bus = CreateBus();
        var eventBusClient = new StubEventBusClient();
        var restApi = new StubRestApiClient();
        var container = new TestContainer();
        var provider = CreateProvider(bus, bus, eventBusClient, restApi, container);

        OpenHabItemCommandReceived? specific = null;
        ValueWriteReceived? baseWrite = null;
        bus.Subscribe<OpenHabItemCommandReceived>(new LambdaHandler<OpenHabItemCommandReceived>(e => specific = e));
        bus.Subscribe<ValueWriteReceived>(new LambdaHandler<ValueWriteReceived>(e => baseWrite = e));

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);

            var evt = new ItemEventTypeValue();
            evt.Configure(EventType.ItemCommandEvent);
            evt.ItemName = "MyLight";
            evt.State = new TypeValuePayload().Set(ItemStateSwitch.ON);

            eventBusClient.Raise(evt);
            await Task.Delay(100);
        });

        Assert.That(specific, Is.Not.Null);
        Assert.That(specific!.RawCommand, Is.EqualTo("ON"));
        Assert.That(specific.NewValue, Is.EqualTo(true));
        Assert.That(baseWrite, Is.Not.Null);
    }

    [Test]
    public async Task OutboundValueWriteRequest_CallsOpenHabRestApi()
    {
        var bus = CreateBus();
        var eventBusClient = new StubEventBusClient();
        var restApi = new StubRestApiClient();
        var container = new TestContainer();
        var provider = CreateProvider(bus, bus, eventBusClient, restApi, container);

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);
            container.Light.Write(true);
            await Task.Delay(100);
        });

        Assert.That(restApi.SetCalls, Has.Count.EqualTo(1));
        Assert.That(restApi.SetCalls[0].ItemName, Is.EqualTo("MyLight"));
    }

    [Test]
    public async Task Lifecycle_StartAndStop_ReflectsConnectionFlags()
    {
        var bus = CreateBus();
        var eventBusClient = new StubEventBusClient { IsActive = false };
        var restApi = new StubRestApiClient();
        var provider = CreateProvider(bus, bus, eventBusClient, restApi);

        Assert.That(provider.IsConnected, Is.False);
        Assert.That(provider.IsInitializationFinished, Is.False);

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);

            eventBusClient.IsActive = true;
            Assert.That(provider.IsConnected, Is.True);
            Assert.That(provider.IsInitializationFinished, Is.True);

            eventBusClient.IsActive = false;
            await provider.StopAsync(CancellationToken.None);
        });

        Assert.That(provider.IsConnected, Is.False);
        Assert.That(provider.IsEnabled, Is.True);
    }

    [Test]
    public async Task StartAsync_WithInactiveEventBusClient_RemainsDisconnected()
    {
        var bus = CreateBus();
        var eventBusClient = new StubEventBusClient { IsActive = false };
        var restApi = new StubRestApiClient();
        var provider = CreateProvider(bus, bus, eventBusClient, restApi);

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);
            Assert.That(provider.IsConnected, Is.False);
        });
    }

    private sealed class LambdaHandler<T>(Action<T> action) : IEventHandler<T> where T : HomeCompanion.Events.IEvent
    {
        public ValueTask HandleAsync(T @event, CancellationToken cancellationToken = default)
        {
            action(@event);
            return ValueTask.CompletedTask;
        }
    }
}
