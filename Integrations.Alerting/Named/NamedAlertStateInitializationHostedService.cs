using HomeCompanion.Abstractions;
using HomeCompanion.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Integrations.Alerting.Named;

/// <summary>
/// Hooks named-alert persistence into global state initialization/save stages.
/// </summary>
public sealed class NamedAlertStateInitializationHostedService : BackgroundService
{
    private readonly IStateInitializationManager _stateInitializationManager;
    private readonly NamedAlertPersistenceAdapter _persistenceAdapter;
    private readonly ILogger<NamedAlertStateInitializationHostedService> _logger;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public NamedAlertStateInitializationHostedService(
        IStateInitializationManager stateInitializationManager,
        NamedAlertPersistenceAdapter persistenceAdapter,
        ILogger<NamedAlertStateInitializationHostedService> logger)
    {
        _stateInitializationManager = stateInitializationManager;
        _persistenceAdapter = persistenceAdapter;
        _logger = logger;

        _stateInitializationManager.RegisterInitialization(AppInitializationStage.InitLoadFromStore, LoadAsync);
        _stateInitializationManager.RegisterSave(SaveAsync);
    }

    /// <inheritdoc/>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _persistenceAdapter.LoadAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Loading named-alert state failed.");
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _persistenceAdapter.SaveAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Saving named-alert state failed.");
        }
    }
}
