using HomeCompanion.Abstractions;
using HomeCompanion.Events;
using HomeCompanion.Integrations;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet.Protocol;
using SRF.Network.Mqtt;
using System.Collections.Concurrent;

namespace HomeCompanion.Integrations.Mqtt;

internal sealed class MqttConnectivityProvider : ConnectivityProviderBase<string, MqttBusEndpointMapping>
{
    private const string ProviderName = nameof(MqttConnectivityProvider);
    private static readonly TimeSpan ValuesReadyWaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OwnPublishTtl = TimeSpan.FromSeconds(20);

    private readonly string _brokerName;
    private readonly IMqttBrokerConnection _brokerConnection;
    private readonly MqttIntegrationOptions _integrationOptions;
    private readonly IEventPublisher _publisher;
    private readonly IEventSubscriber _subscriber;
    private readonly IReadOnlyList<IValuesContainer> _containers;
    private readonly IHomeCompanionLifeCycleSynchronization _lifeCycleSync;
    private readonly MqttPayloadConverter _payloadConverter;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MqttConnectivityProvider> _logger;

    private readonly ConcurrentDictionary<IValue, MqttBusEndpointMapping> _mappingByValue = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _ownPublishes = new(StringComparer.Ordinal);

    private MqttTopicRouter _topicRouter = new([]);

    private long _inboundCount;
    private long _mappedCount;
    private long _unmappedCount;
    private long _conversionFailures;
    private long _publishAttempts;
    private long _publishFailures;

    private volatile bool _isInitializationFinished;

    public override bool IsEnabled
        => _integrationOptions.Brokers.TryGetValue(_brokerName, out var broker) && broker.Enable;

    public override bool IsConnected => IsEnabled && _brokerConnection.IsConnected;

    public override bool IsInitializationFinished => _isInitializationFinished;

    public MqttConnectivityProvider(
        string brokerName,
        IMqttBrokerConnection brokerConnection,
        IOptions<MqttIntegrationOptions> integrationOptions,
        IEventPublisher publisher,
        IEventSubscriber subscriber,
        IEnumerable<IValuesContainer> containers,
        IHomeCompanionLifeCycleSynchronization lifeCycleSync,
        MqttPayloadConverter payloadConverter,
        TimeProvider timeProvider,
        ILogger<MqttConnectivityProvider> logger)
    {
        _brokerName = brokerName;
        _brokerConnection = brokerConnection;
        _integrationOptions = integrationOptions.Value;
        _publisher = publisher;
        _subscriber = subscriber;
        _containers = [.. containers];
        _lifeCycleSync = lifeCycleSync;
        _payloadConverter = payloadConverter;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            _logger.LogInformation("MQTT broker '{BrokerName}' provider disabled by configuration.", _brokerName);
            _isInitializationFinished = true;
            return;
        }

        await WaitForStartupGateAsync(
            _lifeCycleSync,
            _logger,
            $"{ProviderName}[{_brokerName}]",
            ValuesReadyWaitTimeout,
            cancellationToken);

        SubscribeValueWriteRequests(_subscriber, HandleValueWriteRequestAsync);

        BuildMappingsAndRouter();
        RegisterSubscriptions();

        await _brokerConnection.StartAsync(cancellationToken);

