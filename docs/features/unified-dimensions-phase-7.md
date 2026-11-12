# Unified Dimensions — fase 7: legacy-fjerning og endelig skjema

**Status: FERDIG for fase 7.** De to legacy-klassifiseringene er fjernet fra aktiv produksjonskode, kontrakter, administrasjon og gjeldende EF-modell. Ny migrasjonskilde og snapshot er forfattet offline. Alle 13 testprosjekter bygger, og de fire databasefrie suitene passerer. PostgreSQL er ikke brukt. Fase 8 er ikke startet.

## 1. Arbeidsgrunnlag og omfang

- Arbeidskatalog: `/Users/morten/Dev/TenantPlatform`.
- Branch: `main`.
- Startcommit: `f2f2d67338fd05e59624423aad675ec4f8ca415d`.
- Grunnlag: `unified-dimensions.md`, fase-1–6-rapportene, legacy-inventory.json, migration-source-sha256.json, innlimte AGENTS.md-instruksjoner, repoets `Agents.md` og brukerens fase-7-prompt.
- Den frosne oversikten omfatter 66 filer og 637 trefflinjer. Den er brukt sammen med nytt søk i hele dagens løsning, ikke som automatisk sletteliste. Faktiske tjenester, DTO-er, EF-konfigurasjoner, DI, navigasjon, slettingsflyt, dynamiske ressurskall og testinnganger er gjennomgått.
- Ved start fantes alle godkjente sporede/usporede fase-1–6-endringer. SHA-256 for **717 filer** ble registrert i `/tmp/tenantplatform-phase7-start-sha256.json`, med kildekopier under `/tmp/tenantplatform-phase7-start/`. Manifestets SHA-256: `d6810f6e8bba95b2fe5fd577b31d070259cdaeb0ed7a23f83772529ce46e4cb4`.
- Ingen reset, branchbytte, commit, merge, push, deploy, AgentOrchestrator, rettighetsutvidelse eller svekket begrensning. Tidligere arbeid utenfor de uttrykkelig oppførte endringene er bevart.

## 2. Filoversikt

Status gjelder **fra fasestart**, også for filer som allerede var usporet. 23 eksisterende filer er endret, 12 er slettet og åtte nye er levert. De to nye migrasjonsfilene er ikke omskriving av tidligere migrasjoner.

