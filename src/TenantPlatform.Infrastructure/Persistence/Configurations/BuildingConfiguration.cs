using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Properties;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class BuildingConfiguration : IEntityTypeConfiguration<Building>
{
    public void Configure(EntityTypeBuilder<Building> builder)
    {
        builder.ToTable("buildings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.AddressLine1)
            .HasMaxLength(250);

        builder.Property(x => x.PostalCode)
            .HasMaxLength(20);

        builder.Property(x => x.City)
            .HasMaxLength(100);

        builder.Property(x => x.CountryCode)
            .HasMaxLength(2)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .IsRequired();

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.AccountId);
    }
}
