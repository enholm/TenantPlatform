using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Occupancies;
using TenantPlatform.Core.Properties;
using TenantPlatform.Core.Services;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;

static class AuthorizationChecks
{
    public static async Task Run(Factory factory, Guid account, Guid foreignAccount, Guid user, Guid memberId,
        Guid admin, Guid platform, Guid organization, Guid own, Guid read, Guid edit, Guid hidden, Guid foreign, Guid archived, string provider)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL authorization: " + name); Console.WriteLine($"PASS {provider} authorization: {name}"); }
        var actor = new Actor(new(true, user, account));
        var auth = new AuthorizationService(actor, new AuthorizationData(factory), TimeProvider.System);
        Task<bool> Can(string p, ResourceKind k = ResourceKind.Account, Guid? id = null, Guid? selected = null) => auth.CanAsync(p, new(k, id, selected));
        foreach (var role in Enum.GetValues<UserRole>())
        {
            var rights = LegacyRolePermissions.For(role);
            Check(rights.All(Permissions.All.Contains) && rights.Distinct().Count() == rights.Count, "catalog mapping valid for " + role);
            Check(!rights.Any(x => x.StartsWith("platform.")), "account role cannot grant platform rights: " + role);
        }
        Check(Permissions.All.Distinct().Count() == Permissions.All.Count, "unique stable permission IDs");
        Check(LegacyRolePermissions.For((UserRole)999).Count == 0, "unknown role denies");
        var specialists = new Dictionary<UserRole, string>
        {
            [UserRole.LeasingOrderApprover] = Permissions.Leasing.ApproveOrders,
            [UserRole.LeasingLimitApprover] = Permissions.Leasing.ApproveLimit,
            [UserRole.LeasingPlanApprover] = Permissions.Leasing.ActivatePlans,
            [UserRole.LeasingInvoiceApprover] = Permissions.Leasing.ApproveInvoices,
            [UserRole.LeasingVarianceApprover] = Permissions.Leasing.AcceptVariances,
            [UserRole.LeasingEquipmentManager] = Permissions.Leasing.MaintainEquipment,
            [UserRole.LeasingLifecycleManager] = Permissions.Leasing.ManageLifecycle,
            [UserRole.LeasingLifecycleApprover] = Permissions.Leasing.ApproveLifecycle,
            [UserRole.LeasingReportReader] = Permissions.Leasing.ReadReports,
            [UserRole.LeasingReportExporter] = Permissions.Leasing.ExportReports
        };
        foreach (var (role, permission) in specialists)
            Check(LegacyRolePermissions.For(role).Order().SequenceEqual(new[] { Permissions.Leasing.Open, permission, Permissions.McpConnections.Use }.Order()), "specialized leasing role has exactly its action: " + role);
        Check(await Can(Permissions.Agreements.Read, ResourceKind.Agreement, own) && await Can(Permissions.Agreements.Update, ResourceKind.Agreement, own) && await Can(Permissions.Agreements.ManageSharing, ResourceKind.Agreement, own), "owner reads, edits and manages sharing");
        Check(await Can(Permissions.Agreements.Read, ResourceKind.Agreement, read) && !await Can(Permissions.Agreements.Update, ResourceKind.Agreement, read) && !await Can(Permissions.Agreements.ManageSharing, ResourceKind.Agreement, read), "Read grant only reads");
        Check(await Can(Permissions.Agreements.Update, ResourceKind.Agreement, edit) && !await Can(Permissions.Agreements.ManageSharing, ResourceKind.Agreement, edit), "Edit grant cannot manage sharing");
        Check(!await Can(Permissions.Agreements.Update, ResourceKind.Agreement, archived), "archived agreement cannot edit");
        Check(!await Can(Permissions.Agreements.Read, ResourceKind.Agreement, hidden) && !await Can(Permissions.Agreements.Read, ResourceKind.Agreement, foreign), "unrelated and cross-account agreements denied");
        Check(!await Can(Permissions.Leasing.Open) && !await Can(Permissions.Buildings.Create), "agreement relationship grants no other module rights");
        Check(!await Can("missing.permission") && !await Can(Permissions.Agreements.Read, ResourceKind.Building, own) && !await Can(Permissions.Agreements.Open, selected: foreignAccount), "unknown permission, wrong resource kind and supplied account denied");
        actor.Current = new(false, user, account); Check(!await Can(Permissions.Agreements.Open), "unauthenticated denied");
        actor.Current = new(true, user, null); Check(!await Can(Permissions.Agreements.Open), "missing selected account denied");
        actor.Current = new(true, Guid.NewGuid(), account); Check(!await Can(Permissions.Agreements.Open), "missing user denied");
        actor.Current = new(true, user, foreignAccount); Check(!await Can(Permissions.MeetingRooms.Open), "missing membership denied");
        actor.Current = new(true, platform, null);
        Check(await Can(Permissions.Platform.OpenAccounts, ResourceKind.Platform) && await Can(Permissions.Platform.ReadAccount, ResourceKind.PlatformAccount, foreignAccount), "global administrator operates without selected account or membership");
        actor.Current = new(true, platform, account); Check(!await Can(Permissions.Buildings.Create) && !await Can(Permissions.Agreements.Read, ResourceKind.Agreement, hidden), "global administrator does not become account administrator");
        actor.Current = new(true, admin, account); Check(!await Can(Permissions.Platform.OpenAccounts, ResourceKind.Platform), "account admin cannot administer platform");
        Check(await Can(Permissions.Agreements.Read, ResourceKind.Agreement, hidden) && !await Can(Permissions.Agreements.Read, ResourceKind.Agreement, foreign), "account admin bounded by account");
        actor.Current = new(true, user, account);
        var b1 = Guid.NewGuid(); var b2 = Guid.NewGuid(); var bf = Guid.NewGuid(); var unit = Guid.NewGuid();
        var secondOrganization = Guid.NewGuid(); var definition = Guid.NewGuid(); var request = Guid.NewGuid(); var occupancy = Guid.NewGuid();
        var propertyRole = new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = memberId, Role = UserRole.PropertyAdmin, BuildingId = b1 };
        var tenantRole = new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = memberId, Role = UserRole.TenantAdmin, OrganizationId = organization };
        var leasingRole = new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = memberId, Role = UserRole.LeasingInvoiceApprover };
        await using (var db = factory.CreateDbContext())
        {
            db.Buildings.AddRange(new Building { Id = b1, AccountId = account, Name = "Scoped" }, new Building { Id = b2, AccountId = account, Name = "Outside scope" }, new Building { Id = bf, AccountId = foreignAccount, Name = "Foreign" });
            db.Units.Add(new Unit { Id = unit, AccountId = account, BuildingId = b1, Name = "Scoped unit" });
            db.Occupancies.Add(new Occupancy { Id = occupancy, AccountId = account, UnitId = unit, TenantOrganizationId = organization, ValidFrom = new(2020, 1, 1) });
            db.Organizations.Add(new Organization { Id = secondOrganization, AccountId = account, Name = "Other organization" });
            db.ServiceDefinitions.Add(new ServiceDefinition { Id = definition, AccountId = account, Code = "AUTHORIZATION" });
            db.ServiceRequests.Add(new ServiceRequest { Id = request, AccountId = account, BuildingId = b1, RequesterUserId = user, RequesterOrganizationId = organization, ServiceDefinitionId = definition });
            db.UserAccountRoles.AddRange(propertyRole, tenantRole, leasingRole);
            await db.SaveChangesAsync();
        }
        Check(await Can(Permissions.Buildings.Update, ResourceKind.Building, b1) && !await Can(Permissions.Buildings.Update, ResourceKind.Building, b2) && !await Can(Permissions.Buildings.Update, ResourceKind.Building, bf), "building role retains exact building and account scope");
        Check(await Can(Permissions.Organizations.Update, ResourceKind.Organization, organization) && !await Can(Permissions.Buildings.Create), "role union adds tenant organization rights without account create rights");
        Check(await Can(Permissions.Units.Update, ResourceKind.Unit, unit) && await Can(Permissions.Occupancies.Create, ResourceKind.Unit, unit), "unit and occupancy resolve building scope");
        Check(await Can(Permissions.Leasing.ApproveInvoices) && !await Can(Permissions.Leasing.ApproveOrders) && !await Can(Permissions.Leasing.ApproveLifecycle), "leasing union retains action separation");
        Check(await Can(Permissions.Occupancies.Update, ResourceKind.Occupancy, occupancy) && await Can(Permissions.ServiceRequests.Approve, ResourceKind.ServiceRequest, request), "occupancy and request facts resolve account and building");
        Check(await Can(Permissions.ServiceDefinitions.Read, ResourceKind.ServiceDefinition, definition) && !await Can(Permissions.ServiceDefinitions.Update, ResourceKind.ServiceDefinition, definition), "service catalog reading does not allow modification");
        Check(await Can(Permissions.Organizations.Read, ResourceKind.Organization, secondOrganization) && !await Can(Permissions.Organizations.Update, ResourceKind.Organization, secondOrganization), "legacy PropertyAdmin broad organization read isolated from TenantAdmin write scope");
        var scopedAdmin = new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = memberId, Role = UserRole.AccountAdmin, BuildingId = b1 };
        await using (var db = factory.CreateDbContext()) { db.UserAccountRoles.Add(scopedAdmin); await db.SaveChangesAsync(); }
        Check(!(await auth.GetAgreementListScopeAsync(account)).CanReadAll && !await Can(Permissions.Agreements.Create) && !await Can(Permissions.Buildings.Update, ResourceKind.Building, b2), "scoped administrator never becomes account-wide or borrows another assignment's scope");
        await using (var db = factory.CreateDbContext()) { db.UserAccountRoles.Remove(scopedAdmin); await db.SaveChangesAsync(); }
        var facade = new TenantAuthorizationService(factory, new WebContext(user, account), auth);
        Check(await facade.CanManageBuildingsAsync() && !await facade.CanEditBuildingAsync(b2) && await facade.CanEditBuildingAsync(b1), "Web facade preserves broad legacy entry with scoped detail actions");
        Check(await facade.CanViewOrganizationAsync(organization) && !await facade.CanManageOrganizationsAsync(), "legacy broad organization read does not imply management");
        Check(await facade.CanApproveLeasingInvoicesAsync() && !await facade.CanApproveLeasingOrdersAsync(), "Web facade leasing action mapping");
        await using (var db = factory.CreateDbContext()) { db.UserAccountRoles.Remove(propertyRole); await db.SaveChangesAsync(); }
        Check(!await Can(Permissions.Buildings.Update, ResourceKind.Building, b1) && await Can(Permissions.Buildings.Read, ResourceKind.Building, b1) && !await Can(Permissions.Buildings.Read, ResourceKind.Building, b2), "next call observes removed role; active tenancy still scoped");
        await using (var db = factory.CreateDbContext())
        {
            var u = await db.Users.SingleAsync(x => x.Id == user); u.IsActive = false; await db.SaveChangesAsync();
        }
        Check(!await Can(Permissions.Agreements.Read, ResourceKind.Agreement, own) && !await facade.CanApproveLeasingInvoicesAsync(), "inactive user denied despite grants and roles");
        await using (var db = factory.CreateDbContext())
        {
            var u = await db.Users.SingleAsync(x => x.Id == user); u.IsActive = true;
            db.UserAccountRoles.RemoveRange(tenantRole, leasingRole); await db.SaveChangesAsync();
        }
        Check(!await Can(Permissions.Leasing.ApproveInvoices), "same evaluator observes removed leasing role");
        await using (var db = factory.CreateDbContext())
        {
            db.AgreementAccess.Remove(await db.AgreementAccess.SingleAsync(x => x.AccountId == account && x.AgreementId == edit && x.UserId == user)); await db.SaveChangesAsync();
        }
        Check(!await Can(Permissions.Agreements.Read, ResourceKind.Agreement, edit) && !await Can(Permissions.Agreements.Update, ResourceKind.Agreement, edit), "same evaluator observes grant revocation at next call");
        await using (var db = factory.CreateDbContext())
        {
            db.AgreementAccess.Add(new AgreementAccess { Id = Guid.NewGuid(), AccountId = account, AgreementId = edit, UserId = user, Level = AgreementAccessLevel.Edit });
            db.UserAccounts.Remove(await db.UserAccounts.SingleAsync(x => x.Id == memberId)); await db.SaveChangesAsync();
        }
        Check(!await Can(Permissions.Agreements.Read, ResourceKind.Agreement, own) && !await facade.CanUseMeetingRoomsAsync(), "same evaluator and facade observe membership revocation");
        await using (var db = factory.CreateDbContext())
        {
            db.UserAccounts.Add(new UserAccount { Id = memberId, AccountId = account, UserId = user });
            (await db.Users.SingleAsync(x => x.Id == platform)).IsPlatformAdmin = false; await db.SaveChangesAsync();
        }
        actor.Current = new(true, platform, null);
        Check(!await Can(Permissions.Platform.OpenAccounts, ResourceKind.Platform), "global administrator flag is revalidated from database");
        await using (var db = factory.CreateDbContext()) { (await db.Users.SingleAsync(x => x.Id == platform)).IsPlatformAdmin = true; await db.SaveChangesAsync(); }
        // Restore fixtures for the existing list regression checks.

    }
}
