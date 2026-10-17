namespace TenantPlatform.Application.Security;

// A host-provided work identity, never a tool/request argument or a user administrator flag.
// It grants only the dedicated job entry point; user use cases do not consult it.
public enum SystemWorkKind { AgreementAnalysisCleanup, AgreementReminders, LeasingInvoices, LeasingLifecycle, ServiceRequestInbound, EmailOutbox, PlatformSeed, DemoSeed }
public interface ISystemWorkContext { SystemWorkKind? CurrentWork { get; } }
public static class SystemWorkAccess
{
    public static void Require(ISystemWorkContext context, SystemWorkKind work)
    { if (context.CurrentWork != work) throw new UnauthorizedAccessException(); }
}
