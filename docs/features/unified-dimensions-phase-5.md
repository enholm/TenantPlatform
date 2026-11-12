# Unified Dimensions — fase 5: full konvertering av demodata

**Status: FERDIG for fase 5.** Produksjonsgeneratoren lager hele Magida-datasettet med GEO/DEPT, 66 verdier og 600 eksplisitte tildelinger til 300 avtaler. Alle mappinger og referansefelter er sammenlignet mot uendret fase-1-JSON. Rene tester og bygg passerer. Initializer og PostgreSQL er ikke kjørt. Fase 6 er ikke startet.

## 1. Arbeidsgrunnlag og filer

- Arbeidskatalog: `/Users/morten/Dev/TenantPlatform`.
- Branch: `main`.
- Startcommit: `f2f2d67338fd05e59624423aad675ec4f8ca415d`.
- Grunnlag: `unified-dimensions.md`, fase-1–4-rapportene, den frosne fase-1-pakken, brukerens fase-5-prompt, innlimte AGENTS.md-instruksjoner og repoets `Agents.md`. Faktisk initializer, SeedIds, Leasing-helperne, modellene og eksisterende DemoSeedChecks er kontrollert.
- De godkjente fase-1–4-endringene lå fortsatt sporet/usporet i checkouten. SHA-256 for **706 filer** ble registrert ved fasestart i `/tmp/tenantplatform-phase5-start-sha256.json`; manifestets SHA-256 er `a36ae4b7f9c177a38cd384ff28ee39c0ad821b85864260aa076e1c342714b146`.
- Bare de to eksisterende filene nedenfor er endret fra fasestart. Ingen tidligere filer er slettet. Alle øvrige tidligere kildeendringer og nye filer er bevart byteidentisk. Ingen reset, branchbytte, commit, merge, push, deploy, AgentOrchestrator, rettighetsutvidelse eller endring av lokale begrensninger.

| Status fra fasestart | Fil |
|---|---|
| Endret | `src/TenantPlatform.Infrastructure/Initialization/DemoDataInitializer.cs` |
| Endret | `src/TenantPlatform.Infrastructure/Initialization/SeedIds.cs` |
| Ny | `src/TenantPlatform.Infrastructure/Initialization/MagidaDemoData.cs` |
| Ny | `src/TenantPlatform.Infrastructure/Initialization/MagidaDemoGraph.cs` |
| Ny | `tests/TenantPlatform.DemoDimensions.SmokeTests/TenantPlatform.DemoDimensions.SmokeTests.csproj` |
| Ny | `tests/TenantPlatform.DemoDimensions.SmokeTests/Program.cs` |
| Ny | `tests/TenantPlatform.DemoDimensions.SmokeTests/EquivalenceChecks.cs` |
| Ny | `tests/TenantPlatform.DemoDimensions.SmokeTests/ReconciliationChecks.cs` |
| Ny | `docs/features/unified-dimensions-phase-5.md` |

Ingen skjema-, Core-, Web-, dashboard- eller generell legacy-endring. Det nye testprosjektet refererer Web for de eksisterende hierarki-/lokaliseringshjelperne og Infrastructure-generatoren. Det innfører ingen PackageReference eller produksjonsavhengighet.

## 2. Ren produksjonsgenerering, samordning og persistens

`MagidaDemoData.Create(userId, recordedUtc)` bygger en ren kandidatgraf. Kontoen er den eksisterende `SeedIds.MagidaAccount`; eier/aktør og ett fanget tidspunkt er eksplisitte argumenter. Metoden har ingen DbContext, klokkeavlesning, filinnlesing eller applikasjonsoppstart. Den leser aldri docs-/testfiler. De opprinnelige navnelistene, løkkene og formlene er flyttet fra initializer, med dimensjoner i stedet for legacy-entiteter og to tildelinger i stedet for legacy-FK-er.

`MagidaDemoGraph.MissingFrom(existing)` beregner en ren, innsettingsbasert batch. Den endrer ingen eksisterende objekter og gjør ingen persistens. Metadata filtreres mot eksisterende identitet/naturlige nøkler. Hele avtalegrafen filtreres på eksisterende avtale-ID, slik den gamle generatoren gjorde.

