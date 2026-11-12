# Unified Dimensions — fase 1: kartlegging og referansegrunnlag

Dato: 12. november 2026. Status: **FERDIG for fase 1**. Funksjonen er ikke implementert; fase 2 er ikke startet. Rapporten og de frosne referansefilene er leveransen. Alle antall nedenfor gjelder generatorens logiske datasett ved vellykket førstegangsinitialisering, ikke observerte databaseposter.

## 1. Arbeidsgrunnlag og avgrensning

- Arbeidskatalog: `/Users/morten/Dev/TenantPlatform`.
- Branch: `main`. Kildecommit: `f2f2d67338fd05e59624423aad675ec4f8ca415d`.
- `git status --porcelain=v1` var tom ved start. Ingen eksisterende brukerendringer ble funnet. Ingen branchbytte, commit, merge, push eller deploy er utført.
- Kravgrunnlag: [unified-dimensions.md](unified-dimensions.md), særlig §§ 3–12 og 15–17. Brukerens faseprompt overstyrer dokumentets instruksjon om full autonom implementering. AgentOrchestrator er ikke konfigurert eller startet.
- De innlimte AGENTS.md-instruksjonene er fulgt. Ingen fysisk `AGENTS.md` ble funnet i checkouten eller overordnede kataloger. Ingen ytterligere lokale instruksjonsfiler, `Directory.Build.*` eller `develop.json` ble funnet. Ingen begrensninger er utvidet.
- Dokumentasjonskonvensjon: eksisterende Markdown under `docs/` og featurebeskrivelser under `docs/features/`. Nye evidensfiler ligger samlet under `docs/features/unified-dimensions-phase-1/`.
- Dagens løsning har **fire** prosjekter: Core, Infrastructure, Web og den separate `Serga.Website`. Det finnes **ti** konsollbaserte smoke-testprosjekter under `tests/`; de inngår ikke i `TenantPlatform.sln`. Dette avviker fra den eldre prosjektoversikten i de innlimte instruksjonene.
- Ingen funksjonell kildekode, prosjektavhengigheter, konfigurasjon, migrasjoner eller aktivt snapshot er endret. Dokumentasjonsverktøyet nedenfor er kun isolert evidensberegning.

## 2. Arkitektur med konkrete kilder

Alle stier i dette avsnittet er relative til repo-roten. Symbolene gjelder kildecommiten ovenfor; linjenumre for legacy-treff finnes i maskinoversikten.

### Dimensions og Leasing

| Område | Fil og symboler | Dagens oppførsel og konsekvens |
|---|---|---|
| Felles modell | `src/TenantPlatform.Core/Dimensions/Dimension.cs`: `Dimension`, `DimensionValue`, `DimensionHistory` | Kontoavgrensede ID-er; stabil `Code`, fallback `Name`/`Description`, `AllowMultiple`, `LeafOnly`, `IsActive`, `SortOrder`, `Revision`. Verdier har `ParentId`, eget navn/kode/aktivitet/revisjon. Historikk har aktør/tid/før- og etter-JSON. Ingen dimensjons- eller verdioversettelser finnes nå. |
| Hierarki | `src/TenantPlatform.Web/Services/Dimensions/DimensionHierarchy.cs`: `Build`, `AllowedParents`, `DimensionValueOption` | Iterativ dybde-først, ingen fast maksimal dybde; konto- og dimensjonsfiltrering. Sortering etter `SortOrder`, navn, ID. Aktivitet arves gjennom forfedre; `Selectable` krever aktiv sti og eventuelt blad. Syklus eller utilgjengelig forelder gir valideringsfeil. Hele redigert deltre utelukkes som foreldrevalg. |
| Vedlikehold | `src/TenantPlatform.Web/Services/Dimensions/DimensionService.cs`: `Require`, `GetAsync`, `SaveDimensionAsync`, `SaveValueAsync`, `Revision`, `History`, `Lock` | Kortlivet factory-context. Krever valgt konto, aktivt medlemskap og `CanManageAccountStructureAsync`. Skriving låser kontorad i transaksjon, validerer koder/navn/foreldre, sjekker revisjon og skriver historikk. Ingen sletteoperasjon. `GetAsync` er administrativ og skal ikke brukes som eneste lesekatalog for ordinære avtalebrukere. |
| Administrasjon | `src/TenantPlatform.Web/Components/Pages/Dimensions/DimensionsPage.razor`: `DetailTab`, `Reload`, `SaveRule`; øvrige `Dimension*`-komponenter i samme mappe | `/settings/dimensions`, `InteractiveServer`. Master-detail med Egenskaper, Bruk i Leasing, Verdier, Historikk. Modal kopimodell i `DimensionEditorDialog`; `DimensionRulesSection` er eksisterende mønster for ny avtalefane. |
| Leasing-modell | `src/TenantPlatform.Core/Leasing/LeasingDimensions.cs`: `LeasingDimensionRule`, `LeasingDimensionSelection`, `LeasingAllocationDimension`, `LeasingAllocationRow` | Tilgjengelig/obligatorisk/kostnadsfordeling eies av Leasing. Tildelinger er felles eller per fordelingsrad. Navn/kode/verdisti lagres som historiske snapshots. Dette er ikke legacy OrganizationElement og skal bevares. |
| Leasing-tjeneste | `src/TenantPlatform.Web/Services/Leasing/LeasingService.Classification.cs`: `Catalog`, `ClassificationCatalogAsync`, `SaveDimensionRuleAsync`, `MissingDimensions`, `ApplyClassification`; `LeasingClassificationDtos.cs` | Account-scoped katalog med felles hierarkiberegning. Ugyldig regelkombinasjon avvises server-side; kontolås, revisjon og `DimensionHistory`. Eksplisitt reklassifisering validerer aktiv/tilgjengelig, single/multiple/leaf og fordelingsregler. Fordeling krever single-select dimensjon. |
| Historiske Leasing-valg | `src/TenantPlatform.Web/Services/Leasing/LeasingService.cs`: `SaveAcquisitionCore` rundt linje 305–333; `LeasingClassificationInput.From`; `DimensionChoices.razor`, `LeasingClassificationEditor.razor` | Eksisterende klassifisering beholdes når `classifications` ikke inneholder ny klassifisering for linjen; ny linje/registrering har egen valideringssti. UI inkluderer valgte inaktive verdier. Å åpne editor og sende eksplisitt klassifisering er en annen operasjon enn en urelatert endring. Dette skillet må bevares. |
| Kostnadsberegning | `src/TenantPlatform.Core/Leasing/LeasingAllocationCalculator.cs`: `Calculate`; `LeasingCalculator` | None/Percent/NetAmount/Quantity, eksakt måloppfyllelse, kumulativ avrunding av netto/MVA/antall, særregel for null netto. Skal ikke endres eller kopieres inn i Agreements. |
| Rapportering | `src/TenantPlatform.Web/Services/Leasing/LeasingService.Reports.cs`: `GetLeasingReportAsync`; `LeasingReportExport.cs` | Bruker dimensjonstildelingenes historiske snapshots; har fysiske lokasjons-/utstyrsdata som skal bevares. Oversettelse av nåværende dimensjonstittel må ikke omskrive lagret historikk eller endre beløp. |

### Språk og oversettelser

