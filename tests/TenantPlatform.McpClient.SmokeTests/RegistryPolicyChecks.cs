using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Mcp;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.Persistence.OAuth;
using TenantPlatform.Web.Security;

static partial class RegistryChecks
{
    private static async Task RunPolicies(WebApplication web, Factory factory, Guid admin, Guid foreign, Guid member,
        Guid account, Guid otherAccount, Guid internalConnection, Guid connection, Guid otherConnection, AdjustableClock clock, Func<int> calls, Browser administratorBrowser, Browser memberBrowser)
    {
        var dataOptions = web.Services.GetRequiredService<Microsoft.EntityFrameworkCore.DbContextOptions<OAuthDbContext>>();
        var beforeCatalogue = calls();
        var initial = await As(member, s => s.AvailableToolsAsync());
        Check(calls() == beforeCatalogue, "catalogue viewing does not invoke remote tools");
        Check(await Denied(member, s => s.CallAsync(Guid.Empty, "accounts_list", "{}")), "reserved legacy connection cannot bypass registry policy");
        Check(initial.Count == 1 && initial[0].ConnectionId == connection && initial[0].Version.Length > 0,
            "direct consumer catalogue contains only personally authorized approved tools");
        var encoded = JsonSerializer.Serialize(initial);
        Check(!encoded.Contains("AccessToken", StringComparison.OrdinalIgnoreCase) && !encoded.Contains("AccessGrants", StringComparison.OrdinalIgnoreCase), "consumer contract excludes credentials and access-grant membership");
        Check(!(await As(member, s => s.ToolsAsync(internalConnection))).Any(), "no metadata without personal authorization");
        Check(await Denied(foreign, s => s.ToolsAsync(connection), otherAccount), "foreign account cannot read tool metadata");
        var both = new[] { new McpAccessGrant(UserId: admin), new McpAccessGrant(UserId: member) };
        await Audience(McpConnectionAudience.Selected, both);
        await Tool(McpToolAudience.Closed, [], McpToolUse.AutomaticRead);
        var count = calls();
        Check(!(await As(member, s => s.AvailableToolsAsync())).Any() && await Denied(member, s => s.CallAsync(connection, "accounts_list", "{}")) && calls() == count,
            "connection access alone does not grant tool access");
        var hidden = await (await memberBrowser.Get("/mcp-client/registry")).Content.ReadAsStringAsync();
        Check(!hidden.Contains("accounts_list", StringComparison.Ordinal), "SSR does not expose denied tool names or schemas");
        await Audience(McpConnectionAudience.Selected, [new(UserId: admin)]);
        await Tool(McpToolAudience.Selected, [new(UserId: member)], McpToolUse.AutomaticRead);
        Check(!(await As(member, s => s.ListAsync())).Servers.Any(x => x.Id == connection) &&
            !(await As(member, s => s.AvailableToolsAsync())).Any() && await Denied(member, s => s.CallAsync(connection, "accounts_list", "{}")),
            "tool grant cannot expand connection audience or expose its metadata");
        var adminPage = await (await administratorBrowser.Get("/mcp-client/registry")).Content.ReadAsStringAsync();
        var form = Fields(adminPage);
        Check((await administratorBrowser.Post("/mcp-client/registry/connection-access", new() {
            ["__RequestVerificationToken"] = form["__RequestVerificationToken"], ["id"] = connection.ToString(),
            ["revision"] = (await Row()).Revision.ToString(), ["audience"] = "2", ["grant"] = JsonSerializer.Serialize(new McpAccessGrant(UserId: member))
        })).StatusCode == System.Net.HttpStatusCode.Redirect, "GUI form persists an explicit local user audience through Application");

        Check((await As(admin, s => s.ListAsync())).CanManage && await Denied(admin, s => s.CallAsync(connection, "accounts_list", "{}")),
            "management permission does not grant connection or tool use");
        Check((await As(member, s => s.CallAsync(connection, "accounts_list", "{}"))).Contains("synthetic"), "selected user can call using only their own external identity");
        await Audience(McpConnectionAudience.Selected, both);
        await Tool(McpToolAudience.Selected, [new(UserId: member)], McpToolUse.ConfirmationRequired);
        var confirm = (await As(member, s => s.AvailableToolsAsync())).Single(); count = calls();
        Check(confirm.UseClass == McpToolUse.ConfirmationRequired && await Denied(member, s => s.CallAsync(connection, confirm.Name, "{}"), code: "CONFIRMATION_REQUIRED") && calls() == count,
            "confirmation classification is visible but automatic execution is refused");
        await Tool(McpToolAudience.Inherit, [], McpToolUse.AutomaticRead);
        var cached = (await As(member, s => s.AvailableToolsAsync())).Single();
        Check((await As(member, s => s.CallAsync(connection, cached.Name, cached.Version, "{}"))).Contains("synthetic") &&
            await Denied(member, s => s.CallAsync(connection, cached.Name, "old-metadata-version", "{}")), "consumer calls bind to the approved metadata version returned by the catalogue");
        await As(admin, async s => { await s.ApproveAsync(connection, cached.Name, cached.Version, false); return true; });
        Check(await Denied(member, s => s.CallAsync(connection, cached.Name, "{}")) && !(await As(member, s => s.AvailableToolsAsync())).Any(),
            "cached catalogue cannot authorize a call after approval revocation");
        await As(admin, async s => { await s.ApproveAsync(connection, cached.Name, cached.Version, true); return true; });
        await Audience(McpConnectionAudience.Selected, [new(UserId: admin)]);
        Check(await Denied(member, s => s.CallAsync(connection, cached.Name, "{}")), "connection grant revocation stops next call after catalogue read");
        await Audience(McpConnectionAudience.Selected, both);
        await Tool(McpToolAudience.Selected, [new(UserId: admin)], McpToolUse.AutomaticRead);
        Check(await Denied(member, s => s.CallAsync(connection, cached.Name, "{}")), "narrowed tool audience stops next call without changing connection grant");
        await Tool(McpToolAudience.Inherit, [], McpToolUse.AutomaticRead);
        Check(!(await As(member, s => s.AvailableToolsAsync())).Any(t => t.ConnectionId == otherConnection), "credentials cannot be reused for another definition of the same server");
        // Real account role facts: role A/scope A plus role B/scope B must not produce role A/scope B.
        var organization = Guid.NewGuid(); Guid originalRole; Guid originalOrg;
        var extraMemberRole = Guid.NewGuid(); var otherUserRole = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            originalOrg = await db.Organizations.Where(x => x.AccountId == account).Select(x => x.Id).FirstAsync();
            db.Organizations.Add(new Organization { Id = organization, AccountId = account, Name = "Policy scope B" });
            var role = await db.UserAccountRoles.SingleAsync(x => x.UserAccount.UserId == member); originalRole = role.Id; role.OrganizationId = originalOrg;
            db.UserAccountRoles.Add(new UserAccountRole { Id = extraMemberRole, UserAccountId = role.UserAccountId, Role = UserRole.PropertyAdmin, OrganizationId = organization });
            db.UserAccountRoles.Add(new UserAccountRole { Id = otherUserRole, UserAccountId = await db.UserAccounts.Where(x => x.UserId == admin).Select(x => x.Id).SingleAsync(), Role = UserRole.TenantUser, OrganizationId = organization });
            await db.SaveChangesAsync();
        }
        try
        {
            await Audience(McpConnectionAudience.Selected, [new(Role: UserRole.TenantUser, OrganizationId: organization)]);
            Check(await Denied(member, s => s.CallAsync(connection, "accounts_list", "{}")), "a role's use permission cannot borrow another role assignment's scope");
            await Audience(McpConnectionAudience.Selected, [new(Role: UserRole.PropertyAdmin, OrganizationId: organization)]);
            Check((await As(member, s => s.AvailableToolsAsync())).Any(t => t.ConnectionId == connection), "one matching full role assignment grants connection access");
            await using (var db = factory.CreateDbContext()) { db.Remove(await db.UserAccountRoles.SingleAsync(x => x.Id == extraMemberRole)); await db.SaveChangesAsync(); }
            Check(await Denied(member, s => s.CallAsync(connection, "accounts_list", "{}")) && !(await As(member, s => s.AvailableToolsAsync())).Any(), "role assignment removal takes effect after a prior catalogue result");
        }
        finally
        {
            await using var db = factory.CreateDbContext();
            var role = await db.UserAccountRoles.SingleAsync(x => x.Id == originalRole); role.OrganizationId = null;
            db.RemoveRange(await db.UserAccountRoles.Where(x => x.Id == extraMemberRole || x.Id == otherUserRole).ToArrayAsync());
            db.Remove(await db.Organizations.SingleAsync(x => x.Id == organization)); await db.SaveChangesAsync();
        }
        await Audience(McpConnectionAudience.AccountRoles, []);
        UserAccount savedMembership; UserAccountRole[] savedRoles;
        await using (var db = factory.CreateDbContext())
        {
            savedMembership = await db.UserAccounts.AsNoTracking().SingleAsync(x => x.UserId == member && x.AccountId == account);
            savedRoles = await db.UserAccountRoles.AsNoTracking().Where(x => x.UserAccountId == savedMembership.Id).ToArrayAsync();
            db.RemoveRange(savedRoles); db.Remove(savedMembership); await db.SaveChangesAsync();
        }
        try { Check(await Denied(member, s => s.AvailableToolsAsync()) && await Denied(member, s => s.CallAsync(connection, "accounts_list", "{}")), "membership revocation blocks both direct catalogue reads and execution"); }
        finally { await using var db = factory.CreateDbContext(); db.Add(savedMembership); db.AddRange(savedRoles); await db.SaveChangesAsync(); }
        Check(await Denied(admin, async s => { await s.SetConnectionAccessAsync(connection, (await Row()).Revision, McpConnectionAudience.Selected, [new(UserId: foreign)]); return true; }), "foreign user reference cannot be assigned access");
        Check(await Denied(admin, async s => { await s.SetConnectionAccessAsync(connection, (await Row()).Revision, (McpConnectionAudience)999, []); return true; }), "unknown policy cannot be saved");
        await using (var db = new OAuthDbContext(dataOptions)) { await db.Set<McpServerDefinition>().Where(x => x.Id == connection).ExecuteUpdateAsync(s => s.SetProperty(x => x.Audience, (McpConnectionAudience)999)); }
        Check(!(await As(member, s => s.AvailableToolsAsync())).Any() && await Denied(member, s => s.CallAsync(connection, "accounts_list", "{}")), "unknown stored connection policy fails closed");
        await Audience(McpConnectionAudience.AccountRoles, []);
        await using (var db = new OAuthDbContext(dataOptions)) { await db.Set<McpDiscoveredTool>().Where(x => x.ConnectionId == connection).ExecuteUpdateAsync(s => s.SetProperty(x => x.AccessGrants, "not-json")); }
        Check(!(await As(member, s => s.AvailableToolsAsync())).Any() && await Denied(member, s => s.CallAsync(connection, "accounts_list", "{}")), "missing or malformed tool policy fails closed");
        await Tool(McpToolAudience.Inherit, [], McpToolUse.AutomaticRead);
        string originalPayload;
        await using (var db = new OAuthDbContext(dataOptions))
        {
            var credential = await db.Set<McpClientCredential>().SingleAsync(x => x.UserId == member && x.ConnectionId == connection);
            originalPayload = credential.ProtectedTokens!; credential.ProtectedTokens = "unreadable-credential"; await db.SaveChangesAsync();
        }
        Check(!(await As(member, s => s.AvailableToolsAsync())).Any(), "unreadable personal credentials cannot expose a usable tool catalogue");
        await using (var db = new OAuthDbContext(dataOptions))
        {
            var credential = await db.Set<McpClientCredential>().SingleAsync(x => x.UserId == member && x.ConnectionId == connection);
            Check(credential.ProtectedTokens == "unreadable-credential", "catalogue/status does not mutate unreadable credentials or refresh them");
            credential.ProtectedTokens = originalPayload; await db.SaveChangesAsync();
        }
        var original = await Row();
        await As(admin, async s => { await s.SaveAsync(connection, original.Revision, original.Name, original.ProfileId, false); return true; });
        Check(!(await As(member, s => s.AvailableToolsAsync())).Any(), "disabled definition is excluded from consumer catalogue");
        var disabled = await Row(); await As(admin, async s => { await s.SaveAsync(connection, disabled.Revision, disabled.Name, disabled.ProfileId, true); return true; });
        await using (var db = new OAuthDbContext(dataOptions)) { await db.Set<McpServerDefinition>().Where(x => x.Id == connection).ExecuteUpdateAsync(s => s.SetProperty(x => x.ProfileVersion, "changed-profile")); }
        Check(!(await As(member, s => s.AvailableToolsAsync())).Any(), "profile changes exclude a previously available tool");
        await using (var db = new OAuthDbContext(dataOptions)) { await db.Set<McpServerDefinition>().Where(x => x.Id == connection).ExecuteUpdateAsync(s => s.SetProperty(x => x.ProfileVersion, original.ProfileVersion).SetProperty(x => x.DiscoveredUtc, clock.GetUtcNow().AddHours(-25))); }
        Check(!(await As(member, s => s.AvailableToolsAsync())).Any(), "stale catalogue exposes no tool metadata to consumers");
        await using (var db = new OAuthDbContext(dataOptions)) { await db.Set<McpServerDefinition>().Where(x => x.Id == connection).ExecuteUpdateAsync(s => s.SetProperty(x => x.DiscoveredUtc, original.DiscoveredUtc)); }
        Check((await As(member, s => s.AvailableToolsAsync())).Count == 1, "policy tests leave the established regression flow usable");

