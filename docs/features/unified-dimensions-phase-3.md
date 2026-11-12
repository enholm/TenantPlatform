# Unified Dimensions — fase 3: avtalekonfigurasjon og tildelinger

**Status: FERDIG for fase 3.** Kontoavgrenset konfigurasjon, eksplisitte dimensjonstildelinger, servervalidering, autoriserte lesekontrakter, historikk og migrasjonskilde er implementert og kontrollert uten database. Fase 4 er ikke startet. PostgreSQL-persistens, transaksjoner, constraints og faktisk låsing/concurrency er ikke verifisert.

## 1. Arbeidsgrunnlag og omfang

- Arbeidskatalog: `/Users/morten/Dev/TenantPlatform`.
- Branch: `main`.
- Startcommit: `f2f2d67338fd05e59624423aad675ec4f8ca415d`.
- Ved start fantes alle tidligere sporede og usporede fase-1-/fase-2-endringer fra de godkjente fasene. Starttilstanden ble registrert med SHA-256 for 683 eksisterende filer; relevante overlappsfiler ble også lagret lokalt for sammenligning. Ingen reset, overskriving av tidligere arbeid, branchbytte, commit, merge, push eller deploy er utført.
- Grunnlag: `unified-dimensions.md`, fase-1-/fase-2-rapportene, innlimte AGENTS.md-instruksjoner og kontroll av faktisk kode. Ingen fysisk AGENTS.md eller nye lokale instruksjoner ble funnet. Ingen AgentOrchestrator, rettighetsutvidelse eller endring i autentiserings-/autorisasjonsarkitekturen.
- Ingen Razor-kode, demo-generator, dashboard, legacy-modell eller Leasing-kode er endret i fase 3. Eksisterende legacy-felter/kontroller og deres FK-er/tabeller beholdes. Ingen synkronisering med generiske tildelinger eller finansielle dimensjonsfordelinger er innført.
- Produksjonsavhengigheter og eksisterende prosjektfiler er uendret. Det nye isolerte testprosjektet bruker samme EF InMemory 10.0.10 som de eksisterende databasefrie testprosjektene.

## 2. Modell og relasjonelle garantier i kilde

`Core/Agreements/AgreementDimensions.cs` inneholder:

| Entitet | Innhold og hensikt |
|---|---|
| AgreementDimensionRule | AccountId + DimensionId, IsEnabled, IsRequired og egen GUID-Revision. Availability/required tilhører Agreements; AllowMultiple/LeafOnly er fortsatt kun globale dimensjonsregler. |
| AgreementDimensionAssignment | Servergenerert Id og stabile AccountId, AgreementId, DimensionId, ValueId. Bare eksplisitte verdier lagres; ingen beregnede forfedre. |
| AgreementDimensionHistory | Append-only konto-/avtalekoblet hendelse med aktør, tidspunkt og før-/etter-JSON for de dimensjonssettene som faktisk endres. Den blandes ikke inn i notice- eller økonomihistorikk. |

EF-konfigurasjonene ligger i `Infrastructure/Persistence/Configurations/AgreementDimensionConfiguration.cs` og bruker eksisterende assembly scanning. Det trengs ingen nye alternate keys på eksisterende entiteter: Agreement `(AccountId, Id)`, Dimension `(AccountId, Id)` og DimensionValue `(AccountId, DimensionId, Id)` finnes allerede.

- Regelen har sammensatt primærnøkkel `(AccountId, DimensionId)`, account-aware dimensjons-FK, Restrict og concurrency token. Check constraint er `NOT IsRequired OR IsEnabled`.
- Tildeling har unik indeks `(AccountId, AgreementId, DimensionId, ValueId)`. FK til Agreement bruker konto + avtale; FK til DimensionValue bruker konto + dimensjon + verdi. Verdiens eksisterende FK til Dimension sikrer dermed også dimensjonens kontoeierskap. Dette forhindrer krysskobling mellom kontoer og feil dimensjon/verdi i det relasjonelle skjemaet.
- Tildelingens sekundære `(AccountId, DimensionId, ValueId)`-indeks støtter FK og dimensjonsoppslag. Ingen fiktiv ancestor-rad eller egen tildelingsrevisjon introduseres: avtalen er revisjonsgrensen.
- Historikk har account-aware avtale-FK, User-FK, JSONB før/etter og indeks `(AccountId, AgreementId, RecordedUtc)`. Alle nye FK-er bruker etablerte Restrict-regler.

