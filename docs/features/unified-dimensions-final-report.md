# Unified Dimensions — konsolidert sluttrapport

**Status: IMPLEMENTATION COMPLETE — godkjent av bruker med dokumenterte verifikasjonsbegrensninger.** Kildeimplementasjonen er ferdig. Fase 8 dokumenterer den historiske databasefrie sluttkontrollen; senere faktisk PostgreSQL- og UI-verifikasjon er konsolidert i §13 og detaljert i [fase 9](unified-dimensions-phase-9.md). Ikke alle akseptansekriterier er fullt operativt verifisert.

Dato: 12. november 2026. Arbeidskatalog: `/Users/morten/Dev/TenantPlatform`. Branch: `main`. Opprinnelig kildecommit og fase-8-startcommit: `f2f2d67338fd05e59624423aad675ec4f8ca415d`.

Grunnlag: [originalspesifikasjonen](unified-dimensions.md), brukerens manuelle fase-1–8-prompter, rapportene [fase 1](unified-dimensions-phase-1.md), [2](unified-dimensions-phase-2.md), [3](unified-dimensions-phase-3.md), [4](unified-dimensions-phase-4.md), [5](unified-dimensions-phase-5.md), [6](unified-dimensions-phase-6.md), [7](unified-dimensions-phase-7.md), den frosne fase-1-pakken og fase-7-auditen samt repoets `Agents.md` og innlimte AGENTS.md-instruksjoner. AgentOrchestrator-kravene er **prosessmessig erstattet av manuelle faser**. AgentOrchestrator er verken startet, konfigurert eller hevdet implementert. Eksisterende checkout er brukt etter uttrykkelig instruksjon.

## 1. Endelig verifikasjonsstatus

| Kontroll | Faktisk status |
|---|---|
| PostgreSQL-migrasjonskjøring og sluttschema | PASS for brukerens gjenskapte appdatabase: 45 migrasjoner til `20261112104542_RemoveLegacyAgreementClassifications` og de dokumenterte schemakravene i fase 9. |
| Databasebasert demo-initialisering og eksport | PASS for faktisk kjørte, avgrensede Magida-kontroller; dokumenterte runtimeforskjeller og eksportgrenser beholdes. |
| Databaseidempotens | PASS for to konkrete scenarioer: uendret graf med 15 samlinger/3103 rader og redigert graf med 15 samlinger/3102 rader/599 tildelinger; hvert før-/etterpar var byteidentisk. Ikke generell initializer-verifikasjon. |
| PostgreSQL-regresjon | AccountRegisters PASS, 41 kontroller/exit 0; Agreements PASS, 78 kontroller/exit 0. Øvrige programmer: **NOT PERFORMED — bruker har valgt å avslutte videre verifikasjon.** |
| Dimensjonseditor i browser | PASS for brukerens avgrensede manuelle kontroll av faner, responsiv scrolling, handlinger, lagring/avbryt, validering/fallback og tastatur/fokus. Full browser-/tilgjengelighetsmatrise: **NOT PERFORMED — bruker har valgt å avslutte videre verifikasjon.** |

Ingen databaseforbindelser, SQL-eksekvering, migrasjonsanvendelse, initializer-kjøring, databaseopprettelse/reset, commit, branchbytte, merge, push eller deploy ble utført av agenten i fase 8. Senere fase-9-databasehandlinger ble utført manuelt av brukeren. Offline-fabrikken og forbindelsessperren er bevart. Ingen autentiserings-/autorisasjonsarkitektur eller Leasing-kostnadssemantikk er endret.

## 2. Funksjon og arkitektur

Dimensions er fortsatt den felles kontoavgrensede klassifiseringsmodellen. Core har domenet, Infrastructure har EF-mapping/migrasjoner/generering, og Web har applikasjonstjenester og Blazor. Ingen Application-prosjekt, repository, ny frontend eller konkurrerende klassifiseringsmodell er innført. Direkte EF via kortlivede `IDbContextFactory<TenantPlatformDbContext>`-kontekster følger eksisterende mønster.

`Dimension` beholder stabil kode, fallbacknavn/-beskrivelse, aktivitet, `AllowMultiple`, `LeafOnly`, GUID-revisjon og historikk. `DimensionTranslation` legger til navn/beskrivelse per konto, dimensjon og språk. Agreements eier en separat `AgreementDimensionRule` med tilgjengelig/obligatorisk og revisjon. Leasing eier fortsatt sine eksisterende tilgjengelig-/required-/allocation-regler. Globale utvalgsregler er ikke duplisert i modulreglene.

`AgreementDimensionAssignment` lagrer bare eksplisitt valgte verdi-ID-er. Unik indeks på `(AccountId, AgreementId, DimensionId, ValueId)` hindrer duplikater i modellen. Sammensatte FK-er binder avtale og verdi til riktig konto og verdi til riktig dimensjon. `AgreementDimensionHistory` lagrer aktør, tidspunkt og append-only før/etter-JSON for reelle endringer; historiske fallbacknavn, koder og stier omskrives ikke. `AgreementDimensionRule` har check-constraint for required ⇒ enabled og account-aware dimensjons-FK. Oversettelser har unik `(AccountId, DimensionId, LanguageCode)` og account-aware dimensjons-FK. Fase 9 kontrollerte sluttschema og enkelte relasjonelle avvisninger; full negativ constraintmatrise er ikke kjørt, se §13.

Faktisk `Organization`, motparter, adresser, land/post, bygg/enheter og øvrig domenemodell er bevart. Kontosletting har eksplisitte sperrer for oversettelser, dimensjonsverdier/historikk, Agreements-regler/tildelinger/historikk og Leasing-avhengigheter. Denne beslutningslogikken er testet med isolerte InMemory-grafer; virkelige Restrict-FK-er og sletting krever PostgreSQL.

## 3. Tildelingsregler, autorisasjon og samtidighet

Administrasjon bruker eksisterende medlemskontroll og `CanManageAccountStructure`. Opprettelse krever eksisterende create-rettighet. Lesing bruker avtaletilgang; en ordinær lesetilgang kan åpne detaljer uten edit-/registerrettighet. Oppdatering beholder edit/manage/owner-grensene. Kontoen verifiseres mot valgt konto og aktivt medlemskap; alle nye katalog-, regel-, verdi-, tildelings-, historikk- og dashboardlesinger er kontoavgrenset. Plattformadministrator gir ikke en ny generell omgåelse av konto-/medlemsgrensene.

`AgreementService.Dimensions.cs` validerer dimensjon/konto, verdi/konto/dimensjon, duplikate patcher og valg, aktivitet, tilgjengelighet, cardinalitet, leaf-only og required. Et aktivt barn kan fortsatt velges når en forfader er inaktiv; forfaderen er en sti, ikke en ny eksplisitt tildeling. En required-regel uten aktiv dimensjon eller valgbare verdier gir konfigurasjonsfeil ved create, fremfor å bli stille valgfri.

Patchsemantikken er eksplisitt:

| Input ved edit | Virkning |
|---|---|
| Dimensjon utelatt | Eksisterende sett bevares; en nylig required-regel sperrer ikke en urelatert endring |
| Tom verdiliste | Eksplisitt tømming; required valideres når settet faktisk endres |
| Samme sett, også endret rekkefølge | Ingen dimensjonsendring eller ny dimensjonshistorikk |
| Endret sett | Hele planen valideres før tildelinger og dimensjonshistorikk muteres |
| Historisk inaktiv/utilgjengelig verdi beholdes | Tillatt; den forsvinner ikke ved lagring |
| Nytt tillegg av inaktiv/utilgjengelig verdi | Avvist; en fjernet historisk verdi kan ikke gjenvelges som nytt tillegg |

Create validerer alle required-regler, også når klienten utelater dimensjonsinput. Foreldremedlemskap beregnes og persisteres aldri som ekstra tildelinger.

Offentlig create/update ligger i transaksjon og tar kontolås; update tar deretter avtalelås og kontrollerer revisjon. Regel-/globaldimensjons-/verdiendringer følger kontolås og eksisterende revisjonskonvensjoner. Analyse har sin eksisterende draft-lås, deretter konto og ved eksisterende avtale avtale/finanslås. Ingen ny motsatt konto→draft-låsesti ble funnet. Begge analysegodkjenningsgrener kaller samme tildelingsvalidering. Ny `ExpectedAgreementRevision` gjør at eksisterende analyse godkjennes mot den revisjonen utkastet ble åpnet med. Eventuell forhåndslagring av ny motpart ligger fortsatt inne i transaksjonen. Kildegjennomgang og InMemory/private kjernetester beviser ikke faktisk låsing, isolasjonsnivå, deadlockfrihet eller rollback; dette må verifiseres mot PostgreSQL.

## 4. Lokalisering og UI

`DimensionLocalization` bruker eksisterende `TranslationHelper` og støttede språk `nb-NO`, `en-GB`, `sv-SE`. Navn og beskrivelse løses **hver for seg**: forespurt språk → kontostandard når tilgjengelig → lagret fallback → kode for navn. Beskrivelse kan ende tom. En ny read-normalisering tvinger ikke engelsk som manglende kontostandard; eksisterende helperatferd ellers er bevart. Verdier har fortsatt sine eksisterende navn og stier, uten et nytt oversettelsessystem.

Dimensions-editoren redigerer oversettelser uten å endre ID-er. Tomt navn og tom beskrivelse sammen fjerner språkets rad ved eksplisitt patch; utelatte språk beholdes. Administrasjonen har «Bruk i Avtaler» etter Leasing-mønsteret. Required aktiverer tilgjengelig i UI; deaktivering nuller required i UI og server. Lagring har revisjonsvern og audit/historikk. Valideringsfeil og konflikt beholder input og gir ikke automatisk retry.

