namespace HomeCompanion.Calendar;

/// <summary>
/// Synchronizes calendar entries with Quartz scheduler triggers.
/// </summary>
public interface ICalendarScheduler
{
    /// <summary>
    /// Reconciles persisted calendar entries with scheduler triggers.
    /// </summary>
    ValueTask ReconcileAsync(CancellationToken cancellationToken = default);
}
