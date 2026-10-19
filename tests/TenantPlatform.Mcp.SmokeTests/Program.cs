using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Npgsql;
using OpenIddict.Abstractions;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Authentication;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Authentication;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Infrastructure.Persistence.OAuth;
using TenantPlatform.Mcp;
using TenantPlatform.Web.Security.OAuth;

var connection = Environment.GetEnvironmentVariable("MCP_TEST_CONNECTION") ?? throw new Exception("Use isolated PostgreSQL runner.");
var cs = new NpgsqlConnectionStringBuilder(connection);
if (cs.Database != "tenant_mcp_tests" || cs.Host?.StartsWith("/tmp/tenant-agreement-tests.", StringComparison.Ordinal) != true) throw new Exception("Disposable MCP database only.");
var directory = Path.Combine(Path.GetTempPath(), "tenant-mcp-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var pfxPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
var oauth = new OAuthIssuerOptions { Enabled = true, Issuer = "https://tenant.test/", Resource = "https://mcp.test/mcp", ConnectionString = connection,
    SigningCertificatePath = Path.Combine(directory,"sign.pfx"), SigningCertificatePassword = pfxPassword,
    EncryptionCertificatePath = Path.Combine(directory,"enc.pfx"), EncryptionCertificatePassword = pfxPassword, CookieKeyDirectory = Path.Combine(directory,"keys"),
    Clients = [new() { ClientId = "protocol-fixture", DisplayName = "MCP fixture", Secret = secret, RedirectUris = ["https://client.test/callback"] }] };
foreach (var path in new[] { oauth.SigningCertificatePath, oauth.EncryptionCertificatePath })
{
    using var rsa = RSA.Create(2048); var request = new CertificateRequest("CN=MCP test only",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
    using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(2));
    await File.WriteAllBytesAsync(path,cert.Export(X509ContentType.Pfx,pfxPassword));
}
var factory = new Factory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(connection).Options);
var user = Guid.NewGuid(); var other = Guid.NewGuid(); var owner = Guid.NewGuid(); var a = Guid.NewGuid(); var b = Guid.NewGuid();
var grantAgreement = Guid.NewGuid(); var orgA = Guid.NewGuid(); var orgB = Guid.NewGuid();
await using (var db = factory.CreateDbContext())
{
    await db.Database.MigrateAsync();
    db.Accounts.AddRange(new Account { Id=a,Name="MCP A" },new Account { Id=b,Name="MCP B" });
    db.Organizations.AddRange(new Organization { Id=orgA,AccountId=a,Name="Counterparty A" },new Organization { Id=orgB,AccountId=b,Name="Counterparty B" });
    foreach (var id in new[] { user,other,owner })
    {
        var person = new User { Id=id,FirstName="SECRET_OWNER",LastName="NAME",Email=id+"@example.test",IsActive=true,IsPlatformAdmin=id==other };
        var login = new LoginAccount { Id=Guid.NewGuid(),UserId=id,Email=person.Email,CreatedUtc=DateTimeOffset.UtcNow };
        login.PasswordHash = new PasswordService().HashPassword(login,password); db.Users.Add(person); db.LoginAccounts.Add(login);
        db.UserAccounts.Add(new UserAccount { Id=Guid.NewGuid(),AccountId=id==other?b:a,UserId=id });
    }
    for(var i=0;i<28;i++) db.Agreements.Add(new Agreement { Id=Guid.NewGuid(),AccountId=a,CounterpartyOrganizationId=orgA,OwnerUserId=user,
        Title=$"A {i:D2}",Type=AgreementType.Supplier,Status=AgreementStatus.Active,StartDate=new(2025,1,1),CurrentPeriodStartDate=new(2025,1,1),
        CreatedByUserId=user,UpdatedByUserId=user,Revision=Guid.NewGuid(),EndDate=DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20) });
    db.Agreements.AddRange(new Agreement { Id=grantAgreement,AccountId=a,CounterpartyOrganizationId=orgA,OwnerUserId=owner,Title="Grant only",Type=AgreementType.Supplier,Status=AgreementStatus.Active,
        StartDate=new(2025,1,1),CurrentPeriodStartDate=new(2025,1,1),CreatedByUserId=owner,UpdatedByUserId=owner,Revision=Guid.NewGuid() },
        new Agreement { Id=Guid.NewGuid(),AccountId=b,CounterpartyOrganizationId=orgB,OwnerUserId=other,Title="B private",Type=AgreementType.Other,Status=AgreementStatus.Draft,
        StartDate=new(2025,1,1),CurrentPeriodStartDate=new(2025,1,1),CreatedByUserId=other,UpdatedByUserId=other,Revision=Guid.NewGuid() });
    db.AgreementAccess.Add(new AgreementAccess { AccountId=a,AgreementId=grantAgreement,UserId=user,Level=AgreementAccessLevel.Read });
    await db.SaveChangesAsync();
}
var tables = new[] { "accounts","users","user_accounts","user_account_roles","agreements","agreement_access","organizations","agreement_reminder_settings","buildings","units","organization_elements","geographic_areas" };
await using(var admin = new NpgsqlConnection(connection))
{
    await admin.OpenAsync();
    await using var command = new NpgsqlCommand("CREATE ROLE mcp_reader LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT; REVOKE CREATE ON SCHEMA public FROM PUBLIC; GRANT CONNECT ON DATABASE tenant_mcp_tests TO mcp_reader; GRANT USAGE ON SCHEMA public TO mcp_reader; GRANT SELECT ON "+string.Join(",",tables.Select(x=>"public."+x))+" TO mcp_reader",admin);
    await command.ExecuteNonQueryAsync();
}
var runtime = new NpgsqlConnectionStringBuilder(connection) { Username="mcp_reader",SearchPath="public" }.ConnectionString;
var options = new McpOptions { Issuer=oauth.Issuer,Resource=oauth.Resource,ConnectionString=runtime,Clients=["protocol-fixture"],AllowedOrigins=["https://client.test"] };
var logs = new LogCapture(); var clock = new AdjustableClock();
WebApplication? issuer=null, host=null;
try
{
    issuer=await OAuthTestHost.Start(oauth,factory,clock,logs);
    await using(var scope=issuer.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<OAuthDbContext>().Database.MigrateAsync();
    await OAuthComposition.RegisterClientsAsync(issuer.Services,oauth);
    var (token,browser)=await Issue(user); var (otherToken,_)=await Issue(other);
    var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
    Check(jwt.Typ=="at+jwt" && jwt.GetClaim("client_id").Value=="protocol-fixture" && jwt.Subject==user.ToString("D"),"actual issuer token profile: at+jwt/sub/client_id");
    await RuntimeDatabaseCheck.VerifyAsync(options);
    try { await RuntimeDatabaseCheck.VerifyAsync(new McpOptions { ConnectionString=connection }); throw new Exception("admin accepted"); }
    catch(InvalidOperationException ex) when(ex.Message.StartsWith("MCP requires a dedicated")) { Check(true,"startup rejects privileged database identity"); }
    var delegationId=jwt.GetClaim(OpenIddictConstants.Claims.Private.AuthorizationId).Value;
    Check(!string.IsNullOrEmpty(delegationId),"actual access JWT carries signed delegation identifier");
    host=await Start();
    using var raw=host.GetTestClient(); raw.BaseAddress=new Uri("https://mcp.test");
    var metadata=await raw.GetStringAsync("/.well-known/oauth-protected-resource/mcp");
    Check(metadata.Contains(oauth.Resource)&&metadata.Contains(oauth.Issuer),"fixed resource discovery");
    Check((await raw.GetAsync("/mcp")).StatusCode==HttpStatusCode.Unauthorized,"anonymous unsupported HTTP verb fails safely");
    var denied=await raw.PostAsync("/mcp",new StringContent("{}",Encoding.UTF8,"application/json"));
    Check(denied.StatusCode==HttpStatusCode.Unauthorized && denied.Headers.WwwAuthenticate.ToString().Contains("resource_metadata"),"anonymous MCP denied with resource metadata challenge");
    using(var cookieRequest=new HttpRequestMessage(HttpMethod.Post,"/mcp?access_token="+Uri.EscapeDataString(token)) { Content=new StringContent("{}",Encoding.UTF8,"application/json") })
    {
        cookieRequest.Headers.Add("Cookie","TenantPlatform.Auth=fake-cookie");
        Check((await raw.SendAsync(cookieRequest)).StatusCode==HttpStatusCode.Unauthorized,"query tokens and Web cookies cannot authenticate MCP");
    }
    foreach(var version in new[] { "2025-11-25","2026-07-28" })
    {
        await using var client=await Client(token,version);
        var tools=await client.ListToolsAsync();
        Check(tools.Select(t=>t.Name).Order().SequenceEqual(new[]{"accounts_list","agreements_aggregate","agreements_attention","agreements_details","agreements_list","agreements_query"}),"exact six tools "+version);
        var schemas=JsonSerializer.Serialize(tools.Select(t=>t.ProtocolTool));
        Check(tools.All(t=>t.ProtocolTool.OutputSchema!.Value.GetProperty("additionalProperties").ValueKind==JsonValueKind.False) && !schemas.Contains("Owner") && schemas.Contains("additionalProperties") && schemas.Contains("outputSchema"),"bounded input/output schemas "+version);
        var accounts=Data(await client.CallToolAsync("accounts_list",new Dictionary<string,object?>()));
        Check(accounts.GetProperty("accounts").GetArrayLength()==1 && accounts.GetProperty("accounts")[0].GetProperty("id").GetGuid()==a,"own accounts "+version);
        var list=Data(await client.CallToolAsync("agreements_list",Args(a)));
        Check(list.GetProperty("totalCount").GetInt32()==29 && list.GetProperty("returnedCount").GetInt32()==25 && list.GetProperty("hasMore").GetBoolean(),"real paging "+version);
        var second=Data(await client.CallToolAsync("agreements_list",new Dictionary<string,object?> { ["accountId"]=a,["page"]=2 }));
        Check(second.GetProperty("items").GetArrayLength()==4 && !second.GetProperty("complete").GetBoolean() && !second.GetProperty("hasMore").GetBoolean(),"last page does not falsely claim complete");
        var empty=Data(await client.CallToolAsync("agreements_list",new Dictionary<string,object?> { ["accountId"]=a,["search"]="no-match" }));
        Check(empty.GetProperty("totalCount").GetInt32()==0 && empty.GetProperty("complete").GetBoolean(),"empty filtered result");
        var attention=Data(await client.CallToolAsync("agreements_attention",Args(a)));
        var appActor=new FrozenActor(user,a); var auth=new TenantPlatform.Application.Security.AuthorizationService(appActor,new AuthorizationData(factory),TimeProvider.System);
        var direct=await new AgreementListService(auth,new AgreementListData(factory),TimeProvider.System).ListAsync(a,new());
        var count=await new AgreementAttentionService(auth,new AgreementAttentionData(factory),TimeProvider.System).GetAttentionAsync(a);
        Check(direct.TotalCount==list.GetProperty("totalCount").GetInt32() && direct.Items.Select(x=>x.Id).SequenceEqual(list.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("id").GetGuid())) && count.Today==attention.GetProperty("today").GetDateTime().ToDateOnly() && count.Notice==attention.GetProperty("notice").GetInt32(),"direct Application equivalence");
        foreach(var invalid in new Dictionary<string,object?>[] { new() { ["accountId"]=b },new() { ["accountId"]=Guid.NewGuid() } })
            Check(Error(await client.CallToolAsync("agreements_list",invalid))=="RESOURCE_UNAVAILABLE","foreign and unknown account same safe error");
        foreach(var invalid in new Dictionary<string,object?>[] {
            new() { ["accountId"]=a,["userId"]=other },new() { ["accountId"]=a,["scope"]="all" },new() { ["accountId"]=a,["page"]=0 },new() { ["accountId"]=a,["page"]="x" },
            new() { ["accountId"]=a,["type"]=1 },new() { ["accountId"]=a,["status"]="active" },new() { ["accountId"]=a,["search"]=new string('x',201) },new() { ["accountId"]=a,["search"]="\ntext" } })
            Check(Error(await client.CallToolAsync("agreements_list",invalid))=="INVALID_ARGUMENT","invalid/spoofed arguments rejected");
        var surfaces=JsonSerializer.Serialize(new[]{list,second,attention,accounts});
        Check(!new[]{"ownerUserId","ownerName","owners","SECRET_OWNER","transportId","recipientAddress","terms","revision","grants"}.Any(surfaces.Contains),"no omitted metadata in complete payload");
    }
    await ReportingChecks.Run(factory, runtime, password, async subject => { var (access, _) = await Issue(subject); return await Client(access, "2026-07-28"); });
    await using var live=await Client(token,"2026-07-28");
    await using var secondClient=await Client(otherToken,"2026-07-28");
    var parallel=await Task.WhenAll(Enumerable.Range(0,4).Select(async i=> {
        var result=await (i%2==0?live:secondClient).CallToolAsync("agreements_list",Args(i%2==0?a:b));
        return Data(result).GetProperty("accountId").GetGuid()==(i%2==0?a:b);
    }));
    Check(parallel.All(x=>x),"concurrent users/accounts isolated");
    await using(var db=factory.CreateDbContext()) { db.AgreementAccess.Remove(await db.AgreementAccess.SingleAsync()); await db.SaveChangesAsync(); }
    Check(Data(await live.CallToolAsync("agreements_list",Args(a))).GetProperty("totalCount").GetInt32()==28,"grant revocation observed next call");
    await using(var db=factory.CreateDbContext()) { db.UserAccounts.Remove(await db.UserAccounts.SingleAsync(x=>x.UserId==user)); await db.SaveChangesAsync(); }
    Check(Error(await live.CallToolAsync("agreements_list",Args(a)))=="RESOURCE_UNAVAILABLE","membership revocation observed next call");
    await using(var db=factory.CreateDbContext()) { db.UserAccounts.Add(new UserAccount { Id=Guid.NewGuid(),UserId=user,AccountId=a }); (await db.Users.SingleAsync(x=>x.Id==user)).IsActive=false; await db.SaveChangesAsync(); }
    Check(Error(await live.CallToolAsync("accounts_list",new Dictionary<string,object?>()))=="RESOURCE_UNAVAILABLE","inactive user denied next call");
    await using(var db=factory.CreateDbContext()) { (await db.Users.SingleAsync(x=>x.Id==user)).IsActive=true; await db.SaveChangesAsync(); }
    await using(var db=factory.CreateDbContext()) { var member=await db.UserAccounts.SingleAsync(x=>x.UserId==user); db.UserAccountRoles.Add(new UserAccountRole { Id=Guid.NewGuid(),UserAccountId=member.Id,Role=UserRole.AccountAdmin }); await db.SaveChangesAsync(); }
    Check(Data(await live.CallToolAsync("agreements_list",Args(a))).GetProperty("totalCount").GetInt32()==29,"fresh account role expands Application scope");
    await using(var db=factory.CreateDbContext()) { db.UserAccountRoles.Remove(await db.UserAccountRoles.SingleAsync()); await db.SaveChangesAsync(); }
    Check(Data(await live.CallToolAsync("agreements_list",Args(a))).GetProperty("totalCount").GetInt32()==28,"removed role stops expanded access next call");
    Check(Error(await secondClient.CallToolAsync("agreements_list",Args(a)))=="RESOURCE_UNAVAILABLE","PlatformAdmin token does not bypass account membership");
    var management=await browser.Get("/oauth/connections"); var revokeFields=Fields(await management.Content.ReadAsStringAsync());
    Check((await browser.Post("/oauth/connections/revoke",revokeFields)).StatusCode==HttpStatusCode.Redirect,"real OAuth management revocation succeeded");
    await using(var checkScope=issuer.Services.CreateAsyncScope())
        Check((await checkScope.ServiceProvider.GetRequiredService<IOAuthConnectionData>().ListAsync(user,default)).All(x=>!x.Valid),"OAuth authorization actually revoked in issuer storage");
    Check(Data(await live.CallToolAsync("agreements_list",Args(a))).GetProperty("totalCount").GetInt32()==28,"revoked OAuth grant: already issued JWT retains bounded lifetime");
    // Invalid variants use the TEST issuer key, never a configurable production identity bypass.
    using var signing=X509CertificateLoader.LoadPkcs12FromFile(oauth.SigningCertificatePath,pfxPassword);
    using var wrongRsa=RSA.Create(2048);
    string Variant(string? issuerValue=null,string? audience=null,string? type=null,string? scope=null,string? clientId=null,DateTime? expires=null,DateTime? notBefore=null,SecurityKey? key=null,string? subject=null,string algorithm=SecurityAlgorithms.RsaSha256,bool delegated=true,bool omitExpiration=false) => new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation=false }.CreateToken(new SecurityTokenDescriptor {
        Issuer=issuerValue??oauth.Issuer,Audience=audience??oauth.Resource,TokenType=type??"at+jwt",IssuedAt=DateTime.UtcNow.AddMinutes(-10),
        NotBefore=notBefore??DateTime.UtcNow.AddMinutes(-10),Expires=omitExpiration?null:expires??DateTime.UtcNow.AddMinutes(4),
        SigningCredentials=new SigningCredentials(key??new X509SecurityKey(signing),algorithm),
        Claims=new Dictionary<string,object> { [OpenIddictConstants.Claims.Private.AuthorizationId]=delegated?delegationId:"",["sub"]=subject??user.ToString("D"),["client_id"]=clientId??"protocol-fixture",["scope"]=scope??McpOptions.ReadScope } });
    foreach(var invalid in new[] { Variant(issuerValue:"https://evil.test/"),Variant(audience:"https://other.test/"),Variant(type:"JWT"),Variant(scope:"other"),Variant(clientId:"unapproved"),Variant(expires:DateTime.UtcNow.AddMinutes(-1)),Variant(notBefore:DateTime.UtcNow.AddMinutes(2)),Variant(key:new RsaSecurityKey(wrongRsa)),Variant(subject:"service-identity"),Variant(algorithm:SecurityAlgorithms.RsaSha384),Variant(issuerValue:"https://tenant.test"),Variant(delegated:false),Variant(omitExpiration:true) })
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,"/mcp") { Content=new StringContent("{}",Encoding.UTF8,"application/json") }; request.Headers.Authorization=new("Bearer",invalid);
        var response=await raw.SendAsync(request); Check(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,"invalid token profile rejected");
    }
    await using(var readOnly=new TenantPlatformDbContext(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(runtime).Options))
    {
        try { await readOnly.Database.ExecuteSqlRawAsync("UPDATE agreements SET \"Title\"='forbidden'"); throw new Exception("reader wrote"); } catch(PostgresException ex) when(ex.SqlState=="42501") { Check(true,"real runtime PostgreSQL role forbids SQL writes"); }
        try { await readOnly.Database.ExecuteSqlRawAsync("SELECT * FROM login_accounts"); throw new Exception("reader accessed credentials"); } catch(PostgresException ex) when(ex.SqlState=="42501") { Check(true,"runtime role cannot read credentials"); }
    }
    await using(var scope=host.Services.CreateAsyncScope())
    await using(var guarded=await scope.ServiceProvider.GetRequiredService<IDbContextFactory<TenantPlatformDbContext>>().CreateDbContextAsync())
    {
        try { await guarded.SaveChangesAsync(); throw new Exception("SaveChanges allowed"); } catch(InvalidOperationException ex) when(ex.Message=="MCP is read only.") { Check(true,"SaveChanges guard rejects even empty writes"); }
    }
    await using(var db=factory.CreateDbContext()) Check(!await db.AgreementReminderSettings.AnyAsync() && !await db.AgreementDeadlines.AnyAsync() && !await db.Agreements.AnyAsync(x=>x.DeadlinesInitialized),"reads did not initialize deadlines/settings");
    Check(!logs.Messages.Any(line=>line.Contains(token)||line.Contains(otherToken)||line.Contains(password)||line.Contains(secret)||line.Contains("SECRET_OWNER")),"no secrets/omitted metadata in captured logs");
    // Real HTTP boundary tests; TEST-only decorator still calls the real authorized Application reader first.
    await host.DisposeAsync(); var control = new TestControl(); host = await Start(control);
    using var boundaryHttp = host.GetTestClient(); boundaryHttp.BaseAddress = new Uri("https://mcp.test");
    async Task<HttpResponseMessage> Wire(string bearer, Guid account, object? id = null, CancellationToken ct = default, string? origin = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent(JsonSerializer.Serialize(new {
            jsonrpc = "2.0", id = id ?? 7, method = "tools/call", @params = new { name = "agreements_list", arguments = Args(account), _meta = new Dictionary<string,object> { ["io.modelcontextprotocol/protocolVersion"]="2026-07-28", ["io.modelcontextprotocol/clientCapabilities"]=new {}, ["io.modelcontextprotocol/clientInfo"]=new { name="wire-test",version="1" } } }
        }),Encoding.UTF8,"application/json") };
        message.Headers.Authorization = new("Bearer",bearer); message.Headers.Add("MCP-Protocol-Version","2026-07-28");
        message.Headers.Add("Mcp-Method","tools/call"); message.Headers.Add("Mcp-Name","agreements_list");
        message.Headers.Accept.ParseAdd("application/json, text/event-stream");
        if (origin is not null) message.Headers.Add("Origin",origin);
        return await boundaryHttp.SendAsync(message,ct);
    }
    var sameIds = await Task.WhenAll(Wire(token,a),Wire(otherToken,b));
    var sameBodies = await Task.WhenAll(sameIds.Select(x=>x.Content.ReadAsStringAsync()));
    Check(sameBodies[0].Contains(a.ToString()) && !sameBodies[0].Contains("B private") && sameBodies[1].Contains(b.ToString()) && !sameBodies[1].Contains("A 00"),"same JSON-RPC ID cannot mix users/accounts");
    Check((await Wire(token,a,origin:"https://evil.test")).StatusCode==HttpStatusCode.Forbidden,"unapproved Origin denied");
    using(var oversizedRequest=new HttpRequestMessage(HttpMethod.Post,"/mcp") { Content=new StringContent(new string('x',16*1024+1),Encoding.UTF8,"application/json") })
    {
        oversizedRequest.Headers.Authorization=new("Bearer",token);
        Check((await boundaryHttp.SendAsync(oversizedRequest)).StatusCode==HttpStatusCode.RequestEntityTooLarge,"16 KiB HTTP request boundary");
    }
    control.Mode="large";
    var large=await Wire(token,a); var largeText=await large.Content.ReadAsStringAsync();
    Check(largeText.Contains("RESULT_TOO_LARGE") && !largeText.Contains("too-large-secret"),"oversized structured result returns error without partial data");
    control.Mode="envelope";
    var envelope=await Wire(token,a,id:new string('i',12000));
    Check(envelope.StatusCode==HttpStatusCode.RequestEntityTooLarge && (await envelope.Content.ReadAsStringAsync())=="RESULT_TOO_LARGE","64 KiB final HTTP envelope includes RPC ID and framing");
    control.Mode="wait";
    using(var abort=new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
    {
        try { await Wire(token,a,ct:abort.Token); throw new Exception("cancel did not abort"); } catch(OperationCanceledException) { }
        await control.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(control.Active==0,"HTTP abort reaches Application cancellation with no residual work");
    }
    control.Reset();
    var timed=await Wire(token,a); await timed.Content.ReadAsStringAsync();
    await control.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Check(control.Active==0 && (timed.StatusCode==HttpStatusCode.GatewayTimeout || timed.IsSuccessStatusCode),"10 second budget cancels Application and completes request");
    control.Reset();
    var held=Enumerable.Range(0,4).Select(i=>Wire(token,a,id:i)).ToArray();
    var waitUntil=DateTime.UtcNow.AddSeconds(3);
    while(control.Active<4 && DateTime.UtcNow<waitUntil) await Task.Delay(10);
    Check(control.Active==4 && (await Wire(token,a,id:9)).StatusCode==HttpStatusCode.TooManyRequests,"four concurrent requests per user; excess rejected");
    control.Release.TrySetResult(); await Task.WhenAll(held); control.Mode="normal";
    // Cancel a genuinely blocked PostgreSQL SELECT, not just an artificial task delay.
    await using(var locker=new NpgsqlConnection(connection))
    {
        await locker.OpenAsync(); await using var transaction=await locker.BeginTransactionAsync();
        await using(var lockCommand=new NpgsqlCommand("LOCK TABLE public.agreements IN ACCESS EXCLUSIVE MODE",locker,transaction)) await lockCommand.ExecuteNonQueryAsync();
        async Task<long> Waiting()
        {
            await using(var refresh=new NpgsqlCommand("SELECT pg_stat_clear_snapshot()",locker,transaction)) await refresh.ExecuteNonQueryAsync();
            await using var query=new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE usename='mcp_reader' AND state='active' AND wait_event_type='Lock'",locker,transaction);
            return (long)(await query.ExecuteScalarAsync())!;
        }
        using var abortSql=new CancellationTokenSource(); var pending=Wire(token,a,ct:abortSql.Token);
        var until=DateTime.UtcNow.AddSeconds(3);
        while(await Waiting()==0 && DateTime.UtcNow<until) await Task.Delay(20);
        Check(await Waiting()>0,"runtime SELECT actually waits on PostgreSQL lock");
        abortSql.Cancel(); try { await pending; throw new Exception("SQL abort did not propagate"); } catch(OperationCanceledException) { }
        until=DateTime.UtcNow.AddSeconds(3);
        while(await Waiting()>0 && DateTime.UtcNow<until) await Task.Delay(20);
        Check(await Waiting()==0,"HTTP abort cancels actual PostgreSQL query without residual lock waiter");
        await transaction.RollbackAsync();
    }
    bool limited=false;
    for(var i=0;i<65;i++) if((await Wire(token,a)).StatusCode==HttpStatusCode.TooManyRequests) { limited=true; break; }
    Check(limited,"60 requests/minute quota per user across clients");
    await host.DisposeAsync(); host=await Start();
    await using(var db=factory.CreateDbContext())
    {
        for(var i=0;i<100;i++) { var account=new Account { Id=Guid.NewGuid(),Name="Extra "+i }; db.Accounts.Add(account); db.UserAccounts.Add(new UserAccount { Id=Guid.NewGuid(),AccountId=account.Id,UserId=user }); }
        await db.SaveChangesAsync();
    }
    await using(var boundedClient=await Client(token,"2026-07-28"))
        Check(Error(await boundedClient.CallToolAsync("accounts_list",new Dictionary<string,object?>()))=="RESULT_TOO_LARGE","over 100 accounts produces whole-result error");
    clock.Advance(TimeSpan.FromMinutes(6));
    using(var expiredRequest=new HttpRequestMessage(HttpMethod.Post,oauth.Resource) { Content=new StringContent("{}",Encoding.UTF8,"application/json") })
    {
        expiredRequest.Headers.Authorization=new("Bearer",token);
        Check((await host.GetTestClient().SendAsync(expiredRequest)).StatusCode==HttpStatusCode.Unauthorized,"real revoked JWT denied after expiry (no online grant check)");
    }
    var schemes=await host.Services.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>().GetAllSchemesAsync();
    Check(schemes.Count()==1 && !schemes.Any(x=>x.Name.Contains("Cookie")||x.Name.Contains("Test")),"no cookie or test-auth scheme in production composition");
    Console.WriteLine("MCP PostgreSQL/real OAuth/transport checks passed.");

    async Task<McpClient> Client(string bearer,string version)
    {
        var http=host!.GetTestClient(); http.DefaultRequestHeaders.Authorization=new("Bearer",bearer);
        return await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions { Endpoint=new Uri(oauth.Resource),TransportMode=HttpTransportMode.StreamableHttp },http),new McpClientOptions { ProtocolVersion=version });
    }
    async Task<WebApplication> Start(TestControl? control = null)
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName="Testing",ApplicationName=typeof(McpComposition).Assembly.FullName });
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders(); builder.Logging.AddProvider(logs);
        builder.Services.AddTenantMcp(options);
        builder.Services.Configure<OpenIddict.Validation.OpenIddictValidationOptions>(validation => validation.TimeProvider=clock);
        if (control is not null)
        {
            builder.Services.AddScoped<AgreementListService>();
            builder.Services.Replace(ServiceDescriptor.Scoped<IAgreementListService>(sp => new ControlledList(sp.GetRequiredService<AgreementListService>(), control)));
        }
        // Route discovery/JWKS to the real in-process issuer over TestServer. No signature/issuer/auth checks disabled.
        builder.Services.ConfigureAll<Microsoft.Extensions.Http.HttpClientFactoryOptions>(http => http.HttpMessageHandlerBuilderActions.Add(handler => handler.AdditionalHandlers.Insert(0, new IssuerRoute(issuer!.GetTestServer().CreateHandler()))));
        var app=builder.Build(); app.MapTenantMcp(); await app.StartAsync(); return app;
    }
    async Task<(string,Browser)> Issue(Guid subject)
    {
        var browser=new Browser(issuer!.GetTestClient()); var verifier=Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));
        var path="/connect/authorize"+QueryString.Create(new Dictionary<string,string?> { ["client_id"]="protocol-fixture",["redirect_uri"]="https://client.test/callback",["response_type"]="code",["scope"]=McpOptions.ReadScope+" offline_access",["resource"]=oauth.Resource,["state"]="mcp-test",["code_challenge_method"]="S256",["code_challenge"]=Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) });
        var redirect=await browser.Get(path); var login=Fields(await (await browser.Get(redirect.Headers.Location!.OriginalString)).Content.ReadAsStringAsync());
        login["email"]=subject+"@example.test"; login["password"]=password;
        var logged=await browser.Post("/auth/login",login); var consent=Fields(await (await browser.Get(logged.Headers.Location!.OriginalString)).Content.ReadAsStringAsync()); consent["decision"]="approve";
        var approved=await browser.Post("/connect/authorize",consent); var code=QueryHelpers.ParseQuery(approved.Headers.Location!.Query)["code"].ToString();
        var response=await browser.Post("/connect/token",new() { ["grant_type"]="authorization_code",["client_id"]="protocol-fixture",["client_secret"]=secret,["redirect_uri"]="https://client.test/callback",["code_verifier"]=verifier,["code"]=code,["resource"]=oauth.Resource });
        Check(response.IsSuccessStatusCode,"actual login/consent/code+PKCE token flow");
        return (JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("access_token").GetString()!,browser);
    }
}
finally { if(host is not null) await host.DisposeAsync(); if(issuer is not null) await issuer.DisposeAsync(); Directory.Delete(directory,true); }

