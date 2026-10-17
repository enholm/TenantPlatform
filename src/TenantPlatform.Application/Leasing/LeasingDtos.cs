using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public sealed class LeasingValidationException(string key) : Exception(key);
public sealed record LeasingOption(Guid Id, string Name);
public sealed record LeasingOptions(List<LeasingOption> Parties, List<LeasingOption> Owners, List<LeasingOption> Frameworks, bool CanCreate, bool CanRegisterOrders = false);
public sealed record LeasingRow(Guid Id, bool Framework, string Name, string Number, string Status, string Owner, DateOnly Start, DateOnly End, string Currency, decimal Amount);
public sealed record LeasingPage(List<LeasingRow> Items, int Count, int Page, int PageSize);
public sealed record LeasingHistoryRow(DateTimeOffset RecordedUtc, string Actor, string Action, string Reason, string BeforeJson, string AfterJson);
public sealed record LeasingDetails(LeasingFramework? Framework, LeasingAcquisition? Acquisition, string FinanceName,
    string OwnerName, string? SupplierName, string? FrameworkName, decimal Used, bool CanEdit,
    List<LeasingAcquisition> Acquisitions, List<LeasingDocument> Documents, List<LeasingHistoryRow> History, Dictionary<Guid, string> ReferenceNames, decimal Reserved = 0);

