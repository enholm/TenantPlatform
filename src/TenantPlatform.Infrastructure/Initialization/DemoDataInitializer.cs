using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Networking;
using TenantPlatform.Core.Occupancies;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;
using TenantPlatform.Core.Services;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Infrastructure.Authentication;

namespace TenantPlatform.Infrastructure.Initialization;

public static class DemoDataInitializer
{
    public static async Task InitializeAsync(
        TenantPlatformDbContext dbContext,
        PasswordService passwordService,
        CancellationToken cancellationToken = default)
    {
        await CreateAccountAsync(dbContext, cancellationToken);
        await CreateOrganizationsAsync(dbContext, cancellationToken);
        await CreateBuildingAsync(dbContext, cancellationToken);
        await CreateUnitsAsync(dbContext, cancellationToken);
        await CreateOccupanciesAsync(dbContext, cancellationToken);
        await CreateUsersAsync(dbContext, cancellationToken);

        Console.WriteLine("Creating user accounts...");
        await CreateUserAccountsAsync(dbContext, cancellationToken);

        Console.WriteLine("Creating user roles...");
        await CreateUserRolesAsync(dbContext, cancellationToken);        await CreateServiceDefinitionsAsync(dbContext, cancellationToken);

        await CreateNetworkEnvironmentAsync(dbContext, cancellationToken);
        await CreateSsidsAsync(dbContext, cancellationToken);
        await CreateServiceRequestsAsync(dbContext, cancellationToken);

        await CreateLoginAccountsAsync(dbContext, passwordService, cancellationToken);
    }