Dette er verifiserte modell-/migrasjonsmetadata, ikke bevis på PostgreSQL-eksekvering av constraintene.

## 3. Konfigurasjon og endringssemantikk

Semantikken nedenfor ble dokumentert før implementeringen og er videreført i kode og tester.

`SaveAgreementRequest.DimensionChanges` er nullable og som standard null. Den er en liste med patcher, slik at gjentatte dimensjons-ID-er kan avvises eksplisitt. Hver patch har DimensionId og en nullable ValueIds-liste: null ValueIds er ugyldig input; eksplisitt `[]` betyr tømming. Null eller tom ytre liste endrer ingen dimensjoner. Klienten sender ingen AccountId, historikkflagg eller rad-ID-er i patchen.

- Create validerer alle enabled/required-regler, også ved null input. Required uten aktiv dimensjon og minst én aktiv valgbar verdi gir konfigurasjonsfeil; det blir ikke implisitt valgfritt.
- Edit uten patch for en dimensjon bevarer dens rader og kan ikke opprette manglende required-valg. Et identisk sett (også tomt gammelt sett) er uendret, etter duplikat-/identitetskontroll.
- En reell endring validerer required og AllowMultiple på det resulterende settet. Beholdte historiske ID-er valideres fortsatt mot korrekt konto/dimensjon/verdi, men slipper nyvalgkrav om aktivitet, tilgjengelighet og LeafOnly. Nye tillegg må bestå samtlige gjeldende nyvalgkrav, inkludert aktive forfedre.
- Global overgang fra multiple til single blokkerer ikke et identisk historisk sett eller urelatert edit; en reell endring i samme dimensjon må gi høyst én verdi. Historiske foreldrenoder kan beholdes ved en reell endring etter aktivering av LeafOnly; nye tillegg må være blader.
- Eksplisitt fjerning er tillatt selv om dimensjonen er inaktiv/utilgjengelig, så lenge gjeldende required fortsatt oppfylles. Ingen automatisk arv, legacy-synkronisering eller økonomisk fordeling lagres.
- Regelinput har IsEnabled og IsRequired; tilgjengelighet er autoritativ ved normalisering: IsEnabled=false lagres alltid med IsRequired=false. IsRequired=true kan dermed bare persisteres når IsEnabled=true. Leasing-regler berøres ikke.


Konkrete eksempler:

| Situasjon | Input | Resultat |
|---|---|---|
| Ny avtale med obligatorisk D1 | null eller tom ytre liste | Avvises med AgreementDimensionRequired. |
| Ny avtale, D1 obligatorisk men inaktiv/uten aktive valgbare verdier | vilkårlig dimensjonsinput | Avvises med AgreementDimensionRequiredConfiguration før manglende-valg-feil. Administrator må korrigere konfigurasjonen. |
| Gammel avtale, D1 senere satt required, ingen D1-valg | endre kun tittel eller D2 | Tillatt; D1 forblir uten tildeling. |
| Gammel avtale, ingen D1-valg | D1: [] | Identisk gammelt sett; ingen endring eller required-validering ved edit. |
| Gammel avtale, required D1 har A | D1: [] | Reell tømming; avvises. |
| Gammel avtale har inaktiv A i D1 | utelat D1 eller send D1: [A] | Bevares; ingen ny tildelingshistorikk for identisk sett. |
| Gammel multi-dimensjon har inaktiv A | D1: [A, B], B er ny aktiv valgbar verdi | Tillatt hvis tilgjengelig/aktiv dimensjon og AllowMultiple=true; A-radens ID bevares, bare B legges til. |
| Global multi → single etter at A og B ble valgt | D1: [B, A] | Identisk sett, bevares. Reell endring som fortsatt gir to verdier avvises; endring til én beholdt verdi tillates. |
| LeafOnly aktivert etter foreldretildeling | behold forelder + legg til nytt blad | Historisk forelder beholdes; nytt blad valideres. Ny foreldretildeling avvises. |