| Status | Fil |
|---|---|
| Endret | `src/TenantPlatform.Core/Agreements/Agreement.cs` |
| Endret | `src/TenantPlatform.Infrastructure/Persistence/Configurations/AgreementConfiguration.cs` |
| Endret | `src/TenantPlatform.Infrastructure/Persistence/Migrations/TenantPlatformDbContextModelSnapshot.cs` |
| Endret | `src/TenantPlatform.Infrastructure/Persistence/TenantPlatformDbContext.cs` |
| Endret | `src/TenantPlatform.Web/Components/Layout/NavMenu.razor` |
| Endret | `src/TenantPlatform.Web/Program.cs` |
| Endret | `src/TenantPlatform.Web/Resources/TenantPlatformResources.en-GB.resx` |
| Endret | `src/TenantPlatform.Web/Resources/TenantPlatformResources.nb-NO.resx` |
| Endret | `src/TenantPlatform.Web/Resources/TenantPlatformResources.sv-SE.resx` |
| Endret | `src/TenantPlatform.Web/Services/Accounts/AccountService.cs` |
| Endret | `src/TenantPlatform.Web/Services/Agreements/AgreementDtos.cs` |
| Endret | `src/TenantPlatform.Web/Services/Agreements/AgreementService.cs` |
| Endret | `tests/TenantPlatform.AccountRegisters.SmokeTests/Program.cs` |
| Endret | `tests/TenantPlatform.AgreementDimensions.SmokeTests/Fixture.cs` |
| Endret | `tests/TenantPlatform.AgreementDimensions.SmokeTests/MetadataChecks.cs` |
| Endret | `tests/TenantPlatform.AgreementDimensions.SmokeTests/UiChecks.cs` |
| Endret | `tests/TenantPlatform.Agreements.SmokeTests/Program.cs` |
| Endret | `tests/TenantPlatform.Dashboard.SmokeTests/HierarchyChecks.cs` |
| Endret | `tests/TenantPlatform.DemoDimensions.SmokeTests/EquivalenceChecks.cs` |
| Endret | `tests/TenantPlatform.DemoDimensions.SmokeTests/Program.cs` |
| Endret | `tests/TenantPlatform.DemoDimensions.SmokeTests/ReconciliationChecks.cs` |
| Endret | `tests/TenantPlatform.Dimensions.SmokeTests/Program.cs` |
| Endret | `tests/TenantPlatform.Dimensions.SmokeTests/README.md` |
| Slettet | `src/TenantPlatform.Core/Accounts/GeographicArea.cs` |
| Slettet | `src/TenantPlatform.Core/Accounts/OrganizationElement.cs` |
| Slettet | `src/TenantPlatform.Infrastructure/Persistence/Configurations/GeographicAreaConfiguration.cs` |
| Slettet | `src/TenantPlatform.Infrastructure/Persistence/Configurations/OrganizationElementConfiguration.cs` |
| Slettet | `src/TenantPlatform.Web/Components/Pages/AccountSettings/GeographicAreas.razor` |
| Slettet | `src/TenantPlatform.Web/Components/Pages/AccountSettings/OrganizationElements.razor` |
| Slettet | `src/TenantPlatform.Web/Services/AccountSettings/AccountHierarchyException.cs` |
| Slettet | `src/TenantPlatform.Web/Services/AccountSettings/AccountRegisterHierarchy.cs` |
| Slettet | `src/TenantPlatform.Web/Services/AccountSettings/AccountRegisterInput.cs` |
| Slettet | `src/TenantPlatform.Web/Services/AccountSettings/GeographicAreaService.cs` |
| Slettet | `src/TenantPlatform.Web/Services/AccountSettings/OrganizationElementService.cs` |
| Slettet | `src/TenantPlatform.Web/Services/Agreements/AgreementService.Structure.cs` |
| Ny | `docs/features/unified-dimensions-phase-7.md` |
| Ny | `docs/features/unified-dimensions-phase-7/legacy-audit.json` |
| Ny | `src/TenantPlatform.Infrastructure/Persistence/Migrations/20261112104542_RemoveLegacyAgreementClassifications.Designer.cs` |
| Ny | `src/TenantPlatform.Infrastructure/Persistence/Migrations/20261112104542_RemoveLegacyAgreementClassifications.cs` |
| Ny | `tests/TenantPlatform.AccountRegisters.SmokeTests/README.md` |
| Ny | `tests/TenantPlatform.Dimensions.SmokeTests/AccountDeletionChecks.cs` |
| Ny | `tests/TenantPlatform.Dimensions.SmokeTests/FinalModelChecks.cs` |
| Ny | `tests/TenantPlatform.Dimensions.SmokeTests/SchemaAudit.cs` |

## 3. Fjernede områder og erstatninger

- GeographicArea og OrganizationElement er fjernet fra Core. Agreement har ikke lenger de to nullable klassifiserings-ID-ene.
- De to DbSets, dedikerte konfigurasjonene og account-aware legacy-FK-ene i AgreementConfiguration er fjernet. EF oppretter ikke shadow-felter som erstatning; modell-/kolonnefravær er testet.
- SaveAgreementRequest, AgreementDetailsDto og AgreementOptionsDto har ikke legacy-ID-er, navn eller valglister. Den tidligere legacy-only IsActive-egenskapen på AgreementOptionDto er også fjernet; øvrige Id/Name/BuildingId beholdes.
- GetAsync/GetOptionsAsync leser ikke de gamle registrene. Mapping, Apply og Validate har ingen gamle felter eller fallback. Structure-partialen og det nå unødvendige legacy-valideringsargumentet er fjernet. Analyse bruker samme rene requestmodell som ordinær avtale.
- GeographicAreaService, OrganizationElementService, deres input/exception/hierarkihjelper og de to administrasjonssidene er slettet. Ingen runtime-kode beholdes bare for gamle tester.
- DI-registreringer, navigasjonslenker og rutegjenkjenning er fjernet. Dimensions-lenken og eksisterende registeradministrasjonsrett beholdes. De gamle rutene har ingen kompatibilitetsadministrasjon eller nytt overgangssystem.
- Generiske dimensjoner, oversettelser, Agreements-regler/tildelinger/historikk, patchsemantikk og dynamisk dashboard er beholdt.

## 4. Bevarte forretningsdata og oppførsel

Faktisk Organization/motpart, adresse-/land-/postdata, bygg, enheter og fysiske lokasjoner er beholdt. Metadata kontrollerer fortsatt Restrict-relasjonene fra Agreement til Organization, Building og Unit. Ingen endring i hvem som kan lese/skrive avtaler, valgt konto, autentisering eller autorisasjonsarkitektur.

