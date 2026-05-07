using HomeCompanion.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Core.Persistence;

public class StateInitializationManagerHostedService(
    IStateInitializationManager stateInitializationManager,
    ILogger<StateInitializationManagerHostedService> logger) : IHostedService
{
    private readonly IStateInitializationManager _stateInitializationManager = stateInitializationManager;
    private readonly ILogger<StateInitializationManagerHostedService> _logger = logger;

    private CancellationTokenSource? _initializationCancellationTokenSource;
    private Task? _initializationTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _initializationCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _initializationTask = _stateInitializationManager.InitializeStateAsync(_initializationCancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled failure while initializing the IValue framework. State reached before failure: {State}", _stateInitializationManager.CurrentStage);
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Cancel the initialization task and wait for it to complete, with a timeout to avoid hanging indefinitely during shutdown
        _initializationCancellationTokenSource?.Cancel();
        try
        {
            _initializationTask?.Wait(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // expected when the cancellation token is triggered, no action needed
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
        {
            // expected when the task is canceled, no action needed
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled failure while stopping the ValuesInitializationManagerHostedService.");
        }
        _initializationCancellationTokenSource?.Dispose();
        _initializationCancellationTokenSource = null;

        // Save the values during shutdown to persist changes across restarts of the application
        try
        {
            await _stateInitializationManager.SaveStateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected when the cancellation token is triggered, no action needed
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
        {
            // expected when the task is canceled, no action needed
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unhandled failure while saving values during shutdown in ValuesInitializationManagerHostedService.");
        }
    }
}
    