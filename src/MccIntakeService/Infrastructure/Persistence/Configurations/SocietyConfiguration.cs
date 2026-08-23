using MccIntakeService.Domain.Societies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MccIntakeService.Infrastructure.Persistence.Configurations;

public sealed class SocietyConfiguration : IEntityTypeConfiguration<Society>
{
    public void Configure(EntityTypeBuilder<Society> builder)
    {
        builder.ToTable("societies");

        builder.HasKey(society => society.Id);

        builder.Property(society => society.Code)
            .HasMaxLength(Society.MaxCodeLength)
            .IsRequired();

        builder.Property(society => society.Name)
            .HasMaxLength(Society.MaxNameLength)
            .IsRequired();

        builder.Property(society => society.CanLabelPrefix)
            .HasMaxLength(Society.MaxCodeLength)
            .IsRequired();

        builder.Property(society => society.ContactPerson)
            .HasMaxLength(Society.MaxContactPersonLength);

        builder.Property(society => society.ContactNumber)
            .HasMaxLength(Society.MaxContactNumberLength);

        builder.Property(society => society.IsActive)
            .IsRequired();

        builder.HasIndex(society => society.Code)
            .IsUnique()
            .HasDatabaseName("ux_societies_code");

        builder.HasData(SocietySeed.Societies);
    }
}
