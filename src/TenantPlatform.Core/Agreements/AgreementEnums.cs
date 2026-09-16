namespace TenantPlatform.Core.Agreements;

public enum AgreementType { Lease = 1, SoftwareLicense = 2, ServiceMaintenance = 3, Supplier = 4, Other = 5 }
public enum AgreementStatus { Draft = 1, Active = 2, Terminated = 3, Expired = 4 }
public enum AgreementAccessLevel { Read = 1, Edit = 2 }
public enum AgreementDocumentCategory { Contract = 1, Attachment = 2 }