- `src/TenantPlatform.Core/Localization/SupportedLanguages.cs`: `NbNo="nb-NO"`, `EnGb="en-GB"`, `SvSe="sv-SE"`; alle tre i `All`.
- `LanguageHelper.Normalize` normaliserer eksakte koder og prefiksene nb/no, en og sv; tom eller ukjent kode blir **en-GB**. `IsSupported` godtar eksakte støttede koder, uavhengig av store/små bokstaver.
- `src/TenantPlatform.Core/Accounts/Account.cs`: `DefaultLanguage` er **nb-NO** som standard. `AccountService` validerer valgte kontospråk. `Program.cs` rundt linje 66–76 registrerer tre kulturer og norsk request-standard; kulturcookie og brukerens `PreferredLanguage` brukes ved innlogging/språkbytte.
- Eksisterende lagringsmønster: `src/TenantPlatform.Core/Services/ServiceDefinitionTranslation.cs`, `ServiceDefinitionFieldTranslation.cs` og tilsvarende `*TranslationConfiguration.cs` under Infrastructure. Egen oversettelsesrad med språk, navn, beskrivelse; unik kombinasjon av eier-ID og språk. `ServiceDefinitionService.GetServiceDefinitionsAsync` og `ServiceCatalogService` leser kontostandard og oversettelser.
- **Viktig designforskjell:** `TranslationHelper.Select` velger ønsket språk → normalisert kontostandard → en-GB → første oversettelse. Kravet for Dimensions er ønsket språk → kontostandard *hvis tilgjengelig* → lagret fallback → kode (kun navn). Direkte bruk av dagens helper vil derfor ikke alltid oppfylle kravet. Anbefal et bakoverkompatibelt, eksplisitt fallbackvalg i eksisterende lokaliseringsområde; behold dagens helperoppførsel for eksisterende kall. Tomme navn/beskrivelser må håndteres per felt, ikke bare per valgt oversettelsesrad. Ikke normaliser manglende kontostandard til engelsk før fallback.
- Ressurser ligger i `src/TenantPlatform.Web/Resources/TenantPlatformResources.{nb-NO,en-GB,sv-SE}.resx`. Nye UI-tekster skal inn i alle tre; lagrede navn på dimensjonsverdier videreføres uten nytt oversettelsessystem.

### Agreements, tilgang og samtidighet

| Fil/symbol | Relevant ansvar |
|---|---|
| `src/TenantPlatform.Core/Agreements/Agreement.cs` | To nullable legacy-FK-er `OrganizationElementId`, `GeographicAreaId`, samtidig som `CounterpartyOrganizationId`, `BuildingId`, `UnitId`, økonomi, livssyklus og revisjon er selvstendige forretningsdata. |
| `src/TenantPlatform.Web/Services/Agreements/AgreementDtos.cs` | `SaveAgreementRequest`, arvet `AgreementDetailsDto`, `AgreementOptionsDto`, `AgreementOptionDto`, `AgreementFilter`. Dagens kontrakter bærer de to klassifiserings-ID-ene, navn og valglister. |
| `IAgreementService.cs` og `AgreementService.cs` | `CreateAsync`, `UpdateAsync`, `GetAsync`, `GetOptionsAsync`, `ListAsync`, `Apply`, `ValidateAsync`, `SaveAsync`. Create krever account-admin via `CanCreateAgreementAsync`; options ved edit krever redigeringsrett. Lagring og lesing bruker eksplisitt konto. |
| `AgreementService.cs`: `RequireMemberAsync`, `Accessible`, `RequireAsync` | Valgt konto må matche, bruker må være aktivt medlem. Account-admin, eier eller eksplisitt `AgreementAccess` avgrenser lesing; Edit-tilgang for redigering; eier/admin for tilgangsstyring/eierbytte. Arkiv sperrer skriving. Platform-admin er ikke automatisk account-admin. |
| `AgreementService.Structure.cs`: `GetStructureOptionsAsync`, `ValidateStructureAsync` | Lager full sti inkludert inaktive forfedre; nye valg filtreres på nodens aktivitet, gjeldende inaktiv tildeling beholdes. Uendret inaktiv ID tillates ved edit. Merk at felles `DimensionHierarchy` også arver inaktivitet fra forfedre, strengere enn legacy-valideringen. |
| `AgreementService.cs`: `CheckRevision`, `Touch`, `SaveAsync`; `AgreementService.Adjustments.cs`: `LockFinancialAsync` | GUID-revisjon, EF concurrency token, oversettelse av 23505/23503 og concurrency-feil til eksisterende ressursnøkler. Update starter transaksjon og låser konto før avtale. Create har ikke denne kontolåsen nå; ny konfigurasjon/required-validering må samordnes med regelendringer uten å endre autorisasjonsmodellen. |
| `Components/Pages/Agreements/AgreementEdit.razor` | Create- og edit-ruter, `InteractiveServer`, options lastes fra tjeneste. Ved valideringsfeil beholdes `model`; server-feil vises via ressursnøkkel. |
| `AgreementFields.razor`, `AgreementDetails.razor` | Felles skjema har to dedikerte klassifiseringsvalg; details viser to stier. Ny generisk seksjon må også bevare inaktive/utilgjengelige eksisterende valg. Bygg/enhet-plassering er et annet feltområde. |
| `src/TenantPlatform.Web/Security/Authorization/TenantAuthorizationService.cs`, `Security/CurrentUserContext/CurrentUserContextService.cs` | Eksisterende roller og valgt konto beholdes. Ikke stol på circuit-cache alene; tjenestene sjekker medlemskap mot kortlivet context. |

`GetAsync` kan dessuten initialisere avtalefrister via `AgreementDeadlineInitializer` når `DeadlinesInitialized` er false. Den er derfor ikke en egnet «ufarlig» lesesti for evidensuttrekk. Ingen slik metode ble kalt i kartleggingen.

### Dashboard

- `AgreementService.Dashboard.cs`: `GetInsightsAsync` bruker `RequireMemberAsync` + `Accessible(...).Where(!IsArchived)`; alle statuser inkluderes. Type, Counterparty, Status og Owner er faste SQL-grupperinger. Owner-gruppering begrenser medlemsnavn til konto; Counterparty bruker kontoavgrensede Organizations.
- Klassifisering er i dag hardkodet gjennom `AgreementInsightDimension.Geography/Organization` i `AgreementDashboardDtos.cs`, `IAgreementService.cs` og `Components/Pages/Dashboard/InsightsWidget.razor`.
- `AgreementDashboardQueries.GroupHierarchy` tar **én nullable verdi per avtale**, bygger rotnivå eller nærmeste barn og en separat `Direct`-gruppe for direkte foreldretildelinger. Ikke angitt finnes kun på toppnivå. Dette eksisterende mønsteret er nyttig, men signaturen mangler AgreementId og kan ikke deduplisere fremtidige flervalg.
- `AgreementInsightsDto.Total` er nå `Groups.Sum(Count)`. Widgeten bruker dette både som total og prosentnevner. Med flervalg må unik avtaletotal skilles fra summen av viste søskengrupper; en avtale kan bidra til flere søsken, men bare én gang i hver node.
- Non-admin får i dag bare hierarkinoder som er tildelt synlige avtaler og deres forfedre; ukjent/ikke tillatt valgt node avvises. Behold dette vern mot navnelekkasje, også i ny dynamisk katalog.
- Nåværende hierarkifilter er en nedtrekksliste, ikke en komplett drill-down/back-modell. `selected` nullstilles ved dimensjons-/kontobytte. Nye dynamiske valg må identifiseres med GUID, ikke lokaliserte navn eller numeriske enumverdier.
- `AgreementFilter`/`ListAsync` håndterer søk, status, type, eier, Attention og paginering. Attention-widgetens klikkfilter og `GetAttentionAsync`/`GetDashboardContractsAsync` må bevares. Insights har i dag eget hierarkifilter og uarkivert-scope, ikke et generelt felles filterobjekt.

### EF Core og migrasjoner

