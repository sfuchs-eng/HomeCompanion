using HomeCompanion.Values;
using Moq;

namespace HomeCompanion.Tests.Values;

[TestFixture]
public class IValueExtensionsTests
{
    [TestCase(typeof(IValue<double>), 42.5, 42.5)]
    [TestCase(typeof(IValue<int>), 42, 42.0)]
    [TestCase(typeof(IValue<float>), 42.5f, 42.5)]
    [TestCase(typeof(IValue<string>), "42.5", 42.5)]
    public void TryGetNumericValue_ReturnsTrue_ForDoubleValue(Type type, object value, double expected)
    {
        var mockValue = new Mock<IValue>();
        mockValue.Setup(v => v.OValue).Returns(value);
        mockValue.Setup(v => v.Status).Returns(ValueStatus.Initialized | ValueStatus.Loaded);
        var result = IValueExtensions.TryGetNumericValue(mockValue.Object, out var numeric);
        Assert.That(result, Is.True);
        Assert.That(numeric, Is.EqualTo(expected));
    }

    [TestCase(typeof(IValue<string>), "not a number")]
    public void TryGetNumericValue_ReturnsFalse_ForNonNumericValue(Type type, object value)
    {
        var mockValue = new Mock<IValue>();
        mockValue.Setup(v => v.OValue).Returns(value);
        mockValue.Setup(v => v.Status).Returns(ValueStatus.Initialized | ValueStatus.Loaded);
        var result = IValueExtensions.TryGetNumericValue(mockValue.Object, out var numeric);
        Assert.That(result, Is.False);
        Assert.That(numeric, Is.EqualTo(0));
    }

    /// <summary>
    /// Test the TryGetIntegerValue extension method for various scenarios.
    /// </summary>
    [TestCase(typeof(IValue<int>), 42, 42)]
    [TestCase(typeof(IValue<double>), 42.0, 42)]
    [TestCase(typeof(IValue<float>), 42.0f, 42)]
    [TestCase(typeof(IValue<string>), "42", 42)]
    [TestCase(typeof(IValue<bool>), true, 1)]
    [TestCase(typeof(IValue<bool>), false, 0)]
    [TestCase(typeof(IValue<byte>), (byte)42, 42)]
    [TestCase(typeof(IValue<sbyte>), (sbyte)42, 42)]
    [TestCase(typeof(IValue<short>), (short)42, 42)]
    [TestCase(typeof(IValue<ushort>), (ushort)42, 42)]
    [TestCase(typeof(IValue<long>), (long)42, 42)]
    [TestCase(typeof(IValue<ulong>), (ulong)42, 42)]
    public void TryGetIntegralValue_ReturnsTrue_ForIntegerValue(Type type, object value, int expected)
    {
        var mockValue = new Mock<IValue>();
        mockValue.Setup(v => v.OValue).Returns(value);
        mockValue.Setup(v => v.Status).Returns(ValueStatus.Initialized | ValueStatus.Loaded);
        var result = IValueExtensions.TryGetIntegralValue<int>(mockValue.Object, out var typedValue);
        Assert.That(result, Is.True);
        Assert.That(typedValue, Is.EqualTo(expected));
    }

    /// <summary>
    /// TryGetValue should return false and default value when the type does not match.
    /// </summary>
    /// <param name="type">The type of the IValue.</param>
    /// <param name="value">The value to be returned by OValue.</param>
    /// <param name="expected">The expected default value for the type.</param>
    [TestCase(typeof(IValue<string>), "not a number", 0.0)]
    [TestCase(typeof(IValue<bool>), true, 0.0)]
    [TestCase(typeof(IValue<byte>), (byte)42, 0.0)]
    public void TryGetValue_ReturnsFalse_ForMismatchedType(Type type, object value, double expected)
    {
        var mockValue = new Mock<IValue>();
        mockValue.Setup(v => v.OValue).Returns(value);
        mockValue.Setup(v => v.Status).Returns(ValueStatus.Initialized | ValueStatus.Loaded);
        var result = IValueExtensions.TryGetValue<double>(mockValue.Object, out var typedValue);
        Assert.That(result, Is.False);
        Assert.That(typedValue, Is.EqualTo(expected));
    }
}
