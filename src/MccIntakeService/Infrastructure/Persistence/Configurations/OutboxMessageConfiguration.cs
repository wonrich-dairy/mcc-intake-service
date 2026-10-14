using MccIntakeService.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MccIntakeService.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Topic).HasMaxLength(200).IsRequired();
        builder.Property(message => message.Key).HasMaxLength(100).IsRequired();
        builder.Property(message => message.EventType).HasMaxLength(100).IsRequired();
        builder.Property(message => message.Payload).IsRequired();
        builder.Property(message => message.HeadersJson).IsRequired();
        builder.Property(message => message.CreatedAtUtc).IsRequired();
        builder.Property(message => message.Status).HasMaxLength(20).IsRequired();
        builder.Property(message => message.CorrelationId).HasMaxLength(100).IsRequired();
        builder.Property(message => message.LastError).HasMaxLength(2000);

        // The relay reads the oldest pending rows first, on every poll.
        builder.HasIndex(message => new { message.Status, message.CreatedAtUtc })
            .HasDatabaseName("ix_outbox_messages_status_created");
    }
}