Leasing-kilde, regler, snapshots, kostnadsfordeling, fysisk lokasjon, finansiering og betaling er byteuendret fra fasestart. Avtaleøkonomi, dokumenter, oppsigelse, indeks og øvrige tjenester er ikke skrevet om. Hele seed-koden og SeedIds er byteidentiske med fase-7-start. Den nye migrasjonens åtte Up-operasjoner treffer bare de to gamle avtalekolonnene og registrene med deres FK-er/indekser.

26 faktisk foreldede ressursnøkler er fjernet konsistent i nb-NO, en-GB og sv-SE: de dedikerte register-/avtalefeltene, registerfeilene, ubrukte legacy-hierarkitekster og gamle dashboardgrupperingsnavn. Generelle tekster og fysiske lokasjonsressurser er beholdt. **AccountHierarchyAddChild er beholdt** fordi DimensionValuesSection faktisk bruker den som en generell «legg til underelement»-tekst. Navnet er en ressursnøkkel, ikke en runtime-avhengighet til den slettede hierarkihjelperen.

## 5. Kontoslettingssperrer

AccountService.CanDeleteAccountAsync fulgte allerede policyen at en konto med kontoeide data ikke kan slettes. De to legacy-sjekkene er erstattet med eksplisitte, kontoavgrensede AnyAsync-sjekker for:

- Dimensions, DimensionValues, DimensionTranslations og DimensionHistory.
- AgreementDimensionRules, AgreementDimensionAssignments og AgreementDimensionHistory.
- LeasingDimensionRules, LeasingDimensionSelections, LeasingAllocationDimensions og LeasingAllocationRows.

Eksisterende medlems-/Organization-/bygg-/enhets-/øvrige sperrer og selve DeleteAccountAsync-flyten er beholdt. Ingen kaskadesletting eller ny slettingsrett er innført. Forsvarssjekken ved DbUpdateException er fortsatt der.

AccountDeletionChecks kaller den faktiske tjenesten med EF InMemory og isolerer hvert av de elleve nye predikatene: en rad alene uten medlemskap blokkerer både beslutningen og DeleteAccountAsync; en annen kontos rad blokkerer ikke en tom konto. Manglende konto og tom kontos eksisterende sletteoppførsel kontrolleres også. De isolerte barnradene er bevisst ufullstendige testgrafer for beslutningslogikk; InMemory-resultatet hevder **ingen** FK-/PostgreSQL-garanti. Relasjonelle Restrict-/account-aware-metadata kontrolleres separat offline.

## 6. Seed-ID-er og full demodataekvivalens

Ingen generator eller seed-ID-formel er endret. MagidaDivision/Department/Continent/Country/Capital er fortsatt meningsfulle, stabile verdi-ID-fabrikker for ordinære Dimensions. Det finnes ingen ny generering av de slettede entitetene eller ID-remapping.

DemoDimensions-testen bruker ikke lenger AccountRegisterHierarchy. Den uavhengige forventede visningsrekkefølgen kommer fra de **frosne fullstiene**, sortert med CurrentCultureIgnoreCase, og sammenlignes med produksjonens DimensionHierarchy i nb-NO, en-GB og sv-SE. Dette fungerer for alle de eksisterende seed-navnene; testen bruker ikke den nye hjelperen til å beregne sin egen fasit og kopierer ikke den gamle runtime-implementasjonen.

Avtalesammenligningen ekskluderer **nøyaktig** OrganizationElementId og GeographicAreaId fra referansens property-sett, krever at begge finnes i det frosne grunnlaget og at begge er fraværende i faktiske entiteter. Antall egenskaper kontrolleres med akkurat denne differansen. Alle øvrige forventede felter må finnes og sammenlignes; ukjente/manglende avtaleegenskaper ignoreres ikke generelt. De to gamle referanseverdiene kontrolleres fortsatt mot de eksplisitte dimensjonstildelingene.

Full kjøring passerer fortsatt: **36 GEO- og 30 DEPT-verdier**, alle navn/foreldre/nivåer/stier/sortering, **300 avtaler**, nøyaktig **600 tildelinger**, alle per-avtale-mappinger og eksplisitte/inklusive fordelinger, samt **600 økonomilinjer med versjoner/prisdata**, indeksvalg/-verdier og øvrige referansefelter. Redigering-/skip-policyen er beholdt. Testens tidligere oppsett av faktiske legacy-egenskaper er fjernet fordi slike CLR-egenskaper ikke lenger finnes; bevaring av øvrige redigerte rader og eksplisitt fjernede dimensjonsvalg testes videre.

