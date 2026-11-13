using System.Text.Json;
using TenantPlatform.Core.Reports;
using TenantPlatform.Web.Services.Reports;

static class PeriodChecks
{
    public static async Task RunAsync(Fixture fixture)
    {
        ResolveEveryRule();
        ValidateBoundaries();
        ValidateContractsAndSerialization();
        ValidateBuckets();
        await ValidateLocalDateAsync(fixture);
    }

    private static void ResolveEveryRule()
    {
        var today = new DateOnly(2026, 11, 12);
        var custom = new ReportPeriodSelection(ReportPeriodRule.Custom, new(2024, 2, 29), new(2024, 3, 3));
        var cases = new (ReportPeriodSelection Selection, DateOnly From, DateOnly To)[]
        {
            (new(ReportPeriodRule.CurrentMonth), new(2026, 11, 1), new(2026, 11, 30)),
            (new(ReportPeriodRule.PreviousMonth), new(2026, 10, 1), new(2026, 10, 31)),
            (new(ReportPeriodRule.NextMonth), new(2026, 12, 1), new(2026, 12, 31)),
            (new(ReportPeriodRule.CurrentQuarter), new(2026, 10, 1), new(2026, 12, 31)),
            (new(ReportPeriodRule.PreviousQuarter), new(2026, 7, 1), new(2026, 9, 30)),
            (new(ReportPeriodRule.NextQuarter), new(2027, 1, 1), new(2027, 3, 31)),
            (new(ReportPeriodRule.CurrentTertial), new(2026, 9, 1), new(2026, 12, 31)),
            (new(ReportPeriodRule.PreviousTertial), new(2026, 5, 1), new(2026, 8, 31)),
            (new(ReportPeriodRule.NextTertial), new(2027, 1, 1), new(2027, 4, 30)),
            (new(ReportPeriodRule.CurrentHalfYear), new(2026, 7, 1), new(2026, 12, 31)),
            (new(ReportPeriodRule.PreviousHalfYear), new(2026, 1, 1), new(2026, 6, 30)),
            (new(ReportPeriodRule.NextHalfYear), new(2027, 1, 1), new(2027, 6, 30)),
            (new(ReportPeriodRule.CurrentYear), new(2026, 1, 1), new(2026, 12, 31)),
            (new(ReportPeriodRule.PreviousYear), new(2025, 1, 1), new(2025, 12, 31)),
            (new(ReportPeriodRule.NextYear), new(2027, 1, 1), new(2027, 12, 31)),
            (new(ReportPeriodRule.YearToDate), new(2026, 1, 1), today),
            (new(ReportPeriodRule.Rolling12Months), new(2025, 11, 13), today),
            (custom, new(2024, 2, 29), new(2024, 3, 3))
        };
        foreach (var item in cases)
        {
            var result = ReportPeriodResolver.Resolve(item.Selection, today);
            Check.That(result.From == item.From && result.To == item.To,
                $"period rule {item.Selection.Rule} resolves inclusive calendar boundaries");
        }
    }

    private static void ValidateBoundaries()
    {
        CheckRange(ReportPeriodRule.PreviousMonth, new(2026, 1, 15), new(2025, 12, 1), new(2025, 12, 31),
            "previous month crosses year boundary");
        CheckRange(ReportPeriodRule.NextMonth, new(2026, 12, 15), new(2027, 1, 1), new(2027, 1, 31),
            "next month crosses year boundary");
        CheckRange(ReportPeriodRule.CurrentMonth, new(2024, 2, 29), new(2024, 2, 1), new(2024, 2, 29),
            "current month includes leap day");
        CheckRange(ReportPeriodRule.CurrentMonth, new(2025, 2, 14), new(2025, 2, 1), new(2025, 2, 28),
            "current month uses non-leap February boundary");
        CheckRange(ReportPeriodRule.PreviousQuarter, new(2026, 1, 1), new(2025, 10, 1), new(2025, 12, 31),
            "previous quarter crosses year boundary");
        CheckRange(ReportPeriodRule.CurrentQuarter, new(2026, 4, 1), new(2026, 4, 1), new(2026, 6, 30),
            "quarter boundary starts on April first");
        CheckRange(ReportPeriodRule.CurrentTertial, new(2026, 5, 1), new(2026, 5, 1), new(2026, 8, 31),
            "tertial boundary starts on May first");
        CheckRange(ReportPeriodRule.CurrentTertial, new(2026, 9, 1), new(2026, 9, 1), new(2026, 12, 31),
            "third tertial starts on September first");
        CheckRange(ReportPeriodRule.CurrentHalfYear, new(2026, 7, 1), new(2026, 7, 1), new(2026, 12, 31),
            "second half-year starts on July first");
        CheckRange(ReportPeriodRule.YearToDate, new(2024, 2, 29), new(2024, 1, 1), new(2024, 2, 29),
            "year to date includes leap day and stops at today");
        CheckRange(ReportPeriodRule.Rolling12Months, new(2024, 2, 29), new(2023, 3, 1), new(2024, 2, 29),
            "rolling twelve months follows the specified inclusive leap-day rule");
    }

