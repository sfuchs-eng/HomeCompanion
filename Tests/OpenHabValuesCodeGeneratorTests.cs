using HomeCompanion.Integrations.OpenHab;
using HomeCompanion.Support.OpenHab;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HomeCompanion.Tests;

[TestFixture]
public class OpenHabValuesCodeGeneratorTests
{
    [Test]
    public void Generate_NumberDimension_UsesUnitsNetQuantityTypeAndMappingConfig()
    {
        var generator = CreateGenerator();

        var code = generator.Generate([
            new OpenHabItemInfo { Name = "OutdoorTemperature", Type = "Number:Temperature", State = "21.5 C" },
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("ValueBase<global::UnitsNet.Temperature> OutdoorTemperature"));
            Assert.That(code, Does.Contain("new OpenHabBusMappingConfiguration { ItemType = \"Number:Temperature\", StateType = \"Quantity\" }"));
        });
    }

    [Test]
    public void Generate_ItemNameOverride_WinsOverItemTypeOverride()
    {
        var mappingJson = """
        {
          "mappings": [
            {
              "itemType": "Number:Temperature",
              "generatedValueType": "double"
            },
            {
              "itemName": "OutdoorTemperature",
              "generatedValueType": "UnitsNet.Temperature"
            }
          ]
        }
        """;

        var generator = CreateGenerator(mappingJson);

        var code = generator.Generate([
            new OpenHabItemInfo { Name = "OutdoorTemperature", Type = "Number:Temperature", State = "21.5 C" },
        ]);

        Assert.That(code, Does.Contain("ValueBase<global::UnitsNet.Temperature> OutdoorTemperature"));
    }

    [Test]
    public void Generate_ItemTypeOverride_AppliesWhenNoItemNameMatch()
    {
        var mappingJson = """
        {
          "mappings": [
            {
              "itemType": "Number:Pressure",
              "generatedValueType": "double"
            }
          ]
        }
        """;

        var generator = CreateGenerator(mappingJson);

        var code = generator.Generate([
            new OpenHabItemInfo { Name = "BoilerPressure", Type = "Number:Pressure", State = "1.5 bar" },
        ]);

        Assert.That(code, Does.Contain("ValueBase<double> BoilerPressure"));
    }

    [Test]
    public void Generate_ItemNamePatternOverride_AppliesAfterExactChecks()
    {
        var mappingJson = """
        {
          "mappings": [
            {
              "itemNamePattern": ".*_Temp$",
              "generatedValueType": "UnitsNet.Temperature"
            }
          ]
        }
        """;

        var generator = CreateGenerator(mappingJson);

        var code = generator.Generate([
            new OpenHabItemInfo { Name = "Greenhouse_Temp", Type = "Number:Temperature", State = "20.1 C" },
        ]);

        Assert.That(code, Does.Contain("ValueBase<global::UnitsNet.Temperature> Greenhouse_Temp"));
    }

    [Test]
    public void Generate_GeneratedUnitOverride_EmitsValueUnitInfoAssignment()
    {
        var mappingJson = """
        {
          "mappings": [
            {
              "itemName": "OutdoorTemperature",
              "generatedValueType": "UnitsNet.Temperature",
              "generatedUnitQuantityName": "Temperature",
              "generatedUnitName": "DegreeCelsius",
              "generatedUnitSymbol": "°C"
            }
          ]
        }
        """;

        var generator = CreateGenerator(mappingJson);

        var code = generator.Generate([
            new OpenHabItemInfo { Name = "OutdoorTemperature", Type = "Number:Temperature", State = "21.5 C" },
        ]);

        Assert.That(code, Does.Contain("Unit = new ValueUnitInfo(\"Temperature\", \"DegreeCelsius\", \"°C\")"));
    }

    private static OpenHabValuesCodeGenerator CreateGenerator(string? mappingJson = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"homecompanion-openhab-generator-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        if (!string.IsNullOrWhiteSpace(mappingJson))
            File.WriteAllText(Path.Combine(tempDir, "OpenHabTypeMapping.json"), mappingJson);

        var options = Options.Create(new OpenHabIntegrationOptions
        {
            MappingsFolder = tempDir,
            TypeMappingFile = "OpenHabTypeMapping.json",
        });

        return new OpenHabValuesCodeGenerator(
            NullLogger<OpenHabValuesCodeGenerator>.Instance,
            options);
    }
}
