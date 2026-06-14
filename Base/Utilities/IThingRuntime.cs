namespace HomeCompanion.Base.Utilities;

public interface IThingRuntime : IDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}