- `src/TenantPlatform.Infrastructure/Persistence/TenantPlatformDbContext.cs`: DbSets for både shared Dimensions, Leasing-klasser og legacy-register. `OnModelCreating` bruker `ApplyConfigurationsFromAssembly`; ingen global tenant query filter.
- `Persistence/Configurations/DimensionConfiguration.cs` inneholder konfigurasjoner for shared dimensjoner, verdier, historikk og Leasing-regler/tildelinger/fordeling. Unik `(AccountId, Code)` for dimensjoner; `(AccountId, DimensionId, Code)` for verdier. Account-aware alternate keys/FK-er på dimensjon, verdi og verdiens forelder. `Revision` er concurrency token. Restrict ved sletting.
- `AgreementConfiguration.cs` har `(AccountId, Id)` alternate key og account-aware FK-er til begge legacy-register og indeks. Flere eldre forretningsrelasjoner er ID-FK-er med eksplisitte serverkontroller; ikke bruk disse som begrunnelse for svakere FK-er i den nye tildelingsmodellen.
- `GeographicAreaConfiguration.cs`/`OrganizationElementConfiguration.cs` har account-aware selvreferanse, konto-FK og `(AccountId, Id)` alternate key.
- Aktivt snapshot er nøyaktig `src/TenantPlatform.Infrastructure/Persistence/Migrations/TenantPlatformDbContextModelSnapshot.cs`. Det inneholder fortsatt legacy-typer og kolonner og skal senere oppdateres, ikke behandles som historisk unntak.
- Siste migrasjon er `20261111175442_ReplaceLeasingVatFlagWithNetVat`. Migrasjonsmappen har **85 kildefiler**: 42 migrasjoner med Designer + ett snapshot. Bytekontrollsummer for alle finnes i referansepakken.
- Historisk kjede: `20260925121604_AddAccountDepartmentsAndLocations` oppretter `departments`/`locations`; `20260925123553_AddAccountRegisterHierarchies` omdøper til `organization_elements`/`geographic_areas` og legger til hierarki; `20260925125228_AddAgreementOrganizationAndGeography` legger til avtalekoblinger. `20260925205419_AddSharedDimensionsAndLeasingAllocation` etablerer shared rammeverk.
- `src/TenantPlatform.Infrastructure/Auditing/AuditSaveChangesInterceptor.cs` registrerer konto, aktør, tid og endrede egenskaper; støtter sammensatte nøkler. Modulkonfigurasjon bør også følge eksisterende eksplisitte `DimensionHistory`-mønster i samme transaksjon.
- `scripts/add-migration.sh` bruker Infrastructure som prosjekt og Web som startup. Ingen `IDesignTimeDbContextFactory` er funnet. Før senere scaffolding må design-time-stien bevises databasefri; vanlig Web-start må ikke brukes til dette. Ved behov planlegges en snever design-time factory med offline options før scriptet brukes. Ikke kjør scriptet i fase 1. `scripts/update-database.sh` er ikke kjørt og skal ikke brukes uten separat instruksjon.

## 3. Komplett legacy-avhengighetsoversikt

[legacy-inventory.json](unified-dimensions-phase-1/legacy-inventory.json) er en frossen, maskinlesbar linjeoversikt over hele det sporede checkout-innholdet ved kildecommiten: obligatoriske typenavn/ID-navn, tabellnavn, tidligere Department/Location-navn, felles kontrakter/hierarki, dashboardklassifisering, ressursnøkler og ruter. Oversikten dekker 66 filer og 637 trefflinjer. Hvert treff har filsti, linje, tekst og kategori. Brede Location/Department-treff er med for gjennomgang, ikke automatisk sletteliste. I tillegg ble aktive `src/`, `tests/` og repoet søkt med `rg`.

### Aktiv kode som må erstattes eller fjernes senere

| Filområde | Konkrete avhengigheter og behandling |
|---|---|
| Core/Accounts | `GeographicArea.cs`, `OrganizationElement.cs`: erstattes av shared DimensionValue; fjernes til slutt. |
| Core/Agreements | `Agreement.cs`: bare de to legacy-ID-feltene skal bort. |
| Infrastructure | `TenantPlatformDbContext.cs`: to DbSets; de to dedikerte konfigurasjonsfilene; legacy-FK-er i `AgreementConfiguration.cs`. Snapshot skal følge endelig modell. |
| Generator | `Initialization/DemoDataInitializer.cs`: `SeedMagidaAsync` lager begge hele trærne, og 300 avtaler bruker begge FK-er. `SeedIds.cs` har egne legacy-ID-fabrikker; behold bare ved begrunnet bruk som stabil identitet i konverteringen, ikke som parallelle legacy-generatorer. |
| Konto-innstillinger | `Web/Services/AccountSettings/{GeographicAreaService,OrganizationElementService,AccountRegisterInput,AccountRegisterHierarchy,AccountHierarchyException}.cs`. De to tjenestene og de to sidene kan fjernes; input/exception og hierarkihjelper må først få siste forbruker erstattet (Dashboard/Agreement-options/tester). |
| UI og DI | `Components/Pages/AccountSettings/{GeographicAreas,OrganizationElements}.razor`; rutene `/settings/geography`, `/settings/organization-structure`; `Components/Layout/NavMenu.razor` både lenker og `initialGroup`-gjenkjenning; to `AddScoped`-registreringer i Web/Program.cs. |
| Avtaler | `AgreementDtos.cs`; `AgreementService.Structure.cs`; mapping/options/Apply/Validate i `AgreementService.cs`; `AgreementFields.razor`, `AgreementDetails.razor`. `AgreementEdit.razor` berøres indirekte gjennom delt modell og valglister selv uten direkte typenavntreff. |
| Dashboard | `AgreementService.Dashboard.cs`, `AgreementDashboardDtos.cs`, `IAgreementService.cs`, `InsightsWidget.razor`: hardkodet enum, spørringer, hierarkikontrakt og UI må erstattes samlet. `Organization` i dashboard-enum betyr klassifisering; `Counterparty` betyr den reelle forretningsorganisasjonen. |
| **Andre moduler: konto** | `Web/Services/Accounts/AccountService.cs:CanDeleteAccountAsync` linje 167–168 sperrer sletting ved legacy-data. Erstatt med relevante nye account-owned data-kontroller; ikke fjern sperren uten erstatning. Eksisterende klasse har ikke Dimensions-kontroll her. |
| Tester | `tests/TenantPlatform.{AccountRegisters,Agreements,Dashboard}.SmokeTests/Program.cs` har direkte legacy-avhengigheter; oppdater/fjern bare de foreldede kontrollene og viderefør sikkerhet, historie, hierarki og FK-dekning. Leasing-testene bruker felles admin-UI og generiske dimensjoner og trenger regresjon selv uten legacy-entityreferanser. |
| Ressurser | Alle tre `.resx`: `OrganizationElements*`, `GeographicAreas*`, `AgreementOrganizationElement`, `AgreementGeographicArea`, `AgreementInvalid*`, `DashboardDimensionGeography/Organization`. Tidligere Departments/Locations-nøkler har rester. Slett først etter kontroll av faktiske kall, inklusive interpolerte ressursnavn. |

### Referanser som skal bevares eller behandles særskilt

- **Historiske migrasjoner er urørlige**, inkludert Designer-filer. Legacy-treff der er forventet også etter fase 7. Oversikten skiller aktivt snapshot fra historikk. Kontrollsummer gjør bevaring etterprøvbar.
- `Core/Organizations/Organization.cs`, `OrganizationType`, `OrganizationService`, `/organizations`, CounterpartyOrganizationId og alle reelle utleiere/leietakere/leverandører/finansieringsselskaper skal beholdes. Generatorens 16 `Demo Partner …` er reelle Organization-forretningsposter i datasettet.
- `Core/Properties` (Building/Unit), adresse/land/poststed, Occupancy, ServiceRequest-lokasjon og `ServiceRequestLocationDto`/`ValidateLocationAsync` er ikke GeographicArea. `LeasingEquipment.BuildingId`, `LifeLocation`, rapportkolonnen `Location` og fysisk utstyrsplassering er også uavhengige data.
- `Serga.Website` har markedsføringstekst om organizations/locations uten legacy-typeavhengighet; behold den. Nettstedet er med i solution-bygget.
- `DimensionValuesSection.razor` bruker **`AccountHierarchyAddChild`** som delt ressursnøkkel. Behold/omdøp eksplisitt ved behov; ikke slett alle `AccountHierarchy*`-nøkler fordi navnet høres legacy ut.
- Leasing-testdata kalt «Department» og kode `DEPT` i `LifecycleChecks` er vanlige Dimensions, ikke OrganizationElement. Tilsvarende generiske Department-etiketter i fordeling er gyldige. `AgreementTimeZones` sitt locations-navn er ikke legacy-klassifisering.
- Ingen andre aktive moduler med direkte GeographicArea-/OrganizationElement-relasjoner ble funnet utover konto-innstillinger, Agreements, Dashboard, demo og kontoslettingssjekken. Leasing berøres av felles oversettelses-/adminflate og regresjon, ikke av legacy-FK-konvertering.

## 4. Frosset demodatareferanse

