using Quartz;

namespace HomeCompanion.Base.Quartz;

/// <summary>
/// Extension hook for adding Quartz scheduler configuration from HomeCompanion extensions or logic modules.
/// </summary>
/// <remarks>
/// Implementations can schedule jobs, add calendars, and configure triggers against the started scheduler.
/// Register implementations in DI (for example via <c>IExtensionRegistration</c>) as singleton services.
/// </remarks>
public interface IQuartzSchedulerConfigurator
{
    /// <summary>
    /// Applies scheduler configuration.
    /// </summary>
    ValueTask ConfigureAsync(IScheduler scheduler, CancellationToken cancellationToken = default);
}