Tvetydige gjentatte dimensjonspatcher, dupliserte ValueIds, null ValueIds, tomme GUID-er, fremmede/ukjente ressurser og feil dimensjon/verdi avvises server-side. Settene kopieres før asynkrone katalogoppslag, så caller-owned lister ikke kan endre den allerede validerte patchen underveis.

## 4. Historiske verdier og nye tillegg

`AgreementService.Dimensions.cs:ApplyAgreementDimensionsAsync` henter eksisterende tildelinger fra den aktuelle kontoen og avtalen. Klienten kan ikke levere tildelingsrad-ID, AccountId eller et troverdig historikkflagg. Historisk status bestemmes av medlemskap i det faktisk lagrede gamle settet.

Alle innsendte dimensjoner/verdi-ID-er kontrolleres mot kontoavgrenset katalog før identiske sett slipper videre validering. For en reell endring gjelder required og full-resultatets kardinalitet; bare forskjellen `ønsket − eksisterende` må være tilgjengelig/aktiv og bestå LeafOnly. `DimensionHierarchy.Build` gjenbrukes, slik at en inaktiv forfader gjør også en ellers aktiv underverdi uvalgbar.

Valideringsplanen for samtlige patcher ferdigstilles før noen tildelingsrad eller klassifiseringshistorikk endres. Beholdte rader oppdateres ikke; eksplisitt fjernede rader slettes og bare faktiske tillegg får nye server-ID-er. En ugyldig patch sent i forespørselen kan dermed ikke etterlate tidligere tildelingspatcher delvis endret før validering er ferdig. PostgreSQL-transaksjonens rollback på senere feil er fortsatt en separat uverifisert port.

En annen dimensjons manglende required-verdi, historiske verdier eller ugyldige required-konfigurasjon inngår ikke i edit-valideringen når den dimensjonen er utelatt. Opprettelse validerer derimot alle required-regler og all required-konfigurasjon også når input er utelatt.

Ni nye feilmeldinger følger eksisterende AgreementValidationException-/ressursnøkkelmønster og finnes i nb-NO, en-GB og sv-SE. Ingen nye UI-kontroller eller valideringsframework er innført.

## 5. Tilgang, låsing, revisjon og atomisitet

Eksisterende `RequireMemberAsync`, `Accessible`, `RequireAsync` og TenantAuthorizationService beholdes:

- Valgt konto og aktivt bruker-/kontomedlemskap kontrolleres på serveren; circuit-cache alene er ikke tilstrekkelig.
- Create/katalog for ny avtale bruker eksisterende opprettelsesrett (`CanCreateAgreementAsync`).
- Detaljkatalog og klassifiseringshistorikk krever kun lesetilgang til den konkrete avtalen. Eier/admin og eksplisitte read/edit-grants avgrenses som før; en reader får ikke edit ved å lese dimensjonsdata.
- Edit-katalog og writes krever redigeringsrett, og arkiv sperrer edit som før. En editor-grantee kan fortsatt ikke endre avtaleeier uten manage-rett.
- Regeladministrasjon bruker eksplisitt `CanManageAccountStructureAsync`, ikke read/edit-tilgang til en avtale. Platform-admin-claim alene gir ikke account-admin-rett.

Alle produksjonswrites bruker kortlivet factory-context og eksisterende kontolås. Regelendring låser konto. Create bruker nå også en eksplisitt transaksjon og kontolås, slik at lesing av required-regler ikke kan konkurrere med konfigurasjonsendring. Update følger eksisterende konto → avtale-låserekkefølge. Konfigurasjon og gamle tildelinger leses etter låsing.

Create-/Update-operasjonene er delt i en ytre relasjonell wrapper og private `CreateAgreementCoreAsync`/`UpdateAgreementCoreAsync`, etter samme direkte-EF-mønster som eksisterende Leasing-core. Kjernemetodene beholder autorisasjon, øvrig avtalevalidering, endring, GUID-revisjon, historikk og SaveAsync. Det er ingen produksjonsgren som omgår transaksjoner/locks for en testprovider.

Avtalens eksisterende GUID-revisjon/EF-token beskytter også dimensjonsendringer. Update sjekker innsendt revisjon før endringer og Touch fornyer revisjonen. Identiske dimensjonspatcher gir ingen ny tildelingshistorikk eller radendring; selve eksisterende Update-operasjonen fornyer fortsatt avtale-revisjonen som før. Modulregel har egen revisjon og eksisterende DimensionConcurrency-feilkonvensjon.