### Filer, proveniens og bruk

- [demo-reference.json](unified-dimensions-phase-1/demo-reference.json): **alle** 66 legacy-verdier med ID, seed-symbol, navn, nivå, forelder, genereringsrekkefølge, sti og forfedre; **alle 300** avtaler med begge eksplisitte tildelinger og avledet medlemskap; komplette avtalefelter med entitetsstandarder, 600 linjer med versjoner/priser, indeksvalg, 16 motparter, 4 indekser, 8 indeksverdier og fordeling for hver node.
- [derive-reference.py](unified-dimensions-phase-1/derive-reference.py): Python-standardbibliotek og lokale, lesende Git-kall. Leser **den fastlåste kildecommiten**, aldri en fremtidig arbeidskopi av generatoren. Navn og literal-arrays hentes fra frossen C#-kilde; heltalls-/datoformlene er en eksplisitt, gjennomgått transkripsjon av `SeedMagidaAsync`. Ingen C# eller initializer eksekveres. Dette er et kildebasert oracle, ikke et databaseuttrekk eller en generell C#-tolker.
- [migration-source-sha256.json](unified-dimensions-phase-1/migration-source-sha256.json): SHA-256 for eksakte Git-blobbytes i alle 85 eksisterende migrasjons-/snapshotfiler.
- `demo-reference.json.source_sha256` dekker samtlige fem filer i `Infrastructure/Initialization` (inkludert SeedIds, Leasing-partial, DatabaseInitializer og PlatformDataInitializer) de syv relevante Agreement-modellfilene, de to legacy-verditypene, Account, Organization/OrganizationType og SupportedLanguages. Slik kan formler, enumverdier og standardfelt spores til kilde.

Reproduser uten database eller nettverk fra repo-roten:

```sh
python3 docs/features/unified-dimensions-phase-1/derive-reference.py --check
```

`--check` beregner fra samme commit og sammenligner eksakte filbytes uten skriving. Uten flagget produseres evidensfilene på nytt fra **samme** commit. Senere tester skal konsumere den frosne JSON-filen og sammenligne den nye generatorens logiske output med den; de skal ikke lage forventninger fra den nye generatoren. Ikke oppdater baseline for å få en konverteringstest grønn. Reproduksjon krever at kildecommiten finnes i lokal Git-historikk; de ferdige JSON-filene er selvstendige lesbare referanser.

`demo-reference.json` SHA-256: `18c1e1083523112418b9f36bd8d4ab3c196cc86aac8924d4254ea64590a67701` (2 103 412 byte).

### Eksakte antall og kontoavhengighet

| Logiske poster | Magida | Nordic Property |
|---|---:|---:|
| GeographicArea | 36 = 4 verdensdeler + 16 land + 16 hovedsteder | 0 |
| OrganizationElement | 30 = 5 divisjoner + 25 avdelinger | 0 |
| Agreement | **300** | 0 |
| Eksplisitte geografikoblinger / organisasjonskoblinger | 300 / 300 | 0 / 0 |
| AgreementLine / AgreementLineVersion / AgreementPriceVersion | 600 / 600 / 600 | 0 / 0 / 0 |
| AgreementIndexSelection | 300 | 0 |
| Magida-motparter / indekser / indeksverdier | 16 / 4 / 8 | Ikke samme generator |

`InitializeAsync` kaller Magida-generatoren uten parameter for konto eller antall; det finnes ikke en konfigurasjonsavhengig variasjon av 300-løkken. Web-start kaller DatabaseInitializer som alltid migrerer og initialiserer plattformdata; demo inkluderes når miljøet er Development. Når demo er avslått genereres ingen av disse demopostene. Allerede eksisterende reserverte avtale-ID-er i **Magida-kontoen** hoppes over: nye avtaler blir `300 − antall eksisterende treff blant disse 300 ID-ene`. Kontototal kan være større, og eksisterende redigerte avtaler kan avvike fra baseline. Ingen sikker påstand om faktisk lagret antall kan gjøres uten database.

### Hele geografihierarkiet og fordelingen

Rotrekkefølgen i generatoren er Asia, Europa, Afrika, Amerika. Under hver rot følger landene i tabellen; hvert land etterfølges umiddelbart av sin hovedstad. Alle noder har `IsActive=true`, `Description=null`. Legacy har ikke Code eller SortOrder; «stabil nøkkel» er ID/SeedIds-symbol. Ingen ekstra global geografirot finnes.

| Rot (ID-kind 8, nr.) | Land (kind 9, nr.) | Hovedstad (kind 10, samme nr.) | Eksplisitt på hovedstad / medlemskap i landet |
|---|---|---|---:|
| Asia (1) | Japan (1) | Tokyo | 19 |
| Asia (1) | Kina (2) | Beijing | 19 |
| Asia (1) | India (3) | New Delhi | 19 |
| Asia (1) | Thailand (4) | Bangkok | 18 |
| Europa (2) | Norge (5) | Oslo | 19 |
| Europa (2) | Sverige (6) | Stockholm | 19 |
| Europa (2) | Tyskland (7) | Berlin | 19 |
| Europa (2) | Frankrike (8) | Paris | 18 |
| Afrika (3) | Kenya (9) | Nairobi | 19 |
| Afrika (3) | Egypt (10) | Kairo | 19 |
| Afrika (3) | Ghana (11) | Accra | 19 |
| Afrika (3) | Marokko (12) | Rabat | 18 |
| Amerika (4) | USA (13) | Washington, D.C. | 19 |
| Amerika (4) | Canada (14) | Ottawa | 19 |
| Amerika (4) | Brasil (15) | Brasília | 19 |
| Amerika (4) | Argentina (16) | Buenos Aires | 18 |

Verdensdeler har 75 unike avtaler hver gjennom etterkommere; verdensdeler/land har **0 eksplisitte** tildelinger. Hvert lands eneste barn er hovedstaden, så landets inklusive antall er lik hovedstadens. På nivå 1, 2 og 3 er summen hver for seg 300; man skal ikke summere nivåene som ulike avtaler.

### Hele organisasjonshierarkiet og fordelingen

Ingen felles virksomhetsrot finnes. Hver divisjon etterfølges av sine fem avdelinger i numerisk rekkefølge. Divisjonene har ID-kind 6 og avdelingene kind 7; avdelingens forelder er sin divisjon.

| Divisjonsnavn (nr.) | Alle avdelingsnavn (globale seed-numre) | Eksplisitt per avdeling / inklusive i divisjon |
|---|---|---:|
| Divisjon 1 – Eiendom | Avdeling 1.1 (1), Avdeling 1.2 (2), Avdeling 1.3 (3), Avdeling 1.4 (4), Avdeling 1.5 (5) | 12 / 60 |
| Divisjon 2 – Teknologi | Avdeling 2.1 (6), Avdeling 2.2 (7), Avdeling 2.3 (8), Avdeling 2.4 (9), Avdeling 2.5 (10) | 12 / 60 |
| Divisjon 3 – Drift | Avdeling 3.1 (11), Avdeling 3.2 (12), Avdeling 3.3 (13), Avdeling 3.4 (14), Avdeling 3.5 (15) | 12 / 60 |
| Divisjon 4 – Innkjøp | Avdeling 4.1 (16), Avdeling 4.2 (17), Avdeling 4.3 (18), Avdeling 4.4 (19), Avdeling 4.5 (20) | 12 / 60 |
| Divisjon 5 – Forretningsutvikling | Avdeling 5.1 (21), Avdeling 5.2 (22), Avdeling 5.3 (23), Avdeling 5.4 (24), Avdeling 5.5 (25) | 12 / 60 |

Divisjonene har 0 eksplisitte tildelinger. Alle 30 verdier er aktive, beskrivelse null. Legacy UI bruker `AccountRegisterHierarchy.Build` og kultursensitiv alfabetisk søskensortering, **ikke** generatorrekkefølge; begge forhold er dokumentert for senere valg av SortOrder.

### Full deterministisk mapping og ID-er