`SeedMagidaAsync` beholder sin opprinnelige konto-/bruker-/login-/rolleoppløsning og fanger `recordedUtc` én gang. Den **kaller den samme Create-metoden som testene kjører**, laster eksisterende metadata og avtale-ID-er med eksplisitt AccountId og AsNoTracking, kaller MissingFrom og legger alle batchens 13 samlinger til de riktige DbSets. Eksisterende SaveChangesAsync-punkt er beholdt. Faktisk EF-innsetting, transaksjon og audit er ikke utført eller verifisert i denne fasen. Ingen testprovider-bypass er lagt i initializer.

Kandidatgrafen inneholder 3102 rader: 2 dimensjoner, 4 oversettelser, 66 verdier, 2 Agreements-regler, 16 motparter, 4 indekser, 8 indeksverdier, 300 avtaler, 600 dimensjonstildelinger, 300 indeksvalg og 600 rader i hver av Lines, LineVersions og PriceVersions. Konto-/identitetsgraf og øvrig demo håndteres fortsatt av uendret initializer-kode.

## 3. GEO/DEPT, oversettelser, koder og identitet

Bare Magida genererte de gamle hierarkiene; Nordic Property får ingen nye klassifiseringsdimensjoner.

| Egenskap | GEO | DEPT |
|---|---|---|
| Code | GEO | DEPT |
| Fallback / nb-NO | Geografi | Avdeling |
| en-GB | Geography | Department |
| IsActive | true | true |
| AllowMultiple / LeafOnly | false / false | false / false |
| Agreements IsEnabled / IsRequired | true / false | true / false |
| SortOrder på dimensjonen | 30 | 40 |
| Stabil dimensjons-ID | MagidaDimension(3) | MagidaDimension(4) |

Navn følger eksisterende norsk seed-fallback. Fire ordinære DimensionTranslation-rader bruker nb-NO/en-GB. Ingen svensk oversettelse er oppfunnet. Testene kjører fase-2-helperen og bekrefter engelsk oppslag og svensk fallback både via kontostandard og lagret navn.

**Ingen LeasingDimensionRule for GEO/DEPT** genereres. PROJ/EQUIP har fortsatt MagidaDimension(1)/(2), SortOrder 10/20 og uendret Leasing-oppsett.

Alle **66 verdi-ID-er er bevart**. De eksisterende SeedIds-metodene MagidaDivision, MagidaDepartment, MagidaContinent, MagidaCountry og MagidaCapital brukes som identitetsfabrikker for DimensionValue, ikke til legacy-generering. Ingen ID-remapping er nødvendig. Verdikodene bruker stabil nodekategori og tresifret seed-nummer: DIVISION-001…005, DEPARTMENT-001…025, CONTINENT-001…004, COUNTRY-001…016 og CAPITAL-001…016. Navn eller oversettelser inngår ikke i kodene.

SeedIds er bare utvidet med namespace **34** for de fire oversettelsene og **35** for de 600 tildelingene. Alle tidligere definisjoner/formler er byteuendret. Namespace-numrene er desimale; MagidaId formatterer dem som heksadesimale GUID-segmenter. For avtale med nullbasert indeks i får GEO-tildelingen nummer `2*i+1` og DEPT nummer `2*i+2`. Dimensjoner, verdier og regler får stabile, ikke-tomme revisjoner lik sin dimensjons-/verdi-ID. Kandidat-ID-ene er testet for unikhet og kolliderer ikke med Leasing-namespace 19–33, bortsett fra de uttrykkelig reserverte nye dimensjonsnumrene 3/4 i namespace 20.

## 4. Hele hierarkiene og visningsrekkefølge

| Hierarki | Nivåer | Antall |
|---|---|---|
| GEO | 4 kontinenter → 16 land → 16 hovedsteder | **36** |
| DEPT | 5 divisjoner → 25 avdelinger | **30** |

Alle navn, ID-er, foreldre, kontoer, nivåer, stier, forfedre og aktivitetsflagg er sammenlignet node for node mot [demo-reference.json](unified-dimensions-phase-1/demo-reference.json). Alle legacy-beskrivelser i nodegrunnlaget er null; ingen utfylt beskrivelse går tapt. Foreldrene beholdes og er valgbare fordi LeafOnly=false.

Kandidatlisten bevarer generatorrekkefølgen, som også kontrolleres mot generation_order. Alle verdier får SortOrder=0. Dermed følger den generiske DimensionHierarchy eksisterende alfabetiske navnesortering mellom søsken, slik det gamle AccountRegisterHierarchy gjorde, fremfor å introdusere generatorrekkefølgen som ny UI-sortering. Testene sammenligner faktisk ny og gammel full hierarkirekkefølge for **alle 66 verdier i nb-NO, en-GB og sv-SE**. Disse seed-navnene gir samme rekkefølge med de to hjelpernes eksisterende sorteringskonvensjoner.

