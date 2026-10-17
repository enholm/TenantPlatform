using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Initialization;
using TenantPlatform.Application.Security;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.Authentication;
using TenantPlatform.Infrastructure.Initialization;
static class BootstrapChecks
{
    public static async Task Run(Factory factory, string provider)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL bootstrap: " + name); Console.WriteLine($"PASS {provider} bootstrap: {name}"); }
        async Task Denied(Func<Task> action) { try { await action(); throw new Exception("Expected denial"); } catch (UnauthorizedAccessException) { } }
        var port = new SeedProbe();
        foreach (var kind in new SystemWorkKind?[] { null, SystemWorkKind.EmailOutbox, SystemWorkKind.DemoSeed })
            await Denied(() => new PlatformInitializationJob(port, new HostSystemWorkContext(kind)).InitializeAsync());
        foreach (var kind in new SystemWorkKind?[] { null, SystemWorkKind.PlatformSeed, SystemWorkKind.AgreementReminders })
            await Denied(() => new DemoInitializationJob(port, new HostSystemWorkContext(kind), new HostBootstrapEnvironment(true)).InitializeAsync());
        Check(port.Calls == 0, "missing or unrelated work identity never reaches seed data");
        await new DemoInitializationJob(port, new HostSystemWorkContext(SystemWorkKind.DemoSeed), new HostBootstrapEnvironment(false)).InitializeAsync();
        Check(port.Calls == 0, "production environment never seeds demo data");
        await new PlatformInitializationJob(port, new HostSystemWorkContext(SystemWorkKind.PlatformSeed)).InitializeAsync();
        await new DemoInitializationJob(port, new HostSystemWorkContext(SystemWorkKind.DemoSeed), new HostBootstrapEnvironment(true)).InitializeAsync();
        Check(port.Calls == 2, "explicit platform and development demo authorities are separate");
        if (provider == "PostgreSQL")
        {
            var job = new DemoInitializationJob(new DemoSeedData(factory, new PasswordService()), new HostSystemWorkContext(SystemWorkKind.DemoSeed), new HostBootstrapEnvironment(true));
            await job.InitializeAsync();
            async Task<int[]> Counts() { await using var db = factory.CreateDbContext(); return [await db.Accounts.CountAsync(), await db.Users.CountAsync(), await db.UserAccounts.CountAsync(), await db.UserAccountRoles.CountAsync(), await db.Organizations.CountAsync(), await db.ServiceDefinitions.CountAsync()]; }
            var once = await Counts(); await Task.WhenAll(job.InitializeAsync(), job.InitializeAsync());
            var repeated = await Counts();
            Check(once.SequenceEqual(repeated), "actual demo bootstrap is repeatable and serializes concurrent hosts in PostgreSQL");
            await new PlatformInitializationJob(new PlatformSeedData(factory), new HostSystemWorkContext(SystemWorkKind.PlatformSeed)).InitializeAsync();
            var platform = await Counts();
            Check(once.SequenceEqual(platform), "current empty platform builder introduces no tenant data");
        }
    }
    private sealed class SeedProbe : IPlatformSeedData, IDemoSeedData
    { public int Calls; public Task InitializeAsync(CancellationToken ct) { Calls++; return Task.CompletedTask; } }
}
