using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Initialization;

public static partial class DemoDataInitializer
{
    private sealed record MagidaLeaseItemSeed(
        string Description, string ItemNumber, decimal Quantity, decimal UnitPrice, string EquipmentCode);

    private sealed record MagidaAcquisitionSeed(
        int Number, int? FrameworkNumber, string Name, string Reference, DateOnly PurchaseDate, int Months,
        LeasingPaymentFrequency Frequency, decimal AnnualRate, LeasingAcquisitionStatus Status, string ProjectCode,
        MagidaLeaseItemSeed[] Items);

    private sealed record MagidaFrameworkSeed(
        int Number, string Name, string NumberText, DateOnly From, DateOnly To, decimal Limit,
        LeasingFrameworkStatus Status, int Months, LeasingPaymentFrequency Frequency, decimal AnnualRate);

    private static async Task SeedMagidaLeasingAsync(
        TenantPlatformDbContext dbContext, CancellationToken cancellationToken)
    {
        var accountId = SeedIds.MagidaAccount;
        var ownerUserId = await dbContext.LoginAccounts.AsNoTracking()
            .Where(x => x.Email == "morten@magida.org")
            .Select(x => x.UserId)
            .SingleAsync(cancellationToken);
        var recordedUtc = DateTimeOffset.UtcNow;

        var financeOrganizationId = await EnsureMagidaLeasingOrganizationAsync(
            dbContext, 1, "Leasing corp", cancellationToken);
        var supplierOrganizationId = await EnsureMagidaLeasingOrganizationAsync(
            dbContext, 2, "Hybr", cancellationToken);

        var projectDimension = await EnsureMagidaDimensionAsync(
            dbContext, 1, "Prosjekt", "PROJ", 10, cancellationToken);
        var equipmentDimension = await EnsureMagidaDimensionAsync(
            dbContext, 2, "Utstyr", "EQUIP", 20, cancellationToken);

        var projects = new Dictionary<string, DimensionValue>
        {
            ["PRJ-DATA"] = await EnsureMagidaDimensionValueAsync(
                dbContext, projectDimension, 1, null, "Datasenter Oslo", "PRJ-DATA", 1, cancellationToken),
            ["PRJ-NETT"] = await EnsureMagidaDimensionValueAsync(
                dbContext, projectDimension, 2, null, "Nasjonal nettverksfornyelse", "PRJ-NETT", 2, cancellationToken),
            ["PRJ-SIKKER"] = await EnsureMagidaDimensionValueAsync(
                dbContext, projectDimension, 3, null, "Sikker plattform", "PRJ-SIKKER", 3, cancellationToken),
            ["PRJ-DRIFT"] = await EnsureMagidaDimensionValueAsync(
                dbContext, projectDimension, 4, null, "Regional driftsmodernisering", "PRJ-DRIFT", 4, cancellationToken)
        };

        var network = await EnsureMagidaDimensionValueAsync(
            dbContext, equipmentDimension, 5, null, "Nettverk", "CAT-NETT", 1, cancellationToken);
        var servers = await EnsureMagidaDimensionValueAsync(
            dbContext, equipmentDimension, 6, null, "Servere", "CAT-SERVER", 2, cancellationToken);
        var cabinets = await EnsureMagidaDimensionValueAsync(
            dbContext, equipmentDimension, 7, null, "Skap", "CAT-SKAP", 3, cancellationToken);
        var equipment = new Dictionary<string, DimensionValue>
        {
            ["NET-SWITCH"] = await EnsureMagidaDimensionValueAsync(
                dbContext, equipmentDimension, 8, network.Id, "Svitsjer for aksesslag", "NET-SWITCH", 1, cancellationToken),
            ["NET-ROUTER"] = await EnsureMagidaDimensionValueAsync(
                dbContext, equipmentDimension, 9, network.Id, "Kjernerutere", "NET-ROUTER", 2, cancellationToken),
            ["NET-FIREWALL"] = await EnsureMagidaDimensionValueAsync(
                dbContext, equipmentDimension, 10, network.Id, "Brannmurer", "NET-FIREWALL", 3, cancellationToken),
            ["SRV-VIRTUAL"] = await EnsureMagidaDimensionValueAsync(
                dbContext, equipmentDimension, 11, servers.Id, "Virtualiseringsservere", "SRV-VIRTUAL", 1, cancellationToken),
            ["SRV-BACKUP"] = await EnsureMagidaDimensionValueAsync(
                dbContext, equipmentDimension, 12, servers.Id, "Sikkerhetskopiservere", "SRV-BACKUP", 2, cancellationToken),
            ["SRV-DATABASE"] = await EnsureMagidaDimensionValueAsync(
                dbContext, equipmentDimension, 13, servers.Id, "Databaseservere", "SRV-DATABASE", 3, cancellationToken),
            ["RACK-42U"] = await EnsureMagidaDimensionValueAsync(
                dbContext, equipmentDimension, 14, cabinets.Id, "Gulvskap 42U", "RACK-42U", 1, cancellationToken),
            ["RACK-24U"] = await EnsureMagidaDimensionValueAsync(
                dbContext, equipmentDimension, 15, cabinets.Id, "Gulvskap 24U", "RACK-24U", 2, cancellationToken),
            ["RACK-WALL"] = await EnsureMagidaDimensionValueAsync(
                dbContext, equipmentDimension, 16, cabinets.Id, "Veggskap 12U", "RACK-WALL", 3, cancellationToken)
        };

        await EnsureMagidaLeasingDimensionRuleAsync(dbContext, projectDimension, cancellationToken);
        await EnsureMagidaLeasingDimensionRuleAsync(dbContext, equipmentDimension, cancellationToken);

        var acquisitions = new[]
        {
            new MagidaAcquisitionSeed(1, null, "Nettverksplattform – Oslo", "BL-2025-001",
                new(2025, 10, 1), 36, LeasingPaymentFrequency.Monthly, 5.10m,
                LeasingAcquisitionStatus.Registered, "PRJ-NETT",
                [new("Aksessvitsjer", "SW-48P", 4, 48500m, "NET-SWITCH"),
                 new("Kjernerutere", "RTR-CORE", 2, 112000m, "NET-ROUTER")]),
            new MagidaAcquisitionSeed(2, null, "Virtualiseringsplattform", "BL-2026-002",
                new(2026, 1, 1), 48, LeasingPaymentFrequency.Quarterly, 5.45m,
                LeasingAcquisitionStatus.Registered, "PRJ-DATA",
                [new("Virtualiseringsservere", "SRV-2U", 4, 138000m, "SRV-VIRTUAL"),
                 new("Brannmurer", "FW-HA", 2, 89500m, "NET-FIREWALL")]),
            new MagidaAcquisitionSeed(3, null, "Sikkerhetskopi og lagring", "BL-2026-003",
                new(2026, 4, 1), 36, LeasingPaymentFrequency.HalfYearly, 4.90m,
                LeasingAcquisitionStatus.Registered, "PRJ-SIKKER",
                [new("Sikkerhetskopiservere", "SRV-BKP", 2, 164000m, "SRV-BACKUP"),
                 new("Gulvskap 42U", "RACK-42", 2, 28750m, "RACK-42U")]),
            new MagidaAcquisitionSeed(4, null, "Oppgradering av regional drift", "BL-2026-004",
                new(2026, 10, 1), 24, LeasingPaymentFrequency.Yearly, 5.25m,
                LeasingAcquisitionStatus.Draft, "PRJ-DRIFT",
                [new("Databaseservere", "SRV-DB", 2, 149000m, "SRV-DATABASE"),
                 new("Gulvskap 24U", "RACK-24", 2, 21900m, "RACK-24U")]),
            new MagidaAcquisitionSeed(5, null, "Erstattet brannmuranskaffelse", "BL-2025-005",
                new(2025, 3, 1), 24, LeasingPaymentFrequency.Monthly, 5.60m,
                LeasingAcquisitionStatus.Cancelled, "PRJ-SIKKER",
                [new("Brannmurer", "FW-OLD", 2, 73500m, "NET-FIREWALL")]),
            new MagidaAcquisitionSeed(6, 1, "Nettverkskjerne – datasenter", "MAG-FA-001",
                new(2026, 2, 1), 48, LeasingPaymentFrequency.Monthly, 4.95m,
                LeasingAcquisitionStatus.Registered, "PRJ-DATA",
                [new("Kjernerutere", "RTR-DC", 2, 128000m, "NET-ROUTER"),
                 new("Gulvskap 42U", "RACK-DC", 2, 31500m, "RACK-42U")]),
            new MagidaAcquisitionSeed(7, 2, "Serverutvidelse – analyse", "MAG-FA-002",
                new(2026, 3, 1), 36, LeasingPaymentFrequency.Quarterly, 5.20m,
                LeasingAcquisitionStatus.Registered, "PRJ-DATA",
                [new("Virtualiseringsservere", "SRV-AN", 3, 142000m, "SRV-VIRTUAL"),
                 new("Sikkerhetskopiserver", "SRV-AN-BK", 1, 171000m, "SRV-BACKUP")]),
            new MagidaAcquisitionSeed(8, 3, "Sikret nettverk – region øst", "MAG-FA-003",
                new(2026, 6, 1), 60, LeasingPaymentFrequency.HalfYearly, 5.35m,
                LeasingAcquisitionStatus.Registered, "PRJ-SIKKER",
                [new("Aksessvitsjer", "SW-REG", 6, 46200m, "NET-SWITCH"),
                 new("Brannmurer", "FW-REG", 2, 93500m, "NET-FIREWALL")]),
            new MagidaAcquisitionSeed(9, 4, "Driftsnettverk – hovedkontor", "MAG-FA-004",
                new(2025, 11, 1), 36, LeasingPaymentFrequency.Yearly, 5.05m,
                LeasingAcquisitionStatus.Registered, "PRJ-DRIFT",
                [new("Veggskap 12U", "RACK-WH", 4, 14900m, "RACK-WALL"),
                 new("Kjerneruter", "RTR-WH", 2, 105000m, "NET-ROUTER")])
        };

        var frameworkSeeds = new[]
        {
            new MagidaFrameworkSeed(1, "Nettverk og datasenter 2025–2028", "MAG-RAM-001",
                new(2025, 1, 1), new(2028, 12, 31), 1800000m, LeasingFrameworkStatus.Open,
                48, LeasingPaymentFrequency.Monthly, 4.95m),
            new MagidaFrameworkSeed(2, "Serverkapasitet 2025–2027", "MAG-RAM-002",
                new(2025, 7, 1), new(2027, 12, 31), 1500000m, LeasingFrameworkStatus.Open,
                36, LeasingPaymentFrequency.Quarterly, 5.20m),
            new MagidaFrameworkSeed(3, "Informasjonssikkerhet 2026–2030", "MAG-RAM-003",
                new(2026, 1, 1), new(2030, 12, 31), 2000000m, LeasingFrameworkStatus.Open,
                60, LeasingPaymentFrequency.HalfYearly, 5.35m),
            new MagidaFrameworkSeed(4, "Driftsutstyr 2024–2026", "MAG-RAM-004",
                new(2024, 1, 1), new(2026, 6, 30), 900000m, LeasingFrameworkStatus.Closed,
                36, LeasingPaymentFrequency.Yearly, 5.05m),
            new MagidaFrameworkSeed(5, "Tidligere serveravtale 2022–2024", "MAG-RAM-005",
                new(2022, 1, 1), new(2024, 12, 31), 650000m, LeasingFrameworkStatus.Finished,
                24, LeasingPaymentFrequency.Monthly, 4.75m)
        };

        var frameworkRows = frameworkSeeds.Select(seed =>
        {
            var id = SeedIds.MagidaLeasingFramework(seed.Number);
            var terms = MagidaTerms(seed.Months, seed.Frequency, seed.AnnualRate, 0m,
                seed.From, $"MAGIDA-FRAMEWORK-{seed.Number:000}");
            return new LeasingFramework
            {
                Id = id, AccountId = accountId, Name = seed.Name, Number = seed.NumberText,
                FinanceOrganizationId = financeOrganizationId, OwnerUserId = ownerUserId,
                AcquisitionFrom = seed.From, AcquisitionTo = seed.To, Limit = seed.Limit,
                Currency = "NOK", IncludesVat = seed.Number % 2 == 0,
                CreditNotesReleaseLimit = seed.Number % 2 == 0, Terms = terms,
                Notes = "Demonstrasjonsramme med stabil seed-ID.", Status = seed.Status, Revision = id
            };
        }).ToList();
        await AddMissingMagidaEntitiesAsync(dbContext, frameworkRows, x => x.Id, cancellationToken);

        var acquisitionRows = new List<LeasingAcquisition>();
        var itemRows = new List<LeasingItem>();
        var selectionRows = new List<LeasingDimensionSelection>();
        var financingRows = new List<LeasingFinancingRevision>();
        var planRows = new List<LeasingPaymentPlan>();
        var installmentRows = new List<LeasingInstallment>();
        var planTermRows = new List<LeasingPlanTerm>();
        var lifecycleRows = new List<LeasingLifecycle>();
        var equipmentRows = new List<LeasingEquipment>();
        var historyRows = new List<LeasingHistory>();
        var paymentEvents = new List<LeasingPaymentEvent>();
        var selectionNumber = 0;
        var equipmentNumber = 0;
        var paymentEventNumber = 0;

        foreach (var seed in acquisitions)
        {
            var acquisitionId = SeedIds.MagidaLeasingAcquisition(seed.Number);
            var items = seed.Items.Select((itemSeed, position) =>
            {
                var itemId = SeedIds.MagidaLeasingItem(seed.Number * 10 + position + 1);
                var item = new LeasingItem
                {
                    Id = itemId, AccountId = accountId, AcquisitionId = acquisitionId,
                    Position = position, Description = itemSeed.Description, ItemNumber = itemSeed.ItemNumber,
                    Quantity = itemSeed.Quantity, UnitPrice = itemSeed.UnitPrice, VatPercent = 25,
                    CountableEquipment = true
                };
                var project = projects[seed.ProjectCode];
                var classification = equipment[itemSeed.EquipmentCode];
                selectionRows.Add(new()
                {
                    Id = SeedIds.MagidaLeasingDimensionSelection(++selectionNumber),
                    AccountId = accountId, ItemId = itemId, DimensionId = projectDimension.Id, ValueId = project.Id,
                    DimensionName = projectDimension.Name, DimensionCode = projectDimension.Code,
                    ValueName = project.Name, ValueCode = project.Code, ValuePath = project.Name
                });
                var parentName = itemSeed.EquipmentCode.StartsWith("NET-", StringComparison.Ordinal) ? network.Name :
                    itemSeed.EquipmentCode.StartsWith("SRV-", StringComparison.Ordinal) ? servers.Name : cabinets.Name;
                selectionRows.Add(new()
                {
                    Id = SeedIds.MagidaLeasingDimensionSelection(++selectionNumber),
                    AccountId = accountId, ItemId = itemId, DimensionId = equipmentDimension.Id, ValueId = classification.Id,
                    DimensionName = equipmentDimension.Name, DimensionCode = equipmentDimension.Code,
                    ValueName = classification.Name, ValueCode = classification.Code,
                    ValuePath = $"{parentName} / {classification.Name}"
                });
                itemRows.Add(item);
                return item;
            }).ToList();
            var totals = LeasingCalculator.Total(items);
            var financedAmount = totals.Net;
            var terms = MagidaTerms(seed.Months, seed.Frequency, seed.AnnualRate, 0m,
                seed.PurchaseDate, $"MAGIDA-LEASE-{seed.Number:000}");
            var acquisition = new LeasingAcquisition
            {
                Id = acquisitionId, AccountId = accountId,
                FrameworkId = seed.FrameworkNumber.HasValue
                    ? SeedIds.MagidaLeasingFramework(seed.FrameworkNumber.Value) : null,
                Name = seed.Name, Reference = seed.Reference, SupplierOrganizationId = supplierOrganizationId,
                FinanceOrganizationId = financeOrganizationId, OwnerUserId = ownerUserId,
                PurchaseDate = seed.PurchaseDate, EndDate = LeasingCalculator.EndDate(seed.PurchaseDate, seed.Months),
                Currency = "NOK", InvoiceNumber = $"HYB-{seed.PurchaseDate.Year}-{seed.Number:000}",
                InvoiceDate = seed.PurchaseDate, NetTotal = totals.Net, VatTotal = totals.Vat,
                GrossTotal = totals.Gross, FinancedAmount = financedAmount, Terms = terms,
                Notes = "Utstyrs- og betalingsdata er syntetiske demonstrasjonsdata.",
                Status = seed.Status, Revision = acquisitionId, Items = items
            };
            acquisitionRows.Add(acquisition);
            historyRows.Add(new()
            {
                Id = SeedIds.MagidaLeasingHistory(seed.Number + 100), AccountId = accountId,
                AcquisitionId = acquisitionId, ActorUserId = ownerUserId, RecordedUtc = recordedUtc,
                Action = seed.Status == LeasingAcquisitionStatus.Cancelled ? "Cancelled" : "Created",
                Reason = seed.Status == LeasingAcquisitionStatus.Cancelled
                    ? "Erstattet før bestilling i demonstrasjonsdatasettet." : "Opprettet som demodata.",
                BeforeJson = "{}", AfterJson = JsonSerializer.Serialize(acquisition)
            });

            var financingRevisionId = SeedIds.MagidaFinancingRevision(seed.Number);
            var financingRevision = new LeasingFinancingRevision
            {
                Id = financingRevisionId, AccountId = accountId, AcquisitionId = acquisitionId,
                EffectiveFrom = seed.PurchaseDate,
                SnapshotJson = JsonSerializer.Serialize(new LeasingFinancingSnapshot
                {
                    FinanceOrganizationId = financeOrganizationId, Currency = "NOK",
                    FinancedAmount = financedAmount, Terms = terms.Copy()
                }),
                ActorUserId = ownerUserId, RecordedUtc = recordedUtc,
                Reason = "Opprinnelige daterte finansieringsvilkår i demodata."
            };
            financingRows.Add(financingRevision);

            if (seed.Status == LeasingAcquisitionStatus.Registered)
            {
                var endDate = acquisition.EndDate;
                lifecycleRows.Add(new()
                {
                    Id = SeedIds.MagidaLifecycle(seed.Number), AccountId = accountId, AcquisitionId = acquisitionId,
                    OriginalEndDate = endDate, AgreedEndDate = endDate, NoticeBasis = LeasingNoticeBasis.CalendarMonths,
                    NoticeCount = 3, AutomaticExtension = false, ReturnDueDate = endDate, OwnerUserId = ownerUserId,
                    Status = LeasingLifecycleStatus.Active, Revision = SeedIds.MagidaLifecycle(seed.Number),
                    Notes = "Tre måneders oppsigelsesfrist og retur ved avtalt sluttdato."
                });
                foreach (var item in items)
                {
                    for (var unit = 1; unit <= (int)item.Quantity; unit++)
                    {
                        equipmentRows.Add(new()
                        {
                            Id = SeedIds.MagidaEquipment(++equipmentNumber), AccountId = accountId,
                            AcquisitionId = acquisitionId, ItemId = item.Id,
                            SerialNumber = $"MAG-DEMO-{seed.Number:00}-{item.Position + 1:00}-{unit:00}",
                            InternalId = $"MAG-LS-{seed.Number:00}-{item.Position + 1:00}-{unit:00}",
                            Description = $"{item.Description} {unit:00}", OwnerUserId = ownerUserId,
                            Status = LeasingEquipmentStatus.InUse, RegisteredDate = seed.PurchaseDate,
                            Revision = SeedIds.MagidaEquipment(equipmentNumber)
                        });
                    }
                }
            }

            if (seed.Status is LeasingAcquisitionStatus.Registered or LeasingAcquisitionStatus.Draft)
            {
                var planId = SeedIds.MagidaPaymentPlan(seed.Number);
                var planTerms = BuildMagidaPlanTerms(seed, financedAmount, financingRevisionId);
                var isActive = seed.Status == LeasingAcquisitionStatus.Registered;
                var plan = new LeasingPaymentPlan
                {
                    Id = planId, AccountId = accountId, AcquisitionId = acquisitionId, Version = 1,
                    Status = LeasingPlanStatus.Draft,
                    Source = LeasingPlanSource.Manual, Currency = "NOK",
                    ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                        JsonSerializer.Serialize(planTerms.Select(x => new
                        {
                            x.Reference, x.PeriodFrom, x.PeriodTo, x.DueDate, x.Net, x.Vat,
                            x.Gross, x.FinancingRevisionId
                        }))))),
                    Revision = planId, CreatedByUserId = ownerUserId, CreatedUtc = recordedUtc,
                    Notes = "Datert plan med hovedstol og beregnede renter per termin."
                };
                foreach (var (term, index) in planTerms.Select((term, index) => (term, index)))
                {
                    var installmentId = SeedIds.MagidaInstallment(seed.Number * 1000 + index + 1);
                    term.Id = SeedIds.MagidaPlanTerm(seed.Number * 1000 + index + 1);
                    term.AccountId = accountId;
                    term.PlanId = planId;
                    term.InstallmentId = installmentId;
                    installmentRows.Add(new()
                    {
                        Id = installmentId, AccountId = accountId, AcquisitionId = acquisitionId,
                        Reference = term.Reference, Revision = installmentId
                    });
                    planTermRows.Add(term);
                }
                plan.Terms = planTerms;
                planRows.Add(plan);

