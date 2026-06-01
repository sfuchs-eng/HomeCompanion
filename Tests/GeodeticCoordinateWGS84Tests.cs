using HomeCompanion.Base.Utilities;

namespace HomeCompanion.Tests;

[TestFixture]
public class GeodeticCoordinateWGS84Tests
{
    [Test]
    public void Constructor_WithValues_SetsProperties()
    {
        var coordinate = new GeodeticCoordinateWGS84(8.5417, 47.3769, 408.2);

        Assert.That(coordinate.Longitude, Is.EqualTo(8.5417));
        Assert.That(coordinate.Latitude, Is.EqualTo(47.3769));
        Assert.That(coordinate.Altitude, Is.EqualTo(408.2));
    }

    [Test]
    public void ToJson_UsesConfiguredPropertyNames()
    {
        var coordinate = new GeodeticCoordinateWGS84(8.5417, 47.3769, 408.2);

        var json = coordinate.ToJson();

        Assert.That(json, Does.Contain("\"longitude\""));
        Assert.That(json, Does.Contain("\"latitude\""));
        Assert.That(json, Does.Contain("\"altitude\""));
    }

    [Test]
    public void FromJson_DeserializesCoordinate()
    {
        const string json = """
            {
              "longitude": 8.5417,
              "latitude": 47.3769,
              "altitude": 408.2
            }
            """;

        var coordinate = GeodeticCoordinateWGS84.FromJson(json);

        Assert.That(coordinate.Longitude, Is.EqualTo(8.5417).Within(1e-12));
        Assert.That(coordinate.Latitude, Is.EqualTo(47.3769).Within(1e-12));
        Assert.That(coordinate.Altitude, Is.EqualTo(408.2).Within(1e-12));
    }

    [Test]
    public void TryFromJson_WithInvalidJson_ReturnsFalse()
    {
        var success = GeodeticCoordinateWGS84.TryFromJson("{ invalid json }", out var coordinate);

        Assert.That(success, Is.False);
        Assert.That(coordinate, Is.Null);
    }
}