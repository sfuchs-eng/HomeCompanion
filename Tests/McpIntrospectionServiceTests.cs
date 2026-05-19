using HomeCompanion.Core.Mcp;
using HomeCompanion.Logics;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests;

[TestFixture]
public class McpIntrospectionServiceTests
{
    private sealed class TestContainer : IValuesContainer
    {
        public ValueBase<bool> Switch { get; } = new(NullLoggerFactory.Instance.CreateLogger<ValueBase<bool>>())
        {
            Name = "switch.main",
            Label = "Main Switch",
        };

        public ValueBase<int> Counter { get; } = new(NullLoggerFactory.Instance.CreateLogger<ValueBase<int>>())
        {
            Name = "counter.main",
            Label = "Main Counter",
        };

        public IEnumerable<IValue> GetValues() => [Switch, Counter];
    }

    private sealed class TestLogic : ILogic
    {
        public bool IsEnabled { get; private set; }

        public TestLogic(bool isEnabled)
        {
            IsEnabled = isEnabled;
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnableAsync(CancellationToken cancellationToken = default)
        {
            IsEnabled = true;
            return Task.CompletedTask;
        }

        public Task DisableAsync(CancellationToken cancellationToken = default)
        {
            IsEnabled = false;
            return Task.CompletedTask;
        }
    }

    private sealed class TestBusMappingConfig(string? valueFormat = null) : IBusMappingConfiguration
    {
        public string? ValueFormat { get; } = valueFormat;
        public string? FormatConfiguration() => "dpt=1.001";
    }

    [Test]
    public void ListValuesContainers_ReturnsRegisteredContainersWithCount()
    {
        var container = new TestContainer();
        var sut = new McpIntrospectionService([container], []);

        var result = sut.ListValuesContainers();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Type, Is.EqualTo(typeof(TestContainer).FullName));
        Assert.That(result[0].ValueCount, Is.EqualTo(2));
    }

    [Test]
    public void ListContainerValueProperties_ReturnsPublicIValueProperties()
    {
        var container = new TestContainer();
        var sut = new McpIntrospectionService([container], []);

        var result = sut.ListContainerValueProperties(typeof(TestContainer).FullName!);

        Assert.That(result.Select(x => x.PropertyName), Is.EquivalentTo(new[] { "Switch", "Counter" }));
        Assert.That(result.Single(x => x.PropertyName == "Switch").Name, Is.EqualTo("switch.main"));
        Assert.That(result.Single(x => x.PropertyName == "Counter").ValueType, Is.EqualTo(typeof(int).FullName));
    }

    [Test]
    public void GetValueInfo_ReturnsMetadataIncludingBusMappingsAndCurrentValue()
    {
        var container = new TestContainer();
        container.Switch.AddBusEndpoint(
            "knx",
            new ValueBusMapping<string, string>("knx", "1/1/1", new TestBusMappingConfig())
            {
                Communication = BusCommunication.RegularCommunication,
            });
        container.Switch.Write(true);

        var sut = new McpIntrospectionService([container], []);

        var result = sut.GetValueInfo(typeof(TestContainer).FullName!, "Switch");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PropertyName, Is.EqualTo("Switch"));
        Assert.That(result.ValueType, Is.EqualTo(typeof(bool).FullName));
        Assert.That(result.Name, Is.EqualTo("switch.main"));
        Assert.That(result.CurrentValue, Is.EqualTo(true));
        Assert.That(result.Status, Does.Contain(nameof(ValueStatus.Initialized)));
        Assert.That(result.BusMappings, Has.Count.EqualTo(1));
        Assert.That(result.BusMappings[0].BusId, Is.EqualTo("knx"));
        Assert.That(result.BusMappings[0].Address, Is.EqualTo("1/1/1"));
        Assert.That(result.BusMappings[0].Config, Is.EqualTo("dpt=1.001"));
    }

    [Test]
    public void GetValueInfo_ReturnsNullForUnknownProperty()
    {
        var container = new TestContainer();
        var sut = new McpIntrospectionService([container], []);

        var result = sut.GetValueInfo(typeof(TestContainer).FullName!, "Missing");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ListLogicInstances_ReturnsTypeAndEnabledFlag()
    {
        var logicA = new TestLogic(isEnabled: true);
        var logicB = new TestLogic(isEnabled: false);
        var sut = new McpIntrospectionService([], [logicA, logicB]);

        var result = sut.ListLogicInstances();

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.All(x => x.Type == typeof(TestLogic).FullName), Is.True);
        Assert.That(result.Count(x => x.IsEnabled), Is.EqualTo(1));
    }
}
