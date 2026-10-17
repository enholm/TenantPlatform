# Autorisert avtaleoversikt

Kjør fra repository-roten:

```sh
dotnet run --project tests/TenantPlatform.AgreementList.SmokeTests
```

Standardkjøringen bruker EF InMemory og testaktør, uten HTTP, Blazor-krets, nettverkskall eller applikasjonsdatabase. Den kontrollerer Application-orkestrering, EF-adapterens tenant-/eier-/grantfiltre, paging, eieralternativer, tidssoner, tilbakekalling og Web-fasadens delegering. Produksjonens auditinterceptor er aktiv; en egen interceptor avviser SaveChanges under listekall. Npgsql-oversettelse av Attention kontrolleres med `ToQueryString` uten å åpne forbindelse. Arkitekturtesten kontrollerer at Application ikke har assembly-avhengighet til EF, ASP.NET, Infrastructure eller Web.

Hvis `AGREEMENT_TEST_CONNECTION` er eksplisitt satt, kjøres samme suite også mot PostgreSQL. Runneren avviser andre databasenavn enn `tenant_agreement_tests`, oppretter et unikt `agreement_list_*`-skjema, kjører eksisterende migreringer i dette skjemaet og sletter det i `finally`. Bruk aldri applikasjonsdatabasen. Ingen ekstern modell eller e-posttransport brukes.

InMemory beviser ikke relasjonelle constraints, Npgsql-kjøring eller låseoppførsel. Uten konfigurert testforbindelse rapporteres PostgreSQL-kjøringen som SKIP. Eksisterende dashboardtester dekker ytterligere Attention-, DST- og visningsregresjoner.

## Lokal PostgreSQL-verifikasjon uten eksisterende database

Forutsetninger: .NET 10 og PostgreSQL-serververktøyene `initdb`, `pg_ctl` og `createdb`. På denne maskinen brukes PostgreSQL 17.10 fra Postgres.app. Docker er ikke nødvendig; den lokale Docker-daemonen var ikke tilgjengelig ved verifikasjonen. Ikke kjør skriptet som root/sudo.

Fra repository-roten:

```sh
bash scripts/test-agreements-postgres.sh
```

Denne ene kommandoen starter miljøet, kjører testene og stopper/sletter testinstansen etterpå. Den gjør følgende:

1. Oppretter en ny, privat midlertidig datamappe og Unix-socket under `/tmp` med tilgang bare for den lokale brukeren.
2. Initialiserer en separat PostgreSQL-klynge og oppretter `tenant_agreement_tests`. Eksisterende utviklings-/produksjonsinstanser og `docker/.env` brukes ikke.
3. Deaktiverer TCP-lytting og avviser host-autentisering. Passordfri lokal autentisering er begrenset til socketen under den private mode-700-mappen. Ingen passord lagres i repoet eller behøves for dette miljøet.
4. Setter `AGREEMENT_TEST_CONNECTION` bare for skriptets underprosesser. Det erstatter eventuell eksisterende verdi med tilkoblingen til den nye testinstansen.
5. Kjører `TenantPlatform.AgreementList.SmokeTests` (InMemory **og PostgreSQL**), `TenantPlatform.Agreements.SmokeTests` (PostgreSQL) og `TenantPlatform.Dashboard.SmokeTests` (InMemory/SQL-generering).
6. Stopper instansen og fjerner hele den eide klyngen ved avslutning, også ved testfeil. Hvis stopp feiler, beholdes filene med en feilmelding. Hver runner beholder sine databasenavn-/skjemasjekker; ingen sikkerhetssjekker er fjernet. Den eldre Agreements-runneren etterlater selv sitt skjema, som også blir fjernet sammen med den midlertidige klyngen.

Hvis verktøyene ikke finnes på PATH, angi katalogen eksplisitt:

```sh
POSTGRES_TEST_BIN=/Applications/Postgres.app/Contents/Versions/latest/bin \
  bash scripts/test-agreements-postgres.sh
```

Bare avtaleoversiktens suite:

```sh
bash scripts/test-agreements-postgres.sh TenantPlatform.AgreementList.SmokeTests
```

