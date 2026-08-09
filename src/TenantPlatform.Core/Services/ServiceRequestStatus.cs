namespace TenantPlatform.Core.Services;

public enum ServiceRequestStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    InProgress = 4,
    Completed = 5,
    Rejected = 6,
    Cancelled = 7,
    Failed = 8
}
