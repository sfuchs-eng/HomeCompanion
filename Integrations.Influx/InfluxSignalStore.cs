using System.Threading.Channels;
using HomeCompanion.Abstractions;
using HomeCompanion.Diagnostics;
using HomeCompanion.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeCompanion.Integrations.Influx;

internal sealed class InfluxSignalStore : ISignalStore, IHostedService
{
    private readonly IInfluxBatchWriter _batchWriter;
    private readonly IHomeCompanionLifeCycleSynchronization lifeCycleSynchronization;
    private readonly InfluxIntegrationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InfluxSignalStore> _logger;
    private readonly Channel<InternalSignalMeasurement> _queue;

    private readonly object _lifecycleLock = new();
    private Task? _workerTask;
    private CancellationTokenSource? _workerCancellation;
    private volatile bool _acceptWrites;
    private long _droppedMeasurements;

    public bool IsEnabled => _acceptWrites;

    public string ServiceName => nameof(InfluxSignalStore);

    public object? Service => this;

    public InfluxSignalStore(
        IInfluxBatchWriter batchWriter,
        IOptions<InfluxIntegrationOptions> options,
        IHomeCompanionLifeCycleSynchronization lifeCycleSynchronization,
        TimeProvider timeProvider,
        ILogger<InfluxSignalStore> logger)
    {
        _batchWriter = batchWriter;
        this.lifeCycleSynchronization = lifeCycleSynchronization;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;

        _queue = Channel.CreateBounded<InternalSignalMeasurement>(new BoundedChannelOptions(Math.Max(1, _options.MaxBufferedMeasurements))
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
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
            "Influx internal signal store started with flush interval {FlushIntervalSeconds}s, max batch size {MaxQueueSize}, and max buffered measurements {MaxBufferedMeasurements}.",
            _options.FlushIntervalSeconds,
            _options.MaxQueueSize,
            _options.MaxBufferedMeasurements);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? workerTask;
        CancellationTokenSource? workerCancellation;

        await lifeCycleSynchronization.WaitForInitializationStageCompletedAsync(AppLifeCycleStage.ShutDownSave, TimeSpan.FromSeconds(30), cancellationToken);

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

    private ServiceStatusInfo _lastServiceStatusInfo = new(
        ServiceName: nameof(InfluxSignalStore),
        Status: ServiceStatus.Undefined,
        TimeStamp: DateTimeOffset.MinValue,
        LastUpTime: null,
        Exceptions: null
    );

    private void RecordServiceStatus(ServiceStatus? status = null, string? message = null, Exception? exception = null)
    {
        var newStatus = status ?? ServiceStatus.Down;
        if ( message is not null )
            _logger.LogWarning(exception, "Influx signal store service status {Status}: {Message}", newStatus, message ?? "-");
        _lastServiceStatusInfo = new ServiceStatusInfo(
            ServiceName: nameof(InfluxSignalStore),
            Status: newStatus,
            TimeStamp: _timeProvider.GetLocalNow(),
            LastUpTime: newStatus == ServiceStatus.Up ? _timeProvider.GetLocalNow() : _lastServiceStatusInfo.LastUpTime,
            Exceptions: exception is not null ? [exception] : null
        );
    }

    private void RecordServiceUp()
    {
        _lastServiceStatusInfo = new ServiceStatusInfo(
            ServiceName: nameof(InfluxSignalStore),
            Status: ServiceStatus.Up,
            TimeStamp: _timeProvider.GetLocalNow(),
            LastUpTime: _timeProvider.GetLocalNow(),
            Exceptions: null
        );
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

        if (_queue.Writer.TryWrite(measurement))
            return ValueTask.CompletedTask;

        var dropped = Interlocked.Increment(ref _droppedMeasurements);
        if (dropped == 1 || dropped % 200 == 0)
        {
            _logger.LogWarning(
                "Dropping Influx measurement '{Measurement}' because the in-memory buffer is full ({Capacity}). Total dropped measurements: {DroppedCount}.",
                measurement.Measurement,
                _options.MaxBufferedMeasurements,
                dropped);
        }

        return ValueTask.CompletedTask;
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

        RecordServiceUp();

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

        RecordServiceStatus(ServiceStatus.Undefined, "Influx internal signal store worker has stopped.");
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

            try
            {
                var success = await TryFlushWithRetryAsync(bucket, measurements, cancellationToken);
                if (!success)
                {
                    RecordServiceStatus(ServiceStatus.Deviation, $"Failed to flush {measurements.Count} measurements for bucket '{bucket}' after retries were exhausted.");
                }
                else
                {
                    RecordServiceUp();
                }
            }
            catch (Exception ex)
            {
                RecordServiceStatus(ServiceStatus.Deviation, $"Exception occurred while flushing {measurements.Count} measurements for bucket '{bucket}'.", ex);
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

                _logger.LogTrace(
                    "Flushed {Count} internal signal measurements to bucket '{Bucket}' in {ElapsedMs} ms.",
                    measurements.Count,
                    bucket,
                    elapsed.TotalMilliseconds);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (measurements.Count > 0)
                {
                    _logger.LogWarning(
                        "Influx internal signal flush canceled for bucket '{Bucket}' with {Count} pending measurements.",
                        bucket,
                        measurements.Count);
                }
                else
                {
                    _logger.LogInformation(
                        "Influx internal signal flush canceled for bucket '{Bucket}' with no pending measurements.",
                        bucket);
                }
                throw;
            }
            catch (ObjectDisposedException)
            {
                _logger.LogWarning(
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

    public Task<ServiceStatusInfo> GetServiceStatusInfoAsync()
    {
        return Task.FromResult(_lastServiceStatusInfo);
    }
}
