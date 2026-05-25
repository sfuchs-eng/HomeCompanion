using HomeCompanion.Integrations.Influx;
using HomeCompanion.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HomeCompanion.Tests;

[TestFixture]
public class InfluxInternalSignalStoreTests
{
    [Test]
    public async Task FlushesOnTimer_WhenQueueBelowMaxSize()
    {
        var writer = new RecordingBatchWriter();
        var store = CreateStore(writer, new InfluxIntegrationOptions
        {
            Url = "http://localhost:8086",
            Organization = "org",
            Token = "token",
            DefaultBucket = "default",
            FlushIntervalSeconds = 1,
            MaxQueueSize = 100,
            RetryCount = 0,
        });

        await store.StartAsync(CancellationToken.None);
        await store.EnqueueAsync(new InternalSignalMeasurement
        {
            Measurement = "logic.signal",
            Timestamp = DateTimeOffset.UtcNow,
            Fields = new Dictionary<string, object> { ["value"] = 1L },
        });

        await WaitUntilAsync(() => writer.Batches.Count >= 1, TimeSpan.FromSeconds(3));
        await store.StopAsync(CancellationToken.None);

        Assert.That(writer.Batches.Count, Is.EqualTo(1));
        Assert.That(writer.Batches[0].Bucket, Is.EqualTo("default"));
        Assert.That(writer.Batches[0].Measurements.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task FlushesOnMaxQueueSize_BeforeTimer()
    {
        var writer = new RecordingBatchWriter();
        var store = CreateStore(writer, new InfluxIntegrationOptions
        {
            Url = "http://localhost:8086",
            Organization = "org",
            Token = "token",
            DefaultBucket = "default",
            FlushIntervalSeconds = 30,
            MaxQueueSize = 2,
            RetryCount = 0,
        });

        await store.StartAsync(CancellationToken.None);
        await store.EnqueueAsync(CreateMeasurement("m1"));
        await store.EnqueueAsync(CreateMeasurement("m2"));

        await WaitUntilAsync(() => writer.Batches.Count >= 1, TimeSpan.FromSeconds(2));
        await store.StopAsync(CancellationToken.None);

        Assert.That(writer.Batches.Count, Is.EqualTo(1));
        Assert.That(writer.Batches[0].Measurements.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task RoutesByDefaultBucketAndOverride()
    {
        var writer = new RecordingBatchWriter();
        var store = CreateStore(writer, new InfluxIntegrationOptions
        {
            Url = "http://localhost:8086",
            Organization = "org",
            Token = "token",
            DefaultBucket = "default-bucket",
            FlushIntervalSeconds = 30,
            MaxQueueSize = 3,
            RetryCount = 0,
        });

        await store.StartAsync(CancellationToken.None);
        await store.EnqueueAsync(CreateMeasurement("m1"));
        await store.EnqueueAsync(CreateMeasurement("m2", "override-bucket"));
        await store.EnqueueAsync(CreateMeasurement("m3"));

        await WaitUntilAsync(() => writer.Batches.Count >= 2, TimeSpan.FromSeconds(2));
        await store.StopAsync(CancellationToken.None);

        Assert.That(writer.Batches.Count, Is.EqualTo(2));

        var defaultBatch = writer.Batches.Single(b => b.Bucket == "default-bucket");
        var overrideBatch = writer.Batches.Single(b => b.Bucket == "override-bucket");

        Assert.That(defaultBatch.Measurements.Count, Is.EqualTo(2));
        Assert.That(overrideBatch.Measurements.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task StopAsync_DrainsPendingQueue()
    {
        var writer = new RecordingBatchWriter();
        var store = CreateStore(writer, new InfluxIntegrationOptions
        {
            Url = "http://localhost:8086",
            Organization = "org",
            Token = "token",
            DefaultBucket = "default",
            FlushIntervalSeconds = 30,
            MaxQueueSize = 100,
            RetryCount = 0,
        });

        await store.StartAsync(CancellationToken.None);
        await store.EnqueueAsync(CreateMeasurement("pending"));

        await store.StopAsync(CancellationToken.None);

        Assert.That(writer.Batches.Count, Is.EqualTo(1));
        Assert.That(writer.Batches[0].Measurements.Count, Is.EqualTo(1));
    }

    private static InternalSignalMeasurement CreateMeasurement(string name, string? bucketOverride = null)
    {
        return new InternalSignalMeasurement
        {
            Measurement = name,
            Timestamp = DateTimeOffset.UtcNow,
            Fields = new Dictionary<string, object> { ["value"] = 1L },
            BucketOverride = bucketOverride,
        };
    }

    private static InfluxInternalSignalStore CreateStore(RecordingBatchWriter writer, InfluxIntegrationOptions options)
    {
        return new InfluxInternalSignalStore(
            writer,
            Options.Create(options),
            TimeProvider.System,
            NullLogger<InfluxInternalSignalStore>.Instance);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
                Assert.Fail("Condition not reached in time.");

            await Task.Delay(20);
        }
    }

    private sealed class RecordingBatchWriter : IInfluxBatchWriter
    {
        private readonly object _sync = new();
        private readonly List<BatchRecord> _batches = [];

        public IReadOnlyList<BatchRecord> Batches
        {
            get
            {
                lock (_sync)
                {
                    return _batches.ToList();
                }
            }
        }

        public Task WriteBatchAsync(string bucket, IReadOnlyList<InternalSignalMeasurement> measurements, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                _batches.Add(new BatchRecord(bucket, measurements.ToList()));
            }

            return Task.CompletedTask;
        }
    }

    private sealed record BatchRecord(string Bucket, IReadOnlyList<InternalSignalMeasurement> Measurements);
}
