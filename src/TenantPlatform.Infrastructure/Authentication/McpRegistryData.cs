using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Mcp;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Persistence.OAuth;

namespace TenantPlatform.Infrastructure.Authentication;

public sealed class McpRegistryData(DbContextOptions<OAuthDbContext> options) : IMcpRegistryData
{
    public async Task<IReadOnlyList<McpServerDefinition>> ListAsync(Guid account, CancellationToken ct)
    {
        await using var db = new OAuthDbContext(options);
        return await db.Set<McpServerDefinition>().AsNoTracking().Where(x => x.AccountId == account).OrderBy(x => x.Name).ThenBy(x => x.Id).ToArrayAsync(ct);
    }
    public async Task<McpServerDefinition?> ReadAsync(Guid account, Guid connection, CancellationToken ct)
    {
        await using var db = new OAuthDbContext(options);
        return await db.Set<McpServerDefinition>().AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == connection, ct);
    }
    public async Task SaveAsync(McpServerDefinition value, Guid? expectedRevision, bool invalidateTools, CancellationToken ct)
    {
        await using var db = new OAuthDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (expectedRevision.HasValue)
        {
            var row = await db.Set<McpServerDefinition>().FromSqlInterpolated($"SELECT * FROM oauth.mcp_servers WHERE \"AccountId\" = {value.AccountId} AND \"Id\" = {value.Id} FOR UPDATE").SingleOrDefaultAsync(ct)
                ?? throw new McpClientException("RESOURCE_UNAVAILABLE");
            db.Entry(row).Property(x => x.Revision).OriginalValue = expectedRevision.Value;
            // Any profile change clears approvals, even if an old catalogue is still displayed.
            if (invalidateTools)
                await db.Set<McpDiscoveredTool>().Where(x => x.ConnectionId == row.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Approved, false), ct);
            db.Entry(row).CurrentValues.SetValues(value);
        }
        else db.Add(value);
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new McpClientException("CONFIGURATION_CHANGED"); }
    }
    public async Task<IReadOnlyList<McpDiscoveredTool>> ToolsAsync(Guid account, Guid connection, CancellationToken ct)
    {
        await using var db = new OAuthDbContext(options);
        return await (from tool in db.Set<McpDiscoveredTool>().AsNoTracking()
            join server in db.Set<McpServerDefinition>() on tool.ConnectionId equals server.Id
            where server.AccountId == account && server.Id == connection orderby tool.Name select tool).ToArrayAsync(ct);
    }
    public async Task<Guid> SaveDiscoveryAsync(Guid account, Guid connection, Guid revision, IReadOnlyList<McpDiscoveredTool> tools, bool complete, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = new OAuthDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var row = await db.Set<McpServerDefinition>().FromSqlInterpolated($"SELECT * FROM oauth.mcp_servers WHERE \"AccountId\" = {account} AND \"Id\" = {connection} AND \"Revision\" = {revision} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw new McpClientException("CONFIGURATION_CHANGED");
        // Updating the concurrency token makes a concurrent config change roll back the catalogue.
        row.Revision = Guid.NewGuid();
        row.DiscoveryComplete = complete; row.DiscoveredUtc = now;
        if (tools.Count != 0 || complete)
        {
            var previous = await db.Set<McpDiscoveredTool>().Where(x => x.ConnectionId == connection).ToArrayAsync(ct);
            foreach (var tool in tools)
            {
                if (tool.ConnectionId != connection) throw new InvalidOperationException("Catalogue slot mismatch.");
            }
            db.RemoveRange(previous);
            await db.SaveChangesAsync(ct);
            db.AddRange(tools);
        }
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new McpClientException("CONFIGURATION_CHANGED"); }
        return row.Revision;
    }
    public async Task ApproveAsync(Guid account, Guid connection, Guid revision, string name, string version, bool approved, CancellationToken ct)
    {
        await using var db = new OAuthDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var row = await db.Set<McpServerDefinition>().FromSqlInterpolated($"SELECT * FROM oauth.mcp_servers WHERE \"AccountId\" = {account} AND \"Id\" = {connection} AND \"Revision\" = {revision} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw new McpClientException("CONFIGURATION_CHANGED");
        row.Revision = Guid.NewGuid();
        var tool = await db.Set<McpDiscoveredTool>().SingleOrDefaultAsync(x => x.ConnectionId == connection && x.Name == name && x.Version == version, ct)
            ?? throw new McpClientException("CONFIGURATION_CHANGED");
        tool.Approved = approved;
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new McpClientException("CONFIGURATION_CHANGED"); }
    }
}
