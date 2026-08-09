using Microsoft.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore.Metadata.Builders;

using TenantPlatform.Core.Identity;

namespace TenantPlatform.Infrastructure.Persistence.Configurations;


public class LoginAccountConfiguration
    : IEntityTypeConfiguration<LoginAccount>
{
    public void Configure(EntityTypeBuilder<LoginAccount> builder)
    {
        builder.ToTable("login_accounts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Email)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(x => x.PasswordHash)
            .IsRequired();

        builder.HasIndex(x => x.Email)
            .IsUnique();

        builder.HasIndex(x => x.UserId)
            .IsUnique();

        builder.HasOne(x => x.User)
            .WithOne(x => x.LoginAccount)
            .HasForeignKey<LoginAccount>(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}