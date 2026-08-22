using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Networking;
using TenantPlatform.Core.Occupancies;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;
using TenantPlatform.Core.Services;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Infrastructure.Authentication;
using TenantPlatform.Core.Localization;

namespace TenantPlatform.Infrastructure.Initialization;


// TODO: fikse så det blir en upsert og ikke bare insert av seed data. Se SeedBuildingsAsync for eksempel på upsert

public static class DemoDataInitializer
{

    public static async Task InitializeAsync(
        TenantPlatformDbContext dbContext,
        PasswordService passwordService,
        CancellationToken cancellationToken = default)
    {

            await SeedAccountAsync(
                dbContext,
                cancellationToken);
            await SeedOrganizationsAsync(
                dbContext,
                cancellationToken);
            await SeedBuildingsAsync(
                dbContext,
                cancellationToken);
            await SeedUnitsAsync(
                dbContext,
                cancellationToken);
            await SeedOccupanciesAsync(
                dbContext,
                cancellationToken);
            await SeedServiceDefinitionsAsync(
                dbContext,
                cancellationToken);
            await SeedServiceDefinitionTranslationsAsync(
                dbContext,
                cancellationToken);
            await SeedServiceDefinitionFieldsAsync(
                dbContext,
                cancellationToken);
            await SeedServiceDefinitionFieldTranslationsAsync(
                dbContext,
                cancellationToken);
            await SeedServiceProvidersAsync(
                dbContext,
                cancellationToken);
            await SeedUsersAsync(dbContext, cancellationToken);
            await SeedUserAccountsAsync(dbContext, cancellationToken);
            await SeedUserRolesAsync(dbContext, cancellationToken);
            await SeedLoginAccountsAsync(dbContext, passwordService, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedAccountAsync(

        TenantPlatformDbContext dbContext,

        CancellationToken cancellationToken)

    {
        if (await dbContext.Accounts.AnyAsync(
            x => x.Id == SeedIds.NordicPropertyAccount,
            cancellationToken))
        {
            return;
        }
        dbContext.Accounts.Add(
            new Account
            {
                Id = SeedIds.NordicPropertyAccount,
                Name = "Nordic Property",
                DefaultLanguage = SupportedLanguages.NbNo,
                IsActive = true
            });
    }
    private static async Task SeedOrganizationsAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var organizations = new[]
        {
            new Organization
            {
                Id = SeedIds.NordicPropertyOrganization,
                AccountId = SeedIds.NordicPropertyAccount,
                Name = "Nordic Property AS",
                OrganizationNumber = "999111222",
                Type = OrganizationType.PropertyManager,
                IsActive = true
            },
            new Organization
            {
                Id = SeedIds.AcmeOrganization,
                AccountId = SeedIds.NordicPropertyAccount,
                Name = "Acme Consulting AS",
                OrganizationNumber = "999222333",
                Type = OrganizationType.Tenant,
                IsActive = true
            },
            new Organization
            {
                Id = SeedIds.ContosoOrganization,
                AccountId = SeedIds.NordicPropertyAccount,
                Name = "Contoso AS",
                OrganizationNumber = "999333444",
                Type = OrganizationType.Tenant,
                IsActive = true
            },
            new Organization
            {
                Id = SeedIds.BravidaOrganization,
                AccountId = SeedIds.NordicPropertyAccount,
                Name = "Bravida Norge AS",
                OrganizationNumber = "987582561",
                Type = OrganizationType.ServiceProvider,
                IsActive = true
            },
            new Organization
            {
                Id = SeedIds.CaverionOrganization,
                AccountId = SeedIds.NordicPropertyAccount,
                Name = "Caverion Norge AS",
                OrganizationNumber = "959069743",
                Type = OrganizationType.ServiceProvider,
                IsActive = true
            },
            new Organization
            {
                Id = SeedIds.IssOrganization,
                AccountId = SeedIds.NordicPropertyAccount,
                Name = "ISS Facility Services AS",
                OrganizationNumber = "914791723",
                Type = OrganizationType.ServiceProvider,
                IsActive = true
            }
        };
        foreach (var organization in organizations)
        {
            if (!await dbContext.Organizations.AnyAsync(
                x => x.Id == organization.Id,
                cancellationToken))
            {
                dbContext.Organizations.Add(organization);
            }
        }
    }

    private static async Task SeedBuildingsAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Buildings.AnyAsync(
            x => x.Id == SeedIds.OsloAtrium,
            cancellationToken))
        {
            return;
        }
        dbContext.Buildings.Add(
            new Building
            {
                Id = SeedIds.OsloAtrium,
                AccountId = SeedIds.NordicPropertyAccount,
                Name = "Oslo Atrium",
                AddressLine1 = "Dronning Eufemias gate 1",
                PostalCode = "0191",
                City = "Oslo",
                CountryCode = "NO",
                IsActive = true
            });
    }
    private static async Task SeedUnitsAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var units = new[]
        {
            new Unit
            {
                Id = SeedIds.Floor1,
                AccountId = SeedIds.NordicPropertyAccount,
                BuildingId = SeedIds.OsloAtrium,
                Name = "1. etasje",
                Type = UnitType.Floor,
                IsActive = true
            },
            new Unit
            {
                Id = SeedIds.Floor2,
                AccountId = SeedIds.NordicPropertyAccount,
                BuildingId = SeedIds.OsloAtrium,
                Name = "2. etasje",
                Type = UnitType.Floor,
                IsActive = true
            },
            new Unit
            {
                Id = SeedIds.Unit101,
                AccountId = SeedIds.NordicPropertyAccount,
                BuildingId = SeedIds.OsloAtrium,
                ParentUnitId = SeedIds.Floor1,
                Name = "1.101",
                Type = UnitType.Office,
                IsActive = true
            },
            new Unit
            {
                Id = SeedIds.Unit201,
                AccountId = SeedIds.NordicPropertyAccount,
                BuildingId = SeedIds.OsloAtrium,
                ParentUnitId = SeedIds.Floor2,
                Name = "2.101",
                Type = UnitType.Office,
                IsActive = true
            },
            new Unit
            {
                Id = SeedIds.Unit202,
                AccountId = SeedIds.NordicPropertyAccount,
                BuildingId = SeedIds.OsloAtrium,
                ParentUnitId = SeedIds.Floor2,
                Name = "2.102",
                Type = UnitType.Office,
                IsActive = true
            }
        };
        foreach (var unit in units)
        {
            if (!await dbContext.Units.AnyAsync(
                x => x.Id == unit.Id,
                cancellationToken))
            {
                dbContext.Units.Add(unit);
            }
        }
    }
    private static async Task SeedOccupanciesAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var occupancies = new[]
        {
            new Occupancy
            {
                Id = SeedIds.AcmeOccupancy,
                AccountId = SeedIds.NordicPropertyAccount,
                TenantOrganizationId = SeedIds.AcmeOrganization,
                UnitId = SeedIds.Unit201,
                ValidFrom = new DateOnly(2026, 1, 1)
            },
            new Occupancy
            {
                Id = SeedIds.ContosoOccupancy,
                AccountId = SeedIds.NordicPropertyAccount,
                TenantOrganizationId = SeedIds.ContosoOrganization,
                UnitId = SeedIds.Unit202,
                ValidFrom = new DateOnly(2026, 1, 1)
            }
        };
        foreach (var occupancy in occupancies)
        {
            if (!await dbContext.Occupancies.AnyAsync(
                x => x.Id == occupancy.Id,
                cancellationToken))
            {
                dbContext.Occupancies.Add(occupancy);
            }
        }
    }
    private static async Task SeedServiceDefinitionsAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            new ServiceDefinition
            {
                Id = SeedIds.NewSsidService,
                AccountId = SeedIds.NordicPropertyAccount,
                Code = "NEW_SSID",
                Category = "Network",
                HandlerType = "Networking.Ssid",
                RequiresApproval = false,
                IsBookableByTenant = true,
                RequiresOccupancy  = true,
                EstimatedDurationMinutes = 60,
                IsActive = true
            },
            new ServiceDefinition
            {
                Id = SeedIds.AccessCardService,
                AccountId = SeedIds.NordicPropertyAccount,
                Code = "ACCESS_CARD",
                Category = "Access",
                HandlerType = "Generic",
                RequiresApproval = true,
                IsBookableByTenant = true,
                RequiresOccupancy  = true,
                EstimatedDurationMinutes = 30,
                IsActive = true
            },
            new ServiceDefinition
            {
                Id = SeedIds.ParkingService,
                AccountId = SeedIds.NordicPropertyAccount,
                Code = "PARKING",
                Category = "Parking",
                HandlerType = "Generic",
                RequiresApproval = true,
                IsBookableByTenant = true,
                RequiresOccupancy  = true,
                EstimatedDurationMinutes = 15,
                IsActive = true
            },
            new ServiceDefinition
            {
                Id = SeedIds.FacilityFaultService,
                AccountId = SeedIds.NordicPropertyAccount,
                Code = "FACILITY_FAULT",
                Category = "Facility",
                HandlerType = "Generic",
                RequiresApproval = false,
                IsBookableByTenant = true,
                RequiresOccupancy  = false,
                IsActive = true
            },
            new ServiceDefinition
            {
                Id = SeedIds.ExtraCleaningService,
                AccountId = SeedIds.NordicPropertyAccount,
                Code = "EXTRA_CLEANING",
                Category = "Facility",
                HandlerType = "Generic",
                RequiresApproval = false,
                IsBookableByTenant = true,
                RequiresOccupancy  = true,
                EstimatedDurationMinutes = 120,
                IsActive = true
            }
        };
        foreach (var definition in definitions)
        {
            var existing =
                await dbContext.ServiceDefinitions
                    .SingleOrDefaultAsync(
                        x => x.Id == definition.Id,
                        cancellationToken);

            if (existing is null)
            {
                dbContext.ServiceDefinitions.Add(definition);
                continue;
            }

            if (existing.AccountId != definition.AccountId)
            {
                throw new InvalidOperationException(
                    $"Seed definition '{definition.Code}' belongs to a different account.");
            }

            existing.Code = definition.Code;
            existing.Category = definition.Category;
            existing.HandlerType = definition.HandlerType;
            existing.RequiresApproval = definition.RequiresApproval;
            existing.IsBookableByTenant = definition.IsBookableByTenant;
            existing.RequiresOccupancy = definition.RequiresOccupancy;
            existing.EstimatedDurationMinutes = definition.EstimatedDurationMinutes;
            existing.IsActive = definition.IsActive;
        }
    }
    private static async Task SeedServiceDefinitionFieldsAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var fields = new[]
        {
            new ServiceDefinitionField
            {
                Id = SeedIds.AccessCardEmployeeNameField,
                ServiceDefinitionId = SeedIds.AccessCardService,
                Key = "employee_name",
                FieldType = ServiceFieldType.Text,
                IsRequired = true,
                SortOrder = 10
            },
            new ServiceDefinitionField
            {
                Id = SeedIds.AccessCardEmployeeEmailField,
                ServiceDefinitionId = SeedIds.AccessCardService,
                Key = "employee_email",
                FieldType = ServiceFieldType.Text,
                IsRequired = true,
                SortOrder = 20
            },
            new ServiceDefinitionField
            {
                Id = SeedIds.AccessCardValidFromField,
                ServiceDefinitionId = SeedIds.AccessCardService,
                Key = "valid_from",
                FieldType = ServiceFieldType.Date,
                IsRequired = true,
                SortOrder = 30
            },
            new ServiceDefinitionField
            {
                Id = SeedIds.AccessCardValidToField,
                ServiceDefinitionId = SeedIds.AccessCardService,
                Key = "valid_to",
                FieldType = ServiceFieldType.Date,
                IsRequired = false,
                SortOrder = 40
            },
            new ServiceDefinitionField
            {
                Id = SeedIds.AccessCardAccessLevelField,
                ServiceDefinitionId = SeedIds.AccessCardService,
                Key = "access_level",
                FieldType = ServiceFieldType.Choice,
                IsRequired = true,
                SortOrder = 50,
                Options =
                    "[{\"value\": \"common\"},{\"value\": \"own_floor\"},{\"value\": \"extended\"}]"
            },
            new ServiceDefinitionField
            {
                Id = SeedIds.ParkingLicensePlateField,
                ServiceDefinitionId = SeedIds.ParkingService,
                Key = "license_plate",
                FieldType = ServiceFieldType.Text,
                IsRequired = true,
                SortOrder = 10
            },
            new ServiceDefinitionField
            {
                Id = SeedIds.ParkingValidFromField,
                ServiceDefinitionId = SeedIds.ParkingService,
                Key = "valid_from",
                FieldType = ServiceFieldType.Date,
                IsRequired = true,
                SortOrder = 20
            },
            new ServiceDefinitionField
            {
                Id = SeedIds.ParkingValidToField,
                ServiceDefinitionId = SeedIds.ParkingService,
                Key = "valid_to",
                FieldType = ServiceFieldType.Date,
                IsRequired = false,
                SortOrder = 30
            },
            new ServiceDefinitionField
            {
                Id = SeedIds.FacilityFaultDescriptionField,
                ServiceDefinitionId = SeedIds.FacilityFaultService,
                Key = "description",
                FieldType = ServiceFieldType.TextArea,
                IsRequired = true,
                SortOrder = 10
            },
            new ServiceDefinitionField
            {
                Id = SeedIds.FacilityFaultUrgentField,
                ServiceDefinitionId = SeedIds.FacilityFaultService,
                Key = "urgent",
                FieldType = ServiceFieldType.Boolean,
                IsRequired = false,
                SortOrder = 20
            },
            new ServiceDefinitionField
            {
                Id = SeedIds.ExtraCleaningDescriptionField,
                ServiceDefinitionId = SeedIds.ExtraCleaningService,
                Key = "description",
                FieldType = ServiceFieldType.TextArea,
                IsRequired = true,
                SortOrder = 10
            },
            new ServiceDefinitionField
            {
                Id = SeedIds.ExtraCleaningRequestedDateField,
                ServiceDefinitionId = SeedIds.ExtraCleaningService,
                Key = "requested_date",
                FieldType = ServiceFieldType.Date,
                IsRequired = true,
                SortOrder = 20
            }
        };
        foreach (var field in fields)
        {
            if (!await dbContext.ServiceDefinitionFields.AnyAsync(
                x => x.Id == field.Id,
                cancellationToken))
            {
                dbContext.ServiceDefinitionFields.Add(field);
            }
        }
    }

    private static async Task SeedServiceDefinitionFieldTranslationsAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var translations = new[]
        {
            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.AccessCardEmployeeNameFieldNb,
                ServiceDefinitionFieldId =
                    SeedIds.AccessCardEmployeeNameField,
                LanguageCode = SupportedLanguages.NbNo,
                Label = "Navn",
                Placeholder = "Ola Nordmann",
                HelpText = "Navnet på personen adgangskortet gjelder."
            },
            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.AccessCardEmployeeNameFieldEn,
                ServiceDefinitionFieldId =
                    SeedIds.AccessCardEmployeeNameField,
                LanguageCode = SupportedLanguages.EnGb,
                Label = "Name",
                Placeholder = "John Smith",
                HelpText = "Name of the person the access card is for."
            },

            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.AccessCardEmployeeEmailFieldNb,
                ServiceDefinitionFieldId =
                    SeedIds.AccessCardEmployeeEmailField,
                LanguageCode = SupportedLanguages.NbNo,
                Label = "E-post",
                Placeholder = "ola.nordmann@firma.no",
                HelpText = "E-postadressen til personen."
            },
            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.AccessCardEmployeeEmailFieldEn,
                ServiceDefinitionFieldId =
                    SeedIds.AccessCardEmployeeEmailField,
                LanguageCode = SupportedLanguages.EnGb,
                Label = "Email",
                Placeholder = "john.smith@company.com",
                HelpText = "Email address of the person."
            },

            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.AccessCardValidFromFieldNb,
                ServiceDefinitionFieldId =
                    SeedIds.AccessCardValidFromField,
                LanguageCode = SupportedLanguages.NbNo,
                Label = "Gyldig fra",
                Placeholder = "Velg dato",
                HelpText = "Dato adgangskortet skal bli aktivt."
            },
            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.AccessCardValidFromFieldEn,
                ServiceDefinitionFieldId =
                    SeedIds.AccessCardValidFromField,
                LanguageCode = SupportedLanguages.EnGb,
                Label = "Valid from",
                Placeholder = "Select date",
                HelpText = "Date when the access card should become active."
            },

            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.AccessCardValidToFieldNb,
                ServiceDefinitionFieldId =
                    SeedIds.AccessCardValidToField,
                LanguageCode = SupportedLanguages.NbNo,
                Label = "Gyldig til",
                Placeholder = "Velg dato",
                HelpText = "La stå tom dersom kortet ikke har sluttdato."
            },
            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.AccessCardValidToFieldEn,
                ServiceDefinitionFieldId =
                    SeedIds.AccessCardValidToField,
                LanguageCode = SupportedLanguages.EnGb,
                Label = "Valid to",
                Placeholder = "Select date",
                HelpText = "Leave empty if no end date is required."
            },

            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.AccessCardAccessLevelFieldNb,
                ServiceDefinitionFieldId =
                    SeedIds.AccessCardAccessLevelField,
                LanguageCode = SupportedLanguages.NbNo,
                Label = "Tilgangsnivå",
                Placeholder = "Velg tilgangsnivå",
                HelpText = "Velg ønsket tilgangsnivå."
            },
            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.AccessCardAccessLevelFieldEn,
                ServiceDefinitionFieldId =
                    SeedIds.AccessCardAccessLevelField,
                LanguageCode = SupportedLanguages.EnGb,
                Label = "Access level",
                Placeholder = "Select access level",
                HelpText = "Select the requested access level."
            },

            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.ParkingLicensePlateFieldNb,
                ServiceDefinitionFieldId =
                    SeedIds.ParkingLicensePlateField,
                LanguageCode = SupportedLanguages.NbNo,
                Label = "Registreringsnummer",
                Placeholder = "AB12345",
                HelpText = "Bilens registreringsnummer."
            },
            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.ParkingLicensePlateFieldEn,
                ServiceDefinitionFieldId =
                    SeedIds.ParkingLicensePlateField,
                LanguageCode = SupportedLanguages.EnGb,
                Label = "Licence plate",
                Placeholder = "AB12345",
                HelpText = "Vehicle registration number."
            },

            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.ParkingValidFromFieldNb,
                ServiceDefinitionFieldId =
                    SeedIds.ParkingValidFromField,
                LanguageCode = SupportedLanguages.NbNo,
                Label = "Gyldig fra",
                Placeholder = "Velg dato",
                HelpText = "Dato parkeringstillatelsen skal starte."
            },
            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.ParkingValidFromFieldEn,
                ServiceDefinitionFieldId =
                    SeedIds.ParkingValidFromField,
                LanguageCode = SupportedLanguages.EnGb,
                Label = "Valid from",
                Placeholder = "Select date",
                HelpText = "Date when the parking permit should become active."
            },

            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.ParkingValidToFieldNb,
                ServiceDefinitionFieldId =
                    SeedIds.ParkingValidToField,
                LanguageCode = SupportedLanguages.NbNo,
                Label = "Gyldig til",
                Placeholder = "Velg dato",
                HelpText = "La stå tom dersom parkeringstillatelsen er uten sluttdato."
            },
            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.ParkingValidToFieldEn,
                ServiceDefinitionFieldId =
                    SeedIds.ParkingValidToField,
                LanguageCode = SupportedLanguages.EnGb,
                Label = "Valid to",
                Placeholder = "Select date",
                HelpText = "Leave empty if the permit has no end date."
            },

            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.FacilityFaultDescriptionFieldNb,
                ServiceDefinitionFieldId =
                    SeedIds.FacilityFaultDescriptionField,
                LanguageCode = SupportedLanguages.NbNo,
                Label = "Beskrivelse",
                Placeholder = "Beskriv feilen...",
                HelpText = "Beskriv feilen så detaljert som mulig."
            },
            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.FacilityFaultDescriptionFieldEn,
                ServiceDefinitionFieldId =
                    SeedIds.FacilityFaultDescriptionField,
                LanguageCode = SupportedLanguages.EnGb,
                Label = "Description",
                Placeholder = "Describe the issue...",
                HelpText = "Describe the issue in as much detail as possible."
            },

            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.FacilityFaultUrgentFieldNb,
                ServiceDefinitionFieldId =
                    SeedIds.FacilityFaultUrgentField,
                LanguageCode = SupportedLanguages.NbNo,
                Label = "Haster",
                Placeholder = "Angi om saken haster",
                HelpText = "Kryss av dersom saken krever rask oppfølging."
            },
            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.FacilityFaultUrgentFieldEn,
                ServiceDefinitionFieldId =
                    SeedIds.FacilityFaultUrgentField,
                LanguageCode = SupportedLanguages.EnGb,
                Label = "Urgent",
                Placeholder = "Indicate whether this is urgent",
                HelpText = "Tick this box if the issue requires immediate attention."
            },

            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.ExtraCleaningDescriptionFieldNb,
                ServiceDefinitionFieldId =
                    SeedIds.ExtraCleaningDescriptionField,
                LanguageCode = SupportedLanguages.NbNo,
                Label = "Beskrivelse",
                Placeholder = "Beskriv hva som ønskes rengjort...",
                HelpText = "Beskriv omfanget av renholdsoppdraget."
            },
            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.ExtraCleaningDescriptionFieldEn,
                ServiceDefinitionFieldId =
                    SeedIds.ExtraCleaningDescriptionField,
                LanguageCode = SupportedLanguages.EnGb,
                Label = "Description",
                Placeholder = "Describe the requested cleaning...",
                HelpText = "Describe the scope of the requested cleaning."
            },

            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.ExtraCleaningRequestedDateFieldNb,
                ServiceDefinitionFieldId =
                    SeedIds.ExtraCleaningRequestedDateField,
                LanguageCode = SupportedLanguages.NbNo,
                Label = "Ønsket dato",
                Placeholder = "Velg dato",
                HelpText = "Ønsket dato for gjennomføring."
            },
            new ServiceDefinitionFieldTranslation
            {
                Id = SeedIds.ExtraCleaningRequestedDateFieldEn,
                ServiceDefinitionFieldId =
                    SeedIds.ExtraCleaningRequestedDateField,
                LanguageCode = SupportedLanguages.EnGb,
                Label = "Requested date",
                Placeholder = "Select date",
                HelpText = "Preferred date for the cleaning service."
            }
        };

        foreach (var translation in translations)
        {
            if (!await dbContext.ServiceDefinitionFieldTranslations
                .AnyAsync(
                    x => x.Id == translation.Id,
                    cancellationToken))
            {
                dbContext.ServiceDefinitionFieldTranslations.Add(
                    translation);
            }
        }
    }
    private static async Task SeedServiceProvidersAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var providers = new[]
        {
            new ServiceDefinitionProvider
            {
                Id = SeedIds.NewSsidBravidaProvider,
                AccountId = SeedIds.NordicPropertyAccount,
                ServiceDefinitionId = SeedIds.NewSsidService,
                ServiceProviderOrganizationId =
                    SeedIds.BravidaOrganization,
                IntegrationType =
                    ServiceProviderIntegrationType.Email,
                RequestEmailAddress =
                    "network@bravida.example",
                IsDefault = true,
                IsActive = true
            },
            new ServiceDefinitionProvider
            {
                Id = SeedIds.NewSsidCaverionProvider,
                AccountId = SeedIds.NordicPropertyAccount,
                ServiceDefinitionId = SeedIds.NewSsidService,
                ServiceProviderOrganizationId =
                    SeedIds.CaverionOrganization,
                IntegrationType =
                    ServiceProviderIntegrationType.Email,
                RequestEmailAddress =
                    "network@caverion.example",
                IsDefault = false,
                IsActive = true
            },
            new ServiceDefinitionProvider
            {
                Id = SeedIds.AccessCardBravidaProvider,
                AccountId = SeedIds.NordicPropertyAccount,
                ServiceDefinitionId = SeedIds.AccessCardService,
                ServiceProviderOrganizationId =
                    SeedIds.BravidaOrganization,
                IntegrationType =
                    ServiceProviderIntegrationType.Email,
                RequestEmailAddress =
                    "access@bravida.example",
                IsDefault = true,
                IsActive = true
            },
            new ServiceDefinitionProvider
            {
                Id = SeedIds.ParkingIssProvider,
                AccountId = SeedIds.NordicPropertyAccount,
                ServiceDefinitionId = SeedIds.ParkingService,
                ServiceProviderOrganizationId =
                    SeedIds.IssOrganization,
                IntegrationType =
                    ServiceProviderIntegrationType.Email,
                RequestEmailAddress =
                    "parking@iss.example",
                IsDefault = true,
                IsActive = true
            },
            new ServiceDefinitionProvider
            {
                Id = SeedIds.FacilityFaultBravidaProvider,
                AccountId = SeedIds.NordicPropertyAccount,
                ServiceDefinitionId =
                    SeedIds.FacilityFaultService,
                ServiceProviderOrganizationId =
                    SeedIds.BravidaOrganization,
                IntegrationType =
                    ServiceProviderIntegrationType.Email,
                RequestEmailAddress =
                    "facility@bravida.example",
                IsDefault = true,
                IsActive = true
            },
            new ServiceDefinitionProvider
            {
                Id = SeedIds.ExtraCleaningIssProvider,
                AccountId = SeedIds.NordicPropertyAccount,
                ServiceDefinitionId =
                    SeedIds.ExtraCleaningService,
                ServiceProviderOrganizationId =
                    SeedIds.IssOrganization,
                IntegrationType =
                    ServiceProviderIntegrationType.Email,
                RequestEmailAddress =
                    "cleaning@iss.example",
                IsDefault = true,
                IsActive = true
            }
        };
        foreach (var provider in providers)
        {
            if (!await dbContext.ServiceDefinitionProviders.AnyAsync(
                x => x.Id == provider.Id,
                cancellationToken))
            {
                dbContext.ServiceDefinitionProviders.Add(provider);
            }
        }
    }
 private static async Task SeedLoginAccountsAsync(
    TenantPlatformDbContext dbContext,
    PasswordService passwordService,
    CancellationToken cancellationToken)
{
    if (!await dbContext.LoginAccounts.AnyAsync(
            x => x.Id == SeedIds.PerLoginAccount,
            cancellationToken))
    {
        var perLoginAccount = new LoginAccount
        {
            Id = SeedIds.PerLoginAccount,
            UserId = SeedIds.PerPedersen,
            Email = "per@nordicproperty.example",
            IsEnabled = true,
            FailedLoginCount = 0,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        perLoginAccount.PasswordHash =
            passwordService.HashPassword(
                perLoginAccount,
                "ChangeMe123!");

        dbContext.LoginAccounts.Add(perLoginAccount);
    }

    if (!await dbContext.LoginAccounts.AnyAsync(
            x => x.Id == SeedIds.OleLoginAccount,
            cancellationToken))
    {
        var oleLoginAccount = new LoginAccount
        {
            Id = SeedIds.OleLoginAccount,
            UserId = SeedIds.OleOlsen,
            Email = "ole@acme.example",
            IsEnabled = true,
            FailedLoginCount = 0,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        oleLoginAccount.PasswordHash =
            passwordService.HashPassword(
                oleLoginAccount,
                "ChangeMe123!");

        dbContext.LoginAccounts.Add(oleLoginAccount);
    }

    if (!await dbContext.LoginAccounts.AnyAsync(
            x => x.Id == SeedIds.KariLoginAccount,
            cancellationToken))
    {
        var kariLoginAccount = new LoginAccount
        {
            Id = SeedIds.KariLoginAccount,
            UserId = SeedIds.KariHansen,
            Email = "kari@contoso.example",
            IsEnabled = true,
            FailedLoginCount = 0,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        kariLoginAccount.PasswordHash =
            passwordService.HashPassword(
                kariLoginAccount,
                "ChangeMe123!");

        dbContext.LoginAccounts.Add(kariLoginAccount);
    }

    if (!await dbContext.LoginAccounts.AnyAsync(
            x => x.Id == SeedIds.MortenLoginAccount,
            cancellationToken))
    {
        var mortenLoginAccount = new LoginAccount
        {
            Id = SeedIds.MortenLoginAccount,
            UserId = SeedIds.MortenEnholm,
            Email = "enholm@me.com",
            IsEnabled = true,
            FailedLoginCount = 0,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        mortenLoginAccount.PasswordHash =
            passwordService.HashPassword(
                mortenLoginAccount,
                "JallaBalla24");

        dbContext.LoginAccounts.Add(mortenLoginAccount);
    }

    if (!await dbContext.LoginAccounts.AnyAsync(
            x => x.Id == SeedIds.BravidaServiceLoginAccount,
            cancellationToken))
    {
        var bravidaLoginAccount = new LoginAccount
        {
            Id = SeedIds.BravidaServiceLoginAccount,
            UserId = SeedIds.BravidaService,
            Email = "service@magida.org",
            IsEnabled = true,
            FailedLoginCount = 0,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        bravidaLoginAccount.PasswordHash =
            passwordService.HashPassword(
                bravidaLoginAccount,
                "JallaBalla24");

        dbContext.LoginAccounts.Add(bravidaLoginAccount);
    }


    await dbContext.SaveChangesAsync(cancellationToken);
}

    private static async Task SeedUsersAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Users.AnyAsync(
                x => x.Id == SeedIds.PerPedersen,
                cancellationToken))
        {
            dbContext.Users.Add(new User
            {
                Id = SeedIds.PerPedersen,
                Email = "per@nordicproperty.example",
                FirstName = "Per",
                LastName = "Pedersen",
                IsPlatformAdmin = false,
                PreferredLanguage = SupportedLanguages.NbNo,
                IsActive = true
            });
        }

        if (!await dbContext.Users.AnyAsync(
                x => x.Id == SeedIds.OleOlsen,
                cancellationToken))
        {
            dbContext.Users.Add(new User
            {
                Id = SeedIds.OleOlsen,
                Email = "ole@acme.example",
                FirstName = "Ole",
                LastName = "Olsen",
                IsPlatformAdmin = false,
                PreferredLanguage = SupportedLanguages.NbNo,
                IsActive = true
            });
        }

        if (!await dbContext.Users.AnyAsync(
                x => x.Id == SeedIds.KariHansen,
                cancellationToken))
        {
            dbContext.Users.Add(new User
            {
                Id = SeedIds.KariHansen,
                Email = "kari@contoso.example",
                FirstName = "Kari",
                LastName = "Hansen",
                IsPlatformAdmin = false,
                PreferredLanguage = SupportedLanguages.EnGb,
                IsActive = true
            });
        }

        if (!await dbContext.Users.AnyAsync(
                x => x.Id == SeedIds.MortenEnholm,
                cancellationToken))
        {
            dbContext.Users.Add(new User
            {
                Id = SeedIds.MortenEnholm,
                Email = "enholm@me.com",
                FirstName = "Morten",
                LastName = "Enholm",
                IsPlatformAdmin = true,
                PreferredLanguage = SupportedLanguages.EnGb,
                IsActive = true
            });
        }

        if (!await dbContext.Users.AnyAsync(
                x => x.Id == SeedIds.BravidaService,
                cancellationToken))
        {
            dbContext.Users.Add(new User
            {
                Id = SeedIds.BravidaService,
                Email = "service@magida.org",
                FirstName = "Bravida",
                LastName = "Service",
                IsPlatformAdmin = false,
                PreferredLanguage = SupportedLanguages.NbNo,
                IsActive = true
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedUserRolesAsync(
    TenantPlatformDbContext dbContext,
    CancellationToken cancellationToken)
    {
        if (!await dbContext.UserAccountRoles.AnyAsync(
                x => x.Id == SeedIds.PerPropertyAdminRole,
                cancellationToken))
        {
            dbContext.UserAccountRoles.Add(new UserAccountRole
            {
                Id = SeedIds.PerPropertyAdminRole,
                UserAccountId = SeedIds.PerNordicPropertyUserAccount,
                BuildingId = SeedIds.OsloAtrium,
                Role = UserRole.PropertyAdmin
            });
        }

        if (!await dbContext.UserAccountRoles.AnyAsync(
                x => x.Id == SeedIds.PerNordicPropertyAccountAdminRole,
                cancellationToken))
        {
            dbContext.UserAccountRoles.Add(new UserAccountRole
            {
                Id = SeedIds.PerNordicPropertyAccountAdminRole,
                UserAccountId = SeedIds.PerNordicPropertyUserAccount,
                Role = UserRole.AccountAdmin
            });
        }

        if (!await dbContext.UserAccountRoles.AnyAsync(
                x => x.Id == SeedIds.OleTenantAdminRole,
                cancellationToken))
        {
            dbContext.UserAccountRoles.Add(new UserAccountRole
            {
                Id = SeedIds.OleTenantAdminRole,
                UserAccountId = SeedIds.OleNordicPropertyUserAccount,
                OrganizationId = SeedIds.AcmeOrganization,
                Role = UserRole.TenantAdmin
            });
        }

        if (!await dbContext.UserAccountRoles.AnyAsync(
                x => x.Id == SeedIds.KariTenantUserRole,
                cancellationToken))
        {
            dbContext.UserAccountRoles.Add(new UserAccountRole
            {
                Id = SeedIds.KariTenantUserRole,
                UserAccountId = SeedIds.KariNordicPropertyUserAccount,
                OrganizationId = SeedIds.ContosoOrganization,
                Role = UserRole.TenantUser
            });
        }

        if (!await dbContext.UserAccountRoles.AnyAsync(
                x => x.Id == SeedIds.BravidaServiceProviderUserRole,
                cancellationToken))
        {
            dbContext.UserAccountRoles.Add(new UserAccountRole
            {
                Id = SeedIds.BravidaServiceProviderUserRole,
                UserAccountId = SeedIds.BravidaServiceProviderUserAccount,
                OrganizationId = SeedIds.BravidaOrganization,
                Role = UserRole.ServiceProviderUser
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedServiceDefinitionTranslationsAsync(
        TenantPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var translations = new[]
        {
            new ServiceDefinitionTranslation
            {
                Id = SeedIds.NewSsidServiceNorwegian,
                ServiceDefinitionId = SeedIds.NewSsidService,
                LanguageCode = SupportedLanguages.NbNo,
                Name = "Bestill nytt Wi-Fi-nettverk",
                Description =
                    "Bestill et nytt trådløst nettverk for virksomheten."
            },

            new ServiceDefinitionTranslation
            {
                Id = SeedIds.NewSsidServiceEnglish,
                ServiceDefinitionId = SeedIds.NewSsidService,
                LanguageCode = SupportedLanguages.EnGb,
                Name = "Order new Wi-Fi network",
                Description =
                    "Order a new wireless network for your organisation."
            },

            new ServiceDefinitionTranslation
            {
                Id = SeedIds.AccessCardServiceNorwegian,
                ServiceDefinitionId = SeedIds.AccessCardService,
                LanguageCode = SupportedLanguages.NbNo,
                Name = "Bestill adgangskort",
                Description =
                    "Bestill nytt adgangskort eller endre tilgang."
            },

            new ServiceDefinitionTranslation
            {
                Id = SeedIds.AccessCardServiceEnglish,
                ServiceDefinitionId = SeedIds.AccessCardService,
                LanguageCode = SupportedLanguages.EnGb,
                Name = "Order access card",
                Description =
                    "Order a new access card or change access rights."
            },

            new ServiceDefinitionTranslation
            {
                Id = SeedIds.ParkingServiceNorwegian,
                ServiceDefinitionId = SeedIds.ParkingService,
                LanguageCode = SupportedLanguages.NbNo,
                Name = "Bestill parkeringsplass",
                Description =
                    "Bestill eller endre parkering."
            },

            new ServiceDefinitionTranslation
            {
                Id = SeedIds.ParkingServiceEnglish,
                ServiceDefinitionId = SeedIds.ParkingService,
                LanguageCode = SupportedLanguages.EnGb,
                Name = "Order parking",
                Description =
                    "Order or modify parking."
            },

            new ServiceDefinitionTranslation
            {
                Id = SeedIds.FacilityFaultServiceNorwegian,
                ServiceDefinitionId = SeedIds.FacilityFaultService,
                LanguageCode = SupportedLanguages.NbNo,
                Name = "Meld feil",
                Description =
                    "Rapporter feil eller mangler i bygget."
            },

            new ServiceDefinitionTranslation
            {
                Id = SeedIds.FacilityFaultServiceEnglish,
                ServiceDefinitionId = SeedIds.FacilityFaultService,
                LanguageCode = SupportedLanguages.EnGb,
                Name = "Report fault",
                Description =
                    "Report a building fault or maintenance issue."
            },

            new ServiceDefinitionTranslation
            {
                Id = SeedIds.ExtraCleaningServiceNorwegian,
                ServiceDefinitionId = SeedIds.ExtraCleaningService,
                LanguageCode = SupportedLanguages.NbNo,
                Name = "Bestill ekstra renhold",
                Description =
                    "Bestill ekstra renhold utover ordinær leveranse."
            },

            new ServiceDefinitionTranslation
            {
                Id = SeedIds.ExtraCleaningServiceEnglish,
                ServiceDefinitionId = SeedIds.ExtraCleaningService,
                LanguageCode = SupportedLanguages.EnGb,
                Name = "Order extra cleaning",
                Description =
                    "Order additional cleaning services."
            }
        };

        foreach (var translation in translations)
        {
            if (!await dbContext.ServiceDefinitionTranslations.AnyAsync(
                    x => x.Id == translation.Id,
                    cancellationToken))
            {
                dbContext.ServiceDefinitionTranslations.Add(
                    translation);
            }
        }
    }
   
    private static async Task SeedUserAccountsAsync(
    TenantPlatformDbContext dbContext,
    CancellationToken cancellationToken)
    {
        if (!await dbContext.UserAccounts.AnyAsync(
                x => x.Id == SeedIds.PerNordicPropertyUserAccount,
                cancellationToken))
        {
            dbContext.UserAccounts.Add(new UserAccount
            {
                Id = SeedIds.PerNordicPropertyUserAccount,
                UserId = SeedIds.PerPedersen,
                AccountId = SeedIds.NordicPropertyAccount
            });
        }

        if (!await dbContext.UserAccounts.AnyAsync(
                x => x.Id == SeedIds.OleNordicPropertyUserAccount,
                cancellationToken))
        {
            dbContext.UserAccounts.Add(new UserAccount
            {
                Id = SeedIds.OleNordicPropertyUserAccount,
                UserId = SeedIds.OleOlsen,
                AccountId = SeedIds.NordicPropertyAccount
            });
        }

        if (!await dbContext.UserAccounts.AnyAsync(
                x => x.Id == SeedIds.KariNordicPropertyUserAccount,
                cancellationToken))
        {
            dbContext.UserAccounts.Add(new UserAccount
            {
                Id = SeedIds.KariNordicPropertyUserAccount,
                UserId = SeedIds.KariHansen,
                AccountId = SeedIds.NordicPropertyAccount
            });
        }

        if (!await dbContext.UserAccounts.AnyAsync(
                x => x.Id == SeedIds.BravidaServiceProviderUserAccount,
                cancellationToken))
        {
            dbContext.UserAccounts.Add(new UserAccount
            {
                Id = SeedIds.BravidaServiceProviderUserAccount,
                UserId = SeedIds.BravidaService,
                AccountId = SeedIds.NordicPropertyAccount
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}