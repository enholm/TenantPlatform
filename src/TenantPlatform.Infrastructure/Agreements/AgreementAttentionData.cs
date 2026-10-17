using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Agreements;

public sealed class AgreementAttentionData(IDbContextFactory<TenantPlatformDbContext> factory) : IAgreementAttentionData
{
    public async Task<string> GetReminderTimeZoneAsync(Guid accountId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await AgreementReadQueries.ReminderTimeZoneAsync(db, accountId, ct);
    }

    public async Task<AgreementAttentionDto> CountAsync(AgreementListScope scope, AgreementAttentionQuery query, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var visible = AgreementReadQueries.Visible(db, scope);
        // Sequential SQL counts: never materialize agreements or share a context across parallel operations.
        return new(query.Today,
            await AgreementListQueries.Attention(visible, query.Notice).CountAsync(ct),
            await AgreementListQueries.Attention(visible, query.Renewal).CountAsync(ct),
            await AgreementListQueries.Attention(visible, query.Expired).CountAsync(ct));
    }
}
