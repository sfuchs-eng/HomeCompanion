namespace HomeCompanion.Core.Calendar;

internal sealed class CalendarEntryEntity
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public bool IsRecurring { get; set; }
    public string? RecurrenceCronExpression { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
