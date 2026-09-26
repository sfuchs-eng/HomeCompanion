using HomeCompanion.Integrations.OpenHab;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SRF.Network.OpenHab.Items;
using UnitsNet;

namespace HomeCompanion.Tests;

[TestFixture]
public class OpenHabStateConverterTests
{
    private static ValueBase<T> ValueWithoutKnxMapping<T>() where T : notnull
        => new(NullLogger<ValueBase<T>>.Instance);

    private static ValueBase<T> ValueWithOpenHabMapping<T>(string itemName, OpenHabBusMappingConfiguration? config = null) where T : notnull
        => new(NullLogger<ValueBase<T>>.Instance)
        {
            BusMappings = new() { [OpenHabBusEndpointMapping.BusId] = new OpenHabBusEndpointMapping(itemName, config) },
        };

    private static OpenHabStateConverter CreateConverter(OpenHabIntegrationOptions? options = null)
    {
        var registry = new OpenHabTypeConversionRegistry(
            Options.Create(options ?? new OpenHabIntegrationOptions()),
            NullLogger<OpenHabTypeConversionRegistry>.Instance);

        return new OpenHabStateConverter(registry, NullLogger<OpenHabStateConverter>.Instance);
    }

    [Test]
    public void TryConvertValue_WithOnOffStateType_ReturnsTrueForOn()
    {
        var converter = CreateConverter();
        var value = ValueWithoutKnxMapping<bool>();

        var result = converter.TryConvertValue("ON", value, "OnOff", null, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.EqualTo(true));
    }

    [Test]
    public void TryConvertValue_WithOpenClosedStateType_UsesReversedBooleanMapping()
    {
        var converter = CreateConverter();
        var value = ValueWithoutKnxMapping<bool>();

        var result = converter.TryConvertValue("OPEN", value, "OpenClosed", null, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.EqualTo(false));
    }

    [Test]
    public void TryConvertValue_WithSwitchItemMetadata_InfersOnOffStateType()
    {
        var converter = CreateConverter();
        var value = ValueWithoutKnxMapping<bool>();
        var item = new Item { Name = "MySwitch", Type = "Switch", State = "OFF" };

        var result = converter.TryConvertValue("OFF", value, stateType: null, item, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.EqualTo(false));
    }

    [Test]
    public void TryConvertValue_WithUnitMetadata_ParsesQuantityAwareScalar()
    {
        var converter = CreateConverter();
        var value = ValueWithoutKnxMapping<double>();
        value.Unit = new ValueUnitInfo("Speed", "MeterPerSecond", "m/s");

        var result = converter.TryConvertValue("12 m/s", value, "Quantity", null, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.EqualTo(12d).Within(0.0001d));
    }

    [Test]
    public void TryConvertValue_WithQuantityTarget_UsesTargetParser()
    {
        var converter = CreateConverter();
        var value = ValueWithoutKnxMapping<Temperature>();
        value.Unit = new ValueUnitInfo("Temperature", "DegreeCelsius", "°C");

        var result = converter.TryConvertValue("21.5 °C", value, "Quantity", null, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.TypeOf<Temperature>());
        Assert.That(((Temperature)converted!).DegreesCelsius, Is.EqualTo(21.5).Within(0.001));
    }

    [Test]
    public void TryConvertValue_WithLocalLiteralOverride_UsesConfiguredMapping()
    {
        var converter = CreateConverter();
        var value = ValueWithOpenHabMapping<int>("MyMode", new OpenHabBusMappingConfiguration
        {
            LiteralMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AUTO"] = "2",
            },
        });

        var result = converter.TryConvertValue("AUTO", value, stateType: null, itemMetadata: null, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.EqualTo(2));
    }

    [Test]
    public void TryConvertValue_WithSharedLiteralOverride_UsesRegistryFile()
    {
        var tempDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "OpenHabTypeMapping.json"), """
            {
              "mappings": [
                {
                  "stateType": "String",
                  "targetType": "int",
                  "literalMappings": {
                    "AUTO": "7"
                  }
                }
              ]
            }
            """);

            var converter = CreateConverter(new OpenHabIntegrationOptions { MappingsFolder = tempDir });
            var value = ValueWithoutKnxMapping<int>();

            var result = converter.TryConvertValue("AUTO", value, "String", null, out var converted);

            Assert.That(result, Is.True);
            Assert.That(converted, Is.EqualTo(7));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public void TryConvertValue_WithoutOpenHabSemantics_FallsBackToTargetParser()
    {
        var converter = CreateConverter();
        var value = ValueWithoutKnxMapping<double>();

        var result = converter.TryConvertValue("0", value, out var converted);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(converted, Is.EqualTo(0d));
        });
    }

    [Test]
    public void TryConvertValue_WithCanonicalOnOffLiteral_InfersBooleanState()
    {
        var converter = CreateConverter();
        var value = ValueWithoutKnxMapping<bool>();

        var result = converter.TryConvertValue("ON", value, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.EqualTo(true));
    }

    [TestCase("0.0", false)]
    [TestCase("1.0", true)]
    [TestCase("14.0", true)]
    public void TryConvertValue_WithFloatingNumericBooleanTarget_ConvertsToExpectedBoolean(string raw, bool expected)
    {
        var converter = CreateConverter();
        var value = ValueWithoutKnxMapping<bool>();

        var result = converter.TryConvertValue(raw, value, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.EqualTo(expected));
    }

    [Test]
    public void TryConvertValue_WithFloatingNumericAndByteTarget_ParsesIntegralByte()
    {
        var converter = CreateConverter();
        var value = ValueWithoutKnxMapping<byte>();

        var result = converter.TryConvertValue("14.0", value, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.TypeOf<byte>());
        Assert.That(converted, Is.EqualTo((byte)14));
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"homecompanion-openhab-converter-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