        _isInitializationFinished = true;
        _logger.LogInformation(
            "MQTT provider for broker '{BrokerName}' started. Mapped values: {MappedValues}.",
            _brokerName,
            _mappingByValue.Count);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _brokerConnection.StopAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Stopping MQTT provider for broker '{BrokerName}' canceled by host.", _brokerName);
        }
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("MQTT broker connection for '{BrokerName}' already disposed during shutdown.", _brokerName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to stop MQTT provider for broker '{BrokerName}'.", _brokerName);
        }

        _logger.LogInformation(
            "MQTT provider for broker '{BrokerName}' stopped. Inbound={Inbound}, Mapped={Mapped}, Unmapped={Unmapped}, ConversionFailures={ConversionFailures}, PublishAttempts={PublishAttempts}, PublishFailures={PublishFailures}.",
            _brokerName,
            Interlocked.Read(ref _inboundCount),
            Interlocked.Read(ref _mappedCount),
            Interlocked.Read(ref _unmappedCount),
            Interlocked.Read(ref _conversionFailures),
            Interlocked.Read(ref _publishAttempts),
            Interlocked.Read(ref _publishFailures));
    }

    private void BuildMappingsAndRouter()
    {
        _mappingByValue.Clear();

        var mappings = FindValueMappings(_containers)
            .Where(m => string.Equals(m.Mapping.BrokerName, _brokerName, StringComparison.OrdinalIgnoreCase))
            .Select((m, index) => new MqttValueMapping(m.Value, m.Mapping, index))
            .ToList();

        foreach (var m in mappings)
            _mappingByValue[m.Value] = m.Mapping;

        _topicRouter = new MqttTopicRouter(mappings);

        _logger.LogInformation(
            "MQTT provider for broker '{BrokerName}' discovered {Count} value mapping(s).",
            _brokerName,
            mappings.Count);
    }

    private void RegisterSubscriptions()
    {
        if (!_integrationOptions.Brokers.TryGetValue(_brokerName, out var brokerOptions))
            return;

        var patterns = brokerOptions.Subscriptions
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (patterns.Length == 0)
        {
            _logger.LogWarning(
                "MQTT broker '{BrokerName}' has no subscriptions configured. Inbound processing will be inactive.",
                _brokerName);
            return;
        }

        foreach (var pattern in patterns)
        {
            _brokerConnection.Subscribe(pattern, OnMessageReceived, (_, args) =>
            {
                _logger.LogInformation(
                    "MQTT broker '{BrokerName}' subscribed to '{TopicPattern}' with result '{Result}'.",
                    _brokerName,
                    args.Subscription.TopicPattern,
                    args.SubscriptionResultItem.ResultCode);
            });
        }
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs args)
    {
        _ = ProcessInboundAsync(args);
    }

    private async Task ProcessInboundAsync(MessageReceivedEventArgs args)
    {
        LogFirstInboundAfterStartupGate(_logger, $"{ProviderName}[{_brokerName}]", "message");
        Interlocked.Increment(ref _inboundCount);

        try
        {
            if (!_topicRouter.TryResolve(args.Topic, out var selection) || selection is null)
            {
                Interlocked.Increment(ref _unmappedCount);
                _logger.LogTrace(
                    "MQTT broker '{BrokerName}' received unmapped topic '{Topic}'.",
                    _brokerName,
                    args.Topic);
                return;
            }

            Interlocked.Increment(ref _mappedCount);

            if (!selection.Mapping.Communication.HasFlag(BusCommunication.Receive))
                return;

            var payload = args.PayloadUtf8;
            if ((selection.Mapping.Config?.IgnoreOwnPublishes ?? true) && IsOwnPublish(args.Topic, payload))
            {
                _logger.LogTrace(
                    "MQTT broker '{BrokerName}' ignored own publish echo for topic '{Topic}'.",
                    _brokerName,
                    args.Topic);
                return;
            }

            if (!_payloadConverter.TryDecode(payload, selection.Value.ValueType, selection.Mapping, out var decodedValue))
            {
                Interlocked.Increment(ref _conversionFailures);
                _logger.LogWarning(
                    "MQTT broker '{BrokerName}' failed to decode payload for topic '{Topic}' to target value '{ValueName}' ({ValueType}).",
                    _brokerName,
                    args.Topic,
                    selection.Value.Name,
                    selection.Value.ValueType);
                return;
            }

            if (selection.RouteKind == MqttRouteKind.Command)
            {
                await _publisher.PublishAsync(new ValueWriteReceived
                {
                    Timestamp = _timeProvider.GetUtcNow(),
                    Target = selection.Value,
                    NewValue = decodedValue,
                });
            }
            else
            {
                await _publisher.PublishAsync(new ValueUpdateReceived
                {
                    Timestamp = _timeProvider.GetUtcNow(),
                    Target = selection.Value,
                    Value = decodedValue,
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation from broker internals.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "MQTT broker '{BrokerName}' failed while processing inbound message on topic '{Topic}'.",
                _brokerName,
                args.Topic);
        }
    }

    private Task HandleValueWriteRequestAsync(ValueWriteRequest request, CancellationToken cancellationToken)
    {
        if (!_mappingByValue.TryGetValue(request.Source, out var mapping))
            return Task.CompletedTask;

        if (!mapping.Communication.HasFlag(BusCommunication.Transmit))
            return Task.CompletedTask;

        var publishTopic = ResolveOutboundTopic(mapping, request.Source);
        if (string.IsNullOrWhiteSpace(publishTopic))
        {
            _logger.LogTrace(
                "MQTT broker '{BrokerName}' has no outbound topic for value '{ValueName}'.",
                _brokerName,
                request.Source.Name);
            return Task.CompletedTask;
        }

        if (publishTopic.Contains('+') || publishTopic.Contains('#'))
        {
            _logger.LogWarning(
                "MQTT broker '{BrokerName}' resolved outbound topic '{Topic}' for value '{ValueName}' contains wildcard. Publish skipped.",
                _brokerName,
                publishTopic,
                request.Source.Name);
            return Task.CompletedTask;
        }

        try
        {
            var payload = _payloadConverter.Encode(request.NewValue, request.Source.ValueType, mapping);

            var publisher = new PublisherString(publishTopic, payload);
            var config = mapping.Config;
            if (config?.Qos is int qos)
                publisher.Options.ServiceLevel = ParseQos(qos);
            if (config?.Retain is bool retain)
                publisher.Options.Retain = retain;
            if (!string.IsNullOrWhiteSpace(config?.ContentType))
                publisher.Options.ContentType = config.ContentType;

            Interlocked.Increment(ref _publishAttempts);
            _brokerConnection.Publish(publisher, (_, publishEventArgs) =>
            {
                var result = publishEventArgs.PublishingQueueItem.PublishResult;
                if (!(result?.IsSuccess ?? false))
                {
                    Interlocked.Increment(ref _publishFailures);
                    _logger.LogWarning(
                        "MQTT publish failed for broker '{BrokerName}', topic '{Topic}', reason '{ReasonCode}'.",
                        _brokerName,
                        publishTopic,
                        result?.ReasonCode);
                }
            });

            if (config?.IgnoreOwnPublishes ?? true)
                RegisterOwnPublish(publishTopic, payload);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _publishFailures);
            _logger.LogWarning(
                ex,
                "MQTT broker '{BrokerName}' failed to publish write request for value '{ValueName}'.",
                _brokerName,
                request.Source.Name);
        }

        return Task.CompletedTask;
    }

    private static MqttQualityOfServiceLevel ParseQos(int qos)
    {
        return qos switch
        {
            <= 0 => MqttQualityOfServiceLevel.AtMostOnce,
            1 => MqttQualityOfServiceLevel.AtLeastOnce,
            _ => MqttQualityOfServiceLevel.ExactlyOnce,
        };
    }

    private static string? ResolveOutboundTopic(MqttBusEndpointMapping mapping, IValue source)
    {
        if (!string.IsNullOrWhiteSpace(mapping.CommandTopic))
            return mapping.CommandTopic;

        var template = mapping.Config?.OutboundTopicTemplate;
        if (string.IsNullOrWhiteSpace(template))
            return null;

        return template.Replace("{ValueName}", source.Name ?? string.Empty, StringComparison.Ordinal);
    }

    private void RegisterOwnPublish(string topic, string payload)
    {
        CleanupOwnPublishes();
        _ownPublishes[BuildOwnPublishFingerprint(topic, payload)] = _timeProvider.GetUtcNow();
    }

    private bool IsOwnPublish(string topic, string payload)
    {
        CleanupOwnPublishes();

        var key = BuildOwnPublishFingerprint(topic, payload);
        if (!_ownPublishes.TryGetValue(key, out var publishedAt))
            return false;

        return _timeProvider.GetUtcNow() - publishedAt <= OwnPublishTtl;
    }

    private void CleanupOwnPublishes()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var pair in _ownPublishes)
        {
            if (now - pair.Value > OwnPublishTtl)
                _ownPublishes.TryRemove(pair.Key, out _);
        }
    }

    private static string BuildOwnPublishFingerprint(string topic, string payload)
    {
        return $"{topic}\n{payload}";
    }
}
