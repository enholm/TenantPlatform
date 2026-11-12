# Unified Dimensions — fase 4: administrasjon og avtalegrensesnitt

**Status: FERDIG for fase 4.** «Bruk i Avtaler», generiske dimensjonsvalg i ordinære skjemaer og analysegodkjenning, detaljvisning og historikk er implementert. Databasefrie tester og bygg passerer. Nettleser-/layoutkontroll og PostgreSQL-verifikasjon er ikke utført. Fase 5 er ikke startet.

## 1. Arbeidsgrunnlag og bevaring

- Arbeidskatalog: `/Users/morten/Dev/TenantPlatform`.
- Branch: `main`.
- Startcommit: `f2f2d67338fd05e59624423aad675ec4f8ca415d`.
- Kravgrunnlag: `unified-dimensions.md`, fase-1-, fase-2- og fase-3-rapportene, brukerens fase-4-prompt, innlimte AGENTS.md-instruksjoner og repoets `Agents.md`. Faktiske fase-3-kontrakter og tilsvarende Dimensions-/Leasing-komponenter ble kontrollert før integrasjonen.
- Checkouten inneholdt godkjent, ikke innsendt fase-1–3-arbeid. SHA-256 for 698 eksisterende sporede/usporede filer ble registrert ved fasestart i `/tmp/tenantplatform-phase4-start-sha256.json` (filens SHA-256: `fb5778842113dc3c131149808b0c0d73283bee5733c90d4ecd6ceaf39ade6347`). Filoversikten nedenfor gjelder bare fase 4, ikke samlet diff mot HEAD.
- Alle 89 eksisterende filer i migrasjonsmappen, inkludert gjeldende snapshot og fase-2-/fase-3-migrasjonene med Designer-filer, er byteidentiske med fasestart. Det er ingen nye migrasjonsfiler eller konkret avdekket skjemaavvik. Snapshot-endringen som fortsatt vises i samlet `git diff`, stammer fra tidligere faser.
- De fem fase-1-filene, inkludert rapport og referansepakke, samt fase-2-/fase-3-rapportene er byteuendret. Øvrig tidligere arbeid er bevart; bare de oppførte overlappende filene er videreutviklet.
- Ingen databaseforbindelse, SQL-eksekvering, migrasjonskjøring, demo-initialisering, vanlig Web-oppstart, AgentOrchestrator, rettighetsutvidelse, branchbytte, reset, commit, merge, push eller deploy. Ingen Core-/Infrastructure-/skjemaendringer i denne fasen. Ingen endring i autentisering, autorisasjonsarkitektur, Leasing-semantikk eller produksjonsavhengigheter.

## 2. Nye og endrede filer

Alle stier er relative til repo-roten. **Endret** betyr endret fra fasestart, også når filen var usporet ved start.

| Status | Fil |
|---|---|
| Ny | `src/TenantPlatform.Web/Components/Pages/Agreements/AgreementDimensionEditState.cs` |
| Ny | `src/TenantPlatform.Web/Components/Pages/Agreements/AgreementDimensionChoices.razor` |
| Ny | `src/TenantPlatform.Web/Components/Pages/Agreements/AgreementDimensionsView.razor` |
| Ny | `src/TenantPlatform.Web/Components/Pages/Agreements/AgreementDimensionSnapshotView.razor` |
| Ny | `src/TenantPlatform.Web/Components/Pages/Dimensions/AgreementDimensionRulesSection.razor` |
| Endret | `src/TenantPlatform.Web/Components/Pages/Dimensions/DimensionsPage.razor` |
| Endret | `src/TenantPlatform.Web/Components/Pages/Dimensions/DimensionHistoryView.razor` |
| Endret | `src/TenantPlatform.Web/Components/Pages/Agreements/AgreementEdit.razor` |
| Endret | `src/TenantPlatform.Web/Components/Pages/Agreements/AgreementFields.razor` |
| Endret | `src/TenantPlatform.Web/Components/Pages/Agreements/AgreementDetails.razor` |
| Endret | `src/TenantPlatform.Web/Components/Pages/Agreements/AgreementAnalysisDialog.razor` |
| Endret | `src/TenantPlatform.Web/Services/Agreements/AgreementAnalysisDtos.cs` |
| Endret | `src/TenantPlatform.Web/Services/Agreements/AgreementService.Analysis.cs` |
| Endret | `src/TenantPlatform.Web/Resources/TenantPlatformResources.nb-NO.resx` |
| Endret | `src/TenantPlatform.Web/Resources/TenantPlatformResources.en-GB.resx` |
| Endret | `src/TenantPlatform.Web/Resources/TenantPlatformResources.sv-SE.resx` |
| Ny | `tests/TenantPlatform.AgreementDimensions.SmokeTests/UiChecks.cs` |
| Ny | `tests/TenantPlatform.AgreementDimensions.SmokeTests/UiHarness.cs` |
| Endret | `tests/TenantPlatform.AgreementDimensions.SmokeTests/Program.cs` |
| Endret | `tests/TenantPlatform.Leasing.SmokeTests/LeasingComponentChecks.cs` |
| Endret | `tests/TenantPlatform.Leasing.SmokeTests/Program.cs` |
| Ny | `docs/features/unified-dimensions-phase-4.md` |

