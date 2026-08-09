using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Networking;
using TenantPlatform.Core.Occupancies;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;
using TenantPlatform.Core.Services;

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
    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();
    public DbSet<ServiceDefinition> ServiceDefinitions => Set<ServiceDefinition>();
    public DbSet<ServiceDefinitionTranslation> ServiceDefinitionTranslations => Set<ServiceDefinitionTranslation>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<NetworkSsid> NetworkSsids => Set<NetworkSsid>();
    public DbSet<SsidRequestDetails> SsidRequestDetails => Set<SsidRequestDetails>();
    public DbSet<NetworkEnvironment> NetworkEnvironments => Set<NetworkEnvironment>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(TenantPlatformDbContext).Assembly);
    }
}