Skriptet støtter nå alle elleve eksisterende smoke-testprosjekter via en eksplisitt allowlist. Standardutvalget er fortsatt de tre over. De fem andre `TenantPlatform.Agreement*.SmokeTests` bruker også `tenant_agreement_tests`/`AGREEMENT_TEST_CONNECTION`. For register, møterom og leasing oppretter skriptet egne databaser **i sin nye, private klynge** og setter henholdsvis:

| Prosjekt | Database | Variabel |
|---|---|---|
| `TenantPlatform.AccountRegisters.SmokeTests` | `tenant_account_register_tests` | `ACCOUNT_REGISTER_TEST_CONNECTION` |
| `TenantPlatform.MeetingRooms.SmokeTests` | `tenant_meeting_tests` | `MEETING_ROOM_TEST_CONNECTION` |
| `TenantPlatform.Leasing.SmokeTests` | `tenant_leasing_tests` | `LEASING_TEST_CONNECTION` |

Alle runnernes eksisterende databasenavn-/skjemakrav er beholdt. Andre prosjektnavn avvises før oppsettet starter. Eksempel på regresjonskjøring av Web-fasaden:

```sh
bash scripts/test-agreements-postgres.sh \
  TenantPlatform.AccountRegisters.SmokeTests \
  TenantPlatform.MeetingRooms.SmokeTests \
  TenantPlatform.Leasing.SmokeTests
```

Øvrige avtaletester kan kjøres på samme måte ved å angi `TenantPlatform.AgreementAnalysis.SmokeTests`, `TenantPlatform.AgreementFollowup.SmokeTests`, `TenantPlatform.AgreementNotice.SmokeTests`, `TenantPlatform.AgreementPeriods.SmokeTests` og `TenantPlatform.AgreementProcessing.SmokeTests`. Analyse bruker eksisterende mock-transport, ikke en ekstern modell.

Ingen separat appstart eller `update-database.sh` skal kjøres. De eksisterende migreringene anvendes av testene bare i deres isolerte skjemaer. Skriptets exitkode er forskjellig fra null dersom oppsett eller en test feiler.

## Faktisk resultat 17. oktober 2026

Standardkommandoen over bestod med PostgreSQL 17.10. Avtaleoversiktens suite kjørte hele adaptertesten mot PostgreSQL, uten SKIP: kontogrenser, eier-/Read-/Edit-grants, manglende/inaktivt medlemskap, tilbakekalling, søk/type/status/eierfilter, `TotalCount`, eieralternativer, arkivsemantikk, Attention-regler, lagret/default tidssone, 90-dagersgrenser, stabil sortering/paging, parallelle lesekall og fravær av skriveroperasjoner. Eksisterende Agreements-suite bestod også, inkludert migreringssnapshot, migreringskjede, deling og databaseconstraints. Dashboard-regresjonene bestod i sitt eksisterende InMemory-/oversettelses-/komponentoppsett; de skal ikke omtales som PostgreSQL-utførte dashboardtester.

Ingen feil fra Application-uttrekket ble avdekket som krevde kodeendring. Eksisterende NU1902/NU1903-varsler for `System.Security.Cryptography.Pkcs` rapporteres fortsatt; ingen pakkeversjon er endret.

## Steg 3: felles autorisasjon

`AuthorizationChecks.cs` utvider den samme suiten med rettighetskatalog/rollemapping, ressursomfang, rollekombinasjoner, global PlatformAdmin, eierskap/Read/Edit/deling, aktiv bruker/medlemskap og tilbakekalling ved neste kall. Den bruker samme evaluator uten HTTP og Web-fasaden, både med InMemory og faktisk PostgreSQL. Ingen nye testprosjekter eller database-sikkerhetsunntak er nødvendige. Se avsnitt 13 i moderniseringsplanen for faktisk verifikasjon og manuelle GUI-kontroller.

## Steg 4A: dashboardets Attention-tellinger

`AttentionChecks.cs` kjører den nye `AgreementAttentionService` og `AgreementAttentionData` i samme InMemory- og PostgreSQL-fixture som liste-/autorisasjonstestene. Standardkommandoen over kjører dermed også reell SQL-aggregering for dashboardtellingene. Den kontrollerer alle kategorier, eier/Read/Edit/admin og kontogrenser, tilbakekalling, status-/arkiv-/dato-/tidssoneregler, ett aktørsnapshot/referansetidspunkt, skrivevern og samsvar med filtrert listes `TotalCount`. Web-fasadens nøyaktige delegering kontrolleres separat; eksisterende Dashboard-suite tester komponentrendering, ikke interaktiv GUI.

