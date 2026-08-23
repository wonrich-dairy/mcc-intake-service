using MccIntakeService.Domain.Consignments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MccIntakeService.Infrastructure.Persistence.Configurations;

public sealed class ConsignmentCanConfiguration : IEntityTypeConfiguration<ConsignmentCan>
{
    public void Configure(EntityTypeBuilder<ConsignmentCan> builder)
    {
        builder.ToTable("consignment_cans");

        builder.HasKey(can => can.Id);

        builder.Property(can => can.CanLabel)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(can => can.CanNumber)
            .IsRequired();

        builder.Property(can => can.QuantityLitres)
            .HasPrecision(8, 2)
            .IsRequired();

        // A physical can can only be delivered once per consignment.
        builder.HasIndex(can => new { can.ConsignmentId, can.CanNumber })
            .IsUnique()
            .HasDatabaseName("ux_consignment_cans_consignment_can_number");
    }
}
