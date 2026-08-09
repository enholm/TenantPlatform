using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class UserAccountRoleConfiguration
    : IEntityTypeConfiguration<UserAccountRole>
{
    public void Configure(EntityTypeBuilder<UserAccountRole> builder)
    {
        builder.ToTable("user_account_roles");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Role)
            .HasConversion<int>()
            .IsRequired();

        builder.HasOne(x => x.UserAccount)
            .WithMany(x => x.UserAccountRoles)
            .HasForeignKey(x => x.UserAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Building>()
            .WithMany()
            .HasForeignKey(x => x.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.UserAccountId);
        builder.HasIndex(x => x.OrganizationId);
        builder.HasIndex(x => x.BuildingId);

        builder.HasIndex(x => new
        {
            x.UserAccountId,
            x.OrganizationId,
            x.BuildingId,
            x.Role
        })
        .IsUnique();
    }
}