`SeedIds.MagidaId(entityKind, number)` er `Guid.Parse($"b0000000-{entityKind:x4}-0000-0000-{number:x12}")`: begge tall heksadesimale, ingen tilfeldighet eller ekstern seed. Magida-konto er `b0000000-0001-0000-0000-000000000001`.

For **hver** avtale med nummer `n=1..300`, sett `i=n−1` og bruk heltallsdivisjon:

```text
department = i / 12 + 1                   # 1..25
continentIndex = i % 4                    # 0..3
countryIndex = (i / 4) % 4                 # 0..3 innen verdensdelen
locationNumber = continentIndex*4 + countryIndex + 1
AgreementId = MagidaId(14, n)
OrganizationElementId = MagidaId(7, department)
GeographicAreaId = MagidaId(10, locationNumber)
```

Dette er hele mappingen, ikke et utvalg; alle 300 materialiserte rader finnes også i JSON. Fordelingen gjentar en 16-avtalers lokasjonssyklus 18 hele ganger og så 12 avtaler, mens avdelinger følger sammenhengende blokker på 12 avtaler. JSON inneholder separat forfedremedlemskap, ikke ekstra persisterte tildelinger. Det finnes ingen utilordnede avtaler i dette nygenererte datasettet.

ID-kindene er 6 divisjon, 7 avdeling, 8 verdensdel, 9 land, 10 hovedstad, 11 motpart, 12 indeks, 13 indeksverdi, 14 avtale, 15 indeksvalg, 16 avtalelinje, 17 linjeversjon og 18 prisversjon. Eksisterende shared Leasing-dimensjoner bruker kind 20, nr. 1–2 (`PROJ`, `EQUIP`), verdier kind 21, nr. 1–16; nye seed-ID-er må ikke kollidere med disse.

### Andre avtaleegenskaper og relasjoner som skal bevares

JSON inneholder samtlige Agreement-egenskaper med modellstandarder, og de eksplisitte linje-/pris-/indeksradene. Det er et **generatorøyeblikk før senere deadline-workers**, ikke et forventet snapshot av en ferdig kjørt applikasjon.

- Type beregnes med `(i%12 + department−1)%5`; fem navn og primær-/sekundærlinjenavn leses fra kilde. Hver av de fem typene får 60 avtaler. Tittel er `MAG-{n:000} {typenavn} – {hovedstad}`; beskrivelse angir avdeling. Første/siste er `MAG-001 Leieavtale – Tokyo` og `MAG-300 Leieavtale – Brasília`.
- Alle har Status Active og NOK. Income 152 / Cost 148, bestemt av `(i/4)%2`. Motpart bestemmes av lokasjonen, indeks av verdensdelen. Indeksvalg har Sequence=1 og avtalt startdato.
- Start er 1. i måned `(i%6)+1` i 2026. Varighet velges fra `[12,18,24,36,48,60]` med `(i/3)%6`. Form er FixedTerm/Renewing/Ongoing med 100 av hver. FixedTerm slutter dagen før start+varighet; Renewing har fornyelse start+varighet og frist tre måneder før; Ongoing har ingen sluttdato/fornyelse. CurrentPeriodStartDate=start. Renewing har AutoRenew og RenewalMonths; øvrige ikke.
- NoticeMode er BeforeRenewal kun for Renewing; NoticeCount/Unit er null for FixedTerm og 3/Months ellers. ReminderMode Disabled. Terms, BuildingId, UnitId, termination-felt er null; DeadlinesInitialized, IsArchived, IndexSetupNeedsReview er false; ReminderDays er tom. Konverteringen skal ikke «rette» disse standardene eller aktivere andre regler.
- To linjer per avtale med kind-16-numre `i*2+1/2`, én aktiv versjon og én prisversjon hver, Sequence=1, EffectiveFrom/FirstPayableDate/IndexBaseDate=start, indeksregulert. Calendar-anchor `0001-01-01`; frekvens fra Monthly/Quarterly/HalfYearly/Yearly med `(i/4+line)%4`; primær Advance, sekundær Arrears. EndDate følger avtalen.
- Primær kvantum `1+i%10`, pris `5000+(i%40)*1250`; sekundær kvantum 1, pris `750+(i%20)*250`. Ingen økonomiske beregninger skal endres av dimensjonskonverteringen.
- 4 årsindekser med basis 100 i 2026; 2027: Asia 104.2, Europa 102.5, Afrika 106.1, Amerika 103.4. Indeksverdiens PeriodKey er dens egen stabile ID, Revision=1.
- `recordedUtc=DateTimeOffset.UtcNow` fanges én gang i SeedMagidaAsync. JSON bruker `$recordedUtc` i alle relevante felt. Avtalens Revision er dens egen ID ved generering. AuditLog-ID-er/tid og passordhash er ikke deterministiske datasettverdier.
- Eier/aktør kan være eksisterende bruker funnet på ID **eller e-post**, og faktisk User.Id gjenbrukes. JSON bruker `$resolvedMagidaUserId`; i helt fersk graf er det `SeedIds.MagidaAdministrator` (kind 2, nr. 1). Ingen alternative ID-er gjettes.

### Idempotens og øvrig demo som må bevares

`AddMissingMagidaEntitiesAsync` laster eksisterende ID-er innen Magida og legger bare til manglende av de foreslåtte postene. Den oppdaterer ikke eksisterende navn eller foreldre. Avtaleløkken hopper over hele grafen, inkludert begge økonomilinjer, når avtale-ID finnes; den reparerer ikke manglende barn på eksisterende avtaler. Account/bruker/login/medlemskap/rolle har egne eksistenskontroller. Hele initializer har en eldre TODO om upsert; ikke tolk dette som bevist full idempotens.

`DemoDataInitializer.Leasing.cs` har andre ensure-mekanismer: dimensjoner/verdioppslag søker kode så ID; enkelte eksisterende egenskaper normaliseres og revisjon fornyes ved endring. Regler normaliseres til Leasing-tilgjengelig/ikke obligatorisk/ingen fordeling. **Ikke gjenbruk disse helperne uendret for GEO/DEPT:** de tvinger blant annet LeafOnly=true og aktiverer Leasing.

Leasing-demosettet skal stå urørt: PROJ (4 rotverdier), EQUIP (3 røtter + 9 blader), fem rammer, ni anskaffelser (fem selvstendige/fire rammetilknyttede; sju registrerte/ett utkast/én kansellert), 17 linjer med 34 dimensjonsvalg, finansierings-/betalings-/livssyklusdata. `tests/TenantPlatform.Leasing.SmokeTests/DemoSeedChecks.cs` har databasebaserte dobbel-initialiseringstester og kontroller av dette. De er inspisert, ikke kjørt. Nordic Propertys organisasjoner, bygg, enheter, occupancies, tjenester, oversettelser og brukergraf berøres ikke av klassifiseringsbyttet.

Sikkert uten database: kandidatantall, hierarkier, ID-formler, hver kobling, alle eksplisitt genererte felt, kildebaserte insert/skip-regler. **Ikke sikkert uten database:** faktisk gjenbrukt bruker-ID, allerede endrede hierarkier/avtaler, antall innsettinger i konkret database, integritets-/transaksjonsadferd, reell idempotens, migrasjonskjøring og senere workers. Referansen hevder ingen av disse kontrollene som passert.

## 5. Anbefalte designvalg og avklaringer før implementering

Dette er anbefalinger, ingen av dem er implementert.

