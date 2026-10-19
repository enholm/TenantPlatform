using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantPlatform.Core.PersonalPages;
namespace TenantPlatform.Infrastructure.Persistence.Configurations;
public sealed class PersonalPageSetConfiguration : IEntityTypeConfiguration<PersonalPageSet>
{
    public void Configure(EntityTypeBuilder<PersonalPageSet> b)
    {
        b.ToTable("personal_page_sets");
        b.HasKey(x => new { x.AccountId, x.OwnerUserId });
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.Property(x => x.DefinitionsJson).HasMaxLength(524288).IsRequired();
    }
}
