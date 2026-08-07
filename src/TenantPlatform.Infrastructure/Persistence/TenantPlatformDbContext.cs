using Microsoft.EntityFrameworkCore;

namespace TenantPlatform.Infrastructure.Persistence;

public sealed class TenantPlatformDbContext(
    DbContextOptions<TenantPlatformDbContext> options)
    : DbContext(options)
{
}