using HomeCompanion.Alerting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet.Protocol;
using SRF.Network.Mqtt;
using System.Text.Json;

namespace HomeCompanion.Integrations.Alerting.Providers;

/// <summary>
/// Push-message alert provider backed by MQTT publishing.
/// </summary>
public sealed class PushMessageAlertChannelProvider : IAlertChannelProvider
{
    private readonly AlertingIntegrationOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PushMessageAlertChannelProvider> _logger;

    /// <inheritdoc/>
    public AlertPath Path => AlertPath.PushMessage;

    /// <inheritdoc/>
    public bool IsEnabled
        => !string.IsNullOrWhiteSpace(_options.PushMessage.Broker)
           && !string.IsNullOrWhiteSpace(_options.PushMessage.Topic);

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public PushMessageAlertChannelProvider(
        IOptions<AlertingIntegrationOptions> options,
        IServiceProvider serviceProvider,
        TimeProvider timeProvider,
        ILogger<PushMessageAlertChannelProvider> logger)
    {
        _options = options.Value;
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AlertPathDispatchResult> DispatchAsync(AlertRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return new AlertPathDispatchResult
            {
                Path = Path,
                IsSuccess = false,
                ErrorCode = "push-not-configured",
                Message = "Push-message broker/topic is not configured.",
            };
        }

        IMqttBrokerConnection brokerConnection;
        try
        {
            brokerConnection = _serviceProvider.GetRequiredKeyedService<IMqttBrokerConnection>(_options.PushMessage.Broker);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Push-message provider could not resolve MQTT broker '{BrokerName}'.", _options.PushMessage.Broker);
            return new AlertPathDispatchResult
            {
                Path = Path,
                IsSuccess = false,
                ErrorCode = "push-broker-not-found",
                Message = $"MQTT broker '{_options.PushMessage.Broker}' is not registered.",
            };
        }

        var payload = JsonSerializer.Serialize(new
        {
            severity = request.Severity.ToString(),
            message = request.MessageShort,
            messageLong = request.MessageLong,
            alertKey = request.AlertKey,
            timestamp = _timeProvider.GetUtcNow(),
            correlationId = request.CorrelationId,
            metadata = request.Metadata,
        });

        var publisher = new PublisherString(_options.PushMessage.Topic, payload);
        if (_options.PushMessage.Qos is int qos)
            publisher.Options.ServiceLevel = ParseQos(qos);
        if (_options.PushMessage.Retain is bool retain)
            publisher.Options.Retain = retain;
        if (!string.IsNullOrWhiteSpace(_options.PushMessage.ContentType))
            publisher.Options.ContentType = _options.PushMessage.ContentType;

        var completion = new TaskCompletionSource<AlertPathDispatchResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        brokerConnection.Publish(publisher, (_, args) =>
        {
            var publishResult = args.PublishingQueueItem.PublishResult;
            completion.TrySetResult(new AlertPathDispatchResult
            {
                Path = Path,
                IsSuccess = publishResult?.IsSuccess ?? false,
                ErrorCode = publishResult?.IsSuccess ?? false ? null : "push-publish-failed",
                Message = publishResult?.IsSuccess ?? false
                    ? "MQTT publish succeeded."
                    : $"MQTT publish failed: {publishResult?.ReasonString ?? "unknown reason"}",
            });
        });

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.PushMessage.PublishResultTimeoutMs <= 0 ? 5000 : _options.PushMessage.PublishResultTimeoutMs);

        try
        {
            return await completion.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Timed out waiting for MQTT publish result on broker '{BrokerName}' topic '{Topic}'.",
                _options.PushMessage.Broker,
                _options.PushMessage.Topic);

            return new AlertPathDispatchResult
            {
                Path = Path,
                IsSuccess = false,
                ErrorCode = "push-publish-timeout",
                Message = "Timed out waiting for MQTT publish result.",
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Push-message dispatch failed for topic '{Topic}'.", _options.PushMessage.Topic);
            return new AlertPathDispatchResult
            {
                Path = Path,
                IsSuccess = false,
                ErrorCode = "push-dispatch-failed",
                Message = ex.Message,
            };
        }
    }

    private static MqttQualityOfServiceLevel ParseQos(int qos)
        => qos switch
        {
            <= 0 => MqttQualityOfServiceLevel.AtMostOnce,
            1 => MqttQualityOfServiceLevel.AtLeastOnce,
            _ => MqttQualityOfServiceLevel.ExactlyOnce,
        };
}
