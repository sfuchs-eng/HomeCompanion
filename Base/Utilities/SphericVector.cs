namespace HomeCompanion.Base.Utilities;

/// <summary>
/// Spheric vector representation with azimuth and elevation angles <b>in radians.</b>
/// Azimuth is the angle in the horizontal plane, measured from the north direction (0 radians) clockwise to the east direction (π/2 radians).
/// Elevation is the angle in the vertical plane, measured from the horizontal plane (0 radians) upwards to the zenith (π/2 radians).
/// </summary>
/// <remarks>
/// Use the <see cref="FromDegrees(double, double)"/> or <see cref="FromRadians(double, double)"/> static methods to create instances of this class.
/// </remarks>
public class SphericVector : Vector2D
{
    public SphericVector()
    {
    }

    public SphericVector(double azimuthRadians, double elevationRadians)
        : base(azimuthRadians, elevationRadians)
    {
    }

    public double Azimuth
    {
        get => X;
        set => X = value;
    }

    public double Elevation
    {
        get => Y;
        set => Y = value;
    }

    public static SphericVector SunDefaultPosition => FromDegrees(0.0, -10.0); // default sun position below horizon (azimuth 0, elevation -10 degrees)

    public static SphericVector FromRadians(double azimuthRadians, double elevationRadians) =>
        new(azimuthRadians, elevationRadians);

    public static SphericVector FromDegrees(double azimuthDegrees, double elevationDegrees) =>
        new(ToRadians(azimuthDegrees), ToRadians(elevationDegrees));

    public (double Azimuth, double Elevation) ToRadiansPair() => (Azimuth, Elevation);

    public (double Azimuth, double Elevation) ToDegreesPair() => (ToDegrees(Azimuth), ToDegrees(Elevation));

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static double ToDegrees(double radians) => radians * 180.0 / Math.PI;

    internal static double AngleBetween(SphericVector reference, SphericVector other)
    {
        double e1 = reference.Elevation;
        double e2 = other.Elevation;
        double a1 = reference.Azimuth;
        double a2 = other.Azimuth;

        // use the Harversine formula to compute the angle between the two spheric vectors
        double a = Math.Pow(Math.Sin((e2 - e1) / 2), 2) + Math.Cos(e1) * Math.Cos(e2) * Math.Pow(Math.Sin((a2 - a1) / 2), 2);
        double theta = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return theta;
    }
}

public class SphericVectorComparer : IEqualityComparer<SphericVector>
{
    private double tolerance;
    private bool simpleBoxEquality = false;

    public SphericVectorComparer(double toleranceRad)
    {
        this.tolerance = toleranceRad;
    }

    public SphericVectorComparer()
    {
        this.tolerance = double.Epsilon;
    }

    public SphericVectorComparer SetToleranceDeg(double toleranceDeg)
    {
        this.tolerance = toleranceDeg * Math.PI / 180.0;
        return this;
    }

    public SphericVectorComparer UseSimpleBoxEquality()
    {
        simpleBoxEquality = true;
        return this;
    }

    public SphericVectorComparer UseSphericEquality()
    {
        simpleBoxEquality = false;
        return this;
    }

    public bool Equals(SphericVector? x, SphericVector? y)
    {
        return simpleBoxEquality ? EqualsBox(x, y) : EqualsSpheric(x, y);
    }

    public bool EqualsSpheric(SphericVector? x, SphericVector? y)
    {
        if (x is null || y is null) return false;
        return x.AngleTo(y) <= tolerance;
    }

    public bool EqualsBox(SphericVector? x, SphericVector? y)
    {
        if (x is null || y is null) return false;
        return Math.Abs(x.Azimuth - y.Azimuth) <= tolerance && Math.Abs(x.Elevation - y.Elevation) <= tolerance;
    }

    public int GetHashCode(SphericVector obj)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 23 + obj.Azimuth.GetHashCode();
            hash = hash * 23 + obj.Elevation.GetHashCode();
            return hash;
        }
    }
}