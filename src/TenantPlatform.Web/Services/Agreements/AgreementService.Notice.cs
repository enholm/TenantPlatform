using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Services.Agreements;

public record AgreementNoticeSnapshot
{
    public AgreementForm Form { get; init; }
    public AgreementNoticeMode NoticeMode { get; init; }
    public int? NoticeCount { get; init; }
    public AgreementNoticeUnit? NoticeUnit { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly PeriodStartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public DateOnly? RenewalDate { get; init; }
    public DateOnly? NoticeDeadline { get; init; }
    public bool AutoRenew { get; init; }
    public int? RenewalMonths { get; init; }
    public string? Terms { get; init; }
    public DateOnly? EffectiveDate { get; init; }
    public int? RegisteredCount { get; init; }
    public AgreementNoticeUnit? RegisteredUnit { get; init; }
    public DateOnly? CessationDate { get; init; }
    public DateTimeOffset? RegisteredUtc { get; init; }
    public Guid? RegisteredByUserId { get; init; }

    public static AgreementNoticeSnapshot From(Agreement a) => new()
    {
        Form = a.Form, NoticeMode = a.NoticeMode, NoticeCount = a.NoticeCount, NoticeUnit = a.NoticeUnit,
        StartDate = a.StartDate, PeriodStartDate = a.CurrentPeriodStartDate, EndDate = a.EndDate,
        RenewalDate = a.RenewalDate, NoticeDeadline = a.NoticeDeadline, AutoRenew = a.AutoRenew,
        RenewalMonths = a.RenewalMonths, Terms = a.Terms, EffectiveDate = a.TerminationEffectiveDate,
        RegisteredCount = a.TerminationNoticeCount, RegisteredUnit = a.TerminationNoticeUnit,
        CessationDate = a.CessationDate, RegisteredUtc = a.TerminationRegisteredUtc,
        RegisteredByUserId = a.TerminationRegisteredByUserId
    };
}

public record AgreementNoticeHistoryDto(AgreementNoticeAction Action, DateTimeOffset CreatedUtc, string Actor,
    string? Comment, AgreementNoticeSnapshot Before, AgreementNoticeSnapshot After);
public class AgreementTerminationRequest
{
    public Guid Revision { get; set; }
    public AgreementNoticeAction Action { get; set; } = AgreementNoticeAction.Registered;
    public DateOnly EffectiveDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string? Comment { get; set; }
}

public partial class AgreementService
{
    private void NoticeHistory(TenantPlatformDbContext db, Agreement a, AgreementNoticeSnapshot before,
        AgreementNoticeAction action, string? comment = null)
    {
        var after = AgreementNoticeSnapshot.From(a);
        if (before == after && action == AgreementNoticeAction.RuleChanged) return;
        db.AgreementNoticeHistory.Add(new AgreementNoticeHistory
        {
            Id = Guid.NewGuid(), AccountId = a.AccountId, AgreementId = a.Id, Action = action,
            CreatedUtc = Clock.GetUtcNow(), ActorUserId = userContext.Current.UserId, Comment = comment?.Trim(),
            BeforeJson = JsonSerializer.Serialize(before), AfterJson = JsonSerializer.Serialize(after)
        });
    }

    private async Task LoadNoticeAsync(TenantPlatformDbContext db, Agreement a, AgreementDetailsDto details, CancellationToken ct)
    {
        details.NoticeSnapshot = AgreementNoticeSnapshot.From(a);
        details.TerminationRegisteredByName = await db.Users.Where(u => u.Id == a.TerminationRegisteredByUserId)
            .Select(u => u.FirstName + " " + u.LastName).SingleOrDefaultAsync(ct);
        var rows = await (from h in db.AgreementNoticeHistory.AsNoTracking()
                          join u in db.Users on h.ActorUserId equals u.Id
                          where h.AccountId == a.AccountId && h.AgreementId == a.Id
                          orderby h.CreatedUtc descending, h.Id
                          select new { History = h, Actor = u.FirstName + " " + u.LastName }).ToListAsync(ct);
        details.NoticeHistory = rows.Select(x => new AgreementNoticeHistoryDto(x.History.Action, x.History.CreatedUtc,
            x.Actor, x.History.Comment, JsonSerializer.Deserialize<AgreementNoticeSnapshot>(x.History.BeforeJson)!,
            JsonSerializer.Deserialize<AgreementNoticeSnapshot>(x.History.AfterJson)!)).ToList();
    }

    public async Task RecordTerminationAsync(Guid accountId, Guid agreementId, AgreementTerminationRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var (a, _, _) = await RequireAsync(db, accountId, agreementId, true, false, cancellationToken);
        CheckRevision(a, request.Revision);
        if (a.Form != AgreementForm.Ongoing || request.Comment?.Length > 2000)
            throw new AgreementValidationException("NoticeInvalidRule");
        var before = AgreementNoticeSnapshot.From(a);
        switch (request.Action)
        {
            case AgreementNoticeAction.Registered:
                if (a.TerminationEffectiveDate.HasValue) throw new AgreementValidationException("NoticeAlreadyRegistered");
                a.TerminationNoticeCount = a.NoticeCount; a.TerminationNoticeUnit = a.NoticeUnit;
                a.TerminationRegisteredUtc = Clock.GetUtcNow(); a.TerminationRegisteredByUserId = userContext.Current.UserId;
                a.TerminationEffectiveDate = request.EffectiveDate;
                break;
            case AgreementNoticeAction.Corrected:
                if (!a.TerminationEffectiveDate.HasValue) throw new AgreementValidationException("NoticeNotRegistered");
                if (string.IsNullOrWhiteSpace(request.Comment)) throw new AgreementValidationException("NoticeReasonRequired");
                a.TerminationEffectiveDate = request.EffectiveDate;
                break;
            case AgreementNoticeAction.Withdrawn:
                if (!a.TerminationEffectiveDate.HasValue) throw new AgreementValidationException("NoticeNotRegistered");
                a.TerminationEffectiveDate = null; a.TerminationNoticeCount = null; a.TerminationNoticeUnit = null;
                a.TerminationRegisteredUtc = null; a.TerminationRegisteredByUserId = null;
                break;
            default: throw new AgreementValidationException("NoticeInvalidRule");
        }
        if (a.TerminationEffectiveDate == default(DateOnly)) throw new AgreementValidationException("AgreementInvalidDates");
        AgreementNoticeRules.Recalculate(a);
        NoticeHistory(db, a, before, request.Action, request.Comment);
        Touch(a, userContext.Current.UserId);
        await AgreementDeadlineSynchronizer.SynchronizeAsync(db, a, Clock.GetUtcNow(), userContext.Current.UserId, cancellationToken);
        await SaveAsync(db, cancellationToken);
    }
}
