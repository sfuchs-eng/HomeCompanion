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
    public void RoomShutterSceneControllerInitialization()
    {
        var fix = ShutterAutomationTestFixture.Create();
        Assert.That(fix.RuntimesController, Is.Not.Null, "RuntimesController should not be null after initialization.");
        Assert.That(fix.RuntimesController.BuildingRuntimes, Is.Not.Null);
        Assert.That(fix.RuntimesController.BuildingRuntimes, Has.Count.EqualTo(1), "There should be exactly one building runtime in the RuntimesController after initialization.");
        Assert.That(fix.RuntimesController.RoomRuntimes, Is.Not.Null);
        Assert.That(fix.RuntimesController.RoomRuntimes, Has.Count.EqualTo(1), "There should be exactly one room runtime in the RuntimesController after initialization.");

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
            Assert.That(fix.RuntimesController.BuildingRuntimes[buildingKey].BuildingKey, Is.EqualTo(buildingKey), "The building runtime should be associated with the correct building key.");
            Assert.That(fix.RuntimesController.RoomRuntimes.ContainsKey(roomKey), Is.True, "Room runtime for the test room should exist in the RuntimesController.");
            Assert.That(fix.RuntimesController.RoomRuntimes[roomKey].RoomContext.Room, Is.EqualTo(room), "The room runtime should be associated with the correct room context.");
        }

        var rtBuilding = fix.RuntimesController.BuildingRuntimes.Single(b => b.Key.BuildingName.Equals("TestBuilding1")).Value;
        var rtRoom = fix.RuntimesController.RoomRuntimes.Single(r => r.Key.Equals(roomKey)).Value;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rtBuilding.BuildingKey, Is.EqualTo(buildingKey));
            Assert.That(rtRoom.RoomKey, Is.EqualTo(roomKey));
            Assert.That(rtRoom.RoomContext.Building, Is.EqualTo(building));
            Assert.That(rtRoom.RoomContext.Room, Is.EqualTo(room));
        }
    }

    [Test(Description = "Test model initialization and IValue binding")]
    public void RoomShutterSceneControllerModelInitialization()
    {
        var fix = ShutterAutomationTestFixture.Create();
        var roomKey = fix.RuntimesController.RoomRuntimes.Keys.First();
        var room = fix.RuntimesController.RoomRuntimes[roomKey].RoomContext.Room;

        Assert.DoesNotThrow(() => fix.ValuesProvider.Resolve(room.Configuration.ShutterSceneReference ?? throw new InvalidOperationException("Room shutter scene reference is null.")), "Room shutter scene value should be resolvable from the value provider.");
        Assert.That(fix.ValuesProvider.Resolve(room.Configuration.ShutterSceneReference ?? throw new InvalidOperationException("Room shutter scene reference is null.")), Is.Not.Null, "Room shutter scene reference should exist in the value provider.");
        Assert.That(room.ShutterScene, Is.Not.Null, "Room shutter scene should not be null after initialization.");
    }

    [Test(Description = "Tests that the RoomShutterSceneController correctly handles user requests to transition between different room shutter scenes.")]
    [TestCase((byte)RoomShutterScene.AutoNoReopen, (byte)ThermalControlMode.Passive, (byte)RoomShutterScene.AutoNoReopen, 22.0f, "Transition from HardOpen to AutoNoReopen with normal temperature.")]
    public void RoomShutterSceneUserRequestTransitions(byte requestScene, byte thermalControlScene, byte expectedSceneAfterTransition, float roomTemperature, string testDescription)
    {
        var fix = ShutterAutomationTestFixture.Create();
        var roomKey = fix.RuntimesController.RoomRuntimes.Keys.First();
        var room = fix.RuntimesController.RoomRuntimes[roomKey].RoomContext.Room;
        var roomRuntime = fix.RuntimesController.RoomRuntimes[roomKey];
        var special = fix.ModelProvider.GetModel().EnumerateShadowingSpecials().First();

        // It's initialized at room scene 1/KNX 2
        room.ShutterScene?.Write((byte)RoomShutterScene.HardOpen);
        Assert.That(roomRuntime.LastSceneCommanded, Is.EqualTo((byte)RoomShutterScene.Undefined), "Initial room scene should be undefined before setting the room shutter scene.");
        roomRuntime.SetRoomShutterScene(room.ShutterScene?.Value ?? throw new InvalidOperationException("Room shutter scene value is null."));
        Assert.That(roomRuntime.LastSceneCommanded, Is.EqualTo((byte)RoomShutterScene.HardOpen), "Initial room scene not reached. Cannot continue test.");

        fix.StartAsync().GetAwaiter().GetResult();

        // event bus tap; it does not queue the events, rather just pass them through to the subscribers, so that the RoomShutterSceneController can handle them immediately.
        var shutterAutomationComputationTriggerRaised = false;
        IEnumerable<IValue>? triggeringValues = null;
        async ValueTask handleShutterAutomationEvent(ShutterAutomationComputationTriggerEvent evt, CancellationToken cancellationToken)
        {
            triggeringValues = evt.Context.TriggeringValues;
            shutterAutomationComputationTriggerRaised = true;
            await ValueTask.CompletedTask;
        }
        fix.EventSubscriber.Subscribe<ShutterAutomationComputationTriggerEvent>(handleShutterAutomationEvent);

        // Simulate a user request to transition to the requested scene
        room.ShutterScene?.Write(requestScene, this);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(shutterAutomationComputationTriggerRaised, Is.True, "The user request event should have been handled.");
            Assert.That(triggeringValues, Contains.Value(room.ShutterScene), "The triggering values should contain the room's shutter scene value after the user request.");
        }

        // await processing - there's a queueing mechanism in the RoomShutterSceneController that processes the user request asynchronously, so we need to wait for it to complete.
        // --> implement a mechanism to await queue depletion with timeout/cancel token.

        throw new NotImplementedException("Test not fully implemented yet. Need to verify the room shutter scene transition logic and expected outcomes.");

    }
}