using HomeCompanion.Base.Utilities;

namespace HomeCompanion.Tests;

[TestFixture]
public class SphericVectorTests
{
    [Test]
    public void FromDegrees_ConvertsToRadians()
    {
        var vector = SphericVector.FromDegrees(180.0, 90.0);

        Assert.That(vector.Azimuth, Is.EqualTo(Math.PI).Within(1e-12));
        Assert.That(vector.Elevation, Is.EqualTo(Math.PI / 2.0).Within(1e-12));
    }

    [Test]
    public void ToDegreesPair_ConvertsFromRadians()
    {
        var vector = SphericVector.FromRadians(Math.PI, Math.PI / 2.0);

        var (azimuth, elevation) = vector.ToDegreesPair();

        Assert.That(azimuth, Is.EqualTo(180.0).Within(1e-10));
        Assert.That(elevation, Is.EqualTo(90.0).Within(1e-10));
    }

    [Test]
    public void AzimuthAndElevation_AliasXAndY()
    {
        var vector = new SphericVector();

        vector.X = 1.23;
        vector.Y = 4.56;

        Assert.That(vector.Azimuth, Is.EqualTo(1.23));
        Assert.That(vector.Elevation, Is.EqualTo(4.56));

        vector.Azimuth = 7.89;
        vector.Elevation = 0.12;

        Assert.That(vector.X, Is.EqualTo(7.89));
        Assert.That(vector.Y, Is.EqualTo(0.12));
    }

    [Test]
    public void SphericVector_InheritsVector2D()
    {
        var vector = SphericVector.FromRadians(3.0, 4.0);

        Assert.That(vector, Is.InstanceOf<Vector2D>());
        Assert.That(vector.Length, Is.EqualTo(5.0).Within(1e-12));
    }

    [Test]
    public void SphericComparer_LargeDayNightDifference_IsNotEqual()
    {
        var comparer = new SphericVectorComparer()
            .SetToleranceDeg(2.0)
            .UseSphericEquality();

        var dayLike = SphericVector.FromDegrees(176.0, 25.64);
        var nightLike = SphericVector.FromDegrees(285.43, -38.72);

        Assert.That(comparer.Equals(dayLike, nightLike), Is.False);
    }

    [Test]
    public void SphericComparer_AzimuthWrapAround_WithinTolerance_IsEqual()
    {
        var comparer = new SphericVectorComparer()
            .SetToleranceDeg(2.5)
            .UseSphericEquality();

        var nearNorthWest = SphericVector.FromDegrees(359.0, 0.0);
        var nearNorthEast = SphericVector.FromDegrees(1.0, 0.0);

        Assert.That(comparer.Equals(nearNorthWest, nearNorthEast), Is.True);
    }
}
