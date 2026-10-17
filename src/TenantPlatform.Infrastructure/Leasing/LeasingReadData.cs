using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Leasing;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Leasing;
public sealed class LeasingReadData(IDbContextFactory<TenantPlatformDbContext> factory) : ILeasingReadData
{
    private static IQueryable<LeasingFramework> Frameworks(TenantPlatformDbContext db, Guid account, Guid user, bool all) => db.LeasingFrameworks.Where(x => x.AccountId == account && (all || x.OwnerUserId == user));
    private static IQueryable<LeasingAcquisition> Acquisitions(TenantPlatformDbContext db, Guid account, Guid user, bool all) => db.LeasingAcquisitions.Where(x => x.AccountId == account && (all || x.OwnerUserId == user || db.LeasingFrameworks.Any(f => f.AccountId == account && f.Id == x.FrameworkId && f.OwnerUserId == user)));
    private static IQueryable<LeasingAcquisition> ReadAcquisitions(TenantPlatformDbContext db, LeasingReadScope scope) => db.LeasingAcquisitions.Where(x => x.AccountId == scope.AccountId && (scope.AcquisitionReader || x.OwnerUserId == scope.UserId || db.LeasingFrameworks.Any(f => f.AccountId == scope.AccountId && f.Id == x.FrameworkId && f.OwnerUserId == scope.UserId) || scope.OrderApprover && db.LeasingOrderRealizations.Any(r => r.AccountId == scope.AccountId && r.AcquisitionId == x.Id)));
    public Task<bool> HasOwnedRootAsync(Guid account, Guid user, CancellationToken ct) => new LeasingClassificationData(factory).HasOwnedRootAsync(account, user, ct);
    public async Task<LeasingOptionData> ReadOptionsAsync(LeasingReadScope scope, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var account = scope.AccountId; var user = scope.UserId;
        // Only order/limit/admin grants broaden framework options, not report/payment roles.
        var canReadOrders = scope.FrameworkOptionsReader;
        var parties = await db.Organizations.AsNoTracking().Where(x => x.AccountId == account).OrderBy(x => x.Name).Select(x => new LeasingOption(x.Id, x.Name)).ToListAsync(ct);
        var owners = await db.UserAccounts.AsNoTracking().Where(x => x.AccountId == account && x.User.IsActive).OrderBy(x => x.User.FirstName).Select(x => new LeasingOption(x.UserId, x.User.FirstName + " " + x.User.LastName)).ToListAsync(ct);
        var frameworks = await db.LeasingFrameworks.AsNoTracking().Where(x => x.AccountId == account && (canReadOrders || x.OwnerUserId == user || db.LeasingOrders.Any(o => o.AccountId == account && o.FrameworkId == x.Id && o.OwnerUserId == user))).OrderBy(x => x.Name).Select(x => new LeasingOption(x.Id, x.Name)).ToListAsync(ct);
        return new(parties, owners, frameworks, await db.LeasingFrameworks.AnyAsync(x => x.AccountId == account && x.OwnerUserId == user, ct));
    }
    public async Task<string> ReadTimeZoneAsync(Guid account, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.AgreementReminderSettings.AsNoTracking().Where(x => x.AccountId == account).Select(x => x.TimeZoneId).SingleOrDefaultAsync(ct) ?? "Europe/Oslo";
    }
    public async Task<LeasingParentFacts> ReadParentAsync(Guid account, Guid id, bool framework, Guid actor, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var f = framework ? await db.LeasingFrameworks.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == id, ct) : null;
        var a = !framework ? await db.LeasingAcquisitions.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == id, ct) : null;
        var parent = a?.FrameworkId is Guid parentId ? await db.LeasingFrameworks.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == parentId, ct) : null;
        var ownedOrder = framework && await db.LeasingOrders.AnyAsync(x => x.AccountId == account && x.FrameworkId == id && x.OwnerUserId == actor, ct);
        var realized = !framework && await db.LeasingOrderRealizations.AnyAsync(x => x.AccountId == account && x.AcquisitionId == id, ct);
        return new(f, a, parent, ownedOrder, realized);
    }
    public async Task<LeasingPage> ReadListAsync(LeasingReadScope scope, LeasingListFilter filter, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var account = scope.AccountId; var user = scope.UserId; var admin = scope.AcquisitionReader; var frameworkReader = scope.FrameworkReader;
        var search = filter.Search; var kind = filter.Kind; var status = filter.Status; var owner = filter.Owner; var page = filter.Page;
        var frameworks = Frameworks(db, account, user, frameworkReader).AsNoTracking();
        var acquisitions = Acquisitions(db, account, user, admin).AsNoTracking();
        if (kind == "orders") acquisitions = acquisitions.Where(x => x.FrameworkId == null);
        if (owner.HasValue) { frameworks = frameworks.Where(x => x.OwnerUserId == owner); acquisitions = acquisitions.Where(x => x.OwnerUserId == owner); }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            frameworks = frameworks.Where(x => x.Name.ToLower().Contains(term) || x.Number.ToLower().Contains(term));
            acquisitions = acquisitions.Where(x => x.Name.ToLower().Contains(term) || x.Reference.ToLower().Contains(term));
        }
        if (!string.IsNullOrEmpty(status))
        {
            frameworks = Enum.TryParse<LeasingFrameworkStatus>(status, out var fs) && Enum.IsDefined(fs)
                ? frameworks.Where(x => x.Status == fs) : frameworks.Where(x => false);
            acquisitions = Enum.TryParse<LeasingAcquisitionStatus>(status, out var acs) && Enum.IsDefined(acs)
                ? acquisitions.Where(x => x.Status == acs) : acquisitions.Where(x => false);
        }
        if (kind is "orders" or "acquisitions") frameworks = frameworks.Where(x => false);
        if (kind == "frameworks") acquisitions = acquisitions.Where(x => false);
        // Project a common shape before paging; no financial totals from inaccessible agreements.
        var frows = frameworks.Select(x => new { x.Id, Framework = true, x.Name, Number = x.Number, Status = (int)x.Status,
            x.OwnerUserId, Start = x.AcquisitionFrom, End = x.AcquisitionTo, x.Currency, Amount = x.Limit });
        var arows = acquisitions.Select(x => new { x.Id, Framework = false, x.Name, Number = x.Reference, Status = (int)x.Status,
            x.OwnerUserId, Start = x.PurchaseDate, End = x.EndDate, x.Currency, Amount = x.GrossTotal - x.CreditNetTotal - x.CreditVatTotal - x.ReversedNetTotal - x.ReversedVatTotal });
        var query = frows.Concat(arows);
        var count = await query.CountAsync(ct);
        const int size = 25;
        page = Math.Clamp(page, 1, Math.Max(1, (count + size - 1) / size));
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var ids = rows.Select(x => x.OwnerUserId).Distinct().ToArray();
        var names = await db.UserAccounts.Where(x => x.AccountId == account && ids.Contains(x.UserId)).Select(x => new { x.UserId, Name = x.User.FirstName + " " + x.User.LastName }).ToDictionaryAsync(x => x.UserId, x => x.Name, ct);
        return new(rows.Select(x => new LeasingRow(x.Id, x.Framework, x.Name, x.Number,
            x.Framework ? ((LeasingFrameworkStatus)x.Status).ToString() : ((LeasingAcquisitionStatus)x.Status).ToString(),
            names.GetValueOrDefault(x.OwnerUserId, ""), x.Start, x.End, x.Currency, x.Amount)).ToList(), count, page, size);
    }


    public async Task<LeasingDetails> ReadDetailsAsync(LeasingReadScope scope, Guid id, bool framework, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var account = scope.AccountId; var user = scope.UserId;
        var f = framework ? await db.LeasingFrameworks.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == id && (scope.FrameworkReader || x.OwnerUserId == user || db.LeasingOrders.Any(o => o.AccountId == account && o.FrameworkId == id && o.OwnerUserId == user)), ct) ?? throw new UnauthorizedAccessException() : null;
        var a = !framework ? await ReadAcquisitions(db, scope).AsNoTracking().Where(x => x.Id == id).Include(x => x.Items).ThenInclude(x => x.DimensionSelections).Include(x => x.Items).ThenInclude(x => x.AllocationRows).Include(x => x.Items).ThenInclude(x => x.AllocationDimensions).AsSplitQuery().SingleOrDefaultAsync(ct) ?? throw new UnauthorizedAccessException() : null;
        if (a is not null) a.Items = a.Items.OrderBy(x => x.Position).ToList();
        var documents = await db.LeasingDocuments.AsNoTracking().Where(x => x.AccountId == account && (framework ? x.FrameworkId == id : x.AcquisitionId == id)).OrderByDescending(x => x.UploadedUtc).ToListAsync(ct);
        foreach (var document in documents) document.StorageKey = "";
        var names = await db.UserAccounts.AsNoTracking().Where(x => x.AccountId == account).Select(x => new { x.UserId, Name = x.User.FirstName + " " + x.User.LastName }).ToDictionaryAsync(x => x.UserId, x => x.Name, ct);
        var rawHistory = await db.LeasingHistory.AsNoTracking().Where(x => x.AccountId == account && (framework ? x.FrameworkId == id : x.AcquisitionId == id)).OrderByDescending(x => x.RecordedUtc).ToListAsync(ct);
        var history = rawHistory.Select(x => new LeasingHistoryRow(x.RecordedUtc, names.GetValueOrDefault(x.ActorUserId, ""), x.Action, x.Reason, x.BeforeJson, x.AfterJson)).ToList();
        var children = framework ? await ReadAcquisitions(db, scope).AsNoTracking().Where(x => x.FrameworkId == id).OrderBy(x => x.PurchaseDate).ThenBy(x => x.Name).ToListAsync(ct) : [];
        var parties = await db.Organizations.AsNoTracking().Where(x => x.AccountId == account).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var refs = new Dictionary<Guid,string>(parties); foreach (var member in names) refs[member.Key] = member.Value;
        foreach (var frame in await Frameworks(db, account, user, scope.Administrator).AsNoTracking().ToListAsync(ct)) refs[frame.Id] = frame.Name;
        var finance = f?.FinanceOrganizationId ?? a!.FinanceOrganizationId; var owner = f?.OwnerUserId ?? a!.OwnerUserId;
        var frameName = a?.FrameworkId is Guid frameId ? await db.LeasingFrameworks.Where(x => x.AccountId == account && x.Id == frameId).Select(x => x.Name).SingleOrDefaultAsync(ct) : null;
        decimal used = 0, reserved = 0;
        if (f is not null)
        {
            used = await db.LeasingAcquisitions.Where(x => x.AccountId == account && x.FrameworkId == id && x.Status == LeasingAcquisitionStatus.Registered).SumAsync(x => f.IncludesVat ? x.GrossTotal - x.ReleasedNetTotal - x.ReleasedVatTotal : x.NetTotal - x.ReleasedNetTotal, ct);
            var orders = await db.LeasingOrders.AsNoTracking().Where(x => x.AccountId == account && x.FrameworkId == id && x.Status == LeasingOrderStatus.Approved).Include(x => x.Lines).ToListAsync(ct);
            reserved = orders.Sum(x => LeasingOrderCalculator.Reserved(x, f.IncludesVat));
        }
        return new(f, a, parties.GetValueOrDefault(finance, ""), names.GetValueOrDefault(owner, ""), a is null ? null : parties.GetValueOrDefault(a.SupplierOrganizationId, ""), frameName, used, false, children, documents, history, refs, reserved);
    }
}
