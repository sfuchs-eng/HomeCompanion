using Moq;
using NUnit.Framework;
using HomeCompanion.Logics.Shutters;
using HomeCompanion.Values;
using HomeCompanion.Events;
using HomeCompanion.Base.Model;

namespace HomeCompanion.Tests.Logics.Shutters;

[TestFixture]
public class RoomShutterSceneControllerTests
{
    [Test(Description = "Tests that the RoomShutterSceneController initializes correctly and creates the expected room runtime.")]
    public void TestRoomShutterSceneControllerInitialization()
    {
        var fix = ShutterAutomationTestFixture.Create();
        Assert.That(fix.RuntimesController.RoomRuntimes, Is.Not.Null);
        Assert.That(fix.RuntimesController.RoomRuntimes.Count, Is.EqualTo(1));

        // get model entities for the test room
        var building = fix.ModelProvider.GetModel().Buildings["TestBuilding1"];
        var floor = building.Floors["DG"];
        var room = floor.Rooms["TowerRoom"];

        // get the corresponding keys
        var buildingKey = new BuildingKey(building);
        var roomKey = new RoomKey(buildingKey, floor, room);

        // verify that different key objects for the same building and room are considered equal
        var anotherBuildingKey = new BuildingKey(building);
        var anotherRoomKey = new RoomKey(anotherBuildingKey, floor, room);

        //=== TODO: redesign the key classes to be purely based on string identifiers, and have a separate runtime reference class that holds the actual model object references.
        // the same must pass if the key is build straight from string identifiers;
//        var buildingKeyFromStrings = new BuildingKey("TestBuilding1");
//        var roomKeyFromStrings = new RoomKey("TestBuilding1", "DG", "TowerRoom");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(buildingKey, Is.EqualTo(anotherBuildingKey), "Building keys for the same building should be equal.");
            Assert.That(roomKey, Is.EqualTo(anotherRoomKey), "Room keys for the same room should be equal.");
//            Assert.That(buildingKey, Is.EqualTo(buildingKeyFromStrings), "Building keys built from the same string identifiers should be equal.");
//            Assert.That(roomKey, Is.EqualTo(roomKeyFromStrings), "Room keys built from the same string identifiers should be equal.");
        }


        // verify that the room runtime is correctly associated with the room key, same for the building runtime
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fix.RuntimesController.BuildingRuntimes.ContainsKey(buildingKey), Is.True, "Building runtime for the test building should exist in the RuntimesController.");
            Assert.That(fix.RuntimesController.BuildingRuntimes[buildingKey].BuildingKey.Building, Is.EqualTo(building), "The building runtime should be associated with the correct building.");
            Assert.That(fix.RuntimesController.RoomRuntimes.ContainsKey(roomKey), Is.True, "Room runtime for the test room should exist in the RuntimesController.");
            Assert.That(fix.RuntimesController.RoomRuntimes[roomKey].RoomKey.Room, Is.EqualTo(room), "The room runtime should be associated with the correct room.");
        }

        var rtBuilding = fix.RuntimesController.BuildingRuntimes.Single(b => b.Key.Key.Equals("TestBuilding1")).Value;
        var rtRoom = fix.RuntimesController.RoomRuntimes.Single(r => r.Key.Equals(roomKey)).Value;

        Assert.Fail("TODO: implement tests for the RoomShutterSceneController's behavior, including how it reacts to changes in the model and value references, and how it manages room shutter scenes.");
    }
}