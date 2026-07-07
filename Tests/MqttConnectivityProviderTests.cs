using HomeCompanion.Abstractions;
using HomeCompanion.Core.Events;
using HomeCompanion.Events;
using HomeCompanion.Integrations.Mqtt;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Packets;
using SRF.Network.Mqtt;
using System.Collections.Concurrent;
using System.Reflection;

namespace HomeCompanion.Tests;

[TestFixture]
public class MqttConnectivityProviderTests
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

    [Test]
    public async Task StartAsync_RegistersConfiguredSubscriptions()
    {
        var bus = CreateBus();
        var broker = new StubMqttBrokerConnection();
        var container = new TestContainer();
        var provider = CreateProvider("main", bus, broker, container);

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);
        });

        Assert.That(broker.Subscriptions.Select(s => s.TopicPattern), Is.EquivalentTo(["home/+/+/state", "home/+/+/cmd", "home/events/#"]));
    }

    [Test]
    public async Task InboundStateTopic_PublishesOnlyValueUpdateReceived()
    {
        var bus = CreateBus();
        var broker = new StubMqttBrokerConnection();
        var container = new TestContainer();
        var provider = CreateProvider("main", bus, broker, container);

        ValueUpdateReceived? update = null;
        ValueWriteReceived? write = null;
        bus.Subscribe(new LambdaHandler<ValueUpdateReceived>(e => update = e));
        bus.Subscribe(new LambdaHandler<ValueWriteReceived>(e => write = e));

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);
            broker.RaiseInbound("home/living/switch/state", "ON");
            await Task.Delay(100);
        });

        Assert.That(update, Is.Not.Null);
        Assert.That(update!.Target, Is.SameAs(container.MainSwitch));
        Assert.That(update.Value, Is.EqualTo(true));
        Assert.That(write, Is.Null);
    }

    [Test]
    public async Task InboundCommandTopic_PublishesOnlyValueWriteReceived()
    {
        var bus = CreateBus();
        var broker = new StubMqttBrokerConnection();
        var container = new TestContainer();
        var provider = CreateProvider("main", bus, broker, container);

        ValueUpdateReceived? update = null;
        ValueWriteReceived? write = null;
        // thisone we change from "await Task.Delay(100);" to a more robust way to wait for the write event after having observed a rare race. Consider same for the other tests if they fail in the future.
        var writeObserved = new TaskCompletionSource<ValueWriteReceived>(TaskCreationOptions.RunContinuationsAsynchronously);
        bus.Subscribe(new LambdaHandler<ValueUpdateReceived>(e => update = e));
        bus.Subscribe(new LambdaHandler<ValueWriteReceived>(e =>
        {
            write = e;
            writeObserved.TrySetResult(e);
        }));

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);
            broker.RaiseInbound("home/living/switch/cmd", "OFF");
            await writeObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        });

        Assert.That(write, Is.Not.Null);
        Assert.That(write!.Target, Is.SameAs(container.MainSwitch));
        Assert.That(write.NewValue, Is.EqualTo(false));
        Assert.That(update, Is.Null);
    }

    [Test]
    public async Task InboundTopic_IgnoresMappingsForOtherBrokers()
    {
        var bus = CreateBus();
        var broker = new StubMqttBrokerConnection();
        var container = new TestContainer();
        var provider = CreateProvider("main", bus, broker, container);

        ValueUpdateReceived? update = null;
        bus.Subscribe(new LambdaHandler<ValueUpdateReceived>(e => update = e));

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);
            broker.RaiseInbound("lab/switch/state", "ON");
            await Task.Delay(100);
        });

        Assert.That(update, Is.Null);
    }

    [Test]
    public async Task OutboundValueWriteRequest_PublishesToCommandTopic()
    {
        var bus = CreateBus();
        var broker = new StubMqttBrokerConnection();
        var container = new TestContainer();
        var provider = CreateProvider("main", bus, broker, container);

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);
            container.MainSwitch.Write(true);
            await Task.Delay(100);
        });

        Assert.That(broker.Published, Has.Count.EqualTo(1));
        Assert.That(broker.Published[0].Topic, Is.EqualTo("home/living/switch/cmd"));
        Assert.That(broker.Published[0].Payload, Is.EqualTo("true"));
        Assert.That(broker.Published[0].Options.ContentType, Is.EqualTo("text/plain; charset=utf-8"));
    }

    [Test]
    public async Task OutboundValueWriteRequest_UsesValueNameTemplateWhenCommandTopicMissing()
    {
        var bus = CreateBus();
        var broker = new StubMqttBrokerConnection();
        var container = new TemplateContainer();
        var provider = CreateProvider("main", bus, broker, container);

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);
            container.ScenePreset.Write(7);
            await Task.Delay(100);
        });

        Assert.That(broker.Published, Has.Count.EqualTo(1));
        Assert.That(broker.Published[0].Topic, Is.EqualTo("home/ScenePreset/cmd"));
        Assert.That(broker.Published[0].Payload, Is.EqualTo("7"));
    }

    private static MqttConnectivityProvider CreateProvider(
        string brokerName,
        IEventPublisher publisher,
        StubMqttBrokerConnection brokerConnection,
        IValuesContainer? container = null)
    {
        var converter = new MqttPayloadConverter(NullLogger<MqttPayloadConverter>.Instance);
        var containers = container is not null ? new[] { container } : Array.Empty<IValuesContainer>();
        var lifecycle = new StubLifecycleSync();
        var valuesManager = new TestValuesManager((IEventSubscriber)publisher);
        InitializeValues(containers, publisher, valuesManager);

        var options = Options.Create(new MqttIntegrationOptions
        {
            Brokers =
            {
                ["main"] = new MqttBrokerIntegrationOptions
                {
                    Subscriptions = ["home/+/+/state", "home/+/+/cmd", "home/events/#"],
                },
                ["lab"] = new MqttBrokerIntegrationOptions
                {
                    Subscriptions = ["lab/+/state"],
                },
            },
        });

        return new MqttConnectivityProvider(
            brokerName,
            brokerConnection,
            options,
            publisher,
            (IEventSubscriber)publisher,
            containers,
            lifecycle,
            converter,
            TimeProvider.System,
            NullLogger<MqttConnectivityProvider>.Instance);
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

    private sealed class StubLifecycleSync : IHomeCompanionLifeCycleSynchronization
    {
        public Task AwaitBusesConnectedAsync(TimeSpan timeout, CancellationToken token = default) => Task.CompletedTask;
        public Task WaitForInitializationStageCompletedAsync(AppInitializationStage level, TimeSpan timeout, CancellationToken token = default) => Task.CompletedTask;
        public Task SignalInitializationStageCompletedAsync(AppInitializationStage level) => Task.CompletedTask;
        public bool IsInitializationStageCompleted(AppInitializationStage level) => true;
        public bool IsAllUpToStageCompleted(AppInitializationStage level) => true;
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

    private sealed class StubMqttBrokerConnection : IMqttBrokerConnection
    {
        private readonly List<SubscriptionRegistration> _subscriptions = [];

        public IMqttClient? Client => null;
        public bool IsConnected { get; private set; }
        public IReadOnlyList<SubscriptionRegistration> Subscriptions => _subscriptions;
        public List<PublishedMessage> Published { get; } = [];

        public Task StartAsync(CancellationToken cancellationToken)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }

        public Task WaitUntilConnectedAsync(CancellationToken cancel) => Task.CompletedTask;

        public PublishingQueueItem Publish(string topic, string message, EventHandler<PublishEventArgs>? publishedEventHandler = null)
        {
            var publisher = new PublisherString(topic, message);
            return Publish(publisher, publishedEventHandler);
        }

        public PublishingQueueItem Publish(IPublisher publisher, EventHandler<PublishEventArgs>? publishedEventHandler = null)
        {
            Published.Add(new PublishedMessage(publisher.Topic, ExtractPayload(publisher), publisher.Options));
            var queueItem = new PublishingQueueItem(publisher);
            if (publishedEventHandler is not null)
                queueItem.Published += publishedEventHandler;
            return queueItem;
        }

        public PublishingQueueItem PublishJson<TObject>(string topic, TObject payload, Action<PublisherJson<TObject>>? configure = null, EventHandler<PublishEventArgs>? publishedEventHandler = null) where TObject : class
        {
            var publisher = new PublisherJson<TObject>(topic, payload);
            configure?.Invoke(publisher);
            return Publish(publisher, publishedEventHandler);
        }

        public Subscription Subscribe(string topicPattern, EventHandler<MessageReceivedEventArgs> handleMessageReceived, EventHandler<SubscribedEventArgs>? handleSubscribed = null)
        {
            _subscriptions.Add(new SubscriptionRegistration(topicPattern, handleMessageReceived));
            return new Subscription(topicPattern, handleMessageReceived, handleSubscribed);
        }

        public void RaiseInbound(string topic, string payload)
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .Build();
            var args = new MessageReceivedEventArgs(new MqttApplicationMessageReceivedEventArgs(
                "stub-client",
                message,
                new MqttPublishPacket(),
                static (_, _) => Task.CompletedTask));

            foreach (var subscription in _subscriptions.Where(s => MqttTopicFilterComparer.Compare(topic, s.TopicPattern) == MqttTopicFilterCompareResult.IsMatch))
                subscription.Handler.Invoke(this, args);
        }

        private static string ExtractPayload(IPublisher publisher)
        {
            return publisher switch
            {
                PublisherString stringPublisher => stringPublisher.Payload,
                _ => string.Empty,
            };
        }
    }

    private sealed record SubscriptionRegistration(string TopicPattern, EventHandler<MessageReceivedEventArgs> Handler);
    private sealed record PublishedMessage(string Topic, string Payload, PublishingOptions Options);

    private sealed class TestContainer : IValuesContainer
    {
        public ValueBase<bool> MainSwitch { get; } = new(NullLogger<ValueBase<bool>>.Instance)
        {
            Name = "MainSwitch",
            BusMappings = new()
            {
                [MqttBusEndpointMapping.GetBusId("main")] = new MqttBusEndpointMapping("main", "home/living/switch/state", "home/living/switch/cmd")
                {
                    Communication = BusCommunication.Receive | BusCommunication.Transmit,
                    Config = new MqttBusMappingConfiguration
                    {
                        PayloadFormat = MqttPayloadFormat.RawUtf8,
                        ContentType = "text/plain; charset=utf-8",
                    },
                },
            },
        };

        public ValueBase<bool> LabSwitch { get; } = new(NullLogger<ValueBase<bool>>.Instance)
        {
            Name = "LabSwitch",
            BusMappings = new()
            {
                [MqttBusEndpointMapping.GetBusId("lab")] = new MqttBusEndpointMapping("lab", "lab/switch/state", "lab/switch/cmd")
                {
                    Communication = BusCommunication.Receive | BusCommunication.Transmit,
                },
            },
        };

        public IEnumerable<IValue> GetValues() => [MainSwitch, LabSwitch];
    }

    private sealed class TemplateContainer : IValuesContainer
    {
        public ValueBase<int> ScenePreset { get; } = new(NullLogger<ValueBase<int>>.Instance)
        {
            Name = "ScenePreset",
            BusMappings = new()
            {
                [MqttBusEndpointMapping.GetBusId("main")] = new MqttBusEndpointMapping("main", "home/scene/state")
                {
                    Communication = BusCommunication.Receive | BusCommunication.Transmit,
                    Config = new MqttBusMappingConfiguration
                    {
                        PayloadFormat = MqttPayloadFormat.RawUtf8,
                        OutboundTopicTemplate = "home/{ValueName}/cmd",
                    },
                },
            },
        };

        public IEnumerable<IValue> GetValues() => [ScenePreset];
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
