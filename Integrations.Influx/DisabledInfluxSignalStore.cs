using HomeCompanion.Diagnostics;
using HomeCompanion.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace HomeCompanion.Integrations.Influx;

internal sealed class DisabledInfluxSignalStore(ILogger<DisabledInfluxSignalStore> logger)
    : ISignalStore, IHostedService
{
    private readonly ILogger<DisabledInfluxSignalStore> _logger = logger;

    public bool IsEnabled => true; // exercise the Influx integration code path, but drop all measurements because the integration is disabled.

    public string ServiceName => nameof(DisabledInfluxSignalStore);

    public object? Service => this;

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

    public Task<ServiceStatusInfo> GetServiceStatusInfoAsync()
    {
        var statusInfo = new ServiceStatusInfo(
            ServiceName: nameof(DisabledInfluxSignalStore),
            Status: ServiceStatus.Undefined,
            TimeStamp: DateTimeOffset.Now,
            LastUpTime: null,
            Exceptions: null
        );

        return Task.FromResult(statusInfo);
    }
}