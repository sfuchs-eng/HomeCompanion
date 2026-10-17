using HomeCompanion.Base.Diagnostics;
using HomeCompanion.Diagnostics;

namespace HomeCompanion.Base.Diagnostics;

/// <summary>
/// Expects a periodic update/trigger/life-beat which, if absent for a certain time interval, will cause the service to be considered "down".
/// </summary>
public class ServiceStatusMonitorPeriodic : ServiceStatusMonitor
{
    private readonly TimeSpan checkInterval;
    private DateTimeOffset lastLifeSignalTime;
    private readonly TimeProvider timeProvider;

    public override TimeSpan? MinLifeSignalInterval => checkInterval;
    public override DateTimeOffset? LastLifeSignalTime => lastLifeSignalTime == DateTimeOffset.MinValue ? null : lastLifeSignalTime;

    public ServiceStatusMonitorPeriodic(string serviceName, TimeSpan checkInterval, TimeProvider timeProvider) : base(serviceName)
    {
        this.checkInterval = checkInterval;
        this.timeProvider = timeProvider;
        this.checkStatusFunc = () => Task.FromResult(lastLifeSignalTime == DateTimeOffset.MinValue ? ServiceStatus.Undefined : timeProvider.GetLocalNow() - lastLifeSignalTime < checkInterval ? ServiceStatus.Up : ServiceStatus.Down);
        // assume service to be up at the beginning, until proven otherwise
        lastLifeSignalTime = DateTimeOffset.MinValue;
    }

    public void UpdateLifeSignal()
    {
        lastLifeSignalTime = timeProvider.GetLocalNow();
    }

    public override async Task<ServiceStatus> CheckStatusAsync()
    {
        return await base.CheckStatusAsync();
    }
}