static Dictionary<string,object?> Args(Guid account)=>new() { ["accountId"]=account };
static JsonElement Data(CallToolResult result) { if(result.IsError==true) throw new Exception("Tool error: "+Error(result)); return result.StructuredContent!.Value; }
static string Error(CallToolResult result)=>result.Content.OfType<TextContentBlock>().Single().Text;
static Dictionary<string,string> Fields(string html)=>Regex.Matches(html,"<input[^>]*name=\"([^\"]+)\"[^>]*value=\"([^\"]*)\"[^>]*>").GroupBy(x=>WebUtility.HtmlDecode(x.Groups[1].Value)).ToDictionary(x=>x.Key,x=>WebUtility.HtmlDecode(x.First().Groups[2].Value));
static void Check(bool success,string message) { if(!success) throw new Exception("FAIL MCP: "+message); Console.WriteLine("PASS MCP: "+message); }
sealed class FrozenActor(Guid user,Guid? account):ICurrentActor { public ActorSnapshot GetSnapshot()=>new(true,user,account); }
static class Dates { public static DateOnly ToDateOnly(this DateTime date)=>DateOnly.FromDateTime(date); }

sealed class IssuerRoute(HttpMessageHandler testServer) : DelegatingHandler
{
    private readonly HttpMessageInvoker invoker = new(testServer);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => invoker.SendAsync(request, ct);
    protected override void Dispose(bool disposing) { if (disposing) invoker.Dispose(); base.Dispose(disposing); }
}

sealed class TestControl
{
    public string Mode = "normal";
    public int Active;
    public TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Reset() { Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously); Release = new(TaskCreationOptions.RunContinuationsAsynchronously); }
}
sealed class ControlledList(AgreementListService inner, TestControl control) : IAgreementListService
{
    public async Task<AgreementPageDto> ListAsync(Guid accountId, AgreementFilter filter, CancellationToken ct = default)
    {
        var result = await inner.ListAsync(accountId,filter,ct);
        if (control.Mode == "envelope") return result with { Items = result.Items.Select(x=>x with { Title = new string('x',2100) }).ToList() };
        if (control.Mode == "large") return result with { Items = result.Items.Select(x=>x with { Title = "too-large-secret"+new string('x',100_000) }).ToList() };
        if (control.Mode != "wait") return result;
        Interlocked.Increment(ref control.Active);
        try { await control.Release.Task.WaitAsync(ct); }
        catch(OperationCanceledException) { control.Cancelled.TrySetResult(); throw; }
        finally { Interlocked.Decrement(ref control.Active); }
        return result;
    }
}