Leasing-testens to endringer registrerer bare den nye `IAgreementService`-avhengigheten ved rendering av DimensionsPage. Det databasebundne testprogrammet er kompilert, ikke kjørt.

## 3. Bruk i Avtaler og regelhistorikk

DimensionsPage har en egen «Bruk i Avtaler»-fane ved siden av Leasing. Den leser `GetAgreementDimensionRulesAsync` og lagrer en kopi av lokal regel med `SaveAgreementDimensionRuleAsync`. Lagret revisjon følger input. På vellykket lagring lastes registeret og den lagrede regelen på nytt. Ved valideringsfeil/konflikt beholdes input og revisjon, feilen vises, og det kjøres ingen automatisk reload eller nytt lagringsforsøk.

Komponenten har bare «Tilgjengelig» og «Obligatorisk ved registrering». Required=true setter også tilgjengelig=true. Tilgjengelig=false setter også required=false. Serverens fase-3-normalisering er uendret. AllowMultiple og LeafOnly tilhører fortsatt den globale dimensjonen. Leasing bruker sin eksisterende regel og redigeringstilstand; Agreements-lagring endrer dem ikke. Fase-2-editoren og lokaliserte registernavn/beskrivelser er beholdt.

DimensionHistoryView gjenkjenner `Module: Agreements` og viser lokaliserte etiketter for `IsEnabledInAgreements` og `IsRequiredAtRegistration`. Eksisterende Dimensions-, oversettelses- og Leasing-felter tolkes som før.

## 4. Komponent og patchsemantikk

AgreementDimensionChoices bruker katalogens lokaliserte navn/beskrivelser, required, stier, AllowMultiple og Selectable. Enkeltvalg bruker native select; flervalg bruker merkede native checkbokser. Foreldre kan velges når serveren gjør dem valgbare. LeafOnly eller inaktive forfedre gir ingen konkurrerende klientberegning: Selectable fra serveren er autoritativt for nye valg.

AgreementDimensionEditState kopierer katalogen og holder to separate sett per dimensjon: opprinnelige eksplisitte ID-er og lokale valg. Ingen UI-binding muterer originalkatalogen. Avtalens opprinnelige revisjon følger tilstanden, og ApplyTo avviser et annet modellrevisjonsnummer.

| Original → lokale valg | `DimensionChanges` |
|---|---|
| D1={A}, uendret | `null`; D1 utelates |
| D1={A} → {B} | Bare D1 med `[B]` |
| D1={A} → {} | Bare D1 med `[]` |
| D1={A} → {B} → {A} | `null`; D1 utelates |
| D2={A,B} → {B,A} | `null`; sett sammenlignes uten rekkefølge |
| Gammel avtale mangler ny required D3; bare tittel endres | Ingen D3-patch; serveren tillater urelatert edit |

Null/tom ytre patch betyr aldri tømming. Ingen automatiske required-valg, arv, legacy-synkronisering eller endring under rendering er innført. Required markeres med tekst og veiledning, uten generell klientvalidering som sperrer eldre avtaler. Serveren avgjør om en faktisk endring kan lagres.

