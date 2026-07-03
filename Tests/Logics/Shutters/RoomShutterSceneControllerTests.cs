using Moq;
using NUnit.Framework;
using HomeCompanion.Logics.Shutters;
using HomeCompanion.Values;
using HomeCompanion.Events;
using HomeCompanion.Base.Model;
using MQTTnet.Internal;

namespace HomeCompanion.Tests.Logics.Shutters;

[TestFixture]
public class RoomShutterSceneControllerTests
{
    [Test(Description = "Tests that the RoomShutterSceneController initializes correctly and creates the expected room runtime.")]
    public async Task RoomShutterSceneControllerInitialization()
    {
        var fix = ShutterAutomationTestFixture.Create();
        await fix.StartAsync();
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
    public async Task RoomShutterSceneControllerModelInitialization()
    {
        var fix = ShutterAutomationTestFixture.Create();
        await fix.StartAsync();
        var roomKey = fix.RuntimesController.RoomRuntimes.Keys.First();
        var room = fix.RuntimesController.RoomRuntimes[roomKey].RoomContext.Room;

        Assert.DoesNotThrow(() => fix.ValuesProvider.Resolve(room.Configuration.ShutterSceneReference ?? throw new InvalidOperationException("Room shutter scene reference is null.")), "Room shutter scene value should be resolvable from the value provider.");
        Assert.That(fix.ValuesProvider.Resolve(room.Configuration.ShutterSceneReference ?? throw new InvalidOperationException("Room shutter scene reference is null.")), Is.Not.Null, "Room shutter scene reference should exist in the value provider.");
        Assert.That(room.ShutterScene, Is.Not.Null, "Room shutter scene should not be null after initialization.");
    }

    [Test(Description = "Tests that the RoomShutterSceneController correctly handles user requests to transition between different room shutter scenes.")]
    [TestCase((byte)RoomShutterScene.HardOpen, (byte)RoomShutterScene.AutoNoReopen, (byte)ThermalControlMode.Passive, (byte)RoomShutterScene.AutoNoReopen, 22.0f, "User requests AutoNoReopen scene while in Passive thermal control mode.")]
    [TestCase((byte)RoomShutterScene.HardOpen, (byte)RoomShutterScene.AutoNoReopen, (byte)ThermalControlMode.Passive, (byte)RoomShutterScene.AutoNoReopen, 35.0f, "User requests AutoNoReopen scene while in Passive thermal control mode with high room temperature.")]
    [TestCase((byte)RoomShutterScene.HardOpen, (byte)RoomShutterScene.RequestOpen, (byte)ThermalControlMode.CoolingPriority, (byte)RoomShutterScene.HardOpen, 20.0f, "User requests RequestOpen scene while in CoolingPriority thermal control mode, cool room though.")]
    [TestCase((byte)RoomShutterScene.HardOpen, (byte)RoomShutterScene.RequestOpen, (byte)ThermalControlMode.BalancedCooling, (byte)RoomShutterScene.HardOpen, 20.0f, "User requests RequestOpen scene while in BalancedCooling thermal control mode, cool room.")]
    [TestCase((byte)RoomShutterScene.AutoReopen, (byte)RoomShutterScene.RequestOpen, (byte)ThermalControlMode.BalancedCooling, (byte)RoomShutterScene.AutoReopen, 20.0f, "User requests RequestOpen scene while in BalancedCooling thermal control mode, cool room.")]
    [TestCase((byte)RoomShutterScene.AutoReopen, (byte)RoomShutterScene.RequestOpen, (byte)ThermalControlMode.CoolingPriority, (byte)RoomShutterScene.AutoReopen, 20.0f, "User requests RequestOpen scene while in CoolingPriority thermal control mode, cool room.")]
    public async Task RoomShutterSceneUserRequestTransitions(byte startScene, byte requestScene, byte thermalControlScene, byte expectedSceneAfterTransition, float roomTemperature, string testDescription)
    {
        var fix = ShutterAutomationTestFixture.Create();
        await fix.StartAsync();
        var roomKey = fix.RuntimesController.RoomRuntimes.Keys.First();
        var room = fix.RuntimesController.RoomRuntimes[roomKey].RoomContext.Room;
        var roomRuntime = fix.RuntimesController.RoomRuntimes[roomKey];
        var special = fix.ModelProvider.GetModel().Buildings["TestBuilding1"].GetShadowingSpecial();

        ((ValueBase<byte>?)special.ThermalControlMode)?.Write(thermalControlScene);

        // It's initialized at room scene 1/KNX 2
        room.ShutterScene?.Write(startScene);
        Assert.That(roomRuntime.LastSceneCommanded, Is.EqualTo((byte)RoomShutterScene.Undefined), "Initial room scene should be undefined before setting the room shutter scene.");
        roomRuntime.SetRoomShutterScene(room.ShutterScene?.Value ?? throw new InvalidOperationException("Room shutter scene value is null."));
        Assert.That(roomRuntime.LastSceneCommanded, Is.EqualTo(startScene), "Initial room scene not reached. Cannot continue test.");

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

        // Simulate an external value write to transition to the requested scene
        room.ShutterScene?.Write(requestScene, this);

        // Force a room-scene recomputation trigger to make the transition assertion deterministic in tests.
        await fix.EventPublisher.PublishAsync(new ShutterAutomationComputationTriggerEvent
        {
            Context = new ShutterAutomationComputationTriggerContext(
                thingKeys: [roomKey],
                scope: ShutterAutomationComputationScope.RoomSpecific,
                triggeringValue: room.ShutterScene != null ? [room.ShutterScene] : [],
                valueEventArgs: [],
                timestamp: DateTimeOffset.UtcNow,
                urgency: ShutterAutomationComputationTriggerUrgency.Immediate)
        });

        var triggerRaised = SpinWait.SpinUntil(() => shutterAutomationComputationTriggerRaised, TimeSpan.FromSeconds(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(triggerRaised, Is.True, "The external value write event should have been handled.");
            Assert.That(triggeringValues, Contains.Item(room.ShutterScene), "The triggering values should contain the room's shutter scene value after the external value write.");
            Assert.That(room.ShutterScene?.Value, Is.EqualTo(requestScene), $"Unexpected resulting room scene request value for case '{testDescription}'.");
        }
    }
}