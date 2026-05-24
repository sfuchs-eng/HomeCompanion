using HomeCompanion.Integrations.Mqtt;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests;

[TestFixture]
public class MqttPayloadConverterTests
{
    private MqttPayloadConverter _converter = null!;

    [SetUp]
    public void SetUp()
    {
        _converter = new MqttPayloadConverter(NullLogger<MqttPayloadConverter>.Instance);
    }

    [Test]
    public void TryDecode_RawUtf8_BooleanOnOff_Succeeds()
    {
        var mapping = new MqttBusEndpointMapping("main", "home/switch/state")
        {
            Config = new MqttBusMappingConfiguration { PayloadFormat = MqttPayloadFormat.RawUtf8 },
        };

        var success = _converter.TryDecode("ON", typeof(bool), mapping, out var value);

        Assert.That(success, Is.True);
        Assert.That(value, Is.EqualTo(true));
    }

    [Test]
    public void TryDecode_RawUtf8_Enum_Succeeds()
    {
        var mapping = new MqttBusEndpointMapping("main", "home/mode/state")
        {
            Config = new MqttBusMappingConfiguration { PayloadFormat = MqttPayloadFormat.RawUtf8 },
        };

        var success = _converter.TryDecode("Heat", typeof(HvacMode), mapping, out var value);

        Assert.That(success, Is.True);
        Assert.That(value, Is.EqualTo(HvacMode.Heat));
    }

    [Test]
    public void TryDecode_JsonScalar_Numeric_Succeeds()
    {
        var mapping = new MqttBusEndpointMapping("main", "home/temp/state")
        {
            Config = new MqttBusMappingConfiguration
            {
                PayloadFormat = MqttPayloadFormat.JsonScalar,
            },
        };

        var success = _converter.TryDecode("21.5", typeof(double), mapping, out var value);

        Assert.That(success, Is.True);
        Assert.That(value, Is.EqualTo(21.5d));
    }

    [Test]
    public void TryDecode_Json_LenientUnknownFields_Succeeds()
    {
        var mapping = new MqttBusEndpointMapping("main", "home/device/state")
        {
            Config = new MqttBusMappingConfiguration
            {
                PayloadFormat = MqttPayloadFormat.Json,
                StrictJson = false,
            },
        };

        var payload = "{\"id\":\"dev-1\",\"state\":\"ok\",\"unknown\":123}";
        var success = _converter.TryDecode(payload, typeof(DeviceState), mapping, out var value);

        Assert.That(success, Is.True);
        Assert.That(value, Is.TypeOf<DeviceState>());
        var typed = (DeviceState)value!;
        Assert.That(typed.Id, Is.EqualTo("dev-1"));
        Assert.That(typed.State, Is.EqualTo("ok"));
    }

    [Test]
    public void TryDecode_Json_StrictUnknownFields_Fails()
    {
        var mapping = new MqttBusEndpointMapping("main", "home/device/state")
        {
            Config = new MqttBusMappingConfiguration
            {
                PayloadFormat = MqttPayloadFormat.Json,
                StrictJson = true,
            },
        };

        var payload = "{\"id\":\"dev-1\",\"state\":\"ok\",\"unknown\":123}";
        var success = _converter.TryDecode(payload, typeof(DeviceState), mapping, out var value);

        Assert.That(success, Is.False);
        Assert.That(value, Is.Null);
    }

    [Test]
    public void TryDecode_Json_PolymorphicAllowList_Succeeds()
    {
        var mapping = new MqttBusEndpointMapping("main", "home/animal/state")
        {
            Config = new MqttBusMappingConfiguration
            {
                PayloadFormat = MqttPayloadFormat.Json,
                TypeDiscriminatorProperty = "$kind",
                DerivedTypes =
                [
                    new MqttDerivedTypeConfiguration { DerivedType = typeof(Dog), Discriminator = "dog" },
                    new MqttDerivedTypeConfiguration { DerivedType = typeof(Cat), Discriminator = "cat" },
                ],
            },
        };

        var payload = "{\"$kind\":\"dog\",\"name\":\"Rex\",\"barks\":true}";
        var success = _converter.TryDecode(payload, typeof(Animal), mapping, out var value);

        Assert.That(success, Is.True);
        Assert.That(value, Is.TypeOf<Dog>());
        Assert.That(((Dog)value!).Name, Is.EqualTo("Rex"));
    }

    [Test]
    public void Encode_RawUtf8_EnumNameByDefault_Succeeds()
    {
        var mapping = new MqttBusEndpointMapping("main", "home/mode/state")
        {
            Config = new MqttBusMappingConfiguration
            {
                PayloadFormat = MqttPayloadFormat.RawUtf8,
                EnumAsNumeric = false,
            },
        };

        var payload = _converter.Encode(HvacMode.Cool, typeof(HvacMode), mapping);

        Assert.That(payload, Is.EqualTo("Cool"));
    }

    [Test]
    public void Encode_RawUtf8_EnumNumeric_WhenConfigured_Succeeds()
    {
        var mapping = new MqttBusEndpointMapping("main", "home/mode/state")
        {
            Config = new MqttBusMappingConfiguration
            {
                PayloadFormat = MqttPayloadFormat.RawUtf8,
                EnumAsNumeric = true,
            },
        };

        var payload = _converter.Encode(HvacMode.Cool, typeof(HvacMode), mapping);

        Assert.That(payload, Is.EqualTo("1"));
    }

    private enum HvacMode
    {
        Heat = 0,
        Cool = 1,
    }

    private sealed class DeviceState
    {
        public string Id { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
    }

    private abstract class Animal
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class Dog : Animal
    {
        public bool Barks { get; init; }
    }

    private sealed class Cat : Animal
    {
        public int Lives { get; init; }
    }
}