`ApproveAnalysisAsync` er også en eksisterende opprettelsessti. Den bruker nå samme dimensjonsvalidering under sin eksisterende transaksjon/kontolås ved create, og samme patchsemantikk ved edit. Dermed kan analyseopprettelse ikke omgå required. Dens eksisterende draft-lock og dokument-/motpartsflyt er beholdt; denne databasebundne arbeidsflyten er ikke kjørt i verifikasjonen.

Avtale, tildelingsrader, relevant historikk og audit lagres innen samme produksjonstransaksjon. Eksisterende økonomiske versjoner, notice-historikk, dokumenter, tilgangsrelasjoner, bygg/enhet og legacy-felt endres ikke av dimensjonslogikken. Testene sammenligner samtlige uvedkommende Agreement-egenskaper og komplette pris-/linjeversjoner før/etter.

Regelendringer føres i DimensionHistory med `Module=Agreements`, `IsEnabledInAgreements`, `IsRequiredAtRegistration` og før/etter-revisjon. De nye feltnavnene skiller dem fra Leasing-regler. Avtaletildelinger føres i egen append-only AgreementDimensionHistory: ett event per reell batch, bare berørte dimensjoner, med eksplisitte verdi-ID-er og daværende fallbacknavn/koder/stier. Senere navn-/hierarkiendringer omskriver ikke disse JSON-snapshotene. Den vanlige AuditSaveChangesInterceptor registrerer også regel-/rad-/historikkendringer.

## 6. Offentlige kontrakter for fase 4

Alle metodene finnes på IAgreementService og AgreementService; ingen UI-implementasjon er gjort nå.

| Kontrakt | Bruk og tilgang |
|---|---|
| GetAgreementDimensionRulesAsync(accountId, ct) | Administrativ oversikt over kontoens dimensjoner og Agreements-regler, inkludert localized Name/Description, global aktivitet, AllowMultiple/LeafOnly, modulflagg og regelens Revision. |
| SaveAgreementDimensionRuleAsync(accountId, AgreementDimensionRule, ct) | Konto-administrativ lagring; Availability er autoritativ, Required normaliseres bort ved disabled. Send returnert regelrevisjon, Guid.Empty ved første opprettelse. |
| GetAgreementDimensionOptionsAsync(accountId, agreementId: null, forEdit, ct) | Create-katalog for brukere med opprettelsesrett. Bare aktive/tilgjengelige dimensjoner og valgbare verdier. |
| GetAgreementDimensionOptionsAsync(accountId, agreementId, forEdit: false, ct) | Avtaleautorisert lesing uten edit-/registeradmin-krav; inkluderer eksisterende historiske valg. Returnerer AgreementId, Revision og CanEdit. |
| GetAgreementDimensionOptionsAsync(accountId, agreementId, forEdit: true, ct) | Samme katalog med krav om edit og ikke arkivert avtale. |
| GetAgreementDimensionHistoryAsync(accountId, agreementId, ct) | Avtaleautorisert lesing av tidligere batcher med aktør/tid og uforanderlige før-/etter-valg. |
| GetOptionsAsync / AgreementOptionsDto.Dimensions | Eksisterende create/edit-options er utvidet additivt og har fortsatt tidligere tilgangskrav og legacy-valglister. |
| GetAsync / AgreementDetailsDto.Dimensions og DimensionHistory | Eksisterende detaljkontrakt utvidet additivt. DimensionChanges forblir null ved lesing, slik at gammel DTO-round-trip ikke tømmer valg. |
| CreateAsync / UpdateAsync med SaveAgreementRequest.DimensionChanges | De ordinære autoriserte write-kontraktene. Klienten sender kun faktisk endrede dimensjoner; null/[] ytre liste bevarer alle. |

`AgreementDimensionOptionDto` inkluderer IsActive, IsEnabled, IsRequired, AllowMultiple, LeafOnly og verdier med Id, ParentId, Name, Code, Path, Depth, effektiv IsActive, Selectable, IsAssigned og avledet IsHistorical. Katalogen kombinerer tilgjengelige valg med faktisk tildelte historiske verdier; verdier som ikke er tilgjengelige for nye valg vises bare når de allerede er valgt. Detaljlesing krever ikke redigering. Konto-/avtalegrenser sjekkes før katalog eller historikk lastes.