## 5. Alle 300 mappinger og fordelinger

Testen leser alle 300 referanseoppføringene og sammenligner avtale-ID og begge tildelte verdi-ID-er individuelt. Det finnes nøyaktig én GEO- og én DEPT-tildeling per avtale, begge innen Magida. Ingen forfedre persisteres som ekstra tildelinger. Beregnet medlemskap gjennom forfedrene sammenlignes også for hver avtale.

Den uendrede mappingen, som identifiserer alle 300 avtaler uten utvalg av eksempler, er:

- For avtalenummer n=1…300 er i=n−1.
- DEPT-nummer er `floor(i/12)+1`; divisjon er `floor((department−1)/5)+1`, avdeling i divisjonen er `(department−1)%5+1`.
- Kontinent c er `i%4` i rekkefølgen Asia, Europa, Afrika, Amerika.
- Landindeks k er `floor(i/4)%4`; verdi er MagidaCapital(`c*4+k+1`). Tabellen nedenfor angir alle 16 verdier i denne formelen.

| Kontinent, inklusive 75 hver | Land → eksplisitt hovedstad | Avtaler ved hovedstaden / inklusive landet |
|---|---|---|
| Asia | Japan → Tokyo | 19 |
| Asia | Kina → Beijing | 19 |
| Asia | India → New Delhi | 19 |
| Asia | Thailand → Bangkok | 18 |
| Europa | Norge → Oslo | 19 |
| Europa | Sverige → Stockholm | 19 |
| Europa | Tyskland → Berlin | 19 |
| Europa | Frankrike → Paris | 18 |
| Afrika | Kenya → Nairobi | 19 |
| Afrika | Egypt → Kairo | 19 |
| Afrika | Ghana → Accra | 19 |
| Afrika | Marokko → Rabat | 18 |
| Amerika | USA → Washington, D.C. | 19 |
| Amerika | Canada → Ottawa | 19 |
| Amerika | Brasil → Brasília | 19 |
| Amerika | Argentina → Buenos Aires | 18 |

Hver av de 25 avdelingene har **12 eksplisitte avtaler**. Hver divisjon — Eiendom, Teknologi, Drift, Innkjøp og Forretningsutvikling — har **60 unike inklusive avtaler**. Kontinenter, land og divisjoner har **0 eksplisitte avtaler**. GEO har totalt 300 eksplisitte hovedstadstildelinger; DEPT har totalt 300 eksplisitte avdelingstildelinger. Testen kontrollerer eksplisitt og inklusiv unik fordeling ved **hver av de 66 nodene**, ikke bare disse summeringene.

## 6. Øvrige avtaledata og demo

EquivalenceChecks sammenligner referansens faktiske felter med serialiserte produksjonsentiteter og gir avtalenummer/ID, node-ID, linje-ID og feltsti ved avvik. Den bruker ikke en alternativ generator eller en checksum som erstatning for feltkontroll. Frossen JSON brukes bare som forventningsgrunnlag i testprosjektet.

For alle 300 avtaler kontrolleres hele properties-objektet, inkludert titler, beskrivelser, type, retning, valuta, indeks, virkelig motpart, eier, status, datoer, form/livssyklus, oppsigelse, påminnelser, revisjon og øvrige standardfelt. Bare GeographicAreaId og OrganizationElementId forventes erstattet med null i nye kandidater; deres gamle verdier kontrolleres i de nye tildelingene. Bygg/enhet og Terms var null i referansen og forblir null. Ingen nye dokumenter eller øvrige relasjoner oppfinnes.

Alle 300 indeksvalg, 600 linjer, 600 aktive linjeversjoner og 600 prisversjoner sammenlignes feltvis. Kvantum, priser, fakturering, frekvens, kalenderanker, indeksregulering og start-/sluttdatoer er uendret. Dokumentlisten på hver linjeversjon forblir tom som i referansen. De fire årsindeksene og åtte indeksverdiene, inkludert PeriodKey, Revision, publiseringsstandarder og de eksakte desimaltallene, er sammenlignet. Alle 16 faktiske Organization-motparter beholder referansens felter.

