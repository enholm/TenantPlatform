using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Web.Services.Dimensions;

static class ConfigurationChecks
{
    public static async Task Run()
    {
        var f = new Fixture(); await f.Seed();
        var service = f.Service();
        var initial = await service.GetAgreementDimensionRulesAsync(f.Account);
        Check.That(initial.Count == 2 && initial.All(x => !x.IsEnabled && !x.IsRequired && x.Revision == Guid.Empty), "rules default disabled and exclude foreign dimensions");
        await using var baseline = f.Open();
        var leasing = Fixture.Json(await baseline.LeasingDimensionRules.AsNoTracking().SingleAsync());
        await f.Rule(f.Single, true, true);
        var rule = (await service.GetAgreementDimensionRulesAsync(f.Account)).Single(x => x.DimensionId == f.Single);
        Check.That(rule.IsEnabled && rule.IsRequired && rule.Revision != Guid.Empty, "required is persisted with availability and revision");
        await Check.Error<DimensionValidationException>(() => f.RuleInput(new() { DimensionId = f.Single, IsEnabled = true, Revision = Guid.Empty }), "stale configuration revision rejected", "DimensionConcurrency");
        await f.RuleInput(new() { DimensionId = f.Single, IsEnabled = false, IsRequired = true, Revision = rule.Revision });
        var disabled = (await service.GetAgreementDimensionRulesAsync(f.Account)).Single(x => x.DimensionId == f.Single);
        Check.That(!disabled.IsEnabled && !disabled.IsRequired, "disabling availability clears required even for direct service input");
        await using (var db = f.Open())
        {
            Check.That(Fixture.Json(await db.LeasingDimensionRules.AsNoTracking().SingleAsync()) == leasing, "Agreements configuration never creates or changes Leasing rules");
            var history = await db.DimensionHistory.Where(x => x.AccountId == f.Account && x.DimensionId == f.Single).OrderBy(x => x.Id).ToListAsync();
            Check.That(history.Count == 2 && history.All(x => x.ActorUserId == f.Admin && x.RecordedUtc == Fixture.Now && x.AfterJson.Contains("\"Module\":\"Agreements\"")), "rule history records module, account, actor and time");
            Check.That(await db.AuditLogs.AnyAsync(x => x.EntityType == nameof(AgreementDimensionRule) && x.AccountId == f.Account && x.Action == "Modified"), "configuration flows through production auditing");
        }
        await Check.Error<UnauthorizedAccessException>(() => f.RuleInput(new() { AccountId = f.ForeignAccount, DimensionId = f.Single }), "forged rule account rejected");
        await Check.Error<UnauthorizedAccessException>(() => f.RuleInput(new() { DimensionId = f.Foreign }), "foreign dimension rule rejected");
        await Check.Error<UnauthorizedAccessException>(() => f.RuleInput(new() { DimensionId = Guid.NewGuid() }), "nonexistent dimension rule rejected");
        await Check.Error<UnauthorizedAccessException>(() => f.Service(f.Owner).GetAgreementDimensionRulesAsync(f.Account), "owner cannot read administrative rule catalog");
        await Check.Error<UnauthorizedAccessException>(() => f.Service(f.Owner).SaveAgreementDimensionRuleAsync(f.Account, new() { DimensionId = f.Single }), "public rule write denies non-admin before relational operations");
        await Check.Error<UnauthorizedAccessException>(() => f.Service(f.Owner, platformAdmin: true).GetAgreementDimensionRulesAsync(f.Account), "platform-admin claim alone grants no account structure permission");
        await Check.Error<UnauthorizedAccessException>(() => f.Rule(f.Multi, user: f.Owner), "configuration core retains administrative checks");
    }
}
