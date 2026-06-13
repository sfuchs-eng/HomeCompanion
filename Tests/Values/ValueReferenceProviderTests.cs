using HomeCompanion.Values;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests.Values;

[TestFixture]
public class ValueReferenceProviderTests
{
    [Test]
    public void TryResolve_ExplicitFormat_ResolvesValue()
    {
        var container = new TestValuesContainer();
        var sut = new ValueReferenceProvider([container], NullLogger<ValueReferenceProvider>.Instance);

        var result = sut.TryResolve("TestValuesContainer[TestValuesContainer]:Position", out var value);

        Assert.That(result, Is.True);
        Assert.That(value, Is.SameAs(container.Position));
    }

    [Test]
    public void TryResolve_DottedFormat_ResolvesValue()
    {
        var container = new TestValuesContainer();
        var sut = new ValueReferenceProvider([container], NullLogger<ValueReferenceProvider>.Instance);

        var result = sut.TryResolve("TestValuesContainer.TestValuesContainer.Position", out var value);

        Assert.That(result, Is.True);
        Assert.That(value, Is.SameAs(container.Position));
    }

    [Test]
    public void Resolve_AmbiguousBareName_Throws()
    {
        var first = new FirstSharedContainer();
        var second = new SecondSharedContainer();
        var sut = new ValueReferenceProvider([first, second], NullLogger<ValueReferenceProvider>.Instance);

        Assert.That(() => sut.Resolve("Shared"), Throws.InvalidOperationException.With.Message.Contains("ambiguous"));
    }

    [Test]
    public void TryResolve_DynamicValueAddedAfterMiss_ResolvesOnSecondLookup()
    {
        var container = new DynamicValuesContainer();
        var sut = new ValueReferenceProvider([container], NullLogger<ValueReferenceProvider>.Instance);

        Assert.That(sut.TryResolve("Dynamic", out _), Is.False);

        container.Add("Dynamic", CreateValue<int>("Dynamic"));

        var result = sut.TryResolve("Dynamic", out var value);

        Assert.That(result, Is.True);
        Assert.That(value, Is.Not.Null);
        Assert.That(value!.Name, Is.EqualTo("Dynamic"));
    }

    private static ValueBase<T> CreateValue<T>(string name)
    {
        return new ValueBase<T>(NullLogger<ValueBase<T>>.Instance)
        {
            Name = name,
        };
    }

    private sealed class TestValuesContainer : IValuesContainer
    {
        public ValueBase<double> Position { get; } = CreateValue<double>("Position");

        public ValueBase<double> Angle { get; } = CreateValue<double>("Angle");

        public IEnumerable<IValue> GetValues()
        {
            yield return Position;
            yield return Angle;
        }
    }

    private sealed class FirstSharedContainer : IValuesContainer
    {
        public ValueBase<int> Shared { get; } = CreateValue<int>("Shared");

        public IEnumerable<IValue> GetValues()
        {
            yield return Shared;
        }
    }

    private sealed class SecondSharedContainer : IValuesContainer
    {
        public ValueBase<int> Shared { get; } = CreateValue<int>("Shared");

        public IEnumerable<IValue> GetValues()
        {
            yield return Shared;
        }
    }

    private sealed class DynamicValuesContainer : IValuesContainer
    {
        private readonly Dictionary<string, IValue> _values = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string key, IValue value)
        {
            _values[key] = value;
        }

        public IEnumerable<IValue> GetValues() => _values.Values;
    }
}
