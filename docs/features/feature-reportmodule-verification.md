# Rapportmodulen — Steg 9 samlet verifikasjon

Dato: 13. november 2026. Status: **DELVIS GODKJENT VERIFIKASJON — kildeimplementasjonen og de providerkritiske hovedløpene består, men hele akseptanse-/browser-/ytelsesmatrisen er ikke fullført.**

Dette er evidensrapporten for Steg 9. Historiske databasefrie resultater i implementasjonsplanen er ikke omskrevet til PostgreSQL-PASS; denne rapporten dokumenterer den senere runtimekjøringen.

## 1. Isolert miljø

- macOS 26.5 arm64, .NET SDK 10.0.302/runtime 10.0.10.
- PostgreSQL 17.10 fra Postgres.app i en ny `initdb`-klynge under en oppgavespesifikk tempkatalog, kun på `127.0.0.1:53615`. Effektiv database/bruker/port ble kontrollert før skriving. Ingen eksisterende PostgreSQL-instans eller utviklingsdatabase ble brukt.
- Rapportdatabase: `tenantplatform_report_step9`. Agreements- og Leasing-runnerne brukte henholdsvis `tenant_agreement_tests` og `tenant_leasing_tests` i samme disponible klynge.
- Google Chrome 157.0.8103.78, headless. Faktisk app ble publisert til tempkatalog og startet i `Test` på `127.0.0.1:53616`; dette sikret at Blazor/static assets kom fra publish-output uten launch-profiler eller Development-user-secrets.
- IMAP og Agreement reminder worker var deaktivert. SMTP pekte til en ubrukt loopback-port uten legitimasjon. Dokumentlageret var en separat tempkatalog. Ingen reelle meldinger eller eksterne skriveoperasjoner ble utført.
- Testinnlogging brukte en syntetisk konto og et flyktig passord som aldri er skrevet i rapport eller logg.

En tidlig oppstart med `dotnet run` lastet repoets Development-launchprofil og demo-initialisering i den disponible databasen. Prosessen ble stoppet, og endelig browserkjøring brukte publisert `Test`-output med eksplisitt content root. Dette berørte bare testklyngen.

## 2. PostgreSQL, migrasjon og persistence

`TenantPlatform.Reports.SmokeTests` har fått eksplisitt Npgsql-modus via `REPORT_TEST_CONNECTION`. Runneren nekter andre databasenavn enn `tenantplatform_report_step9`.

Resultat: **PASS, 288 kontroller** mot PostgreSQL.

Verifisert:

- komplett migrasjonskjede på tom database til `20261112212606_AddReportPages`;
- ingen ventende modellendringer;
- down til forrige migrasjon fjerner rapporttabellene, og up oppretter dem igjen;
- reell `jsonb`-type og semantisk roundtrip for filter/display;
- rapportkortets check constraints og konto-sammensatte FK-er;
- oppretting, navneendring, flytting mellom sider, dyp duplisering med nye ID-er/ny eier og sletting;
- stale revision og mislykket multi-page save uten delvis endring;
- to samtidige cross-page saves på separate context-er og motsatt inputrekkefølge: én vinner, én får konflikt, og begge sider ender fra samme vinner. Dette verifiserer den sorterte `FOR UPDATE`-stien uten stille overskriving;
- Agreements-/Leasing-filter, tenant-/ressursscope, rapportkjøring, valuta, grain, perioder, paging, størrelsesgrenser og progressive trequeries gjennom faktisk Npgsql-oversettelse.

En særskilt injisert databasefeil midt etter at domenemutasjonene er anvendt i `SaveWorkspaceAsync` ble ikke fremprovosert. Full rollback er verifisert for stale/validation/concurrency-feil før commit og ved at alle berørte sider forblir konsistente; vilkårlig providerfeil etter mutation er **IKKE TESTET**.

## 3. Avstemming og datamengde

Den samme deterministiske fixturen som databasefri Reports-smoke ble kjørt på PostgreSQL. Den omfatter to rapportkontoer, admin/eiere/lesere med ulik tilgang, Agreements med advance/arrears, gratisperiode, indeks, linje-/prisversjoner og NOK/EUR/GBP, Leasing med ramme/frittstående/underliggende anskaffelser, kreditering/reversering/frigivelse, hierarkiske/flerverdige/historiske/ikke-angitte dimensjoner og perioder over kalendergrenser.

Rapportkontrollene avstemte blant annet 1 155,48 NOK fakturadatobasert Agreement-inntekt, 1 200 EUR arrears, 100 GBP kostnad og Leasing original/justert/kapasitetsverdier mot de eksisterende kalkulatorene. Flere valutaer forble separate, mixed-currency KPI ble avvist, og distinct total ble beregnet før flerverdig gruppeutvidelse.

