using TenantPlatform.Application.ServiceCatalog;
using TenantPlatform.Core.Occupancies;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Security;
using TenantPlatform.Application.ServiceDefinitions;
using TenantPlatform.Application.ServiceDefinitionFields;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;
using TenantPlatform.Core.Services;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.ServiceDefinitions;

static class ServiceDefinitionChecks
{
    public static async Task Run(Factory factory, ReadOnlyGuard guard, string provider)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL service definition: " + name); Console.WriteLine($"PASS {provider} service definition: {name}"); }
        async Task Denied(Func<Task> action, string name) { try { await action(); throw new Exception("Expected denial: " + name); } catch (UnauthorizedAccessException) { Check(true, name); } }
        async Task Invalid(Func<Task> action, string text) { try { await action(); throw new Exception("Expected " + text); } catch (InvalidOperationException ex) when (ex.Message == text) { Check(true, text); } }
        var account = Guid.NewGuid(); var other = Guid.NewGuid(); var admin = Guid.NewGuid(); var tenant = Guid.NewGuid(); var platform = Guid.NewGuid();
        var membership = Guid.NewGuid(); var definition = Guid.NewGuid(); var foreign = Guid.NewGuid(); var organization = Guid.NewGuid(); var foreignOrg = Guid.NewGuid(); var building = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.Accounts.AddRange(new Account { Id = account, Name = "Definitions", DefaultLanguage = "nb-NO" }, new Account { Id = other, Name = "Other" });
            foreach (var id in new[] { admin, tenant, platform })
            {
                db.Users.Add(new User { Id = id, Email = id + "@example.test", FirstName = "Definition", IsActive = true, IsPlatformAdmin = id == platform });
                var m = new UserAccount { Id = id == admin ? membership : Guid.NewGuid(), UserId = id, AccountId = account }; db.UserAccounts.Add(m);
                if (id != platform) db.UserAccountRoles.Add(new() { Id = Guid.NewGuid(), UserAccountId = m.Id, Role = id == admin ? UserRole.AccountAdmin : UserRole.TenantUser, OrganizationId = id == tenant ? organization : null });
            }
            db.Organizations.AddRange(new Organization { Id = organization, AccountId = account, Name = "Local provider", IsActive = true }, new Organization { Id = foreignOrg, AccountId = other, Name = "Secret foreign" });
            db.Buildings.Add(new Building { Id = building, AccountId = account, Name = "Local", CountryCode = "NO" });
            var locationUnit = Guid.NewGuid();
            db.Units.Add(new Unit { Id = locationUnit, AccountId = account, BuildingId = building, Name = "Tenant unit" });
            db.Occupancies.Add(new Occupancy { Id = Guid.NewGuid(), AccountId = account, TenantOrganizationId = organization, UnitId = locationUnit, ValidFrom = new(2020, 1, 1) });
            db.ServiceDefinitions.AddRange(new ServiceDefinition { Id = definition, AccountId = account, Code = "VISIBLE", IsActive = true, IsBookableByTenant = true },
                new ServiceDefinition { Id = foreign, AccountId = other, Code = "FOREIGN" });
            db.ServiceDefinitionTranslations.Add(new() { Id = Guid.NewGuid(), ServiceDefinitionId = definition, LanguageCode = "nb-NO", Name = "Synlig" });
            await db.SaveChangesAsync();
        }
        var actor = new Actor(new(true, admin, account)); var data = new ServiceDefinitionData(factory);
        var service = new ServiceDefinitionService(new AuthorizationService(actor, new AuthorizationData(factory), TimeProvider.System), data);
        var fields = new ServiceDefinitionFieldService(service, data);
        var catalog = new ServiceCatalogService(new AuthorizationService(actor, new AuthorizationData(factory), TimeProvider.System), data,
            new ServiceCatalogLocationData(factory), TimeProvider.System);
        guard.Protect = true;
        try
        {
            Check((await service.GetServiceDefinitionsAsync(account, "en-GB")).Single().Name == "Synlig", "authorized account list preserves fallback translation");
            Check((await service.GetServiceDefinitionAsync(account, definition, "nb-NO"))?.Code == "VISIBLE", "direct concrete definition read");
            await Denied(() => service.GetServiceDefinitionAsync(account, foreign, "nb-NO"), "foreign definition denied");
            actor.Current = new(true, tenant, account);
            Check((await catalog.GetAvailableServicesAsync(account, "nb-NO")).Single().Id == definition &&
                (await catalog.GetServiceAsync(account, definition, "nb-NO"))?.Name == "Synlig", "catalog independently authorizes active member and filters bookable account data");
            Check((await catalog.GetAvailableRequestLocationsAsync(account)).Single().OrganizationId == organization, "catalog locations use trusted actor's tenant role and current occupancy");
            Check(await catalog.GetServiceAsync(account, foreign, "nb-NO") is null, "catalog reveals no foreign service metadata");
            await Denied(() => catalog.GetAvailableServicesAsync(other, "nb-NO"), "catalog rejects a foreign selected account");
            Check((await service.GetServiceDefinitionAsync(account, definition, "nb-NO"))?.Id == definition, "tenant retains bookable active concrete read");
            await Denied(() => service.GetServiceDefinitionsAsync(account, "nb-NO"), "tenant cannot open management list");
            await Denied(() => fields.CreateFieldAsync(account, definition, new()), "tenant cannot modify fields");
            actor.Current = new(true, platform, account);
            await Denied(() => service.GetServiceDefinitionsAsync(account, "nb-NO"), "PlatformAdmin does not imply definition management");
            await Denied(() => catalog.GetAvailableServicesAsync(account, "nb-NO"), "global platform flag does not grant tenant catalog access");
        }
        finally { guard.Protect = false; actor.Current = new(true, admin, account); }
        if (provider == "PostgreSQL")
        {
            var id = await service.CreateServiceDefinitionAsync(account, new() { Code = "  new  ", HandlerType = " ", IsActive = true, IsBookableByTenant = true,
                Translations = [new() { LanguageCode = "en-GB", Name = "  Created  " }] });
            Check((await service.GetServiceDefinitionAsync(account, id, "en-GB")) is { Code: "NEW", Name: "Created", HandlerType: "Generic" }, "creation normalizes definition and translations");
            await service.UpdateServiceDefinitionAsync(account, id, new() { Code = "new", IsActive = true, IsBookableByTenant = true,
                Translations = [new() { LanguageCode = "en-GB", Name = "Updated" }, new() { LanguageCode = "nb-NO", Name = "Oppdatert" }] });
            var field = await fields.CreateFieldAsync(account, id, new() { Key = "  choice  ", FieldType = ServiceFieldType.Choice, OptionsText = "one\ntwo\none",
                Translations = [new() { LanguageCode = "en-GB", Label = "  Choice  " }] });
            Check((await fields.GetFieldAsync(account, id, field)) is { Key: "choice", OptionsText: not null }, "field options and translation roundtrip");
            await fields.UpdateFieldAsync(account, id, field, new() { Key = "choice", FieldType = ServiceFieldType.Text, Translations = [new() { LanguageCode = "nb-NO", Label = "Valg" }] });
            Check((await fields.GetFieldAsync(account, id, field))?.FieldType == ServiceFieldType.Text, "field update belongs to authorized definition");
            await Invalid(() => service.CreateProviderAsync(account, id, new() { ServiceProviderOrganizationId = foreignOrg, IntegrationType = ServiceProviderIntegrationType.Email, RequestEmailAddress = "x@example.test" }), "ServiceProviderOrganizationNotFound");
            var providerId = await service.CreateProviderAsync(account, id, new() { ServiceProviderOrganizationId = organization, IntegrationType = ServiceProviderIntegrationType.Email,
                RequestEmailAddress = " provider@example.test ", IsActive = true, IsDefault = true });
            Check((await service.GetProvidersAsync(account, id)).Single().RequestEmailAddress == "provider@example.test" &&
                (await service.GetProviderOrganizationOptionsAsync(account)).All(x => x.Id != foreignOrg), "provider metadata and selectors remain account bounded");
            await service.UpdateProviderAsync(account, id, providerId, new() { ServiceProviderOrganizationId = organization, IntegrationType = ServiceProviderIntegrationType.Webhook, IsActive = false, IsDefault = true });
            Check((await service.GetProviderAsync(account, id, providerId)) is { IsDefault: false, RequestEmailAddress: null }, "inactive/non-email provider normalization preserved");
            await service.UpdateProviderAsync(account, id, providerId, new() { ServiceProviderOrganizationId = organization, IntegrationType = ServiceProviderIntegrationType.Email, RequestEmailAddress = "p@example.test", IsActive = true });
            Guid requestId = Guid.NewGuid();
            await using (var db = factory.CreateDbContext())
            {
                db.ServiceRequests.Add(new() { Id = requestId, AccountId = account, ServiceDefinitionId = id, RequesterUserId = admin, RequesterOrganizationId = organization,
                    BuildingId = building, AssignedServiceProviderOrganizationId = organization, Status = ServiceRequestStatus.Approved, ReplyToken = Guid.NewGuid().ToString("N"), CreatedAt = DateTimeOffset.UtcNow });
                db.ServiceRequestFieldValues.Add(new() { Id = Guid.NewGuid(), ServiceRequestId = requestId, ServiceDefinitionFieldId = field, Value = "used" });
                await db.SaveChangesAsync();
            }
            Check(!(await service.CanDeleteServiceDefinitionAsync(account, id)).CanDelete && !(await service.CanDeleteProviderAsync(account, id, providerId)).CanDelete, "deletion previews reflect stored requests");
            await Invalid(() => service.DeleteProviderAsync(account, id, providerId), "ServiceProviderHasOpenRequests");
            await Invalid(() => fields.DeleteFieldAsync(account, id, field), "The field cannot be deleted because it has been used in service requests.");
            await using (var db = factory.CreateDbContext())
            { db.ServiceRequestFieldValues.RemoveRange(await db.ServiceRequestFieldValues.Where(x => x.ServiceRequestId == requestId).ToListAsync()); db.ServiceRequests.Remove(await db.ServiceRequests.SingleAsync(x => x.Id == requestId)); await db.SaveChangesAsync(); }
            await fields.DeleteFieldAsync(account, id, field);
            await service.DeleteProviderAsync(account, id, providerId);
            await service.DeleteServiceDefinitionAsync(account, id);
            await using (var db = factory.CreateDbContext()) Check(!await db.ServiceDefinitions.AnyAsync(x => x.Id == id), "field/provider/definition deletion persists atomically");
        }
        await using (var db = factory.CreateDbContext()) { db.UserAccountRoles.RemoveRange(await db.UserAccountRoles.Where(x => x.UserAccountId == membership).ToListAsync()); db.UserAccounts.Remove(await db.UserAccounts.SingleAsync(x => x.Id == membership)); await db.SaveChangesAsync(); }
        await Denied(() => service.GetServiceDefinitionsAsync(account, "nb-NO"), "removed membership applies on reused service");
    }
}
