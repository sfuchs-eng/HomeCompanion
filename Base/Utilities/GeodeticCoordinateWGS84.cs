using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeCompanion.Base.Utilities;

/// <summary>
/// Represents a geodetic WGS84 coordinate with longitude and latitude in degrees, and altitude in meters.
/// </summary>
/// <remarks>
/// JSON serialization uses the property names <c>longitude</c>, <c>latitude</c>, and <c>altitude</c>.
/// </remarks>
public class GeodeticCoordinateWGS84 : IEquatable<GeodeticCoordinateWGS84>
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public GeodeticCoordinateWGS84()
    {
    }

    [JsonConstructor]
    public GeodeticCoordinateWGS84(double longitude, double latitude, double altitude = 0)
    {
        Longitude = longitude;
        Latitude = latitude;
        Altitude = altitude;
    }

    /// <summary>
    /// Gets or sets the longitude in degrees, positive eastward.
    /// </summary>
    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }

    /// <summary>
    /// Gets or sets the latitude in degrees, positive northward.
    /// </summary>
    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the altitude in meters above sea level.
    /// </summary>
    [JsonPropertyName("altitude")]
    public double Altitude { get; set; }

    /// <summary>
    /// Serializes this coordinate to JSON.
    /// </summary>
    public string ToJson(JsonSerializerOptions? options = null) => JsonSerializer.Serialize(this, options ?? DefaultJsonOptions);

    /// <summary>
    /// Deserializes a coordinate from JSON.
    /// </summary>
    public static GeodeticCoordinateWGS84 FromJson(string json, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var serializerOptions = options ?? DefaultJsonOptions;
        return JsonSerializer.Deserialize<GeodeticCoordinateWGS84>(json, serializerOptions)
            ?? throw new JsonException("Failed to deserialize geodetic coordinate from JSON.");
    }

    /// <summary>
    /// Attempts to deserialize a coordinate from JSON.
    /// </summary>
    public static bool TryFromJson(string json, out GeodeticCoordinateWGS84? coordinate, JsonSerializerOptions? options = null)
    {
        coordinate = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            coordinate = FromJson(json, options);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public bool Equals(GeodeticCoordinateWGS84? other)
    {
        if (other is null)
        {
            return false;
        }

        return Longitude == other.Longitude && Latitude == other.Latitude && Altitude == other.Altitude;
    }

    public override bool Equals(object? obj) => obj is GeodeticCoordinateWGS84 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Longitude, Latitude, Altitude);
}