Fase-2 DimensionLocalization og kontostandard brukes; ID-er/relasjoner er uavhengige av språk. Ingen verdioversettelser. Administrativ DimensionService.GetAsync brukes ikke som lesetilgang for vanlige avtalebrukere.

## 7. Migrasjon, snapshot og tidligere arbeid

Ny additiv migrasjon:

- `src/TenantPlatform.Infrastructure/Persistence/Migrations/20261112091606_AddAgreementDimensions.cs`.
- Tilsvarende `20261112091606_AddAgreementDimensions.Designer.cs`.
- Oppdatert `TenantPlatformDbContextModelSnapshot.cs` i samme mappe.

Up inneholder bare tre CreateTable og fire CreateIndex for agreement_dimension_rules, agreement_dimension_assignments og agreement_dimension_history. FK-er/check constraint opprettes som del av tabellene. Down dropper kun de tre nye tabellene. Ingen gamle kolonner/FK-er/tabeller fjernes, og ingen eksisterende data kopieres eller seedes. Ingen GEO/DEPT-opprettelse.

`bash scripts/add-migration.sh AddAgreementDimensions` brukte den eksisterende offline design-time factoryen fra fase 2. Factory og forbindelsessperre er byteuendret: ingen secrets/config-lesing, ingen vanlig Web-host og ingen forbindelse åpnet. Det er kun kildeforfatting. Metadata-testen bruker samme factory og verifiserer HasPendingModelChanges=false og at connection-objektet fortsatt er lukket.

Hashkontroll mot fase-3-start og fase-1-manifest:

- **86 av 86** eksisterende historiske migrasjons-/Designer-filer er byteidentiske, inklusive begge AddDimensionTranslations-filene.
- Hele fase-1-referansepakken og fase-1-/fase-2-rapportene er byteidentiske. derive-reference.py --check reproduserer fortsatt alle tre JSON-filene.
- Fase-2-kilde/tester/factory/UI er uendret utenom de tre nødvendige overlappsområdene: DbContext, resx og aktivt snapshot. De tidligere tilleggene i disse filene er bevart. Ved å fjerne bare fase-3-tilleggene i DbContext/resx fås eksakte startbytes; snapshot-diffen mot fase-2-start består bare av 132 nye linjer, ingen slettede/erstattede linjer.
- Totalt er ni eksisterende filer endret i denne fasen; øvrige eksisterende 674 filer er urørt. Filoversikten nedenfor skiller disse endringene fra tidligere ucommittet arbeid.

## 8. Kjørte kontroller og faktiske resultater

SDK: 10.0.302. Tester er selvstendige konsollprogrammer, ikke testprosjekter som automatisk kjøres av solution-bygg. Entry points ble kontrollert før kjøring.

