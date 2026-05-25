using System.Threading.Channels;
using HomeCompanion.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeCompanion.Integrations.Influx;

internal sealed class InfluxInternalSignalStore : IInternalSignalStore, IHostedService
{
    private readonly IInfluxBatchWriter _batchWriter;
    private readonly InfluxIntegrationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InfluxInternalSignalStore> _logger;
    private readonly Channel<InternalSignalMeasurement> _queue;

    private readonly object _lifecycleLock = new();
    private Task? _workerTask;
    private CancellationTokenSource? _workerCancellation;
    private volatile bool _acceptWrites;

    public InfluxInternalSignalStore(
        IInfluxBatchWriter batchWriter,
        IOptions<InfluxIntegrationOptions> options,
        TimeProvider timeProvider,
        ILogger<InfluxInternalSignalStore> logger)
    {
        _batchWriter = batchWriter;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;

        _queue = Channel.CreateUnbounded<InternalSignalMeasurement>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycleLock)
        {
            if (_workerTask is not null)
                return Task.CompletedTask;

            _acceptWrites = true;
            _workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _workerTask = Task.Run(() => RunWorkerAsync(_workerCancellation.Token), CancellationToken.None);
        }

        _logger.LogInformation(
            "Influx internal signal store started with flush interval {FlushIntervalSeconds}s and max queue size {MaxQueueSize}.",
            _options.FlushIntervalSeconds,
            _options.MaxQueueSize);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? workerTask;
        CancellationTokenSource? workerCancellation;

        lock (_lifecycleLock)
        {
            _acceptWrites = false;
            _queue.Writer.TryComplete();
            workerTask = _workerTask;
            workerCancellation = _workerCancellation;
        }

        if (workerTask is null)
            return;

        try
        {
            await workerTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Stop requested with cancellation while draining Influx internal signal queue.");
            workerCancellation?.Cancel();
            throw;
        }
        finally
        {
            lock (_lifecycleLock)
            {
                _workerTask = null;
                _workerCancellation?.Dispose();
                _workerCancellation = null;
            }
        }
    }

    public ValueTask EnqueueAsync(InternalSignalMeasurement measurement, CancellationToken cancellationToken = default)
    {
        if (measurement is null)
            throw new ArgumentNullException(nameof(measurement));

        if (!_acceptWrites)
            throw new InvalidOperationException("Influx internal signal store is not accepting new measurements.");

        if (string.IsNullOrWhiteSpace(measurement.Measurement))
            throw new ArgumentException("Measurement name must be provided.", nameof(measurement));

        if (measurement.Fields.Count == 0)
            throw new ArgumentException("At least one field must be provided.", nameof(measurement));

        return _queue.Writer.WriteAsync(measurement, cancellationToken);
    }

    public async ValueTask EnqueueRangeAsync(IEnumerable<InternalSignalMeasurement> measurements, CancellationToken cancellationToken = default)
    {
        if (measurements is null)
            throw new ArgumentNullException(nameof(measurements));

        foreach (var measurement in measurements)
        {
            await EnqueueAsync(measurement, cancellationToken);
        }
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        var flushInterval = TimeSpan.FromSeconds(_options.FlushIntervalSeconds);
        var buffer = new List<InternalSignalMeasurement>(_options.MaxQueueSize);

        while (!cancellationToken.IsCancellationRequested)
        {
            bool hasData;
            try
            {
                hasData = await _queue.Reader
                    .WaitToReadAsync(cancellationToken)
                    .AsTask()
                    .WaitAsync(flushInterval, cancellationToken);
            }
            catch (TimeoutException)
            {
                if (buffer.Count > 0)
                {
                    await FlushAsync(buffer, cancellationToken);
                    buffer.Clear();
                }

                continue;
            }

            if (!hasData)
                break;

            while (_queue.Reader.TryRead(out var measurement))
            {
                buffer.Add(measurement);
                if (buffer.Count >= _options.MaxQueueSize)
                {
                    await FlushAsync(buffer, cancellationToken);
                    buffer.Clear();
                }
            }
        }

        while (_queue.Reader.TryRead(out var pending))
        {
            buffer.Add(pending);
        }

        if (buffer.Count > 0)
        {
            await FlushAsync(buffer, CancellationToken.None);
            buffer.Clear();
        }
    }

    private async Task FlushAsync(List<InternalSignalMeasurement> buffer, CancellationToken cancellationToken)
    {
        var groupedByBucket = buffer
            .GroupBy(x => string.IsNullOrWhiteSpace(x.BucketOverride) ? _options.DefaultBucket : x.BucketOverride!)
            .ToList();

        foreach (var bucketGroup in groupedByBucket)
        {
            var bucket = bucketGroup.Key;
            var measurements = bucketGroup.ToList();

            var success = await TryFlushWithRetryAsync(bucket, measurements, cancellationToken);
            if (!success)
            {
                _logger.LogError(
                    "Dropping {DroppedCount} measurements for bucket '{Bucket}' after retries were exhausted.",
                    measurements.Count,
                    bucket);
            }
        }
    }

    private async Task<bool> TryFlushWithRetryAsync(
        string bucket,
        IReadOnlyList<InternalSignalMeasurement> measurements,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt <= _options.RetryCount; attempt++)
        {
            try
            {
                var started = _timeProvider.GetTimestamp();
                await _batchWriter.WriteBatchAsync(bucket, measurements, cancellationToken);
                var elapsed = _timeProvider.GetElapsedTime(started);

                _logger.LogInformation(
                    "Flushed {Count} internal signal measurements to bucket '{Bucket}' in {ElapsedMs} ms.",
                    measurements.Count,
                    bucket,
                    elapsed.TotalMilliseconds);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Influx internal signal flush canceled for bucket '{Bucket}' with {Count} pending measurements.",
                    bucket,
                    measurements.Count);
                throw;
            }
            catch (ObjectDisposedException)
            {
                _logger.LogInformation(
                    "Influx internal signal flush encountered disposed resources during shutdown for bucket '{Bucket}'.",
                    bucket);
                return false;
            }
            catch (Exception ex)
            {
                var willRetry = attempt < _options.RetryCount;
                _logger.LogWarning(
                    ex,
                    "Failed to flush {Count} internal signal measurements to bucket '{Bucket}'. Attempt {Attempt}/{MaxAttempts}.",
                    measurements.Count,
                    bucket,
                    attempt + 1,
                    _options.RetryCount + 1);

                if (!willRetry)
                    return false;

                var retryDelay = TimeSpan.FromSeconds(_options.RetryDelaySeconds);
                if (retryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(retryDelay, cancellationToken);
                }
            }
        }

        return false;
    }
}