    private static void ValidateContractsAndSerialization()
    {
        foreach (var invalid in new[]
        {
            new ReportPeriodSelection(ReportPeriodRule.Custom),
            new ReportPeriodSelection(ReportPeriodRule.Custom, new(2026, 1, 1), null),
            new ReportPeriodSelection(ReportPeriodRule.Custom, null, new(2026, 1, 1)),
            new ReportPeriodSelection(ReportPeriodRule.Custom, new(2026, 2, 1), new(2026, 1, 31)),
            new ReportPeriodSelection(ReportPeriodRule.CurrentMonth, new(2026, 1, 1), new(2026, 1, 31))
        })
            Check.Throws<ReportPeriodValidationException>(() => invalid.Validate(),
                "invalid custom/relative date combination is rejected", "ReportPeriodInvalid");

        Check.Throws<ReportPeriodValidationException>(() =>
            ReportPeriodResolver.Resolve(new((ReportPeriodRule)999), new(2026, 1, 1)),
            "unknown period enum is rejected", "ReportPeriodInvalid");
        Check.Throws<ReportPeriodValidationException>(() => ReportTimeBuckets.Validate((ReportTimeGrouping)999),
            "unknown time-grouping enum is rejected", "ReportTimeGroupingInvalid");

        var relative = new ReportPeriodSelection(ReportPeriodRule.CurrentQuarter);
        var relativeJson = JsonSerializer.Serialize(relative);
        var relativeCopy = JsonSerializer.Deserialize<ReportPeriodSelection>(relativeJson)!;
        Check.That(relativeCopy == relative && relativeCopy.CustomFrom is null && relativeCopy.CustomTo is null,
            "relative period serializes as its rule without resolved dates");
        Check.That(ReportPeriodResolver.Resolve(relativeCopy, new(2026, 2, 1)) !=
            ReportPeriodResolver.Resolve(relativeCopy, new(2027, 2, 1)),
            "deserialized relative rule resolves again against the supplied today");

        var custom = new ReportPeriodSelection(ReportPeriodRule.Custom, new(2026, 3, 4), new(2026, 5, 6));
        var customCopy = JsonSerializer.Deserialize<ReportPeriodSelection>(JsonSerializer.Serialize(custom))!;
        Check.That(customCopy == custom && ReportPeriodResolver.Resolve(customCopy, new(2030, 1, 1)) == new ReportDateRange(new(2026, 3, 4), new(2026, 5, 6)),
            "custom period roundtrip preserves its own dates independently of today");

        var unknown = JsonSerializer.Deserialize<ReportPeriodSelection>("{\"Rule\":999,\"CustomFrom\":null,\"CustomTo\":null}")!;
        Check.Throws<ReportPeriodValidationException>(() => ReportPeriodResolver.Resolve(unknown, new(2026, 1, 1)),
            "unknown deserialized enum value is rejected", "ReportPeriodInvalid");

        var monthly = new ReportTemporalSelection(new(ReportPeriodRule.CurrentYear), ReportTimeGrouping.Month);
        var quarterly = monthly with { TimeGrouping = ReportTimeGrouping.Quarter };
        monthly.Validate(); quarterly.Validate();
        var period = ReportPeriodResolver.Resolve(monthly.Period, new(2026, 6, 1));
        Check.That(ReportTimeBuckets.Create(monthly.TimeGrouping!.Value, period).Count == 12 &&
            ReportTimeBuckets.Create(quarterly.TimeGrouping!.Value, period).Count == 4,
            "report period and time grouping remain independent choices");
        new ReportTemporalSelection(new(ReportPeriodRule.Custom, new(2026, 1, 1), new(2026, 1, 2))).Validate();
        Check.That(true, "time grouping remains optional for a valid period contract");
    }