Ingen nye variabler, databasenavn eller sikkerhetsunntak trengs. Se moderniseringsplanens avsnitt 14 for tellesemantikk, faktisk testresultat og GUI-sjekkliste.

## Steg 4B: prioriterte dashboardavtaler

`DashboardChecks.cs` kjører `AgreementDashboardService`/`AgreementDashboardData` i samme InMemory- og PostgreSQL-oppsett. Standardkommandoen over dekker dermed også ekte SQL-union, deduplisering, sortering og femgrense etter tilgangsfilter. Egne fixtures kontrollerer at skjulte toppkandidater ikke fortrenger synlige treff, og dekker konto/eier/Read/Edit/admin, stabile ID-ties, dato/status/arkiv, Renewal alene, tidssone/DST, tilbakekalling og skrivevern. Snapshot- og fasadetester bruker eksisterende testaktør uten HTTP/Blazor. Se planens avsnitt 15 for eksakt rekkefølge og manuelle GUI-kontroller.

## Steg 4C: avtaleinnsikt og delt hierarki

`InsightsChecks.cs` kjører alle seks dimensjoner gjennom `AgreementInsightsService`/`AgreementInsightsData` i både InMemory og PostgreSQL. Standardkommandoen dekker ekte SQL-grupperinger, kontobegrensede eier-/motpartsjoins og nodeaggregater. Tester verifiserer hierarkisk drilldown, metadataavgrensning, tomme/ugyldige nodevalg, stable tie-breaks, snapshots/token, tilbakekalling og skrivefrihet. Inkonsistente manglende referanser/ukjente enumverdier simuleres i InMemory; PostgreSQL-constraints svekkes ikke.

Den rene `AccountRegisterHierarchy.Build` er flyttet til Application og deles med registersidene og avtalenes strukturvalg. Kjør derfor også registerregresjonene:

```sh
bash scripts/test-agreements-postgres.sh TenantPlatform.AccountRegisters.SmokeTests
```

Øvrige berørte avtale-composition-roots kjøres med de allerede dokumenterte prosjektvalgene. Se planens avsnitt 16 for faktisk resultat, datamengde, MCP-status og GUI-sjekkliste.

## Steg 4D: skrivefri detaljlesing og separat GUI-backfill

`DetailsChecks.cs` kjører `AgreementDetailsReader`/`AgreementDetailsData` gjennom samme isolerte PostgreSQL-oppsett. Direkte lesinger har SaveChanges-vakt og må ikke endre settings, frister eller initialiseringsflagget. Separate tester av Webs `GetAsync` tillater dens eksplisitte system-backfill, kontrollerer ressursautorisasjon før skriver, begrensning til valgt avtale, idempotens og PostgreSQL-samtidighet. Snapshot-, metadata-, handlingsflagg-, tilbakekallings- og notice-komponenttester inngår også.

Kjør standardkommandoen som før. Den nye readeren er skrivefri; Web-fasaden og oppfølgingspanelets separate kall kan fortsatt skrive. Se moderniseringsplanens avsnitt 17 for det presise skillet og GUI-sjekklisten.

## Steg 4E: oppfølgingsreader og samspill med detaljfasaden

`FollowupChecks.cs` tester `AgreementFollowupReader`/`AgreementFollowupData` med samme InMemory-/PostgreSQL-fixture og SaveChanges-vakt. Den dekker roller og ressursinnsyn, frist-/historikk-/leveringsmetadata, manglende initialisering, dato/tidssone/DST, snapshots/token og tilbakekalling. Separate GUI-tester kontrollerer at `GetFollowupAsync` og `GetAsync` bare initialiserer valgt avtale, også samtidig, og forblir idempotente. Panelet renderes for både Read og eier. Read får ikke leveringskøoppsummeringer, og ingen roller får rå transport-ID eller lagret adressetekst.

Kjør standardkommandoen og de dokumenterte avtale-/oppfølgings-/notice-/processingregresjonene. Ingen nye databasenavn, konfigurasjonshemmeligheter eller sikkerhetsunntak trengs. Se planens avsnitt 18 for faktisk resultat og nødvendige kontra valgfrie steg før en første MCP-lesehost.
