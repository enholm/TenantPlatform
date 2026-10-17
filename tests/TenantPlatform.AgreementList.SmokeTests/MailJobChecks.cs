using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Security;
using TenantPlatform.Application.ServiceRequests;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;
using TenantPlatform.Core.Services;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.ServiceRequests;
static class MailJobChecks
{
    public static async Task Run(Factory factory, string provider)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL mail job: " + name); Console.WriteLine($"PASS {provider} mail job: {name}"); }
        async Task Denied(Func<Task> action) { try { await action(); throw new Exception("Expected denial"); } catch (UnauthorizedAccessException) { } }
        var clock = new MailClock(); var sender = new Sender(); var parser = new Parser(); var addresses = new Addresses();
        var before = factory.Created;
        foreach (var kind in new SystemWorkKind?[] { null, SystemWorkKind.AgreementReminders })
        {
            await Denied(() => new InboundServiceRequestEmailJob(new InboundServiceRequestData(factory), parser, new HostSystemWorkContext(kind)).ProcessAsync(new()));
            await Denied(() => new EmailOutboxJob(new ServiceRequestOutboxData(factory), sender, addresses, clock, new HostSystemWorkContext(kind)).ProcessOneAsync());
        }
        Check(factory.Created == before, "missing/unrelated job identity is rejected before data access");
        if (provider != "PostgreSQL") return;
        var account = Guid.NewGuid(); var other = Guid.NewGuid(); var user = Guid.NewGuid(); var org = Guid.NewGuid(); var foreignOrg = Guid.NewGuid(); var supplier = Guid.NewGuid();
        var building = Guid.NewGuid(); var otherBuilding = Guid.NewGuid(); var definition = Guid.NewGuid(); var otherDefinition = Guid.NewGuid(); var request = Guid.NewGuid(); var foreign = Guid.NewGuid(); var providerId = Guid.NewGuid();
        var token = Guid.NewGuid().ToString("N"); var foreignToken = Guid.NewGuid().ToString("N");
        await using (var db = factory.CreateDbContext())
        {
            db.Accounts.AddRange(new Account { Id = account, Name = "Mail A" }, new Account { Id = other, Name = "Mail B" });
            db.Users.Add(new() { Id = user, Email = user + "@example.test", IsActive = true });
            db.Organizations.AddRange(new Organization { Id = org, AccountId = account, Name = "Tenant" }, new Organization { Id = supplier, AccountId = account, Name = "Supplier" }, new Organization { Id = foreignOrg, AccountId = other, Name = "Foreign" });
            db.Buildings.AddRange(new Building { Id = building, AccountId = account, Name = "A", CountryCode = "NO" }, new Building { Id = otherBuilding, AccountId = other, Name = "B", CountryCode = "NO" });
            db.ServiceDefinitions.AddRange(new ServiceDefinition { Id = definition, AccountId = account, Code = "MAIL" }, new ServiceDefinition { Id = otherDefinition, AccountId = other, Code = "MAIL-B" });
            db.ServiceDefinitionProviders.Add(new() { Id = providerId, AccountId = account, ServiceDefinitionId = definition, ServiceProviderOrganizationId = supplier, IsActive = true, IntegrationType = ServiceProviderIntegrationType.Email, RequestEmailAddress = "supplier@example.test" });
            db.ServiceRequests.AddRange(new ServiceRequest { Id = request, AccountId = account, ServiceDefinitionId = definition, RequesterUserId = user, RequesterOrganizationId = org, BuildingId = building, AssignedServiceProviderOrganizationId = supplier, ReplyToken = token, Status = ServiceRequestStatus.Approved, CreatedAt = clock.Now },
                new ServiceRequest { Id = foreign, AccountId = other, ServiceDefinitionId = otherDefinition, RequesterUserId = user, RequesterOrganizationId = foreignOrg, BuildingId = otherBuilding, ReplyToken = foreignToken, Status = ServiceRequestStatus.Submitted, CreatedAt = clock.Now });
            await db.SaveChangesAsync();
        }
        var inbound = new InboundServiceRequestEmailJob(new InboundServiceRequestData(factory), parser, new HostSystemWorkContext(SystemWorkKind.ServiceRequestInbound));
        InboundEmailMessage Message(string selectedToken, string id) => new() { ExternalMessageId = id, FromAddress = "forged-admin@example.test", ToAddresses = ["request-" + selectedToken + "@example.test"], Subject = "Reply", Body = "Transport content", ReceivedAt = clock.Now, References = "<thread>", InReplyToMessageId = "<prior>" };
        var external = Guid.NewGuid() + "@example.test";
        Check(!await inbound.ProcessAsync(Message("unknown", external)), "unknown token never resolves through From identity");
        var parallel = await Task.WhenAll(inbound.ProcessAsync(Message(token, external)), inbound.ProcessAsync(Message(token, external)));
        Check(parallel.All(x => x), "concurrent duplicate inbound is idempotent under request lock");
        Check(!await inbound.ProcessAsync(Message(foreignToken, external)), "global Message-ID collision cannot acknowledge another account's message");
        await using (var db = factory.CreateDbContext())
        {
            var message = await db.ServiceRequestMessages.SingleAsync(x => x.ExternalMessageId == external);
            Check(message.ServiceRequestId == request && message.CreatedByUserId is null && message.InReplyToMessageId == "<prior>" && message.References == "<thread>", "token-bound inbound preserves thread without assigning sender a user identity");
        }
        var outbox = new EmailOutboxJob(new ServiceRequestOutboxData(factory), sender, addresses, clock, new HostSystemWorkContext(SystemWorkKind.EmailOutbox));
        async Task<Guid> Queue(string recipient = "supplier@example.test", Guid? selectedAccount = null, string? reply = null)
        {
            var id = Guid.NewGuid(); await using var db = factory.CreateDbContext();
            db.EmailOutboxMessages.Add(new() { Id = id, AccountId = selectedAccount ?? account, ServiceRequestId = request, ToAddress = recipient, ReplyToAddress = "foreign-token@evil.test", Subject = "Job", Body = "Body", Status = EmailOutboxStatus.Pending, CreatedAt = clock.Now, NextAttemptAt = clock.Now, InReplyToMessageId = reply, References = "<thread>" });
            await db.SaveChangesAsync(); return id;
        }
        var first = await Queue();
        await Task.WhenAll(outbox.ProcessOneAsync(), outbox.ProcessOneAsync());
        Check(sender.Count == 1 && sender.LastReplyTo == "request-" + token + "@example.test", "parallel outbox workers send once and derive Reply-To from current request");
        await using (var db = factory.CreateDbContext()) Check((await db.EmailOutboxMessages.SingleAsync(x => x.Id == first)).Status == EmailOutboxStatus.Sent && await db.ServiceRequestMessages.CountAsync(x => x.ServiceRequestId == request && x.Direction == ServiceRequestMessageDirection.Outbound) == 1, "send and outbound history commit together");
        var cross = await Queue(selectedAccount: other); await outbox.ProcessOneAsync();
        var unknown = await Queue("secret@example.test"); await outbox.ProcessOneAsync();
        Check(sender.Count == 1, "foreign request reference and unrelated recipient never reach transport");
        await using (var db = factory.CreateDbContext()) Check(await db.EmailOutboxMessages.CountAsync(x => (x.Id == cross || x.Id == unknown) && x.Status == EmailOutboxStatus.Failed) == 2, "invalid scoped queue entries terminate without history");
        var replyId = await Queue("forged-admin@example.test", reply: external); await outbox.ProcessOneAsync();
        Check(sender.Count == 2, "token-bound correspondence can receive a threaded reply without being a user");
        var retry = await Queue(); sender.Fail = true; await outbox.ProcessOneAsync();
        await using (var db = factory.CreateDbContext()) Check((await db.EmailOutboxMessages.SingleAsync(x => x.Id == retry)) is { AttemptCount: 1, LastError: "EmailDeliveryFailed" }, "failure uses sanitized result and bounded backoff");
        Check(!await outbox.ProcessOneAsync(), "retry is not immediately eligible");
        clock.Now = clock.Now.AddMinutes(3); sender.Fail = false; await outbox.ProcessOneAsync();
        await using (var db = factory.CreateDbContext()) Check((await db.EmailOutboxMessages.SingleAsync(x => x.Id == retry)).Status == EmailOutboxStatus.Sent, "retry can succeed after backoff");
        var changed = await Queue();
        await using (var db = factory.CreateDbContext()) { (await db.ServiceDefinitionProviders.SingleAsync(x => x.Id == providerId)).IsActive = false; await db.SaveChangesAsync(); }
        var count = sender.Count; await outbox.ProcessOneAsync(); Check(sender.Count == count, "provider recipient is revalidated immediately before delivery");
    }
    private sealed class MailClock : TimeProvider { public DateTimeOffset Now = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Parser : IServiceRequestReplyAddressParser
    { public bool TryGetReplyToken(string address, out string token) { token = address.StartsWith("request-") && address.Contains('@') ? address[8..address.IndexOf('@')] : ""; return token.Length > 0; } }
    private sealed class Addresses : IServiceRequestEmailAddressService { public string GetReplyAddress(string token) => "request-" + token + "@example.test"; }
    private sealed class Sender : IEmailSender
    {
        public int Count; public bool Fail; public string? LastReplyTo;
        public async Task SendAsync(string toAddress, string? replyToAddress, string subject, string body, string? inReplyToMessageId = null, string? references = null, CancellationToken cancellationToken = default)
        { await Task.Delay(30, cancellationToken); if (Fail) throw new IOException("secret transport content"); Interlocked.Increment(ref Count); LastReplyTo = replyToAddress; }
    }
}
