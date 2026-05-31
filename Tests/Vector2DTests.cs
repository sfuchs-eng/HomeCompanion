using HomeCompanion.Base.Utilities;

namespace HomeCompanion.Tests;

[TestFixture]
public class Vector2DTests
{
    [Test]
    public void Constructor_WithCoordinates_SetsXAndY()
    {
        var vector = new Vector2D(3.5, -2.0);

        Assert.That(vector.X, Is.EqualTo(3.5));
        Assert.That(vector.Y, Is.EqualTo(-2.0));
    }

    [Test]
    public void Deconstruct_ReturnsCoordinates()
    {
        var vector = new Vector2D(1.2, 3.4);

        var (x, y) = vector;

        Assert.That(x, Is.EqualTo(1.2));
        Assert.That(y, Is.EqualTo(3.4));
    }

    [Test]
    public void Equals_SameCoordinates_ReturnsTrue()
    {
        var left = new Vector2D(1.0, 2.0);
        var right = new Vector2D(1.0, 2.0);

        Assert.That(left.Equals(right), Is.True);
    }

    [Test]
    public void Equals_DifferentCoordinates_ReturnsFalse()
    {
        var left = new Vector2D(1.0, 2.0);
        var right = new Vector2D(1.0, 3.0);

        Assert.That(left.Equals(right), Is.False);
    }

    [Test]
    public void Equals_NonVectorObject_ReturnsFalse()
    {
        var vector = new Vector2D(1.0, 2.0);

        Assert.That(vector.Equals("not-a-vector"), Is.False);
    }

    [Test]
    public void GetHashCode_EqualVectors_HaveEqualHashCode()
    {
        var left = new Vector2D(8.0, -4.5);
        var right = new Vector2D(8.0, -4.5);

        Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
    }

    [Test]
    public void EqualityOperator_SameCoordinates_ReturnsTrue()
    {
        var left = new Vector2D(5.0, 7.0);
        var right = new Vector2D(5.0, 7.0);

        Assert.That(left == right, Is.True);
    }

    [Test]
    public void InequalityOperator_DifferentCoordinates_ReturnsTrue()
    {
        var left = new Vector2D(5.0, 7.0);
        var right = new Vector2D(5.0, 8.0);

        Assert.That(left != right, Is.True);
    }

    [Test]
    public void AdditionOperator_AddsCoordinates()
    {
        var left = new Vector2D(1.5, -2.0);
        var right = new Vector2D(2.5, 4.0);

        var result = left + right;

        Assert.That(result.X, Is.EqualTo(4.0));
        Assert.That(result.Y, Is.EqualTo(2.0));
    }

    [Test]
    public void SubtractionOperator_SubtractsCoordinates()
    {
        var left = new Vector2D(10.0, 3.0);
        var right = new Vector2D(2.0, 5.0);

        var result = left - right;

        Assert.That(result.X, Is.EqualTo(8.0));
        Assert.That(result.Y, Is.EqualTo(-2.0));
    }

    [Test]
    public void MultiplicationOperator_VectorTimesScalar_ScalesCoordinates()
    {
        var vector = new Vector2D(3.0, -4.0);

        var result = vector * 2.5;

        Assert.That(result.X, Is.EqualTo(7.5));
        Assert.That(result.Y, Is.EqualTo(-10.0));
    }

    [Test]
    public void MultiplicationOperator_ScalarTimesVector_ScalesCoordinates()
    {
        var vector = new Vector2D(3.0, -4.0);

        var result = 2.5 * vector;

        Assert.That(result.X, Is.EqualTo(7.5));
        Assert.That(result.Y, Is.EqualTo(-10.0));
    }

    [Test]
    public void AngleTo_ReturnsAtan2AngleInRadians()
    {
        var origin = new Vector2D(0.0, 0.0);

        var angleRight = origin.AngleTo(new Vector2D(1.0, 0.0));
        var angleUp = origin.AngleTo(new Vector2D(0.0, 1.0));
        var angleLeft = origin.AngleTo(new Vector2D(-1.0, 0.0));

        Assert.That(angleRight, Is.EqualTo(0.0).Within(1e-12));
        Assert.That(angleUp, Is.EqualTo(Math.PI / 2.0).Within(1e-12));
        Assert.That(angleLeft, Is.EqualTo(Math.PI).Within(1e-12));
    }
}
