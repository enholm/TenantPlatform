# Unified Dimensions — fase 2: felles dimensjoner og oversettelser

**Status: FERDIG for fase 2.** Dato: 12. november 2026. Modell, persistenskilde, tjenestelogikk, editor og relevant eksisterende visning er implementert; databasefrie kontroller er kjørt. PostgreSQL-kjøring er en separat uverifisert port. Fase 3 er ikke startet.

## Arbeidsgrunnlag

- Arbeidskatalog: `/Users/morten/Dev/TenantPlatform`.
- Branch: `main`.
- Startcommit: `f2f2d67338fd05e59624423aad675ec4f8ca415d`.
- Ved start var bare de fem godkjente fase-1-filene nye/usporede. Ingen sporede brukerendringer forelå. Alle fem er bevart byteidentisk; ingen commit, branchbytte, merge, push eller deploy er utført.
- Grunnlag: `docs/features/unified-dimensions.md`, `docs/features/unified-dimensions-phase-1.md`, fase-1-referansepakken og innlimte AGENTS.md-instruksjoner. Ingen fysisk AGENTS.md eller ytterligere lokale byggeinstruksjoner ble funnet. Faktisk kode ble kontrollert mot kartleggingen før endringene.
- Ingen AgentOrchestrator-konfigurasjon eller kjøring. Ingen endring i autentisering, autorisasjonsarkitektur eller produksjonsavhengigheter. Det nye testprosjektet bruker samme EF InMemory 10.0.10 som eksisterende Dashboard-tester; dette er den eneste nye PackageReference, og den gjelder bare tester.

## Modell og fallback

`DimensionTranslation` har `Id`, `AccountId`, `DimensionId`, `LanguageCode`, `Name` og `Description`. `Dimension.Translations` er en samling uten tilbakenavigasjon som skaper JSON-sykluser. Eksisterende Name/Description/Code/ID-er, verdinavn, hierarki og globale valgregler er beholdt.

`DimensionTranslationConfiguration` definerer `dimension_translations`, en unik indeks på `(AccountId, DimensionId, LanguageCode)` og en Restrict-FK til dimensjonens `(AccountId, Id)`. Språk har maks 10 tegn, navn 200 og beskrivelse 2000. FK- og indeksmetadata er kontrollert databasefritt; de er ikke eksekvert i PostgreSQL.

`TranslationHelper.ResolveText` er en ny, snever feltvis oppslagsmetode. Den endrer ikke eksisterende `Select`. `LanguageHelper.NormalizeSupported` bruker eksisterende nb/no/en/sv-konvensjoner uten å gjøre fravær/ukjent språk til engelsk; eksisterende `Normalize`/`IsSupported` er uendret. `DimensionLocalization` bruker helperen og avgrenser oversettelsesrader til korrekt konto og dimensjon.

Fallback kjøres separat for navn og beskrivelse:

1. Ønsket UI-språk, med støttede aliaser normalisert.
2. Kontoens standardspråk, hvis et støttet språk faktisk finnes.
3. Lagret fallbacktekst.
4. Stabil kode for navn; beskrivelse kan forbli tom.

Whitespace behandles som manglende innhold. Navn uten beskrivelse stopper ikke beskrivelsens fallback. Ingen automatisk engelsk- eller første-rad-fallback tilføyes. Eksisterende `TranslationHelper.Select` beholder sin tidligere engelsk-/første-rad-oppførsel for andre forbrukere.

## Lagring, avgrensning og samtidighet

`DimensionService.GetAsync` laster kontoavgrensede oversettelser og kontostandard. `DimensionRegister` har lokaliserte displaymetoder uten å skrive over entitetens fallbackfelter.

`SaveDimensionAsync` beholder kortlivet factory-context, eksisterende account-admin-/aktivt medlemskapskontroll, kontolås og transaksjon. Den laster nå dimensjon og egne oversettelser sammen. Den private `ApplyDimensionChanges` validerer og endrer den sporede grafen; én SaveChanges og Commit omfatter dimensjon, oversettelser og DimensionHistory. EF-concurrency-feil oversettes til eksisterende `DimensionConcurrency`.

Oversettelser sendes som en språkpatch:

