using HomeCompanion.Integrations.Mqtt;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UnitsNet;
using UnitsNet.Units;

namespace HomeCompanion.Tests;

[TestFixture]
public class MqttPayloadConverterTests
{
    private MqttPayloadConverter _converter = null!;

    private static ValueBase<T> CreateValue<T>() where T : notnull
        => new(NullLoggerFactory.Instance.CreateLogger<ValueBase<T>>());

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

        var success = _converter.TryDecode("ON", CreateValue<bool>(), mapping, out var value);

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

        var success = _converter.TryDecode("Heat", CreateValue<HvacMode>(), mapping, out var value);

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

        var success = _converter.TryDecode("21.5", CreateValue<double>(), mapping, out var value);

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
        var success = _converter.TryDecode(payload, CreateValue<DeviceState>(), mapping, out var value);

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
        var success = _converter.TryDecode(payload, CreateValue<DeviceState>(), mapping, out var value);

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
        var success = _converter.TryDecode(payload, CreateValue<Animal>(), mapping, out var value);

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

    [Test]
    public void TryDecode_RawUtf8_UnitAwareNumeric_Succeeds()
    {
        var mapping = new MqttBusEndpointMapping("main", "home/temp/state")
        {
            Config = new MqttBusMappingConfiguration { PayloadFormat = MqttPayloadFormat.RawUtf8 },
        };

        var value = CreateValue<double>();
        value.Unit = new ValueUnitInfo("Temperature", "DegreeCelsius", "°C");

        var success = _converter.TryDecode("68 °F", value, mapping, out var decoded);

        Assert.That(success, Is.True);
        Assert.That(decoded, Is.TypeOf<double>());
        Assert.That((double)decoded!, Is.EqualTo(20d).Within(0.01));
    }

    [TestCase("22", 22d)]
    [TestCase("22.5", 22.5d)]
    [TestCase("22 \u00B0C", 22d)]
    [TestCase("22.5 \u00B0C", 22.5d)]
    [TestCase("71.6 \u00B0F", 22d)]
    public void TryDecode_RawUtf8_QuantityText_ParsesAndNormalizesToConfiguredUnit(string payload, double expectedCelsius)
    {
        var mapping = new MqttBusEndpointMapping("main", "home/temp/state")
        {
            Config = new MqttBusMappingConfiguration { PayloadFormat = MqttPayloadFormat.RawUtf8 },
        };

        var value = CreateValue<Temperature>();
        value.Unit = new ValueUnitInfo("Temperature", "DegreeCelsius", "\u00B0C");

        var success = _converter.TryDecode(payload, value, mapping, out var decoded);

        Assert.That(success, Is.True);
        Assert.That(decoded, Is.TypeOf<Temperature>());
        var quantity = (Temperature)decoded!;
        Assert.That(quantity.Unit, Is.EqualTo(TemperatureUnit.DegreeCelsius));
        Assert.That(quantity.DegreesCelsius, Is.EqualTo(expectedCelsius).Within(0.01));
    }

    [Test]
    public void TryDecode_RawUtf8_QuantityTargetAsIQuantity_ParsesAndNormalizes()
    {
        var mapping = new MqttBusEndpointMapping("main", "home/temp/state")
        {
            Config = new MqttBusMappingConfiguration { PayloadFormat = MqttPayloadFormat.RawUtf8 },
        };

        var value = CreateValue<UnitsNet.IQuantity>();
        value.Unit = new ValueUnitInfo("Temperature", "DegreeCelsius", "\u00B0C");

        var success = _converter.TryDecode("71.6 \u00B0F", value, mapping, out var decoded);

        Assert.That(success, Is.True);
        Assert.That(decoded, Is.AssignableTo<UnitsNet.IQuantity>());
        var quantity = (UnitsNet.IQuantity)decoded!;
        Assert.That(quantity.QuantityInfo.Name, Is.EqualTo("Temperature"));
        Assert.That(quantity.Unit, Is.EqualTo((Enum)TemperatureUnit.DegreeCelsius));
        Assert.That(quantity.As(TemperatureUnit.DegreeCelsius), Is.EqualTo(22d).Within(0.01));
    }

