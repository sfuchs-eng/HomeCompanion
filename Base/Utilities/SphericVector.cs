namespace HomeCompanion.Base.Utilities;

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

    public static SphericVector FromRadians(double azimuthRadians, double elevationRadians) =>
        new(azimuthRadians, elevationRadians);

    public static SphericVector FromDegrees(double azimuthDegrees, double elevationDegrees) =>
        new(ToRadians(azimuthDegrees), ToRadians(elevationDegrees));

    public (double Azimuth, double Elevation) ToRadiansPair() => (Azimuth, Elevation);

    public (double Azimuth, double Elevation) ToDegreesPair() => (ToDegrees(Azimuth), ToDegrees(Elevation));

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static double ToDegrees(double radians) => radians * 180.0 / Math.PI;
}