        async Task<McpServerDefinition> Row() { await using var db = new OAuthDbContext(dataOptions); return await db.Set<McpServerDefinition>().AsNoTracking().SingleAsync(x => x.Id == connection); }
        async Task Audience(McpConnectionAudience policy, IReadOnlyList<McpAccessGrant> grants) =>
            await As(admin, async s => { await s.SetConnectionAccessAsync(connection, (await Row()).Revision, policy, grants); return true; });
        async Task Tool(McpToolAudience policy, IReadOnlyList<McpAccessGrant> grants, McpToolUse use) =>
            await As(admin, async s => { await s.SetToolPolicyAsync(connection, "accounts_list", (await As(admin, x => x.ToolsAsync(connection))).Single().Version, policy, grants, use); return true; });
        async Task<bool> Denied<T>(Guid user, Func<McpRegistryService, Task<T>> action, Guid? selected = null, string? code = null)
        {
            try { await As(user, action, selected); return false; }
            catch (UnauthorizedAccessException) { return code is null; }
            catch (McpClientException e) { return code is null || code == e.Code; }
        }
        async Task<T> As<T>(Guid user, Func<McpRegistryService, Task<T>> action, Guid? selected = null)
        {
            await using var scope = web.Services.CreateAsyncScope(); var sp = scope.ServiceProvider;
            var accessor = sp.GetRequiredService<IHttpContextAccessor>(); var previous = accessor.HttpContext;
            accessor.HttpContext = new DefaultHttpContext { RequestServices = sp, User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(TenantPlatformClaimTypes.UserId, user.ToString()), new Claim(TenantPlatformClaimTypes.CurrentAccountId, (selected ?? account).ToString()) }, "fixture-trusted-operation")) };
            try
            {
                var actor = new FixedActor(new(true, user, selected ?? account));
                var auth = new TenantPlatform.Application.Security.AuthorizationService(actor, new AuthorizationData(factory), clock);
                var service = new McpRegistryService(actor, auth, sp.GetRequiredService<IMcpRegistryData>(), sp.GetRequiredService<IMcpAuthProfiles>(),
                    sp.GetRequiredService<IMcpRegisteredClientFactory>(), clock, sp.GetRequiredService<IMcpAccessDirectory>());
                return await action(service);
            }
            finally { accessor.HttpContext = previous; }
        }
    }
}