Create/edit bruker `AgreementDimensionChoices` og `AgreementDimensionEditState`: lokaliserte dimensjonsnavn, required-merking, hierarkistier, single/multiple, foreldervalg, historiske valg og bare reelle endringspatcher. Valideringsfeil bevarer valgene. Både ny avtale og eksisterende avtale i `AgreementAnalysisDialog` bruker samme katalog og tilstand, med kontroll av draft-/katalogrevisjon. Detaljer viser eksplisitte tildelinger og merket registrert historikk, også for lesebruker.

Leasing-katalogen laster kontoavgrensede oversettelser/kontostandard. Nåværende valg, detaljer og rapportfilter bruker lokaliserte dimensjonsetiketter. Historikk/rapportdata/eksport beholder lagrede snapshots; ny snapshot-skriving bruker fortsatt fallbacknavn. Kostnadskalkulatorer, allocation-regler, betaling/livssyklus og rapportberegning er ikke skrevet om.

Ressurskontrollen fant 40 nye feature-nøkler i alle tre språk, uten duplikater og med like formatplassholdere. 26 foreldede nøkler er fjernet konsistent. `AccountHierarchyAddChild` er bevisst beholdt fordi generisk `DimensionValuesSection` bruker den. HtmlRenderer sjekker tekst/markup, tester kaller utvalgte komponenthandlinger direkte, og bindings-/labelgjennomgang er statisk. Dette dekker ikke faktisk DOM-eventkobling, fokus, skjermleser, tastatur, responsive mål eller lys/mørk layout. Ingen preview/browser ble startet; Dashboard-previewfilskriving var deaktivert.

## 5. Dashboard

De fire faste grupperingene Avtaletype, Avtalepartner, Status og Avtaleansvarlig beholdes. Aktive tilgjengelige dimensjoner kommer fra en kontoavgrenset katalog med stabile ID-er og lokaliserte navn. GEO/DEPT er ordinære dimensjoner.

Produksjonsqueryen bruker bare brukerens tilgjengelige, ikke-arkiverte avtaler og bevarer eksisterende statusomfang og filtre. Dynamisk gruppering projiserer avtale-ID og nullable eksplisitt verdi gjennom en avgrenset left join; ingen nye økonomisummer, fullstendige avtalegrafer eller én query per node. `AgreementInsightHierarchy.Aggregate` validerer treet, dedupliserer avtale/verdi-par og bruker separate ID-sett for direkte og inklusive medlemskap. Et barn ruller opp gjennom alle forfedre; søsken og parent+child teller én gang ved felles forfader. Foreldre arver ikke medlemskap ned til etterkommere.

Toppnivå viser røtter og «Ikke angitt». Drill viser umiddelbare barn og en egen direkte-gruppe for kontekstnoden. «Ikke angitt» flyttes ikke ned i treet. `TotalUnique`, `ContextUnique` og `GroupSum` er forskjellige størrelser. Prosent er gruppens antall / summen av de viste gruppene; overlappende søsken kan gi større gruppesum enn unike avtaler. Forfedre og etterkommere blandes ikke i nevneren; nullsum gir tom tilstand. Ingen dimensjonsbasert finansiell fordeling er introdusert.

Ikke-admin ser bare autoriserte tildelte noder og deres nødvendige forfedre. Fremmed eller skjult valgt node avvises. UI har request-rekkefølge, kansellering og vern mot at sene katalogsvar, resultater, feil eller finally endrer nyere tilstand. Kontobytte nuller kontekst; dimensjonsbytte starter på topp; gyldig kontekst bevares ved refresh/filter. Ugyldig gammel kontekst gir kontrollert retur til topp, og deaktivert dimensjon faller tilbake til fast gruppering. Gamle antall tømmes straks ved nytt kall.

Rene hierarki-/service-/komponenttester dekker alle 66 demonoder og syntetisk fire-nivåstre, overlapp, parent-only, parent+child, direkte-gruppe, manglende, tilgang, prosent, drill/back og raske bytter. InMemory og offline SQL-oversettelse i eksisterende testdekning er ikke bevis for dynamiske Npgsql-queryers kjøring eller ytelse.

## 6. Hele demodatasettet og idempotensens grenser

Frossen referanse er utledet direkte fra committen oppgitt over. `derive-reference.py --check` reproduserer de tre JSON-filene byteidentisk. Produksjonen leser ikke referansepakken. `MagidaDemoData.Create(userId, recordedUtc)` inneholder de opprinnelige deterministiske løkkene/formlene, og den virkelige `SeedMagidaAsync` kaller denne, leser eksisterende kontoavgrensede data, kaller `MagidaDemoGraph.MissingFrom`, legger alle 13 samlinger til riktige DbSets og lagrer ved sitt eksisterende lagringspunkt. Integrasjonen var bare kildekontrollert i fase 8; initializer ble senere kjørt manuelt i fase 9, se §13.

| Kandidatgraf | Faktisk databasefritt kontrollert |
|---|---|
| GEO | 4 kontinenter → 16 land → 16 hovedsteder = 36 verdier |
| DEPT | 5 divisjoner → 25 avdelinger = 30 verdier |
| Konfigurasjon | Begge aktive, single-select, LeafOnly=false, tilgjengelige/valgfrie i Agreements, ingen automatisk Leasing-regel |
| Oversettelser/ID-er | 4 nb-NO/en-GB-rader; GEO/DEPT dimensjonsnummer 3/4, PROJ/EQUIP fortsatt 1/2; alle 66 gamle verdi-ID-er bevart |
| Avtaler/tildelinger | Alle 300 avtaler og nøyaktig 600 eksplisitte tildelinger; én GEO og én DEPT per avtale, ingen ekstra forfedrerader |
| Øvrige referansefelt | Alle avtaleegenskaper, 16 virkelige motparter, 4 indekser/8 indeksverdier, 300 indeksvalg, 600 linjer, 600 linjeversjoner og 600 prisversjoner sammenlignes feltvis |
| Hele grafen | 3102 kandidatrader; eier/aktør/tid kontrolleres mot injiserte argumenter, ingen tilfeldig redistribusjon |

Hvert kontinent har 75 inklusive avtaler. Innen hvert kontinent har de fire land/hovedsteder 19,19,19,18. Hver divisjon har 60 inklusive, hver avdeling 12 eksplisitte. Alle nodefordelinger, foreldre, navn, nivåer, genereringsrekkefølge og UI-hierarkirekkefølge i de tre språkene er sammenlignet, ikke bare disse summene. Nye verdikoder bygger på stabil nodekategori/seednummer. Oversettelser bruker nytt SeedIds-namespace 34 og tildelinger 35, uten omnummerering av tidligere ID-er.

`MissingFrom` er insert-missing/skip, ikke generell upsert eller integritetsreparasjon. Eksisterende metadata/rule/oversettelsesrad bevares med brukerens endringer. Eksisterende avtale-ID gjør at **hele avtalegrafen hoppes over**, inklusive assignments, indeksvalg, linjer og versjoner. Fjernede valg gjenopprettes derfor ikke. Delvis manglende metadata kan tilføyes. En eksisterende avtale med manglende barn repareres ikke. Opptatt dimensjons-/verdikode med annen ID avvises eksplisitt, fremfor stille remapping. Samordningen validerer konto i alle 13 samlinger. Slettede seed-metadata har ingen tombstone og kan bli foreslått igjen.

725188 assertions er gjentatte felt-/strukturkontroller av produksjonsgenerering og samordning, ikke 725188 selvstendige akseptansescenarioer. Ingen DbContext eller initializer brukes i denne suiten. Fase 9 dokumenterte senere faktisk innsetting og to avgrensede idempotensscenarioer; audit, relasjonell atomisitet, parallelle initialiseringer og avbruddsgjenoppretting er fortsatt ikke fullstendig verifisert. Øvrig Nordic Property-/konto-/identitetsdemo og Leasing-partialen er bevart ved kildegjennomgang; Leasing-partialen er byteidentisk med opprinnelig commit. Det finnes **ingen ferdig PostgreSQL-basert fullgraf-mot-JSON-komparator** i repoet. Leasing `DemoSeedChecks` ville kjørt initializer to ganger i Leasing-runneren, men den runneren ble ikke kjørt.

## 7. Legacy og migrasjoner

`GeographicArea`/`OrganizationElement`, dedikerte tjenester/registersider, gamle avtalefelter/DTO-er, DbSets/mappinger, DI/nav, gammel dashboardgruppering og legacy-generering er fjernet. Reelle Organization- og fysiske lokasjonsdata er beholdt. En frisk avgrenset kontroll av `src` fant 276 sterke legacy-treff i 15 migrasjons-/Designer-filer, ingen i aktiv modell, snapshot, tjenester eller UI. Dette er en snevrere kontroll enn fase-7-auditens brede ordsøk. Den uendrede [legacy-auditen](unified-dimensions-phase-7/legacy-audit.json) begrunner alle sine 2485 treff i 62 filer: migrasjonshistorie, tilsiktet schemafjerning, frossen referanse, tester, stabile seed-identiteter, dokumentasjon, delt generisk label eller legitim fysisk/annen domeneterminologi. Brede ord som Department/Location er ikke i seg selv legacy-avhengigheter.

| Ny migrasjon | Up / Down i kilde |
|---|---|
| `20261112084722_AddDimensionTranslations` | Oversettelsestabell og unik indeks / bare ny tabell fjernes |
| `20261112091606_AddAgreementDimensions` | Regler, eksplisitte tildelinger og historikk med FK-er/indekser/check / bare de tre nye tabellene fjernes |
| `20261112104542_RemoveLegacyAgreementClassifications` | 8 operasjoner: 2 gamle FK-er, 2 indekser, 2 avtalekolonner og 2 klassifiseringstabeller fjernes / 12 schemaoperasjoner gjenoppretter gammelt schema, **ikke data** |