| Faktisk kommando | Resultat |
|---|---|
| `dotnet build TenantPlatform.sln --no-restore` | PASS, siste kjøring 0 feil / 0 advarsler; fire solution-prosjekter bygget. |
| `bash scripts/add-migration.sh AddAgreementDimensions` | PASS; kilde/Designer/snapshot generert offline, ingen databasekjøring. |
| `dotnet restore tests/TenantPlatform.AgreementDimensions.SmokeTests/TenantPlatform.AgreementDimensions.SmokeTests.csproj --source /Users/morten/.nuget/packages -p:NuGetAudit=false` | PASS; ny testinngang restaurert fra lokal cache uten produksjonsavhengighetsendring. |
| `dotnet build tests/TenantPlatform.AgreementDimensions.SmokeTests/TenantPlatform.AgreementDimensions.SmokeTests.csproj --no-restore` | PASS, 0 feil / 0 advarsler ved siste kjøring. |
| `dotnet run --project tests/TenantPlatform.AgreementDimensions.SmokeTests --no-build --no-restore` | PASS, **111 faktisk kjørte kontroller**. |
| `dotnet build tests/TenantPlatform.Dimensions.SmokeTests/TenantPlatform.Dimensions.SmokeTests.csproj --no-restore` | PASS, 0 feil / 0 advarsler. Fase-2-testkilden er uendret. |
| `dotnet run --project tests/TenantPlatform.Dimensions.SmokeTests --no-build --no-restore` | PASS, **91 faktisk kjørte kontroller**. |
| `dotnet build tests/TenantPlatform.Dashboard.SmokeTests/TenantPlatform.Dashboard.SmokeTests.csproj --no-restore` | PASS, eksisterende cache-/pakkeadvarsler. |
| `env -u DASHBOARD_PREVIEW_PATH dotnet run --project tests/TenantPlatform.Dashboard.SmokeTests --no-build --no-restore` | PASS, faktisk kjørt til Dashboard smoke checks passed. |
| `dotnet build tests/TenantPlatform.AgreementAnalysis.SmokeTests/TenantPlatform.AgreementAnalysis.SmokeTests.csproj --no-restore` | Først FAIL pga. gammel restore-cache; etter lokal restore PASS, 0 feil / 0 advarsler. Programmet er ikke kjørt. |
| `dotnet restore tests/TenantPlatform.AgreementAnalysis.SmokeTests/TenantPlatform.AgreementAnalysis.SmokeTests.csproj --source /Users/morten/.nuget/packages -p:NuGetAudit=false` | PASS; gjenoppbygde kun lokale restore-artefakter, ingen kilde-/prosjektendring. |
| `python3 docs/features/unified-dimensions-phase-1/derive-reference.py --check` | PASS; alle tre JSON-filer byteidentiske. |
| Python SHA-256-/snapshot-/resx-kontroller og `git diff --check` | PASS; tidligere arbeid bevart, 86 historiske filer uendret, ni nye feilmeldinger finnes nøyaktig én gang i alle tre språk, ingen whitespace-feil. |

Alle **tolv** testprosjekter ble også eksplisitt bygget med følgende kommandofamilie, sekvensielt per faktisk prosjektfil:

```sh
dotnet build tests/<Prosjekt>/<Prosjekt>.csproj --no-restore
```

Kompilering er ikke testkjøring. Ni PostgreSQL-bundne programmer (AccountRegisters, AgreementAnalysis, AgreementFollowup, AgreementNotice, AgreementPeriods, AgreementProcessing, Agreements, Leasing og MeetingRooms) er kun kompilert. De er ikke startet, og ingen databasevariabler er konfigurert. Den konkrete prosjektmatrisen følger nedenfor.

### Testdekning og begrensninger

Ny entrypoint er `tests/TenantPlatform.AgreementDimensions.SmokeTests`, dokumentert i dens README. Den bruker isolert EF InMemory, produksjonens autorisasjonstjeneste/audit-interceptor og fast tid. Offentlige lese-/options-/historikk-/admingrenser kjøres direkte. Gyldige writes kaller de private produksjonskjernene CreateAgreementCoreAsync, UpdateAgreementCoreAsync og SaveAgreementDimensionRuleCoreAsync via reflection, med faktisk SaveAsync/SaveChanges. De beholder autorisasjon, arkiv/eier, revisjon, øvrig avtalevalidering, tildelingsvalidering, historikk og audit. Bare den ytre relasjonelle transaksjonen og kontorad-/avtaleradlåsene hoppes over i testharnesset. Produksjonen har ingen provider-spesifikk bypass.

Kontrollene dekker konfigurasjon, fravær/empty/identiske patcher, create-required, ugyldig required-konfigurasjon, single/multiple/leaf/forelder, manipulerte ID-er, duplikater, inaktive forfedre, historiske sett + nye tillegg, nye required-regler på eldre avtaler, endring i én dimensjon mens en annen mangler required, read/edit/admin, deaktivert bruker/fjernet medlemskap, arkiv, foreldertoken/revisjonskonflikter, uforandret økonomisk versjonshistorikk, historikksnapshots, lokale navn og stier. Npgsql metadata kontrollerer keys/FK/index/check/revision og additiv migrasjon uten tilkobling.

