using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class UserRoleAssignmentConfiguration
    : IEntityTypeConfiguration<UserRoleAssignment>
{
    public void Configure(EntityTypeBuilder<UserRoleAssignment> builder)
    {
        builder.ToTable("user_role_assignments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Role)
            .HasConversion<int>()
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Building>()
            .WithMany()
            .HasForeignKey(x => x.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.AccountId);
        builder.HasIndex(x => x.OrganizationId);
        builder.HasIndex(x => x.BuildingId);

        builder.HasIndex(x => new
        {
            x.UserId,
            x.AccountId,
            x.OrganizationId,
            x.BuildingId,
            x.Role
        })
        .IsUnique();
    }
}
