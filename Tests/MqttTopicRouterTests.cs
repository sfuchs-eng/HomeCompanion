using HomeCompanion.Integrations.Mqtt;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests;

[TestFixture]
public class MqttTopicRouterTests
{
    [Test]
    public void TryResolve_ExactMatch_WinsOverWildcard()
    {
        var exactValue = CreateValue<bool>("Exact");
        var wildcardValue = CreateValue<bool>("Wildcard");

        var exact = new MqttBusEndpointMapping("main", "home/living/temp/state");
        var wildcard = new MqttBusEndpointMapping("main", "home/+/temp/state");

        var router = new MqttTopicRouter(
        [
            new MqttValueMapping(exactValue, exact, 0),
            new MqttValueMapping(wildcardValue, wildcard, 1),
        ]);

        var found = router.TryResolve("home/living/temp/state", out var selection);

        Assert.That(found, Is.True);
        Assert.That(selection, Is.Not.Null);
        Assert.That(selection!.Value, Is.SameAs(exactValue));
    }

    [Test]
    public void TryResolve_WildcardSpecificity_PrefersMoreFixedSegments()
    {
        var specificValue = CreateValue<float>("Specific");
        var broadValue = CreateValue<float>("Broad");

        var specific = new MqttBusEndpointMapping("main", "home/+/temperature/state");
        var broad = new MqttBusEndpointMapping("main", "home/#");

        var router = new MqttTopicRouter(
        [
            new MqttValueMapping(specificValue, specific, 0),
            new MqttValueMapping(broadValue, broad, 1),
        ]);

        var found = router.TryResolve("home/living/temperature/state", out var selection);

        Assert.That(found, Is.True);
        Assert.That(selection, Is.Not.Null);
        Assert.That(selection!.Value, Is.SameAs(specificValue));
    }

    [Test]
    public void TryResolve_Priority_BreaksWildcardTies()
    {
        var lowPriority = CreateValue<int>("LowPriority");
        var highPriority = CreateValue<int>("HighPriority");

        var low = new MqttBusEndpointMapping("main", "lab/+/state")
        {
            Config = new MqttBusMappingConfiguration { Priority = 1 },
        };
        var high = new MqttBusEndpointMapping("main", "lab/+/state")
        {
            Config = new MqttBusMappingConfiguration { Priority = 10 },
        };

        var router = new MqttTopicRouter(
        [
            new MqttValueMapping(lowPriority, low, 0),
            new MqttValueMapping(highPriority, high, 1),
        ]);

        var found = router.TryResolve("lab/device-1/state", out var selection);

        Assert.That(found, Is.True);
        Assert.That(selection, Is.Not.Null);
        Assert.That(selection!.Value, Is.SameAs(highPriority));
    }

    [Test]
    public void TryResolve_RegistrationOrder_BreaksFinalTie()
    {
        var first = CreateValue<int>("First");
        var second = CreateValue<int>("Second");

        var mappingA = new MqttBusEndpointMapping("main", "tie/+/state");
        var mappingB = new MqttBusEndpointMapping("main", "tie/+/state");

        var router = new MqttTopicRouter(
        [
            new MqttValueMapping(first, mappingA, 0),
            new MqttValueMapping(second, mappingB, 1),
        ]);

        var found = router.TryResolve("tie/value/state", out var selection);

        Assert.That(found, Is.True);
        Assert.That(selection, Is.Not.Null);
        Assert.That(selection!.Value, Is.SameAs(first));
    }

    [Test]
    public void TryResolve_CommandTopic_ProducesCommandRouteKind()
    {
        var value = CreateValue<bool>("Switch");
        var mapping = new MqttBusEndpointMapping(
            brokerName: "main",
            stateTopicFilter: "home/switch/state",
            commandTopic: "home/switch/cmd");

        var router = new MqttTopicRouter([new MqttValueMapping(value, mapping, 0)]);

        var found = router.TryResolve("home/switch/cmd", out var selection);

        Assert.That(found, Is.True);
        Assert.That(selection, Is.Not.Null);
        Assert.That(selection!.RouteKind, Is.EqualTo(MqttRouteKind.Command));
    }

    private static ValueBase<T> CreateValue<T>(string name)
    {
        return new ValueBase<T>(NullLogger<ValueBase<T>>.Instance)
        {
            Name = name,
        };
    }
}