Ingen data-copy, backfill eller egendefinert SQL finnes i de tre feature-migrasjonene. Historikkens seks INSERT/UPDATE-operasjoner inngår i metadataauditen og er klassifisert, aldri eksekvert. 45 migrasjoner er strukturelt replayet; tabell-/kolonne-/nøkkel-/indeks-/FK-/check-navn stemmer med sluttmodellen. Feature-Down mot forrige TargetModel og Up tilbake er kontrollert. Strukturell replay er ikke validering av PostgreSQL-eksekvering, provider-spesifikke datatyper, eksisterende data eller låsing.

Alle 84 historiske migrasjons-/Designer-filer stemmer byte-for-byte med fase-1-manifestet. Alle 90 migrasjons-/Designer-filer og gjeldende snapshot, totalt 91 filer ved fase-8-start, er uendret i fase 8. Snapshot stemmer med nåværende Npgsql-modell; `has-pending-model-changes` rapporterer ingen endringer. Ingen ny migrasjon var nødvendig i fase 8.

## 8. Historisk kravmatrise ved fase-8-slutt

Statuskoder gjelder **evidens**, ikke generell produksjonsgodkjenning: **D** = implementert og kontrollert databasefritt; **S** = implementert, men her bare kildegjennomgått/kompilert; **P** = avgjørende PostgreSQL-bevis gjenstår; **B** = avgjørende browserbevis gjenstår; **R** = prosessmessig erstattet. D+P/B betyr at den kjørte delkontrollen passerer, mens hele kravet ikke er operativt verifisert. Ingen rad er klassifisert som reell implementasjonsmangel etter denne gjennomgangen.

Filreferanser nedenfor er relative til repoet. Forkortelser har eksakte røtter:

- **C** = `src/TenantPlatform.Core`; **I** = `src/TenantPlatform.Infrastructure`; **W** = `src/TenantPlatform.Web`; **T** = `tests`.
- **AS** = `W/Services/Agreements/AgreementService.Dimensions.cs` (`ApplyAgreementDimensionsAsync`, katalog-/regel-/historikkoperasjoner); **AG** = `W/Services/Agreements/AgreementService.cs` (offentlige create/update og private kjernemetoder); **AN** = `W/Services/Agreements/AgreementService.Analysis.cs`.
- **DS** = `W/Services/Dimensions/DimensionService.cs`; **DL** = `C/Dimensions/DimensionLocalization.cs` og `C/Localization/{LanguageHelper,TranslationHelper}.cs`; **AM** = `C/Agreements/AgreementDimensions.cs` og `I/Persistence/Configurations/AgreementDimensionConfiguration.cs`.
- **UI** = `W/Components/Pages/Agreements/{AgreementEdit,AgreementFields,AgreementDetails,AgreementAnalysisDialog,AgreementDimensionChoices,AgreementDimensionsView,AgreementDimensionSnapshotView}.razor` og `AgreementDimensionEditState.cs`; **DU** = `W/Components/Pages/Dimensions/{DimensionsPage,AgreementDimensionRulesSection,DimensionEditorDialog,DimensionOverview,DimensionHistoryView}.razor`.
- **DH** = `W/Services/Agreements/{AgreementService.Dashboard,AgreementInsightHierarchy}.cs` og `AgreementDashboardDtos.cs`; **DW** = `W/Components/Pages/Dashboard/InsightsWidget.razor`.
- **DM** = `I/Initialization/{MagidaDemoData,MagidaDemoGraph,DemoDataInitializer,SeedIds}.cs`; **LS** = `W/Services/Leasing/{LeasingService.Classification,LeasingClassificationDtos}.cs` og `W/Components/Pages/Leasing/{DimensionChoices,LeasingClassificationEditor,LeasingClassificationView,LeasingReportPage}.razor`.
- **TD** = `T/TenantPlatform.Dimensions.SmokeTests/{LocalizationChecks,ServiceChecks,ComponentChecks,AccountDeletionChecks,FinalModelChecks,SchemaAudit}.cs` og `Program.cs`.
- **TA** = `T/TenantPlatform.AgreementDimensions.SmokeTests/{ConfigurationChecks,AssignmentChecks,AccessChecks,MetadataChecks,UiChecks}.cs`.
- **TM** = `T/TenantPlatform.DemoDimensions.SmokeTests/{EquivalenceChecks,ReconciliationChecks}.cs`; **TH** = `T/TenantPlatform.Dashboard.SmokeTests/{Program,HierarchyChecks,DynamicWidgetChecks}.cs`.

### Funksjonelle krav, tester og avgrensning

