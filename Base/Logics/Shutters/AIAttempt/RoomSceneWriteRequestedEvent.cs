namespace HomeCompanion.Base.Logics.Shutters.AIAttempt;

/// <summary>
/// Published when a logic requests writing a room scene value.
/// </summary>
public sealed class RoomSceneWriteRequestedEvent : HomeCompanionEvent
{
    public string RoomKey { get; init; } = string.Empty;
    public string ScheduleKey { get; init; } = string.Empty;
    public int Scene { get; init; }
    public bool CloseOnly { get; init; }
    public TimeSpan ManualOpenGracePeriod { get; init; }
    public bool EnableShadowTranslationAfterManualOpen { get; init; }
    public DateTime TriggerLocalTime { get; init; }
    public TimeSpan? ResumeAutomationAfter { get; init; }
    public TimeSpan? ResumeAutomationAtLocalTime { get; init; }
    public int? ResumeAutomationScene { get; init; }
}
