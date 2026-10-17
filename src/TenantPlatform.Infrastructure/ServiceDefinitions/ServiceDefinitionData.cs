using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.ServiceDefinitions;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Services;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.ServiceDefinitions;

public sealed class ServiceDefinitionData(IDbContextFactory<TenantPlatformDbContext> factory) : IServiceDefinitionData
{
    public async Task<ServiceDefinitionState> ReadBookableAsync(Guid account, Guid? definition, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await Load(db, account, definition, false, ct, true);
    }
    public async Task<ServiceDefinitionState> ReadAsync(Guid account, Guid? definition, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await Load(db, account, definition, false, ct);
    }
    public async Task<IReadOnlyList<ServiceDefinitionUsage>> ReadUsageAsync(Guid account, Guid definition, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ServiceRequests.AsNoTracking().Where(x => x.AccountId == account && x.ServiceDefinitionId == definition)
            .Select(x => new ServiceDefinitionUsage(x.ServiceDefinitionId, x.AssignedServiceProviderOrganizationId, x.Status)).ToListAsync(ct);
    }
    public async Task<List<Organization>> ReadOrganizationsAsync(Guid account, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Organizations.AsNoTracking().Where(x => x.AccountId == account).ToListAsync(ct);
    }
    private static async Task<ServiceDefinitionState> Load(TenantPlatformDbContext db, Guid account, Guid? id, bool write, CancellationToken ct, bool bookable = false)
    {
        if (!write) db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        var language = await db.Accounts.Where(x => x.Id == account).Select(x => x.DefaultLanguage).SingleAsync(ct);
        var definitions = await db.ServiceDefinitions.Where(x => x.AccountId == account && (!id.HasValue || x.Id == id) && (!bookable || x.IsActive && x.IsBookableByTenant)).ToListAsync(ct);
        var ids = definitions.Select(x => x.Id).ToArray();
        var translations = await db.ServiceDefinitionTranslations.Where(x => ids.Contains(x.ServiceDefinitionId)).ToListAsync(ct);
        var fields = await db.ServiceDefinitionFields.Where(x => ids.Contains(x.ServiceDefinitionId)).ToListAsync(ct);
        var fieldIds = fields.Select(x => x.Id).ToArray();
        var fieldTranslations = await db.ServiceDefinitionFieldTranslations.Where(x => fieldIds.Contains(x.ServiceDefinitionFieldId)).ToListAsync(ct);
        var providers = await db.ServiceDefinitionProviders.Where(x => x.AccountId == account && ids.Contains(x.ServiceDefinitionId)).ToListAsync(ct);
        var organizations = await db.Organizations.AsNoTracking().Where(x => x.AccountId == account).ToListAsync(ct);
        var usage = write ? await db.ServiceRequests.AsNoTracking().Where(x => x.AccountId == account && ids.Contains(x.ServiceDefinitionId))
            .Select(x => new ServiceDefinitionUsage(x.ServiceDefinitionId, x.AssignedServiceProviderOrganizationId, x.Status)).ToListAsync(ct) : [];
        var usedFields = write ? await (from value in db.ServiceRequestFieldValues.AsNoTracking()
            join request in db.ServiceRequests on value.ServiceRequestId equals request.Id
            where request.AccountId == account && fieldIds.Contains(value.ServiceDefinitionFieldId)
            select value.ServiceDefinitionFieldId).Distinct().ToListAsync(ct) : [];
        return new(language, definitions, translations, fields, fieldTranslations, providers, organizations, usage, usedFields);
    }
    public async Task<IServiceDefinitionTransaction> BeginAsync(Guid account, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct); IDbContextTransaction? tx = null;
        try
        {
            tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
            return new DefinitionTransaction(db, tx, account, await Load(db, account, null, true, ct));
        }
        catch { if (tx is not null) await tx.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    private sealed class DefinitionTransaction : IServiceDefinitionTransaction
    {
        private readonly TenantPlatformDbContext db;
        private readonly IDbContextTransaction tx;
        private readonly Guid account;
        private readonly List<ServiceDefinition> originalDefinitions;
        private readonly List<ServiceDefinitionTranslation> originalTranslations;
        private readonly List<ServiceDefinitionField> originalFields;
        private readonly List<ServiceDefinitionFieldTranslation> originalFieldTranslations;
        private readonly List<ServiceDefinitionProvider> originalProviders;
        public ServiceDefinitionState State { get; }
        public DefinitionTransaction(TenantPlatformDbContext db, IDbContextTransaction tx, Guid account, ServiceDefinitionState state)
        {
            this.db = db; this.tx = tx; this.account = account; State = state;
            originalDefinitions = state.Definitions.ToList(); originalTranslations = state.Translations.ToList(); originalFields = state.Fields.ToList();
            originalFieldTranslations = state.FieldTranslations.ToList(); originalProviders = state.Providers.ToList();
        }
        // Technical change tracking for this one service-definition graph; no rule decisions.
        private static void Track<T>(DbSet<T> set, List<T> before, List<T> after) where T : class
        { set.RemoveRange(before.Except(after)); set.AddRange(after.Except(before)); }
        public async Task CommitAsync(CancellationToken ct)
        {
            if (State.Definitions.Any(x => x.AccountId != account) || State.Providers.Any(x => x.AccountId != account)) throw new UnauthorizedAccessException();
            var definitions = State.Definitions.Select(x => x.Id).ToHashSet();
            if (State.Fields.Any(x => !definitions.Contains(x.ServiceDefinitionId)) && !originalDefinitions.Except(State.Definitions).Any()) throw new UnauthorizedAccessException();
            Track(db.ServiceDefinitionTranslations, originalTranslations, State.Translations);
            Track(db.ServiceDefinitionFieldTranslations, originalFieldTranslations, State.FieldTranslations);
            Track(db.ServiceDefinitionFields, originalFields, State.Fields);
            Track(db.ServiceDefinitionProviders, originalProviders, State.Providers);
            Track(db.ServiceDefinitions, originalDefinitions, State.Definitions);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync() { try { await tx.DisposeAsync(); } finally { await db.DisposeAsync(); } }
    }
}
