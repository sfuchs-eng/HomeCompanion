using Cronos;
using HomeCompanion.Base.Model;
using Quartz;
using CronosExpression = Cronos.CronExpression;
using QuartzExpression = Quartz.CronExpression;

namespace HomeCompanion.Base.Logics.Shutters.AIAttempt;

/// <summary>
/// Evaluates room schedule transitions and returns due schedule events.
/// </summary>
public interface IRoomScheduleEvaluator
{
    IReadOnlyList<RoomScheduleDueTransition> EvaluateDueTransitions(Model.Model model, DateTimeOffset now);
}

/// <summary>
/// In-process cron-based schedule evaluator.
/// </summary>
/// <remarks>
/// This evaluator is intentionally lightweight and swappable. It can later be replaced by a Quartz-backed implementation
/// without changing shutter policy orchestration.
/// </remarks>
public sealed class InProcessCronRoomScheduleEvaluator(TimeZoneInfo? timeZone = null) : IRoomScheduleEvaluator
{
    private readonly TimeZoneInfo _timeZone = timeZone ?? TimeZoneInfo.Local;
    private readonly Dictionary<string, CronosExpression?> _cronByTransitionKey = [];
    private DateTime _lastEvaluationUtc;
    private bool _isPrimed;

    public IReadOnlyList<RoomScheduleDueTransition> EvaluateDueTransitions(Model.Model model, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(model);

        var nowUtc = now.UtcDateTime;

        if (!_isPrimed)
        {
            _lastEvaluationUtc = nowUtc;
            _isPrimed = true;
            return [];
        }

        var due = new List<RoomScheduleDueTransition>();
        foreach (var building in model.Buildings.Values)
        {
            foreach (var floor in building.Floors.Values)
            {
                foreach (var room in floor.Rooms.Values)
                {
                    if (room.Configuration.ScheduleTransitions.Count == 0)
                        continue;

                    var roomKey = $"{building.Name}/{floor.Name}/{room.Name}";
                    foreach (var schedule in room.Configuration.ScheduleTransitions)
                    {
                        var transitionKey = $"{roomKey}/{schedule.Key}";
                        var cron = ResolveCron(transitionKey, schedule.Value.CronExpression);
                        if (cron is null)
                            continue;

                        var next = cron.GetNextOccurrence(_lastEvaluationUtc, _timeZone, false);
                        if (next is null || next > nowUtc)
                            continue;

                        var triggerLocal = TimeZoneInfo.ConvertTime(new DateTimeOffset(next.Value, TimeSpan.Zero), _timeZone).DateTime;

                        due.Add(new RoomScheduleDueTransition(
                            RoomKey: roomKey,
                            ScheduleKey: schedule.Key,
                            Scene: schedule.Value.Scene,
                            CloseOnly: schedule.Value.CloseOnly,
                            ManualOpenGracePeriod: schedule.Value.ManualOpenGracePeriod,
                            EnableShadowTranslationAfterManualOpen: schedule.Value.EnableShadowTranslationAfterManualOpen,
                            TriggerLocalTime: triggerLocal,
                            ResumeAutomationAfter: schedule.Value.ResumeAutomationAfter,
                            ResumeAutomationAtLocalTime: schedule.Value.ResumeAutomationAtLocalTime,
                            ResumeAutomationScene: schedule.Value.ResumeAutomationScene));
                    }
                }
            }
        }

        _lastEvaluationUtc = nowUtc;
        return due;
    }

    private CronosExpression? ResolveCron(string transitionKey, string expression)
    {
        if (_cronByTransitionKey.TryGetValue(transitionKey, out var cached))
            return cached;

        try
        {
            var format = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length == 6
                ? CronFormat.IncludeSeconds
                : CronFormat.Standard;
            cached = CronosExpression.Parse(expression, format);
        }
        catch
        {
            cached = null;
        }

        _cronByTransitionKey[transitionKey] = cached;
        return cached;
    }
}

/// <summary>
/// Quartz-based room schedule evaluator.
/// </summary>
/// <remarks>
/// This implementation uses Quartz cron parsing and next-occurrence calculation but keeps execution in-process.
/// It does not register Quartz jobs/triggers yet; it only evaluates due transitions.
/// </remarks>
public sealed class QuartzRoomScheduleEvaluator(TimeZoneInfo? timeZone = null) : IRoomScheduleEvaluator
{
    private readonly TimeZoneInfo _timeZone = timeZone ?? TimeZoneInfo.Local;
    private readonly Dictionary<string, QuartzExpression?> _cronByTransitionKey = [];
    private DateTimeOffset _lastEvaluation;
    private bool _isPrimed;

    public IReadOnlyList<RoomScheduleDueTransition> EvaluateDueTransitions(Model.Model model, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!_isPrimed)
        {
            _lastEvaluation = now;
            _isPrimed = true;
            return [];
        }

        var due = new List<RoomScheduleDueTransition>();
        foreach (var building in model.Buildings.Values)
        {
            foreach (var floor in building.Floors.Values)
            {
                foreach (var room in floor.Rooms.Values)
                {
                    if (room.Configuration.ScheduleTransitions.Count == 0)
                        continue;

                    var roomKey = $"{building.Name}/{floor.Name}/{room.Name}";
                    foreach (var schedule in room.Configuration.ScheduleTransitions)
                    {
                        var transitionKey = $"{roomKey}/{schedule.Key}";
                        var cron = ResolveCron(transitionKey, schedule.Value.CronExpression);
                        if (cron is null)
                            continue;

                        var next = cron.GetNextValidTimeAfter(_lastEvaluation);
                        if (next is null || next > now)
                            continue;

                        var triggerLocal = TimeZoneInfo.ConvertTime(next.Value, _timeZone).DateTime;
                        due.Add(new RoomScheduleDueTransition(
                            RoomKey: roomKey,
                            ScheduleKey: schedule.Key,
                            Scene: schedule.Value.Scene,
                            CloseOnly: schedule.Value.CloseOnly,
                            ManualOpenGracePeriod: schedule.Value.ManualOpenGracePeriod,
                            EnableShadowTranslationAfterManualOpen: schedule.Value.EnableShadowTranslationAfterManualOpen,
                            TriggerLocalTime: triggerLocal,
                            ResumeAutomationAfter: schedule.Value.ResumeAutomationAfter,
                            ResumeAutomationAtLocalTime: schedule.Value.ResumeAutomationAtLocalTime,
                            ResumeAutomationScene: schedule.Value.ResumeAutomationScene));
                    }
                }
            }
        }

        _lastEvaluation = now;
        return due;
    }

    private QuartzExpression? ResolveCron(string transitionKey, string expression)
    {
        if (_cronByTransitionKey.TryGetValue(transitionKey, out var cached))
            return cached;

        try
        {
            cached = new QuartzExpression(expression)
            {
                TimeZone = _timeZone,
            };
        }
        catch
        {
            cached = null;
        }

        _cronByTransitionKey[transitionKey] = cached;
        return cached;
    }
}

/// <summary>
/// Due room schedule transition.
/// </summary>
public sealed record RoomScheduleDueTransition(
    string RoomKey,
    string ScheduleKey,
    int Scene,
    bool CloseOnly,
    TimeSpan ManualOpenGracePeriod,
    bool EnableShadowTranslationAfterManualOpen,
    DateTime TriggerLocalTime,
    TimeSpan? ResumeAutomationAfter,
    TimeSpan? ResumeAutomationAtLocalTime,
    int? ResumeAutomationScene);