1. Utvid eksisterende Dimensions med `DimensionTranslation` i Core, egen EF-konfigurasjon og account-aware `(AccountId, DimensionId)`-FK, unik `(AccountId, DimensionId, LanguageCode)`. Behold fallbackfelter. Bruk SupportedLanguages/LanguageHelper og eksisterende oversettelsesradmønster, med eksplisitt streng fallback slik avsnitt 2 beskriver. Inkluder oversettelser i revisjon og før-/etterhistorikk i samme transaksjon, ikke bare i vanlig audit. Ingen verdioversettelser introduseres.
2. Egen `AgreementDimensionRule` per `(AccountId, DimensionId)` med tilgjengelig/obligatorisk og GUID-revisjon; uavhengig av LeasingDimensionRule. Ingen kopi av AllowMultiple/LeafOnly. Required impliserer available; disabling fjerner required server-side. Manglende regel betyr utilgjengelig/ikke obligatorisk. Følg eksisterende account-admin-autorisasjon og kontolås.
3. `AgreementDimensionAssignment` med kun eksplisitte verdier og konto, avtale, dimensjon, verdi. Unik `(AccountId, AgreementId, DimensionId, ValueId)`, FK til avtalens `(AccountId, Id)` og verdiens `(AccountId, DimensionId, Id)`. Dette hindrer både cross-account og feil dimensjon/verdi i databasen. Servervalidering kommer i tillegg. AllowMultiple=false krever tjenestevalidering av hele dimensjonssettet og samtidighetsvern; unik verdiindeks alene er utilstrekkelig.
4. Edit-kontrakten må skille **utelatt dimensjon** fra **eksplisitt sendt tomt sett**. Anbefal en nullable patch/dictionary over dimensjoner som brukeren faktisk endrer; nøkkel med tom liste betyr eksplisitt tømming, manglende nøkkel betyr bevar. Create validerer alle obligatoriske tilgjengelige dimensjoner; edit validerer required bare for eksplisitt endrede dimensjoner. Ikke legg nye krav på gamle avtaler gjennom urelaterte endringer. Alle sendte ID-er valideres mot servertilstand og valgt konto.
5. Last eksisterende tildelinger som union med tilgjengelig katalog. Behold historiske inaktive/utilgjengelige valg og vis dem merket; de skal ikke forsvinne ved filtering eller DTO-mapping. Ved eksplisitt endring beholdes uendrede historiske ID-er; **nye** ID-er må være aktive, tilhøre korrekt konto/dimensjon og være selectable. Fjerning kan være eksplisitt, men required gjelder da. Regelendring fra multi til single eller til leaf-only skal ikke retroaktivt slette/inaktivere gamle tildelinger; urelaterte endringer må fortsatt fungere. Test disse kombinasjonene særskilt.
6. Samordne create/edit av tildelinger og konfigurasjonsendringer med eksisterende kontolås/revisjoner. Bruk samme låserekkefølge konto → avtale ved avtaleskriving. Ikke la required-konfigurasjonen endres mellom lesing og lagring. Oppdater avtale-revisjonen når tildelinger endres og behold eksisterende konfliktnøkler.
7. Dashboard må bære `(AgreementId, ValueId)` og beregne HashSet av avtale-ID-er per node. Ikke summer barn for å finne foreldertall. Behold separate unike avtaletotaler for synlig datasett/valgt deltre og `sum(displayedGroups.Count)` som prosentnevner. Flere søsken kan overlappe; delte forfedre teller avtalen én gang. Direkte foreldrevalg gir egen Direct-gruppe ved drill-down, ikke medlemskap i vilkårlige barn. En avtale som eksplisitt har både forelder og barn kan bidra til Direct og barn, men foreldrenodens unike total er fortsatt én. Test dette uttrykkelig. Ikke angitt bare på toppnivå; ingen finansiell dimensjonsaggregering.
8. GEO/DEPT blir vanlige aktive, single-select dimensjoner med LeafOnly=false, tilgjengelige/valgfrie i Agreements og uten automatisk Leasing-regel. Bevar helst de 66 eksisterende verdi-GUID-ene og bruk stabile kodeverdier fra seed-nøkkel (for eksempel `CONTINENT-001`/`COUNTRY-001`/`CAPITAL-001` og `DIVISION-001`/`DEPARTMENT-001`), ikke navn/kultur. Reserver nye dimensjons-ID-er utenfor PROJ/EQUIP. Sammenlign alle 300 avtaler mot JSON, ikke bare summer. Eventuell ID-remapping må være eksplisitt og fullstendig i testene.
9. En required-dimensjon som deaktiveres, eller mangler aktive valgbare verdier, gir konflikt mellom konfigurasjonen og nye tildelinger. Anbefal å håndheve create-required og gi en tydelig konfigurasjonsfeil til administratoren til regelen/verdiene er korrigert; ikke gjør required implisitt valgfri. Eksisterende avtaler og urelatert edit skal fortsatt fungere. Leasing `MissingDimensions` filtrerer heller ikke bort required på grunn av IsActive. Produktregelen for dette bør bekreftes i fase 3.
10. Generatorrekkefølge og gammel UI-sortering er forskjellige. Anbefal å bevare gammel alfabetisk visning ved like SortOrder, samtidig som kilderekkefølgen står i referansen; dersom tilsiktet produktrekkefølge er generatorrekkefølge, må dette velges eksplisitt i fase 5. Det er ikke en blocker for kartleggingen.

Reelle avklaringspunkter for neste fase er fallbackutvidelsens konkrete API/per-felt-håndtering, DTO-patchens form, navngivning/reservasjon av nye seed-ID-er sorteringsvalget og håndtering av required uten aktive valgbare verdier. Kravet avgjør allerede hovedreglene; det trengs ingen ekstra godkjenning for å levere denne kartleggingen. PostgreSQL-adferd og design-time-sikkerhet kan ikke erklæres verifisert på grunnlag av dagens bygg.

## 6. Konkret plan for fase 2–9

Hver fase leveres med byggbar kode og egne kontroller; fasene er ikke påbegynt. Mellomfasene er overgangstilstand, ikke akseptert ferdig funksjon. Ingen av dem forutsetter at applikasjonen eller demo-initialiseringen startes mens implementeringen er ufullstendig. Bruk Web til applikasjonstjenester, direkte EF og kortlivede contexts; ingen ny Application-prosjektstruktur.

### Fase 2 — Felles dimensjoner og oversettelser

**Filer:** Core/Dimensions/Dimension.cs og ny DimensionTranslation; Core/Localization/TranslationHelper.cs eller snever utvidelse samme sted; Infrastructure/Persistence/TenantPlatformDbContext.cs og ny oversettelseskonfigurasjon; Web/Services/Dimensions/DimensionService.cs; lesemodeller i LeasingService.Classification.cs/LeasingClassificationDtos.cs. Senere UI-konsum forberedes uten å omskrive historiske snapshots.

**Avhengigheter/design:** velg streng fallback med bakoverkompatibilitet; konto-FK og språkunikhet; transaksjon/revisjon/historikk dekker oversettelser. Avklar trygg offline design-time-sti før scaffolding.

**Migrasjonskilde:** innfør ny additiv migrasjon, foreslått navn `AddDimensionTranslations`, med `bash scripts/add-migration.sh AddDimensionTranslations` først når design-time er dokumentert databasefri; oppdater det navngitte aktive snapshotet. Ingen historiske filer endres; ingen migration application.

**Tester/ferdig:** nb-NO/en-GB/sv-SE, aliasnormalisering, manglende kontostandard, feltvis tom tekst, fallbacknavn/kode, tilgang og konto, stale revision/historikk. Rene tester må kunne kjøre separat fra PostgreSQL-runneren. Leasing-kalkulator og lagrede snapshots er uendret. Legacy-modeller og gamle DTO-felter står fortsatt, slik at alle forbrukere kompilerer.

### Fase 3 — Avtalekonfigurasjon og tildelingsregler

**Filer:** nye Core/Agreements/AgreementDimensionRule og AgreementDimensionAssignment; EF-konfigurasjoner/DbSets; Web/Services/Agreements/AgreementDtos.cs, IAgreementService.cs, AgreementService.cs og ny/erstattet klassifiseringspartial; eksisterende Structure-partial beholdes bare så lenge gamle UI-forbrukere trenger den.

**Avhengigheter/design:** fase 2; patch kontra utelatt dimensjon; historiske valg; kontolås og parent revision. Implementer deduplisering, konto/dimensjon/verdi, tilgjengelig/aktiv, single/multi/leaf, create-required og eksplisitt edit-required. Lesekatalog må autoriseres som avtaleoperasjon, ikke som registeradministrasjon.

**Migrasjonskilde:** ny additiv `AddAgreementDimensions` for regler/tildelinger, account-aware FK-er, unikhet og indekser; oppdater snapshot. Legacy-FK-er beholdes i denne fasen; ingen datakopiering fra eksisterende database.

