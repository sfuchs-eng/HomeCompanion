# HomeCompanion Influx Internal Signals Integration

HomeCompanion.Integrations.Influx provides buffered persistence of internal signals to InfluxDB OSS v2.

The integration registers an implementation of HomeCompanion.Persistence.IInternalSignalStore through extension discovery.

## Configuration

Configure Influx under Influx:

```json
{
  "Influx": {
    "Url": "http://influxdb.local:8086",
    "Organization": "home",
    "Token": "***",
    "DefaultBucket": "homecompanion-internal",
    "FlushIntervalSeconds": 10,
    "MaxQueueSize": 500,
    "RetryCount": 3,
    "RetryDelaySeconds": 2
  }
}
```

Required:

- Url
- Organization
- Token
- DefaultBucket

Validation fails fast at startup when required settings are missing or invalid.

## Runtime behavior

- Enqueue APIs are non-blocking relative to network I/O and write to an in-memory channel.
- Flush is triggered by any of:
  - FlushIntervalSeconds elapsed
  - MaxQueueSize reached
  - application shutdown
- Flush groups measurements by target bucket.
- Bucket selection uses measurement BucketOverride first, otherwise DefaultBucket.
- Failed flush attempts retry with bounded RetryCount and RetryDelaySeconds.
- Expected lifecycle cancellation/disposal behavior during shutdown is handled gracefully.

## Usage from logic modules

Inject HomeCompanion.Persistence.IInternalSignalStore and enqueue one or more measurements.

```csharp
using HomeCompanion.Persistence;

public sealed class ExampleLogic(IInternalSignalStore signalStore)
{
    public async Task RecordAsync(CancellationToken cancellationToken)
    {
        await signalStore.EnqueueAsync(new InternalSignalMeasurement
        {
            Measurement = "logic.temperature_controller",
            Timestamp = DateTimeOffset.UtcNow,
            Tags = new Dictionary<string, string>
            {
                ["logic"] = "temperature-controller",
                ["room"] = "living-room"
            },
            Fields = new Dictionary<string, object>
            {
                ["target_temp"] = 21.5,
                ["is_heating"] = true
            }
        }, cancellationToken);
    }
}
```

Example with bucket override:

```csharp
await signalStore.EnqueueAsync(new InternalSignalMeasurement
{
    Measurement = "logic.diagnostics",
    Timestamp = DateTimeOffset.UtcNow,
    BucketOverride = "homecompanion-diagnostics",
    Fields = new Dictionary<string, object>
    {
        ["queue_depth"] = 42
    }
}, cancellationToken);
```

## Activation and deployment

The extension is discovered only when its assembly is loaded.

- Keep HomeCompanion.Integrations.Influx.dll in the application base directory, or
- place it in the directory configured by HomeCompanion:ExtensionsPath.

If the assembly is absent, no Influx-backed IInternalSignalStore implementation is registered.