Etter scope-limit-fixture og isolert demooppstart inneholdt databasen 4 kontoer, 305 avtaler, 603 avtale-linjer, 604 prisversjoner, 8 rammeavtaler, 1 014 anskaffelser, 19 varelinjer, 40 utstyrsposter, 8 dimensjoner og 89 dimensjonsverdier. Scope-limit-testen viste eksplisitt feil uten stille trunkering over 1 000 Leasing-anskaffelser.

Lokale `EXPLAIN (ANALYZE, BUFFERS)`-stikkprøver:

- rapportsideoversikt for fixturekonto: 7 sider, `AK_report_pages_AccountId_Id` og `IX_report_cards_AccountId_ReportPageId_SortOrder`, 0,050 ms execution;
- distinct agreement count for fixturekonto: kontoindeksert scan, 0,015 ms execution.

Dette er små, lokale målinger og ingen produksjonsgaranti. Full query-count-instrumentering, N+1-profil for alle kombinasjoner, representative store trær, klientpayload og bred queryplanmatrise ble ikke gjennomført.

## 4. Faktisk app og nettleser

Den nye `browser/run-app-browser.mjs` bruker installert Chrome via lokal DevTools-pipe, ekte cookieinnlogging, publisert Blazor InteractiveServer, den lokale ECharts-distribusjonen og PostgreSQL. Den kopierer ikke rapport- eller diagramlogikk.

**PASS, 25 kontroller per viewport**:

- desktop: forespurt `1440×1000`, faktisk CSS-viewport `1440×913`;
- smal: forespurt `390×844`, faktisk CSS-viewport `500×757` (Chrome-minimum, fortsatt under 768 px-breakpointet).

Verifisert i faktisk app:

- innlogging og `/reports`;
- direkte `/reports/{guid}`, refresh og stabil bokmerke-URL;
- visningsmodus som standard, overgang til redigeringsmodus og Avbryt uten persistence;
- faktisk rapportkjøring mot PostgreSQL, ECharts-canvas og tilgjengelig tabellalternativ;
- oppretting av rapportside, ny GUID-URL, navneendring, save og uendret URL;
- smal viewport for samme kjerneløp.

Den databasefrie browserharnessen bestod i tillegg 50 kontroller ved `1440×913` og 51 ved `500×757`: bar/line/donut/tree, tooltips/spesialtegn, manglende/tomme verdier, tema/reduced motion, resize/dispose, drag/resize/collision/Escape/pointercancel, tastaturalternativ og responsivt grid. JS-modulharnessen bestod også.

Følgende er ikke dekket av den faktiske appharnessen: dupliser/slett-dialog i UI, to samtidige browserøkter, native beforeunload-dialog, circuit disconnect/reconnect, full card-buildermatrise, faktisk tree-søk/automatisk utvidelse/detaljpanel, full nb-NO/en-GB/sv-SE browsermatrise, skjermleser, fysisk touch/stylus og andre nettlesere. De databasefrie testene gir relevant kontrakt-/renderingsevidens, men erstatter ikke disse runtimekontrollene.

## 5. Påviste feil og rettelser

1. **Produksjonsfeil — uendret `jsonb` ble oppfattet som endret.** PostgreSQL normaliserer whitespace i `jsonb`. `ReportPageService.SameDefinition` brukte tekstlikhet, slik at et uendret kort kunne bli avvist etter tapt modultilgang. Filtre og display sammenlignes nå strukturelt med `JsonElement.DeepEquals`. PostgreSQL-regresjonen «uendret kan bevares, endret kan ikke omgå tilgang» består.
2. **Testfixture — ugyldige FK-er skjult av InMemory.** Fremmed dimensjonsverdi og syntetiske Leasing-anskaffelser manglet gyldig dimension-/organization-referanse. Fixturen er korrigert; ingen produksjonsregel ble svekket.
3. **Testforventning — `jsonb`-tekst.** Deep-copy-testen sammenligner display-JSON semantisk i stedet for byteformat.
4. **Harnessoppsett.** Faktisk browserharness må starte publisert output fra publish-katalogen. En upublisert `Test`-host serverte ikke development static assets. Dette var ikke en produktfeil.

## 6. Regresjonsresultater

