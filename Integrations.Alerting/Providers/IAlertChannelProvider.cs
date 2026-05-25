using HomeCompanion.Alerting;

namespace HomeCompanion.Integrations.Alerting.Providers;

/// <summary>
/// Alert delivery provider contract for one abstract alert path.
/// </summary>
public interface IAlertChannelProvider
{
    /// <summary>
    /// Path implemented by this provider.
    /// </summary>
    AlertPath Path { get; }

    /// <summary>
    /// Indicates whether the provider is enabled/configured.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Dispatches one alert message via this provider.
    /// </summary>
    /// <param name="request">Alert request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path-level dispatch result.</returns>
    Task<AlertPathDispatchResult> DispatchAsync(AlertRequest request, CancellationToken cancellationToken = default);
}
