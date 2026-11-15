using NUnit.Framework;
using HomeCompanion.Logics.Shutters.ShutterAutomation;
using HomeCompanion.Base.Model;

namespace HomeCompanion.Tests.Logics.Shutters;

public class ShutterControllerTests
{
    [Test]
    public void ScopeFlags_RoomAndShutterCombination_DoesNotAliasGlobal()
    {
        var combined = ShutterAutomationComputationScope.RoomSpecific | ShutterAutomationComputationScope.ShutterSpecific;

        Assert.That(combined.HasFlag(ShutterAutomationComputationScope.RoomSpecific), Is.True);
        Assert.That(combined.HasFlag(ShutterAutomationComputationScope.ShutterSpecific), Is.True);
        Assert.That(combined.HasFlag(ShutterAutomationComputationScope.Global), Is.False);
    }

    [Test]
    public void TriggerContextMerge_UnionsScopesAndKeys()
    {
        var fixture = ShutterAutomationTestFixture.Create();
        var model = fixture.ModelProvider.GetModel();
        var roomContext = model.EnumerateRoomContexts().First();
        var roomKey = roomContext.RoomKey;
        var shutterKey = new ShutterKey(roomKey, roomContext.Room.Shutters.Values.First());

        var roomContextTrigger = new ShutterAutomationComputationTriggerContext(
            thingKeys: [roomKey],
            scope: ShutterAutomationComputationScope.RoomSpecific,
            triggeringValue: null,
            valueEventArgs: null,
            timestamp: DateTimeOffset.UtcNow,
            urgency: ShutterAutomationComputationTriggerUrgency.Normal);

        var shutterContextTrigger = new ShutterAutomationComputationTriggerContext(
            thingKeys: [shutterKey],
            scope: ShutterAutomationComputationScope.ShutterSpecific,
            triggeringValue: null,
            valueEventArgs: null,
            timestamp: DateTimeOffset.UtcNow,
            urgency: ShutterAutomationComputationTriggerUrgency.Immediate);

        var merged = new ShutterAutomationComputationTriggerContext([roomContextTrigger, shutterContextTrigger]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(merged.IsRoomScoped, Is.True);
            Assert.That(merged.IsShutterScoped, Is.True);
            Assert.That(merged.IsGlobalScope, Is.False);
            Assert.That(merged.GetRoomKeys().Count(), Is.EqualTo(1));
            Assert.That(merged.GetShutterKeys().Count(), Is.EqualTo(1));
        }
    }

    [Test]
    public void TriggerContextMerge_DeduplicatesOverlappingThingKeys()
    {
        var fixture = ShutterAutomationTestFixture.Create();
        var model = fixture.ModelProvider.GetModel();
        var roomContext = model.EnumerateRoomContexts().First();
        var shutterKey = new ShutterKey(roomContext.RoomKey, roomContext.Room.Shutters.Values.First());

        var first = new ShutterAutomationComputationTriggerContext(
            thingKeys: [shutterKey],
            scope: ShutterAutomationComputationScope.ShutterSpecific,
            triggeringValue: null,
            valueEventArgs: null,
            timestamp: DateTimeOffset.UtcNow,
            urgency: ShutterAutomationComputationTriggerUrgency.Normal);

        var second = new ShutterAutomationComputationTriggerContext(
            thingKeys: [shutterKey],
            scope: ShutterAutomationComputationScope.ShutterSpecific,
            triggeringValue: null,
            valueEventArgs: null,
            timestamp: DateTimeOffset.UtcNow,
            urgency: ShutterAutomationComputationTriggerUrgency.Slow);

        var merged = new ShutterAutomationComputationTriggerContext([first, second]);

        Assert.That(merged.GetShutterKeys().Count(), Is.EqualTo(1));
    }
}