Referansens `$resolvedMagidaUserId` og `$recordedUtc` sammenlignes mot de injiserte argumentene. Den opprinnelige gjenbruken av bruker funnet på ID eller e-post er statisk uendret; den faktiske databaseoppløsningen er ikke kjørt. Testen kontrollerer i tillegg en annen identitet og et senere tidspunkt uten at deterministiske ID-er endres.

**Øvrig demo er bevart ved målrettet kildekontroll:**

- Hele `DemoDataInitializer.Leasing.cs` er byteidentisk både med fasestart og den frosne commiten. SHA-256: `57f24de2138edcce54b11d18fa1d34dc836bea327a8b2df2eeda06c9ccac9e35`.
- Initializers kallrekkefølge, Magida konto-/identitetsoppsett, delte AddMissingMagidaEntitiesAsync-helper og hele Nordic Property-genereringen etter den erstattede blokken er byteidentiske med fasestart. Organisasjoner, bygg, enheter, adresser, occupancies, tjenester og brukerrelasjoner er dermed ikke skrevet om.
- Alle tidligere SeedIds er uendret. Den delte Leasing-helperen er beholdt fordi Leasing-partialen fortsatt bruker den.
- Leasing har fortsatt PROJ med fire røtter, EQUIP med tre røtter/ni blader, fem rammer, ni anskaffelser, 17 linjer og 34 dimensjonsvalg, samt sin uendrede finansierings-/betalings-/livssyklusgenerering. Dette er **kildebevaring**, ikke en faktisk kjørt ny Leasing-graf. Eksisterende DemoSeedChecks er inspisert og kompilert, aldri kjørt. Databasefrie Dimensions-tester kjører fortsatt de eksisterende Leasing-beregnings-/renderingskontrollene.

## 7. Idempotens, brukerredigeringer og delvise grafer

Dette er en videreføring av insert-missing/skip-konvensjonen, ikke generell upsert, datamigrering eller reparasjon.

| Tilstand | Samordningspolicy og verifikasjon |
|---|---|
| Tom ny tilstand | Hele kandidatgrafen blir innsettingsbatch. Ren batch er sammenlignet med full kandidat, uten database. |
| Gjentatt full tilstand | Ingen nye rader. Nytt klokkeslett eller annen oppløst bruker gir ingen nye deterministiske ID-er. |
| Eksisterende dimensjoner/verdier/motparter/indeksdata | Samme ID bevares, uten oppdatering av navn, foreldre, aktivitet, regler eller revisjoner. |
| Oversettelser | Eksisterende ID bevares; en rad med samme dimensjon/språk og annen ID bevares også, uten duplikat. Manglende kandidatrader kan tilføyes. |
| Agreements-regler | Eksisterende `(AccountId, DimensionId)` bevares, også deaktivert eller endret required. Manglende regel opprettes med seed-standard. Ingen løpende reset. |
| Eksisterende avtale, redigert eller uredigert | **Hele grafen hoppes over**: ingen dimensjonstildelinger, indeksvalg, linjer eller versjoner tilføyes. Tittel, eier, status, arkivering, revisjon og klassifiseringer berøres ikke. |
| Eksplisitt fjernede dimensjonsvalg | Gjenopprettes ikke når avtalen finnes. Testet også med alle assignment-rader fjernet. |
| Eksisterende gammel legacy-avtale | Ingen kopiering/konvertering; legacy-FK-er beholdes i den eksisterende raden, og nye tildelinger legges ikke til. Dagens database er ikke oppgradert med demodata. |
| Delvis metadata, ingen avtaler | Manglende metadata legges til, eksisterende rader bevares. Testet med manglende GEO-verdier, oversettelser og regel. |
| 137 komplette avtalegrafer finnes | Bare de 163 manglende komplette grafene foreslås; batchen inneholder 326 tildelinger og 326 linjer med versjoner. Ren påføring og ny samordning gir tom batch. |
| Avtalerad finnes, men barn mangler | **Repareres ikke**, som i gammel policy. Det kan innebære manglende dimensjonsvalg eller finansrader. Ikke påstått full gjenoppretting etter avbrudd/manuelle inngrep. |
| Manglende seed-ID, men kode opptatt av annen ID | Avvises eksplisitt for dimensjon/verdi; ingen stille remapping, overskriving eller ny kolliderende kandidatinnsetting. |