| Krav | Implementasjon / konkret evidens | Status og begrensning |
|---|---|---|
| §1.1–3 flerspråk, modulregler, generiske avtaletildelinger | DL, DS, AM, AS; TD/TA | D+P/B; detaljer nedenfor |
| §1.4,8 full legacy-erstatning | §7 i rapporten, frisk kildesøk, TD FinalModelChecks/SchemaAudit | D+P; slutt-DDL ikke kjørt |
| §1.5–6 komplett demo/~300 | DM, TM mot frossen referanse | D+P; faktisk initializer ikke kjørt |
| §1.7 hierarkisk dashboard | DH/DW; TH | D+P/B |
| §1.9 Leasing/øvrig forretning | LS; TD ComponentChecks; diff mot originalcommit | D for rene beregninger/rendering, S+P/B for full regresjon |
| §1.10 autonom/trusted utførelse | Manuell faseprosess, kontroller i §9 | R for orchestrator; tillatte kontroller utført uten utvidede rettigheter |
| §2.1 ingen flytting av lagrede data eller DB-tilgang/reset | Tre nye migrasjoner uten DML; offline fabrikk/guard, kun tillatte innganger kjørt | S; ingen driftshandling utført |
| §2.2 nytt schema, legacy-drop, uendret historikk, øvrig schema | AM, translation-konfigurasjon, snapshot, tre migrasjoner; TD SchemaAudit + 84 historiske hasher | D+P; logisk kjede ≠ PostgreSQL |
| §2.3 eksplisitt utsettelse | Åpne porter §1 og sjekkliste §12 | S; ingen PG-PASS-påstand |
| §3.1 eksisterende rammeverk, stabile koder, navn, aktive flagg, single/multi/leaf, dybde, revisjon/historikk | `C/Dimensions/Dimension.cs`, DS, eksisterende DimensionHierarchy; TD/TA/TH | D+P/B; ingen ekstra klassifiseringsmodell |
| §3.2 globalt vs modulansvar, uavhengig Leasing | AM uten AllowMultiple/LeafOnly, AS regelendring; TA ConfigurationChecks | D+P; lagring testet InMemory |
| §3.3 faktisk Organization og adresser/bygg/enheter | Uendret øvrig Core, AG/AN og DM diff; TM motpartsfelt; TD schema | D for kandidat/schema-navn, S+P for øvrig drift |
| §3.4 autorisasjon og konto på alle nye lesinger/skrivinger | AS RequireMember/admin/resource; AG/AN/DH/DS/LS, AM FK-er; TA AccessChecks, TD ServiceChecks, TH | D+P; serverkontroll finnes, PG-/cookie/circuit-bevis gjenstår |
| §4.1 single-select | AS ApplyAgreementDimensionsAsync; TA AssignmentChecks/UI | D+P/B |
| §4.2 multiple og ingen duplikater | AS full plan; AM unik indeks; TA AssignmentChecks/MetadataChecks; TH overlapp | D+P; unikhet ikke kjørt på DB |
| §4.3 parent/leaf-only | AS validert verdikatalog; UI; TA AssignmentChecks/UiChecks | D+P/B |
| §4.4 bare eksplisitte rader, beregnet medlemskap | AM/AS, DH Aggregate, DM; TA/TM/TH | D+P; faktisk DB-radtelling gjenstår |
| §4.5 inaktive dimensjoner/verdier, historikk, Leasing | AS tillegg vs eksisterende, UI editstate, LS; TA/TD | D+P/B; historiske snapshots ikke omskrevet |
| §5.1.1 stabile koder og .9 ID-er ved språkbytte | DL/DS/AM/LS; TD Localization/ComponentChecks, TM | D; persistens P |
| §5.1.2–5 navn/beskrivelse, nb-NO/en-GB/sv-SE, gjenbruk | `C/Dimensions/DimensionTranslation.cs`, DL, resx; TD LocalizationChecks | D+P/B |
| §5.1.6 oversettelseseditor | DU DimensionEditorDialog + DS translation-patch; TD Service/ComponentChecks | D+P/B; ekte modal/fokus gjenstår |
| §5.1.7 lokaliserte etiketter i Agreements/Leasing/dashboard/velgere | UI/LS/DH/DW/DU; TA UiChecks, TD ComponentChecks, TH | D+B; runtime DB P |
| §5.1.8 og §5.2 feltvis fallback, kode, tom beskrivelse | DL ResolveText, kontoavgrensede oversettelser; TD LocalizationChecks | D; ingen tvungen engelsk-fallback |
| §5.3 behold verdinavn/oppførsel | DimensionValue/DimensionHierarchy, DS; TD/TM | D/S; ingen verdioversettelsesredesign |
| §6 ny fane og kun to modulflagg | DU DimensionsPage/AgreementDimensionRulesSection; TA UiChecks | D+B |
| §6.1 required⇒available, disable⇒not required | AS SaveAgreementDimensionRuleAsync, DU handlers, AM check; TA ConfigurationChecks/UiChecks | D+P/B |
| §6.1 unavailable ikke nyvalg, required create på server | AS; TA ConfigurationChecks/AssignmentChecks | D+P; deaktivert/manglende valgbart required-oppsett gir konfigurasjonsfeil |
| §6.1 bevar eksisterende unavailable | AS/UI; TA AssignmentChecks/UiChecks | D+P/B |
| §6.1 lagring, audit, konto og concurrency | AS kontolås/revisjon/history, AM, audit-interceptor; TA ConfigurationChecks/AccessChecks | D for kjernelogikk, S+P for transaksjon/låsing |
| §6.2 ny required på gammel avtale, urelatert edit, ingen autoassign, eksplisitt endring | AS patchplan; UI state; TA AssignmentChecks/UiChecks | D+P/B |
| §7.1 flere dimensjoner, stable IDs, gyldig account/value/dimension, unikhet | AM account-aware FK-er, AS; TA MetadataChecks/AssignmentChecks | D+P |
| §7.2 avtaletilgang, dimensjon/konto, verdi/konto/dimensjon | AG/AS/AN; TA AccessChecks/AssignmentChecks | D+P; ugyldige ID-er avvises på server |
| §7.2 availability, active, multiple, leaf, required, duplicate | AS samlet før mutasjon; TA AssignmentChecks | D+P |
| §7.2 historikk og eksisterende concurrency | AS/AG, AN ExpectedAgreementRevision; TA og kompilerte PG-runnere | D for kjernetilfeller, S+P for reelle samtidige commits |
| §7.3 dynamikk/navn/required/stier/single/multi/parent | UI + AS katalog; TA UiChecks | D+B/P |
| §7.3 input etter feil, lokale meldinger, create/edit/details, historiske valg | UI editstate og feilhåndtering; TA UiChecks incl lesebruker | D+B/P |
| §7 tillegg: alle create-/analysegrener | AN begge godkjenninger + UI AgreementAnalysisDialog, DTO ExpectedAgreementRevision; TA UiChecks, kompilert AgreementAnalysis | D for komponentstate, S+P for offentlig transaksjon/dokumentpromotering |
| §7.4 lifecycle/motpart/eier/økonomi/notice/index/reminders/documents | Fokusert AG/AN diff, øvrige partials og DM; TM alle kandidatfelt; ni PG-programmer kun kompilert | D for kandidater, S+P/B for faktisk regresjon |
| §8.1 full dependency inventory | Fase-1 legacy-inventory + fase-7 audit + frisk src-søk, samlet diff inkl nye/slettede filer | S/D; semantisk klassifisert, ingen aktive sterke legacy-treff |
| §8.2 modeller/services/pages/DI/nav/DTO/dashboard/EF/seed/tester fjernet/erstattet | Rapport §7, TD FinalModelChecks, ny drop-migrasjon, oppdaterte AccountRegisters/Agreements/Dashboard | D/S+P; slutt-tabeller ikke fysisk droppet |
| §8.3 historikk bevart, aktuell modell/snapshot uten legacy | 84 historiske hasher; 91 fase-8-hasher; TD FinalModelChecks/SchemaAudit + pending-model-check | D; ny drop-migrasjon omtaler nødvendigvis gamle navn |
| §8.4 Organization/adresse og Leasing-semantikk | Core/diff, uendret Leasing-partial/kalkulator; TD ComponentChecks | D/S+P/B |
| §9.1 autoritativ generatorinventar | Frossen derive-reference fra originalcommit; byteidentisk --check | D; ingen levende DB brukt |
| §9.2 GEO og §9.3 DEPT alle flagg/navn/no Leasing | DM Create; TM EquivalenceChecks | D+P; svenske seed-navn følger fallback |
| §9.4 alle noder/nivåer/navn/foreldre/order/stabilitet | DM + alle 66 gamle verdi-ID-er; TM feltvis tre/språkrekkefølge | D+P |
| §9.5 én av hver/mapping/distribusjon, ikke random/ancestor-rader | DM deterministiske formler; TM alle 300 par + direkte/inklusive fordelinger | D+P |
| §9.6 300 avtaler og alle øvrige felter/relasjoner/økonomilinjer | DM; TM hele kandidatgrafen, inkl versjoner/indekser/motparter | D+P; ikke bare antall |
| §9.7 once/duplicates/existing/account | DM MissingFrom og faktisk SeedMagidaAsync-integrasjon; TM ReconciliationChecks | D for ren samordning, S+P for DB-initialisering/idempotens |
| §9.8 ingen legacy-generering | DM, frisk søk; TM og TD sluttmodell | D/S; faktisk database P |
| §10.1 fire faste grupper | DH/DW + TH eksisterende kontroller | D+P/B |
| §10.2 alle aktive tilgjengelige dimensjoner, navn, ordinær GEO/DEPT | DH GetInsightDimensionsAsync, DW; TH DynamicWidgetChecks | D+P/B |
| §10.3 roll-up, shared ancestor, én pernode, ingen ekstra rader | DH Aggregate HashSet; TH HierarchyChecks, TM | D+P |
| §10.4 root/child/videre/back, filtre/gyldig kontekst | DH/DW; TH fire nivåer og komponenthandlinger | D+P/B |
| §10.5.1–3 distinct vs søskenoverlapp | DH separate ID-sett og TotalUnique/ContextUnique/GroupSum; TH | D+P |
| §10.5.4–7 vist gruppesum som nevner, ikke miksnivå, egne unike totaler, overlaphelp | DW + DH DTO; TH parent+child/overlapp/nullsum | D+B/P |
| §10.6 parent-only ikke descendants, direkte-gruppe | DH Aggregate, DW; TH | D+P/B |
| §10.7 Ikke angitt bare topp | DH leftjoin + Aggregate; TH | D+P/B |
| §10.8 bare tilgjengelige avtaler/konto | DH Accessible, konto-/nodekontroll; TH service/access | D+P; ingen provider-/RLS-påstand |
| §10 tillegg: sene svar/bytte/stale node | DW requestvern/reset/retry; TH DynamicWidgetChecks | D+B; reell nettverks-/circuitinteraksjon gjenstår |
| §10.9 ingen økonomisk aggregering | DH count-only projeksjon; diff/kildegjennomgang | S |
| §11 alle schemaobjekter/accountFK/unikhet/index/legacy-drop | AM/translation-config, snapshot/tre migrasjoner; TD Metadata/SchemaAudit | D+P; se §7 for Up/Down |
| §11.1–9 historikk/nytt/snapshot/konto/øvrig/logisk kjede/no copy/noDB | TD 45-migrasjonsreplay + hasher + safe fabrikk; faktisk kommandologg | D/S; PostgreSQL-kjede obligatorisk P |
| §12.1 konfigurasjon/persistens/auth/tenant | TA Configuration/AccessChecks | D InMemory; faktisk persistens P |
| §12.2 hele utvalgs-/edit-/historikkomfang | TA Assignment/UiChecks; TM øvrige kandidatfelt | D; provider/øvrig drift P/B |
| §12.3 alle dashboardtilfeller/filter/auth/tenant | TH Program/HierarchyChecks/DynamicWidgetChecks | D+P/B |
| §12.4 språk/fallback/labels | TD Localization/ComponentChecks, TA UiChecks, TH, resx-kontroll | D+B/P |
| §12.5 full demoreferanse og idempotens | TM, derive --check | D for produksjonsgraf/samordning; DB P |
| §12.6 Leasing/classification/calc/agreements/dashboard/auth/contracts | TD ComponentChecks + TA/TH; alle testprosjekter bygget | D for tillatte innganger; ni PG-programmer bare kompilert, P/B |
| §12.7 ærlig teststatus | Kommandotabell §9 og åpne porter | S; compile/run klart atskilt |
| §13.1–4 orchestrator/plan/develop.json/worktree/policy engine | Brukerens manuelle faser og eksplisitte eksisterende checkout erstatter mekanismen | R; ingen orchestratorfunksjon påstått, sikkerhetsgrenser fortsatt fulgt |
| §13.5–6 complete-vs-failed, sikkerhet/ingen DB/deploy | Statusinnledning, §1 og faktisk utførelse | S; complete er kildeimplementasjon, ikke PG-godkjenning |
| §14.1–20 sluttrapport og gjennomgang | Denne rapporten §§1–12 og fase-8-rapporten | S; manuell sjekkliste er ikke utført |
| §16 alle non-goals | Samlet diff/ingen driftshandling; uendrede økonomi-/auth-/businessmønstre | S; ingen datamigrering eller ny plattform introdusert |
| §17 full logisk demo, ingen forenkling/random/parallellegacy/copy/access | DM/TM + frisk legacy-/migrasjonskontroll | D/S+P; db-ekvivalens fortsatt åpen |

### Alle akseptansekriterier i §15, i original rekkefølge

