using System.ComponentModel.DataAnnotations;

namespace HomeCompanion.Calendar;

/// <summary>
/// Represents a user-defined calendar entry that can publish an attributed calendar event type at start and end.
/// </summary>
public sealed class CalendarEntry
{
    /// <summary>Gets or sets the unique identifier of the entry.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the user-facing title of the entry.</summary>
    [Required]
    [MaxLength(160)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the assembly-qualified type name of the selected <see cref="Events.ICalendarEvent"/> implementation.
    /// </summary>
    [Required]
    [MaxLength(512)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>Gets or sets the start time interpreted in the entry timezone.</summary>
    public DateTimeOffset StartTime { get; set; }

    /// <summary>Gets or sets the end time interpreted in the entry timezone.</summary>
    public DateTimeOffset EndTime { get; set; }

    /// <summary>Gets or sets whether this entry uses recurring schedule behavior.</summary>
    public bool IsRecurring { get; set; }

    /// <summary>
    /// Gets or sets a Quartz cron expression used for recurring start triggers when <see cref="IsRecurring"/> is enabled.
    /// </summary>
    [MaxLength(120)]
    public string? RecurrenceCronExpression { get; set; }

    /// <summary>
    /// Gets or sets the timezone identifier used for schedule interpretation.
    /// Defaults to server local timezone.
    /// </summary>
    [MaxLength(120)]
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;

    /// <summary>Gets or sets whether this entry is active for scheduling.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets entry metadata as JSON object payload.
    /// </summary>
    [Required]
    public string MetadataJson { get; set; } = "{}";

    /// <summary>Gets or sets the creation timestamp in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Gets or sets the last update timestamp in UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