Alle samlinger kontrolleres for Magida-eierskap i samordningen; en fremmed konto avvises både på kandidat- og eksisterende side. Initializers faktiske lesinger har dessuten eksplisitt AccountId. Testene endrer konto i hver av de 13 samlingene og bekrefter avvisning.

Samordningen forutsetter gyldige eksisterende FK-relasjoner. Den er ikke en orphan-/integritetsreparasjon. Det eksisterende SaveChanges-punktet samler Magida-grafen, mens øvrige initializer-/Leasing-steg har sine eksisterende lagringspunkter. Reell atomisitet, parallelle initialiseringer, FK-er, kodekollisjoner mot en konkret database og idempotens etter faktisk prosessavbrudd er **uverifisert**. Metadata har ingen tombstone-policy; slettede seed-metadata kan bli foreslått igjen. Eksplisitt fjernede avtaletildelinger beskyttes av whole-agreement-skip.

## 8. Faktisk verifikasjon

| Kommando/kontroll | Resultat |
|---|---|
| `python3 docs/features/unified-dimensions-phase-1/derive-reference.py --check` før endringer | PASS; alle tre JSON-filer reprodusert byteidentisk |
| `dotnet build TenantPlatform.sln --no-restore` | PASS; fire prosjekter, 0 feil, 0 advarsler |
| `dotnet restore tests/TenantPlatform.DemoDimensions.SmokeTests --source /Users/morten/.nuget/packages -p:NuGetAudit=false` | PASS; lokalt pakkegrunnlag, ingen nye pakkeavhengigheter |
| `dotnet build tests/TenantPlatform.DemoDimensions.SmokeTests --no-restore` | PASS; 0 feil, 0 advarsler, også etter siste testendring |
| `dotnet build <prosjektsti> --no-restore` for alle 13 `tests/*/*.csproj` | PASS; alle exit 0 |
| `dotnet run --project tests/TenantPlatform.DemoDimensions.SmokeTests --no-build --no-restore` | PASS; ni rapporterte kontrollgrupper, 732688 felt-/struktur-/traverseringsassertions inklusive gjentatte determinismekontroller; full graf, ingen DbContext/initializer |
| `dotnet run --project tests/TenantPlatform.Dimensions.SmokeTests --no-build --no-restore` | PASS; 91 kontroller |
| `dotnet run --project tests/TenantPlatform.AgreementDimensions.SmokeTests --no-build --no-restore` | PASS; 166 kontroller |
| `dotnet run --project tests/TenantPlatform.Dashboard.SmokeTests --no-build --no-restore` | PASS; 49 PASS-linjer |
| `python3 docs/features/unified-dimensions-phase-1/derive-reference.py --check` etter endringer | PASS; 36 GEO, 30 DEPT, 300 mappinger, 600 linjer; uendret referanse |
| SHA-256 mot fasestart for migrasjoner/snapshot, tidligere rapporter/referanser og tidligere arbeid | PASS; 89 migrasjonsfiler inkl. snapshot, alle åtte tidligere rapport-/referansefiler og alt tidligere arbeid utenfor de to oppførte filene byteuendret; ingen slettinger |
| Python-sammenligning av initializer utenfor erstattet blokk, Leasing-fil og eksisterende SeedIds | PASS; bytebevaring som beskrevet over |
| `rg -n 'GeographicArea\|OrganizationElement' src/TenantPlatform.Infrastructure/Initialization` | Ingen treff; ingen legacy-entitetsgenerering eller legacy-avtalereferanser i initializer/generator |
| `git diff --check` | PASS |

De 13 eksplisitt bygde testprosjektene er AccountRegisters, AgreementAnalysis, AgreementDimensions, AgreementFollowup, AgreementNotice, AgreementPeriods, AgreementProcessing, Agreements, Dashboard, DemoDimensions, Dimensions, Leasing og MeetingRooms. De **ni databasebundne programmene er bare kompilert**. Ingen DemoDataInitializer, vanlig Web-start eller databasebundet runner er kjørt.

Enkelte eldre testbygg har fortsatt NU1902/NU1903 fra cache for System.Security.Cryptography.Pkcs 10.0.0 og MSB9008 om et gammelt Application-prosjekt i restore-cache. Ingen uvedkommende kildeendringer eller undertrykking er gjort for å rydde disse. Det nye testprosjektet bygger uten advarsler.

