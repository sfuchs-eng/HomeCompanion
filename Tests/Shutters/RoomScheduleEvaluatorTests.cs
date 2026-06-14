using HomeCompanion.Base.Logics.Shutters;
using HomeCompanion.Base.Model;

namespace HomeCompanion.Tests.Shutters;
/*
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

    [Test]
    public void EvaluateDueTransitions_PropagatesOptionalAutoResumeSettings()
    {
        var model = CreateModel("* * * * *", transition =>
        {
            transition.ResumeAutomationAfter = TimeSpan.FromHours(8);
            transition.ResumeAutomationAtLocalTime = new TimeSpan(7, 30, 0);
            transition.ResumeAutomationScene = 52;
        });

        var sut = new InProcessCronRoomScheduleEvaluator(TimeZoneInfo.Utc);

        _ = sut.EvaluateDueTransitions(model, new DateTimeOffset(2026, 6, 3, 10, 0, 0, TimeSpan.Zero));
        var due = sut.EvaluateDueTransitions(model, new DateTimeOffset(2026, 6, 3, 10, 1, 0, TimeSpan.Zero));

        Assert.That(due.Count, Is.EqualTo(1));
        Assert.That(due[0].ResumeAutomationAfter, Is.EqualTo(TimeSpan.FromHours(8)));
        Assert.That(due[0].ResumeAutomationAtLocalTime, Is.EqualTo(new TimeSpan(7, 30, 0)));
        Assert.That(due[0].ResumeAutomationScene, Is.EqualTo(52));
    }

    [Test]
    public void Scenario16_WeekendSchedule_UsesLaterResumeTime()
    {
        var roomCfg = new CfgRoom
        {
            ScheduleTransitions =
            {
                ["WeekdayResume"] = new CfgRoomScheduleTransition
                {
                    CronExpression = "0 7 * * 1-5",
                    Scene = 52,
                },
                ["WeekendResume"] = new CfgRoomScheduleTransition
                {
                    CronExpression = "30 9 * * 6",
                    Scene = 52,
                },
            },
        };

        var room = new Room("Living", roomCfg);
        var floor = new Floor { Name = "Ground", Rooms = new Dictionary<string, Room> { ["Living"] = room } };
        var building = new Building { Name = "Main", Floors = new Dictionary<string, Floor> { ["Ground"] = floor } };
        var model = new HomeCompanion.Base.Model.Model
        {
            Buildings = new Dictionary<string, Building>
            {
                ["Main"] = building,
            },
        };

        var sut = new InProcessCronRoomScheduleEvaluator(TimeZoneInfo.Utc);

        _ = sut.EvaluateDueTransitions(model, new DateTimeOffset(2026, 6, 6, 9, 29, 0, TimeSpan.Zero)); // Saturday
        var due = sut.EvaluateDueTransitions(model, new DateTimeOffset(2026, 6, 6, 9, 30, 0, TimeSpan.Zero));

        Assert.That(due.Count, Is.EqualTo(1));
        Assert.That(due[0].ScheduleKey, Is.EqualTo("WeekendResume"));
        Assert.That(due[0].Scene, Is.EqualTo(52));
    }

    private static HomeCompanion.Base.Model.Model CreateModel(string cronExpression, Action<CfgRoomScheduleTransition>? configure = null)
    {
        var transition = new CfgRoomScheduleTransition
        {
            CronExpression = cronExpression,
            Scene = 3,
            CloseOnly = true,
        };

        configure?.Invoke(transition);

        var roomCfg = new CfgRoom
        {
            ScheduleTransitions =
            {
                ["NightClose"] = transition,
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
*/