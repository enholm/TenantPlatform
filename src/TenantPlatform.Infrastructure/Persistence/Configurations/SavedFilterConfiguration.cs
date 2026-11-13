using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public sealed class SavedFilterConfiguration : IEntityTypeConfiguration<SavedFilter>
{
    public void Configure(EntityTypeBuilder<SavedFilter> builder)
    {
        builder.ToTable("saved_filters", table =>
        {
            table.HasCheckConstraint("CK_saved_filters_page", "\"Page\" BETWEEN 1 AND 5");
            table.HasCheckConstraint("CK_saved_filters_criteria_version", "\"CriteriaVersion\" > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CriteriaJson).HasColumnType("jsonb").IsRequired();
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.AccountId, x.UserId, x.Page, x.NormalizedName }).IsUnique();
        builder.HasIndex(x => new { x.AccountId, x.UserId, x.Page, x.Name });
    }
}
