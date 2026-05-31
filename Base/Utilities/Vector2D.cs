namespace HomeCompanion.Base.Utilities;

public class Vector2D
{
    public Vector2D()
    {
    }

    public Vector2D(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; set; }

    public double Y { get; set; }

    public double Length => Math.Sqrt((X * X) + (Y * Y));

    public void Deconstruct(out double x, out double y)
    {
        x = X;
        y = Y;
    }

    public override string ToString() => $"({X}, {Y})";

    public override bool Equals(object? obj)
    {
        if (obj is not Vector2D other)
            return false;

        return X == other.X && Y == other.Y;
    }

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public static bool operator ==(Vector2D left, Vector2D right) => left.Equals(right);

    public static bool operator !=(Vector2D left, Vector2D right) => !(left == right);

    public static Vector2D operator +(Vector2D left, Vector2D right) => new(left.X + right.X, left.Y + right.Y);

    public static Vector2D operator -(Vector2D left, Vector2D right) => new(left.X - right.X, left.Y - right.Y);

    public static Vector2D operator *(Vector2D vector, double scalar) => new(vector.X * scalar, vector.Y * scalar);

    public static Vector2D operator *(double scalar, Vector2D vector) => vector * scalar;

    // Get the angle between this and another vector in radians
    public double AngleTo(Vector2D other) => Math.Atan2(other.Y - Y, other.X - X);
}