Lokale logger ligger i `/tmp/tenantplatform-phase5-build.log`, `/tmp/tenantplatform-phase5-demo-build.log`, `/tmp/tenantplatform-phase5-demo-restore.log`, `/tmp/tenantplatform-phase5-test-builds.log` og `/tmp/tenantplatform-phase5-{DemoDimensions,Dimensions,AgreementDimensions,Dashboard}-run.log`. Logger og startmanifest er lokale evidensfiler, ikke produksjonsavhengigheter.

## 9. Evidensgrenser og bevarte referanser

**Faktisk kjørt produksjonsgenerering:** Create bygger hele grafen uten database og sammenlignes feltvis mot frossen JSON. Den samme metoden brukes av initializer. Produksjonskoden har ingen referansefil som input.

**Faktisk testet samordning:** MissingFrom kjøres mot tom, full, redigert, delvis og fremmed-konto-tilstand. Ren batchpåføring og ny samordning er testet. Dette bekrefter beslutningslogikken, ikke EF-/PostgreSQL-persistens.

**Statisk kontroll:** Initializers kontopredikater, konsum av batchen, AddRange/SaveChanges, gjenbruk av identitet, fravær av legacy-generering og bevaring av øvrig demo er gjennomgått. Leasing/PROJ/EQUIP er kontrollert ved bytebevaring og eksisterende databasefrie beregningskontroller; ingen påstand om kjørt Leasing-initialisering.

**Ikke utført:** PostgreSQL-tilkobling, SQL-eksekvering, migrasjonskjøring, databaseopprettelse/reset, databasebasert demogenerering eller datakopiering. Eksisterende offline-suiters provider-/SQL-generering åpner ingen databaseforbindelse og er ikke SQL-eksekvering.

Alle **89** migrasjonsfiler, Designer-filer og gjeldende snapshot er byteidentiske med **fase-5-start**, inkludert de godkjente additive fase-2-/fase-3-filene. Ingen nye migrasjoner eller skjemaavvik er funnet. Samlet Git-diff viser fortsatt tidligere fasers snapshot-endring; denne fasen endret den ikke.

Fase-1-pakkens script og tre JSON-filer er urørt. Etterkontrollen reproduserer:

- demo-reference.json: `18c1e1083523112418b9f36bd8d4ab3c196cc86aac8924d4254ea64590a67701`.
- legacy-inventory.json: `21f2e6dbe2331c51df799232ff2eefd2f30c39c227873336532b0bd787cb16c5`.
- migration-source-sha256.json: `0b9f2a30126b7a69e2232d376c1def6e70da1b23411daa234b09dc9d23e3046e`.

Det frosne migrasjonsmanifestet beskriver kildecommiten, mens fase-5-startmanifestet også inkluderer senere godkjente additive migrasjoner. Disse to kontrollgrunnlagene er ikke blandet sammen.

## 10. Begrensninger og overlevering til fase 6

Ingen kjente uferdige implementasjonspunkter innen fase 5. Faktiske lagrede data og full databaseidempotens er fortsatt uverifisert. En separat autorisert prosess må senere gjenskape en disponibel database, kjøre migrasjonskjeden og initializer, sammenligne alle rader/mappinger mot referansen, kjøre igjen og kontrollere redigerte/eksplisitt fjernede valg og delvise tilstander. Ingen eksisterende database er endret eller konvertert nå.

Dette er fortsatt en overgangsfase: nye demoavtaler har null i legacy-klassifiseringsfeltene, mens det gamle dashboardets hardkodede Geography/Organization-gruppering fortsatt leser disse feltene. **Fase 6 må bruke de nye eksplisitte dimensjonstildelingene.** Intet nytt dashboard er implementert her. Legacy-modeller/-tjenester og øvrige forbrukere beholdes frem til sine senere faser.

Fase 6 skal startes med en ny manuell prompt. Gjenbruk de ordinære GEO/DEPT-dimensjonene, fase-3-kontraktene og det frosne ekvivalensgrunnlaget; ikke legg inn legacy-synkronisering eller demokonvertering av eksisterende poster.

- PostgreSQL-migrasjonskjøring: **NOT PERFORMED — POST_RUN_VERIFICATION**
- Opprettelse av ny database og databasebasert demo-initialisering: **NOT PERFORMED — POST_RUN_VERIFICATION**
- Databasebasert demodataekvivalens og idempotens: **NOT PERFORMED — POST_RUN_VERIFICATION**
