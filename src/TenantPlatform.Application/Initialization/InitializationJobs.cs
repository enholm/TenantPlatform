using TenantPlatform.Application.Security;
namespace TenantPlatform.Application.Initialization;
public interface IPlatformSeedData { Task InitializeAsync(CancellationToken ct); }
public interface IDemoSeedData { Task InitializeAsync(CancellationToken ct); }
public interface IBootstrapEnvironment { bool IsDevelopment { get; } }
public sealed class PlatformInitializationJob(IPlatformSeedData data, ISystemWorkContext system)
{
    public Task InitializeAsync(CancellationToken ct = default)
    {
        SystemWorkAccess.Require(system, SystemWorkKind.PlatformSeed);
        return data.InitializeAsync(ct);
    }
}
public sealed class DemoInitializationJob(IDemoSeedData data, ISystemWorkContext system, IBootstrapEnvironment environment)
{
    public Task InitializeAsync(CancellationToken ct = default)
    {
        SystemWorkAccess.Require(system, SystemWorkKind.DemoSeed);
        return environment.IsDevelopment ? data.InitializeAsync(ct) : Task.CompletedTask;
    }
}
