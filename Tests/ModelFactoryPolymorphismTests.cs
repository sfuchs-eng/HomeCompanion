using HomeCompanion.Base.Model;

namespace HomeCompanion.Tests;

[TestFixture]
public class ModelFactoryPolymorphismTests
{
    private static readonly ModelFactory Sut = new();

    [Test]
    public void CreateBuildingConfig_WithCustomKind_ResolvesDerivedConfigType()
    {
        var cfg = Sut.CreateBuildingConfig("CustomBuilding", "Model:Buildings:Main");

        Assert.That(cfg, Is.TypeOf<CfgCustomBuilding>());
    }

    [Test]
    public void CreateFacadeConfig_WithCustomKind_ResolvesDerivedConfigType()
    {
        var cfg = Sut.CreateFacadeConfig("CustomFacade", "Model:Buildings:Main:Facades:South");

        Assert.That(cfg, Is.TypeOf<CfgCustomFacade>());
    }

    [Test]
    public void CreateFloorConfig_WithCustomKind_ResolvesDerivedConfigType()
    {
        var cfg = Sut.CreateFloorConfig("CustomFloor", "Model:Buildings:Main:Floors:Ground");

        Assert.That(cfg, Is.TypeOf<CfgCustomFloor>());
    }

    [Test]
    public void CreateRoomConfig_WithCustomKind_ResolvesDerivedConfigType()
    {
        var cfg = Sut.CreateRoomConfig("CustomRoom", "Model:Buildings:Main:Floors:Ground:Rooms:Living");

        Assert.That(cfg, Is.TypeOf<CfgCustomRoom>());
    }

    [Test]
    public void CreateShutterConfig_WithCustomKind_ResolvesDerivedConfigType()
    {
        var cfg = Sut.CreateShutterConfig("CustomShutter", "Model:Buildings:Main:Floors:Ground:Rooms:Living:Shutters:West");

        Assert.That(cfg, Is.TypeOf<CfgCustomShutter>());
    }

    [Test]
    public void CreateSpecialConfig_WithCustomKind_ResolvesDerivedConfigType()
    {
        var cfg = Sut.CreateSpecialConfig("CustomSpecial", "Model:Buildings:Main:Specials:Demo");

        Assert.That(cfg, Is.TypeOf<CfgCustomSpecial>());
    }

    [Test]
    public void CreateRoomConfig_WithCfgPrefixKind_ResolvesDerivedConfigType()
    {
        var cfg = Sut.CreateRoomConfig("CfgCustomRoom", "Model:Buildings:Main:Floors:Ground:Rooms:Living");

        Assert.That(cfg, Is.TypeOf<CfgCustomRoom>());
    }

    [Test]
    public void CreateRoom_WithDerivedConfig_ResolvesDerivedRuntimeType()
    {
        var model = new Model();
        var building = new Building { Name = "Main" };
        var floor = new Floor { Name = "Ground" };

        var room = Sut.CreateRoom(
            new RoomCreationContext(model, building, floor),
            "Living",
            new CfgCustomRoom());

        Assert.That(room, Is.TypeOf<CustomRoom>());
    }

    [Test]
    public void CreateFacade_WithDerivedConfig_ResolvesDerivedRuntimeType()
    {
        var model = new Model();
        var building = new Building { Name = "Main" };

        var facade = Sut.CreateFacade(
            new FacadeCreationContext(model, building),
            "South",
            new CfgCustomFacade());

        Assert.That(facade, Is.TypeOf<CustomFacade>());
    }

    [Test]
    public void CreateShutter_WithDerivedConfig_ResolvesDerivedRuntimeType()
    {
        var model = new Model();
        var building = new Building { Name = "Main" };
        var floor = new Floor { Name = "Ground" };
        var room = new Room("Living", new CfgRoom());

        var shutter = Sut.CreateShutter(
            new ShutterCreationContext(model, building, floor, room),
            "West",
            new CfgCustomShutter());

        Assert.That(shutter, Is.TypeOf<CustomShutter>());
    }

