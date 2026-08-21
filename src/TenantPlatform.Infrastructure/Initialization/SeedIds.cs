namespace TenantPlatform.Infrastructure.Initialization;

public static class SeedIds
{
    // ============================================================
    // Accounts
    // ============================================================

    public static readonly Guid NordicPropertyAccount =
        Guid.Parse("10000000-0000-0000-0000-000000000001");


    // ============================================================
    // Organizations
    // ============================================================

    public static readonly Guid NordicPropertyOrganization =
        Guid.Parse("20000000-0000-0000-0000-000000000001");

    public static readonly Guid AcmeOrganization =
        Guid.Parse("20000000-0000-0000-0000-000000000002");

    public static readonly Guid ContosoOrganization =
        Guid.Parse("20000000-0000-0000-0000-000000000003");

    public static readonly Guid BravidaOrganization =
        Guid.Parse("20000000-0000-0000-0000-000000000004");

    public static readonly Guid CaverionOrganization =
        Guid.Parse("20000000-0000-0000-0000-000000000005");

    public static readonly Guid IssOrganization =
        Guid.Parse("20000000-0000-0000-0000-000000000006");


    // ============================================================
    // Buildings
    // ============================================================

    public static readonly Guid OsloAtrium =
        Guid.Parse("30000000-0000-0000-0000-000000000001");


    // ============================================================
    // Units
    // ============================================================

    public static readonly Guid Floor1 =
        Guid.Parse("40000000-0000-0000-0000-000000000001");

    public static readonly Guid Floor2 =
        Guid.Parse("40000000-0000-0000-0000-000000000002");

    public static readonly Guid Unit101 =
        Guid.Parse("40000000-0000-0000-0000-000000000003");

    public static readonly Guid Unit201 =
        Guid.Parse("40000000-0000-0000-0000-000000000004");

    public static readonly Guid Unit202 =
        Guid.Parse("40000000-0000-0000-0000-000000000005");


    // ============================================================
    // Occupancies
    // ============================================================

    public static readonly Guid AcmeOccupancy =
        Guid.Parse("50000000-0000-0000-0000-000000000001");

    public static readonly Guid ContosoOccupancy =
        Guid.Parse("50000000-0000-0000-0000-000000000002");


    // ============================================================
    // Users
    // ============================================================

    public static readonly Guid PerPedersen =
        Guid.Parse("60000000-0000-0000-0000-000000000001");

    public static readonly Guid OleOlsen =
        Guid.Parse("60000000-0000-0000-0000-000000000002");

    public static readonly Guid KariHansen =
        Guid.Parse("60000000-0000-0000-0000-000000000003");

    public static readonly Guid ServiceProviderDemoUser =
        Guid.Parse("60000000-0000-0000-0000-000000000004");

    public static readonly Guid MortenEnholm =
        Guid.Parse("60000000-0000-0000-0000-000000000005");

    // ============================================================
    // User roles
    // ============================================================

    public static readonly Guid PerPropertyAdminRole =
        Guid.Parse("70000000-0000-0000-0000-000000000001");

    public static readonly Guid OleTenantAdminRole =
        Guid.Parse("70000000-0000-0000-0000-000000000002");

    public static readonly Guid KariTenantUserRole =
        Guid.Parse("70000000-0000-0000-0000-000000000003");

    public static readonly Guid BravidaServiceProviderUserRole =
        Guid.Parse("70000000-0000-0000-0000-000000000004");


    // ============================================================
    // User accounts
    // ============================================================

    public static readonly Guid PerNordicPropertyUserAccount =
        Guid.Parse("71000000-0000-0000-0000-000000000001");

    public static readonly Guid OleNordicPropertyUserAccount =
        Guid.Parse("71000000-0000-0000-0000-000000000002");

    public static readonly Guid KariNordicPropertyUserAccount =
        Guid.Parse("71000000-0000-0000-0000-000000000003");

    public static readonly Guid ServiceProviderNordicPropertyUserAccount =
        Guid.Parse("71000000-0000-0000-0000-000000000004");


    // ============================================================
    // Login accounts
    // ============================================================

    public static readonly Guid PerLoginAccount =
        Guid.Parse("72000000-0000-0000-0000-000000000001");

    public static readonly Guid OleLoginAccount =
        Guid.Parse("72000000-0000-0000-0000-000000000002");

    public static readonly Guid KariLoginAccount =
        Guid.Parse("72000000-0000-0000-0000-000000000003");

