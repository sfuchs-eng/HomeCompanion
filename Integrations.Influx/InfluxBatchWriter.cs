using HomeCompanion.Persistence;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client;
using InfluxDB.Client.Writes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeCompanion.Integrations.Influx;

internal interface IInfluxBatchWriter
{
    Task WriteBatchAsync(string bucket, IReadOnlyList<InternalSignalMeasurement> measurements, CancellationToken cancellationToken);
}

internal sealed class InfluxBatchWriter : IInfluxBatchWriter, IDisposable
{
    private readonly InfluxDBClient _client;
    private readonly InfluxIntegrationOptions _options;
    private readonly ILogger<InfluxBatchWriter> _logger;

    public InfluxBatchWriter(IOptions<InfluxIntegrationOptions> options, ILogger<InfluxBatchWriter> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new InfluxDBClient(_options.Url, _options.Token);
    }

    public async Task WriteBatchAsync(string bucket, IReadOnlyList<InternalSignalMeasurement> measurements, CancellationToken cancellationToken)
    {
        if (measurements.Count == 0)
            return;

        var points = new List<PointData>(measurements.Count);
        foreach (var measurement in measurements)
        {
            if (TryCreatePoint(measurement, out var point))
                points.Add(point);
        }

        if (points.Count == 0)
            return;

        await _client.GetWriteApiAsync().WritePointsAsync(points, bucket, _options.Organization, cancellationToken);
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private bool TryCreatePoint(InternalSignalMeasurement measurement, out PointData point)
    {
        point = PointData.Measurement(measurement.Measurement)
            .Timestamp(measurement.Timestamp.UtcDateTime, WritePrecision.Ns);

        foreach (var tag in measurement.Tags)
        {
            if (string.IsNullOrWhiteSpace(tag.Key) || string.IsNullOrWhiteSpace(tag.Value))
                continue;

            point = point.Tag(tag.Key, tag.Value);
        }

        var validFieldCount = 0;
        foreach (var field in measurement.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key) || field.Value is null)
                continue;

            if (!TryAddField(field.Key, field.Value, ref point))
                continue;

            validFieldCount++;
        }

        if (validFieldCount > 0)
            return true;

        _logger.LogWarning(
            "Internal signal measurement '{Measurement}' has no valid scalar fields and was dropped.",
            measurement.Measurement);
        return false;
    }

    private static bool TryAddField(string name, object value, ref PointData point)
    {
        switch (value)
        {
            case bool boolValue:
                point = point.Field(name, boolValue);
                return true;
            case byte byteValue:
                point = point.Field(name, byteValue);
                return true;
            case sbyte sbyteValue:
                point = point.Field(name, sbyteValue);
                return true;
            case short shortValue:
                point = point.Field(name, shortValue);
                return true;
            case ushort ushortValue:
                point = point.Field(name, ushortValue);
                return true;
            case int intValue:
                point = point.Field(name, intValue);
                return true;
            case uint uintValue:
                point = point.Field(name, uintValue);
                return true;
            case long longValue:
                point = point.Field(name, longValue);
                return true;
            case ulong ulongValue:
                point = point.Field(name, unchecked((long)ulongValue));
                return true;
            case float floatValue:
                point = point.Field(name, floatValue);
                return true;
            case double doubleValue:
                point = point.Field(name, doubleValue);
                return true;
            case decimal decimalValue:
                point = point.Field(name, Convert.ToDouble(decimalValue));
                return true;
            case string stringValue:
                point = point.Field(name, stringValue);
                return true;
            default:
                return false;
        }
    }
}
