using HomeCompanion.Diagnostics;

namespace HomeCompanion.Base.Diagnostics;

/// <summary>
/// Proxy to a service that is checked.
/// Its status is determined by the last time it was checked and the result of that check.
/// </summary>
public class ServiceStatusMonitor : IServiceStatusPeriodic
{
    public string ServiceName { get; init; }
    protected Func<Task<ServiceStatus>> checkStatusFunc { get; init; }
    public virtual ServiceStatus LastCheckedStatus { get; private set; } = ServiceStatus.Undefined;

    public virtual DateTimeOffset LastCheckedTime { get; private set; } = DateTimeOffset.MinValue;

    public virtual string[]? LastCheckedErrorMessages { get; protected set; }

    public virtual TimeSpan? MinLifeSignalInterval => null;

    public virtual DateTimeOffset? LastLifeSignalTime => null;

    public object? Service => null;

    public ServiceStatusMonitor(string serviceName, Func<Task<ServiceStatus>> checkStatusFunc)
    {
        ServiceName = serviceName;
        this.checkStatusFunc = checkStatusFunc;
    }

    protected ServiceStatusMonitor(string serviceName)
    {
        ServiceName = serviceName;
        this.checkStatusFunc = () => Task.FromResult(ServiceStatus.Undefined);
    }

    public virtual async Task<ServiceStatus> CheckStatusAsync()
    {
        LastCheckedStatus = await checkStatusFunc();
        LastCheckedTime = DateTimeOffset.Now;
        return LastCheckedStatus;
    }

    public async Task<ServiceStatusInfo> GetServiceStatusInfoAsync()
    {
        await CheckStatusAsync();
        return new ServiceStatusInfo(
            ServiceName,
            LastCheckedStatus,
            LastCheckedTime,
            LastLifeSignalTime,
            LastCheckedErrorMessages?.Select(msg => new Exception(msg)).ToArray()
        );
    }
}
