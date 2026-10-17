namespace TenantPlatform.Application.AccountSettings;

public sealed class AccountHierarchyException(string resourceKey) : Exception(resourceKey);
