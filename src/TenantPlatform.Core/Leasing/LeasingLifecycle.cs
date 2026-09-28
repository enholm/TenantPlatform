namespace TenantPlatform.Core.Leasing;

public enum LeasingLifecycleStatus { Active=1, Closing=2, Extended=3, Closed=4 }
public enum LeasingEquipmentStatus { InUse=1, PlannedReturn=2, Returned=3, Replaced=4, BoughtOut=5, LostOrDamaged=6 }
public enum LeasingNoticeBasis { Unknown=0, Explicit=1, Days=2, CalendarMonths=3 }
public enum LeasingLifecycleKind { Return=1, Replace=2, Buyout=3, LossOrDamage=4, Extend=5, StartClosing=6, Close=7, EarlyClose=8, Correct=9 }
public enum LeasingLifecycleDecision { Pending=1, Approved=2, Rejected=3, Reversed=4 }
public enum LeasingFollowupKind { Expiry=1, Notice=2, Return=3, ExtensionOutcome=4, OpenReservation=5, Equipment=6, UnknownTerms=7, MissingOwner=8, PaymentPlan=9, Documents=10, FrameworkEnd=11 }
public enum LeasingFollowupStatus { Open=1, InProgress=2, Completed=3, Obsolete=4 }
public enum LeasingNotificationStatus { Pending=1, Delivered=2, Skipped=3, Failed=4 }

// A separate operational projection. EndDate on the acquisition remains the original financial record.
public sealed class LeasingLifecycle
{
    public Guid Id {get;set;}
    public Guid AccountId {get;set;}
    public Guid AcquisitionId {get;set;}
    public DateOnly OriginalEndDate {get;set;}
    public DateOnly AgreedEndDate {get;set;}
    public LeasingNoticeBasis NoticeBasis {get;set;}
    public DateOnly? ExplicitNoticeDate {get;set;}
    public int? NoticeCount {get;set;}
    public bool? AutomaticExtension {get;set;}
    public int? ExtensionMonths {get;set;}
    public DateOnly? ReturnDueDate {get;set;}
    public Guid? OwnerUserId {get;set;}
    public Guid? SourceDocumentId {get;set;}
    public string Notes {get;set;}="";
    public LeasingLifecycleStatus Status {get;set;}=LeasingLifecycleStatus.Active;
    public DateOnly? ClosedDate {get;set;}
    public LeasingLifecycleKind? ClosureKind {get;set;}
    public bool DocumentReviewComplete {get;set;}
    public bool PaymentPlanReviewRequired {get;set;}
    public bool PaymentsChanged {get;set;}
    public Guid? PlanAtExtensionId {get;set;}
    public Guid Revision {get;set;}
    public DateOnly? NoticeDate => LeasingLifecycleCalculator.Notice(AgreedEndDate,NoticeBasis,ExplicitNoticeDate,NoticeCount);
}
public sealed class LeasingEquipment
{
    public Guid Id {get;set;}
    public Guid AccountId {get;set;}
    public Guid AcquisitionId {get;set;}
    public Guid ItemId {get;set;}
    public string? SerialNumber {get;set;}
    public string? InternalId {get;set;}
    public string Description {get;set;}="";
    public Guid? BuildingId {get;set;}
    public Guid? OwnerUserId {get;set;}
    public LeasingEquipmentStatus Status {get;set;}=LeasingEquipmentStatus.InUse;
    public DateOnly RegisteredDate {get;set;}
    public DateOnly? StatusDate {get;set;}
    public Guid? ReplacementEquipmentId {get;set;}
    public Guid? SourceDocumentId {get;set;}
    public string Notes {get;set;}="";
    public Guid Revision {get;set;}
}
public sealed class LeasingLifecycleEvent
{
    public Guid Id {get;set;}
    public Guid AccountId {get;set;}
    public Guid AcquisitionId {get;set;}
    public LeasingLifecycleKind Kind {get;set;}
    public LeasingLifecycleDecision Decision {get;set;}
    public DateOnly Date {get;set;}
    public Guid ActorUserId {get;set;}
    public DateTimeOffset RecordedUtc {get;set;}
    public Guid? ApprovedByUserId {get;set;}
    public DateTimeOffset? ApprovedUtc {get;set;}
    public string Reason {get;set;}="";
    public Guid? CounterpartyId {get;set;}
    public string Reference {get;set;}="";
    public Guid? SourceDocumentId {get;set;}
    public string InputJson {get;set;}="{}";
    public string BeforeJson {get;set;}="{}";
    public string AfterJson {get;set;}="{}";
    public Guid Revision {get;set;}
}
public sealed class LeasingDisposition
{
    public Guid Id {get;set;}
    public Guid AccountId {get;set;}
    public Guid AcquisitionId {get;set;}
    public Guid EventId {get;set;}
    public Guid ItemId {get;set;}
    public Guid? EquipmentId {get;set;}
    public decimal Quantity {get;set;}
    public bool Reversed {get;set;}
}
public sealed class LeasingFollowup
{
    public bool AssignedExplicitly {get;set;}
    public Guid Id {get;set;}
    public Guid AccountId {get;set;}
    public Guid? AcquisitionId {get;set;}
    public Guid? FrameworkId {get;set;}
    public string Key {get;set;}="";
    public LeasingFollowupKind Kind {get;set;}
    public Guid? OwnerUserId {get;set;}
    public DateOnly? DueDate {get;set;}
    public LeasingFollowupStatus Status {get;set;}
    public string Comment {get;set;}="";
    public Guid? CompletedByUserId {get;set;}
    public DateTimeOffset? CompletedUtc {get;set;}
    public Guid Revision {get;set;}
}
public sealed class LeasingNotificationSettings
{
    public Guid AccountId {get;set;}
    public bool Enabled {get;set;}
    public DateTimeOffset? ActivatedUtc {get;set;}
    public string TimeZoneId {get;set;}="Europe/Oslo";
    public int[] Days {get;set;}=[90,30,7];
    public Guid[] ExtraRecipientIds {get;set;}=[];
    public Guid Revision {get;set;}
}
public sealed class LeasingNotification
{
    public Guid Id {get;set;}
    public Guid AccountId {get;set;}
    public Guid FollowupId {get;set;}
    public Guid RecipientUserId {get;set;}
    public int DaysBefore {get;set;}
    public DateTimeOffset ScheduledUtc {get;set;}
    public DateTimeOffset? DeliveredUtc {get;set;}
    public DateTimeOffset? ReadUtc {get;set;}
    public LeasingNotificationStatus Status {get;set;}
    public string? ResultKey {get;set;}
}
public static class LeasingLifecycleCalculator
{
    public static DateOnly? Notice(DateOnly end,LeasingNoticeBasis basis,DateOnly? explicitDate,int? count)
    {
        try{return basis switch{LeasingNoticeBasis.Explicit=>explicitDate,LeasingNoticeBasis.Days when count>=0=>end.AddDays(-count.Value),LeasingNoticeBasis.CalendarMonths when count>=0=>LeasingPaymentCalculator.Anchored(end,-count.Value,end.Day==DateTime.DaysInMonth(end.Year,end.Month)),_=>null};}
        catch(ArgumentOutOfRangeException){return null;}
    }
    public static LeasingLifecycle Initial(LeasingAcquisition a)=>new(){Id=a.Id,AccountId=a.AccountId,AcquisitionId=a.Id,OriginalEndDate=a.EndDate,AgreedEndDate=a.EndDate,OwnerUserId=a.OwnerUserId,Revision=Guid.Empty};
}
