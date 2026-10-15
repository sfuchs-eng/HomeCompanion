using HomeCompanion.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace HomeCompanion.Logics.Diagnostics;

public sealed class ResourceConsumption(
    TimeProvider timeProvider,
    ISignalStore signalStore,
    ILogger<ResourceConsumption> logger
) : LogicBase(logger), IDiagnosable
{
    public enum MemoryMetric
    {
        WorkingSet,
        PeakWorkingSet,
        PrivateMemorySize,
        VirtualMemorySize,
        PagedMemorySize,
        PagedSystemMemorySize,
        NonpagedSystemMemorySize
    }

    public record MemoryConsumptionEntry(MemoryMetric Metric, double ValueMB);

    MemoryConsumptionEntry[]? _lastMemoryConsumptionEntries;

    DateTimeOffset _lastCpuSampleTime = timeProvider.GetUtcNow();
    double _lastCpuTime = 0.0;
    int proccessorCount = Environment.ProcessorCount;
    double _lastCpuUsage;

    protected override Task InitializeLatchedAsync(CancellationToken cancellationToken = default)
    {
        var avgWindow = TimeSpan.FromHours(24);
        var sampleInterval = TimeSpan.FromSeconds(60);
        var maxSamples = (int)(avgWindow.TotalSeconds / sampleInterval.TotalSeconds);

        // Memory consumption monitoring
        var memStream = Observable.Timer(TimeSpan.Zero, sampleInterval)
            .Select(_ =>
            {
                var process = Process.GetCurrentProcess();
                process.Refresh(); // Ensures counters are updated (especially on Windows)

                return new[]
                {
                    new MemoryConsumptionEntry(MemoryMetric.WorkingSet, process.WorkingSet64 / (1024.0 * 1024.0)),
                    new MemoryConsumptionEntry(MemoryMetric.PeakWorkingSet, process.PeakWorkingSet64 / (1024.0 * 1024.0)),
                    new MemoryConsumptionEntry(MemoryMetric.PrivateMemorySize, process.PrivateMemorySize64 / (1024.0 * 1024.0)),
                    new MemoryConsumptionEntry(MemoryMetric.VirtualMemorySize, process.VirtualMemorySize64 / (1024.0 * 1024.0 * 1024.0)),
                    new MemoryConsumptionEntry(MemoryMetric.PagedMemorySize, process.PagedMemorySize64 / (1024.0 * 1024.0)),
                    new MemoryConsumptionEntry(MemoryMetric.PagedSystemMemorySize, process.PagedSystemMemorySize64 / (1024.0 * 1024.0)),
                    new MemoryConsumptionEntry(MemoryMetric.NonpagedSystemMemorySize, process.NonpagedSystemMemorySize64 / (1024.0 * 1024.0))
                };
            })
            .Scan(new Queue<MemoryConsumptionEntry[]>(), (queue, entry) =>
            {
                queue.Enqueue(entry);
                if (queue.Count > maxSamples)
                {
                    queue.Dequeue();
                }
                return queue;
            })
            .Select(queue =>
            {
                var allEntries = queue.SelectMany(x => x).ToList();
                var avgValues = allEntries
                    .GroupBy(x => x.Metric)
                    .Select(g => new MemoryConsumptionEntry(g.Key, g.Average(x => x.ValueMB)))
                    .ToList();

                return avgValues;
            })
            .Subscribe(avgValues => {
                _lastMemoryConsumptionEntries = [.. avgValues];

                try
                {
                    _ = signalStore.EnqueueAsync(new InternalSignalMeasurement()
                    {
                        Measurement = "HomeCompanionMemoryConsumption",
                        Fields = avgValues.ToDictionary(x => x.Metric.ToString(), x => (object)x.ValueMB),
                        Timestamp = timeProvider.GetUtcNow()
                    });
                }
                catch (Exception)
                {
                    logger.LogTrace("Failed to enqueue memory consumption measurement.");
                }
            });
        Disposables.Add(memStream);

        // CPU usage monitoring
        _lastCpuSampleTime = timeProvider.GetUtcNow();
        _lastCpuTime = Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds;

        var cpuStream = Observable.Timer(TimeSpan.Zero, sampleInterval)
            .Select(_ =>
            {
                var process = Process.GetCurrentProcess();
                process.Refresh(); // Ensures counters are updated (especially on Windows)

                var currentCpuTime = process.TotalProcessorTime.TotalMilliseconds;
                var presentTime = timeProvider.GetUtcNow();
                var elapsedCpuTime = currentCpuTime - _lastCpuTime;
                var elapsedTime = (presentTime - _lastCpuSampleTime).TotalMilliseconds;
                var cpuUsage = elapsedCpuTime / (elapsedTime * Environment.ProcessorCount);
                _lastCpuSampleTime = presentTime;
                _lastCpuTime = currentCpuTime;
                return cpuUsage;
            })
            .Scan(new Queue<double>(), (queue, cpuUsage) =>
            {
                queue.Enqueue(cpuUsage);
                if (queue.Count > maxSamples)
                {
                    queue.Dequeue();
                }
                return queue;
            })
            .Select(queue => queue.Average())
            .Subscribe(cpuUsage => {
                _lastCpuUsage = cpuUsage;

                try
                {
                    _ = signalStore.EnqueueAsync(new InternalSignalMeasurement()
                    {
                        Measurement = "HomeCompanionCpuUsage",
                        Fields = new Dictionary<string, object> { { "CpuUsage", cpuUsage } },
                        Timestamp = timeProvider.GetUtcNow()
                    });
                }
                catch (Exception)
                {
                    logger.LogTrace("Failed to enqueue CPU usage measurement.");
                }
            });
        Disposables.Add(cpuStream);

        return Task.CompletedTask;
    }

    protected override async Task<DiagnosticResultNode> PopulateDiagnosticResultsAsync(DiagnosticResultNode parentNode, CancellationToken cancellationToken)
    {
        var n = await base.PopulateDiagnosticResultsAsync(parentNode, cancellationToken);
        foreach (var entry in _lastMemoryConsumptionEntries ?? Array.Empty<MemoryConsumptionEntry>())
        {
            n.AddRecord(entry.Metric.ToString(), $"{entry.ValueMB:F2} MB");
        }
        n.AddRecord("CPU Usage", $"{_lastCpuUsage * 100:F1} %");
        return n;
    }
}