- Utelatte språk beholdes, også for eldre kall som ikke sender oversettelser.
- Et sendt språk oppdaterer bare sin rad; ID beholdes.
- Begge felt eksplisitt tomme fjerner bare det språket; ett tomt felt gir feltvis fallback.
- Beskrivelses-only oversettelse er tillatt. Dimensjonens eksisterende fallbacknavn er fortsatt obligatorisk med samme lengdevalidering som før.
- Skriving krever støttede kanoniske språkkoder, som eksisterende oversettelseseditorer; trimming og store/små bokstaver normaliseres. Lesing støtter aliaser som no, nb, en-US og sv. Ukjent språk avvises før Normalize kan gjøre det til engelsk; duplikater etter normalisering avvises.
- Konto-/dimensjons-/rad-ID-er fra input blir ikke brukt til å velge fremmede poster. Eventuelle oppgitte identiteter må stemme med serverens konto, dimensjon og aktuelle oversettelsesrad. Nye ID-er og eierkoblinger settes server-side.
- Forelderens GUID-revisjon sjekkes før noen endring og fornyes også ved oversettelsesendring. Dette beskytter oversettelsespatchen mot gamle editorer og samtidige oppdateringer.

Historikken serialiserer før-/ettertilstand med hele oversettelsessamlingen, aktør, konto og tid. Eksisterende audit-interceptor registrerer nye/endrede/slettede oversettelsesrader gjennom normal EF-tracking. Ingen lagrede historikk- eller Leasing-snapshots omskrives.

## Redigering og visning

Eksisterende dimensjonsmodal har separate standardnavn/-beskrivelse og valgfrie navn-/beskrivelsesfelt for Norsk (`nb-NO`), English (`en-GB`) og Svenska (`sv-SE`). Den bruker samme EditForm, modal, kopimodell og feilvisning. Oversettelser kopieres dypt, slik at ikke-lagrede endringer ikke muterer registeret. Feltene beholdes når servervalidering feiler. Seks nye UI-ressursnøkler er lagt til i alle tre resx-filene.

Oversikt, valgt dimensjons detaljvisning og verdilisteoverskrift bruker lokaliserte navn; oversikt og egenskaper bruker feltvis lokalisert beskrivelse. Editor arbeider fortsatt med de faktiske fallbackfeltene. DimensionHistoryView viser også lagrede oversettelsesendringer og kan fortsatt vise eldre historikk uten Translations.

Leasing-katalogen laster samme kontoavgrensede oversettelser og kontostandard. `LeasingDimensionOption.DisplayName` brukes i valgkontroller, klassifiseringseditor, varsel om manglende obligatorisk dimensjon, nåværende detaljvisning og dimensjonsfilteret i rapporter. Ordinære autoriserte Leasing-eiere bruker eksisterende katalogtilgang; de trenger ikke registeradministrasjon.

Gjeldende detaljvisning bruker lokaliserte katalognavn uten å endre tildelingsobjektene. Historikk uten katalog viser fortsatt registrerte snapshotnavn. Rapportenes historiske data og eksport bruker fortsatt lagrede snapshots; bare filterets nåværende dimensjonsetikett er lokalisert. Ny klassifiseringsskriving tar fortsatt snapshot fra fallbacknavnet, uavhengig av UI-kultur. `LeasingService.Reports`, kostnadskalkulatorene, reglene og tildelingssemantikken er ikke endret. Dimensjonsverdier har ingen nye oversettelser.

## Migrasjonsforfatting uten database

Ny kilde:

- `src/TenantPlatform.Infrastructure/Persistence/Migrations/20261112084722_AddDimensionTranslations.cs`.
- Tilsvarende ny `20261112084722_AddDimensionTranslations.Designer.cs`.
- Oppdatert aktivt `TenantPlatformDbContextModelSnapshot.cs` i samme mappe.

Up inneholder bare CreateTable for dimension_translations og én unik indeks; FK-en opprettes som del av tabellen. Down dropper bare den nye tabellen. Ingen datakopiering, oppdatering av eksisterende navn eller oversettelses-seed. Snapshot-diffen inneholder bare ny entitet, FK og navigasjon.