    public static readonly Guid ServiceProviderLoginAccount =
        Guid.Parse("72000000-0000-0000-0000-000000000004");

    public static readonly Guid MortenLoginAccount =
        Guid.Parse("72000000-0000-0000-0000-000000000005");


    // ============================================================
    // Account admin roles
    // ============================================================

    public static readonly Guid PerNordicPropertyAccountAdminRole =
        Guid.Parse("73000000-0000-0000-0000-000000000001");


    // ============================================================
    // Service definitions
    // ============================================================

    public static readonly Guid NewSsidService =
        Guid.Parse("80000000-0000-0000-0000-000000000001");

    public static readonly Guid AccessCardService =
        Guid.Parse("80000000-0000-0000-0000-000000000002");

    public static readonly Guid ParkingService =
        Guid.Parse("80000000-0000-0000-0000-000000000003");

    public static readonly Guid FacilityFaultService =
        Guid.Parse("80000000-0000-0000-0000-000000000004");

    public static readonly Guid ExtraCleaningService =
        Guid.Parse("80000000-0000-0000-0000-000000000005");


    // ============================================================
    // Service definition translations
    // ============================================================

    public static readonly Guid NewSsidServiceNorwegian =
        Guid.Parse("81000000-0000-0000-0000-000000000001");

    public static readonly Guid NewSsidServiceEnglish =
        Guid.Parse("81000000-0000-0000-0000-000000000002");

    public static readonly Guid AccessCardServiceNorwegian =
        Guid.Parse("81000000-0000-0000-0000-000000000003");

    public static readonly Guid AccessCardServiceEnglish =
        Guid.Parse("81000000-0000-0000-0000-000000000004");

    public static readonly Guid ParkingServiceNorwegian =
        Guid.Parse("81000000-0000-0000-0000-000000000005");

    public static readonly Guid ParkingServiceEnglish =
        Guid.Parse("81000000-0000-0000-0000-000000000006");

    public static readonly Guid FacilityFaultServiceNorwegian =
        Guid.Parse("81000000-0000-0000-0000-000000000007");

    public static readonly Guid FacilityFaultServiceEnglish =
        Guid.Parse("81000000-0000-0000-0000-000000000008");

    public static readonly Guid ExtraCleaningServiceNorwegian =
        Guid.Parse("81000000-0000-0000-0000-000000000009");

    public static readonly Guid ExtraCleaningServiceEnglish =
        Guid.Parse("81000000-0000-0000-0000-000000000010");


    // ============================================================
    // Service definition fields
    // ============================================================

    public static readonly Guid AccessCardEmployeeNameField =
        Guid.Parse("82000000-0000-0000-0000-000000000001");

    public static readonly Guid AccessCardEmployeeEmailField =
        Guid.Parse("82000000-0000-0000-0000-000000000002");

    public static readonly Guid AccessCardValidFromField =
        Guid.Parse("82000000-0000-0000-0000-000000000003");

    public static readonly Guid AccessCardValidToField =
        Guid.Parse("82000000-0000-0000-0000-000000000004");

    public static readonly Guid AccessCardAccessLevelField =
        Guid.Parse("82000000-0000-0000-0000-000000000005");

    public static readonly Guid ParkingLicensePlateField =
        Guid.Parse("82000000-0000-0000-0000-000000000006");

    public static readonly Guid ParkingValidFromField =
        Guid.Parse("82000000-0000-0000-0000-000000000007");

    public static readonly Guid ParkingValidToField =
        Guid.Parse("82000000-0000-0000-0000-000000000008");

    public static readonly Guid FacilityFaultDescriptionField =
        Guid.Parse("82000000-0000-0000-0000-000000000009");

    public static readonly Guid FacilityFaultUrgentField =
        Guid.Parse("82000000-0000-0000-0000-000000000010");

    public static readonly Guid ExtraCleaningDescriptionField =
        Guid.Parse("82000000-0000-0000-0000-000000000011");

    public static readonly Guid ExtraCleaningRequestedDateField =
        Guid.Parse("82000000-0000-0000-0000-000000000012");

    // ============================================================
    // Service definition field translations
    // ============================================================

    public static readonly Guid AccessCardEmployeeNameFieldNb =
        Guid.Parse("82100000-0000-0000-0000-000000000001");

    public static readonly Guid AccessCardEmployeeNameFieldEn =
        Guid.Parse("82100000-0000-0000-0000-000000000002");

    public static readonly Guid AccessCardEmployeeEmailFieldNb =
        Guid.Parse("82100000-0000-0000-0000-000000000003");

