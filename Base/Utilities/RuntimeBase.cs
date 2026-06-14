using Microsoft.Extensions.Logging;

namespace HomeCompanion.Base.Utilities;

public abstract class RuntimeBase : IThingRuntime
{
    private readonly ILogger logger;

    public RuntimeBase(ILogger logger)
    {
        this.logger = logger;
    }

    bool IsDisposed { get; set; }

    public virtual void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public virtual void Dispose(bool disposing)
    {
        if (IsDisposed)
            return;

        if (disposing)
        {
            // dispose managed state (managed objects)
        }

        IsDisposed = true;
    }

    public abstract Task InitializeAsync(CancellationToken cancellationToken = default);

    public abstract Task StartAsync(CancellationToken cancellationToken = default);

    public abstract Task StopAsync(CancellationToken cancellationToken = default);
}
