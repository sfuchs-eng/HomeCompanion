using HomeCompanion.Base.Model;
using HomeCompanion.Core.Model;
using HomeCompanion.Logics;
using HomeCompanion.Logics.Garden;
using HomeCompanion.Tests.Logics.Shutters;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Quartz;
using UnitsNet;

namespace HomeCompanion.Tests;

[TestFixture]
public class ModelValueBinderTests
{
    [Test]
    public void Bind_BindsShutterValues_UsingAttributes()
    {
        var position = CreateValue<Ratio>("Position");
        var angle = CreateValue<Ratio>("Angle");
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
    public void Bind_BindsRainSensorValues_UsingConfiguredReferences()
    {
        var rainInput = CreateValue<Length>("RainInput");
        var rain24h = CreateValue<Length>("Rain24h");
        var rain48h = CreateValue<Length>("Rain48h");
        var rain72h = CreateValue<Length>("Rain72h");
        var rain1w = CreateValue<Length>("Rain1w");
        var rain2w = CreateValue<Length>("Rain2w");

        var resolver = new StubValueReferenceProvider(new Dictionary<string, IValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["ref-rain-input"] = rainInput,
            ["ref-rain-24h"] = rain24h,
            ["ref-rain-48h"] = rain48h,
            ["ref-rain-72h"] = rain72h,
            ["ref-rain-1w"] = rain1w,
            ["ref-rain-2w"] = rain2w,
        });

        var sut = new ModelValueBinder(resolver, NullLogger<ModelValueBinder>.Instance);
        var cfg = new CfgRainSensor
        {
            RainInputReference = "ref-rain-input",
            RainActual24hReference = "ref-rain-24h",
            RainActual48hReference = "ref-rain-48h",
            RainActual72hReference = "ref-rain-72h",
            RainActual1wReference = "ref-rain-1w",
            RainActual2wReference = "ref-rain-2w",
        };
        var sensor = new RainSensor("Rain", cfg);
        var cfgBuilding = new CfgBuilding
        {
            Specials = new Dictionary<string, CfgBuildingSpecial>(StringComparer.OrdinalIgnoreCase)
            {
                ["Rain"] = cfg,
            },
        };
        var building = new Building("Main", cfgBuilding)
        {
            Specials = new Dictionary<string, IBuildingSpecial>(StringComparer.OrdinalIgnoreCase)
            {
                ["Rain"] = sensor,
            },
        };
        var model = new Model(new CfgModel())
        {
            Buildings = new Dictionary<string, Building>(StringComparer.OrdinalIgnoreCase)
            {
                ["Main"] = building,
            },
        };

        sut.Bind(model);

        Assert.That(sensor.RainInput, Is.SameAs(rainInput));
        Assert.That(sensor.RainActual24h, Is.SameAs(rain24h));
        Assert.That(sensor.RainActual48h, Is.SameAs(rain48h));
        Assert.That(sensor.RainActual72h, Is.SameAs(rain72h));
        Assert.That(sensor.RainActual1w, Is.SameAs(rain1w));
        Assert.That(sensor.RainActual2w, Is.SameAs(rain2w));
    }

    [Test]
    public async Task RainSensorLogic_UsesModelProviderToResolveBoundRainSensor()
    {
        var rainInput = new ValueBase<Length>(NullLogger<ValueBase<Length>>.Instance);
        var rain24h = new ValueBase<Length>(NullLogger<ValueBase<Length>>.Instance);
        var cfg = new CfgRainSensor();
        var sensor = new RainSensor("Rain", cfg)
        {
            RainInput = rainInput,
            RainActual24h = rain24h,
        };

        var model = new Model(new CfgModel())
        {
            Buildings = new Dictionary<string, Building>(StringComparer.OrdinalIgnoreCase)
            {
                ["Main"] = new Building("Main", new CfgBuilding
                {
                    Specials = new Dictionary<string, CfgBuildingSpecial>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Rain"] = cfg,
                    },
                })
                {
                    Specials = new Dictionary<string, IBuildingSpecial>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Rain"] = sensor,
                    },
                },
            },
        };

        var rainService = new RainService(TimeProvider.System);
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.ScheduleJob(It.IsAny<ITrigger>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTimeOffset.UtcNow);

        var schedulerFactory = new Mock<ISchedulerFactory>();
        schedulerFactory.Setup(f => f.GetScheduler(It.IsAny<CancellationToken>()))
            .ReturnsAsync(scheduler.Object);

        var logic = new RainSensorLogic(
            schedulerFactory.Object,
            new StubModelProvider(model),
            rainService,
            TimeProvider.System,
            NullLogger<RainSensorLogic>.Instance);

        await logic.InitializeAsync();
        rainInput.Write(Length.FromMillimeters(3.5));

        Assert.That(rainService.AcceptedTickCount, Is.EqualTo(1));
        Assert.That(rain24h.Value.Millimeters, Is.EqualTo(3.5).Within(0.0001));
    }

    [Test]
    public void Bind_BindsShadowingSpecialReferences_UsingAttributes()
    {
        var globalScene = CreateValue<byte>("GlobalScene");
        var autoShadowStatus = CreateValue<bool>("AutoShadowStatus");
        var absence = CreateValue<bool>("Absence");
        var disableAssessment = CreateValue<bool>("DisableAssessment");
        var outdoorTemperature = CreateValue<Temperature>("OutdoorTemperature");
        var sunEast = CreateValue<Illuminance>("SunEast");
        var sunSouth = CreateValue<Illuminance>("SunSouth");
        var sunWest = CreateValue<Illuminance>("SunWest");

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

    private static ValueBase<T> CreateValue<T>(string name) where T : notnull
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
