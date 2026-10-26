using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Http;
using Npgsql;
using OpenIddict.Abstractions;
using OpenIddict.Client;
using OpenIddict.Client.SystemNetHttp;
using TenantPlatform.Application.Mcp;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Authentication;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Infrastructure.Persistence.OAuth;
using TenantPlatform.Mcp;
using TenantPlatform.Web.Security.McpClient;
using TenantPlatform.Web.Security.OAuth;

await AgentProtocolFixture.CheckReportSchemaAsync();
if (args.SequenceEqual(new[] { "--model-protocol-only" })) return; // Test-only: never constructs a host or reads application configuration.
var connection=Environment.GetEnvironmentVariable("MCP_CLIENT_TEST_CONNECTION")??throw new Exception("Use isolated PostgreSQL runner.");
var cs=new NpgsqlConnectionStringBuilder(connection);
if(cs.Database!="tenant_mcp_client_tests" || cs.Host?.StartsWith("/tmp/tenant-agreement-tests.",StringComparison.Ordinal)!=true) throw new Exception("Disposable MCP-client database/socket only.");
var directory=Path.Combine(Path.GetTempPath(),"tenant-mcp-client-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
var password=Convert.ToHexString(RandomNumberGenerator.GetBytes(24)); var secret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var pfxPassword=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
var options=new InternalMcpClientOptions { Enabled=true,Issuer="https://tenant.test/",Resource="https://mcp.test/mcp",Callback="https://tenant.test/mcp-client/callback",ClientId="internal-mcp-fixture",ClientSecret=secret };
var issuerOptions=new OAuthIssuerOptions { Enabled=true,Issuer=options.Issuer,Resource=options.Resource,ConnectionString=connection,
    SigningCertificatePath=Path.Combine(directory,"sign.pfx"),SigningCertificatePassword=pfxPassword,
    EncryptionCertificatePath=Path.Combine(directory,"encrypt.pfx"),EncryptionCertificatePassword=pfxPassword,CookieKeyDirectory=Path.Combine(directory,"keys"),
    Clients=[new() { ClientId=options.ClientId,Secret=secret,DisplayName="Internal fixture",RedirectUris=[options.Callback] }] };
foreach(var path in new[]{issuerOptions.SigningCertificatePath,issuerOptions.EncryptionCertificatePath})
{
    using var rsa=RSA.Create(2048); var request=new CertificateRequest("CN=client test only",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
    using var certificate=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(2));
    await File.WriteAllBytesAsync(path,certificate.Export(X509ContentType.Pfx,pfxPassword));
}
var factory=new Factory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(connection).Options);
var user=Guid.NewGuid(); var other=Guid.NewGuid(); var owner=Guid.NewGuid(); var accountA=Guid.NewGuid();var accountB=Guid.NewGuid();
var agreement=Guid.NewGuid(); var organizationA=Guid.NewGuid(); var organizationB=Guid.NewGuid();
await using(var db=factory.CreateDbContext())
{
    Check(!db.Database.HasPendingModelChanges(),"domain model unchanged by client storage");
    await db.Database.MigrateAsync();
    db.Accounts.AddRange(new Account { Id=accountA,Name="Account A" },new Account { Id=accountB,Name="Account B" });
    db.Organizations.AddRange(new Organization { Id=organizationA,AccountId=accountA,Name="Organization A" },new Organization { Id=organizationB,AccountId=accountB,Name="Organization B" });
    foreach(var id in new[]{user,other,owner})
    {
        var person=new User { Id=id,Email=id+"@example.test",FirstName="Client",LastName="User",IsActive=true };
        var login=new LoginAccount { Id=Guid.NewGuid(),UserId=id,Email=person.Email,CreatedUtc=DateTimeOffset.UtcNow };
        login.PasswordHash=new PasswordService().HashPassword(login,password); db.Users.Add(person);db.LoginAccounts.Add(login);
        db.UserAccounts.Add(new UserAccount { Id=Guid.NewGuid(),UserId=id,AccountId=id==other?accountB:accountA });
    }
    db.Agreements.Add(new Agreement { Id=agreement,AccountId=accountA,CounterpartyOrganizationId=organizationA,OwnerUserId=owner,Title="Delegated agreement",
        Type=AgreementType.Supplier,Status=AgreementStatus.Active,StartDate=new(2025,1,1),CurrentPeriodStartDate=new(2025,1,1),CreatedByUserId=owner,UpdatedByUserId=owner,Revision=Guid.NewGuid(),EndDate=DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20) });
    db.AgreementAccess.Add(new AgreementAccess { Id=Guid.NewGuid(),AccountId=accountA,AgreementId=agreement,UserId=user,Level=AgreementAccessLevel.Read });
    await db.SaveChangesAsync();
}
await using(var admin=new NpgsqlConnection(connection))
{
    await admin.OpenAsync(); await using var command=new NpgsqlCommand("CREATE ROLE internal_mcp_reader LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT; REVOKE CREATE ON SCHEMA public FROM PUBLIC; GRANT CONNECT ON DATABASE tenant_mcp_client_tests TO internal_mcp_reader; GRANT USAGE ON SCHEMA public TO internal_mcp_reader; GRANT SELECT ON accounts,users,user_accounts,user_account_roles,agreements,agreement_access,organizations,agreement_reminder_settings,buildings,units,organization_elements,geographic_areas TO internal_mcp_reader",admin); await command.ExecuteNonQueryAsync();
    await using var requestGrant = new NpgsqlCommand("GRANT SELECT (\"Id\",\"AccountId\",\"RequesterUserId\",\"RequesterOrganizationId\",\"AssignedServiceProviderOrganizationId\",\"BuildingId\",\"UnitId\",\"Title\",\"Status\",\"CreatedAt\",\"SubmittedAt\",\"CompletedAt\") ON public.service_requests TO internal_mcp_reader", admin);
    await requestGrant.ExecuteNonQueryAsync();
    await using var deadlineGrant = new NpgsqlCommand("GRANT SELECT (\"Id\",\"AccountId\",\"AgreementId\",\"Kind\",\"DueDate\",\"Status\",\"State\",\"UpdatedUtc\",\"AssignedUserId\") ON public.agreement_deadlines TO internal_mcp_reader", admin);
    await deadlineGrant.ExecuteNonQueryAsync();
    await using var extensionGrant = new NpgsqlCommand("GRANT SELECT (\"Id\",\"AccountId\",\"Name\") ON public.agreement_indices TO internal_mcp_reader; GRANT SELECT (\"Id\",\"AccountId\",\"DeadlineId\",\"Kind\",\"CreatedUtc\") ON public.agreement_deadline_history TO internal_mcp_reader", admin);
    await extensionGrant.ExecuteNonQueryAsync();
    await using var financeGrant = new NpgsqlCommand(FinanceReadPrivileges.Sql("internal_mcp_reader"), admin); await financeGrant.ExecuteNonQueryAsync();
}
var readConnection=new NpgsqlConnectionStringBuilder(connection) { Username="internal_mcp_reader" }.ConnectionString;
var clock=new AdjustableClock(); var logs=new LogCapture(); var route=new RouteControl();
WebApplication? web=null,mcp=null;
try
{
    web=await StartWeb();
    await using(var scope=web.Services.CreateAsyncScope())
    {
        var db=scope.ServiceProvider.GetRequiredService<OAuthDbContext>();
        await db.GetService<IMigrator>().MigrateAsync("20261017142957_AddInternalMcpCredentials");
        var legacyUser=Guid.NewGuid();var legacyBinding=new McpClientBinding(options.ClientId,options.Issuer,options.Resource);
        var protection=scope.ServiceProvider.GetRequiredService<IMcpClientTokenProtection>();
        var payload=protection.Protect(legacyUser,legacyBinding,new() { AccessToken="migration-fixture-access",RefreshToken="migration-fixture-refresh" });
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO oauth.mcp_client_credentials (\"UserId\",\"ClientId\",\"Issuer\",\"Resource\",\"ProtectedTokens\") VALUES ({legacyUser},{options.ClientId},{options.Issuer},{options.Resource},{payload})");
        await db.GetService<IMigrator>().MigrateAsync("20261017151954_AddMcpConnectionRegistry");
        var legacyDefinition=Guid.NewGuid();var legacyRevision=Guid.NewGuid();var legacyEpoch=Guid.NewGuid();var now=clock.GetUtcNow();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO oauth.mcp_servers (\"Id\",\"AccountId\",\"Name\",\"ProfileId\",\"ProfileVersion\",\"Enabled\",\"Revision\",\"CredentialEpoch\",\"DiscoveryComplete\") VALUES ({legacyDefinition},{accountA},{"Migration fixture"},{"tenantplatform"},{"version"},{true},{legacyRevision},{legacyEpoch},{true})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO oauth.mcp_tools (\"ConnectionId\",\"Name\",\"Version\",\"InputSchema\",\"DiscoveredUtc\",\"Approved\") VALUES ({legacyDefinition},{"approved"},{"version"},{"{}"},{now},{true}),({legacyDefinition},{"unapproved"},{"version"},{"{}"},{now},{false})");
        await db.Database.MigrateAsync();
        var definition=await db.Set<McpServerDefinition>().SingleAsync(x=>x.Id==legacyDefinition);
        var migratedTools=await db.Set<McpDiscoveredTool>().Where(x=>x.ConnectionId==legacyDefinition).ToArrayAsync();
        Check(definition.Audience==McpConnectionAudience.AccountRoles&&definition.AccessGrants=="[]"&&migratedTools.All(x=>x.Audience==McpToolAudience.Inherit&&x.AccessGrants=="[]")&&
            migratedTools.Single(x=>x.Name=="approved").UseClass==McpToolUse.AutomaticRead&&migratedTools.Single(x=>x.Name=="unapproved").UseClass==McpToolUse.Unclassified,
            "6A upgrade explicitly preserves existing audiences and classifies only already approved tools for automatic use");
        Check(!db.Database.HasPendingModelChanges(),"OAuth policy model matches migration snapshot");
        db.RemoveRange(migratedTools);db.Remove(definition);await db.SaveChangesAsync();
        var migrated=await db.Set<McpClientCredential>().SingleAsync(x=>x.UserId==legacyUser);
        Check(migrated.ConnectionId==Guid.Empty&&migrated.ProfileVersion==""&&migrated.ProtectedTokens==payload&&protection.Unprotect(legacyUser,legacyBinding,payload).RefreshToken=="migration-fixture-refresh","upgrade preserves legacy slot and ciphertext without reassigning credentials");
        db.Remove(migrated);await db.SaveChangesAsync();
    }
    await OAuthComposition.RegisterClientsAsync(web.Services,issuerOptions);
    mcp=await StartMcp();
    var a=new Browser(web.GetTestClient()); var b=new Browser(web.GetTestClient());
    await Login(a,user); await Login(b,other);
    await Connect(a); await Connect(b);
    await RegistryChecks.Run(factory,connection,issuerOptions,options,mcp,web,accountA,accountB,password);
    var page=await (await a.Get("/mcp-client")).Content.ReadAsStringAsync();
    Check(page.Contains("Connected")||page.Contains("Tilkoblet"),"connected GUI status");
    var probe=await Probe(a,accountA); Check(probe.Contains("Account A")&&!probe.Contains("Account B"),"real MCP probe with own accounts/Attention");
    var concurrent=await Task.WhenAll(Probe(new Browser(web.GetTestClient(),new(a.Cookies)),accountA),Probe(new Browser(web.GetTestClient(),new(b.Cookies)),accountB));
    Check(concurrent[0].Contains("Account A")&&!concurrent[0].Contains("Account B") && concurrent[1].Contains("Account B")&&!concurrent[1].Contains("Account A"),"simultaneous delegated users/accounts isolated");
    var rows=await Rows(); Check(rows.Count==2 && rows.All(x=>x.ProtectedTokens is { Length:>100 }),"encrypted server-side credential payloads");
    using(var scope=web.Services.CreateScope())
    {
        var protector=scope.ServiceProvider.GetRequiredService<IMcpClientTokenProtection>();var binding=scope.ServiceProvider.GetRequiredService<McpClientBinding>();
        var row=rows.Single(x=>x.UserId==user);var tokens=protector.Unprotect(user,binding,row.ProtectedTokens!);
        Check(!page.Contains(tokens.AccessToken)&&!probe.Contains(tokens.AccessToken)&&!string.Join(";",a.Cookies.Values).Contains(tokens.RefreshToken)&&!row.ProtectedTokens!.Contains(tokens.AccessToken)&&!row.ProtectedTokens.Contains(tokens.RefreshToken),"no access/refresh tokens in GUI or cookies");
        try { protector.Unprotect(other,binding,row.ProtectedTokens!);throw new Exception("cross-user decryption accepted"); } catch(CryptographicException) { Check(true,"encryption bound to local user"); }
    }
    route.FailMcp=true;
    Check(!(await Probe(a,accountA)).Contains("Account A"),"unavailable MCP has no direct Application/data fallback");
    route.FailMcp=false;
    Check((await Probe(a,accountA)).Contains("Account A"),"transient MCP outage preserves delegated connection");
    route.WaitMcp=true;
    using(var abort=new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://tenant.test/fixture/list?accountId="+accountA);
        request.Headers.Add("Cookie",string.Join("; ",a.Cookies.Select(x=>x.Key+"="+x.Value)));
        try { await web.GetTestClient().SendAsync(request,abort.Token);throw new Exception("MCP request did not cancel"); } catch(OperationCanceledException) { }
        await route.McpCancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(true,"HTTP cancellation reaches official MCP HTTP transport");
    }
    route.WaitMcp=false;
    // Dispose/recreate Web process with same persisted key ring, store and cookies.
    await web.DisposeAsync();web=await StartWeb();a=new Browser(web.GetTestClient(),new(a.Cookies));b=new Browser(web.GetTestClient(),new(b.Cookies));
    Check((await Probe(a,accountA)).Contains("Account A"),"restart retains encrypted credentials and GUI session");
    await Disconnect(a); await Connect(a,loginAgain:true);
    Check((await Probe(a,accountA)).Contains("Account A"),"new issuer password login returns to the same initiating user");
    await Disconnect(a); await Connect(a,decision:"deny");
    Check(!(await Rows()).Any(x=>x.UserId==user&&x.ProtectedTokens!=null),"consent denial stores no usable credentials");
    // State and cookie identity bindings.
    var authorize=await Begin(a);var consent=Fields(await (await a.Get(authorize)).Content.ReadAsStringAsync());consent["decision"]="approve";
    var approved=await a.Post("/connect/authorize",consent);var callback=approved.Headers.Location!.OriginalString;
    var callbackUri=new Uri(callback);var values=QueryHelpers.ParseQuery(callbackUri.Query).ToDictionary(x=>x.Key,x=>(string?)x.Value.ToString());values["state"]="invalid";
    var badState=callbackUri.GetLeftPart(UriPartial.Path)+QueryString.Create(values);
    Check((await a.Get("https://tenant.test/mcp-client/not-the-callback"+callbackUri.Query)).StatusCode==HttpStatusCode.NotFound,"unregistered callback path cannot redeem or store connection");
    Check((await a.Get(badState)).StatusCode!=HttpStatusCode.Redirect,"invalid state rejected");
    await Login(a,other); var switched=await a.Get(callback);
    Check(switched.StatusCode!=HttpStatusCode.Redirect && !(await Rows()).Any(x=>x.UserId==user&&x.ProtectedTokens!=null),"GUI user B cannot bind user A grant");
    await Login(a,user);
    var logoutStart=await Begin(a);var logoutFields=Fields(await (await a.Get(logoutStart)).Content.ReadAsStringAsync());logoutFields["decision"]="approve";
    var logoutGrant=await a.Post("/connect/authorize",logoutFields);a.Cookies.Remove("TenantPlatform.Auth");
    Check((await a.Get(logoutGrant.Headers.Location!.OriginalString)).StatusCode==HttpStatusCode.Unauthorized &&
        !(await Rows()).Any(x=>x.UserId==user&&x.ProtectedTokens!=null),"logout during flow cannot attach credentials without a fresh GUI user");
    await Login(a,user);
    var aChallenge=await Begin(a);await Login(a,other);
    var bConsent=Fields(await (await a.Get(aChallenge)).Content.ReadAsStringAsync());bConsent["decision"]="approve";
    var bGrant=await a.Post("/connect/authorize",bConsent);await Login(a,user);
    Check((await a.Get(bGrant.Headers.Location!.OriginalString)).StatusCode!=HttpStatusCode.Redirect &&
        !(await Rows()).Any(x=>x.UserId==user&&x.ProtectedTokens!=null),"GUI user A cannot store user B's signature-validated grant");
    await Connect(a);
    var originalEnd=(await Rows()).Single(x=>x.UserId==user).GrantExpiresUtc;
    // Refresh real rotated token, coordinated over two independent Web service providers.
    await web.DisposeAsync();clock.Advance(TimeSpan.FromMinutes(4.6));web=await StartWeb();a=new Browser(web.GetTestClient(),new(a.Cookies));b=new Browser(web.GetTestClient(),new(b.Cookies));
    var beforeRefresh=route.Refreshes;
    string[] refreshCalls;
    await using(var peer=await StartWeb())
        refreshCalls=await Task.WhenAll(Probe(new Browser(web.GetTestClient(),new(a.Cookies)),accountA),
            Probe(new Browser(peer.GetTestClient(),new(a.Cookies)),accountA),Probe(new Browser(peer.GetTestClient(),new(a.Cookies)),accountA));
    Check(refreshCalls.All(x=>x.Contains("Account A"))&&route.Refreshes==beforeRefresh+1,"independent Web providers consume exactly one rotated refresh token");
    Check((await Rows()).Single(x=>x.UserId==user).GrantExpiresUtc==originalEnd,"refresh preserves original absolute grant end");
    // Revocation at issuer UI stops refresh; existing access remains locally valid until the refresh boundary.
    var management=Fields(await (await a.Get("/oauth/connections")).Content.ReadAsStringAsync());
    management["connection"]=(await Rows()).Single(x=>x.UserId==user).GrantId!;
    Check((await a.Post("/oauth/connections/revoke",management)).StatusCode==HttpStatusCode.Redirect,"issuer UI revokes the client's exact grant");
    await ForceRefresh(user);
    await Probe(a,accountA);
    Check((await Rows()).Single(x=>x.UserId==user).ProtectedTokens is null,"issuer revocation forces reconnect at refresh");
    await Connect(a);
    route.FailRefresh=true;await ForceRefresh(user);var failedRefreshes=route.Refreshes;await Probe(a,accountA);await Probe(a,accountA);
    Check(route.Refreshes==failedRefreshes+1 && (await Rows()).Single(x=>x.UserId==user).ProtectedTokens is null,"ambiguous refresh failure not retried and cannot retain active credentials");route.FailRefresh=false;
    await Connect(a); route.FailRevoke=true;var disconnected=await Disconnect(a);
    Check(!(await Rows()).Any(x=>x.UserId==user) && (disconnected.Contains("could not be confirmed")||disconnected.Contains("ikke bekreftes")),"local disconnect removes credentials despite revocation outage");route.FailRevoke=false;
    await Connect(a);
    await using(var db=factory.CreateDbContext()) { db.AgreementAccess.Remove(await db.AgreementAccess.SingleAsync());await db.SaveChangesAsync(); }
    var list=await a.Get("/fixture/list?accountId="+accountA);var listBody=await list.Content.ReadAsStringAsync();
    Check(list.IsSuccessStatusCode&&JsonDocument.Parse(listBody).RootElement.GetProperty("totalCount").GetInt32()==0,"removed agreement grant honored through MCP");
    await using(var db=factory.CreateDbContext()) { db.UserAccounts.Remove(await db.UserAccounts.SingleAsync(x=>x.UserId==user));await db.SaveChangesAsync(); }
    Check(!(await Probe(a,accountA)).Contains("Account A"),"removed membership denies selected-account MCP data");
    await using(var db=factory.CreateDbContext()) { (await db.Users.SingleAsync(x=>x.Id==user)).IsActive=false;await db.SaveChangesAsync(); }
    Check((await a.Get("/mcp-client")).StatusCode==HttpStatusCode.Unauthorized,"local deactivation blocks stored connection");
    await using(var db=factory.CreateDbContext()) { (await db.Users.SingleAsync(x=>x.Id==user)).IsActive=true;await db.SaveChangesAsync(); }
    var grantEnd=(await Rows()).Single(x=>x.UserId==user).GrantExpiresUtc!.Value;
    clock.Advance(grantEnd-clock.GetUtcNow()-TimeSpan.FromMinutes(1));
    await Probe(a);
    var bounded=(await Rows()).Single(x=>x.UserId==user);
    Check(bounded.GrantExpiresUtc==grantEnd && bounded.AccessExpiresUtc<=grantEnd && bounded.AccessExpiresUtc>clock.GetUtcNow(),"real near-end refresh/access expiry remains bounded by original eight-hour grant");
    clock.Advance(TimeSpan.FromMinutes(2));
    var callsBeforeEnd=route.Refreshes;var requestsBeforeEnd=route.McpRequests;
    Check(!(await Probe(a,accountA)).Contains("Account A") && route.Refreshes==callsBeforeEnd && route.McpRequests==requestsBeforeEnd,"absolute eight-hour end requires reconnect without extending or attempting refresh");
    // New challenge with a cold client verifies issuer outage is safely bounded, without a substitute identity.
    await web.DisposeAsync();web=await StartWeb();a=new Browser(web.GetTestClient(),new(a.Cookies));route.FailIssuer=true;
    var unavailableFields=Fields(await (await a.Get("/mcp-client")).Content.ReadAsStringAsync());
    var unavailable=await a.Post("/mcp-client/connect",new() { ["__RequestVerificationToken"]=unavailableFields["__RequestVerificationToken"] });
    var unavailableBody=await unavailable.Content.ReadAsStringAsync();
    Check(unavailable.StatusCode!=HttpStatusCode.Redirect && !unavailableBody.Contains("Exception") && !unavailableBody.Contains(secret),"issuer outage has safe failure without credential fallback");route.FailIssuer=false;
    foreach(var unavailableOptions in new[] { new InternalMcpClientOptions { Enabled=true }, new InternalMcpClientOptions {
        Enabled=true,Issuer=options.Issuer,Resource=options.Resource,Callback=options.Callback,ClientId=null!,ClientSecret=null! } })
    await using(var disabled=await StartWeb(unavailableOptions))
    {
        var ordinary=new Browser(disabled.GetTestClient());await Login(ordinary,other);
        var unavailableConfig=await (await ordinary.Get("/mcp-client")).Content.ReadAsStringAsync();
        Check(unavailableConfig.Contains("not configured")||unavailableConfig.Contains("ikke konfigurert"),"missing client configuration is unavailable without changing ordinary GUI login");
    }
    Check(!logs.Messages.Any(x=>x.Contains(password)||x.Contains(secret)||route.Tokens.Any(token=>x.Contains(token))),"no access/refresh tokens or credentials in captured application logs");
    Console.WriteLine("Internal MCP client real OAuth/MCP/PostgreSQL checks passed.");

    async Task<WebApplication> StartWeb(InternalMcpClientOptions? alternate = null) => await OAuthTestHost.Start(issuerOptions,factory,clock,logs,builder=>
    {
        builder.Services.AddInternalMcpClient(alternate??options,issuerOptions);
        builder.Services.Configure<OpenIddict.Validation.OpenIddictValidationOptions>(v=>v.TimeProvider=clock);
        builder.Services.ConfigureAll<HttpClientFactoryOptions>(http=>http.HttpMessageHandlerBuilderActions.Add(handler=>
            handler.AdditionalHandlers.Add(new TestRoute(route,()=>web,()=>mcp))));
    },app=>
    {
        app.MapInternalMcpClient();
        app.MapGet("/fixture/list",async(HttpContext http,CancellationToken ct)=>
        {
            try { return Results.Json(await http.RequestServices.GetRequiredService<InternalMcpClientService>().AgreementsAsync(new(Guid.Parse(http.Request.Query["accountId"]!)),ct)); }
            catch(Exception) { return Results.StatusCode(503); }
        }).RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute { AuthenticationSchemes=CookieAuthenticationDefaults.AuthenticationScheme });
    });
    async Task<WebApplication> StartMcp()
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName="Testing",ApplicationName=typeof(McpComposition).Assembly.FullName });builder.WebHost.UseTestServer();builder.Logging.ClearProviders();builder.Logging.AddProvider(logs);
        builder.Services.AddTenantMcp(new McpOptions { Issuer=options.Issuer,Resource=options.Resource,Clients=[options.ClientId],ConnectionString=readConnection });
        builder.Services.Configure<OpenIddict.Validation.OpenIddictValidationOptions>(v=>v.TimeProvider=clock);
        builder.Services.ConfigureAll<HttpClientFactoryOptions>(http=>http.HttpMessageHandlerBuilderActions.Add(handler=>handler.AdditionalHandlers.Add(new TestRoute(route,()=>web,()=>mcp))));
        var app=builder.Build();app.MapTenantMcp();await app.StartAsync();return app;
    }
    async Task Login(Browser browser,Guid id)
    {
        var form=Fields(await (await browser.Get("/login")).Content.ReadAsStringAsync());form["email"]=id+"@example.test";form["password"]=password;
        Check((await browser.Post("/auth/login",form)).StatusCode==HttpStatusCode.Redirect,"existing local password/cookie login");
    }
    async Task<string> Begin(Browser browser)
    {
        var fields=Fields(await (await browser.Get("/mcp-client")).Content.ReadAsStringAsync());
        var response=await browser.Post("/mcp-client/connect",new() { ["__RequestVerificationToken"]=fields["__RequestVerificationToken"] });
        Check(response.StatusCode==HttpStatusCode.Redirect,"OpenIddict client starts code+PKCE challenge");return response.Headers.Location!.OriginalString;
    }
    async Task Connect(Browser browser,string decision="approve",bool loginAgain=false)
    {
        var path=await Begin(browser);
        if(loginAgain) browser.Cookies.Remove("TenantPlatform.Auth");
        var consent=await browser.Get(path);
        if(loginAgain)
        {
            Check(consent.StatusCode==HttpStatusCode.Redirect,"issuer requests fresh password login");
            var login=Fields(await (await browser.Get(consent.Headers.Location!.OriginalString)).Content.ReadAsStringAsync());
            login["email"]=user+"@example.test";login["password"]=password;
            var returned=await browser.Post("/auth/login",login);consent=await browser.Get(returned.Headers.Location!.OriginalString);
        }
        Check(consent.StatusCode==HttpStatusCode.OK,"actual issuer consent page");var fields=Fields(await consent.Content.ReadAsStringAsync());fields["decision"]=decision;
        var issued=await browser.Post("/connect/authorize",fields);Check(issued.StatusCode==HttpStatusCode.Redirect,"real issuer authorization response");
        var callback=await browser.Get(issued.Headers.Location!.OriginalString);
        Check(decision!="approve"||callback.StatusCode==HttpStatusCode.Redirect,"OpenIddict validates callback, exchanges code and stores delegated connection");
    }
    async Task<string> Probe(Browser browser,Guid? account=null)
    {
        var fields=Fields(await (await browser.Get("/mcp-client")).Content.ReadAsStringAsync());
        return await (await browser.Post("/mcp-client/probe",new() { ["__RequestVerificationToken"]=fields["__RequestVerificationToken"],["accountId"]=account?.ToString()??"" })).Content.ReadAsStringAsync();
    }
    async Task<string> Disconnect(Browser browser)
    {
        var fields=Fields(await (await browser.Get("/mcp-client")).Content.ReadAsStringAsync());
        return await (await browser.Post("/mcp-client/disconnect",new() { ["__RequestVerificationToken"]=fields["__RequestVerificationToken"] })).Content.ReadAsStringAsync();
    }
    async Task<List<McpClientCredential>> Rows()
    {
        await using var scope=web!.Services.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<OAuthDbContext>().Set<McpClientCredential>().AsNoTracking().Where(x=>x.ConnectionId==Guid.Empty).ToListAsync();
    }
    async Task ForceRefresh(Guid id)
    {
        await using var scope=web!.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<OAuthDbContext>();var row=await db.Set<McpClientCredential>().SingleAsync(x=>x.UserId==id);row.AccessExpiresUtc=clock.GetUtcNow().AddSeconds(-1);await db.SaveChangesAsync();
    }
}
finally { if(web is not null) await web.DisposeAsync();if(mcp is not null) await mcp.DisposeAsync();Directory.Delete(directory,true); }

