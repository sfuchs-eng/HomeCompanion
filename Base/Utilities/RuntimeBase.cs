using HomeCompanion.Diagnostics;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Base.Utilities;

public abstract class RuntimeBase : IThingRuntime, IDiagnosable
{
    private readonly ILogger logger;

    public RuntimeBase(ILogger logger)
    {
        this.logger = logger;
    }

    public virtual string Name => $"{nameof(IThingRuntime)}/{nameof(RuntimeBase)} {GetType().Name}";

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

    /// <summary>
    /// Override without calling base. This base method only creates a diagnostic record indicating that the method is not implemented and required overriding in the derived class.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public virtual async Task<IDiagnosticResultNode> GetDiagnosisAsync(CancellationToken cancellationToken = default)
    {
        var rootNode = DiagnosticResultNode.Create(Name);
        rootNode.AddRecord("GetDiagnosisAsync", "Not implemented in derived class. Override this method without calling base to provide diagnostic information for this runtime.");
        return rootNode;
    }

    public abstract Task InitializeAsync(CancellationToken cancellationToken = default);

    public abstract Task StartAsync(CancellationToken cancellationToken = default);

    public abstract Task StopAsync(CancellationToken cancellationToken = default);
}