Dette beviser **ikke** PostgreSQL-persistens, constraint-eksekvering, transaksjoner, rollback, låsing eller faktisk PostgreSQL-concurrency. Den nye InMemory-suiten tester både gamle innsendte revisjoner og en separat allerede sporet stale writer, men det er ikke en PostgreSQL-samtidighetstest. Analyseflytens relasjonelle draft-lock/document-promotion er bare kildeintegrert og kompilert, ikke kjørt. Dashboard beholder sin tidligere ToQueryString-kontroll uten connection-open; øvrig kjøring er InMemory/HtmlRenderer. Ingen browser- eller UI-arbeidsflyt er startet.

Under utviklingen ble fire nye nullable-advarsler fjernet og en manglende using i den nye testfilen rettet. En test-fixture ble korrigert: den første patchen i en batch skulle være gyldig, men forsøkte først ny foreldretildeling under LeafOnly og traff derfor riktig en tidligere valideringsfeil. Siste 111-kontrollkjøring passerer. En tidlig run uten ferdig testbinær startet ikke tester; det er ikke regnet som passert kjøring.

Eksisterende lokale problemer: NU1902/NU1903 om System.Security.Cryptography.Pkcs 10.0.0 og MSB9008 om tidligere Application-prosjekt i flere eldre testprosjekters restore-cache. Analysis-cachen manglet også Infrastructure-reference og ga 10 kompileringsfeil. Den ble restaurert fra allerede tilgjengelige pakker og bygger nå uten kildeendring. Andre prosjekters eksisterende advarsler er beholdt; ingen produksjonspakke eller kilde ble endret for å rydde dem. NuGetAudit=false gjaldt kun de to offline restore-kommandoene, ikke prosjekt-/konfigurasjonsfiler.

## 9. Begrensninger og overlevering til fase 4

Ingen kjent gjenstående funksjonell kildeoppgave innen fase 3. Gjeldende skjema trenger den nye migrasjonen før nye spørringer kan brukes mot en faktisk database; den er ikke anvendt her. Etterfølgende databaseverifikasjon må bruke separat autorisert runtime-context/testmiljø, ikke svekke den eksisterende offline-factoryens sperre.

Fase 4 skal bruke kontraktene i avsnitt 6 til Bruk i Avtaler-fanen og generisk avtale-UI. Den må sende bare faktisk endrede dimensjonspatcher, vise historiske ikke-valgbare valg, beholde input ved feil, bruke serverens CanEdit/Revision og holde read/details adskilt fra edit. Regelhistorikkens nye Agreements-spesifikke JSON-felter kan få egne lokaliserte etiketter i UI-fasen. Ingen slik Razor-endring er gjort nå.

Legacy og generiske felt lever parallelt uten synkronisering i denne overgangsfasen; dette er ikke sluttproduktet. Demo-/dashboard-/legacy-konvertering hører fortsatt til senere faser. Det gamle UI-et kan ikke velge generiske required-verdier før fase 4; hvis administrator aktiverer nye required-regler via tjenesten nå, vil gammel create-UI korrekt få required-feil. Fase 4 er ikke påbegynt.

- **PostgreSQL-migrasjonskjøring: NOT PERFORMED — POST_RUN_VERIFICATION**
- **Opprettelse av ny database og databasebasert demo-initialisering: NOT PERFORMED — POST_RUN_VERIFICATION**
- **PostgreSQL-basert persistens, constraints, transaksjoner og concurrency: NOT PERFORMED — POST_RUN_VERIFICATION**

## 10. Full prosjekt- og filoversikt

Alle prosjektbygg nedenfor er endelig PASS. Kun de tre merkede programmene ble kjørt. Analysis har et separat vellykket retry-bygg etter lokal restore.

