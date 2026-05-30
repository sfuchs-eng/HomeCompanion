using HomeCompanion.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace HomeCompanion.Integrations.Influx;

internal sealed class DisabledInfluxSignalStore(ILogger<DisabledInfluxSignalStore> logger)
    : ISignalStore, IHostedService
{
    private readonly ILogger<DisabledInfluxSignalStore> _logger = logger;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Influx internal signal store is disabled due to incomplete configuration.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public ValueTask EnqueueAsync(InternalSignalMeasurement measurement, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Dropping internal signal measurement '{Measurement}' because Influx integration is disabled.",
            measurement?.Measurement ?? "<null>");

        return ValueTask.CompletedTask;
    }

    public ValueTask EnqueueRangeAsync(IEnumerable<InternalSignalMeasurement> measurements, CancellationToken cancellationToken = default)
    {
        var count = measurements switch
        {
            null => 0,
            ICollection<InternalSignalMeasurement> collection => collection.Count,
            _ => measurements.Count(),
        };

        _logger.LogDebug(
            "Dropping {Count} internal signal measurements because Influx integration is disabled.",
            count);

        return ValueTask.CompletedTask;
    }
}