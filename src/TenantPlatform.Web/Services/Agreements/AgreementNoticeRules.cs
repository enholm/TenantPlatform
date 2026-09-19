using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Web.Services.Agreements;

public static class AgreementNoticeRules
{
    public static DateOnly Calculate(DateOnly date, int? count, AgreementNoticeUnit? unit, bool before)
    {
        if (!count.HasValue || !unit.HasValue) throw new AgreementValidationException("NoticeInvalidDuration");
        try { return AgreementNoticeCalculator.Calculate(date, count.Value, unit.Value, before); }
        catch (ArgumentOutOfRangeException) { throw new AgreementValidationException("NoticeInvalidDuration"); }
    }

    // Normalizes only fields made irrelevant by the selected form; no client-computed date is trusted.
    public static void Apply(Agreement a, SaveAgreementRequest r)
    {
        if (!Enum.IsDefined(r.Form) || !Enum.IsDefined(r.NoticeMode) ||
            (r.NoticeUnit.HasValue && !Enum.IsDefined(r.NoticeUnit.Value)))
            throw new AgreementValidationException("NoticeInvalidRule");
        if (a.TerminationEffectiveDate.HasValue && r.Form != a.Form)
            throw new AgreementValidationException("NoticeWithdrawBeforeFormChange");
        a.Form = r.Form; a.NoticeMode = r.NoticeMode;
        a.NoticeCount = r.NoticeCount; a.NoticeUnit = r.NoticeUnit;
        if (a.Form == AgreementForm.Legacy)
        {
            // Compatibility for ambiguous migrated records; keep all original dates.
            a.NoticeMode = a.NoticeDeadline.HasValue ? AgreementNoticeMode.Manual : AgreementNoticeMode.None;
            a.NoticeCount = null; a.NoticeUnit = null;
        }
        else if (a.Form == AgreementForm.Ongoing)
        {
            a.EndDate = null; a.RenewalDate = null; a.AutoRenew = false; a.RenewalMonths = null;
            a.NoticeMode = AgreementNoticeMode.None; a.NoticeDeadline = null;
            ValidateDuration(a.NoticeCount, a.NoticeUnit);
        }
        else
        {
            if (a.Form == AgreementForm.FixedTerm)
            {
                if (!a.EndDate.HasValue) throw new AgreementValidationException("NoticeEndRequired");
                a.RenewalDate = null; a.AutoRenew = false; a.RenewalMonths = null;
                if (a.NoticeMode == AgreementNoticeMode.BeforeRenewal)
                    throw new AgreementValidationException("NoticeInvalidRule");
            }
            else if (!a.RenewalDate.HasValue) throw new AgreementValidationException("NoticeRenewalRequired");
            if (a.NoticeMode != AgreementNoticeMode.BeforeRenewal) { a.NoticeCount = null; a.NoticeUnit = null; }
            if (a.NoticeMode == AgreementNoticeMode.Manual && !a.NoticeDeadline.HasValue)
                throw new AgreementValidationException("NoticeManualRequired");
        }
        Recalculate(a);
    }

    public static void ValidateDuration(int? count, AgreementNoticeUnit? unit)
    {
        // At least one representable DateOnly interval must exist (no decimal or unbounded durations).
        if (count is null or <= 0 || unit is null || !Enum.IsDefined(unit.Value) ||
            count > (unit == AgreementNoticeUnit.Months ? 119987 : 3652058))
            throw new AgreementValidationException("NoticeInvalidDuration");
    }

    public static void Recalculate(Agreement a)
    {
        if (a.Form == AgreementForm.Legacy) return;
        if (a.Form == AgreementForm.Ongoing) a.NoticeDeadline = null;
        else if (a.NoticeMode == AgreementNoticeMode.BeforeRenewal)
            a.NoticeDeadline = Calculate(a.RenewalDate ?? throw new AgreementValidationException("NoticeRenewalRequired"),
                a.NoticeCount, a.NoticeUnit, true);
        else if (a.NoticeMode == AgreementNoticeMode.None) a.NoticeDeadline = null;
        // A registration's original duration is authoritative, not the agreement's current duration.
        a.CessationDate = a.TerminationEffectiveDate.HasValue
            ? Calculate(a.TerminationEffectiveDate.Value, a.TerminationNoticeCount, a.TerminationNoticeUnit, false) : null;
    }
}
