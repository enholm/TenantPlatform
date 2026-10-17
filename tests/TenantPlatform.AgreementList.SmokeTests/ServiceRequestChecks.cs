using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Security;
using TenantPlatform.Application.ServiceRequests;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Occupancies;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;
using TenantPlatform.Core.Services;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.ServiceRequests;

static class ServiceRequestChecks
{
    public static async Task Run(Factory factory, ReadOnlyGuard guard, string provider)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL requests: " + name); Console.WriteLine($"PASS {provider} requests: {name}"); }
        async Task Denied(Func<Task> action) { try { await action(); throw new Exception("Expected denial"); } catch (UnauthorizedAccessException) { } }
        var account = Guid.NewGuid(); var other = Guid.NewGuid(); var admin = Guid.NewGuid(); var tenant = Guid.NewGuid(); var supplier = Guid.NewGuid(); var outsider = Guid.NewGuid();
        var org = Guid.NewGuid(); var providerOrg = Guid.NewGuid(); var building = Guid.NewGuid(); var otherBuilding = Guid.NewGuid(); var unit = Guid.NewGuid(); var occupancy = Guid.NewGuid(); var definition = Guid.NewGuid(); var id = Guid.NewGuid();
        var tenantMembership = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.Accounts.AddRange(new Account { Id = account, Name = "Requests" }, new Account { Id = other, Name = "Other" });
            foreach (var user in new[] { admin, tenant, supplier, outsider })
            {
                db.Users.Add(new() { Id = user, Email = user + "@example.test", FirstName = "Request", IsActive = true, IsPlatformAdmin = user == outsider });
                var membership = new UserAccount { Id = user == tenant ? tenantMembership : Guid.NewGuid(), AccountId = account, UserId = user }; db.UserAccounts.Add(membership);
                if (user != outsider) db.UserAccountRoles.Add(new() { Id = Guid.NewGuid(), UserAccountId = membership.Id, Role = user == admin ? UserRole.AccountAdmin : user == tenant ? UserRole.TenantUser : UserRole.ServiceProviderUser, OrganizationId = user == tenant ? org : user == supplier ? providerOrg : null });
            }
            db.Organizations.AddRange(new Organization { Id = org, AccountId = account, Name = "Tenant" }, new Organization { Id = providerOrg, AccountId = account, Name = "Supplier" });
            db.Buildings.AddRange(new Building { Id = building, AccountId = account, Name = "Building", CountryCode = "NO" }, new Building { Id = otherBuilding, AccountId = account, Name = "Other building", CountryCode = "NO" });
            db.Units.Add(new() { Id = unit, AccountId = account, BuildingId = building, Name = "Unit" });
            db.Occupancies.Add(new() { Id = occupancy, AccountId = account, UnitId = unit, TenantOrganizationId = org, ValidFrom = new(2020, 1, 1) });
            db.ServiceDefinitions.Add(new() { Id = definition, AccountId = account, Code = "REQUEST", IsActive = true, IsBookableByTenant = true });
            db.ServiceDefinitionProviders.Add(new() { Id = Guid.NewGuid(), AccountId = account, ServiceDefinitionId = definition, ServiceProviderOrganizationId = providerOrg, IsActive = true, IsDefault = false, IntegrationType = ServiceProviderIntegrationType.Email, RequestEmailAddress = "supplier@example.test" });
            db.ServiceRequests.Add(new() { Id = id, AccountId = account, ServiceDefinitionId = definition, RequesterUserId = tenant, RequesterOrganizationId = org, BuildingId = building, UnitId = unit, ReplyToken = Guid.NewGuid().ToString("N"), Status = ServiceRequestStatus.Submitted, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var actor = new Actor(new(true, tenant, account));
        var service = new ServiceRequestService(new AuthorizationService(actor, new AuthorizationData(factory), TimeProvider.System), new ServiceRequestData(factory), new Addresses(), TimeProvider.System);
        guard.Protect = true;
        try
        {
            Check((await service.GetMyRequestsAsync(account, "en-GB")).Single().Id == id, "trusted requester list");
            Check(await service.GetRequestAsync(account, id, "en-GB") is not null, "tenant reads own request");
            Check((await service.GetAdminRequestsAsync(account, "en-GB", new())).Requests.Count == 0, "tenant gets no administrative metadata");
            await Denied(() => service.GetAvailableProvidersAsync(account, id));
            await Denied(() => service.ComposeProviderRequestAsync(account, id));
            actor.Current = new(true, outsider, account);
            Check(await service.GetRequestAsync(account, id, "en-GB") is null, "global flag does not bypass account resource authority");
            actor.Current = new(true, admin, account);
            Check((await service.GetAdminRequestsAsync(account, "en-GB", new())).Requests.Single().Id == id, "administrative scope and projection");
            Check((await service.GetAvailableProvidersAsync(account, id)).Count == 1, "provider selector requires resource authority");
            Check((await service.ComposeProviderRequestAsync(account, id)).Body.Contains("REQUEST"), "authorized composition is write free");
            await Denied(() => service.GetMyRequestsAsync(other, "en-GB"));
        }
        finally { guard.Protect = false; }
        actor.Current = new(true, tenant, account);
        await Denied(() => service.ApproveRequestAsync(account, id));
        await Denied(() => service.QueueProviderEmailAsync(account, id));
        if (provider == "PostgreSQL")
        {
            var created = await service.CreateServiceRequestAsync(account, new() { ServiceDefinitionId = definition, OccupancyId = occupancy });
            await using (var db = factory.CreateDbContext()) Check((await db.ServiceRequests.SingleAsync(x => x.Id == created)).RequesterUserId == tenant, "create uses trusted identity");
            await service.AddCommentAsync(account, id, "  comment  ");
            actor.Current = new(true, admin, account);
            await service.ApproveRequestAsync(account, id);
            try { await service.AssignProviderAsync(account, id, Guid.NewGuid()); throw new Exception("Expected invalid provider"); }
            catch (ServiceRequestValidationException) { }
            await service.AssignProviderAsync(account, id, providerOrg);
            await Task.WhenAll(service.QueueProviderEmailAsync(account, id), service.QueueProviderEmailAsync(account, id));
            await using (var db = factory.CreateDbContext()) Check(await db.EmailOutboxMessages.CountAsync(x => x.ServiceRequestId == id) == 1, "concurrent email ordering is idempotent");
            actor.Current = new(true, supplier, account);
            Check((await service.GetAssignedRequestsAsync(account, "en-GB")).Single().Id == id, "assigned provider list");
            await Denied(() => service.ReplyToEmailAsync(account, id, Guid.NewGuid(), "reply"));
            await service.StartWorkAsync(account, id);
            await service.CompleteRequestAsync(account, id);
            await using (var db = factory.CreateDbContext()) Check((await db.ServiceRequests.SingleAsync(x => x.Id == id)).Status == ServiceRequestStatus.Completed, "provider transitions retain completed timestamp and history");
            var message = Guid.NewGuid();
            await using (var db = factory.CreateDbContext())
            {
                db.ServiceRequestMessages.Add(new() { Id = message, ServiceRequestId = created, Type = ServiceRequestMessageType.Email, Direction = ServiceRequestMessageDirection.Inbound, FromAddress = "supplier@example.test", Subject = "Question", Body = "Please reply", ExternalMessageId = "<inbound@example.test>", CreatedAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync();
            }
            actor.Current = new(true, tenant, account);
            await service.ReplyToEmailAsync(account, created, message, "Answer");
            await using (var db = factory.CreateDbContext()) Check((await db.EmailOutboxMessages.SingleAsync(x => x.ServiceRequestId == created)).InReplyToMessageId == "<inbound@example.test>", "requester reply preserves threading without sending");
        }
        // A PropertyAdmin without a building must not turn an area entry into resource authority.
        await using (var db = factory.CreateDbContext())
        {
            var membership = await db.UserAccounts.SingleAsync(x => x.AccountId == account && x.UserId == outsider);
            db.UserAccountRoles.Add(new() { Id = Guid.NewGuid(), UserAccountId = membership.Id, Role = UserRole.PropertyAdmin });
            await db.SaveChangesAsync();
        }
        actor.Current = new(true, outsider, account);
        Check((await service.GetAdminRequestsAsync(account, "en-GB", new())).Requests.Count == 0, "unscoped PropertyAdmin has no administrative resource scope");
        await Denied(() => service.GetAvailableProvidersAsync(account, id));
        await using (var db = factory.CreateDbContext())
        {
            db.UserAccountRoles.RemoveRange(await db.UserAccountRoles.Where(x => x.UserAccountId == tenantMembership).ToListAsync());
            db.UserAccounts.Remove(await db.UserAccounts.SingleAsync(x => x.Id == tenantMembership)); await db.SaveChangesAsync();
        }
        actor.Current = new(true, tenant, account);
        await Denied(() => service.GetMyRequestsAsync(account, "en-GB"));
        await Denied(() => service.AddCommentAsync(account, id, "revoked"));
    }
    private sealed class Addresses : IServiceRequestEmailAddressService { public string GetReplyAddress(string token) => "reply+" + token + "@example.test"; }
}