Historiske valgte verdier vises med tekstlig status og kan beholdes eller fjernes, også når dimensjonen er utilgjengelig. Hele kontrollen deaktiveres ikke. Fjernet historisk ID kan ikke legges tilbake hvis Selectable=false. Ved senere innlasting av en katalog som tillater verdien, gjelder vanlig nyvalgadgang. Dersom en global endring til enkeltvalg møter flere gamle tildelinger, beholdes alle frem til eksplisitt endring; UI forklarer dette og tilbyr individuell fjerning. Rendering og kulturskifte endrer ingen tildelinger.

## 5. Ordinære og alternative flyter

Søk i hele `src/TenantPlatform.Web/Components` etter CreateAsync, UpdateAsync og ApproveAnalysisAsync, samt kontroll av avtalemodulens injiserte tjenester, fant disse avtalekallene:

| UI-sted | Kall | Integrasjon |
|---|---|---|
| AgreementEdit.SaveAsync, ny avtale | `IAgreementService.CreateAsync` | Katalog fra GetOptionsAsync; samme delte komponent; ApplyTo før kall |
| AgreementEdit.SaveAsync, eksisterende | `IAgreementService.UpdateAsync` | Opprinnelige sett/revisjon fra samme AgreementDetailsDto; ApplyTo før kall |
| AgreementAnalysisDialog.ApproveAsync, ny avtale | `IAgreementAnalysisService.ApproveAnalysisAsync` | Opprettelseskatalog fra GetOptionsAsync; dimensjonskomponent i AgreementFields |
| Samme dialog, eksisterende avtale | Samme metode | Autorisert edit-katalog fra GetAgreementDimensionOptionsAsync; samme komponent separat fra øvrige avtalefelter |

Ingen andre UI-baserte opprettelses-/oppdateringsstier for avtaler ble funnet. Andre modulers CreateAsync/UpdateAsync er ikke avtalestier. Eksisterende innganger til analysedialogen bruker den samme integrasjonen.

AgreementEdit laster ikke samme konto/avtale på nytt ved parameter-rerender. Save-feil beholder dimensjoner, andre modellfelter og opprinnelig revisjon. Først vellykket lagring navigerer videre. De dedikerte legacy-kontrollene er fjernet fra AgreementFields; DTO-feltene GeographicAreaId og OrganizationElementId blir fortsatt med rundturen ved redigering. De nullstilles ikke. Motpart/Organization, eier, bygg/enhet, økonomi, indeks, oppsigelse, livssyklus, dokumenter og tilgang beholder eksisterende flyt.

Analyseutkastet har allerede ExpectedAgreementRevision i fase-3-modellen. Det eksponeres nå som en valgfri, bakoverkompatibel `AnalysisDraftDto.AgreementRevision`; ingen skjemaendring. Ved eksisterende avtale må edit-katalogens revisjon være lik utkastets. Ulikhet gir konflikt før dimensjonsredigering, og godkjenning blokkeres uten en sammenhengende tilstand. Ved godkjenningsfeil beholdes draft, valgte dimensjoner, motpartsdata, bekreftelse og øvrige felter. Motpartsmatching kan oppdatere sine opsjoner, men erstatter ikke dimensjonstilstanden eller revisjonen. Dokument-/analysebehandling og serverens eksisterende revisjonskontroll er ellers uendret.

## 6. Detaljvisning, tilgang og historikk

AgreementDetails bruker Dimensions og DimensionHistory fra den allerede autoriserte detaljkontrakten. Den nye komponenten viser bare Values med IsAssigned, gruppert under lokaliserte dimensjonsnavn. Stier viser plassering; forfedre opprettes ikke som ekstra eksplisitte valg. Historiske tildelinger merkes med tekst.

Historikken viser aktør, tidspunkt med UTC, før/etter og uendrede snapshot-navn/-koder/-stier. Forklaringsteksten sier at navnene er registrert ved endringstidspunktet og kan avvike fra dagens register. Ingen oppslag omskriver historiske etiketter. Eksisterende analyse-, oppsigelses-, oppfølgings- og økonomihistorikk er bevart.

Reader uten edit-/adminrett får dimensjoner/historikk fra GetAsync. Ingen ny register-/edit-forespørsel er nødvendig for dette. Eksisterende CanEdit/CanManageAccess og serverkontroller styrer handlingene som før. Legacy-klassifiseringsradene er fjernet fra oversikten, mens faktisk motpart, bygg/enhet og øvrige data vises videre.

