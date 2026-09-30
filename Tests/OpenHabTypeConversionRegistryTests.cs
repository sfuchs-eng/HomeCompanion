using HomeCompanion.Integrations.OpenHab;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SRF.Network.OpenHab.Items;
using UnitsNet;

namespace HomeCompanion.Tests;

[TestFixture]
public class OpenHabTypeConversionRegistryTests
{
    private static ValueBase<T> ValueWithoutKnxMapping<T>() where T : notnull
        => new(NullLogger<ValueBase<T>>.Instance);

    private static ValueBase<T> ValueWithOpenHabMapping<T>(string itemName, OpenHabBusMappingConfiguration? config = null) where T : notnull
        => new(NullLogger<ValueBase<T>>.Instance)
        {
            BusMappings = new() { [OpenHabBusEndpointMapping.BusId] = new OpenHabBusEndpointMapping(itemName, config) },
        };

    private static OpenHabTypeConversionRegistry CreateRegistry(OpenHabIntegrationOptions? options = null)
        => new(
            Options.Create(options ?? new OpenHabIntegrationOptions()),
            NullLogger<OpenHabTypeConversionRegistry>.Instance);

    [Test]
    public void TryConvertValue_WithOnOffStateType_ReturnsTrueForOn()
    {
        var registry = CreateRegistry();
        var value = ValueWithoutKnxMapping<bool>();

        var result = registry.TryConvertValue("ON", value, "OnOff", null, null, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.EqualTo(true));
    }

    [Test]
    public void TryConvertValue_WithOpenClosedStateType_UsesReversedBooleanMapping()
    {
        var registry = CreateRegistry();
        var value = ValueWithoutKnxMapping<bool>();

        var result = registry.TryConvertValue("OPEN", value, "OpenClosed", null, null, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.EqualTo(false));
    }

    [Test]
    public void TryConvertValue_WithSwitchItemMetadata_InfersOnOffStateType()
    {
        var registry = CreateRegistry();
        var value = ValueWithoutKnxMapping<bool>();
        var item = new Item { Name = "MySwitch", Type = "Switch", State = "OFF" };

        var result = registry.TryConvertValue("OFF", value, stateType: null, item, localConfig: null, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.EqualTo(false));
    }

    [Test]
    public void TryConvertValue_WithUnitMetadata_ParsesQuantityAwareScalar()
    {
        var registry = CreateRegistry();
        var value = ValueWithoutKnxMapping<double>();
        value.Unit = new ValueUnitInfo("Speed", "MeterPerSecond", "m/s");

        var result = registry.TryConvertValue("12 m/s", value, "Quantity", null, null, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.EqualTo(12d).Within(0.0001d));
    }

    [Test]
    public void TryConvertValue_WithQuantityTarget_UsesTargetParser()
    {
        var registry = CreateRegistry();
        var value = ValueWithoutKnxMapping<Temperature>();
        value.Unit = new ValueUnitInfo("Temperature", "DegreeCelsius", "°C");

        var result = registry.TryConvertValue("21.5 °C", value, "Quantity", null, null, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.TypeOf<Temperature>());
        Assert.That(((Temperature)converted!).DegreesCelsius, Is.EqualTo(21.5).Within(0.001));
    }

    [Test]
    public void TryConvertValue_WithLocalLiteralOverride_UsesConfiguredMapping()
    {
        var registry = CreateRegistry();
        var value = ValueWithOpenHabMapping<int>("MyMode", new OpenHabBusMappingConfiguration
        {
            LiteralMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AUTO"] = "2",
            },
        });

        var result = registry.TryConvertValue(
            "AUTO",
            value,
            stateType: null,
            itemMetadata: null,
            localConfig: (OpenHabBusMappingConfiguration?)value.BusMappings[OpenHabBusEndpointMapping.BusId].Config,
            out var converted);

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

            var registry = CreateRegistry(new OpenHabIntegrationOptions { MappingsFolder = tempDir });
            var value = ValueWithoutKnxMapping<int>();

            var result = registry.TryConvertValue("AUTO", value, "String", null, null, out var converted);

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
        var registry = CreateRegistry();
        var value = ValueWithoutKnxMapping<double>();

        var result = registry.TryConvertValue("0", value, stateType: null, itemMetadata: null, localConfig: null, out var converted);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(converted, Is.EqualTo(0d));
        });
    }

    [Test]
    public void TryConvertValue_WithCanonicalOnOffLiteral_InfersBooleanState()
    {
        var registry = CreateRegistry();
        var value = ValueWithoutKnxMapping<bool>();

        var result = registry.TryConvertValue("ON", value, stateType: null, itemMetadata: null, localConfig: null, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.EqualTo(true));
    }

    [TestCase("0.0", false)]
    [TestCase("1.0", true)]
    [TestCase("14.0", true)]
    public void TryConvertValue_WithFloatingNumericBooleanTarget_ConvertsToExpectedBoolean(string raw, bool expected)
    {
        var registry = CreateRegistry();
        var value = ValueWithoutKnxMapping<bool>();

        var result = registry.TryConvertValue(raw, value, stateType: null, itemMetadata: null, localConfig: null, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.EqualTo(expected));
    }

    [Test]
    public void TryConvertValue_WithFloatingNumericAndByteTarget_ParsesIntegralByte()
    {
        var registry = CreateRegistry();
        var value = ValueWithoutKnxMapping<byte>();

        var result = registry.TryConvertValue("14.0", value, stateType: null, itemMetadata: null, localConfig: null, out var converted);

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