Før scaffolding ble Web/Program.cs, DatabaseInitializer, prosjekter, migrasjonsskript og manglende design-time factory undersøkt. Vanlig Web-start kjører migrasjon/initialisering og ble ikke brukt. En snever `TenantPlatformDbContextDesignTimeFactory` i Infrastructure oppretter bare Npgsql-options med en fast dummy-databaseidentitet. Den leser ikke appsettings, brukerhemmeligheter eller miljøkonfigurasjon og laster ikke Web-hostens tjenester. En interceptor avviser synkron og asynkron EF-åpning av forbindelser.

`dotnet ef dbcontext info ... --no-build --verbose` bekreftet `Using DbContext factory 'TenantPlatformDbContextDesignTimeFactory'`. Verktøyet konstruerte et lukket DbConnection-objekt for å lese metadata; det åpnet ingen forbindelse, kjørte ingen SQL og startet ikke Web-initialisering. Deretter ble den foreskrevne `bash scripts/add-migration.sh AddDimensionTranslations` brukt til kildegenerering. Factoryens sperre gjelder også eventuelle EF-databasekommandoer; den er ment for offline kilde-/modellverktøy. Senere autorisert databaseverifikasjon må bruke en ordinær runtime-context/testkonfigurasjon, som eksisterende PostgreSQL-smoke-programmer allerede gjør. Runtime-startens factory/konfigurasjon er uendret.

Kontroll mot fase-1-manifestet: **84 av 84 historiske migrasjons-/Designer-filer er byteidentiske**. Det aktive snapshotet er forventet endret. Alle fem fase-1-filer er byteidentiske mot starttilstanden. `derive-reference.py --check` reproduserer fortsatt alle tre JSON-filer fra den frosne commiten.

## Faktiske kontroller og resultater

Ingen test- eller byggekommando ble brukt til vanlig Web-start. Prosjektoppsett og entrypoints ble inspisert først. Alle operasjoner under er databasefrie.

| Kommando | Faktisk resultat |
|---|---|
| `dotnet ef --version` | 10.0.10. |
| `dotnet build TenantPlatform.sln --no-restore` | PASS; siste kjøring 0 feil, 0 advarsler; alle fire solution-prosjekter bygget. Tester inngår ikke i dette bygget. |
| `dotnet ef dbcontext info --context TenantPlatformDbContext --project src/TenantPlatform.Infrastructure --startup-project src/TenantPlatform.Web --no-build --verbose` | PASS; offline factory funnet og brukt; metadata fra lukket connection-objekt, ingen åpning/init/SQL. |
| `bash scripts/add-migration.sh AddDimensionTranslations` | PASS; ny migrasjon/Designer og snapshot generert uten databasekjøring. |
| `dotnet restore tests/TenantPlatform.Dimensions.SmokeTests/TenantPlatform.Dimensions.SmokeTests.csproj --source /Users/morten/.nuget/packages -p:NuGetAudit=false` | PASS; pakker fra lokal cache, ingen produksjonsavhengighetsendring. NuGetAudit ble deaktivert kun for denne offline restore-kommandoen, ikke i prosjekt eller konfigurasjon. |
| `dotnet build tests/TenantPlatform.Dimensions.SmokeTests/TenantPlatform.Dimensions.SmokeTests.csproj --no-restore` | PASS; siste kjøring 0 feil og 0 advarsler. |
| `dotnet run --project tests/TenantPlatform.Dimensions.SmokeTests --no-build --no-restore` | PASS; **91 faktisk kjørte kontroller**, ingen databaseforbindelser/SQL/initialisering/migrasjonskjøring. |
| `dotnet build tests/TenantPlatform.Dashboard.SmokeTests/TenantPlatform.Dashboard.SmokeTests.csproj --no-restore` | PASS; 0 feil, 3 eksisterende cache-/pakkeadvarsler. |
| `env -u DASHBOARD_PREVIEW_PATH dotnet run --project tests/TenantPlatform.Dashboard.SmokeTests --no-build --no-restore` | PASS; faktisk kjørt til `Dashboard smoke checks passed (in-memory; no application database or migrations used).` |
| `dotnet build tests/TenantPlatform.Leasing.SmokeTests/TenantPlatform.Leasing.SmokeTests.csproj --no-restore` | PASS; 0 feil, 2 eksisterende pakkeadvarsler. **Kun kompilert; PostgreSQL-runneren er ikke kjørt.** |
| `python3 docs/features/unified-dimensions-phase-1/derive-reference.py --check` | PASS; alle tre JSON-filer byteidentiske. |
| Python-kontroll av startfilhasher, migrasjonsmanifest og resx-XML; `git diff --check` | PASS; alle fem referansefiler og 84 historiske filer uendret, seks nye nøkler finnes nøyaktig én gang i hvert språk, ingen whitespace-feil. |

