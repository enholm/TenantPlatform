using TenantPlatform.Application.Security;
namespace TenantPlatform.Infrastructure.Authorization;

// Composition-root adapter for a specific hosted job; no HTTP/client binding.
public sealed class HostSystemWorkContext(SystemWorkKind? work) : ISystemWorkContext
{
    public SystemWorkKind? CurrentWork { get; } = work;
}
