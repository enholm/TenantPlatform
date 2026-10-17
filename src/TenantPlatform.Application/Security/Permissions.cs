namespace TenantPlatform.Application.Security;

public static class Permissions
{
    public static class Users
    {
        public const string Manage = "users.manage";
    }
    public static class Platform
    {
        public const string OpenAccounts = "platform.open-accounts";
        public const string CreateAccount = "platform.create-account";
        public const string ReadAccount = "platform.read-account";
        public const string UpdateAccount = "platform.update-account";
        public const string DeleteAccount = "platform.delete-account";
    }
    public static class Agreements
    {
        public const string Open = "agreements.open";
        public const string List = "agreements.list";
        public const string ReadAll = "agreements.read-all";
        public const string Create = "agreements.create";
        public const string Read = "agreements.read";
        public const string Update = "agreements.update";
        public const string ManageSharing = "agreements.manage-sharing";
    }
    public static class Buildings
    {
        public const string Open = "buildings.open";
        public const string Create = "buildings.create";
        public const string Read = "buildings.read";
        public const string Update = "buildings.update";
        public const string Delete = "buildings.delete";
    }
    public static class Organizations
    {
        public const string Open = "organizations.open";
        public const string Create = "organizations.create";
        public const string Read = "organizations.read";
        public const string Update = "organizations.update";
        public const string Delete = "organizations.delete";
    }
    public static class Units
    {
        public const string Open = "units.open";
        public const string Create = "units.create";
        public const string Read = "units.read";
        public const string Update = "units.update";
        public const string Delete = "units.delete";
    }
    public static class Occupancies
    {
        public const string Read = "occupancies.read";
        public const string Open = "occupancies.open";
        public const string Create = "occupancies.create";
        public const string Update = "occupancies.update";
        public const string End = "occupancies.end";
        public const string Delete = "occupancies.delete";
    }
    public static class ServiceDefinitions
    {
        public const string Open = "servicedefinitions.open";
        public const string Create = "servicedefinitions.create";
        public const string Read = "servicedefinitions.read";
        public const string Update = "servicedefinitions.update";
        public const string Delete = "servicedefinitions.delete";
    }
    public static class ServiceRequests
    {
        public const string Open = "servicerequests.open";
        public const string Approve = "servicerequests.approve";
        public const string Complete = "servicerequests.complete";
    }
    public static class Leasing
    {
        public const string Open = "leasing.open";
        public const string ApproveOrders = "leasing.approve-orders";
        public const string ApproveLimit = "leasing.approve-limit";
        public const string ActivatePlans = "leasing.activate-plans";
        public const string ApproveInvoices = "leasing.approve-invoices";
        public const string AcceptVariances = "leasing.accept-variances";
        public const string MaintainEquipment = "leasing.maintain-equipment";
        public const string ManageLifecycle = "leasing.manage-lifecycle";
        public const string ApproveLifecycle = "leasing.approve-lifecycle";
        public const string ReadReports = "leasing.read-reports";
        public const string ExportReports = "leasing.export-reports";
    }
    public static class MeetingRooms
    {
        public const string Open = "meetingrooms.open";
        public const string Manage = "meetingrooms.manage";
    }
    public static class Navigation
    {
        public const string Home = "navigation.home";
        public const string TenantPortal = "navigation.tenant-portal";
        public const string ProviderPortal = "navigation.provider-portal";
    }
    public static class AccountStructure
    {
        public const string Manage = "accountstructure.manage";
    }

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        Users.Manage,
        Platform.OpenAccounts,
        Platform.CreateAccount,
        Platform.ReadAccount,
        Platform.UpdateAccount,
        Platform.DeleteAccount,
        Agreements.Open,
        Agreements.List,
        Agreements.ReadAll,
        Agreements.Create,
        Agreements.Read,
        Agreements.Update,
        Agreements.ManageSharing,
        Buildings.Open,
        Buildings.Create,
        Buildings.Read,
        Buildings.Update,
        Buildings.Delete,
        Organizations.Open,
        Organizations.Create,
        Organizations.Read,
        Organizations.Update,
        Organizations.Delete,
        Units.Open,
        Units.Create,
        Units.Read,
        Units.Update,
        Units.Delete,
        Occupancies.Open,
        Occupancies.Read,
        Occupancies.Create,
        Occupancies.Update,
        Occupancies.End,
        Occupancies.Delete,
        ServiceDefinitions.Open,
        ServiceDefinitions.Create,
        ServiceDefinitions.Read,
        ServiceDefinitions.Update,
        ServiceDefinitions.Delete,
        ServiceRequests.Open,
        ServiceRequests.Approve,
        ServiceRequests.Complete,
        Leasing.Open,
        Leasing.ApproveOrders,
        Leasing.ApproveLimit,
        Leasing.ActivatePlans,
        Leasing.ApproveInvoices,
        Leasing.AcceptVariances,
        Leasing.MaintainEquipment,
        Leasing.ManageLifecycle,
        Leasing.ApproveLifecycle,
        Leasing.ReadReports,
        Leasing.ExportReports,
        MeetingRooms.Open,
        MeetingRooms.Manage,
        Navigation.Home,
        Navigation.TenantPortal,
        Navigation.ProviderPortal,
        AccountStructure.Manage,
    });
}
