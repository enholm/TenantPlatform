using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Localization;
using TenantPlatform.Core.Organizations;

namespace TenantPlatform.Infrastructure.Initialization;

// Pure candidate generation. No clock, database, identity lookup, or reference-file dependency.
public static class MagidaDemoData
{
    public static MagidaDemoGraph Create(Guid userId, DateTimeOffset recordedUtc)
    {
        var accountId = SeedIds.MagidaAccount;
        var geographyDimension = SeedIds.MagidaDimension(3);
        var departmentDimension = SeedIds.MagidaDimension(4);
        var graph = new MagidaDemoGraph();
        foreach (var (id, code, nb, en, order, number) in new[]
        {
            (geographyDimension, "GEO", "Geografi", "Geography", 30, 1),
            (departmentDimension, "DEPT", "Avdeling", "Department", 40, 2)
        })
        {
            graph.Dimensions.Add(new Dimension { Id = id, AccountId = accountId, Code = code, Name = nb,
                IsActive = true, AllowMultiple = false, LeafOnly = false, SortOrder = order, Revision = id });
            graph.Translations.Add(new DimensionTranslation { Id = SeedIds.MagidaDimensionTranslation(number * 2 - 1),
                AccountId = accountId, DimensionId = id, LanguageCode = SupportedLanguages.NbNo, Name = nb });
            graph.Translations.Add(new DimensionTranslation { Id = SeedIds.MagidaDimensionTranslation(number * 2),
                AccountId = accountId, DimensionId = id, LanguageCode = SupportedLanguages.EnGb, Name = en });
            graph.Rules.Add(new AgreementDimensionRule { AccountId = accountId, DimensionId = id,
                IsEnabled = true, IsRequired = false, Revision = id });
        }
        // Equal SortOrder (0) preserves the former alphabetical sibling display, rather than generation order.
        string[] divisions = ["Eiendom", "Teknologi", "Drift", "Innkjøp", "Forretningsutvikling"];
        for (var division = 1; division <= 5; division++)
        {
            graph.Values.Add(new DimensionValue
            {
                Id = SeedIds.MagidaDivision(division), AccountId = accountId, DimensionId = departmentDimension,
                Code = $"DIVISION-{division:000}", Revision = SeedIds.MagidaDivision(division),
                Name = $"Divisjon {division} – {divisions[division - 1]}"
            });
            for (var department = 1; department <= 5; department++)
                graph.Values.Add(new DimensionValue
                {
                    Id = SeedIds.MagidaDepartment((division - 1) * 5 + department), AccountId = accountId, DimensionId = departmentDimension,
                    Code = $"DEPARTMENT-{(division - 1) * 5 + department:000}", Revision = SeedIds.MagidaDepartment((division - 1) * 5 + department),
                    ParentId = SeedIds.MagidaDivision(division), Name = $"Avdeling {division}.{department}"
                });
        }

        var continents = new (string Name, string Code, decimal Value2027, (string Country, string Capital)[] Countries)[]
        {
            ("Asia", "ASIA", 104.2m, [("Japan", "Tokyo"), ("Kina", "Beijing"), ("India", "New Delhi"), ("Thailand", "Bangkok")]),
            ("Europa", "EUROPA", 102.5m, [("Norge", "Oslo"), ("Sverige", "Stockholm"), ("Tyskland", "Berlin"), ("Frankrike", "Paris")]),
            ("Afrika", "AFRIKA", 106.1m, [("Kenya", "Nairobi"), ("Egypt", "Kairo"), ("Ghana", "Accra"), ("Marokko", "Rabat")]),
            ("Amerika", "AMERIKA", 103.4m, [("USA", "Washington, D.C."), ("Canada", "Ottawa"), ("Brasil", "Brasília"), ("Argentina", "Buenos Aires")])
        };
        for (var c = 0; c < continents.Length; c++)
        {
            var continent = continents[c];
            var continentId = SeedIds.MagidaContinent(c + 1);
            var indexId = SeedIds.MagidaIndex(c + 1);
            graph.Values.Add(new DimensionValue { Id = continentId, AccountId = accountId, DimensionId = geographyDimension, Name = continent.Name,
                Code = $"CONTINENT-{c + 1:000}", Revision = continentId });
            graph.Indices.Add(new AgreementIndex
            {
                Id = indexId, AccountId = accountId, Code = $"MAGIDA-{continent.Code}",
                Name = $"Magida {continent.Name} årsindeks", Resolution = AgreementIndexResolution.Year,
                Description = "Syntetisk demoindeks med basis 100 i 2026.", Source = "Magida demodata",
                RecordedUtc = recordedUtc, ActorUserId = userId
            });
            for (var year = 2026; year <= 2027; year++)
            {
                var valueId = SeedIds.MagidaIndexValue(c * 2 + year - 2026 + 1);
                graph.IndexValues.Add(new AgreementIndexValue
                {
                    Id = valueId, AccountId = accountId, IndexId = indexId, PeriodKey = valueId,
                    Period = new DateOnly(year, 1, 1), Revision = 1,
                    Value = year == 2026 ? 100m : continent.Value2027,
                    RecordedUtc = recordedUtc, ActorUserId = userId, Reason = "Syntetisk demoverdi"
                });
            }
            for (var country = 0; country < continent.Countries.Length; country++)
            {
                var number = c * 4 + country + 1;
                var location = continent.Countries[country];
                graph.Values.Add(new DimensionValue
                {
                    Id = SeedIds.MagidaCountry(number), AccountId = accountId, DimensionId = geographyDimension,
                    Code = $"COUNTRY-{number:000}", Revision = SeedIds.MagidaCountry(number),
                    ParentId = continentId, Name = location.Country
                });
                graph.Values.Add(new DimensionValue
                {
                    Id = SeedIds.MagidaCapital(number), AccountId = accountId, DimensionId = geographyDimension,
                    Code = $"CAPITAL-{number:000}", Revision = SeedIds.MagidaCapital(number),
                    ParentId = SeedIds.MagidaCountry(number), Name = location.Capital
                });
                graph.Counterparties.Add(new Organization
                {
                    Id = SeedIds.MagidaCounterparty(number), AccountId = accountId,
                    Name = $"Demo Partner {location.Capital}", Type = OrganizationType.ServiceProvider, IsActive = true
                });
            }
        }

        string[] typeNames = ["Leieavtale", "Programvarelisens", "Drift og vedlikehold", "Leverandøravtale", "Samarbeidsavtale"];
        string[] primaryLines = ["Leie av lokaler", "Brukerlisenser", "Driftstjenester", "Vareleveranser", "Rådgivning"];
        string[] secondaryLines = ["Felleskostnader", "Brukerstøtte", "Forebyggende vedlikehold", "Transport", "Prosjektoppfølging"];
        int[] durations = [12, 18, 24, 36, 48, 60];
        AgreementFrequency[] frequencies = [AgreementFrequency.Monthly, AgreementFrequency.Quarterly,
            AgreementFrequency.HalfYearly, AgreementFrequency.Yearly];

        for (var i = 0; i < 300; i++)
        {
            var agreementId = SeedIds.MagidaAgreement(i + 1);

            var department = i / 12 + 1;
            var c = i % 4;
            var country = (i / 4) % 4;
            var locationNumber = c * 4 + country + 1;
            var type = (i % 12 + department - 1) % 5;
            var start = new DateOnly(2026, i % 6 + 1, 1);
            var duration = durations[(i / 3) % durations.Length];
            var form = (AgreementForm)(i % 3 + 1);
            var end = form == AgreementForm.FixedTerm ? start.AddMonths(duration).AddDays(-1) : (DateOnly?)null;
            var renewal = form == AgreementForm.Renewing ? start.AddMonths(duration) : (DateOnly?)null;
            var indexId = SeedIds.MagidaIndex(c + 1);
            graph.Agreements.Add(new Agreement
            {
                Id = agreementId, AccountId = accountId,
                Title = $"MAG-{i + 1:000} {typeNames[type]} – {continents[c].Countries[country].Capital}",
                Description = $"Demonstrasjonsavtale for Magida, avdeling {(department - 1) / 5 + 1}.{(department - 1) % 5 + 1}.",
                Type = (AgreementType)(type + 1), Direction = (i / 4) % 2 == 0 ? AgreementDirection.Income : AgreementDirection.Cost,
                Currency = "NOK", IndexId = indexId, CounterpartyOrganizationId = SeedIds.MagidaCounterparty(locationNumber),
                OwnerUserId = userId, Status = AgreementStatus.Active, StartDate = start, EndDate = end,
                Form = form, CurrentPeriodStartDate = start, RenewalDate = renewal,
                AutoRenew = form == AgreementForm.Renewing, RenewalMonths = form == AgreementForm.Renewing ? duration : null,
                NoticeMode = form == AgreementForm.Renewing ? AgreementNoticeMode.BeforeRenewal : AgreementNoticeMode.None,
                NoticeCount = form == AgreementForm.FixedTerm ? null : 3,
                NoticeUnit = form == AgreementForm.FixedTerm ? null : AgreementNoticeUnit.Months,
                NoticeDeadline = renewal?.AddMonths(-3), ReminderMode = AgreementReminderMode.Disabled,
                CreatedUtc = recordedUtc, UpdatedUtc = recordedUtc, CreatedByUserId = userId, UpdatedByUserId = userId,
                Revision = agreementId
            });
            graph.Assignments.Add(new AgreementDimensionAssignment
            {
                Id = SeedIds.MagidaAgreementDimensionAssignment(i * 2 + 1), AccountId = accountId,
                AgreementId = agreementId, DimensionId = geographyDimension, ValueId = SeedIds.MagidaCapital(locationNumber)
            });
            graph.Assignments.Add(new AgreementDimensionAssignment
            {
                Id = SeedIds.MagidaAgreementDimensionAssignment(i * 2 + 2), AccountId = accountId,
                AgreementId = agreementId, DimensionId = departmentDimension, ValueId = SeedIds.MagidaDepartment(department)
            });
            graph.IndexSelections.Add(new AgreementIndexSelection
            {
                Id = SeedIds.MagidaIndexSelection(i + 1), AccountId = accountId, AgreementId = agreementId,
                IndexId = indexId, Sequence = 1, EffectiveFrom = start, RecordedUtc = recordedUtc, ActorUserId = userId
            });
            for (var line = 0; line < 2; line++)
            {
                var number = i * 2 + line + 1;
                var lineId = SeedIds.MagidaAgreementLine(number);
                graph.Lines.Add(new AgreementLine
                {
                    Id = lineId, AccountId = accountId, AgreementId = agreementId, ActivatedUtc = recordedUtc
                });
                graph.LineVersions.Add(new AgreementLineVersion
                {
                    Id = SeedIds.MagidaLineVersion(number), AccountId = accountId, AgreementId = agreementId,
                    LineId = lineId, Sequence = 1, EffectiveFrom = start, RecordedUtc = recordedUtc, ActorUserId = userId,
                    Name = line == 0 ? primaryLines[type] : secondaryLines[type], StartDate = start, EndDate = end,
                    FirstPayableDate = start, Frequency = frequencies[(i / 4 + line) % frequencies.Length],
                    Anchor = AgreementAnchor.Calendar, AnchorDate = new DateOnly(1, 1, 1),
                    BillingTiming = line == 0 ? AgreementBillingTiming.Advance : AgreementBillingTiming.Arrears,
                    Status = AgreementLineStatus.Active, IndexRegulated = true, Reason = "Opprettet som demodata"
                });
                graph.PriceVersions.Add(new AgreementPriceVersion
                {
                    Id = SeedIds.MagidaPriceVersion(number), AccountId = accountId, AgreementId = agreementId,
                    LineId = lineId, Sequence = 1, EffectiveFrom = start, IndexBaseDate = start,
                    Quantity = line == 0 ? 1 + i % 10 : 1,
                    UnitPrice = line == 0 ? 5000m + i % 40 * 1250m : 750m + i % 20 * 250m,
                    RecordedUtc = recordedUtc, ActorUserId = userId, Reason = "Opprettet som demodata"
                });
            }
        }
        return graph;
    }
}