| Kontroll | Resultat |
|---|---|
| Reports smoke, databasefri | PASS — 278 |
| Reports smoke, PostgreSQL | PASS — 288 |
| Agreements PostgreSQL smoke | PASS, exit 0 |
| Leasing PostgreSQL smoke | **FAIL**, exit 134; se under |
| AgreementDimensions | PASS — 166 |
| Dimensions | PASS — 228 |
| Dashboard | PASS |
| JS-modulharness | PASS |
| Isolert statisk Chrome-harness | PASS — 50 desktop / 51 smal |
| Faktisk app/InteractiveServer Chrome | PASS — 25 desktop / 25 smal |
| `dotnet build TenantPlatform.sln --no-restore` | PASS — 0 feil; eksisterende NU1902/NU1903 består |
| `git diff --check` | PASS |

Leasing-runneren feilet i en eksisterende kostnadsfordelingsforventning: en frittstående anskaffelse opprettes av testhelperen med 0 % VAT, mens kontrollen forventer 25 i hver rad. Persisted data viser quantity 2, unit price 100 og VAT percent 0; kontrollen forventer net 100 og VAT 25. Feilen ligger etter de rapportrelevante access/filter/concurrency/UI/migrasjonskontrollene og er ikke forårsaket av rapportendringene. Testen ble ikke endret eller gjentatt for å få PASS. Feillogg og schemasnapshot ble bevart under diagnose; oppgavens disponible ressurser ryddes samlet etter rapportering.

## 7. Akseptansematrise

| # | Status | Evidens |
|---|---|---|
| 1 | PASS | Typed katalog/filterparitet og begge rapportmoduler består på Npgsql. |
| 2 | PASS | Katalog- og valideringstester avviser/utelater property/building/unit/location. |
| 3 | PASS | Uavhengig periode/tidsgruppering i smoke og Npgsql-kjøring. |
| 4 | PASS | Alle relative regler, årsskifte, leap year og lokal tid er testet med fast klokke. |
| 5 | PASS | `jsonb`-/layout-roundtrip, reload og faktisk browser-refresh. |
| 6 | DELVIS | Service/workspace dekker kort/sider, deep copy og cross-page move; ikke hele flyten i faktisk browser. |
| 7 | PASS med avgrensning | Full arbeidskopi/cancel i smoke og faktisk rename-cancel; cross-page cancel er komponenttestet, ikke browserkjørt. |
| 8 | DELVIS | PG treequeries og browserens isolerte treeharness består; faktisk app tree-søk/path-utvidelse ikke kjørt. |
| 9 | DELVIS | Typed detaljer/ruter og isolert render består; faktisk app åpneknapper for begge moduler ikke kjørt. |
| 10 | PASS | Npgsql-tester for konto, medlemskap, module/resource scope og direkte servicekall. |
| 11 | DELVIS | Agreements PASS og målrettede modultester PASS; full Leasing-runner har den dokumenterte ikke-rapportrelaterte feilen. |
| 12 | PASS | Ekte browser: direkte GUID-URL, rename og refresh beholder URL. |
| 13 | DELVIS | Nye ID-er, eier, delete/unavailable er Npgsql-testet; komplett duplicate/delete-flow ikke kjørt i faktisk browser. |
| 14 | PASS | Ingen startsidefunksjon eller endring av eksisterende startside er implementert. |

Rapportmodulen er derfor **klar for neste leveransesteg med dokumenterte begrensninger**, men ikke korrekt beskrevet som «alle akseptansekriterier fullstendig verifisert».

## 8. Reproduserbare kommandoer

Kommandoene forutsetter en eksplisitt disponibel database og egne lokale verdier. Ikke legg passord i kommandolinjen; bruk skjult miljøinnlasting eller passordfil med restriktive rettigheter.

```bash
REPORT_TEST_CONNECTION='<disponibel PostgreSQL-tilkobling til tenantplatform_report_step9>' \
  dotnet run --project tests/TenantPlatform.Reports.SmokeTests --no-restore

node tests/TenantPlatform.Reports.SmokeTests/js/report-modules.test.mjs
node tests/TenantPlatform.Reports.SmokeTests/browser/run-browser-harness.mjs

REPORT_APP_ORIGIN='http://127.0.0.1:<ledig-port>' \
REPORT_BROWSER_EMAIL='<syntetisk-testbruker>' \
REPORT_BROWSER_PASSWORD="$(</sikker/midlertidig/passordfil)" \
REPORT_BROWSER_PAGE_ID='<fixture-side-guid>' \
  node tests/TenantPlatform.Reports.SmokeTests/browser/run-app-browser.mjs
```

Appharnessen forutsetter en allerede startet, isolert appinstans og browser-fixture. Den oppretter rapportsider i den disponible databasen.
