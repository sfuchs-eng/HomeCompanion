using HomeCompanion.Calendar;
using Microsoft.EntityFrameworkCore;

namespace HomeCompanion.Core.Calendar;

internal sealed class EfCalendarEntryStore(
    CalendarDbContext dbContext,
    TimeProvider timeProvider) : ICalendarEntryStore
{
    private readonly CalendarDbContext _dbContext = dbContext;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<IReadOnlyList<CalendarEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.CalendarEntries
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var orderedEntities = entities
            .OrderBy(e => e.StartTime)
            .ToArray();

        return orderedEntities.Select(MapToModel).ToArray();
    }

    public async Task<CalendarEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.CalendarEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : MapToModel(entity);
    }

    public async Task<CalendarEntry> UpsertAsync(CalendarEntry entry, CancellationToken cancellationToken = default)
    {
        var nowUtc = _timeProvider.GetUtcNow();
        var existing = await _dbContext.CalendarEntries
            .SingleOrDefaultAsync(e => e.Id == entry.Id, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            var entity = MapToEntity(entry);
            if (entity.CreatedAtUtc == default)
                entity.CreatedAtUtc = nowUtc;
            entity.UpdatedAtUtc = nowUtc;
            await _dbContext.CalendarEntries.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            existing.Title = entry.Title;
            existing.EventType = entry.EventType;
            existing.StartTime = entry.StartTime;
            existing.EndTime = entry.EndTime;
            existing.IsRecurring = entry.IsRecurring;
            existing.RecurrenceCronExpression = entry.RecurrenceCronExpression;
            existing.TimeZoneId = entry.TimeZoneId;
            existing.IsEnabled = entry.IsEnabled;
            existing.MetadataJson = entry.MetadataJson;
            existing.UpdatedAtUtc = nowUtc;
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (await GetAsync(entry.Id, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var affectedRows = await _dbContext.CalendarEntries
            .Where(e => e.Id == id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        return affectedRows > 0;
    }

    private static CalendarEntry MapToModel(CalendarEntryEntity entity)
    {
        return new CalendarEntry
        {
            Id = entity.Id,
            Title = entity.Title,
            EventType = entity.EventType,
            StartTime = entity.StartTime,
            EndTime = entity.EndTime,
            IsRecurring = entity.IsRecurring,
            RecurrenceCronExpression = entity.RecurrenceCronExpression,
            TimeZoneId = entity.TimeZoneId,
            IsEnabled = entity.IsEnabled,
            MetadataJson = entity.MetadataJson,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc,
        };
    }

    private static CalendarEntryEntity MapToEntity(CalendarEntry entry)
    {
        return new CalendarEntryEntity
        {
            Id = entry.Id,
            Title = entry.Title,
            EventType = entry.EventType,
            StartTime = entry.StartTime,
            EndTime = entry.EndTime,
            IsRecurring = entry.IsRecurring,
            RecurrenceCronExpression = entry.RecurrenceCronExpression,
            TimeZoneId = entry.TimeZoneId,
            IsEnabled = entry.IsEnabled,
            MetadataJson = entry.MetadataJson,
            CreatedAtUtc = entry.CreatedAtUtc,
            UpdatedAtUtc = entry.UpdatedAtUtc,
        };
    }
}
