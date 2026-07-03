using System;
using HomeCompanion.Base.Model;
using HomeCompanion.Core.Model;
using HomeCompanion.Events;
using HomeCompanion.Logics.Shutters;
using HomeCompanion.Tests.TestUtilities;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests.Logics.Shutters;

/// <summary>
/// Provides a test fixture for testing shutter automation and room shutter scene logic, including model creation, value reference management, and runtime controller setup.
/// </summary>
/// <param name="valuesProvider"></param>
/// <param name="eventPublisher"></param>
/// <param name="eventSubscriber"></param>
/// <param name="timeProvider"></param>
/// <param name="modelProvider"></param>
/// <param name="runtimesProvider"></param>
/// <param name="runtimesController"></param>
/// <param name="loggerFactory"></param>
/// <param name="logger"></param> <summary>
public partial class ShutterAutomationTestFixture(
    IValueProvider valuesProvider,
    IEventPublisher eventPublisher,
    IEventSubscriber eventSubscriber,
    TimeProvider timeProvider,
    IModelProvider modelProvider,
    IRuntimesProvider runtimesProvider,
    ShadowingRuntimesController runtimesController,
    ShutterController shutterController,
    RoomShutterSceneLogic roomShutterSceneLogic,
    ValuesManager valuesManager,
    ILoggerFactory loggerFactory,
    ILogger<ShutterAutomationTestFixture> logger
)
{
    public IValueProvider ValuesProvider { get; private set; } = valuesProvider;
    public IEventPublisher EventPublisher { get; private set; } = eventPublisher;
    public IEventSubscriber EventSubscriber { get; private set; } = eventSubscriber;
    public TimeProvider TimeProvider { get; private set; } = timeProvider;
    public IModelProvider ModelProvider { get; private set; } = modelProvider;
    public IRuntimesProvider RuntimesProvider { get; private set; } = runtimesProvider;
    public ShadowingRuntimesController RuntimesController { get; private set; } = runtimesController;
    public ShutterController ShutterController { get; private set; } = shutterController;
    public RoomShutterSceneLogic RoomShutterSceneLogic { get; private set; } = roomShutterSceneLogic;
    public ILoggerFactory LoggerFactory { get; private set; } = loggerFactory;
    public ValuesManager ValuesManager { get; private set; } = valuesManager;
    public ILogger<ShutterAutomationTestFixture> Logger { get; private set; } = logger;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await RuntimesController.InitializeAsync(cancellationToken);
        await ShutterController.InitializeAsync(cancellationToken);
        await RoomShutterSceneLogic.InitializeAsync(cancellationToken);
        await ValuesManager.StartAsync(cancellationToken);
    }

    /// <summary>
    /// IValue *Reference properties must be prefixed with their type. E.g. an ValueBase&lt;bool&gt; property must be referenced as "Bool:PropertyName", an ValueBase&lt;int&gt; property as "Int:PropertyName", etc.
    /// This method creates a default model configuration with a building, floor, room and 5 shutters (NW, SE, SW, NE, Roof tilted SW) and returns the configuration. The model can then be built from this configuration using the ModelFactory.
    /// </summary>
    /// <returns></returns>
    public static CfgModel CreateDefaultModelConfiguration()
    {
        var cfg = new CfgModel
        {
            Buildings = new Dictionary<string, CfgBuilding>
            {
                ["TestBuilding1"] = new CfgBuilding()
                {
                    Facades = new Dictionary<string, CfgFacade>
                    {
                        ["NW"] = new CfgFacade { Azimuth = 315 },
                        ["SE"] = new CfgFacade { Azimuth = 135 },
                        ["SW"] = new CfgFacade { Azimuth = 225 },
                        ["NE"] = new CfgFacade { Azimuth = 45 },
                        ["RoofTiltedSW"] = new CfgFacade { Azimuth = 225, Elevation = 45 }
                    },
                    Floors = new Dictionary<string, CfgFloor>
                    {
                        ["DG"] = new CfgFloor()
                        {
                            Rooms = new Dictionary<string, CfgRoom>
                            {
                                ["TowerRoom"] = new CfgRoom
                                {
                                    TemperatureReference = "Float:TowerRoomTemperature",
                                    AntiGlareEnableReference = "Bool:TowerRoomAntiGlareEnable",
                                    ShutterSceneReference = "Byte:TowerRoomShutterScene",
                                    Shutters = new Dictionary<string, CfgShutter>
                                    {
                                        ["Shutter1_NW"] = new CfgShutter { FacadeReference = "NW", Type = ShutterType.VenetianBlind, PositionValueReference = "Float:Shutter1Position", AngleValueReference = "Float:Shutter1Angle" },
                                        ["Shutter2_SE"] = new CfgShutter { FacadeReference = "SE", Type = ShutterType.VenetianBlind, PositionValueReference = "Float:Shutter2Position", AngleValueReference = "Float:Shutter2Angle" },
                                        ["Shutter3_SW"] = new CfgShutter { FacadeReference = "SW", Type = ShutterType.VenetianBlind, PositionValueReference = "Float:Shutter3Position", AngleValueReference = "Float:Shutter3Angle" },
                                        ["Shutter4_NE"] = new CfgShutter { FacadeReference = "NE", Type = ShutterType.VenetianBlind, PositionValueReference = "Float:Shutter4Position", AngleValueReference = "Float:Shutter4Angle" },
                                        ["Shutter5_RoofTiltedSW"] = new CfgShutter { FacadeReference = "RoofTiltedSW", Type = ShutterType.OpenClose, OpenCloseReference = "Bool:Shutter5Closed" }
                                    }
                                }
                            }
                        }
                    },
                    Specials = new()
                    {
                        ["DefaultShading"] = new CfgShadowingSpecial
                        {
                            DefaultShutterConstraints = ShutterConstraints.None,
                            GlobalShutterSceneReference = "Byte:GlobalShutterScene",
                            AutoShadowStatusReference = "Bool:AutoShadowStatus",
                            AbsenceReference = "Bool:Absence",
                            DisableAutoShadowAssessmentReference = "Bool:DisableAutoShadowAssessment",
                            OutdoorTemperatureReference = "Float:OutdoorTemperature",
                            SunIntensityEastReference = "Float:SunIntensityEast",
                            SunIntensitySouthReference = "Float:SunIntensitySouth",
                            SunIntensityWestReference = "Float:SunIntensityWest",
                            SunPositionAzimuthReference = "Float:SunPositionAzimuth",
                            SunPositionElevationReference = "Float:SunPositionElevation",
                            ThermalControlModeReference = "Byte:ThermalControlMode",
                            UvIntensityReference = null,
                        }
                    }
                }
            }
        };
        return cfg;
    }

    /// <summary>
    /// Initialized the <see cref="IValue"/> used in the default model configuration with default values for testing.
    /// The method may (silently or throw) fail if the model configuration is not the default one created by <see cref="CreateDefaultModelConfiguration"/>.
    /// </summary>
    /// <param name="valueReferences"></param>
    /// </summary>
    /// <param name="model"></param>
    public static void SetShadowingBaseScenario(Model model)
    {
        var building = model.Buildings["TestBuilding1"];
        var shadowingSpecial = building.Specials["DefaultShading"] as ShadowingSpecial
            ?? throw new InvalidOperationException("DefaultShading special not found in TestBuilding1");

        // Set default values for the IValue properties used in the default model configuration
        (shadowingSpecial.GlobalShutterScene as ValueBase<byte>)?.Write(0);
        (shadowingSpecial.AutoShadowStatus as ValueBase<bool>)?.Write(false);
        (shadowingSpecial.Absence as ValueBase<bool>)?.Write(false);
        (shadowingSpecial.DisableAutoShadowAssessment as ValueBase<bool>)?.Write(false);
        (shadowingSpecial.OutdoorTemperature as ValueBase<float>)?.Write(4.0f);
        (shadowingSpecial.SunIntensityEast as ValueBase<float>)?.Write(0.0f);
        (shadowingSpecial.SunIntensitySouth as ValueBase<float>)?.Write(0.0f);
        (shadowingSpecial.SunIntensityWest as ValueBase<float>)?.Write(0.0f);
        // Sun position somewhen in spring 1980 somewhere in Switzerland, 10:00:
        (shadowingSpecial.SunPositionAzimuth as ValueBase<float>)?.Write(137.5f);
        (shadowingSpecial.SunPositionElevation as ValueBase<float>)?.Write(26.3f);
        (shadowingSpecial.ThermalControlMode as ValueBase<byte>)?.Write((byte)ThermalControlMode.Passive);
        (shadowingSpecial.UvIntensity as ValueBase<float>)?.Write(0.0f);

        // Set defaults for room level values
        var allRooms = model.EnumerateRoomContexts().ToArray();
        foreach (var roomContext in allRooms)
        {
            var room = roomContext.Room;
            // set default values for the IValue properties used in the default model configuration
            (roomContext.Room.AntiGlareEnable as ValueBase<bool>)?.Write(false);
            (roomContext.Room.ShutterScene as ValueBase<byte>)?.Write((byte)RoomShutterScene.HardOpen); // value 1, HardOpen, is equivalent to KNX scene 2 = all shutters open.
            (roomContext.Room.Temperature as ValueBase<float>)?.Write(20.0f);
        }

        var allShutters = model.EnumerateShutterContexts().ToArray();
        foreach (var shutterContext in allShutters)
        {
            var shutter = shutterContext.Shutter;
            if (shutter.Configuration.Type == ShutterType.VenetianBlind)
            {
                // fully open and horizontal
                (shutter.PositionValue as ValueBase<float>)?.Write(0.0f);
                (shutter.AngleValue as ValueBase<float>)?.Write(0.0f);
            }
            else if (shutter.Configuration.Type == ShutterType.OpenClose)
            {
                // fully open
                (shutter.OpenCloseValue as ValueBase<bool>)?.Write(false);
            }
        }
    }

    /// <summary>
    /// If no modelConfig is provided, a default model configuration is created using <see cref="CreateDefaultModelConfiguration"/>.
    /// The model is then built from the configuration using the ModelFactory, including IValue binding where the IValue properties are bound to a GenerativeReferenceProvider that generates the values on demand.
    /// The generated values are returned in the valueReferences dictionary, which can be used to access the IValue instances for testing.
    /// </summary>
    /// <param name="modelConfig">The model configuration to use. If null, a default configuration is created.</param>
    /// <param name="model">The created model.</param>
    /// <param name="valueReferences">The dictionary of generated IValue instances.</param>
    public static void BuildTestModel(CfgModel? modelConfig, out Model model, TimeProvider timeProvider, Dictionary<string, IValue> valueReferences, out Dictionary<string, IValue> generatedValues)
    {
        var cfg = modelConfig ?? CreateDefaultModelConfiguration();

        // use the regular factory to build the model from the configuration, so that all the keys are properly generated and linked
        var mf = new ModelFactory();
        model = mf.CreateModel(cfg);

        // Use the binder for regularly binding the model's IValue properties. The used special reference provider will generate the values on demand, so we don't need to pre-populate it with values.
        var valuesProvider = new GenerativeValueProvider(valueReferences, new ConsoleLoggerFactory(), timeProvider);
        var binder = new ModelValueBinder(valuesProvider, NullLoggerFactory.Instance.CreateLogger<ModelValueBinder>());
        binder.Bind(model);
        generatedValues = valuesProvider.GeneratedValues;

        // Initialize the IValue properties used in the default model configuration with default values for testing
        SetShadowingBaseScenario(model);
    }

    public static ShutterAutomationTestFixture Create(CfgModel? modelConfig = null, Model? model = null, TimeProvider? timeProvider = null, Dictionary<string, IValue>? valueReferences = null)
    {
        modelConfig ??= model?.Configuration ?? CreateDefaultModelConfiguration();
        var tempValueReferences = valueReferences ?? new Dictionary<string, IValue>();
        if (model == null)
        {
            BuildTestModel(modelConfig, out model, timeProvider ?? TimeProvider.System, tempValueReferences, out var generatedValues);
            foreach (var kvp in generatedValues)
            {
                tempValueReferences[kvp.Key] = kvp.Value;
            }
        }
        var valuesProvider1 = new ReadOnlyValueProvider(tempValueReferences);
        //Console.Error.WriteLine($"Created test model with {model.Buildings.Count} buildings, {model.EnumerateRoomContexts().Count()} rooms, and {model.EnumerateShutterContexts().Count()} shutters.");
        //Console.Error.WriteLine($"Value references: {string.Join(", ", tempValueReferences.Keys)}");
        //Console.Error.WriteLine($"Value names: {string.Join(", ", tempValueReferences.Values.Select(v => v.Name))}");
        var eventPublisher = new PassThroughEventBus(new ConsoleLoggerFactory().CreateLogger<PassThroughEventBus>());
        var eventSubscriber = eventPublisher;
        timeProvider ??= TimeProvider.System;
        var modelProvider = new StubModelProvider(model);
        var loggerFactory = NullLoggerFactory.Instance;
        var runtimesController = new ShadowingRuntimesController(valuesProvider1, eventPublisher, eventSubscriber, timeProvider, modelProvider, loggerFactory);
        IRuntimesProvider runtimesProvider = runtimesController;
        var logger = loggerFactory.CreateLogger<ShutterAutomationTestFixture>();

        var shutterController = new ShutterController(valuesProvider1, eventPublisher, eventSubscriber, timeProvider, modelProvider, loggerFactory, loggerFactory.CreateLogger<ShutterController>());
        var roomShutterSceneLogic = new RoomShutterSceneLogic(valuesProvider1, eventPublisher, eventSubscriber, timeProvider, modelProvider, runtimesProvider, runtimesController, loggerFactory, loggerFactory.CreateLogger<RoomShutterSceneLogic>());

        var lifeCycleManager = new StubLifeCycleManager();
        var valuesManager = new ValuesManager(eventPublisher, eventSubscriber, new[] { valuesProvider1 }, lifeCycleManager, loggerFactory.CreateLogger<ValuesManager>());

        return new ShutterAutomationTestFixture(valuesProvider1, eventPublisher, eventSubscriber, timeProvider, modelProvider, runtimesProvider, runtimesController, shutterController, roomShutterSceneLogic, valuesManager, loggerFactory, logger);
    }
}
