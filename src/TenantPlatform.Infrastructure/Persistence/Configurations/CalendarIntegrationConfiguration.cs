using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.MeetingRooms;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;

public class CalendarIntegrationConfiguration : IEntityTypeConfiguration<CalendarIntegration>
{
    public void Configure(EntityTypeBuilder<CalendarIntegration> builder)
    {
        builder.ToTable("calendar_integrations", table =>
            table.HasCheckConstraint("CK_calendar_integrations_provider", "\"Provider\" IN (1, 2)"));
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.AccountId, x.Id });
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Provider).HasConversion<int>();
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.AccountId);
    }
}