    private static void ValidateBuckets()
    {
        var period = new ReportDateRange(new(2025, 12, 15), new(2026, 2, 10));
        var months = ReportTimeBuckets.Create(ReportTimeGrouping.Month, period);
        Check.That(months.Count == 3 && months[0].Key.MachineKey == "month:2025:12" &&
            months[1].Key.MachineKey == "month:2026:01" && months[2].Key.MachineKey == "month:2026:02",
            "monthly bucket identities are stable and chronological across years");
        Check.That(months[0].FullFrom == new DateOnly(2025, 12, 1) && months[0].FullTo == new DateOnly(2025, 12, 31) &&
            months[0].OverlapFrom == period.From && months[0].OverlapTo == new DateOnly(2025, 12, 31) && months[0].IsPartial,
            "first partial bucket retains full calendar identity and separate overlap");
        Check.That(months[1].FullFrom == months[1].OverlapFrom && months[1].FullTo == months[1].OverlapTo && !months[1].IsPartial,
            "fully covered middle bucket is identified as complete");
        Check.That(months[2].FullFrom == new DateOnly(2026, 2, 1) && months[2].FullTo == new DateOnly(2026, 2, 28) &&
            months[2].OverlapTo == period.To && months[2].IsPartial,
            "last partial bucket retains full calendar identity and separate overlap");
        Check.That(months.Select(x => x.Key).SequenceEqual(months.Select(x => x.Key).Order()),
            "bucket key comparison preserves chronology across year boundary");

        var quarters = ReportTimeBuckets.Create(ReportTimeGrouping.Quarter, period);
        Check.That(quarters.Count == 2 && quarters[0].Key == new ReportTimeBucketKey(ReportTimeGrouping.Quarter, 2025, 4) &&
            quarters[1].Key == new ReportTimeBucketKey(ReportTimeGrouping.Quarter, 2026, 1),
            "quarter buckets keep year and bucket number in machine identity");
        var tertial = ReportTimeBuckets.Full(ReportTimeGrouping.Tertial, new(2026, 8, 31));
        Check.That(tertial.Key.Bucket == 2 && tertial.FullFrom == new DateOnly(2026, 5, 1) && tertial.FullTo == new DateOnly(2026, 8, 31),
            "tertial bucket uses May through August boundaries");
        var halfYear = ReportTimeBuckets.Full(ReportTimeGrouping.HalfYear, new(2026, 12, 31));
        Check.That(halfYear.Key.Bucket == 2 && halfYear.FullFrom == new DateOnly(2026, 7, 1) && halfYear.FullTo == new DateOnly(2026, 12, 31),
            "half-year bucket uses July through December boundaries");
        var year = ReportTimeBuckets.Full(ReportTimeGrouping.Year, new(2024, 2, 29));
        Check.That(year.Key.MachineKey == "year:2024:01" && year.FullFrom == new DateOnly(2024, 1, 1) && year.FullTo == new DateOnly(2024, 12, 31),
            "year bucket has stable identity and leap-year calendar bounds");
        var leapMonth = ReportTimeBuckets.Full(ReportTimeGrouping.Month, new(2024, 2, 29));
        Check.That(leapMonth.FullTo == new DateOnly(2024, 2, 29), "month bucket includes leap day");

        var shortJanuary = ReportTimeBuckets.Create(ReportTimeGrouping.Month,
            new(new(2026, 1, 10), new(2026, 1, 20))).Single();
        var longJanuary = ReportTimeBuckets.Create(ReportTimeGrouping.Month,
            new(new(2026, 1, 1), new(2026, 1, 31))).Single();
        Check.That(shortJanuary.Key == longJanuary.Key && shortJanuary.FullFrom == longJanuary.FullFrom &&
            shortJanuary.FullTo == longJanuary.FullTo && shortJanuary.OverlapFrom != longJanuary.OverlapFrom,
            "partial overlap never changes a bucket's machine identity or calendar bounds");
    }

    private static async Task ValidateLocalDateAsync(Fixture fixture)
    {
        var fallback = await fixture.LocalDate(fixture.Admin, new(2026, 1, 31, 23, 30, 0, TimeSpan.Zero))
            .GetAsync(fixture.Account);
        Check.That(fallback.TimeZoneId == "Europe/Oslo" && fallback.Today == new DateOnly(2026, 2, 1),
            "report local date reuses Europe/Oslo fallback around UTC midnight");

        await fixture.SetTimeZoneAsync("UTC");
        var utc = await fixture.LocalDate(fixture.Admin, new(2026, 1, 31, 23, 30, 0, TimeSpan.Zero))
            .GetAsync(fixture.Account);
        Check.That(utc.TimeZoneId == "UTC" && utc.Today == new DateOnly(2026, 1, 31),
            "report local date reuses the stored account time zone");

        await fixture.SetTimeZoneAsync("Europe/Oslo");
        var beforeDstJump = await fixture.LocalDate(fixture.Admin, new(2026, 3, 28, 23, 30, 0, TimeSpan.Zero))
            .GetAsync(fixture.Account);
        var summerMidnight = await fixture.LocalDate(fixture.Admin, new(2026, 6, 30, 22, 30, 0, TimeSpan.Zero))
            .GetAsync(fixture.Account);
        Check.That(beforeDstJump.Today == new DateOnly(2026, 3, 29) && summerMidnight.Today == new DateOnly(2026, 7, 1),
            "fixed TimeProvider resolves local dates across DST and summer UTC-midnight offsets");

        await Check.ThrowsAsync<UnauthorizedAccessException>(() => fixture.LocalDate(fixture.Admin,
            new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), fixture.ForeignAccount).GetAsync(fixture.ForeignAccount),
            "report local date rejects selected account without active membership");
    }

    private static void CheckRange(ReportPeriodRule rule, DateOnly today, DateOnly from, DateOnly to, string message)
    {
        var result = ReportPeriodResolver.Resolve(new(rule), today);
        Check.That(result.From == from && result.To == to, message);
    }
}
