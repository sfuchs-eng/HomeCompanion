using HomeCompanion.Diagnostics;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics;

/// <summary>
/// Base class for all logic modules. Provides access to the event bus for publishing and subscribing to events.
/// </summary>
/// <remarks>
/// <para>Subclasses should call <see cref="Subscribe{T}"/> from their constructor to register event handlers.</para>
/// <para>Use <see cref="Publisher"/> to publish events.</para>
/// <para>Inherit <see cref="IDiagnosable"/> in deriving classes and override <see cref="PopulateDiagnosticResultsAsync"/> to provide diagnostic information about the logic module.</para>
/// </remarks>
/// <remarks>
/// Initializes the logic with the required event bus services.
/// </remarks>
public abstract class LogicBase(ILogger<ILogic> logicLogger) : ILogic
{
    public virtual string Name => $"Logic {GetType().Name}";

    protected ILogger<ILogic> Logger { get; } = logicLogger;

    // Semaphore to ensure that InitializeAsyncLatched is only called once, even if InitializeAsync is called multiple times.
    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);
    private bool _isInitialized = false;

    /// <summary>
    /// Initializes the logic. For convenience, this method calls <see cref="InitializeAsyncLatched"/> only
    /// once, even if called multiple times. Subsequent calls wait until the first initialization completes and then return immediately.
    /// This allows dependent logics to call <c>InitializeAsync</c> on their dependencies without risking multiple initializations or deadlocks.
    /// Inheriting classes should override <see cref="InitializeAsyncLatched"/> to perform their initialization logic, which is guaranteed to only run once.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _initializationSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (_isInitialized)
                    return;

                await InitializeAsyncLatched(cancellationToken);
                _isInitialized = true;
            }
            catch
            {
                throw;
            }
            finally
            {
                _initializationSemaphore.Release();
            }
            await EnableAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            OnActivationFailed(ex);
            throw;
        }
    }
    
    /// <summary>
    /// Internal initialization method that is guaranteed to only be called once, even if <see cref="InitializeAsync"/> is called multiple times.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected abstract Task InitializeAsyncLatched(CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public virtual Task EnableAsync(CancellationToken cancellationToken = default)
    {
        if ( IsActivationFailed )
            throw new InvalidOperationException("Cannot enable logic because activation failed.", ActivationException);
        IsEnabled = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual Task DisableAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
            return Task.CompletedTask;
        IsEnabled = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public bool IsEnabled { get; private set; }

    public bool IsActivationFailed => ActivationException is not null;
    public bool IsActivated => !IsActivationFailed && _isInitialized;

    /// <summary>
    /// Should be set in case <see cref="InitializeAsyncLatched(CancellationToken)"/> or <see cref="EnableAsync(CancellationToken)"/> fail,
    /// causing <see cref="IsEnabled"/> to remain false.
    /// </summary>
    /// <value></value>
    public Exception? ActivationException { get; private set; } = null;

    protected void OnActivationFailed(Exception exception)
    {
        ActivationException = exception;
        IsEnabled = false;
        Logger.LogError(exception, "Logic activation failed: {Message}", exception.Message);
    }

    protected virtual Task<DiagnosticResultNode> PopulateDiagnosticResultsAsync(DiagnosticResultNode parentNode, CancellationToken cancellationToken)
    {
        var node = parentNode;
        node.AddRecord("IsInitialized", _isInitialized.ToString());
        node.AddRecord("IsEnabled", IsEnabled.ToString());
        return Task.FromResult(node);
    }

    public virtual async Task<IDiagnosticResultNode> GetDiagnosisAsync(CancellationToken cancellationToken)
    {
        return await PopulateDiagnosticResultsAsync(DiagnosticResultNode.Create(Name), cancellationToken);
    }
}
