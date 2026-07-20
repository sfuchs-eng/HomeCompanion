using HomeCompanion.Diagnostics;

namespace HomeCompanion.Logics;

/// <summary>
/// Base class for all logic modules. Provides access to the event bus for publishing and subscribing to events.
/// </summary>
/// <remarks>
/// <para>Subclasses should call <see cref="Subscribe{T}"/> from their constructor to register event handlers.</para>
/// <para>Use <see cref="Publisher"/> to publish events.</para>
/// <para>Inherit <see cref="IDiagnosable"/> in deriving classes and override <see cref="PopulateDiagnosticResultsAsync"/> to provide diagnostic information about the logic module.</para>
/// </remarks>
public abstract class LogicBase : ILogic
{
    protected IEventSubscriber Subscriber { get; }

    /// <summary>The event publisher for dispatching events onto the event bus.</summary>
    protected IEventPublisher Publisher { get; }

    public virtual string Name => $"Logic {GetType().Name}";

    /// <summary>
    /// Initializes the logic with the required event bus services.
    /// </summary>
    protected LogicBase(IEventPublisher publisher, IEventSubscriber subscriber)
    {
        Publisher = publisher;
        Subscriber = subscriber;
    }

    /// <summary>
    /// Registers <paramref name="handler"/> to receive events of type <typeparamref name="T"/> from the event bus.
    /// </summary>
    protected void Subscribe<T>(IEventHandler<T> handler) where T : IEvent
        => Subscriber.Subscribe(handler);

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
        await _initializationSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized)
                return;

            await InitializeAsyncLatched(cancellationToken);
            _isInitialized = true;
        }
        finally
        {
            _initializationSemaphore.Release();
        }
        await EnableAsync(cancellationToken);
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
        IsEnabled = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual Task DisableAsync(CancellationToken cancellationToken = default)
    {
        IsEnabled = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public bool IsEnabled { get; private set; }

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
