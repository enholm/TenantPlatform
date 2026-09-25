namespace TenantPlatform.Web.Services.AccountSettings;

public sealed class AccountHierarchyException(string resourceKey) : Exception(resourceKey);
