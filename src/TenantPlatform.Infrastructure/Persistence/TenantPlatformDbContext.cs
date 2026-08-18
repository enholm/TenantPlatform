using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Networking;
using TenantPlatform.Core.Occupancies;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;
using TenantPlatform.Core.Services;
using TenantPlatform.Core.Auditing;

namespace TenantPlatform.Infrastructure.Persistence;

public class TenantPlatformDbContext : DbContext
{
    public TenantPlatformDbContext(
        DbContextOptions<TenantPlatformDbContext> options)
        : base(options)
    {
    }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Occupancy> Occupancies => Set<Occupancy>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserAccountRole> UserAccountRoles => Set<UserAccountRole>();
    public DbSet<ServiceDefinition> ServiceDefinitions => Set<ServiceDefinition>();
    public DbSet<ServiceDefinitionTranslation> ServiceDefinitionTranslations => Set<ServiceDefinitionTranslation>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<NetworkSsid> NetworkSsids => Set<NetworkSsid>();
    public DbSet<SsidRequestDetails> SsidRequestDetails => Set<SsidRequestDetails>();
    public DbSet<NetworkEnvironment> NetworkEnvironments => Set<NetworkEnvironment>();
    public DbSet<LoginAccount> LoginAccounts => Set<LoginAccount>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(TenantPlatformDbContext).Assembly);
    }
}
