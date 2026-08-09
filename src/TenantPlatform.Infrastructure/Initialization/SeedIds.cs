namespace TenantPlatform.Infrastructure.Initialization;

public static class SeedIds
{
    // Account
    public static readonly Guid NordicPropertyAccount =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

    // Organizations
    public static readonly Guid NordicPropertyOrganization =
        Guid.Parse("20000000-0000-0000-0000-000000000001");

    public static readonly Guid AcmeOrganization =
        Guid.Parse("20000000-0000-0000-0000-000000000002");

    public static readonly Guid ContosoOrganization =
        Guid.Parse("20000000-0000-0000-0000-000000000003");

    // Building
    public static readonly Guid OsloAtrium =
        Guid.Parse("30000000-0000-0000-0000-000000000001");

    // Units
    public static readonly Guid Floor1 =
        Guid.Parse("40000000-0000-0000-0000-000000000001");

    public static readonly Guid Floor2 =
        Guid.Parse("40000000-0000-0000-0000-000000000002");

    public static readonly Guid Unit101 =
        Guid.Parse("40000000-0000-0000-0000-000000000003");

    public static readonly Guid Unit201 =
        Guid.Parse("40000000-0000-0000-0000-000000000004");

    public static readonly Guid Unit202 =
        Guid.Parse("40000000-0000-0000-0000-000000000005");

    // Occupancies
    public static readonly Guid AcmeOccupancy =
        Guid.Parse("50000000-0000-0000-0000-000000000001");

    public static readonly Guid ContosoOccupancy =
        Guid.Parse("50000000-0000-0000-0000-000000000002");

    // Users
    public static readonly Guid PerPedersen =
        Guid.Parse("60000000-0000-0000-0000-000000000001");

    public static readonly Guid OleOlsen =
        Guid.Parse("60000000-0000-0000-0000-000000000002");

    public static readonly Guid KariHansen =
        Guid.Parse("60000000-0000-0000-0000-000000000003");

    // User roles
    public static readonly Guid PerPropertyAdminRole =
        Guid.Parse("70000000-0000-0000-0000-000000000001");

    public static readonly Guid OleTenantAdminRole =
        Guid.Parse("70000000-0000-0000-0000-000000000002");

    public static readonly Guid KariTenantUserRole =
        Guid.Parse("70000000-0000-0000-0000-000000000003");

    // Service definition
    public static readonly Guid NetworkSsidService =
        Guid.Parse("80000000-0000-0000-0000-000000000001");

    public static readonly Guid NetworkSsidServiceNorwegian =
        Guid.Parse("81000000-0000-0000-0000-000000000001");

    public static readonly Guid NetworkSsidServiceEnglish =
        Guid.Parse("81000000-0000-0000-0000-000000000002");

    // Network environment
    public static readonly Guid CorporateLan =
        Guid.Parse("90000000-0000-0000-0000-000000000001");

    // SSIDs
    public static readonly Guid AcmeCorpSsid =
        Guid.Parse("91000000-0000-0000-0000-000000000001");

    public static readonly Guid ContosoSsid =
        Guid.Parse("91000000-0000-0000-0000-000000000002");

    // Service request
    public static readonly Guid AcmeSsidChangeRequest =
        Guid.Parse("a0000000-0000-0000-0000-000000000001");

    public static readonly Guid AcmeSsidChangeRequestDetails =
        Guid.Parse("a1000000-0000-0000-0000-000000000001");
}