| Faktisk byggekommando | Resultat / kjøring |
|---|---|
| `dotnet build tests/TenantPlatform.AccountRegisters.SmokeTests/TenantPlatform.AccountRegisters.SmokeTests.csproj --no-restore` | PASS; kun kompilert |
| `dotnet build tests/TenantPlatform.AgreementAnalysis.SmokeTests/TenantPlatform.AgreementAnalysis.SmokeTests.csproj --no-restore` | PASS; kun kompilert |
| `dotnet build tests/TenantPlatform.AgreementDimensions.SmokeTests/TenantPlatform.AgreementDimensions.SmokeTests.csproj --no-restore` | PASS; faktisk kjørt |
| `dotnet build tests/TenantPlatform.AgreementFollowup.SmokeTests/TenantPlatform.AgreementFollowup.SmokeTests.csproj --no-restore` | PASS; kun kompilert |
| `dotnet build tests/TenantPlatform.AgreementNotice.SmokeTests/TenantPlatform.AgreementNotice.SmokeTests.csproj --no-restore` | PASS; kun kompilert |
| `dotnet build tests/TenantPlatform.AgreementPeriods.SmokeTests/TenantPlatform.AgreementPeriods.SmokeTests.csproj --no-restore` | PASS; kun kompilert |
| `dotnet build tests/TenantPlatform.AgreementProcessing.SmokeTests/TenantPlatform.AgreementProcessing.SmokeTests.csproj --no-restore` | PASS; kun kompilert |
| `dotnet build tests/TenantPlatform.Agreements.SmokeTests/TenantPlatform.Agreements.SmokeTests.csproj --no-restore` | PASS; kun kompilert |
| `dotnet build tests/TenantPlatform.Dashboard.SmokeTests/TenantPlatform.Dashboard.SmokeTests.csproj --no-restore` | PASS; faktisk kjørt |
| `dotnet build tests/TenantPlatform.Dimensions.SmokeTests/TenantPlatform.Dimensions.SmokeTests.csproj --no-restore` | PASS; faktisk kjørt |
| `dotnet build tests/TenantPlatform.Leasing.SmokeTests/TenantPlatform.Leasing.SmokeTests.csproj --no-restore` | PASS; kun kompilert |
| `dotnet build tests/TenantPlatform.MeetingRooms.SmokeTests/TenantPlatform.MeetingRooms.SmokeTests.csproj --no-restore` | PASS; kun kompilert |

Filoversikten gjelder bare endringer i fase 3, sammenlignet med fase-3-start. Tidligere ucommittet arbeid er bevart.

Endret (9):

- `src/TenantPlatform.Infrastructure/Persistence/Migrations/TenantPlatformDbContextModelSnapshot.cs`
- `src/TenantPlatform.Infrastructure/Persistence/TenantPlatformDbContext.cs`
- `src/TenantPlatform.Web/Resources/TenantPlatformResources.en-GB.resx`
- `src/TenantPlatform.Web/Resources/TenantPlatformResources.nb-NO.resx`
- `src/TenantPlatform.Web/Resources/TenantPlatformResources.sv-SE.resx`
- `src/TenantPlatform.Web/Services/Agreements/AgreementDtos.cs`
- `src/TenantPlatform.Web/Services/Agreements/AgreementService.Analysis.cs`
- `src/TenantPlatform.Web/Services/Agreements/AgreementService.cs`
- `src/TenantPlatform.Web/Services/Agreements/IAgreementService.cs`

Ny (15):

- `docs/features/unified-dimensions-phase-3.md`
- `src/TenantPlatform.Core/Agreements/AgreementDimensions.cs`
- `src/TenantPlatform.Infrastructure/Persistence/Configurations/AgreementDimensionConfiguration.cs`
- `src/TenantPlatform.Infrastructure/Persistence/Migrations/20261112091606_AddAgreementDimensions.Designer.cs`
- `src/TenantPlatform.Infrastructure/Persistence/Migrations/20261112091606_AddAgreementDimensions.cs`
- `src/TenantPlatform.Web/Services/Agreements/AgreementDimensionDtos.cs`
- `src/TenantPlatform.Web/Services/Agreements/AgreementService.Dimensions.cs`
- `tests/TenantPlatform.AgreementDimensions.SmokeTests/AccessChecks.cs`
- `tests/TenantPlatform.AgreementDimensions.SmokeTests/AssignmentChecks.cs`
- `tests/TenantPlatform.AgreementDimensions.SmokeTests/ConfigurationChecks.cs`
- `tests/TenantPlatform.AgreementDimensions.SmokeTests/Fixture.cs`
- `tests/TenantPlatform.AgreementDimensions.SmokeTests/MetadataChecks.cs`
- `tests/TenantPlatform.AgreementDimensions.SmokeTests/Program.cs`
- `tests/TenantPlatform.AgreementDimensions.SmokeTests/README.md`
- `tests/TenantPlatform.AgreementDimensions.SmokeTests/TenantPlatform.AgreementDimensions.SmokeTests.csproj`
