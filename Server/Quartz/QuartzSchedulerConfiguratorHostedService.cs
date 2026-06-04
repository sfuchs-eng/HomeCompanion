using HomeCompanion.Base.Quartz;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;

namespace HomeCompanion.Server.Quartz;

internal sealed class QuartzSchedulerConfiguratorHostedService(
    ISchedulerFactory schedulerFactory,
    IEnumerable<IQuartzSchedulerConfigurator> configurators,
    ILogger<QuartzSchedulerConfiguratorHostedService> logger)
    : IHostedService
{
    private readonly ISchedulerFactory _schedulerFactory = schedulerFactory;
    private readonly IReadOnlyList<IQuartzSchedulerConfigurator> _configurators = [.. configurators];
    private readonly ILogger<QuartzSchedulerConfiguratorHostedService> _logger = logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_configurators.Count == 0)
            return;

        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken).ConfigureAwait(false);

        foreach (var configurator in _configurators)
        {
            _logger.LogInformation("Applying Quartz scheduler configurator {ConfiguratorType}.", configurator.GetType().FullName);
            await configurator.ConfigureAsync(scheduler, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Applied {Count} Quartz scheduler configurator(s).", _configurators.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
