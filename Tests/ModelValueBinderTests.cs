using HomeCompanion.Base.Model;
using HomeCompanion.Core.Model;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests;

[TestFixture]
public class ModelValueBinderTests
{
    [Test]
    public void Bind_BindsShutterValues_UsingAttributes()
    {
        var position = CreateValue<double>("Position");
        var angle = CreateValue<double>("Angle");
        var resolver = new StubValueReferenceProvider(new Dictionary<string, IValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["ref-position"] = position,
            ["ref-angle"] = angle,
        });

        var sut = new ModelValueBinder(resolver, NullLogger<ModelValueBinder>.Instance);
        var model = BuildModelWithSingleShutter(
            new CfgShutter
            {
                PositionValueReference = "ref-position",
                AngleValueReference = "ref-angle",
            },
            out var shutter);

        sut.Bind(model);

        Assert.That(shutter.PositionValue, Is.SameAs(position));
        Assert.That(shutter.AngleValue, Is.SameAs(angle));
    }

    [Test]
    public void Bind_BindsByConvention_WhenNoAttributeIsPresent()
    {
        var probe = CreateValue<bool>("Probe");
        var resolver = new StubValueReferenceProvider(new Dictionary<string, IValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["ref-probe"] = probe,
        });

        var sut = new ModelValueBinder(resolver, NullLogger<ModelValueBinder>.Instance);
        var model = BuildModelWithSpecial(new CfgSpecialWithConvention { ProbeValueReference = "ref-probe" }, out var special);

        sut.Bind(model);

        Assert.That(special.ProbeValue, Is.SameAs(probe));
    }

    [Test]
    public void Bind_BindsByAttributeOverride_WhenConfigPropertyNameDiffers()
    {
        var overrideValue = CreateValue<int>("OverrideValue");
        var resolver = new StubValueReferenceProvider(new Dictionary<string, IValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["ref-override"] = overrideValue,
        });

        var sut = new ModelValueBinder(resolver, NullLogger<ModelValueBinder>.Instance);
        var model = BuildModelWithSpecial(new CfgSpecialWithConvention { AlternateProbeReference = "ref-override" }, out var special);

        sut.Bind(model);

        Assert.That(special.OverriddenProbeValue, Is.SameAs(overrideValue));
    }

    [Test]
    public void Bind_BindsShadowingSpecialReferences_UsingAttributes()
    {
        var globalScene = CreateValue<byte>("GlobalScene");
        var autoShadowStatus = CreateValue<bool>("AutoShadowStatus");
        var absence = CreateValue<bool>("Absence");
        var disableAssessment = CreateValue<bool>("DisableAssessment");
        var outdoorTemperature = CreateValue<float>("OutdoorTemperature");
        var sunEast = CreateValue<float>("SunEast");
        var sunSouth = CreateValue<float>("SunSouth");
        var sunWest = CreateValue<float>("SunWest");

        var resolver = new StubValueReferenceProvider(new Dictionary<string, IValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["ref-global-scene"] = globalScene,
            ["ref-auto-shadow-status"] = autoShadowStatus,
            ["ref-absence"] = absence,
            ["ref-disable-assessment"] = disableAssessment,
            ["ref-outdoor-temperature"] = outdoorTemperature,
            ["ref-sun-east"] = sunEast,
            ["ref-sun-south"] = sunSouth,
            ["ref-sun-west"] = sunWest,
        });

        var sut = new ModelValueBinder(resolver, NullLogger<ModelValueBinder>.Instance);
        var model = BuildModelWithShadowingSpecial(
            new CfgShadowingSpecial
            {
                GlobalShutterSceneReference = "ref-global-scene",
                AutoShadowStatusReference = "ref-auto-shadow-status",
                AbsenceReference = "ref-absence",
                DisableAutoShadowAssessmentReference = "ref-disable-assessment",
                OutdoorTemperatureReference = "ref-outdoor-temperature",
                SunIntensityEastReference = "ref-sun-east",
                SunIntensitySouthReference = "ref-sun-south",
                SunIntensityWestReference = "ref-sun-west",
            },
            out var special);

        sut.Bind(model);

        Assert.That(special.GlobalShutterScene, Is.SameAs(globalScene));
        Assert.That(special.AutoShadowStatus, Is.SameAs(autoShadowStatus));
        Assert.That(special.Absence, Is.SameAs(absence));
        Assert.That(special.DisableAutoShadowAssessment, Is.SameAs(disableAssessment));
        Assert.That(special.OutdoorTemperature, Is.SameAs(outdoorTemperature));
        Assert.That(special.SunIntensityEast, Is.SameAs(sunEast));
        Assert.That(special.SunIntensitySouth, Is.SameAs(sunSouth));
        Assert.That(special.SunIntensityWest, Is.SameAs(sunWest));
    }

    private static ValueBase<T> CreateValue<T>(string name)
        => new(NullLogger<ValueBase<T>>.Instance) { Name = name };

    private static Model BuildModelWithSingleShutter(CfgShutter cfg, out Shutter shutter)
    {
        shutter = new Shutter("Blind", cfg);
        var room = new Room("Living", new CfgRoom())
        {
            Shutters = new Dictionary<string, Shutter>(StringComparer.OrdinalIgnoreCase)
            {
                ["Blind"] = shutter,
            },
        };

        var cfgFloor = new CfgFloor
        {
            Rooms = new Dictionary<string, CfgRoom>(StringComparer.OrdinalIgnoreCase)
            {
                ["Living"] = new CfgRoom(),
            },
        };
        var floor = new Floor("Ground", cfgFloor)
        {
            Rooms = new Dictionary<string, Room>(StringComparer.OrdinalIgnoreCase)
            {
                ["Living"] = room,
            },
        };

        var cfgBuilding = new CfgBuilding
        {
            Floors = new Dictionary<string, CfgFloor>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ground"] = cfgFloor,
            },
        };
        var building = new Building("Main", cfgBuilding)
        {
            Floors = new Dictionary<string, Floor>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ground"] = floor,
            },
        };

        return new Model(new CfgModel())
        {
            Buildings = new Dictionary<string, Building>(StringComparer.OrdinalIgnoreCase)
            {
                ["Main"] = building,
            },
        };
    }

    private static Model BuildModelWithSpecial(CfgSpecialWithConvention cfg, out SpecialWithConvention special)
    {
        special = new SpecialWithConvention("Special", cfg);
        var cfgBuilding = new CfgBuilding();
        var building = new Building("Main", cfgBuilding)
        {
            Specials = new Dictionary<string, IBuildingSpecial>(StringComparer.OrdinalIgnoreCase)
            {
                ["Special"] = special,
            },
        };

        return new Model(new CfgModel())
        {
            Buildings = new Dictionary<string, Building>(StringComparer.OrdinalIgnoreCase)
            {
                ["Main"] = building,
            },
        };
    }

    private static Model BuildModelWithShadowingSpecial(CfgShadowingSpecial cfg, out ShadowingSpecial special)
    {
        special = new ShadowingSpecial("Shadowing", cfg);
        var cfgBuilding = new CfgBuilding();
        var building = new Building("Main", cfgBuilding)
        {
            Specials = new Dictionary<string, IBuildingSpecial>(StringComparer.OrdinalIgnoreCase)
            {
                ["Shadowing"] = special,
            },
        };

        return new Model(new CfgModel())
        {
            Buildings = new Dictionary<string, Building>(StringComparer.OrdinalIgnoreCase)
            {
                ["Main"] = building,
            },
        };
    }

    private sealed class StubValueReferenceProvider(Dictionary<string, IValue> byReference) : IValueProvider
    {
        private readonly Dictionary<string, IValue> _byReference = byReference;

        public IValue Resolve(string reference)
        {
            if (_byReference.TryGetValue(reference, out var value))
                return value;

            throw new InvalidOperationException($"Unmapped reference '{reference}'.");
        }

        public bool TryResolve(string reference, out IValue? value)
            => _byReference.TryGetValue(reference, out value);

        public bool TryResolve<T>(string reference, out IValue<T>? value)
        {
            value = null;
            if (!_byReference.TryGetValue(reference, out var raw))
                return false;

            if (raw is IValue<T> typed)
            {
                value = typed;
                return true;
            }

            return false;
        }
    }

    private sealed class CfgSpecialWithConvention : CfgSpecial
    {
        public string? ProbeValueReference { get; set; }

        public string? AlternateProbeReference { get; set; }
    }

    private sealed class SpecialWithConvention : Special<CfgSpecialWithConvention>, IBuildingSpecial
    {
        public SpecialWithConvention(string name, CfgSpecialWithConvention config)
            : base(name, config)
        {
        }

        public IValue? ProbeValue { get; set; }

        [ModelValueBinding(SourceConfigPropertyName = nameof(CfgSpecialWithConvention.AlternateProbeReference))]
        public IValue? OverriddenProbeValue { get; set; }
    }
}