**Tester/ferdig:** alle valideringskombinasjoner, cross-account/feil dimensjon, duplikat, foreldervalg, inaktive forfedre, historiske unavailable/inactive valg, tom patch, ny required på gammel avtale, stale revision og uendret øvrig avtalegraf. Uavhengighet fra Leasing verifiseres. DTO-er utvides additivt; gamle UI-/dashboardkontrakter kompilerer fram til senere utskifting.

### Fase 4 — Administrasjon og avtalegrensesnitt

**Filer:** Components/Pages/Dimensions/DimensionsPage.razor, DimensionEditorDialog/Overview/Rules/History og ny Agreements-regelseksjon; Components/Pages/Agreements/AgreementEdit/AgreementFields/AgreementDetails og ny generisk valgkomponent; Leasing DimensionChoices/LeasingClassificationEditor og øvrige dimensjonsnavnvisninger; alle tre resx.

**Avhengigheter/design:** fase 2–3. Bruk eksisterende Bruk i Leasing-fane som mønster; oversettelser i eksisterende modal; dynamiske dimensjoner med stabile ID-er, stier, single/multiple, required og historikkmerking. UI sender bare eksplisitte dimensjonsendringer. Behold valgt modell etter feil, tilgjengelighet, tastaturbruk og render mode.

**Tester/ferdig:** komponentkontroller for create/edit/details, språkbytte uten endrede ID-er, required, foreldervalg, bevarte verdier ved feil, unavailable/historiske valg og autorisasjon server-side. Ingen ny skjemaendring forventes. Gamle tjenester/modeller beholdes midlertidig for legacy dashboard, tester og generator; ikke fjern symboler før forbrukerne er flyttet. Solution og berørte testprosjekter bygges.

### Fase 5 — Full konvertering av demodata

**Filer:** Infrastructure/Initialization/DemoDataInitializer.cs, SeedIds.cs, eventuelt ren separat generatorbeskrivelse under samme område; DemoDataInitializer.Leasing.cs bare hvis delt helper må skilles uten endret Leasing-oppførsel; nye databasefrie ekvivalenstester og eksisterende Leasing/DemoSeedChecks.

**Avhengigheter/design:** fase 2–4 og frossen fase-1-JSON. Lag alle 36 GEO-/30 DEPT-verdier direkte som Dimensions; 300 avtalers to eksplisitte valg. Sikre stabile koder/ID-er, rekkefølgevalg og regelidempotens. Bevar eksisterende skip-edited-agreement-policy og hele økonomigrafen. PROJ/EQUIP og øvrig demo bevares. Avgjør hvordan manglende nye assignment-rader håndteres ved gjentakelse uten å overskrive brukerredigeringer; ikke påstå full reparasjon av eldre delvise grafer.

**Migrasjonskilde:** ingen egen skjemaendring forventes; fase 2–3 leverer skjema. Eventuell ny nødvendig struktur får ny migrasjon, aldri historikkendring.

**Tester/ferdig:** ny generator sammenlignes mot uforandret baseline for ALLE noder/foreldre/navn og 300 mappinger, eksplisitte og inklusive fordelinger og alle urelaterte avtaleegenskaper/linjer/indeksdata. Ingen legacy-entiteter genereres. Rene kandidat-/mergekontroller må kunne kjøre uten initializer; databasebasert idempotens forblir fase 9. Legacy er fortsatt kompilérbar fram til fase 7; ingen vanlig app-start brukes som test.

### Fase 6 — Dashboard med hierarki

**Filer:** AgreementService.Dashboard.cs, AgreementDashboardDtos.cs, IAgreementService.cs, InsightsWidget.razor; eventuell delt ren hierarkiaggregering under Web/Services/Dimensions; tests/TenantPlatform.Dashboard.SmokeTests/Program.cs og resx.

**Avhengigheter/design:** fase 3, lokaliseringslesemodell fra fase 2. Erstatt bare Geography/Organization-valgene med konfigurerte dimensjoner; behold Type/Counterparty/Status/Owner. Definer separate unique-total-/group-sum-felt og dynamisk identifikator. Behold account/access og scope/filteroppførsel.

**Tester/ferdig:** delt forfader, samme verdi flere ganger, flere søsken, eksplisitt forelder + barn, Direct, Ikke angitt, drill/back/reset, prosentnevner, unik total, inaktiv historikk, språk, skjulte grener og konto/eier/sharing. Eksisterende Attention-/prioritets-/listetester beholdes. Samordne DTO/interface/widget/tester i samme fase for byggbarhet. Ingen migrasjon forventes; AccountRegisterHierarchy kan først fjernes når siste forbruker er borte.

### Fase 7 — Legacy-fjerning og endelig databaseskjema

**Filer:** alle aktive legacy-områder i avsnitt 3: to Core-typer, Agreement-felter, EF-konfigurasjoner/DbSets, AccountSettings-tjenester/UI/hjelpere uten forbrukere, Program-DI, NavMenu, gamle DTO-felter/mapping, kontoslettingssjekk, resx og relevante tester. Aktivt snapshot eksplisitt inkludert.

**Avhengigheter/design:** fase 2–6 må ha flyttet alle brukere; gjennomgå maskinoversikten semantisk før sletting. Behold reell Organization, plassering, delte ressursnøkler og generiske Leasing-eksempler.

**Migrasjonskilde:** ny `RemoveLegacyAgreementClassifications` etter additive migrasjoner. Fjern først gamle FK-er/indekser/avtalekolonner og deretter legacy-tabeller. Behold øvrig skjema. Nytt snapshot uten legacy. Historiske migrasjoner og Designer-filer skal fortsatt være byteidentiske mot manifestet; Down beskriver skjema, ingen lovnad om gjenoppretting av slettede data. Ingen SQL-/migrasjonskjøring eller data-copy-migrasjon.

**Tester/ferdig:** hele solution og alle berørte testprosjekter kompilerer uten gamle symboler; aktive treff er null bortsett fra eksplisitt tillatte dokument-/historikkreferanser. AccountRegisters-testens gamle oppgraderingsscenario må avgrenses til den historiske migrasjonen før drop, eller erstattes av nytt FK-/konto-/hierarkiscenario. Finalmodell/snapshot sammenlignes kilde-/metadata-basert uten tilkobling. Migrasjonskjeden gjennomgås statisk fra første til siste; PostgreSQL-kjøring utsettes. UI, generator, tester og EF-opprydding må være ett sammenhengende byggbart fasesteg.

### Fase 8 — Samlet regresjon og leveranserapport

**Filer:** databasefrie nye testprosjekter/kontroller, eksisterende Dashboard-runner, relevante Agreements-/Leasing-/AccountRegisters-tester og dokumentasjon. Ingen forventet migrasjon; nye funn som krever skjema løses med ytterligere ny migrasjon.

**Avhengigheter/design:** fase 2–7. Verifiser full JSON-ekvivalens, konto/autorisasjon, required/edit/historikk, lokalisering, alle dashboardtilfeller og ren Leasing-beregning. Bygg alle testprosjekter eksplisitt; solution-bygget inkluderer dem ikke. Kjør bare dokumentert infrastrukturfrie entrypoints. Ikke kjør databasebundne konsollprogrammer under påskudd av at noen av kontrollene deres er rene.

**Tester/ferdig:** solution og relevante testprosjekter bygger; alle tillatte rene tester faktisk kjørt og rapportert separat fra kompilering. 84 historiske migrasjonsfiler har samme hash; aktivt snapshot har forventede nye strukturer. Full leveranserapport med faktisk diff, tester, uverifiserte punkter og manuell PostgreSQL-sjekkliste. Ingen auto-merge/deploy eller driftshandlinger.

### Fase 9 — Manuell PostgreSQL-verifikasjon

**Område:** hele migration chain, nye EF-konfigurasjoner, demo-initializer og databasebundne Agreements/AccountRegisters/Leasing-smoke-testprogrammer. Utføres av bruker eller en senere separat autorisert prosess, **ikke denne fase-1-kjøringen**.