                paymentEvents.Add(new()
                {
                    Id = SeedIds.MagidaPaymentEvent(++paymentEventNumber), AccountId = accountId,
                    AcquisitionId = acquisitionId, ActorUserId = ownerUserId, RecordedUtc = recordedUtc,
                    Action = "PlanDraft", Reason = "Plan opprettet som demodata.",
                    BeforeJson = "{}", AfterJson = JsonSerializer.Serialize(plan)
                });
                if (isActive)
                {
                    plan.Status = LeasingPlanStatus.Active;
                    plan.CheckedByUserId = ownerUserId;
                    plan.CheckedUtc = recordedUtc;
                    paymentEvents.Add(new()
                    {
                        Id = SeedIds.MagidaPaymentEvent(++paymentEventNumber), AccountId = accountId,
                        AcquisitionId = acquisitionId, ActorUserId = ownerUserId, RecordedUtc = recordedUtc,
                        Action = "PlanActivated", Reason = "Plan kontrollert som demodata.",
                        BeforeJson = "{}", AfterJson = JsonSerializer.Serialize(plan)
                    });
                }
            }
        }

        await AddMissingMagidaEntitiesAsync(dbContext, acquisitionRows, x => x.Id, cancellationToken);
        await AddMissingMagidaEntitiesAsync(dbContext, itemRows, x => x.Id, cancellationToken);
        await AddMissingMagidaEntitiesAsync(dbContext, selectionRows, x => x.Id, cancellationToken);
        await AddMissingMagidaEntitiesAsync(dbContext, financingRows, x => x.Id, cancellationToken);
        await AddMissingMagidaEntitiesAsync(dbContext, planRows, x => x.Id, cancellationToken);
        await AddMissingMagidaEntitiesAsync(dbContext, installmentRows, x => x.Id, cancellationToken);
        await AddMissingMagidaEntitiesAsync(dbContext, planTermRows, x => x.Id, cancellationToken);
        await AddMissingMagidaEntitiesAsync(dbContext, lifecycleRows, x => x.Id, cancellationToken);
        await AddMissingMagidaEntitiesAsync(dbContext, equipmentRows, x => x.Id, cancellationToken);
        await AddMissingMagidaEntitiesAsync(dbContext, historyRows, x => x.Id, cancellationToken);
        await AddMissingMagidaEntitiesAsync(dbContext, paymentEvents, x => x.Id, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Guid> EnsureMagidaLeasingOrganizationAsync(
        TenantPlatformDbContext dbContext, int number, string name, CancellationToken cancellationToken)
    {
        var accountId = SeedIds.MagidaAccount;
        var organization = await dbContext.Organizations.SingleOrDefaultAsync(
            x => x.AccountId == accountId && x.Name == name, cancellationToken)
            ?? await dbContext.Organizations.SingleOrDefaultAsync(
                x => x.AccountId == accountId && x.Id == SeedIds.MagidaLeasingOrganization(number), cancellationToken);
        if (organization is null)
        {
            organization = new Organization
            {
                Id = SeedIds.MagidaLeasingOrganization(number), AccountId = accountId,
                Name = name, Type = OrganizationType.ServiceProvider, IsActive = true
            };
            dbContext.Organizations.Add(organization);
        }
        else
        {
            organization.Name = name;
            organization.Type = OrganizationType.ServiceProvider;
            organization.IsActive = true;
        }
        return organization.Id;
    }

    private static async Task<Dimension> EnsureMagidaDimensionAsync(
        TenantPlatformDbContext dbContext, int number, string name, string code, int sortOrder,
        CancellationToken cancellationToken)
    {
        var accountId = SeedIds.MagidaAccount;
        var dimension = await dbContext.Dimensions.SingleOrDefaultAsync(
            x => x.AccountId == accountId && x.Code == code, cancellationToken)
            ?? await dbContext.Dimensions.SingleOrDefaultAsync(
                x => x.AccountId == accountId && x.Id == SeedIds.MagidaDimension(number), cancellationToken);
        if (dimension is null)
        {
            dimension = new Dimension
            {
                Id = SeedIds.MagidaDimension(number), AccountId = accountId,
                Name = name, Code = code, IsActive = true, AllowMultiple = false, LeafOnly = true,
                SortOrder = sortOrder, Revision = SeedIds.MagidaDimension(number)
            };
            dbContext.Dimensions.Add(dimension);
        }
        else
        {
            var changed = dimension.Name != name || dimension.Code != code || !dimension.IsActive ||
                dimension.AllowMultiple || !dimension.LeafOnly || dimension.SortOrder != sortOrder;
            dimension.Name = name;
            dimension.Code = code;
            dimension.IsActive = true;
            dimension.AllowMultiple = false;
            dimension.LeafOnly = true;
            dimension.SortOrder = sortOrder;
            if (changed) dimension.Revision = Guid.NewGuid();
        }
        return dimension;
    }

    private static async Task<DimensionValue> EnsureMagidaDimensionValueAsync(
        TenantPlatformDbContext dbContext, Dimension dimension, int number, Guid? parentId, string name,
        string code, int sortOrder, CancellationToken cancellationToken)
    {
        var value = await dbContext.DimensionValues.SingleOrDefaultAsync(
            x => x.AccountId == dimension.AccountId && x.DimensionId == dimension.Id && x.Code == code, cancellationToken)
            ?? await dbContext.DimensionValues.SingleOrDefaultAsync(
                x => x.AccountId == dimension.AccountId && x.DimensionId == dimension.Id &&
                    x.Id == SeedIds.MagidaDimensionValue(number), cancellationToken);
        if (value is null)
        {
            value = new DimensionValue
            {
                Id = SeedIds.MagidaDimensionValue(number), AccountId = dimension.AccountId,
                DimensionId = dimension.Id, ParentId = parentId, Name = name, Code = code,
                IsActive = true, SortOrder = sortOrder, Revision = SeedIds.MagidaDimensionValue(number)
            };
            dbContext.DimensionValues.Add(value);
        }
        else
        {
            var changed = value.ParentId != parentId || value.Name != name || value.Code != code ||
                !value.IsActive || value.SortOrder != sortOrder;
            value.ParentId = parentId;
            value.Name = name;
            value.Code = code;
            value.IsActive = true;
            value.SortOrder = sortOrder;
            if (changed) value.Revision = Guid.NewGuid();
        }
        return value;
    }

    private static async Task EnsureMagidaLeasingDimensionRuleAsync(
        TenantPlatformDbContext dbContext, Dimension dimension, CancellationToken cancellationToken)
    {
        var rule = await dbContext.LeasingDimensionRules.SingleOrDefaultAsync(
            x => x.AccountId == dimension.AccountId && x.DimensionId == dimension.Id, cancellationToken);
        if (rule is null)
        {
            dbContext.LeasingDimensionRules.Add(new LeasingDimensionRule
            {
                AccountId = dimension.AccountId, DimensionId = dimension.Id, IsEnabled = true,
                IsRequired = false, AllowAllocation = false, Revision = dimension.Revision
            });
        }
        else if (!rule.IsEnabled || rule.IsRequired || rule.AllowAllocation)
        {
            rule.IsEnabled = true;
            rule.IsRequired = false;
            rule.AllowAllocation = false;
            rule.Revision = Guid.NewGuid();
        }
    }

    private static LeasingTerms MagidaTerms(
        int months, LeasingPaymentFrequency frequency, decimal annualRate, decimal advanceRent,
        DateOnly firstDate, string financeReference) => new()
    {
        Months = months, InterestKind = LeasingInterestKind.Fixed, AnnualRatePercent = annualRate,
        PaymentFrequency = frequency, FinanceReference = financeReference, AdvanceRent = advanceRent,
        PaymentTiming = LeasingPaymentTiming.Arrears,
        FirstDueDate = firstDate.AddMonths((int)frequency)
    };

    private static List<LeasingPlanTerm> BuildMagidaPlanTerms(
        MagidaAcquisitionSeed seed, decimal financedAmount, Guid financingRevisionId)
    {
        var periodMonths = (int)seed.Frequency;
        var count = seed.Months / periodMonths;
        var periodicRate = seed.AnnualRate / 100m * periodMonths / 12m;
        var paymentFactor = periodicRate == 0
            ? 1m / count
            : periodicRate / (1m - (decimal)Math.Pow(1d + (double)periodicRate, -count));
        var regularPayment = decimal.Round(financedAmount * paymentFactor, 2, MidpointRounding.AwayFromZero);
        var balance = financedAmount;
        var terms = new List<LeasingPlanTerm>(count);

        for (var index = 0; index < count; index++)
        {
            var periodFrom = seed.PurchaseDate.AddMonths(index * periodMonths);
            var dueDate = seed.PurchaseDate.AddMonths((index + 1) * periodMonths);
            var interest = decimal.Round(balance * periodicRate, 2, MidpointRounding.AwayFromZero);
            var capital = index == count - 1
                ? balance
                : Math.Min(balance, Math.Max(0m, regularPayment - interest));
            var net = capital + interest;
            var vat = decimal.Round(net * 0.25m, 2, MidpointRounding.AwayFromZero);
            terms.Add(new LeasingPlanTerm
            {
                Reference = $"A{seed.Number:00}-{index + 1:000}",
                PeriodFrom = periodFrom, PeriodTo = dueDate.AddDays(-1), DueDate = dueDate,
                Type = LeasingPaymentType.Rent, Net = net, Vat = vat, Gross = net + vat,
                CapitalComponent = capital, InterestComponent = interest, FeeComponent = 0m,
                ComponentsComplete = true, FinancingRevisionId = financingRevisionId
            });
            balance -= capital;
        }

        return terms;
    }
}
