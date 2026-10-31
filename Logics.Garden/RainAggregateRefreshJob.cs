using HomeCompanion.Base.Quartz;
using Microsoft.Extensions.Logging;
using Quartz;

namespace HomeCompanion.Logics.Garden;

[RegisterQuartzJob(nameof(RainAggregateRefreshJob), nameof(HomeCompanion.Logics.Garden))]
public sealed class RainAggregateRefreshJob(
    IRainService rainService,
    TimeProvider timeProvider,
    ILogger<RainAggregateRefreshJob> logger) : IJob
{
    public Task Execute(IJobExecutionContext context)
    {
        var when = timeProvider.GetUtcNow();
        rainService.RecomputeAggregates(when);
        logger.LogDebug("Recomputed rain aggregates at {When}.", when);
        return Task.CompletedTask;
    }
}
