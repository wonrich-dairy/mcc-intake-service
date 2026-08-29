using MccIntakeService.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MccIntakeService.Infrastructure.Persistence.Configurations;

public sealed class SyncedRecordConfiguration : IEntityTypeConfiguration<SyncedRecord>
{
    public void Configure(EntityTypeBuilder<SyncedRecord> builder)
    {
        builder.ToTable("synced_records");

        builder.HasKey(record => record.Id);

        builder.Property(record => record.ClientRecordId).HasMaxLength(100).IsRequired();

        builder.Property(record => record.Kind)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(record => record.ResultReference).HasMaxLength(40);
        builder.Property(record => record.SyncedBy).HasMaxLength(100);
        builder.Property(record => record.SyncedAtUtc).IsRequired();

        // The whole point of the table: one client record applies once, however many times a
        // handheld replays its queue after losing connectivity mid-upload.
        builder.HasIndex(record => record.ClientRecordId)
            .IsUnique()
            .HasDatabaseName("ux_synced_records_client_record");
    }
}