Tidlige solution-/EF-kjøringer rapporterte eksisterende NU1902/NU1903 om System.Security.Cryptography.Pkcs 10.0.0. Dashboard-/Leasing-cache har fortsatt disse; Dashboard har også eksisterende MSB9008 om et tidligere Application-prosjekt i obj/project.assets.json, som dokumentert i fase 1. Ingen kildefiks eller produksjonsavhengighetsoppdatering ble gjort for disse. At siste solution-bygg etter offline restore ikke viser disse cachevarslene er ikke et bevis på at pakkevarslenes underliggende forhold er rettet.

Under utvikling ble to nye nullable-testadvarsler rettet. Første nye testkjøring feilet fordi testen brukte value tuples mot den gamle generiske `TranslationHelper.Select`, mens dagens forbrukere bruker referansetyper. Fixture ble endret til en referansetype; eksisterende helperoppførsel ble ikke endret. Etterpå passerte først 87 og til slutt 91 kontroller med ytterligere eksplisitte kontroller av manglende rader, platform-admin-grense og deaktivert bruker. Ingen utestående kompilerings- eller testfeil i de kjørte kontrollene.

### Hva de databasefrie testene beviser

Den nye selvstendige konsollinngangen bruker EF InMemory, produksjonens autorisasjonstjeneste og audit-interceptor, fast tid, HtmlRenderer og Npgsql modellmetadata. Den dekker språk/aliaser, feltvis fallback, fravær/tom tekst, ingen uønsket engelsk-fallback, gammel helperoppførsel, språkpatch/bevaring/sletting, lengder/språk/duplikatvalidering, konto-/bruker-/admingrenser, ordinær Leasing-tilgang, revisjon/historikk/audit, lokalisert rendering, feilbevaring, uendrede snapshots og kostnadsfordeling.

Offentlige lese-/tilgangsmetoder kjøres normalt. Gyldige writes testes ved å kalle den private produksjonsmetoden `ApplyDimensionChanges` via reflection og deretter InMemory SaveChanges. Bare den ytre relasjonelle transaksjonen/kontolåsen kan ikke kjøres her; produksjonskoden har ingen provider-spesifikk bypass for tester. InMemory bekrefter eksplisitt revisjonsjekk og foreldertokenkonflikt, men beviser ikke PostgreSQL atomicitet/locks/constraints. Npgsql modell-/snapshot-/migrasjonsoperasjonskontroll bygger bare metadata og åpner ingen forbindelse. Ingen SQL-generering ble brukt som erstatning for eksekveringsbevis.

Dashboard-runneren har fra før en ToQueryString-kontroll mot Npgsql uten åpning av forbindelse; øvrige data er InMemory. De ni PostgreSQL-bundne smoke-programmene er ikke kjørt. Ingen browser-E2E-/layout-/tastaturkontroll er utført; komponentene er rendret og editorhandlinger kjørt i HtmlRenderer.

## Begrensninger og overlevering

Ingen kjent uferdig funksjonell kildeoppgave innen fase 2. Reell PostgreSQL-persistens, referanseintegritet/unikhet, transaksjoner, samtidighet og migrasjonskjede må kontrolleres i senere separat autorisert miljø. Eksisterende database trenger ny migrasjon før applikasjonens nye spørringer kan brukes; den er ikke anvendt her. Ingen database eller vanlig Web-applikasjon ble startet.