## 7. Tilpasning av øvrige tester

AgreementDimensions-fixture lager ikke gamle registerrader eller legacy-ID-er. Metadata forventer nå fravær. UI-testen bevarer fortsatt tittel, vilkår, faktisk motpart og eier. Alle eksisterende create/edit/details/analyse-, revisjons-, historikk-, inaktiv-/historisk-valg- og patchkontroller kjører videre uten kompatibilitetsfelter.

Agreements-smoke er fortsatt PostgreSQL-bundet og er **bare kompilert**. Dets gamle runtime-klassifiseringsscenario er erstattet med ordinære Dimensions og eksplisitte patcher: kontoavgrensede valg/stier, create/detail/edit, read/edit-tilgang, ukjent/fremmed verdi, inaktive historiske valg, eksplisitt fjerning og account/dimension-aware assignment-FK. Direkte sletting av en referert verdi testes mot Restrict i stedet for en gammel tjeneste-delete som ikke finnes i Dimensions. Ingen gyldig gammel FK-/tilgangsintensjon er beholdt via slettede CLR-typer.

**AccountRegisters er behandlet eksplisitt og er fortsatt en nyttig PostgreSQL-runner, aldri kjørt her:**

1. Det historiske scenarioet stopper først ved AddAccountDepartmentsAndLocations og deretter AddAccountRegisterHierarchies. Rader leses gjennom SQL og en snever unmapped HistoricalRegisterRow-DTO; ingen slettede gjeldende CLR-entiteter brukes. Rename-/databevaring kontrolleres bare ved denne historiske grensen.
2. Deretter går runneren til siste migrasjon, som bevisst dropper de gamle strukturene. Den hevder ikke at gamle klassifiseringsrader overlever dette steget.
3. Gjeldende runtime-tester bruker DimensionService for normalisering, validering, revisjoner, konto-/rolle-/platform-admin-vern, flytting av deltrær, manglende/fremmed forelder, selv-/etterkommersyklus, konkurrerende reparenting, relasjonell foreldre-FK og kontoslettingssperre. Admin-/medlemsnavigasjon kontrolleres videre. Ingen ny testprovider-bypass er innført.

De gamle register-delete-handlingene er ikke gjenskapt som kompatibilitet; gjeldende Dimensions bruker deaktivering, revisjon og Restrict-relasjoner. Offline Dimensions-/AgreementDimensions-kontroller og den oppdaterte PostgreSQL-kilden dekker den relevante sikkerhets-/hierarki-/integritetsintensjonen. PostgreSQL-scenarioene er kun kilde/kompileringsbevis inntil separat kjøring.

## 8. Ny migrasjonskilde og snapshot

Kommando: `bash scripts/add-migration.sh RemoveLegacyAgreementClassifications`.

Nye filer:

- `20261112104542_RemoveLegacyAgreementClassifications.cs`.
- `20261112104542_RemoveLegacyAgreementClassifications.Designer.cs`.

Gjeldende TenantPlatformDbContextModelSnapshot.cs er generert på nytt. Eksisterende offline design-time factory og forbindelsessperre er byteuendret. EF ga den forventede advarselen om mulig datatap ved scaffolding; dette er tilsiktet **kilde for gjennomgang**, ikke utført datatap.

**Up, i kontrollert rekkefølge:**

1. Drop begge FK-er fra agreements til de gamle registrene.
2. Drop de to AccountId/legacy-ID-indeksene på agreements.
3. Drop GeographicAreaId og OrganizationElementId fra agreements.
4. Drop geographic_areas og organization_elements, med deres interne selvreferanser/indekser/constraints.

Det er åtte operasjoner. Ingen SqlOperation, data-copy, backfill, konvertering eller endring av andre tabeller.

**Down:** gjenoppretter de nullable avtalekolonnene, to tomme tabeller med PK/account-aware alternate keys og account-/selvreferanse-FK-er, seks indekser og de to avtalereferansene. Tolv operasjoner. Det beskriver en skjemareversering; **slettede data kan ikke gjenopprettes** av Down, og avtalefeltene blir ikke fylt med gamle verdier.

## 9. Hele kjeden og uendrede migrasjoner