    public static readonly Guid AccessCardEmployeeEmailFieldEn =
        Guid.Parse("82100000-0000-0000-0000-000000000004");

    public static readonly Guid AccessCardValidFromFieldNb =
        Guid.Parse("82100000-0000-0000-0000-000000000005");

    public static readonly Guid AccessCardValidFromFieldEn =
        Guid.Parse("82100000-0000-0000-0000-000000000006");

    public static readonly Guid AccessCardValidToFieldNb =
        Guid.Parse("82100000-0000-0000-0000-000000000007");

    public static readonly Guid AccessCardValidToFieldEn =
        Guid.Parse("82100000-0000-0000-0000-000000000008");

    public static readonly Guid AccessCardAccessLevelFieldNb =
        Guid.Parse("82100000-0000-0000-0000-000000000009");

    public static readonly Guid AccessCardAccessLevelFieldEn =
        Guid.Parse("82100000-0000-0000-0000-000000000010");

    public static readonly Guid ParkingLicensePlateFieldNb =
        Guid.Parse("82100000-0000-0000-0000-000000000011");

    public static readonly Guid ParkingLicensePlateFieldEn =
        Guid.Parse("82100000-0000-0000-0000-000000000012");

    public static readonly Guid ParkingValidFromFieldNb =
        Guid.Parse("82100000-0000-0000-0000-000000000013");

    public static readonly Guid ParkingValidFromFieldEn =
        Guid.Parse("82100000-0000-0000-0000-000000000014");

    public static readonly Guid ParkingValidToFieldNb =
        Guid.Parse("82100000-0000-0000-0000-000000000015");

    public static readonly Guid ParkingValidToFieldEn =
        Guid.Parse("82100000-0000-0000-0000-000000000016");

    public static readonly Guid FacilityFaultDescriptionFieldNb =
        Guid.Parse("82100000-0000-0000-0000-000000000017");

    public static readonly Guid FacilityFaultDescriptionFieldEn =
        Guid.Parse("82100000-0000-0000-0000-000000000018");

    public static readonly Guid FacilityFaultUrgentFieldNb =
        Guid.Parse("82100000-0000-0000-0000-000000000019");

    public static readonly Guid FacilityFaultUrgentFieldEn =
        Guid.Parse("82100000-0000-0000-0000-000000000020");

    public static readonly Guid ExtraCleaningDescriptionFieldNb =
        Guid.Parse("82100000-0000-0000-0000-000000000021");

    public static readonly Guid ExtraCleaningDescriptionFieldEn =
        Guid.Parse("82100000-0000-0000-0000-000000000022");

    public static readonly Guid ExtraCleaningRequestedDateFieldNb =
        Guid.Parse("82100000-0000-0000-0000-000000000023");

    public static readonly Guid ExtraCleaningRequestedDateFieldEn =
        Guid.Parse("82100000-0000-0000-0000-000000000024");
        
    // ============================================================
    // Service definition providers
    // ============================================================

    public static readonly Guid NewSsidBravidaProvider =
        Guid.Parse("83000000-0000-0000-0000-000000000001");

    public static readonly Guid NewSsidCaverionProvider =
        Guid.Parse("83000000-0000-0000-0000-000000000002");

    public static readonly Guid AccessCardBravidaProvider =
        Guid.Parse("83000000-0000-0000-0000-000000000003");

    public static readonly Guid ParkingIssProvider =
        Guid.Parse("83000000-0000-0000-0000-000000000004");

    public static readonly Guid FacilityFaultBravidaProvider =
        Guid.Parse("83000000-0000-0000-0000-000000000005");

    public static readonly Guid ExtraCleaningIssProvider =
        Guid.Parse("83000000-0000-0000-0000-000000000006");


    // ============================================================
    // Network environments
    // ============================================================

    public static readonly Guid CorporateLan =
        Guid.Parse("90000000-0000-0000-0000-000000000001");


    // ============================================================
    // SSIDs
    // ============================================================

    public static readonly Guid AcmeCorpSsid =
        Guid.Parse("91000000-0000-0000-0000-000000000001");

    public static readonly Guid ContosoSsid =
        Guid.Parse("91000000-0000-0000-0000-000000000002");


    // ============================================================
    // Service requests
    // ============================================================

    public static readonly Guid AcmeSsidChangeRequest =
        Guid.Parse("a0000000-0000-0000-0000-000000000001");

    public static readonly Guid AcmeSsidChangeRequestDetails =
        Guid.Parse("a1000000-0000-0000-0000-000000000001");
}
