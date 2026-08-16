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
public abstract class LogicBase(ILogger<ILogic> logicLogger) : ILogic, IDisposable
{
    public virtual string Name => $"Logic {GetType().Name}";

    protected ILogger<ILogic> Logger { get; } = logicLogger;

    // Semaphore to ensure that InitializeAsyncLatched is only called once, even if InitializeAsync is called multiple times.
    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);
    private bool _isInitialized = false;
    private bool _isTerminated = false;
    public bool IsInitialized => _isInitialized;
    public bool IsTerminated => _isTerminated;
    public bool IsDisposed => _isDisposed;

    /// <summary>
    /// List of disposable resources that will be disposed when the logic is disposed.
    /// </summary>
    protected readonly List<IDisposable> Disposables = new();

    /// <summary>
    /// Initializes the logic. For convenience, this method calls <see cref="InitializeLatchedAsync"/> only
    /// once, even if called multiple times. Subsequent calls wait until the first initialization completes and then return immediately.
    /// This allows dependent logics to call <c>InitializeAsync</c> on their dependencies without risking multiple initializations or deadlocks.
    /// Inheriting classes should override <see cref="InitializeLatchedAsync"/> to perform their initialization logic, which is guaranteed to only run once.
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

                await InitializeLatchedAsync(cancellationToken);
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
    /// This method is called in life cycle phase <see cref="HomeCompanion.Abstractions.AppLifeCycleStage.InitLogics"/> to initialize the logic.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected abstract Task InitializeLatchedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Terminates the logic. This method is guaranteed to only be called once, even if <see cref="TerminateAsync"/> is called multiple times.
    /// This method is called in life cycle phase <see cref="HomeCompanion.Abstractions.AppLifeCycleStage.TerminateLogics"/> to terminate the logic.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected virtual Task TerminateLatchedAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual async Task EnableAsync(CancellationToken cancellationToken = default)
    {
        if (IsActivationFailed)
            throw new InvalidOperationException("Cannot enable logic because activation failed.", ActivationException);
        IsEnabled = true;
    }

    /// <inheritdoc/>
    public virtual async Task DisableAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
            return;
        IsEnabled = false;
    }

    /// <inheritdoc/>
    public bool IsEnabled { get; private set; }

    public bool IsActivationFailed => ActivationException is not null;
    public bool IsActivated => !IsActivationFailed && _isInitialized;

    /// <summary>
    /// Should be set in case <see cref="InitializeLatchedAsync(CancellationToken)"/> or <see cref="EnableAsync(CancellationToken)"/> fail,
    /// causing <see cref="IsEnabled"/> to remain false.
    /// </summary>
    /// <value></value>
    public Exception? ActivationException { get; protected set; } = null;

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

    /// <summary>
    /// Terminates the logic component, allowing it to clean up resources and perform any necessary shutdown procedures.
    /// Calls <see cref="DisableAsync(CancellationToken)"/> before terminating if the logic is currently enabled.
    /// </summary>
    public virtual async Task TerminateAsync(CancellationToken cancellationToken = default)
    {
        lock (this)
        {
            if (_isTerminated)
                return;
            _isTerminated = true;
        }

        try
        {
            await DisableAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error while disabling logic {LogicName} during termination.", Name);
        }

        try
        {
            await TerminateLatchedAsync(cancellationToken);
            Logger.LogTrace("Logic {LogicName} terminated.", Name);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error while terminating logic {LogicName}.", Name);
        }
    }

    private bool _isDisposed = false;

    ~LogicBase()
    {
        Dispose(false);
    }

    /// <summary>
    /// This method is called by <see cref="Dispose(bool)"/>, only if the logic has not already been disposed, and <see cref="Dispose(bool disposing)"/> only if <paramref name="disposing"/> is true.
    /// </summary>
    /// <remarks>
    /// If the only need is to dispose of some <see cref="IDisposable"/> objects, add them to the <see cref="Disposables"/> list instead of overriding this method.
    /// </remarks>
    protected virtual void DisposingInterlocked()
    {
        foreach (var disposable in Disposables)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error while disposing resource of type {ResourceType} in logic {LogicName}.", disposable.GetType().Name, Name);
            }
        }
        // Dispose managed resources here
        _initializationSemaphore.Dispose();
    }

    /// <summary>
    /// Generally, overriding classes should not override this method. Instead, they should override <see cref="DisposingInterlocked"/> to dispose of their resources.
    /// </summary>
    /// <param name="disposing"></param>
    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed)
            return;

        if (!_isTerminated)
            Logger.LogWarning("Logic {LogicName} is being disposed without being terminated. Call TerminateAsync() before disposing the logic.", Name);

        if (disposing)
        {
            DisposingInterlocked();
        }

        _isDisposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