SchemaAudit går databasefritt gjennom **alle 45 migrasjoners UpOperations**, fra tomt strukturelt skjema til gjeldende modell. Den kontrollerer tabell-/kolonneeksistens, key-/index-/FK-navn og kolonner, principal keys, rename-oppdateringer og at eksterne FK-er/indekser er fjernet før aktuelle drop-operasjoner. Sluttresultatet sammenlignes med modellens tabeller, kolonner, keys, indekser, FK-er og check-navn.

Deretter brukes den nye Down i samme strukturelle representasjon. Resultatet sammenlignes med **forrige migrasjons TargetModel**, før ny Up anvendes og sluttmodellen kontrolleres igjen. Seks historiske INSERT/UPDATE-operasjoner er klassifisert som data-SQL og **aldri eksekvert**. Ukjente operasjoner eller strukturell SQL krever eksplisitt gjennomgang fremfor stille ignorering.

Dette er en logisk strukturkontroll, ikke en SQL-motor: datatyper, check-uttrykkenes faktiske PostgreSQL-evaluering, historiske datatransformasjoner, providerspesifikke detaljer, låsing og drift på konkrete data er ikke bevist ved replay. Ingen DML-/DDL-streng er sendt til en database.

Alle **88 eksisterende migrasjons-/Designer-filer** fra fasestart er byteidentiske, inkludert de fire additive filene fra fase 2/3. Alle **84 historiske filer** i fase-1-manifestet er også byteidentiske. Det gamle manifestets snapshot gjelder den frosne commiten; gjeldende snapshot er uttrykkelig oppdatert som tillatt. Med to nye filer og ett oppdatert snapshot inneholder migrasjonsmappen nå 91 kildefiler. Ingen historisk migrasjon er omskrevet.

## 10. Faktiske kommandoer og resultater

| Kommando/kontroll | Resultat |
|---|---|
| `dotnet build TenantPlatform.sln --no-restore` | PASS; fire prosjekter, 0 feil, 2 eksisterende cacheadvarsler |
| `dotnet build <prosjektsti> --no-restore` for alle 13 `tests/*/*.csproj` | PASS; alle exit 0 |
| `bash scripts/add-migration.sh RemoveLegacyAgreementClassifications` | PASS; offline kildeforfatting, ny migration/Designer/snapshot |
| `dotnet ef migrations has-pending-model-changes --project src/TenantPlatform.Infrastructure --startup-project src/TenantPlatform.Web` | PASS; «No changes have been made to the model since the last migration.» |
| `dotnet run --project tests/TenantPlatform.Dimensions.SmokeTests --no-restore` | PASS etter siste testendring; **157 kontroller**, inklusive kontosletting og full strukturell migrasjonskontroll |
| `dotnet run --project tests/TenantPlatform.AgreementDimensions.SmokeTests --no-build --no-restore` | PASS; **166 kontroller** |
| `dotnet run --project tests/TenantPlatform.DemoDimensions.SmokeTests --no-build --no-restore` | PASS; full ekvivalens, **725188 assertions** inklusive gjentatte felt-/strukturkontroller |
| `env -u DASHBOARD_PREVIEW_PATH dotnet run --project tests/TenantPlatform.Dashboard.SmokeTests --no-build --no-restore` | PASS; **107 PASS-linjer** |
| `python3 docs/features/unified-dimensions-phase-1/derive-reference.py --check` før og etter | PASS; alle tre JSON-filer byteidentiske |
| SHA-256 mot fasestart og fase-1-manifest, sluttinventar og ressursgjennomgang | PASS; tidligere migrasjoner/referanser/rapporter, seed-/ID-kode og offline guard bevart |
| `git diff --check` | PASS |
| Vanlig Web-start, PostgreSQL-runner, migrasjonskjøring, demo-/databaseinitialisering, SQL-eksekvering | IKKE UTFØRT |

De 13 eksplisitt bygde prosjektene er AccountRegisters, AgreementAnalysis, AgreementDimensions, AgreementFollowup, AgreementNotice, AgreementPeriods, AgreementProcessing, Agreements, Dashboard, DemoDimensions, Dimensions, Leasing og MeetingRooms. De **ni databasebundne programmene er bare kompilert**. Intet nytt testprosjekt eller ny pakke er innført.

Eksisterende NU1902/NU1903 om System.Security.Cryptography.Pkcs 10.0.0 og MSB9008 i enkelte eldre restore-cacher forekommer fortsatt. Scaffolding ga cacheadvarsler og tilsiktet datatap-advarsel; det er ikke gjort uvedkommende kildeendringer eller advarselsundertrykking for å rydde dem.

