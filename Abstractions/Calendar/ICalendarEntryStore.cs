namespace HomeCompanion.Calendar;

/// <summary>
/// Stores and retrieves calendar entries.
/// </summary>
public interface ICalendarEntryStore
{
    /// <summary>Lists all calendar entries.</summary>
    Task<IReadOnlyList<CalendarEntry>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets a calendar entry by id.</summary>
    Task<CalendarEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates a calendar entry.</summary>
    Task<CalendarEntry> UpsertAsync(CalendarEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Deletes a calendar entry by id.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