    [Test]
    public void CreateSpecial_WithDerivedConfig_ResolvesDerivedRuntimeType()
    {
        var model = new Model();
        var building = new Building { Name = "Main" };

        var special = Sut.CreateSpecial(
            new SpecialCreationContext(model, building),
            "Demo",
            new CfgCustomSpecial());

        Assert.That(special, Is.TypeOf<CustomSpecial>());
    }

    [Test]
    public void CreateSpecial_WithShadowingSpecialConfig_ResolvesShadowingSpecialRuntimeType()
    {
        var model = new Model();
        var building = new Building { Name = "Main" };

        var special = Sut.CreateSpecial(
            new SpecialCreationContext(model, building),
            "Shadowing",
            new CfgShadowingSpecial());

        Assert.That(special, Is.TypeOf<ShadowingSpecial>());
    }

    [Test]
    public void CreateRoomConfig_WithUnknownKind_ThrowsWithConfigurationPath()
    {
        var configurationPath = "Model:Buildings:Main:Floors:Ground:Rooms:Living";

        Assert.That(
            () => Sut.CreateRoomConfig("DefinitelyUnknownRoomKind", configurationPath),
            Throws.InvalidOperationException.With.Message.Contains(configurationPath));
    }

    [Test]
    public void CreateRoomConfig_WhenDerivedConfigLacksDefaultConstructor_Throws()
    {
        Assert.That(
            () => Sut.CreateRoomConfig("NoDefaultConstructorRoom", "Model:Rooms:X"),
            Throws.InvalidOperationException.With.Message.Contains("parameterless constructor"));
    }

    [Test]
    public void CreateRoom_WhenDerivedRuntimeConstructorDoesNotMatch_Throws()
    {
        var model = new Model();
        var building = new Building { Name = "Main" };
        var floor = new Floor { Name = "Ground" };

        Assert.That(
            () => Sut.CreateRoom(new RoomCreationContext(model, building, floor), "Living", new CfgBadCtorRoom()),
            Throws.InvalidOperationException.With.Message.Contains("must define a public constructor"));
    }

    [Test]
    public void CreateRoom_WhenRuntimeTypeMissing_Throws()
    {
        var model = new Model();
        var building = new Building { Name = "Main" };
        var floor = new Floor { Name = "Ground" };

        Assert.That(
            () => Sut.CreateRoom(new RoomCreationContext(model, building, floor), "Living", new CfgMissingRuntimeRoom()),
            Throws.InvalidOperationException.With.Message.Contains("No runtime model type named"));
    }

    // Test-only polymorphic types emulate Local-app extensions in another assembly.
    private sealed class CfgCustomBuilding : CfgBuilding;

    private sealed class CfgCustomFacade : CfgFacade;
    private sealed class CustomFacade(string name, CfgCustomFacade config) : Facade(name, config);

    private sealed class CfgCustomFloor : CfgFloor;

    private sealed class CfgCustomRoom : CfgRoom;
    private sealed class CustomRoom(string name, CfgCustomRoom config) : Room(name, config);

    private sealed class CfgCustomShutter : CfgShutter;
    private sealed class CustomShutter(string name, CfgCustomShutter config) : Shutter(name, config);

    private sealed class CfgCustomSpecial : CfgSpecial;
    private sealed class CustomSpecial(string name, CfgCustomSpecial config) : Special(name, config);

    private sealed class CfgNoDefaultConstructorRoom : CfgRoom
    {
        public CfgNoDefaultConstructorRoom(string ignored)
        {
            _ = ignored;
        }
    }

    private sealed class CfgBadCtorRoom : CfgRoom;
    private sealed class BadCtorRoom : Room
    {
        public BadCtorRoom(string name) : base(name, new CfgRoom())
        {
        }
    }

    private sealed class CfgMissingRuntimeRoom : CfgRoom;
}
