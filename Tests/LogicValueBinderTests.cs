using HomeCompanion.Core;
using HomeCompanion.Core.Logics;
using HomeCompanion.Logics;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HomeCompanion.Tests;

[TestFixture]
public class LogicValueBinderTests
{
    [Test]
    public void Bind_AttributeOnly_AssignsProperty()
    {
        var container = new LogicTestValuesContainer();
        var resolver = new ValueReferenceProvider([container], NullLogger<ValueReferenceProvider>.Instance);
        var options = Options.Create(new CoreOptions());
        var sut = new LogicValueBinder(resolver, options, NullLogger<LogicValueBinder>.Instance);
        var logic = new AttributeBoundLogic();

        sut.Bind(logic);

        Assert.That(logic.Flag, Is.SameAs(container.Flag));
    }

    [Test]
    public void Bind_ConfigAndAttribute_ConfigWins()
    {
        var container = new LogicTestValuesContainer();
        var resolver = new ValueReferenceProvider([container], NullLogger<ValueReferenceProvider>.Instance);
        var coreOptions = new CoreOptions();
        coreOptions.LogicValueBindings[$"{nameof(OverrideBoundLogic)}.{nameof(OverrideBoundLogic.Flag)}"] = "LogicTestValuesContainer[TestValuesContainer]:OverrideFlag";

        var sut = new LogicValueBinder(resolver, Options.Create(coreOptions), NullLogger<LogicValueBinder>.Instance);
        var logic = new OverrideBoundLogic();

        sut.Bind(logic);

        Assert.That(logic.Flag, Is.SameAs(container.OverrideFlag));
    }

    [Test]
    public void Bind_TypeMismatch_Throws()
    {
        var container = new LogicTestValuesContainer();
        var resolver = new ValueReferenceProvider([container], NullLogger<ValueReferenceProvider>.Instance);
        var options = Options.Create(new CoreOptions());
        var sut = new LogicValueBinder(resolver, options, NullLogger<LogicValueBinder>.Instance);
        var logic = new MismatchBoundLogic();

        Assert.That(() => sut.Bind(logic), Throws.InvalidOperationException.With.Message.Contains("not assignable"));
    }

    private static ValueBase<T> CreateValue<T>(string name)
        => new(NullLogger<ValueBase<T>>.Instance) { Name = name };

    private sealed class LogicTestValuesContainer : IValuesContainer
    {
        public string Name => "TestValuesContainer";

        public ValueBase<bool> Flag { get; } = CreateValue<bool>("Flag");
        public ValueBase<bool> OverrideFlag { get; } = CreateValue<bool>("OverrideFlag");
        public ValueBase<int> Counter { get; } = CreateValue<int>("Counter");

        public IEnumerable<IValue> GetValues()
        {
            yield return Flag;
            yield return OverrideFlag;
            yield return Counter;
        }
    }

    private sealed class AttributeBoundLogic : ILogic
    {
        [ValueBinding("LogicTestValuesContainer[TestValuesContainer]:Flag")]
        public IValue<bool>? Flag { get; set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnableAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisableAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool IsEnabled => true;
    }

    private sealed class OverrideBoundLogic : ILogic
    {
        [ValueBinding("LogicTestValuesContainer[TestValuesContainer]:Flag")]
        public IValue<bool>? Flag { get; set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnableAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisableAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool IsEnabled => true;
    }

    private sealed class MismatchBoundLogic : ILogic
    {
        [ValueBinding("LogicTestValuesContainer[TestValuesContainer]:Counter")]
        public IValue<bool>? Flag { get; set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnableAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisableAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool IsEnabled => true;
    }
}