static Dictionary<string,string> Fields(string html)=>Regex.Matches(html,"<input[^>]*name=\"([^\"]+)\"[^>]*value=\"([^\"]*)\"[^>]*>").GroupBy(x=>WebUtility.HtmlDecode(x.Groups[1].Value)).ToDictionary(x=>x.Key,x=>WebUtility.HtmlDecode(x.First().Groups[2].Value));
static void Check(bool value,string message) { if(!value) throw new Exception("FAIL MCP client: "+message);Console.WriteLine("PASS MCP client: "+message); }
sealed class RouteControl
{
    public readonly System.Collections.Concurrent.ConcurrentBag<string> Tokens = [];
    public int Refreshes; public int McpRequests; public bool FailRefresh; public bool FailRevoke; public bool FailIssuer; public bool FailMcp; public bool WaitMcp;
    public TaskCompletionSource McpCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
sealed class TestRoute(RouteControl control,Func<WebApplication?> web,Func<WebApplication?> mcp):DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        if(request.RequestUri!.Host=="tenant.test")
        {
            if(control.FailIssuer) throw new HttpRequestException("Simulated issuer outage.");
            var refreshing=false;
            if(request.RequestUri.AbsolutePath=="/connect/token")
            {
                var body=await request.Content!.ReadAsStringAsync(ct);
                if(QueryHelpers.ParseQuery(body).TryGetValue("grant_type",out var type)&&type=="refresh_token")
                { Interlocked.Increment(ref control.Refreshes);refreshing=true; }
            }
            if(request.RequestUri.AbsolutePath=="/connect/revoke"&&control.FailRevoke) throw new HttpRequestException("Simulated revoke outage.");
            var response=await web()!.GetTestServer().CreateHandler().SendVia(request,ct);
            if(request.RequestUri.AbsolutePath=="/connect/token" && response.IsSuccessStatusCode)
            {
                using var tokenResponse=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                foreach(var key in new[]{"access_token","refresh_token"}) if(tokenResponse.RootElement.TryGetProperty(key,out var token)) control.Tokens.Add(token.GetString()!);
            }
            if(refreshing&&control.FailRefresh) { response.Dispose();throw new HttpRequestException("Simulated lost response AFTER refresh was consumed."); }
            return response;
        }
        if(request.RequestUri.Host=="mcp.test")
        {
            Interlocked.Increment(ref control.McpRequests);
            if(control.FailMcp) throw new HttpRequestException("Simulated MCP outage.");
            if(control.WaitMcp) { try { await Task.Delay(Timeout.Infinite,ct); } catch(OperationCanceledException) { control.McpCancelled.TrySetResult(); throw; } }
            return await mcp()!.GetTestServer().CreateHandler().SendVia(request,ct);
        }
        throw new Exception("Nonfixture network request rejected.");
    }
}
static class HandlerTestExtensions
{
    public static async Task<HttpResponseMessage> SendVia(this HttpMessageHandler handler,HttpRequestMessage request,CancellationToken ct)
    { using var invoker=new HttpMessageInvoker(handler);return await invoker.SendAsync(request,ct); }
}