    [TestCase("22", 22d)]
    [TestCase("22.5", 22.5d)]
    [TestCase("\"22 \u00B0C\"", 22d)]
    [TestCase("\"22.5 \u00B0C\"", 22.5d)]
    [TestCase("\"71.6 \u00B0F\"", 22d)]
    public void TryDecode_JsonScalar_QuantityText_ParsesAndNormalizesToConfiguredUnit(string payload, double expectedCelsius)
    {
        var mapping = new MqttBusEndpointMapping("main", "home/temp/state")
        {
            Config = new MqttBusMappingConfiguration { PayloadFormat = MqttPayloadFormat.JsonScalar },
        };

        var value = CreateValue<Temperature>();
        value.Unit = new ValueUnitInfo("Temperature", "DegreeCelsius", "\u00B0C");

        var success = _converter.TryDecode(payload, value, mapping, out var decoded);

        Assert.That(success, Is.True);
        Assert.That(decoded, Is.TypeOf<Temperature>());
        var quantity = (Temperature)decoded!;
        Assert.That(quantity.Unit, Is.EqualTo(TemperatureUnit.DegreeCelsius));
        Assert.That(quantity.DegreesCelsius, Is.EqualTo(expectedCelsius).Within(0.01));
    }

    [Test]
    public void TryDecode_RawUtf8_QuantityText_NonTemperatureDimension_IsHandledGenerically()
    {
        var mapping = new MqttBusEndpointMapping("main", "home/speed/state")
        {
            Config = new MqttBusMappingConfiguration { PayloadFormat = MqttPayloadFormat.RawUtf8 },
        };

        var value = CreateValue<Speed>();
        value.Unit = new ValueUnitInfo("Speed", "MeterPerSecond", "m/s");

        var success = _converter.TryDecode("36 km/h", value, mapping, out var decoded);

        Assert.That(success, Is.True);
        Assert.That(decoded, Is.TypeOf<Speed>());
        var quantity = (Speed)decoded!;
        Assert.That(quantity.Unit, Is.EqualTo(SpeedUnit.MeterPerSecond));
        Assert.That(quantity.MetersPerSecond, Is.EqualTo(10d).Within(0.01));
    }

    [Test]
    public void TryDecode_RawUtf8_QuantityText_WithIncompatibleDimension_Fails()
    {
        var mapping = new MqttBusEndpointMapping("main", "home/temp/state")
        {
            Config = new MqttBusMappingConfiguration { PayloadFormat = MqttPayloadFormat.RawUtf8 },
        };

        var value = CreateValue<Temperature>();
        value.Unit = new ValueUnitInfo("Temperature", "DegreeCelsius", "\u00B0C");

        var success = _converter.TryDecode("22 m/s", value, mapping, out var decoded);

        Assert.That(success, Is.False);
        Assert.That(decoded, Is.Null);
    }

    [Test]
    public void Encode_RawUtf8_Quantity_PreservesInstanceUnit_ByDefault()
    {
        var mapping = new MqttBusEndpointMapping("main", "home/temp/state")
        {
            Config = new MqttBusMappingConfiguration { PayloadFormat = MqttPayloadFormat.RawUtf8 },
        };

        var source = CreateValue<Temperature>();
        source.Unit = new ValueUnitInfo("Temperature", "DegreeCelsius", "°C");

        var payload = _converter.Encode(Temperature.FromDegreesFahrenheit(68), typeof(Temperature), mapping, source);

        Assert.That(payload, Does.Contain("°F"));
    }

    [Test]
    public void Encode_RawUtf8_Quantity_UsesConfiguredUnit_WhenMappingConfigured()
    {
        var mapping = new MqttBusEndpointMapping("main", "home/temp/state")
        {
            Config = new MqttBusMappingConfiguration
            {
                PayloadFormat = MqttPayloadFormat.RawUtf8,
                OutboundQuantityUnitMode = MqttOutboundQuantityUnitMode.MappingConfiguredUnit,
            },
        };

        var source = CreateValue<Temperature>();
        source.Unit = new ValueUnitInfo("Temperature", "DegreeCelsius", "°C");

        var payload = _converter.Encode(Temperature.FromDegreesFahrenheit(68), typeof(Temperature), mapping, source);

        Assert.That(payload, Does.Contain("°C"));
        Assert.That(payload, Does.Not.Contain("°F"));
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