Lokale logger er `/tmp/tenantplatform-phase7-build.log`, `tenantplatform-phase7-test-builds.log`, `tenantplatform-phase7-migration.log`, `tenantplatform-phase7-pending.log` og `tenantplatform-phase7-{Dimensions,AgreementDimensions,DemoDimensions,Dashboard}-run.log` under `/tmp/`. Startmanifest og kildekopier er også lokale evidensfiler.

## 11. Avsluttende fullstendig treffoversikt

[legacy-audit.json](unified-dimensions-phase-7/legacy-audit.json) inneholder **alle gjenværende trefflinjer** innen det dokumenterte søket, med sti, linjenummer, tekst, matchede uttrykk, kategori og begrunnelse per fil. Søket omfatter dagens sporede/usporede ikke-ignorerte UTF-8-filer, både de spesifikke modell-/tabell-/rutenavnene og brede Geography/Department/Location-/seed-ID-navn. Slettede filer, binærfiler, ignorerte bin/obj og selve treffartefakten er eksplisitte unntak. Nåværende rapport inngår som dokumentasjon.

Gjenværende treff er begrunnet slik:

| Kategori | Behandling/begrunnelse |
|---|---|
| Historiske migrasjoner/Designer | Urørlige kilder som fortsatt beskriver gammel opprettelse/rename/FK-er. |
| Ny drop-migrasjon | Gamle navn er nødvendige i Up/Down-operasjonene; ingen runtime-modellavhengighet. |
| Frossen fase-1-pakke | Autoritativt forventningsgrunnlag og opprinnelig inventar, bevart uendret. |
| Krav og fase-/leveranserapporter | Dokumentasjon av gammel og ny tilstand, ikke aktiv produksjonskode. |
| Demo-/dashboard-/metadata-tester | Negative fraværsassertions og eksplisitt tilpasning mot frosne felter; ingen gamle CLR-entiteter eller kompatibilitetskontrakter. |
| AccountRegisters historiske test | Avgrenset SQL-scenario ved historisk migrasjonsgrense med unmapped DTO, ikke gjeldende klassifiseringsmodell. |
| Seed-ID-er og ordinære navn | MagidaDepartment m.fl. bevarer GUID-er; Geography/Department er nå vanlige GEO/DEPT-oversettelser. |
| Delte tekster | AccountHierarchyAddChild brukes fortsatt i generisk DimensionValuesSection og finnes i tre språk. Den slettede runtime-hjelperen finnes ikke. |
| Fysisk lokasjon/øvrige domeneord | Adresse-/utstyrs-/Leasing-/andre forretningsdata og tekster som skal bevares. Bredt navnetreff er ikke grunnlag for sletting. |

Det finnes **ingen aktiv produksjonsavhengighet** til GeographicArea/OrganizationElement, deres felt/DbSets/tjenester/ruter eller de slettede runtime-hjelperne. Gjeldende snapshot har ingen av de slettede entitetene/tabellene/kolonnene. Dette er kontrollert både ved søk, modell-/kontraktmetadata og kompilering.

## 12. Begrensninger og overlevering til fase 8

Ingen kjente uferdige implementasjonspunkter innen fase 7. Bevisene gjelder kode, generering, strukturmetadata, InMemory-beslutninger og komponentrendering/kalte handlinger. De bekrefter ikke relasjonell persistens, faktisk PostgreSQL-migrering, historisk databevaring før drop eller nåværende browserinteraksjon.

Fase 8 skal startes med ny manuell prompt og samle regresjon/leveranse etter det avtalte omfanget. Fase-1-pakken er fortsatt reproducerbar. Senere separat autorisert PostgreSQL-verifikasjon må bruke en bruker-godkjent disponibel database: gjennomgå den destruktive migrasjonskilden, kjør hele kjeden, verifiser sluttmodell/constraints, initialiser demo og kontroller alle 300 mappinger/600 linjer, gjenta for idempotens og kjør de oppdaterte PostgreSQL-regresjonene. Dette er ikke utført nå, og ingen eksisterende database er kopiert, konvertert eller slettet.

- PostgreSQL-migrasjonskjøring: **NOT PERFORMED — POST_RUN_VERIFICATION**
- Opprettelse av ny database og databasebasert demo-initialisering: **NOT PERFORMED — POST_RUN_VERIFICATION**
- PostgreSQL-basert schema, constraints, persistens og regresjon: **NOT PERFORMED — POST_RUN_VERIFICATION**