| ID | Kriterium / evidensreferanse i matrisen over | Status |
|---|---|---|
| AC01 | Delt rammeverk (§3.1) | D |
| AC02 | Flerspråklig navn/beskrivelse (§5) | D+P/B |
| AC03 | Bruk i Avtaler (§6) | D+B |
| AC04 | Tilgjengelig/required (§6.1–2) | D+P/B |
| AC05 | Generiske tildelinger (§7.1–3) | D+P/B |
| AC06 | Single/multiple/leaf (§4.1–3) | D+P/B |
| AC07 | GEO/DEPT ordinære (§9.2–3) | D+P |
| AC08 | Aktiv/single/parent (§9.2–4) | D+P |
| AC09 | Tilgjengelig/valgfri (§9.2–3) | D+P |
| AC10 | Ingen automatisk Leasing (§9.2–3) | D+P |
| AC11 | Hele GEO-treet (§9.4) | D+P |
| AC12 | Hele DEPT-treet (§9.4) | D+P |
| AC13 | Nivå/navn/foreldre (§9.4) | D+P |
| AC14 | 300 avtaler (§9.6) | D+P |
| AC15 | Korrekt GEO+DEPT per avtale (§9.5) | D+P |
| AC16 | Deterministisk fordeling (§9.5) | D+P |
| AC17 | Idempotens (§9.7) | D for ren samordning, P for database |
| AC18 | Ingen legacy-generering (§9.8) | D/S |
| AC19 | Legacy runtime/modell/schema fjernet (§8) | D/S, P for fysisk schema |
| AC20 | Organization/geografi ellers bevart (§3.3/8.4) | D/S+P |
| AC21 | Dynamisk dashboard (§10.2) | D+P/B |
| AC22 | Roll-up/drill (§10.3–4) | D+P/B |
| AC23 | Unik pernode (§10.3/5) | D+P |
| AC24 | Parent-only synlig (§10.6) | D+P/B |
| AC25 | Søskennivånevner (§10.5) | D+B/P |
| AC26 | Leasing uendret (§8.4/12.6) | D for beregning/rendering, S+P/B for full regresjon |
| AC27 | Tillatt automatisert verifikasjon passerer (§12.7) | D; alle 18 build/run-kommandoer exit 0 |
| AC28 | Ærlig utsettelse/manual follow-up (§1/12) | S; NOT PERFORMED |
| AC29 | Migrasjonskilde/snapshot/historikk (§11) | D; execution P |
| AC30 | Ingen DB/SQL/reset/deploy/security-redesign (§2/13) | S; grenser fulgt |

## 9. Faktisk utført bygg og regresjon i fase 8

Alle kommandoer nedenfor ble kjørt fra arbeidskatalogen øverst. De 13 testprosjektene ble funnet i faktisk filsystem og bygget eksplisitt; solution-bygget alene bygger ikke disse konsolltestene. Entrypoints ble kontrollert før kjøring. `dotnet test` er ikke brukt som erstatning for konsollinngangene.

| Faktisk kommando | Type | Resultat | Advarsler / grense |
|---|---|---|---|
| `dotnet build TenantPlatform.sln --no-restore` | Bygg | PASS, exit 0 | NU1902, NU1903; kun kompilering |
| `dotnet build tests/TenantPlatform.AccountRegisters.SmokeTests/TenantPlatform.AccountRegisters.SmokeTests.csproj --no-restore` | Bygg | PASS, exit 0 | MSB9008, NU1902, NU1903; kun kompilering |
| `dotnet build tests/TenantPlatform.AgreementAnalysis.SmokeTests/TenantPlatform.AgreementAnalysis.SmokeTests.csproj --no-restore` | Bygg | PASS, exit 0 | NU1902, NU1903; kun kompilering |
| `dotnet build tests/TenantPlatform.AgreementDimensions.SmokeTests/TenantPlatform.AgreementDimensions.SmokeTests.csproj --no-restore` | Bygg | PASS, exit 0 | NU1902, NU1903; kun kompilering |
| `dotnet build tests/TenantPlatform.AgreementFollowup.SmokeTests/TenantPlatform.AgreementFollowup.SmokeTests.csproj --no-restore` | Bygg | PASS, exit 0 | MSB9008, NU1902, NU1903; kun kompilering |
| `dotnet build tests/TenantPlatform.AgreementNotice.SmokeTests/TenantPlatform.AgreementNotice.SmokeTests.csproj --no-restore` | Bygg | PASS, exit 0 | MSB9008, NU1902, NU1903; kun kompilering |
| `dotnet build tests/TenantPlatform.AgreementPeriods.SmokeTests/TenantPlatform.AgreementPeriods.SmokeTests.csproj --no-restore` | Bygg | PASS, exit 0 | MSB9008, NU1902, NU1903; kun kompilering |
| `dotnet build tests/TenantPlatform.AgreementProcessing.SmokeTests/TenantPlatform.AgreementProcessing.SmokeTests.csproj --no-restore` | Bygg | PASS, exit 0 | MSB9008, NU1902, NU1903; kun kompilering |
| `dotnet build tests/TenantPlatform.Agreements.SmokeTests/TenantPlatform.Agreements.SmokeTests.csproj --no-restore` | Bygg | PASS, exit 0 | MSB9008, NU1902, NU1903; kun kompilering |
| `dotnet build tests/TenantPlatform.Dashboard.SmokeTests/TenantPlatform.Dashboard.SmokeTests.csproj --no-restore` | Bygg | PASS, exit 0 | MSB9008, NU1902, NU1903; kun kompilering |
| `dotnet build tests/TenantPlatform.DemoDimensions.SmokeTests/TenantPlatform.DemoDimensions.SmokeTests.csproj --no-restore` | Bygg | PASS, exit 0 | NU1902, NU1903; kun kompilering |
| `dotnet build tests/TenantPlatform.Dimensions.SmokeTests/TenantPlatform.Dimensions.SmokeTests.csproj --no-restore` | Bygg | PASS, exit 0 | NU1902, NU1903; kun kompilering |
| `dotnet build tests/TenantPlatform.Leasing.SmokeTests/TenantPlatform.Leasing.SmokeTests.csproj --no-restore` | Bygg | PASS, exit 0 | NU1902, NU1903; kun kompilering |
| `dotnet build tests/TenantPlatform.MeetingRooms.SmokeTests/TenantPlatform.MeetingRooms.SmokeTests.csproj --no-restore` | Bygg | PASS, exit 0 | MSB9008, NU1902, NU1903; kun kompilering |
| `dotnet run --project tests/TenantPlatform.Dimensions.SmokeTests --no-build --no-restore` | Kjøring | PASS, exit 0; 157 kontroller | Ingen; InMemory/offline metadata |
| `dotnet run --project tests/TenantPlatform.AgreementDimensions.SmokeTests --no-build --no-restore` | Kjøring | PASS, exit 0; 166 kontroller | Ingen; InMemory/offline metadata |
| `dotnet run --project tests/TenantPlatform.DemoDimensions.SmokeTests --no-build --no-restore` | Kjøring | PASS, exit 0; 725188 felt-/strukturassertions | Ingen; ren produksjonsgraf/samordning |
| `dotnet run --project tests/TenantPlatform.Dashboard.SmokeTests --no-build --no-restore` | Kjøring | PASS, exit 0; 107 PASS-kontroller | Ingen; InMemory/offline metadata; DASHBOARD_PREVIEW_PATH fjernet fra prosessmiljøet |

**Advarsler:** NU1902/NU1903 gjelder eksisterende `System.Security.Cryptography.Pkcs` 10.0.0. MSB9008 er eksisterende restore-cache som peker på det tidligere, manglende Application-prosjektet. Solution-bygget hadde 2 pakkeadvarsler og 0 feil. Testbyggene hadde advarselstypene spesifisert i tabellen. Ingen pakker, cache eller produksjonsavhengigheter er endret for å fjerne varslene. Rene run-kommandoer ga ingen slike varsler.

**Leasing som faktisk kjørte:** TD ComponentChecks bruker produksjonens `LeasingAllocationCalculator` med None/Percent/NetAmount/Quantity, kontroll av avrunding og bevaring av netto 100,01, MVA og kvantum, og rendering av valg/nåværende visning/historikk i tre språk. Serialisert snapshot er identisk før/etter språkbytte. Dette er ikke full Leasing PostgreSQL-regresjon. Hele `TenantPlatform.Leasing.SmokeTests` er bare kompilert.

| Øvrig faktisk kontroll | Resultat / bevisgrense |
|---|---|
| `dotnet ef migrations has-pending-model-changes --project src/TenantPlatform.Infrastructure --startup-project src/TenantPlatform.Web` | PASS: «No changes have been made to the model since the last migration.» Offline fabrikk; build med eksisterende NU1902/NU1903; ingen forbindelse |
| `python3 docs/features/unified-dimensions-phase-1/derive-reference.py --check` | PASS: alle 3 JSON byteidentiske; 36/30 noder, 300 komplette mappinger, 600 økonomilinjer |
| Python SHA-256-kontroll mot startmanifest og frossent migrasjonsmanifest | PASS: alle 713 eksisterende filer, alle 91 migrasjon/snapshot-filer og alle 84 historiske manifestoppføringer uendret |
| Python XML-/ressurskontroll mot originalcommit | PASS: 40 nye/26 fjernede feature-nøkler konsistent i nb-NO/en-GB/sv-SE, ingen duplikat, like formatplassholdere |
| Friskt sterkt legacy-søk i src uten bin/obj | PASS: bare tilsiktet migrasjonshistorie/schemafjerning; 276 treff i 15 filer, ingen i snapshot/aktiv kode |
| `git diff --check` | PASS; ingen whitespace-feil |

SHA-/ressurs-/søkkontrollene var inline Python-inspeksjoner, ingen nye repo-testinnganger. Rå kjøringslogg og eksakte argumenter/exitkoder ligger som midlertidig sesjonsevidens i `/tmp/tenantplatform-phase8-verification.json`, build-/run-logger i `/tmp/tenantplatform-phase8-*.log` og søk/diffliste i `/tmp/tenantplatform-phase8-review.json`. Disse er ikke varige repo-leveranser; tabellene over er den konsoliderte kjøringsrapporten. Ingen databasetester er skjult bak disse resultatene.

## 10. Beslutninger, rettelser og bevaring

