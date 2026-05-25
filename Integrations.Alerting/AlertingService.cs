using HomeCompanion.Alerting;
using HomeCompanion.Integrations.Alerting.Named;
using HomeCompanion.Integrations.Alerting.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeCompanion.Integrations.Alerting;

/// <summary>
/// Default alerting service implementation.
/// </summary>
public sealed class AlertingService : IAlertingService
{
    private readonly AlertingIntegrationOptions _options;
    private readonly IReadOnlyDictionary<AlertPath, IAlertChannelProvider> _providers;
    private readonly NamedAlertStateMachine _namedAlertStateMachine;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AlertingService> _logger;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public AlertingService(
        IOptions<AlertingIntegrationOptions> options,
        IEnumerable<IAlertChannelProvider> providers,
        NamedAlertStateMachine namedAlertStateMachine,
        TimeProvider timeProvider,
        ILogger<AlertingService> logger)
    {
        _options = options.Value;
        _providers = providers.ToDictionary(p => p.Path, p => p);
        _namedAlertStateMachine = namedAlertStateMachine;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AlertDispatchResult> SendAsync(AlertRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.Enable)
        {
            return new AlertDispatchResult
            {
                Status = AlertDispatchStatus.Disabled,
                Message = "Alerting is disabled by configuration.",
            };
        }

        if (request.IsUserInfo && (request.RecipientOverride is null || request.RecipientOverride.Count == 0))
        {
            _logger.LogWarning("Rejecting user-info alert because no recipients were provided by logic.");
            return new AlertDispatchResult
            {
                Status = AlertDispatchStatus.Rejected,
                Message = "User-info alert requires recipient override.",
            };
        }

        var paths = _options.GetPaths(request.Severity)
            .Distinct()
            .ToArray();

        if (paths.Length == 0)
        {
            return new AlertDispatchResult
            {
                Status = AlertDispatchStatus.Rejected,
                Message = $"No alert path configured for severity '{request.Severity}'.",
            };
        }

        var results = new List<AlertPathDispatchResult>(paths.Length + 1);

        var tasks = paths.Select(path => DispatchToPathAsync(path, request, cancellationToken)).ToArray();
        var dispatchResults = await Task.WhenAll(tasks);
        results.AddRange(dispatchResults);

        if (request.Severity == AlertSeverity.Warning
            && _options.Fallbacks.WarningEmailToCriticalPushMessage
            && results.Any(r => r.Path == AlertPath.Email && !r.IsSuccess)
            && results.All(r => r.Path != AlertPath.PushMessage || !r.IsSuccess))
        {
            var fallbackResult = await DispatchToPathAsync(AlertPath.PushMessage, request, cancellationToken);
            results.Add(new AlertPathDispatchResult
            {
                Path = fallbackResult.Path,
                IsSuccess = fallbackResult.IsSuccess,
                ErrorCode = fallbackResult.ErrorCode,
                Message = fallbackResult.IsSuccess
                    ? $"Warning e-mail fallback succeeded: {fallbackResult.Message}"
                    : $"Warning e-mail fallback failed: {fallbackResult.Message}",
            });
        }

        var successful = results.Count(r => r.IsSuccess);

        var status = successful switch
        {
            0 => AlertDispatchStatus.Failed,
            _ when successful == results.Count => AlertDispatchStatus.Succeeded,
            _ => AlertDispatchStatus.PartiallySucceeded,
        };

        return new AlertDispatchResult
        {
            Status = status,
            PathResults = results,
            Message = $"Alert dispatch finished with status {status}.",
        };
    }

    /// <inheritdoc/>
    public Task<NamedAlertTransitionResult> HandleNamedAlertIntentAsync(NamedAlertIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var now = _timeProvider.GetUtcNow();
        var reminderInterval = ResolveReminderInterval(intent);
        return Task.FromResult(_namedAlertStateMachine.ApplyIntent(intent, now, reminderInterval));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyCollection<NamedAlertState>> GetNamedAlertsSnapshotAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_namedAlertStateMachine.GetSnapshot());

    private async Task<AlertPathDispatchResult> DispatchToPathAsync(AlertPath path, AlertRequest request, CancellationToken cancellationToken)
    {
        if (!_providers.TryGetValue(path, out var provider))
        {
            return new AlertPathDispatchResult
            {
                Path = path,
                IsSuccess = false,
                ErrorCode = "provider-not-found",
                Message = $"No provider registered for path '{path}'.",
            };
        }

        if (!provider.IsEnabled)
        {
            return new AlertPathDispatchResult
            {
                Path = path,
                IsSuccess = false,
                ErrorCode = "provider-disabled",
                Message = $"Provider for path '{path}' is disabled.",
            };
        }

        return await provider.DispatchAsync(request, cancellationToken);
    }

    private TimeSpan ResolveReminderInterval(NamedAlertIntent intent)
    {
        if (!_options.NamedAlerts.PerSeverityReminderInterval.TryGetValue(AlertSeverity.Warning, out var interval))
            interval = _options.NamedAlerts.DefaultReminderInterval;

        if (interval <= TimeSpan.Zero)
            interval = TimeSpan.FromMinutes(15);

        return interval;
    }
}
