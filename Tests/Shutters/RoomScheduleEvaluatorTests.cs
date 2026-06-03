using HomeCompanion.Base.Logics.Shutters;
using HomeCompanion.Base.Model;

namespace HomeCompanion.Tests.Shutters;

[TestFixture]
public class RoomScheduleEvaluatorTests
{
    [Test]
    public void EvaluateDueTransitions_PrimesOnFirstCall_WithoutImmediateDueEvents()
    {
        var model = CreateModel("* * * * *");
        var sut = new InProcessCronRoomScheduleEvaluator(TimeZoneInfo.Utc);

        var due = sut.EvaluateDueTransitions(model, new DateTimeOffset(2026, 6, 3, 10, 0, 0, TimeSpan.Zero));

        Assert.That(due, Is.Empty);
    }

    [Test]
    public void EvaluateDueTransitions_ReturnsDueTransition_OnNextMinute()
    {
        var model = CreateModel("* * * * *");
        var sut = new InProcessCronRoomScheduleEvaluator(TimeZoneInfo.Utc);

        _ = sut.EvaluateDueTransitions(model, new DateTimeOffset(2026, 6, 3, 10, 0, 0, TimeSpan.Zero));
        var due = sut.EvaluateDueTransitions(model, new DateTimeOffset(2026, 6, 3, 10, 1, 0, TimeSpan.Zero));

        Assert.That(due.Count, Is.EqualTo(1));
        Assert.That(due[0].RoomKey, Is.EqualTo("Main/Ground/Living"));
        Assert.That(due[0].ScheduleKey, Is.EqualTo("NightClose"));
        Assert.That(due[0].Scene, Is.EqualTo(3));
    }

    [Test]
    public void EvaluateDueTransitions_IgnoresInvalidCronExpressions()
    {
        var model = CreateModel("not a cron");
        var sut = new InProcessCronRoomScheduleEvaluator(TimeZoneInfo.Utc);

        _ = sut.EvaluateDueTransitions(model, new DateTimeOffset(2026, 6, 3, 10, 0, 0, TimeSpan.Zero));
        var due = sut.EvaluateDueTransitions(model, new DateTimeOffset(2026, 6, 3, 10, 1, 0, TimeSpan.Zero));

        Assert.That(due, Is.Empty);
    }

    [Test]
    public void QuartzEvaluator_PrimesOnFirstCall_WithoutImmediateDueEvents()
    {
        var model = CreateModel("0 0/1 * * * ?");
        var sut = new QuartzRoomScheduleEvaluator(TimeZoneInfo.Utc);

        var due = sut.EvaluateDueTransitions(model, new DateTimeOffset(2026, 6, 3, 10, 0, 0, TimeSpan.Zero));

        Assert.That(due, Is.Empty);
    }

    [Test]
    public void QuartzEvaluator_ReturnsDueTransition_OnNextMinute()
    {
        var model = CreateModel("0 0/1 * * * ?");
        var sut = new QuartzRoomScheduleEvaluator(TimeZoneInfo.Utc);

        _ = sut.EvaluateDueTransitions(model, new DateTimeOffset(2026, 6, 3, 10, 0, 0, TimeSpan.Zero));
        var due = sut.EvaluateDueTransitions(model, new DateTimeOffset(2026, 6, 3, 10, 1, 0, TimeSpan.Zero));

        Assert.That(due.Count, Is.EqualTo(1));
        Assert.That(due[0].RoomKey, Is.EqualTo("Main/Ground/Living"));
        Assert.That(due[0].ScheduleKey, Is.EqualTo("NightClose"));
        Assert.That(due[0].Scene, Is.EqualTo(3));
    }

    private static HomeCompanion.Base.Model.Model CreateModel(string cronExpression)
    {
        var roomCfg = new CfgRoom
        {
            ScheduleTransitions =
            {
                ["NightClose"] = new CfgRoomScheduleTransition
                {
                    CronExpression = cronExpression,
                    Scene = 3,
                    CloseOnly = true,
                },
            },
        };

        var room = new Room("Living", roomCfg);
        var floor = new Floor { Name = "Ground", Rooms = new Dictionary<string, Room> { ["Living"] = room } };
        var building = new Building { Name = "Main", Floors = new Dictionary<string, Floor> { ["Ground"] = floor } };
        return new HomeCompanion.Base.Model.Model
        {
            Buildings = new Dictionary<string, Building>
            {
                ["Main"] = building,
            },
        };
    }
}
