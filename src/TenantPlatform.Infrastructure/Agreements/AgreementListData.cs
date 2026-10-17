using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Agreements;

public sealed class AgreementListData(IDbContextFactory<TenantPlatformDbContext> factory) : IAgreementListData
{
    public async Task<string> GetReminderTimeZoneAsync(Guid accountId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await AgreementReadQueries.ReminderTimeZoneAsync(db, accountId, ct);
    }

    public async Task<AgreementPageDto> QueryAsync(AgreementListScope scope, AgreementListQuery queryFilter,
        AgreementAttentionCriteria? attention, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var visible = AgreementReadQueries.Visible(db, scope);
        if (attention is not null) visible = AgreementListQueries.Attention(visible, attention);
        var owners = await (from a in visible join u in db.Users on a.OwnerUserId equals u.Id
                            select new { u.Id, Name = u.FirstName + " " + u.LastName })
            .Distinct().OrderBy(x => x.Name)
            .Select(x => new AgreementOptionDto(x.Id, x.Name, null)).ToListAsync(cancellationToken);
        var query = from a in visible
                    join org in db.Organizations on a.CounterpartyOrganizationId equals org.Id
                    join owner in db.Users on a.OwnerUserId equals owner.Id
                    where org.AccountId == scope.AccountId
                    select new { Agreement = a, Counterparty = org.Name, Owner = owner.FirstName + " " + owner.LastName };
        if (!string.IsNullOrWhiteSpace(queryFilter.Search))
        {
            var search = queryFilter.Search.Trim().ToLowerInvariant();
            query = query.Where(x => x.Agreement.Title.ToLower().Contains(search) || x.Counterparty.ToLower().Contains(search));
        }
        if (queryFilter.Status.HasValue) query = query.Where(x => x.Agreement.Status == queryFilter.Status.Value);
        if (queryFilter.Type.HasValue) query = query.Where(x => x.Agreement.Type == queryFilter.Type.Value);
        if (queryFilter.OwnerUserId.HasValue) query = query.Where(x => x.Agreement.OwnerUserId == queryFilter.OwnerUserId.Value);
        const int pageSize = 25;
        var count = await query.CountAsync(cancellationToken);
        var page = Math.Clamp(queryFilter.Page, 1, Math.Max(1, (count + pageSize - 1) / pageSize));
        var items = await query.OrderBy(x => x.Agreement.Title).ThenBy(x => x.Agreement.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AgreementListItemDto(x.Agreement.Id, x.Agreement.Title, x.Counterparty, x.Agreement.Type,
                x.Agreement.Status, x.Agreement.OwnerUserId, x.Owner, x.Agreement.EndDate, x.Agreement.NoticeDeadline, x.Agreement.IsArchived))
            .ToListAsync(cancellationToken);
        return new(items, count, page, pageSize, owners);
    }
}