**Forutsetninger:** gjennomgå nye migrasjoner, særlig DropTable/DropColumn og bevaring av Organizations/bygg/enheter; velg og godkjenn kun en disponibel database. Verifiser schema fra tom database gjennom hele historikken, constraints/indexer og concurrency. Kjør demo én gang, sammenlign alle hierarkier og mappinger mot frossen JSON, kontroller øvrige relasjoner, kjør igjen for idempotens og kontroller bevaring av redigerte/historiske valg. Gjennomfør PostgreSQL-basert sikkerhets- og Leasing-regresjon i uttrykkelig godkjent testmiljø.

**Ferdigkriterium:** faktisk kjørte kommandoer, database-/scopeidentitet uten hemmeligheter, antall og alle per-avtale-sammenligninger dokumenteres. Manglende gate holdes tydelig uverifisert. Eventuelle kildefeil tilbakeføres til riktig fase; ny migrasjon ved skjemaendring, ingen omskriving av historikk. Inntil dette er gjort finnes det bare kilde-/kompilerings-/rene testbevis, ikke bevis på fungerende PostgreSQL-migrasjoner.

## 7. Verifikasjon: undersøkt oppsett, kommandoer og faktiske resultater

Før eksekvering ble solution/prosjektfiler, build-target-søk, Web-start, DatabaseInitializer og samtlige testprogrammers databaseinnganger inspisert. Ingen egne build-targets med app-start/databaseoperasjoner ble funnet. `--no-restore` valgte eksisterende lokal restore-cache og unngikk pakkerestaurering/eksterne tjenester.

| Faktisk kommando | Faktisk resultat |
|---|---|
| `git branch --show-current`, `git rev-parse HEAD`, `git status --porcelain=v1` | `main`, kildecommit ovenfor, ren ved start. |
| `rg --files …`, `rg -n 'GeographicArea\|OrganizationElement' src tests --glob '!**/Migrations/**'` samt utvidede søk | Fil-/symbolkartlegging fullført. Historiske migrasjoner og aktivt snapshot er deretter søkt separat og inkludert i JSON. Ingen appkjøring. |
| `dotnet --version` | `10.0.302`. |
| `dotnet build TenantPlatform.sln --no-restore` | **PASS**, exit 0; 4 prosjekter bygget, 0 feil, 2 advarsler, 2,24 s. Ingen tester kjørt av denne kommandoen. |
| `dotnet build tests/TenantPlatform.Dashboard.SmokeTests/TenantPlatform.Dashboard.SmokeTests.csproj --no-restore` | **PASS**, exit 0; 0 feil, 5 advarsler, 2,24 s. Testprogram kompilert, ikke kjørt av build. |
| `env -u DASHBOARD_PREVIEW_PATH dotnet run --project tests/TenantPlatform.Dashboard.SmokeTests --no-build --no-restore` | **PASS**, exit 0; konsollen sluttet med `Dashboard smoke checks passed (in-memory; no application database or migrations used).` Testene ble faktisk kjørt. |
| `python3 docs/features/unified-dimensions-phase-1/derive-reference.py` | **PASS**, exit 0; tre JSON-filer produsert ved lokal statisk kildeavlesning/ren beregning; innebygde kontroller for hele hierarkiet, ID-unikhet, 300 mappinger, 600 linjer og fordelinger. |
| `python3 docs/features/unified-dimensions-phase-1/derive-reference.py --check` | **PASS**, exit 0; alle tre evidensfiler reprodusert byteidentisk fra fastlåst commit, ingen skriving. |
| JSON-/hash-/referansekontroll og `git diff --check` | **PASS**; se avsnitt 8 for sluttstatus. |

Eksisterende advarsler: NU1902/NU1903 om `System.Security.Cryptography.Pkcs` 10.0.0 fra restore-cache; Dashboard-bygget gjentar disse for to prosjekter og har MSB9008 om manglende `TenantPlatform.Application.csproj`. Dagens Dashboard-csproj refererer bare Web; den gamle Application-referansen finnes i lokale `obj/project.assets.json`/nuget-dgspec. Cache ble ikke ryddet eller restoret, og ingen avhengigheter ble endret. Kjent begrensning: byggene dokumenterer denne lokale cachetilstanden, ikke et verifisert rent offline-restore på ny maskin.

Dashboard-runneren bruker EF InMemory, fast tid og HtmlRenderer. Den oppretter også en Npgsql-context kun for `ToQueryString`-oversettelse av prioritetsquery; **ingen connection åpnes**, ingen SQL kjøres, ingen migrasjon/init kalles. Det er tillatt provider-generering, ikke databaseverifikasjon. Preview-miljøvariabel ble fjernet kun i testprosessen for å unngå filskriving utenfor rapporten.

**Ikke kjørt:** de ni andre smoke-testprogrammene (AccountRegisters, AgreementAnalysis, AgreementFollowup, AgreementNotice, AgreementPeriods, AgreementProcessing, Agreements, Leasing, MeetingRooms). Alle entrypoints bruker Npgsql og migrasjoner/testskjema; flere kjører SQL og Leasing inkluderer DemoDataInitializer. De kan ikke kjøres innen denne oppgavens rammer selv om noen underkontroller er rene. De er heller ikke hevdet kompilert separat. Det finnes ikke en dokumentert separat databasefri Leasing-/Agreements-runner nå. `dotnet test` er ikke en erstatning for konsollrunnerne.

**Ikke kjørt:** vanlig Web-start, `dotnet ef`, `add-migration.sh`, `update-database.sh`, SQL, databaseopprettelse, DemoDataInitializer eller databasebundne tjenester for datauttrekk. Vanlig Web-start ville kjøre DatabaseInitializer før app.Run og er derfor uegnet til baseline-verifikasjon. Ingen eksisterende kildefeil ble funnet ved de kjørte kontrollene; kildekoden er ikke forsøkt reparert fordi oppgaven er kartlegging.

## 8. Opprettede filer og kontroll av omfang

Kun disse fem nye filene er leveransen:

1. `docs/features/unified-dimensions-phase-1.md` — samlet rapport.
2. `docs/features/unified-dimensions-phase-1/derive-reference.py` — isolert, fastlåst evidensutledning og bytekontroll.
3. `docs/features/unified-dimensions-phase-1/demo-reference.json` — full logisk demodatareferanse.
4. `docs/features/unified-dimensions-phase-1/legacy-inventory.json` — komplett søketreffoversikt med semantiske forbehold.
5. `docs/features/unified-dimensions-phase-1/migration-source-sha256.json` — bytegrunnlag for migrasjoner/snapshot.

Avsluttende Git-kontroll viser bare disse nye dokumentasjons-/referansefilene. Ingen sporet eksisterende fil er endret. Migrasjonsmanifestet er kontrollert mot arbeidskopien: alle 85 filer er byteidentiske, inkludert aktivt snapshot. Bygg har som normalt skrevet ignorerte bin/obj-artefakter; dette er ikke funksjonelle kildeendringer. Ingen funksjonell kode er endret, og eksisterende brukerarbeid er bevart.

## 9. Blockere, usikkerheter og utsatte porter

Ingen blocker for **fase 1**: kravfilen finnes, komplett kildebasert referanse er etablert, og tillatt bygg og databasefrie relevante tester er kjørt. Den ferdige rapporten angir konkrete implementeringsvalg; den er ikke en bekreftelse på at funksjonen allerede finnes.

Gjenstående usikkerheter: faktisk databaseinnhold/brukeridentitet, runtime audit/tid, drift av migrasjonskjeden, databasebasert idempotens/ekvivalens og konkurranse mellom reelle PostgreSQL-skrivinger. Design-time-stien må avklares før senere migrasjonsskript. Lokal restore-cache gir eksisterende advarsler og begrenser hvor bredt baseline-bygget kan generaliseres. Ingen av disse usikkerhetene er skjult ved å redusere datasettet eller erklære kompilering som databasebevis.

- **PostgreSQL-migrasjonskjøring: NOT PERFORMED — POST_RUN_VERIFICATION**
- **Opprettelse av ny database og databasebasert demo-initialisering: NOT PERFORMED — POST_RUN_VERIFICATION**
- **Databasebasert demodataekvivalens og idempotens: NOT PERFORMED — POST_RUN_VERIFICATION**

Arbeidet stopper etter fase 1.