## 7. Lokalisering og tilgjengelighet

14 nye ressursnøkler er lagt til i hver av nb-NO, en-GB og sv-SE. Alle 21 ressursnøkler som de nye komponentene bruker, inkludert gjenbrukte nøkler, er kontrollert for nøyaktig én forekomst og ikke-tom verdi i alle tre filer. Navn/beskrivelser kommer fra fase-3-katalogen med fase-2-fallback; dimensjons-/verdi-ID-er endres ikke med språk.

Native kontroller har labels, fieldset/legend, tilgjengelige navn for fjerningsknapper og eksplisitt historisk/required-tekst. Hjelpetekst er knyttet med aria-describedby, manglende verdi bruker role=status, feil følger eksisterende alert-mønster. Status krever ikke fargeforståelse. Ingen ny fokusstyring eller frontendavhengighet er innført. Faktisk fokus-, tastatur- og skjermleseroppførsel er ikke bekreftet i nettleser.

## 8. Verifikasjon og faktiske resultater

| Kommando/kontroll | Resultat |
|---|---|
| `dotnet build TenantPlatform.sln --no-restore` | PASS; fire prosjekter, 0 feil, 0 advarsler |
| `dotnet build <prosjektsti> --no-restore` for alle 12 `tests/*/*.csproj` | PASS; alle 12 exit 0, ingen kompileringsfeil |
| `dotnet run --project tests/TenantPlatform.Dimensions.SmokeTests --no-restore` | PASS; 91 kontroller |
| `dotnet run --project tests/TenantPlatform.AgreementDimensions.SmokeTests --no-restore` | PASS; 166 kontroller: 111 tidligere kontroller + 55 nye UI-/tilstandskontroller |
| `dotnet run --project tests/TenantPlatform.Dashboard.SmokeTests --no-restore` | PASS; 49 PASS-linjer, databasefri suite |
| `python3 docs/features/unified-dimensions-phase-1/derive-reference.py --check` | PASS; tre JSON-filer byteidentiske; 36 geografiverdier, 30 organisasjonsverdier, 300 avtalekoblinger, 600 finanslinjer |
| Python SHA-256-sammenligning mot fasestart | PASS; 89 migrasjonsfiler inkl. snapshot, fem fase-1-filer og fase-2-/fase-3-rapporter uendret; ingen slettede startfiler |
| XML-/ressurskontroll i Python | PASS; nye komponenters 21 brukte nøkler finnes nøyaktig én gang i hvert språk |
| `git diff --check` | PASS |
| UI-kallinventar, gjennomgang av bindings-/autorisering-/legacy-kode og isolert preview-mulighet | Utført som statisk kontroll |
| Vanlig Web-oppstart, HTTP-/browserinteraksjon, layout, tastatur og skjermleser | IKKE UTFØRT |

De eksplisitte testbyggene gjelder AccountRegisters, AgreementAnalysis, AgreementDimensions, AgreementFollowup, AgreementNotice, AgreementPeriods, AgreementProcessing, Agreements, Dashboard, Dimensions, Leasing og MeetingRooms. De ni databasebundne programmene er bare kompilert, aldri kjørt. Enkelte eldre testprosjekters eksisterende restore-cache gir MSB9008 om et gammelt Application-prosjekt og NU1902/NU1903 om System.Security.Cryptography.Pkcs 10.0.0. Disse eksisterende advarslene er ikke endret eller undertrykt. De nye AgreementDimensions-kontrollene bygger uten advarsler.

Lokale logger: `/tmp/tenantplatform-phase4-build.log`, `tenantplatform-phase4-test-builds.log`, `tenantplatform-phase4-dimensions.log`, `tenantplatform-phase4-ui-run.log` og `tenantplatform-phase4-dashboard.log` i `/tmp/`. Dette er lokale kjøringslogger, ikke nye repository-avhengigheter.

### Hva testene faktisk beviser

