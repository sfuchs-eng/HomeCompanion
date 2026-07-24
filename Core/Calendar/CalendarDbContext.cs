using Microsoft.EntityFrameworkCore;

namespace HomeCompanion.Core.Calendar;

internal sealed class CalendarDbContext(DbContextOptions<CalendarDbContext> options) : DbContext(options)
{
    public DbSet<CalendarEntryEntity> CalendarEntries => Set<CalendarEntryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CalendarEntryEntity>();
        entity.ToTable("CalendarEntries");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Title).HasMaxLength(160).IsRequired();
        entity.Property(e => e.EventType).HasMaxLength(512).IsRequired();
        entity.Property(e => e.RecurrenceCronExpression).HasMaxLength(120);
        entity.Property(e => e.TimeZoneId).HasMaxLength(120).IsRequired();
        entity.Property(e => e.MetadataJson).HasColumnType("TEXT").IsRequired();
        entity.HasIndex(e => e.IsEnabled);
        entity.HasIndex(e => e.UpdatedAtUtc);
    }
}
