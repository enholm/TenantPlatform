using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Web.Services.Agreements;

public static class AgreementReminderSchedule
{
    public static int[] ValidateDays(int[]? days)
    {
        if (days is null || days.Length == 0 || days.Length > 20 || days.Any(x => x < 0 || x > 36500) || days.Distinct().Count() != days.Length)
            throw new AgreementValidationException("FollowupInvalidDays");
        return days.OrderDescending().ToArray();
    }
    public static int[] ParseDays(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        var days = new List<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var day)) throw new AgreementValidationException("FollowupInvalidDays");
            days.Add(day);
        }
        return ValidateDays(days.ToArray());
    }
    public static TimeZoneInfo Zone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { throw new AgreementValidationException("FollowupInvalidTimeZone"); }
    }
    public static DateOnly Today(DateTimeOffset now, string zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Zone(zone)).DateTime);
    public static DateTimeOffset Scheduled(DateOnly due, int days, string zone, TimeOnly time)
    {
        var tz = Zone(zone);
        // Clamp ancient dates so valid DateOnly values never overflow.
        var date = DateOnly.FromDayNumber(Math.Max(0, due.DayNumber - days));
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        // A missing DST wall time is postponed to the next valid minute.
        while (tz.IsInvalidTime(local)) local = local.AddMinutes(1);
        // Ambiguous times use the later UTC occurrence, never the earlier one.
        var offset = tz.IsAmbiguousTime(local) ? tz.GetAmbiguousTimeOffsets(local).Min() : tz.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
    public static int[] EffectiveDays(Agreement a, AgreementReminderSettings settings) =>
        a.ReminderMode == AgreementReminderMode.Custom ? a.ReminderDays : settings.Days;
}