- **Rendering:** HtmlRenderer/StaticHtmlRenderer rendrer DimensionsPage med Agreements-fanen, regelkomponenten, generiske valg, AgreementEdit for create/edit, analysemodal for ny/eksisterende avtale, full AgreementDetails for reader, dimensjonsdetaljer og gammel/ny registerhistorikk. Språktester rendrer katalogens lokaliserte navn/beskrivelser og stabile ID-er for alle tre språk.
- **Kalte handlinger:** Testene kaller komponentenes faktiske handler-metoder via refleksjon på rendererens dispatcher, inkludert required/available-kobling, regelens Save-callback, side-lagring, single/multiple/endring/tømming, Open/Analyze/Match/Approve og rerender. Payload, kallantall, lokale felter, feil og revisjoner kontrolleres før/etter. Settsammenligning, tilbakeføring, deep copy og historisk fjerning kontrolleres også direkte.
- **Tjenestebinding:** Autoriserte katalog-/detaljlesinger bruker produksjonstjenestene med EF InMemory. Ordinær create/update og vellykket regellagring går via de samme produksjonskjernene som fase-3-testene, med testadaptasjon rundt de relasjonelle wrapperne. Konflikt ved UI-update/regellagring er injisert. Analysetjenestens svar/godkjenning og uvedkommende finans-/oppfølgingspaneler er stubs; godkjenningspayload og feilbevaring er verifisert, ikke PostgreSQL-transaksjonen eller dokumentpersistensen.
- **Statisk:** Razor event-bindingene, tilgjengelige labels, beholdte felt, kallinventaret og uendret serverautorisasjon er gjennomgått. Refleksjonskall er ikke DOM-hendelser; det er ikke kjørt automatiserte klikk eller native nettleservalidering.
- **Ingen browserpåstand:** Testene er ikke bevis på layout, browserinteraksjon eller tastaturbruk. Eksisterende Leasing UiPreview skriver bare opt-in HTML-snapshots; ingen etablert separat Agreements-preview med browser ble startet. Vanlig app ble ikke startet for visuell kontroll.

## 9. Begrensninger og senere sjekkliste

Ingen kjente uferdige implementasjonspunkter innen fase 4. Følgende verifikasjon gjenstår utenfor denne databasefrie kjøringen:

1. I separat godkjent testmiljø: åpne DimensionsPage i de tre språkene og lys/mørk visning; kontroller responsive faner, labels, tabrekkefølge, fokus og required/available-kobling med tastatur.
2. Registrer/rediger avtale med single/multiple, valgbare foreldre og historiske verdier. Kontroller at bevaring, eksplisitt tømming og tilbakeføring oppleves riktig og at historiske valg ikke kan legges tilbake etter fjerning.
3. Utløs ekte revisjonskonflikt med to sesjoner i ordinært skjema, adminregel og analyse. Kontroller at feil, lokale felter og dimensjoner beholdes, og at ingen automatisk retry skjer.
4. Gjenta analyseopprettelse og analyse av eksisterende avtale med dokumenter, motpartsmatching, manglende required-verdi og feil. Kontroller at godkjenning og avbrudd bevarer eksisterende dokumentflyt.
5. Åpne detaljene som reader uten edit-/adminrett. Kontroller historiske etiketter, før/etter og lokaliserte nåværende dimensjonsnavn; kontroller at ingen ekstra forfedre presenteres som tildelt.
6. Utfør PostgreSQL-portene under separat autorisasjon. Offline-kontroller bekrefter ikke låsing, constraints, atomisitet eller faktisk persistens.

## 10. Overlevering til fase 5

Fase 4 etterlater fase-3-lese-/skrivekontraktene og migrasjonskilden intakt. Ny UI-tilstand beregner endringspatcher, mens tjenestene fortsatt eier validering og tilgang. Analyseutkastets allerede lagrede revisjon er nå tilgjengelig i DTO-en. Fase 5 skal startes med en ny manuell prompt og sitt avgrensede omfang. Det er ikke implementert nytt dashboard, demokonvertering eller legacy-fjerning; disse hører til senere faser. Den frosne fase-1-pakken er fortsatt reproducerbar.

- PostgreSQL-migrasjonskjøring: **NOT PERFORMED — POST_RUN_VERIFICATION**
- Opprettelse av ny database og databasebasert demo-initialisering: **NOT PERFORMED — POST_RUN_VERIFICATION**
- PostgreSQL-basert persistens, constraints, transaksjoner og concurrency: **NOT PERFORMED — POST_RUN_VERIFICATION**