Fase 3 kan bygge AgreementDimensionRule/-Assignment og tjenesteregler oppå eksisterende shared Dimensions med Translations, DimensionLocalization og den uendrede GUID-revisjons-/kontolåskonvensjonen. Det finnes ennå ingen Bruk i Avtaler-fane, Agreements-regler/tildelinger, nytt avtale-UI eller nytt dashboard. GEO/DEPT/demo og legacy er urørt. Neste migrasjonskilde må være ny og additiv; historiske filer forblir urørlige. Fase 3 er ikke påbegynt.

- **PostgreSQL-migrasjonskjøring: NOT PERFORMED — POST_RUN_VERIFICATION**
- **Opprettelse av ny database og databasebasert demo-initialisering: NOT PERFORMED — POST_RUN_VERIFICATION**
- **PostgreSQL-basert persistens, constraints og concurrency: NOT PERFORMED — POST_RUN_VERIFICATION**

## Opprettede og endrede filer i fase 2

Følgende liste utelater de fem uendrede fase-1-filene som allerede fantes ved start.

Endret:

- `src/TenantPlatform.Core/Dimensions/Dimension.cs`
- `src/TenantPlatform.Core/Localization/LanguageHelper.cs`
- `src/TenantPlatform.Core/Localization/TranslationHelper.cs`
- `src/TenantPlatform.Infrastructure/Persistence/Migrations/TenantPlatformDbContextModelSnapshot.cs`
- `src/TenantPlatform.Infrastructure/Persistence/TenantPlatformDbContext.cs`
- `src/TenantPlatform.Web/Components/Pages/Dimensions/DimensionEditorDialog.razor`
- `src/TenantPlatform.Web/Components/Pages/Dimensions/DimensionHistoryView.razor`
- `src/TenantPlatform.Web/Components/Pages/Dimensions/DimensionOverview.razor`
- `src/TenantPlatform.Web/Components/Pages/Dimensions/DimensionsPage.razor`
- `src/TenantPlatform.Web/Components/Pages/Leasing/DimensionChoices.razor`
- `src/TenantPlatform.Web/Components/Pages/Leasing/LeasingClassificationEditor.razor`
- `src/TenantPlatform.Web/Components/Pages/Leasing/LeasingClassificationView.razor`
- `src/TenantPlatform.Web/Components/Pages/Leasing/LeasingReportPage.razor`
- `src/TenantPlatform.Web/Resources/TenantPlatformResources.en-GB.resx`
- `src/TenantPlatform.Web/Resources/TenantPlatformResources.nb-NO.resx`
- `src/TenantPlatform.Web/Resources/TenantPlatformResources.sv-SE.resx`
- `src/TenantPlatform.Web/Services/Dimensions/DimensionService.cs`
- `src/TenantPlatform.Web/Services/Leasing/LeasingClassificationDtos.cs`
- `src/TenantPlatform.Web/Services/Leasing/LeasingService.Classification.cs`

Ny:

- `docs/features/unified-dimensions-phase-2.md`
- `src/TenantPlatform.Core/Dimensions/DimensionLocalization.cs`
- `src/TenantPlatform.Core/Dimensions/DimensionTranslation.cs`
- `src/TenantPlatform.Infrastructure/Persistence/Configurations/DimensionTranslationConfiguration.cs`
- `src/TenantPlatform.Infrastructure/Persistence/Migrations/20261112084722_AddDimensionTranslations.Designer.cs`
- `src/TenantPlatform.Infrastructure/Persistence/Migrations/20261112084722_AddDimensionTranslations.cs`
- `src/TenantPlatform.Infrastructure/Persistence/TenantPlatformDbContextDesignTimeFactory.cs`
- `tests/TenantPlatform.Dimensions.SmokeTests/ComponentChecks.cs`
- `tests/TenantPlatform.Dimensions.SmokeTests/LocalizationChecks.cs`
- `tests/TenantPlatform.Dimensions.SmokeTests/Program.cs`
- `tests/TenantPlatform.Dimensions.SmokeTests/README.md`
- `tests/TenantPlatform.Dimensions.SmokeTests/ServiceChecks.cs`
- `tests/TenantPlatform.Dimensions.SmokeTests/TenantPlatform.Dimensions.SmokeTests.csproj`