 private static async Task CreateLoginAccountsAsync(
    TenantPlatformDbContext dbContext,
    PasswordService passwordService,
    CancellationToken cancellationToken)
{
    if (!await dbContext.LoginAccounts.AnyAsync(
            x => x.Id == SeedIds.PerLoginAccount,
            cancellationToken))
    {
        var perLoginAccount = new LoginAccount
        {
            Id = SeedIds.PerLoginAccount,
            UserId = SeedIds.PerPedersen,
            Email = "per@nordicproperty.example",
            IsEnabled = true,
            FailedLoginCount = 0,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        perLoginAccount.PasswordHash =
            passwordService.HashPassword(
                perLoginAccount,
                "ChangeMe123!");

        dbContext.LoginAccounts.Add(perLoginAccount);
    }

    if (!await dbContext.LoginAccounts.AnyAsync(
            x => x.Id == SeedIds.OleLoginAccount,
            cancellationToken))
    {
        var oleLoginAccount = new LoginAccount
        {
            Id = SeedIds.OleLoginAccount,
            UserId = SeedIds.OleOlsen,
            Email = "ole@acme.example",
            IsEnabled = true,
            FailedLoginCount = 0,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        oleLoginAccount.PasswordHash =
            passwordService.HashPassword(
                oleLoginAccount,
                "ChangeMe123!");

        dbContext.LoginAccounts.Add(oleLoginAccount);
    }

    await dbContext.SaveChangesAsync(cancellationToken);
}
    private static async Task CreateAccountAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Accounts.AnyAsync(
                x => x.Id == SeedIds.NordicPropertyAccount,
                cancellationToken))
        {
            return;
        }

        dbContext.Accounts.Add(new Account
        {
            Id = SeedIds.NordicPropertyAccount,
            Name = "Nordic Property AS",
            DefaultLanguage = "nb-NO",
            IsActive = true
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task CreateOrganizationsAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Organizations.AnyAsync(
                x => x.Id == SeedIds.NordicPropertyOrganization,
                cancellationToken))
        {
            dbContext.Organizations.Add(new Organization
            {
                Id = SeedIds.NordicPropertyOrganization,
                AccountId = SeedIds.NordicPropertyAccount,
                Name = "Nordic Property AS",
                OrganizationNumber = "999888777",
                Type = OrganizationType.PropertyManager,
                IsActive = true
            });
        }

        if (!await dbContext.Organizations.AnyAsync(
                x => x.Id == SeedIds.AcmeOrganization,
                cancellationToken))
        {
            dbContext.Organizations.Add(new Organization
            {
                Id = SeedIds.AcmeOrganization,
                AccountId = SeedIds.NordicPropertyAccount,
                Name = "Acme Consulting AS",
                OrganizationNumber = "999888776",
                Type = OrganizationType.Tenant,
                IsActive = true
            });
        }

        if (!await dbContext.Organizations.AnyAsync(
                x => x.Id == SeedIds.ContosoOrganization,
                cancellationToken))
        {
            dbContext.Organizations.Add(new Organization
            {
                Id = SeedIds.ContosoOrganization,
                AccountId = SeedIds.NordicPropertyAccount,
                Name = "Contoso Energy AS",
                OrganizationNumber = "999888775",
                Type = OrganizationType.Tenant,
                IsActive = true
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task CreateBuildingAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Buildings.AnyAsync(
                x => x.Id == SeedIds.OsloAtrium,
                cancellationToken))
        {
            return;
        }

        dbContext.Buildings.Add(new Building
        {
            Id = SeedIds.OsloAtrium,
            AccountId = SeedIds.NordicPropertyAccount,
            Name = "Oslo Atrium",
            AddressLine1 = "Atriumveien 1",
            PostalCode = "0001",
            City = "Oslo",
            CountryCode = "NO",
            IsActive = true
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task CreateUnitsAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Units.AnyAsync(
                x => x.Id == SeedIds.Floor1,
                cancellationToken))
        {
            dbContext.Units.Add(new Unit
            {
                Id = SeedIds.Floor1,
                AccountId = SeedIds.NordicPropertyAccount,
                BuildingId = SeedIds.OsloAtrium,
                Name = "1. etasje",
                Type = UnitType.Floor,
                IsActive = true
            });
        }

        if (!await dbContext.Units.AnyAsync(
                x => x.Id == SeedIds.Floor2,
                cancellationToken))
        {
            dbContext.Units.Add(new Unit
            {
                Id = SeedIds.Floor2,
                AccountId = SeedIds.NordicPropertyAccount,
                BuildingId = SeedIds.OsloAtrium,
                Name = "2. etasje",
                Type = UnitType.Floor,
                IsActive = true
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (!await dbContext.Units.AnyAsync(
                x => x.Id == SeedIds.Unit101,
                cancellationToken))
        {
            dbContext.Units.Add(new Unit
            {
                Id = SeedIds.Unit101,
                AccountId = SeedIds.NordicPropertyAccount,
                BuildingId = SeedIds.OsloAtrium,
                ParentUnitId = SeedIds.Floor1,
                Name = "1.101",
                Type = UnitType.Office,
                IsActive = true
            });
        }

        if (!await dbContext.Units.AnyAsync(
                x => x.Id == SeedIds.Unit201,
                cancellationToken))
        {
            dbContext.Units.Add(new Unit
            {
                Id = SeedIds.Unit201,
                AccountId = SeedIds.NordicPropertyAccount,
                BuildingId = SeedIds.OsloAtrium,
                ParentUnitId = SeedIds.Floor2,
                Name = "2.101",
                Type = UnitType.Office,
                IsActive = true
            });
        }

        if (!await dbContext.Units.AnyAsync(
                x => x.Id == SeedIds.Unit202,
                cancellationToken))
        {
            dbContext.Units.Add(new Unit
            {
                Id = SeedIds.Unit202,
                AccountId = SeedIds.NordicPropertyAccount,
                BuildingId = SeedIds.OsloAtrium,
                ParentUnitId = SeedIds.Floor2,
                Name = "2.102",
                Type = UnitType.Office,
                IsActive = true
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task CreateOccupanciesAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Occupancies.AnyAsync(
                x => x.Id == SeedIds.AcmeOccupancy,
                cancellationToken))
        {
            dbContext.Occupancies.Add(new Occupancy
            {
                Id = SeedIds.AcmeOccupancy,
                AccountId = SeedIds.NordicPropertyAccount,
                TenantOrganizationId = SeedIds.AcmeOrganization,
                UnitId = SeedIds.Unit201,
                ValidFrom = new DateOnly(2026, 1, 1)
            });
        }

        if (!await dbContext.Occupancies.AnyAsync(
                x => x.Id == SeedIds.ContosoOccupancy,
                cancellationToken))
        {
            dbContext.Occupancies.Add(new Occupancy
            {
                Id = SeedIds.ContosoOccupancy,
                AccountId = SeedIds.NordicPropertyAccount,
                TenantOrganizationId = SeedIds.ContosoOrganization,
                UnitId = SeedIds.Unit202,
                ValidFrom = new DateOnly(2026, 1, 1)
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task CreateUsersAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Users.AnyAsync(
                x => x.Id == SeedIds.PerPedersen,
                cancellationToken))
        {
            dbContext.Users.Add(new User
            {
                Id = SeedIds.PerPedersen,
                Email = "per@nordicproperty.example",
                FirstName = "Per",
                LastName = "Pedersen",
                PreferredLanguage = "nb-NO",
                IsActive = true
            });
        }

        if (!await dbContext.Users.AnyAsync(
                x => x.Id == SeedIds.OleOlsen,
                cancellationToken))
        {
            dbContext.Users.Add(new User
            {
                Id = SeedIds.OleOlsen,
                Email = "ole@acme.example",
                FirstName = "Ole",
                LastName = "Olsen",
                PreferredLanguage = "nb-NO",
                IsActive = true
            });
        }

        if (!await dbContext.Users.AnyAsync(
                x => x.Id == SeedIds.KariHansen,
                cancellationToken))
        {
            dbContext.Users.Add(new User
            {
                Id = SeedIds.KariHansen,
                Email = "kari@contoso.example",
                FirstName = "Kari",
                LastName = "Hansen",
                PreferredLanguage = "en-GB",
                IsActive = true
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task CreateUserRolesAsync(
    TenantPlatformDbContext dbContext,
    CancellationToken cancellationToken)
    {
        if (!await dbContext.UserAccountRoles.AnyAsync(
                x => x.Id == SeedIds.PerPropertyAdminRole,
                cancellationToken))
        {
            dbContext.UserAccountRoles.Add(new UserAccountRole
            {
                Id = SeedIds.PerPropertyAdminRole,
                UserAccountId = SeedIds.PerNordicPropertyUserAccount,
                BuildingId = SeedIds.OsloAtrium,
                Role = UserRole.PropertyAdmin
            });
        }

        if (!await dbContext.UserAccountRoles.AnyAsync(
                x => x.Id == SeedIds.PerNordicPropertyAccountAdminRole,
                cancellationToken))
        {
            dbContext.UserAccountRoles.Add(new UserAccountRole
            {
                Id = SeedIds.PerNordicPropertyAccountAdminRole,
                UserAccountId = SeedIds.PerNordicPropertyUserAccount,
                Role = UserRole.AccountAdmin
            });
        }

        if (!await dbContext.UserAccountRoles.AnyAsync(
                x => x.Id == SeedIds.OleTenantAdminRole,
                cancellationToken))
        {
            dbContext.UserAccountRoles.Add(new UserAccountRole
            {
                Id = SeedIds.OleTenantAdminRole,
                UserAccountId = SeedIds.OleNordicPropertyUserAccount,
                OrganizationId = SeedIds.AcmeOrganization,
                Role = UserRole.TenantAdmin
            });
        }

        if (!await dbContext.UserAccountRoles.AnyAsync(
                x => x.Id == SeedIds.KariTenantUserRole,
                cancellationToken))
        {
            dbContext.UserAccountRoles.Add(new UserAccountRole
            {
                Id = SeedIds.KariTenantUserRole,
                UserAccountId = SeedIds.KariNordicPropertyUserAccount,
                OrganizationId = SeedIds.ContosoOrganization,
                Role = UserRole.TenantUser
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
    private static async Task CreateServiceDefinitionsAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.ServiceDefinitions.AnyAsync(
                x => x.Id == SeedIds.NetworkSsidService,
                cancellationToken))
        {
            dbContext.ServiceDefinitions.Add(new ServiceDefinition
            {
                Id = SeedIds.NetworkSsidService,
                AccountId = SeedIds.NordicPropertyAccount,
                Code = "NETWORK_SSID",
                IsActive = true
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (!await dbContext.ServiceDefinitionTranslations.AnyAsync(
                x => x.Id == SeedIds.NetworkSsidServiceNorwegian,
                cancellationToken))
        {
            dbContext.ServiceDefinitionTranslations.Add(
                new ServiceDefinitionTranslation
                {
                    Id = SeedIds.NetworkSsidServiceNorwegian,
                    ServiceDefinitionId = SeedIds.NetworkSsidService,
                    LanguageCode = "nb-NO",
                    Name = "Trådløst nettverk",
                    Description =
                        "Opprett, endre eller slett et trådløst nettverk."
                });
        }

        if (!await dbContext.ServiceDefinitionTranslations.AnyAsync(
                x => x.Id == SeedIds.NetworkSsidServiceEnglish,
                cancellationToken))
        {
            dbContext.ServiceDefinitionTranslations.Add(
                new ServiceDefinitionTranslation
                {
                    Id = SeedIds.NetworkSsidServiceEnglish,
                    ServiceDefinitionId = SeedIds.NetworkSsidService,
                    LanguageCode = "en-GB",
                    Name = "Wireless network",
                    Description =
                        "Create, modify or delete a wireless network."
                });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task CreateNetworkEnvironmentAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (await dbContext.NetworkEnvironments.AnyAsync(
                x => x.Id == SeedIds.CorporateLan,
                cancellationToken))
        {
            return;
        }

        dbContext.NetworkEnvironments.Add(new NetworkEnvironment
        {
            Id = SeedIds.CorporateLan,
            AccountId = SeedIds.NordicPropertyAccount,
            BuildingId = SeedIds.OsloAtrium,
            Name = "Corporate LAN",
            Vendor = NetworkVendor.CiscoMeraki,
            IsActive = true
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task CreateSsidsAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.NetworkSsids.AnyAsync(
                x => x.Id == SeedIds.AcmeCorpSsid,
                cancellationToken))
        {
            dbContext.NetworkSsids.Add(new NetworkSsid
            {
                Id = SeedIds.AcmeCorpSsid,
                AccountId = SeedIds.NordicPropertyAccount,
                NetworkEnvironmentId = SeedIds.CorporateLan,
                TenantOrganizationId = SeedIds.AcmeOrganization,
                UnitId = SeedIds.Unit201,
                Name = "ACME-CORP",
                VlanId = 312,
                SecurityType = SsidSecurityType.WPA3Enterprise,
                IsBroadcast = true,
                Status = NetworkSsidStatus.Active,
                CreatedAt = new DateTimeOffset(
                    2026, 1, 15, 10, 0, 0, TimeSpan.Zero)
            });
        }

        if (!await dbContext.NetworkSsids.AnyAsync(
                x => x.Id == SeedIds.ContosoSsid,
                cancellationToken))
        {
            dbContext.NetworkSsids.Add(new NetworkSsid
            {
                Id = SeedIds.ContosoSsid,
                AccountId = SeedIds.NordicPropertyAccount,
                NetworkEnvironmentId = SeedIds.CorporateLan,
                TenantOrganizationId = SeedIds.ContosoOrganization,
                UnitId = SeedIds.Unit202,
                Name = "CONTOSO",
                VlanId = 318,
                SecurityType = SsidSecurityType.WPA3Enterprise,
                IsBroadcast = true,
                Status = NetworkSsidStatus.Active,
                CreatedAt = new DateTimeOffset(
                    2026, 2, 1, 10, 0, 0, TimeSpan.Zero)
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task CreateServiceRequestsAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.ServiceRequests.AnyAsync(
                x => x.Id == SeedIds.AcmeSsidChangeRequest,
                cancellationToken))
        {
            dbContext.ServiceRequests.Add(new ServiceRequest
            {
                Id = SeedIds.AcmeSsidChangeRequest,
                AccountId = SeedIds.NordicPropertyAccount,
                ServiceDefinitionId = SeedIds.NetworkSsidService,
                RequesterUserId = SeedIds.OleOlsen,
                RequesterOrganizationId = SeedIds.AcmeOrganization,
                BuildingId = SeedIds.OsloAtrium,
                UnitId = SeedIds.Unit201,
                Status = ServiceRequestStatus.Submitted,
                CreatedAt = new DateTimeOffset(
                    2026, 8, 8, 10, 0, 0, TimeSpan.Zero),
                SubmittedAt = new DateTimeOffset(
                    2026, 8, 8, 10, 5, 0, TimeSpan.Zero)
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (!await dbContext.SsidRequestDetails.AnyAsync(
                x => x.Id == SeedIds.AcmeSsidChangeRequestDetails,
                cancellationToken))
        {
            dbContext.SsidRequestDetails.Add(new SsidRequestDetails
            {
                Id = SeedIds.AcmeSsidChangeRequestDetails,
                ServiceRequestId = SeedIds.AcmeSsidChangeRequest,
                NetworkEnvironmentId = SeedIds.CorporateLan,
                Action = SsidRequestAction.Update,
                ExistingNetworkSsidId = SeedIds.AcmeCorpSsid,
                RequestedName = "ACME-GUEST",
                RequestedVlanId = 350,
                RequestedSecurityType = SsidSecurityType.WPA3Enterprise,
                RequestedIsBroadcast = true
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task CreateUserAccountsAsync(
    TenantPlatformDbContext dbContext,
    CancellationToken cancellationToken)
    {
        if (!await dbContext.UserAccounts.AnyAsync(
                x => x.Id == SeedIds.PerNordicPropertyUserAccount,
                cancellationToken))
        {
            dbContext.UserAccounts.Add(new UserAccount
            {
                Id = SeedIds.PerNordicPropertyUserAccount,
                UserId = SeedIds.PerPedersen,
                AccountId = SeedIds.NordicPropertyAccount
            });
        }

        if (!await dbContext.UserAccounts.AnyAsync(
                x => x.Id == SeedIds.OleNordicPropertyUserAccount,
                cancellationToken))
        {
            dbContext.UserAccounts.Add(new UserAccount
            {
                Id = SeedIds.OleNordicPropertyUserAccount,
                UserId = SeedIds.OleOlsen,
                AccountId = SeedIds.NordicPropertyAccount
            });
        }

        if (!await dbContext.UserAccounts.AnyAsync(
                x => x.Id == SeedIds.KariNordicPropertyUserAccount,
                cancellationToken))
        {
            dbContext.UserAccounts.Add(new UserAccount
            {
                Id = SeedIds.KariNordicPropertyUserAccount,
                UserId = SeedIds.KariHansen,
                AccountId = SeedIds.NordicPropertyAccount
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}