Fase 1–7 er godkjente faseleveranser, men fase 8 leste faktisk produksjonsimplementasjon og samlet endring, ikke bare rapportene. Særlig vurdert: alle create-/analysegrener, autorisasjon og konto, omitted/empty/identical patch, historiske valg, ny required på gammel avtale, kontolås/revisjon/atomisitet, feltvis fallback, Leasing snapshots, dashboardets tellings- og svarvern, faktisk initializer-integrasjon, whole-agreement-skip, reelle forretningsobjekter, kontosletting og fravær av shadow-/legacykontrakter.

**Ingen konkret kildefeil ble påvist i sluttkontrollen.** Ingen produksjonskode, tester, forventninger, migrasjoner eller tidligere artefakter er endret i fase 8. Bare fase-8-rapporten og denne sluttrapporten er nye. Det er derfor ikke lagt til tester eller en ekstra migrasjon. Kildebevaring og presise verifikasjonsgrenser foretrekkes fremfor kosmetiske endringer eller oppblåste testantall.

Bevisst videreført: tilgjengelighet er autoritativ på server (`false` ⇒ required `false`), ny required uten gyldig oppsett feiler create, gamle avtaler krever bare reell berørt dimensjon, historikk bruker registrerte navn, dashboard bruker sett og viste grupper som nevner, og demodata bruker deterministisk insert-missing/whole-agreement-skip. Dette er avtalte beslutninger i fase-rapportene, ikke tilfeldige justeringer av testforventninger.

Ved fase-8-start ble SHA-256 registrert for 713 eksisterende filer. Manifest: `/tmp/tenantplatform-phase8-start-sha256.json`, SHA-256 `783fcc20a17abfd9b6d231a1c98de2e743a5b05dd98b80d6858af12c20c309c6`. Alle er bevart. Aggregerte hasher beregnes ved UTF-8 av sorterte `path<TAB>sha256<LF>`-linjer:

| Omfang | Antall | Aggregert SHA-256 |
|---|---:|---|
| Alle startfiler | 713 | `45a23619e9f61edd228fd0a3d7cf425cfe70e4b651db7ae71193181f189af6ea` |
| Migrasjoner/Designers/snapshot ved start | 91 | `be110899c761f7f226e4c0bcdb717da5c546d183ef60234a76cdc61fc305eacc` |
| Tidligere faser/referanse/audit | 12 | `2a5d67ef4482eadd8d41b5352212ce45e39fd8842a37258641b273e5ec5bd19b` |

Gjeldende snapshot SHA-256: `762bdcdd81ea85ef02b57b5e5f6c42609d4dd41d0f3c7aa58f68d2f6d21fcdf8`. Fase-1-historikken kan revideres mot det bevarte `migration-source-sha256.json` og originalcommittens Git-blobs. Manifestet har 85 oppføringer inklusive datidens snapshot; bare de 84 historiske migrasjons-/Designer-filene skal være uendret fra fase 1. En avsluttende inline-inspeksjons telleassertion ble korrigert for dette skillet og kjørt på nytt med PASS. Gjeldende snapshot er i stedet kontrollert byteidentisk mot fase-8-start og strukturelt mot dagens modell.

## 11. Samlet diff og gjenværende risiko

Ved sluttkontrollen omfattet diffen fra originalcommit 57 sporede endrede/slettede filer og 60 allerede usporede filer. De to nye rapportene gir **62 usporede filer** ved fase-8-slutt. Usporede domene-/service-/UI-/migrasjons-/testfiler er gjennomgått separat; vanlig `git diff` viser dem ikke. Tidligere slettinger er bevart. Ingen staged/unstaged arbeid er nullstilt.

Gjennomgå fra `/Users/morten/Dev/TenantPlatform` med ufarlige inspeksjoner:

```sh
git status --short --untracked-files=all
git diff --stat f2f2d67338fd05e59624423aad675ec4f8ca415d
git diff --name-status f2f2d67338fd05e59624423aad675ec4f8ca415d
git diff f2f2d67338fd05e59624423aad675ec4f8ca415d -- src tests
git ls-files --others --exclude-standard
git diff --check
```

Les hver usporet tekstfil fra siste liste separat, særlig de tre migrasjonene med Designers, modellen/config, AS, DM og nye testprosjekter. Bruk `git show f2f2d67338fd05e59624423aad675ec4f8ca415d:<sti>` for opprinnelig innhold og `git diff --no-index -- /dev/null <ny-fil>` ved behov (exit 1 betyr diff). Ikke stage/commit eller anvend databaseendringer som del av denne inspeksjonen.

Ved fase-8-slutt var gjenværende risiko utsatt verifikasjon, ikke en påvist implementasjonsblocker. Fase 9 reduserte dette gjennom faktisk migrering/sluttschema, demoeksporter, to PostgreSQL-runnere og avgrenset manuell editorverifikasjon. Resten står presist i §13.4. InMemory kjører utvalgte kjernemetoder og håndhever ikke relasjonelle garantier. Det finnes verken en egen PostgreSQL-dashboardrunner eller fullgraf-DB-komparator, og faktisk ytelse på store kundegrafer er ikke målt. Tidligere pakke-/cachevarsler er fortsatt til stede.

## 12. Historisk fase-8-sjekkliste for manuell POST_RUN_VERIFICATION

**Historisk kontekst:** Ingen handling nedenfor var autorisert i fase 8. Deler ble senere utført manuelt av brukeren og er dokumentert i fase 9 og §13. Punkter uten senere evidens forblir NOT PERFORMED; denne sjekklisten omskrives ikke som om den var kjørt i fase 8.

### 12.1 Ufarlig kildeinspeksjon før databasearbeid

1. Bruk arbeidskatalogen øverst. Gjennomgå hele diffen inklusive usporede filer som i §11, særlig de tre nye Up/Down-metodene og snapshot.
2. Bekreft at drop kun treffer `GeographicAreaId`, `OrganizationElementId`, deres FK-er/indekser, `geographic_areas` og `organization_elements`. Kontroller at faktisk Organizations-, adresse-, bygg/enhets-, Leasing- og økonomischema bevares. Down gjenoppretter bare schema, ikke tapte klassifiseringsdata.
3. Kjør ved behov de databasefrie kommandoene i §9 på nytt. De er inspeksjon/build/pure tests, ikke databasegodkjenning.
4. Bevar `TenantPlatformDbContextDesignTimeFactory` og dens connection-opening-interceptor. Repoets `scripts/update-database.sh` bruker EF-design-time-fabrikken og vil treffe forbindelsessperren. **Ikke fjern sperren, ikke bruk dette skriptet eller `dotnet ef database update` som fase-9-veien.** Å angi en annen connection string gjør ikke denne fabrikken til runtime-fabrikken. Det finnes ingen separat støttet reset-, migrate-only- eller seed-only-CLI i repoet.

### 12.2 Brukerutført opprettelse, runtime-migrering og demo

1. Bekreft manuelt hos databaseansvarlig nøyaktig host, port, database, schema og rolle for en godkjent disponibel database. Velg rett rolle og isolert miljø før tilkobling; navn fra lokal appkonfigurasjon er ikke godkjenning. Bruk eget administrasjonsverktøy til sletting/gjenskaping **bare etter denne bekreftelsen**. Repoet har ingen sikker generell reset-kommando; derfor gis ingen oppdiktet `dropdb`/reset-kommando.
2. Sett `ConnectionStrings__DefaultConnection` sikkert til denne databasen uten å logge strengen. Runtime-registreringen i `Web/Program.cs` bruker Npgsql-fabrikken med audit, ikke offline-design-time-fabrikken. Ved Web-oppstart kaller `DatabaseInitializer.InitializeAsync` pending-migration/MigrateAsync, så plattformdata, og bare i Development demo. Ingen migrering/seed skjer først etter innlogging; oppstart er i seg selv databaseendrende.
3. Gjør oppstart kontrollert også for bakgrunnsarbeid. Støttede miljønøkler er `InboundEmail__Enabled=false`, `AgreementReminders__WorkerEnabled=false`, `AgreementReminders__TransportMode=Capture`, `LeasingInvoiceInterpretation__Enabled=false`, og `AgreementDocuments__RootPath` til en privat testkatalog utenfor wwwroot. `EmailOutboxWorker` har **ingen enable-bryter**: bruk godkjent isolering av utgående trafikk eller en kontrollert test-SMTP via de faktiske `Email:Host/Port/EnableSsl/Username/Password/FromAddress/FromName/ReplyDomain`-innstillingene. Ikke anta at reminder-flagg deaktiverer outbox eller alle andre hosted workers. `AgreementDocuments:MaxFileSizeBytes` er eksisterende størrelseinnstilling. Ved senere egen reminder-kontroll må `AgreementReminders:ApplicationBaseUrl` være en gyldig test-URL og `AgreementReminders:CapturePath` ligge utenfor wwwroot. Ikke legg hemmeligheter i rapport/logg.
4. For å skille migrasjoner fra demo kan begge `DOTNET_ENVIRONMENT` og `ASPNETCORE_ENVIRONMENT` settes til `Test` (samme verdi, ikke Development). Kjør fra repo-roten, først etter egen autorisasjon:

   ```sh
   dotnet run --project src/TenantPlatform.Web/TenantPlatform.Web.csproj --no-launch-profile --no-build --no-restore
   ```

   Dette starter **hele Web**, migrerer og initialiserer plattformdata; det er ikke en headless migreringskommando. Kontroller logg og migrasjonshistorikk, stopp prosessen kontrollert. Non-Development har eksisterende HTTPS-/appoppførsel. En allerede bygget app er forutsetning; bygg først med kommandoen i §9 ved behov.
