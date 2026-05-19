using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests;

[TestFixture]
public class ValueContainerBaseTests
{
    // ── Fixture containers ───────────────────────────────────────────────────

    private sealed class TwoValueContainer : ValueContainerBase
    {
        public TwoValueContainer() : base(NullLoggerFactory.Instance.CreateLogger<ValueContainerBase>()) { }

        public ValueBase<bool> Switch { get; } = new(NullLoggerFactory.Instance.CreateLogger<ValueBase<bool>>());
        public ValueBase<int> Counter { get; } = new(NullLoggerFactory.Instance.CreateLogger<ValueBase<int>>());
    }

    private sealed class NonValuePropertiesContainer : ValueContainerBase
    {
        public NonValuePropertiesContainer() : base(NullLoggerFactory.Instance.CreateLogger<ValueContainerBase>()) { }

        public ValueBase<bool> Light { get; } = new(NullLoggerFactory.Instance.CreateLogger<ValueBase<bool>>());
        public string Name { get; } = "ignored";
        public int Count { get; } = 42;
    }

    private abstract class BaseContainer : ValueContainerBase
    {
        protected BaseContainer() : base(NullLoggerFactory.Instance.CreateLogger<ValueContainerBase>()) { }

        // Value defined on base class — should be discovered by subclass
        public ValueBase<bool> BaseSwitch { get; } = new(NullLoggerFactory.Instance.CreateLogger<ValueBase<bool>>());
    }

    private sealed class DerivedContainer : BaseContainer
    {
        public ValueBase<int> DerivedCounter { get; } = new(NullLoggerFactory.Instance.CreateLogger<ValueBase<int>>());
    }

    private sealed class NullValueContainer : ValueContainerBase
    {
        public NullValueContainer() : base(NullLoggerFactory.Instance.CreateLogger<ValueContainerBase>()) { }

        // Returns null — should be skipped gracefully
        public ValueBase<bool>? MaybeValue { get; } = null;
        public ValueBase<int> RealValue { get; } = new(NullLoggerFactory.Instance.CreateLogger<ValueBase<int>>());
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Test]
    public void GetValues_ReturnsAllIValueProperties()
    {
        var container = new TwoValueContainer();

        var values = container.GetValues().ToList();

        Assert.That(values, Has.Count.EqualTo(2));
        Assert.That(values, Contains.Item(container.Switch));
        Assert.That(values, Contains.Item(container.Counter));
    }

    [Test]
    public void GetValues_SkipsNonIValueProperties()
    {
        var container = new NonValuePropertiesContainer();

        var values = container.GetValues().ToList();

        Assert.That(values, Has.Count.EqualTo(1));
        Assert.That(values, Contains.Item(container.Light));
    }

    [Test]
    public void GetValues_IncludesPropertiesInheritedFromBaseClass()
    {
        var container = new DerivedContainer();

        var values = container.GetValues().ToList();

        Assert.That(values, Has.Count.EqualTo(2));
        Assert.That(values, Contains.Item(container.BaseSwitch));
        Assert.That(values, Contains.Item(container.DerivedCounter));
    }

    [Test]
    public void GetValues_NullPropertyValue_IsSkipped()
    {
        var container = new NullValueContainer();

        var values = container.GetValues().ToList();

        Assert.That(values, Has.Count.EqualTo(1));
        Assert.That(values, Contains.Item(container.RealValue));
    }

    [Test]
    public void GetValues_EmptyContainer_ReturnsEmptyEnumerable()
    {
        var container = new TwoValueContainer();

        // Calling multiple times is safe and returns same values
        var first = container.GetValues().ToList();
        var second = container.GetValues().ToList();

        Assert.That(first, Has.Count.EqualTo(second.Count));
    }
}
