using HomeCompanion.Values;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Core;

public class ValuesInitializationManager : IValuesInitializationManager
{
    public Task InitializeValuesAsync(CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    public ValuesInitializationStage CurrentStage { get; private set; }
}

public class ValuesInitializationManagerHostedService(
    IValuesInitializationManager valuesInitializationManager,
    ILogger<ValuesInitializationManagerHostedService> logger) : IHostedService
{
    private readonly IValuesInitializationManager _valuesInitializationManager = valuesInitializationManager;
    private readonly ILogger<ValuesInitializationManagerHostedService> _logger = logger;

    private CancellationTokenSource? _initializationCancellationTokenSource;
    private Task? _initializationTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _initializationCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _initializationTask = _valuesInitializationManager.InitializeValuesAsync(_initializationCancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled failure while initializing the IValue framework. State reached before failure: {State}", _valuesInitializationManager.CurrentStage);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
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
        return Task.CompletedTask;
    }
}