5. Kontroller i brukerens DB-verktøy at alle 45 migrasjoner er anvendt i rekkefølge. Inspiser sluttabeller og kolonnetyper, alle nye account-aware FK-er, Restrict-relasjoner, unik indeks for assignments/oversettelser, check required⇒enabled, revisjonskolonner og historikkens jsonb/index. Bekreft fravær av gamle tabeller/kolonner. Test med kontrollerte transaksjoner at cross-account/feil dimensjon, duplikat og ugyldig required-regel avvises; rollback prøvedata. Ikke tolke kun appfeil som bevis på databaseconstraint.
6. Sett begge miljønavn til `Development`, behold den godkjente testforbindelsen og worker-/dokumentoppsettet, og kjør samme runtime-kommando. Nå kalles også `DemoDataInitializer`. Logg fullføring og inspiser kontoer uten å gjengi seed-passord. Ikke bland dette med eksisterende ordinære data.
7. Eksporter/les Magida-kontoens relevante rader og sammenlign **alle felt** mot `unified-dimensions-phase-1/demo-reference.json` og de kontrollerte nye kandidatene: 36 GEO, 30 DEPT, alle ID-er/navn/nivåer/foreldre/stier/order, 300 avtaler, 600 assignments med nøyaktig én av hver og ingen ancestor-rader, 16 motparter, 4/8 indeksdata, 300 indeksvalg og 600 av hver linje-/linjeversjon-/prisversjonsrad. Referansens `$resolvedMagidaUserId`/`$recordedUtc` må sammenholdes med faktisk oppløst bruker og tidspunkt; øvrige felt skal ikke normaliseres bort. Kontroller hvert eksplisitt par og inklusiv nodefordeling, ikke bare antall. Bruk DM/TM som felt-/mappingfasit. **Det finnes ingen klar repo-kommando som utfører denne DB-sammenligningen**; gjennomfør dokumentert manuell eksport/sammenligning eller bestill separat DB-harness. Den rene DemoDimensions-runneren teller ikke som dette steget.
8. Stopp og start Development på nytt. Sammenlign rad-ID-er, antall, revisjoner og innhold før/etter: ingen duplikater og ingen overskriving av eksisterende avtalegrafer. Rediger deretter kontrollerte demoavtaler (tittel/status/eier, endre og fjern valg), metadata/oversettelse/regler og eventuelt arkiver en avtale; start igjen og bekreft bevaring. Test en manglende hel avtalegraf separat; bare den manglende skal tilføyes. Eksisterende avtaler med manglende barn skal etter dokumentert policy ikke repareres. Test kodekollisjon med annen ID og bekreft eksplisitt avvisning uten stille remapping. Test kun i den disponible databasen og registrer den bevisste metadata-/tombstonegrensen.

### 12.3 Brukerutførte PostgreSQL-regresjonsprogrammer

Databasene nedenfor må finnes på den godkjente disponible instansen. Runnerne validerer **eksakt databasenavn**, lager unikt test-schema, bruker Npgsql/MigrateAsync og rydder normalt med DropSchema/CASCADE i finally. De åpner forbindelser og endrer database; ikke kjør dem som databasefrie tester. Host/rolle må fortsatt godkjennes selv om navnet matcher. Kjør sekvensielt, noter exitkode og eventuell manglende opprydding. Tilkoblinger settes sikkert som miljøvariabler, uten å skrive verdiene i terminalhistorikk/rapport.

| Miljøvariabel | Påkrevd databasenavn | Programmer |
|---|---|---|
| `ACCOUNT_REGISTER_TEST_CONNECTION` | `tenant_account_register_tests` | AccountRegisters (inkl Dimensions) |
| `AGREEMENT_TEST_CONNECTION` | `tenant_agreement_tests` | Agreements, AgreementAnalysis, AgreementFollowup, AgreementNotice, AgreementPeriods, AgreementProcessing |
| `LEASING_TEST_CONNECTION` | `tenant_leasing_tests` | Leasing |
| `MEETING_ROOM_TEST_CONNECTION` | `tenant_meeting_tests` | MeetingRooms, valgfri bredere regresjon |

Etter eksplisitt build av prosjektene i §9 er de faktiske kommandoene, fra repo-roten:

```sh
dotnet run --project tests/TenantPlatform.AccountRegisters.SmokeTests --no-build --no-restore
dotnet run --project tests/TenantPlatform.Agreements.SmokeTests --no-build --no-restore
dotnet run --project tests/TenantPlatform.AgreementAnalysis.SmokeTests --no-build --no-restore
dotnet run --project tests/TenantPlatform.AgreementFollowup.SmokeTests --no-build --no-restore
dotnet run --project tests/TenantPlatform.AgreementNotice.SmokeTests --no-build --no-restore
dotnet run --project tests/TenantPlatform.AgreementPeriods.SmokeTests --no-build --no-restore
dotnet run --project tests/TenantPlatform.AgreementProcessing.SmokeTests --no-build --no-restore
dotnet run --project tests/TenantPlatform.Leasing.SmokeTests --no-build --no-restore
dotnet run --project tests/TenantPlatform.MeetingRooms.SmokeTests --no-build --no-restore
```

AccountRegisters inneholder historisk migrasjonsgrense/rename-scenario før slutt-drop og nåværende dimensjons-/konto-/revisjons-/syklus-/samtidighetstester. Agreements har blant annet stale og samtidige edits; AgreementAnalysis bruker fake analyseklient og krever ikke ekstern AI. Leasing inkluderer klassifisering, allocation, faktura/ordre/betaling/livssyklus, budsjettkonflikt og initializer to ganger med Leasing-assertions. Fullgraf-DB-ekvivalens og dynamisk dashboard på PostgreSQL er ikke dekket av en ferdig egen runner.

### 12.4 PostgreSQL: manuelle service-, isolasjons- og konfliktkontroller

1. Bruk minst to kontoer, konto-admin, avtaleeier, lesetildelt bruker, bruker uten tilgang og plattformadmin. Bekreft serveravvisning ved fremmed AccountId/AgreementId/DimensionId/ValueId, ikke bare skjult UI. Bytt konto i langlivet circuit og bekreft riktig avgrensning. Kontroller både katalog, detaljer, historikk, regler og dashboard.
2. Test vanlige create/update og begge analysegrener med required/optional, single/multi, parent/leaf, inactive/unavailable og duplikat-/feildimensjonsinput. Ny required på gammel avtale skal tillate urelatert tittelendring uten autoassign; eksplisitt endret required-sett skal valideres. Utelatt/identisk sett gir ingen dimensjonshistorikk, tomt sett er eksplisitt tømming.
3. Bruk to separate sessions/kontekster med samme revisjon for dimensjon/verdi/regler/avtale. Bare gyldig første commit skal vinne; stale andre får konflikt og ingen delvis endring. Test samtidig reparent, regelendring mot create, og assignment mot verdi-/aktivitet-/availability-endring. Bekreft låserekkefølge, ingen hang og endelig gyldig graf. InMemory-testene erstatter ikke dette.
4. Fremprovoser en operasjon med to dimensjoner hvor siste er ugyldig, og feil i ny/eksisterende analyse. Bekreft på databasen at tittel, assignment-rader, dimensjonshistorikk, revisjon, ny motpart og dokumentpromotering ikke delvis blir stående etter rollback. Skill eksisterende dokument-/lagringsmekanismers forventede oppførsel fra DB-atomisitet. Bekreft audit ved vellykket commit.
5. Kjør dynamisk dashboard via virkelig runtime mot PostgreSQL: alle 66 seednoder og syntetisk fire-nivåtre, shared ancestor/søsken/parent+child, parent-only, direkte-gruppe, manglende bare topp, unik total vs gruppesum og nullsum. Bekreft eksisterende filtre/status/ikke-arkiverte scope og alle fire faste grupper. Test skjult/fremmed node, deaktivert dimensjon og autorisert forfedresti for ikke-admin. Inspiser faktisk querykjøring/antall/ytelse med testmiljøets verktøy; ingen ferdig dashboard-PG-kommando finnes. Ikke aktiver logging av sensitive data.
6. Verifiser kontoslettingssperrer med hver type avhengighet, og at faktisk Organization, adresser, bygg/enheter, avtaleøkonomi, dokumenter, frister/påminnelser og Leasing forblir funksjonelle. Bare disponibelt miljø brukes; mail/outbox og filer må fortsatt være kontrollert.

### 12.5 Browser, layout, språk og tastatur

Alle punkter er **ikke utført** i fase 8. Bruk den separat godkjente runtime-testappen ovenfor; rendering i konsolltest er ikke en browserøkt.

