using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Occupancies;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class OccupancyConfiguration : IEntityTypeConfiguration<Occupancy>
{
    public void Configure(EntityTypeBuilder<Occupancy> builder)
    {
        builder.ToTable("occupancies");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ValidFrom)
            .IsRequired();

        builder.Property(x => x.ValidTo);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.TenantOrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.AccountId);
        builder.HasIndex(x => x.TenantOrganizationId);
        builder.HasIndex(x => x.UnitId);

        builder.HasIndex(x => new
        {
            x.TenantOrganizationId,
            x.UnitId,
            x.ValidFrom
        });
    }
}