1. **Dimensions:** åpne alle faner og oversettelsesmodal. Test navn/beskrivelse separat i nb-NO/en-GB/sv-SE, kontostandard/manglende tekst/fallbackkode, lagring/fjerning/utelatte språk og stabile ID-er. Test «Bruk i Avtaler»: required aktiverer tilgjengelig; disable fjerner required; Leasing-flagg endres ikke. Kontroller required uten gyldige valgbare verdier og konflikt med input bevart.
2. **Create/edit:** prøv single/multi og alle støttede nivåer/parent/leaf. Bekreft required-merking, hierarkistier, lokale feil og at valg består etter feil. Fjern/tilbakestill et sett og bekreft patchresultat/historikk. Behold og fjern historisk inaktiv/utilgjengelig verdi; fjernet historisk verdi skal ikke kunne tilføyes igjen. Endre tittel på gammel avtale som mangler ny required. Bekreft lesebrukerens detaljer uten edit-kontroller og registrert navn/aktør/tid i historikk.
3. **Analyse:** prøv ny avtale og eksisterende avtale med samme valgmuligheter, katalog-/draftrevisjon og bevaring ved required-feil/stale revisjon. Kontroller motpart, dokumentflyt og øvrige felt. Ekte analyse krever konfigurasjonen `AgreementAnalysis:ApiKey` og `AgreementAnalysis:Model` og særskilt godkjent ekstern behandling; ikke legg inn nøkkel eller send dokumenter som del av fase 8. Den kompilerte PG-testen bruker fake analyseklient.
4. **Konflikter:** to browserfaner med samme revisjon på editor/regel/avtale/analyse. Bekreft klar konflikt, bevart input, ingen stille retry og ingen halvlagret tilstand. Kontroller at senere bevisst reload viser serverens vinner.
5. **Dashboard:** alle faste/dynamiske valg, root→barn→videre→back, parent-only/direkte-gruppe, overlapp, unike totaler/gruppesum/prosent og «Ikke angitt» bare topp. Test refresh med gyldig/ugyldig node, deaktivering, ingen tilgjengelige dimensjoner, raske konto-/dimensjons-/filterbytter og forsinkede svar/feil; bare siste kontekst skal vises og gamle tall skal forsvinne straks.
6. **Visuell og tilgjengelighet:** gjenta relevante flyter på norsk, britisk engelsk og svensk; smal og bred skjerm, lys og mørk visning. Kontroller at lange lokaliserte navn/stier ikke bryter layout. Bruk bare tastatur gjennom faner, checkbox/radio/select, drill/back, modal, feilmelding og lagre/avbryt; kontroller fokusrekkefølge, synlig fokus, labelkoblinger, feilmeldingers lesbarhet og retur av fokus. Vurder skjermleser på de faktiske native kontrollene.
7. Registrer PASS/FAIL og avvik per punkt med miljø/browser/rolle. Samlet aksept må vente på både PostgreSQL-portene og browserkontrollene. Stopp etter fase-8-leveransen her; sjekklisten er kun overlevering.

## 13. Senere fase-9-evidens og endelig godkjenning

Denne delen er den autoritative senere oppdateringen til de historiske fase-8-formuleringene i §1–12. Den utvider bare påstander der fase 9 har konkret evidens. [Fase-9-rapporten](unified-dimensions-phase-9.md) inneholder detaljer, hasher og avgrensning.

### 13.1 Faktisk PostgreSQL- og demoverifikasjon

Brukeren gjenskapte den valgte disponible utviklingsdatabasen og utførte runtime-kjøringene. Agenten koblet ikke til databasen. Følgende er dokumentert:

- Migrasjonshistorikken inneholdt 45 migrasjoner med siste migrasjon `20261112104542_RemoveLegacyAgreementClassifications`. Kontrollert sluttschema omfattet de nye tabellene, kolonnene, typene, nøklene, indeksene, check-constraintene og account-aware FK-ene; den supplerende assignment→agreement-FK-en var validert og hadde ON DELETE RESTRICT. Dette er metadataevidens, ikke negative tester av alle constraints.
- Development initialiserte faktisk Magida-demoen. GEO/DEPT hadde 36/30 verdier, alle 300 avtaler hadde forventet GEO/DEPT-par før redigering, og 66/66 noder samsvarte i struktur og direkte/inklusive fordelinger. Økonomikontrollen fant 4 indekser, 8 indeksverdier, 300 indeksvalg og 600 rader av hver kontrollerte linje-/linjeversjons-/prisversjonstype.
- Feltvis eksport mot den frosne referansen samsvarte for eksporterte forretningsdata, med to eksplisitte runtimeforskjeller per avtale: `DeadlinesInitialized=true` og ny revisjon for alle 300. Disse 600 forskjellene ble ikke normalisert bort eller kalt seed-identiske.
- Gyldig gjentatt initialisering uten redigering brukte to eksporter på 1 702 780 byte med samme SHA-256 `e0bd51f8ade6d664dbd4563741143c28f451765b215a5d6cfd1b9b29ec25aa6a`: 15 samlinger og 3103 rader var byteidentiske før/etter.
- Gyldig redigeringsscenario endret MAG-001-tittelen til `MAG-001 Leieavtale – Tokyo IDEMPOTENSETEST`, fjernet GEO eksplisitt og beholdt DEPT. Før-/ettereksportene var begge 1 702 514 byte med SHA-256 `87a57e4c21fdb0822089ee80d3715fcd08f1e9f4eb323cae98be6d3c199095f4`: 15 samlinger, 3102 rader og 599 tildelinger var byteidentiske etter ny initialisering.
- Et tidligere eksportpar ble tatt på hver side av gjenskaping/tømming/gjenoppretting og er uttrykkelig ikke brukt som idempotensbevis.

Eksportene dekker Magida-kontoen og 15 kontrollerte samlinger, ikke hele Nordic Property-, Leasing-, audit-, outbox- eller fristgrafen. Antall alene er ikke brukt som fullgrafbevis.

### 13.2 Faktisk PostgreSQL-regresjon

| Runner | Dokumentert resultat | Evidensgrense |
|---|---|---|
| AccountRegisters | PASS; 41 `PASS:`-kontroller; exitkode 0 | Eget testschema i `tenant_account_register_tests`; dekker blant annet full kjede, historisk rename, register-/konto-/revisjons-/hierarkiscenarioer, ett samtidig reparent-scenario og én direkte FK-avvisning. Ikke generell concurrency-/constraintverifikasjon. |
| Agreements | PASS; 78 `PASS:`-kontroller; exitkode 0 | Eget testschema i `tenant_agreement_tests`; dekker blant annet migrasjon, tilgang, avtaleedit, ett samtidig edit-scenario, dokumentvalidering og én konkret upload-feilsti samt utvalgte dimensjons-/FK-scenarioer. Ikke full rollback-/økonomi-/livssyklusregresjon. |

Loggene som ble vurdert er `/Users/morten/Downloads/AccountRegisters.g5PQOXiw` (SHA-256 `992580eb8c9db7d90c91c5fea70bb01172a9facf1c501ead608ac12033783586`) og `/Users/morten/Downloads/Agreements.TUTaZru4` (SHA-256 `ddfbf17250a14a0d10a553a0ae3fadc9f365c4bd0b992394a606f9c418d5c22f`). Ingen andre PostgreSQL-runnere regnes som kjørt.

### 13.3 Siste editorrettelse og manuell UI-verifikasjon

Den avgrensede rettelsen berørte:

- `src/TenantPlatform.Web/Components/Pages/Dimensions/DimensionEditorDialog.razor`;
- `src/TenantPlatform.Web/Components/Pages/Dimensions/DimensionEditorDialog.razor.css`;
- `tests/TenantPlatform.Dimensions.SmokeTests/ComponentChecks.cs`;
- `docs/features/unified-dimensions-phase-9.md`.

Editoren viser Norsk, English og Svenska i hver sin fane, med bare navn/beskrivelse for aktivt språk. Fallbacknavn/-beskrivelse og globale innstillinger er felles. Redigeringsmodellen bevares ved fanebytte; faneknappene er ikke submit-knapper. Modalens flex-/overflow-layout begrenser høyden, ruller innholdet og holder Lagre/Avbryt i egen synlig bunnrad. Aktiv fane og input beholdes etter valideringsfeil, og skjult språkfeil markerer fanen. Fallback-/lagringsmodell, tilgang, revisjonsvern og oversettelsespatching er uendret.

Før avslutningsrunden ble solution og målrettet Dimensions-prosjekt bygget med 0 feil og de to eksisterende NU1902/NU1903-varslene. Den databasefrie Dimensions-runneren passerte 159 kontroller, inklusive de nye komponentkontrollene; `git diff --check` passerte. Disse kontrollene ble ikke kjørt på nytt under rapportavslutningen.

Brukeren utførte og godkjente manuell kontroll av språkfaner/inputbevaring, scrolling ved lavt/smalt vindu, synlige handlinger, lagring og gjenåpning av alle språk, lokaliserte navn i kontrollerte visninger, Avbryt uten lagring, valideringsfeil med bevart input, fallback samt tastaturnavigasjon og synlig fokus. Brukerens generelle tilbakemelding er at grensesnittet ser bra ut. Dette er ikke en systematisk test av alle browserflyter, roller, størrelser, nettlesere eller tilgjengelighetsteknologier.

### 13.4 Endelige begrensninger

Følgende har status **NOT PERFORMED — bruker har valgt å avslutte videre verifikasjon**:

1. AgreementAnalysis, AgreementFollowup, AgreementNotice, AgreementPeriods, AgreementProcessing, Leasing og MeetingRooms som PostgreSQL-runnere.
2. Generell/fullstendig concurrency-, deadlock- og rollbackverifikasjon utover de konkrete scenarioene i AccountRegisters og Agreements.
3. Full PostgreSQL-dashboardverifikasjon, faktiske dynamiske dashboardqueries og ytelsesmåling.
4. Full systematisk browser-/tilgjengelighetsmatrise på tvers av alle flyter, roller, skjermstørrelser, nettlesere, lys/mørk visning og skjermleser.
5. Full dekning av required/multiple/leaf-only/historikk, alle provider-constraints, fysisk kontosletting, parallelle/avbrutte initialiseringer og full analyse-/dokumentrollback.
6. Databasebasert fullgrafkontroll utenfor de 15 eksporterte Magida-samlingene.

Historiske NOT PERFORMED-resultater uten senere evidens er ikke erstattet med PASS. Fase 8 forblir den databasefrie kjøringsrapporten; fase 9 er kronologisk senere verifikasjon.

### 13.5 Sluttstatus

**IMPLEMENTATION COMPLETE — godkjent av bruker med dokumenterte verifikasjonsbegrensninger.** Kildeimplementasjonen og den siste editorrettelsen er ferdige. Faktisk verifikasjon er begrenset til det som er eksplisitt dokumentert ovenfor og i fase 9. Videre testing er avsluttet etter brukerens valg; dette omklassifiserer ikke uutførte kontroller til PASS.
