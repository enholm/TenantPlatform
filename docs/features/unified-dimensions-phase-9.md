# Unified Dimensions — fase 9: manuell PostgreSQL-verifikasjon

**Status: IMPLEMENTATION COMPLETE — godkjent av bruker med dokumenterte verifikasjonsbegrensninger.** Kildeimplementasjonen er ferdig. Faktisk fase-9-verifikasjon omfatter runtime-migrering/sluttschema, avgrensede demoeksporter og idempotensscenarioer, PostgreSQL-runnerne AccountRegisters og Agreements, samt brukerens manuelle kontroll av den siste dimensjonseditoren. Øvrige kontroller er ikke utført etter brukerens beslutning om å avslutte videre verifikasjon; alle akseptansekriterier er derfor ikke påstått fullt verifisert.

Dato for denne oppdateringen: 12. november 2026. Arbeidskatalog: `/Users/morten/Dev/TenantPlatform`. Branch: `main`, HEAD `f2f2d67338fd05e59624423aad675ec4f8ca415d`. Tidligere, ikke-committet fasearbeid er bevart. [Sluttrapporten](unified-dimensions-final-report.md) og [fase 8](unified-dimensions-phase-8.md) beholdes som historiske rapporter; denne rapporten fører senere faktisk evidens.

## 1. Utført og ikke utført

Databasehandlinger, runtime-oppstarter, redigering og eksport nedenfor er utført av brukeren. Agenten har analysert brukerens resultater/filer lokalt, uten databaseforbindelser. Under denne forberedelsen er bare kildelesing, en avgrenset testinngangsendring for diagnose, kompilering og dokumentasjon utført. `tenantplatform_dev` er ikke koblet til eller endret av agenten.

| Kontroll | Faktisk status og omfang |
|---|---|
| Migrasjonshistorikk/sluttschema i appdatabasen | PASS for de kontrollerte kravene: brukeren rapporterte 45 migrasjoner, siste `20261112104542_RemoveLegacyAgreementClassifications`; tabeller, kolonner, typer, nøkler, FK-er, check og indekser er gjennomgått mot levert resultat. Metadata er ikke negative constraint-tester. |
| Assignment → avtale-FK | PASS etter supplerende resultat: `(AccountId, AgreementId)` → `agreements(AccountId, Id)`, validert, ON DELETE RESTRICT. Første eksport utelot denne raden; det er ikke et gjenværende schemafunn. |
| Tom database etter bare Test-oppstart | Ikke videreført som krav: brukeren bekreftet at databasen med vilje var fylt med demo og instruerte videreføring. En ren Test-kjøring uten demo på tom database er ikke hevdet bevist. |
| GEO/DEPT-oppsett | PASS, brukerutført SQL: aktive, single-select, LeafOnly=false, tilgjengelige/valgfrie i Agreements, ingen Leasing-regel; 36/30 verdier. |
| 300 avtaler / 600 tildelinger | PASS før brukerredigering: én GEO og én DEPT per avtale; alle 300 forventede avtale-/verdi-ID-par samsvarte. |
| Alle 66 noder og fordelinger | PASS: resultat `66 / 66 / 66 / 66 / 0 / 0` for forventede/faktiske noder, struktur, direkte/inklusive fordelinger, uventede/manglende. Dette er ikke en kjøring av dashboard-tjenestens queries eller browser. |
| Økonomirader | PASS for antall: 4 indekser, 8 indeksverdier, 300 indeksvalg, 600 linjer, 600 linjeversjoner og 600 prisversjoner. |
| Feltvis databaseeksport mot frossen referanse | PASS for eksporterte forretningsdata, med dokumenterte runtimeforskjeller nedenfor. Eksakt urørt seed-state er ikke påstått. |
| Gjentatt initialisering på samme database | PASS for eksportert Magida-graf: 15 samlinger, 3103 rader, byteidentiske før/etter-eksporter. |
| Bevaring av brukerendringer | PASS for testet scenario: MAG-001 fikk endret tittel og fjernet GEO-valg. DEPT ble beholdt. 599 tildelinger og 3102 eksporterte rader både før/etter ny initialisering; filene er byteidentiske. |
| AccountRegisters PostgreSQL-runner | **PASS — brukerutført, 41 PASS-kontroller, exitkode 0.** Konkret scope og logg i avsnitt 8. |
| Agreements PostgreSQL-runner | **PASS — brukerutført, 78 PASS-kontroller, exitkode 0.** Konkret scope og logg i avsnitt 9. |
| Øvrige PostgreSQL-runnere | **NOT PERFORMED — bruker har valgt å avslutte videre verifikasjon.** |
| Hele autorisasjons-/constraint-/concurrency-/rollback-regresjonen | **NOT PERFORMED — bruker har valgt å avslutte videre verifikasjon.** Bare de konkrete scenarioene i AccountRegisters og Agreements er PASS. |
| Dimensjonseditor, manuell UI | PASS for brukerens oppgitte scope: språkfaner/inputbevaring, lavt/smalt vindu med scrolling og synlige handlinger, lagring/gjenåpning i tre språk, kontrollerte lokaliserte visninger, avbryt, valideringsfeil, fallback og tastatur/synlig fokus. |
| Full systematisk browser-/tilgjengelighetsmatrise | **NOT PERFORMED — bruker har valgt å avslutte videre verifikasjon.** Den manuelle kontrollen er ikke bevis for alle browserflyter, roller, skjermstørrelser, nettlesere, skjermlesere eller lys/mørk visning. |

## 2. Eksportevidens og begrensninger

Den frosne referansen er `unified-dimensions-phase-1/demo-reference.json`, SHA-256 `18c1e1083523112418b9f36bd8d4ab3c196cc86aac8924d4254ea64590a67701`.

Første eksport er vedlegget `dc6fabf0-4b74-43ee-be79-ce35525d141e/Pasted text.txt`, SHA-256 `d94cb27f5e75cd62e1bbb376b7aae124746045e442fc5a617179115f70c37f28`. Alle forventede ID-er, rader og eksporterte felter ble sammenlignet, inklusive eksakte desimaler, enumverdier, datoverdier og referansens avtalte runtime-aktør/-tid. LineDocuments var tom som forventet. Sammenligningen hadde 43180 feltkontroller og 600 avvik, utelukkende:

- Alle 300 avtaler hadde `DeadlinesInitialized=true` i stedet for seedens false.
- Alle 300 hadde en ny GUID-revisjon i stedet for seed-ID som revisjon.

Mønsteret samsvarer med `AgreementDeadlineInitializer.EnsureAccountAsync`, som initialiserer frister og tildeler ny revisjon. DeadlineSynchronizer/ReminderProcessor var byteidentiske med originalcommitten. Den konkrete historiske triggeren er ikke utledet fra en prosesslogg. Andre forretningsfelt samsvarte; disse 600 forskjellene ble ikke skjult eller erklært seed-identiske.

Alle 600 `AnchorDate`-verdier var PostgreSQL `-infinity`, som Npgsql normalt mapper til `DateOnly.MinValue`, referansens `0001-01-01`. Dette ble behandlet som provider-representasjon, jf. [Npgsqls dokumentasjon](https://www.npgsql.org/doc/types/datetime.html#infinity-values). Tidssoneoffset ble sammenlignet som samme tidspunkt, ikke fjernet som et vilkårlig avvik.

Et første forsøk på idempotens sammenlignet første eksport med `ded4ca58-0190-489d-87f7-eacc8c386dc2/Pasted text.txt`. Det viste 3012 forskjeller i tidspunkt/revisjoner. Brukeren bekreftet gjenskaping/tømming/gjenoppretting mellom eksportene. Forsøket er derfor **ikke en gyldig idempotenskontroll**, og er ikke brukt som bevis på en implementasjonsfeil.

Gyldig ordinær idempotens brukte vedleggene `ded4ca58-0190-489d-87f7-eacc8c386dc2` og `7b92de62-442a-4a24-8f54-f380ca80c0ee`. Begge er 1702780 byte med SHA-256 `e0bd51f8ade6d664dbd4563741143c28f451765b215a5d6cfd1b9b29ec25aa6a`. Ingen tidspunkt, revisjoner eller andre verdier ble normalisert bort.

Gyldig redigert idempotens brukte `/Users/morten/Downloads/redigert_før.json` og `/Users/morten/Downloads/redigert-etter.json`. Begge er 1702514 byte med SHA-256 `87a57e4c21fdb0822089ee80d3715fcd08f1e9f4eb323cae98be6d3c199095f4`. Tittelen `MAG-001 Leieavtale – Tokyo IDEMPOTENSETEST`, fravær av GEO-valg og beholdt DEPT er kontrollert eksplisitt.

Eksportene dekker Magida-kontoen og feature-grafen, ikke alle Nordic Property-/Leasing-/audit-/outbox-/fristtabeller. Oppdaterte metadata/oversettelser/regler, parallelle initialiseringer og avbruddsgjenoppretting er ikke særskilt databaseverifisert gjennom disse to idempotensscenarioene.

Midlertidige lokale analyser ligger i `/tmp/tenantplatform-phase9-export-comparison.json`, `/tmp/tenantplatform-phase9-idempotency-comparison.json`, `/tmp/tenantplatform-phase9-idempotency-comparison-2.json` og `/tmp/tenantplatform-phase9-edited-idempotency-comparison.json`. Rapporten og input-hashene er den varige oppsummeringen; tmp-filene er ikke repo-verktøy eller nye testinnganger.

## 3. Første PostgreSQL-regresjonsrunde: faktisk undersøkt kode

| Program | Fast databasenavn | Miljøvariabel | Schema | Opprydding etter forberedelsen |
|---|---|---|---|---|
| `TenantPlatform.AccountRegisters.SmokeTests` | `tenant_account_register_tests` | `ACCOUNT_REGISTER_TEST_CONNECTION` | `register_test_` + 32 heksadesimale GUID-tegn | DROP SCHEMA CASCADE bare etter vellykkede kontroller. Ved feil etter opprettelse beholdes schema og opprinnelig exception. |
| `TenantPlatform.Agreements.SmokeTests` | `tenant_agreement_tests` | `AGREEMENT_TEST_CONNECTION` | `agreement_test_` + 32 heksadesimale GUID-tegn | Ingen automatisk schemaopprydding, heller ikke ved suksess. Filfixtures under egen `agreement-files_<GUID>`-katalog i OS-temp beholdes. |

Begge validerer eksakt databasenavn før de kobler til. De oppretter schema selv og overstyrer Npgsql SearchPath til dette schemaet. Alle 45 migrasjoner inngår; AccountRegisters stopper først ved to historiske migrasjonsgrenser, legger inn historiske registerrader og kontrollerer rename før sluttmigrering. De skriver og endrer testdata i sine egne testdatabaser. Gjennomgang av migrasjonskilden fant ikke eksplisitte public-schema-targets eller EnsureSchema som omgår deres valgte SearchPath.

Begge bruker egen direkte Npgsql-fabrikk. De starter ikke Web-host, hosted workers eller DemoDataInitializer. Agreements skriver lokale filfixtures og prøver feil-/oppryddingsstier for dokumenter. Offline design-time factory brukes ikke av runnerne og er beholdt uendret. Ingen EF database-update-kommando eller update-database.sh inngår.

**Undersøkt kandidatscope — ikke testresultat:**

- AccountRegisters: historisk rename/full migrasjonskjede, Dimensions-normalisering/validering, konto-/admin-/plattformrollegrenser, stale revisjon, flytting av undertrær, syklus/selv-/fremmed forelder, ett samtidig reparent-scenario, databaseavvisning av fremmed forelder og en kontoslettingssperre.
- Agreements: migrasjon/snapshot, create/read/edit/access/owner/property-/tenantgrenser, avtalerevisjon og ett samtidig edit-scenario, dokumentlagring/tilgang/filvalidering/avbrudd/revisjonskonflikt, enkelte FK-/Restrict-avvisninger samt generiske tildelinger, historiske inaktive valg, endring/tømming og fremmed verdi.
- Disse to alene beviser ikke alle required/multiple/leaf-only-/historikk-/rollbackkombinasjoner, analyseflyter, dashboardqueries, Leasing eller generell samtidighet/deadlockfrihet.

## 4. Avgrenset tilpasning for å bevare feilevidens

Den opprinnelige AccountRegisters-inngangen slettet schemaet i `finally`, også ved feil. Dette var uforenlig med brukerens krav om å bevare feilet testschema. Før første kjøring er bare følgende tilpasset:

1. AccountRegisters logger databasenavn/schema før tilkobling, bevarer schema ved feil med `throw;`, og dropper bare ved suksess. En suksessmelding kommer etter vellykket opprydding.
2. Agreements logger databasenavn/schema før tilkobling og fixture-sti før dokumentkontrollene; ingen endring av eksisterende oppryddingspolicy.
3. AccountRegisters README beskriver den nye diagnosepolicyen.

Ingen assertions, testdata, forventninger, produksjonstjenester, migrasjoner eller snapshot er endret. Ved å reversere bare diagnostikk-/oppryddingsendringene rekonstrueres begge Program.cs byteidentisk med sine hasher ved rundestart:

- AccountRegisters: `dc12da3ff861dcf331e1ff470807a8fd3c5b9152b17c17a5ffe7a18ec32cc00c`
- Agreements: `45b667098c57b64b618c1a35eb989a14737485978fa651ee73dfb5f4d5a56f91`

Ingen assertions eller testforventninger ble endret for å få PASS. Under forberedelsen var intet databaseprogram startet. Første brukerutførte PostgreSQL-kjøring har nå dokumentert suksessoppryddingen; feilgrenen som bevarer schema er fortsatt bare kildekontrollert/kompilert. Alle migrasjons-/Designer-/snapshot-filer og offline-fabrikken er byteuendret fra denne rundens startmanifest `/tmp/tenantplatform-phase9-pg-preparation-start-sha256.json`.

## 5. Faktisk utførte kommandoer under forberedelsen

| Kommando/kontroll | Type | Resultat |
|---|---|---|
| `dotnet build TenantPlatform.sln --no-restore` | Bygg | PASS, exit 0; 2 eksisterende advarsler, 0 feil |
| `dotnet build tests/TenantPlatform.AccountRegisters.SmokeTests/TenantPlatform.AccountRegisters.SmokeTests.csproj --no-restore` | Bygg | PASS, exit 0; 5 eksisterende advarsler, 0 feil; runner ikke kjørt |
| `dotnet build tests/TenantPlatform.Agreements.SmokeTests/TenantPlatform.Agreements.SmokeTests.csproj --no-restore` | Bygg | PASS, exit 0; 5 eksisterende advarsler, 0 feil; runner ikke kjørt |
| `zsh -n` på kommandofunksjonen nedenfor | Kun syntakskontroll | PASS; funksjonen er ikke kalt, ingen passord lest og ingen forbindelse |
| Inline SHA-/rekonstruksjonskontroll | Kildekontroll | PASS; bare oppgitt diagnostikk/opprydding er endret i testinngangene |
| `git diff --check` og whitespace-kontroll av ny rapport | Kildekontroll | PASS |

NU1902/NU1903 gjelder eksisterende System.Security.Cryptography.Pkcs 10.0.0. MSB9008 gjelder eksisterende restore-cache som viser til det gamle Application-prosjektet, selv om de to nåværende csproj bare refererer Web. Ingen pakke-/cacheendring eller undertrykking er gjort. Rå bygglogger ligger i `/tmp/tenantplatform-phase9-build-{solution,AccountRegisters,Agreements}.log`.

Endrede filer i denne forberedelsen: de to nevnte test-Program.cs, AccountRegisters README og denne nye rapporten. Tidligere faser/referanser/audit er ikke skrevet om. Ingen commit, push, deploy, databaseopprettelse eller reset.

## 6. Brukerutført opprettelse i pgAdmin — ikke utført av agenten

På den avtalte lokale instansen `localhost:5432` oppretter brukeren **to nye separate databaser**. Bruk eksisterende administrasjonsrolle som allerede kan opprette databaser, og velg eksisterende `tenantplatform_dev` som eier/testtilkoblingsrolle. Det kreves ikke at denne testrollen gis superuser/CREATEDB.

1. Høyreklikk **Databases → Create → Database**.
2. General: Database = `tenant_account_register_tests`, Owner = `tenantplatform_dev`.
3. Definition: Template = `template0`, Encoding = `UTF8`; behold ellers passende lokale standarder.
4. Kontroller SQL-fanen og lagre.
5. Gjenta med Database = `tenant_agreement_tests`, samme eier.
6. Ikke velg appdatabasen `tenantplatform_dev` som mal eller testmål. Hvis et testnavn allerede finnes: undersøk det, ikke slett/gjenskap automatisk.

Feltene er kontrollert mot [pgAdmins Database-dialog](https://www.pgadmin.org/docs/pgadmin4/latest/database_dialog.html).

I Query Tool på vedlikeholdsdatabasen `postgres` kan brukeren kontrollere opprettelsen read-only:

```sql
SELECT datname, pg_get_userbyid(datdba) AS owner
FROM pg_database
WHERE datname IN ('tenant_account_register_tests', 'tenant_agreement_tests')
ORDER BY datname;
```

Forvent to rader, begge med owner `tenantplatform_dev`. Brukeren har nå bekreftet at begge testdatabasene er opprettet med korrekt eier. Agenten har ikke selv åpnet en forbindelse eller mottatt passord/connection strings.

## 7. Terminalkommandoer — begge programmer brukerutført

Definer følgende funksjon i en lokal zsh-terminal. Definisjonen alene kjører ingen test. Den tillater bare de to faste navnene/prosjektene, fjerner arvede app-/testforbindelser i en underprosess, bruker skjult passordinntasting og setter forbindelsen bare i underprosessens miljø. Ingen connection string eller passord skrives ut.

```zsh
tp_pg_smoke() {
(
    set +x
    set +e
    umask 077
    cd /Users/morten/Dev/TenantPlatform || exit 1

    case "$1" in
        AccountRegisters)
            tp_db='tenant_account_register_tests'
            tp_env='ACCOUNT_REGISTER_TEST_CONNECTION'
            tp_project='tests/TenantPlatform.AccountRegisters.SmokeTests'
            ;;
        Agreements)
            tp_db='tenant_agreement_tests'
            tp_env='AGREEMENT_TEST_CONNECTION'
            tp_project='tests/TenantPlatform.Agreements.SmokeTests'
            ;;
        *)
            print -r -- 'Avbrutt: bare AccountRegisters eller Agreements er tillatt.'
            exit 1
            ;;
    esac

    unset ConnectionStrings__DefaultConnection
    unset ACCOUNT_REGISTER_TEST_CONNECTION AGREEMENT_TEST_CONNECTION

    print -r -- "Testmål: localhost:5432 / $tp_db / rolle tenantplatform_dev"
    if ! IFS= read -r -s 'tp_password?Passord for tenantplatform_dev, skjult inntasting: '; then
        printf '\n'
        exit 1
    fi
    printf '\n'
    tp_password=${tp_password//\"/\"\"}

    typeset -a tp_parts
    tp_parts=(
        'Host=localhost'
        'Port=5432'
        "Database=$tp_db"
        'Username=tenantplatform_dev'
        'Search Path=public'
        "Password=\"$tp_password\""
    )
    export "$tp_env=${(j:;:)tp_parts}"
    unset tp_password tp_parts

    tp_logroot='/Users/morten/Library/Application Support/TenantPlatform/PostRunVerification/pg-logs'
    mkdir -p "$tp_logroot" || exit 1
    tp_log=$(mktemp "$tp_logroot/$1.XXXXXXXX") || exit 1
    print -r -- "Logg: $tp_log"

    dotnet run --project "$tp_project" --no-build --no-restore 2>&1 | tee "$tp_log"
    tp_results=("${pipestatus[@]}")
    tp_exit=${tp_results[1]}
    if (( tp_results[2] != 0 && tp_exit == 0 )); then
        tp_exit=${tp_results[2]}
    fi
    print -r -- "Exitkode: $tp_exit" | tee -a "$tp_log"
    print -r -- "Behold loggfilen: $tp_log"
    if (( tp_exit != 0 )); then
        print -r -- 'Stopp her. Behold testschema og fixtures; ikke gjenta testen.'
    fi
    exit "$tp_exit"
)
}
```

**Første program er brukerutført og vurdert PASS (se avsnitt 8):**

```zsh
tp_pg_smoke AccountRegisters
```

Funksjonen kaller da eksakt:

```sh
dotnet run --project tests/TenantPlatform.AccountRegisters.SmokeTests --no-build --no-restore
```

Etter vurdert AccountRegisters-resultat ble også dette programmet brukerutført alene og vurdert PASS (se avsnitt 9):

```zsh
tp_pg_smoke Agreements
```

Dette kaller eksakt:

```sh
dotnet run --project tests/TenantPlatform.Agreements.SmokeTests --no-build --no-restore
```

Ingen løkke eller automatisk kjeding starter neste program. Loggfilen er ny og privat for hver kjøring. Runnerens exitkode tas fra zsh `pipestatus`, ikke fra tee. Testdatabase/schema og fixture-sti føres i loggen. Ikke bruk shell tracing, dump av miljøvariabler eller passord som kommandolinjeargument.

Ved feil: stopp, behold logg/schema/fixtures, og send full feillogg uten hemmeligheter. Ingen automatisk repetisjon, reset eller cleanup av feiltilstanden. Diagnose skal først skille miljø/tilkobling/fixturefeil, eksisterende regresjon/testfeil og Unified Dimensions-feil ut fra første feil, stacktrace, testscenario og relevant kildediff. Ingen av disse kategoriene er forhåndsdømt.

## 8. Faktisk PostgreSQL-kjøring: AccountRegisters

**PASS.** Brukeren kjørte `tp_pg_smoke AccountRegisters`, som kaller:

```sh
dotnet run --project tests/TenantPlatform.AccountRegisters.SmokeTests --no-build --no-restore
```

- Mottatt logg: `/Users/morten/Downloads/AccountRegisters.g5PQOXiw`, 2209 byte, SHA-256 `992580eb8c9db7d90c91c5fea70bb01172a9facf1c501ead608ac12033783586`.
- Loggen bekrefter `tenant_account_register_tests` og schema `register_test_7bd9f9ffe84c41f486f1dd7448dde77e`.
- 41 linjer med `PASS:`, avsluttende suksessmelding og `Exitkode: 0`. Brukeren oppga også exitkode 0.
- Loggen bekrefter at akkurat dette testschemaet ble slettet ved suksess. Ingen feiltilstand krevde bevaring eller diagnose.
- AccountRegisters-kilden er fortsatt SHA-256 `8bd644b606a400eb17ac7ecd7614fa36a38b7635a483c35b392094fa0da70c91`; Agreements-kilden er fortsatt `9696b241f1bcdbefe4fe42bb4b4d1fdc3940a02ec74fdf0b2d38a4badd43bff0`.
- Dette er den første mottatte PostgreSQL-runnerloggen; ingen agentutført testkjøring eller automatisk repetisjon er utført.

| Faktisk testet delscope | Resultat og grense |
|---|---|
| Aktuell modell/snapshot og full migrasjonskjede | PASS i nytt, isolert testschema, gjennom tilsiktet legacy-drop. |
| Historisk register-rename | PASS for avdelings- og lokasjonsraden ved historisk migrasjonsgrense; ikke en påstand om å bevare droppede legacy-data i sluttschema. |
| Dimensions-normalisering og feltgrenser | PASS for navn/kode/eierskap og de tre testede ugyldige inputkombinasjonene. |
| Stale dimensjonsrevisjon | PASS; testet stale lagring ble avvist. |
| Hierarki | PASS for descendant-/self-cycle, manglende/fremmed forelder og bevaring av undertrær ved flytting. |
| Konto-/serverautorisasjon | PASS for testede kontoavgrensninger og fem ikke-admin/forged/missing/anonymous identitetskontekster med lesing/create/edit/verdi-edit. Dette er direkte servicetester, ikke cookie-/circuit-/browserbevis. |
| Samtidig reparent | PASS for ett par samtidige motstridende flyttinger; nøyaktig én lyktes. Ikke generell concurrency-/stress-/deadlockverifikasjon. |
| Relasjonell foreldre-FK | PASS; direkte EF-innsetting med fremmed konto-/dimensjonsforelder ble avvist av PostgreSQL. Ikke en negativ test av alle feature-FK-er. |
| Kontoslettingssperre | PASS for beslutningen om en konto med kun dimensjonsdata; ingen generell fysisk kontosletting testet. |
| Navigasjonsrettigheter | PASS for serviceberegnet admin-/medlemsrettighet; ikke faktisk browsernavigasjon. |

Ingen testfeil er observert i denne kjøringen. Det er derfor ingen feil å klassifisere som eksisterende regresjon eller Unified Dimensions-feil. Samtlige kontroller i loggen er vurdert sammen med kildekoden; antallet 41 brukes ikke som bevis for alle akseptansekriterier.

## 9. Faktisk PostgreSQL-kjøring: Agreements

**PASS.** Brukeren kjørte `tp_pg_smoke Agreements`, som kaller:

```sh
dotnet run --project tests/TenantPlatform.Agreements.SmokeTests --no-build --no-restore
```

- Mottatt logg: `/Users/morten/Downloads/Agreements.TUTaZru4`, 3317 byte, SHA-256 `ddfbf17250a14a0d10a553a0ae3fadc9f365c4bd0b992394a606f9c418d5c22f`.
- Loggen bekrefter databasen `tenant_agreement_tests` og schema `agreement_test_bf947e5dbdf141f7965d0a3735c965cd`.
- 78 linjer med `PASS:`, `All agreement smoke tests passed.` og `Exitkode: 0`. Brukeren oppga også exitkode 0.
- Fixture-sti fra loggen: `/var/folders/5w/wph_kmg56z587gg47rhwd9yw0000gn/T/agreement-files-726da3e1e13f4b4c989bf29fd367ad9d`.
- Runneren har ingen avsluttende schema-/fixture-opprydding. Ingen automatisk sletting eller ny kjøring er utført av agenten; programmet etterlater schema og fixture-katalog.
- Agreements-kilden er fortsatt SHA-256 `9696b241f1bcdbefe4fe42bb4b4d1fdc3940a02ec74fdf0b2d38a4badd43bff0`, uendret fra klargjøring og før teststart.

| Faktisk testet delscope | Resultat og grense |
|---|---|
| Modell/snapshot og migrasjonskjede | PASS i nytt isolert testschema, uten Web-/demo-oppstart. |
| Konto-/ressurstilgang | PASS for fremmed avtale/konto/motpart/eier/bygg/enhet, manglende grant, begrenset liste/filter og testede plattform-/byggadministratorgrenser. Direkte servicetester med testkontekst, ikke cookie-/circuit-/browserbevis. |
| Eier, read/edit og grants | PASS for eieradministrasjon, leser-/editorbegrensninger, eierbytte/grants og avvist tilgang etter fjernet medlemskap. |
| Avtalefelt og validering | PASS for de testede dato-/notice-/renewal-/bygg-/enhetskombinasjonene samt søk/filter. Ikke full økonomi-/livssyklusregresjon. |
| Revisjon/samtidig edit | PASS for stale edit og ett par samtidige edits der bare én lyktes. Ikke generell concurrency-/stress-/deadlockverifikasjon. |
| Dokumenttilgang og filvalidering | PASS for autoriserte/avviste nedlastinger, filename-sanitization, separate dokument-ID-er, størrelse/signatur/extension, syntetiske DOCX/XLSX- og PNG-fixtures og avbrutt upload. Ingen faktisk browser eller Office-app er testet. |
| Feilsti under upload | PASS: endret revisjon etter streaming avvises; ucommittet fil fjernes og dokumentmetadata forblir uendret. Dette er den konkrete testede feilstien, ikke bevis på alle rollback-scenarioer. |
| Enkelte relasjonelle avvisninger | PASS for sletting av referert motpart/verdi, krysskonto-grant og krysskonto/feil dimensjon i direkte assignment-innsetting. Runneren forventer DbUpdateException, ikke eksakt SQLSTATE/constraintnavn. |
| Generiske dimensjoner | PASS for kontoavgrensede kataloger, full hierarkisti, eksplisitt create/detail-roundtrip, eierens tilgang uten registeradmin og avvisning av manglende/fremmed verdi. |
| Historisk inaktiv verdi | PASS for skjuling ved nyvalg, historisk visning ved edit, bevaring ved urelatert edit og avvisning ved nytt tillegg. Dette dekker ikke alle unavailable-/required-/multiple-/leaf-only-kombinasjoner. |
| Endring og tømming | PASS for foreldervalg, avvist fremmed edit-verdi og eksplisitt tom patch som fjerner tildelingen. |

Ingen testfeil er observert i denne kjøringen. Det foreligger derfor ingen feil å klassifisere eller begrunnelse for ny kjøring/endring av tester. Ingen assertions eller forventninger ble endret etter mottatt logg.

## 10. Status etter første PostgreSQL-regresjonsrunde

Den bestilte runden med **AccountRegisters og Agreements er fullført og PASS**, én runner om gangen med resultatvurdering mellom dem. `tenantplatform_dev` og Magida-grafen er ikke brukt som mål for runnerne. Agenten har ikke åpnet en databaseforbindelse, startet appen/testprogrammene, hentet hemmeligheter fra konfigurasjon/user-secrets eller slettet databaser/schemaer. Testprogrammene er kjørt manuelt av brukeren; AccountRegisters sin suksessopprydding er dokumentert i loggen.

Det er ikke startet flere programmer. Analyse, Leasing, dashboardets faktiske PostgreSQL-queryer, øvrige testprogrammer og bredere required/multiple/leaf-/historikk-/rollbackkombinasjoner har status **NOT PERFORMED — bruker har valgt å avslutte videre verifikasjon**. De to konkrete samtidighetsscenarioene og upload-feilstien oppgraderes ikke til «all concurrency/rollback verifisert». Den senere avgrensede editorverifikasjonen står i avsnitt 11; ingen ny regresjonsrunde skal startes som del av denne leveransen.

## 11. Avslutning av videre regresjonsrunde og UI-oppfølging

Brukeren avsluttet 2026-11-12 videre PostgreSQL-regresjon etter de vurderte PASS-resultatene for AccountRegisters og Agreements. De resultatene og evidensen i avsnitt 8–9 beholdes. AgreementAnalysis og Leasing ble bare kildekontrollert/klargjort; de ble ikke startet. De og øvrige PostgreSQL-programmer, full regresjon og de bredere samtidighets-/rollbackkravene i avsnitt 10 har status **NOT PERFORMED — bruker har valgt å avslutte videre verifikasjon**.

Brukeren rapporterte at grensesnittet fungerer bra ved manuell bruk, men fant et konkret layoutproblem i dimensjonseditoren: alle tre oversettelsesspråk lå etter hverandre og modalens handlinger kunne havne utenfor skjermen. Dette er ikke registrert som en full browser-PASS. En avgrenset kodeendring deler oversettelsene i språkfanene Norsk, English og Svenska, beholder fallbackfelter og globale innstillinger i fellesdelen, gjør modalinnholdet høydebegrenset/rullbart og holder handlingsraden separat. Aktiv fane og alle inntastede modellverdier beholdes ved fanebytte og etter lagringsfeil; skjulte oversettelsesfelt med feltgrensefeil markerer tilhørende fane.

Målrettet offline komponentrunner passerte 159 kontroller uten databaseforbindelse. De nye kontrollene dekker tre native knappfaner, bare ett synlig språkpanel, `type="button"`, fanebytte, ingen utilsiktet submit, bevart input, bevart aktiv fane etter valideringsfeil og markering av skjult språkfeil. Knappene beholder vanlig tastaturaktivering og fokusmønster. Fallbackregler, lagringsmodell, tilgang, revisjonsvern og oversettelsespatching er ikke endret.

Faktiske filer i den siste UI-rettelsen:

- `src/TenantPlatform.Web/Components/Pages/Dimensions/DimensionEditorDialog.razor` — én fane per støttet språk, ett synlig oversettelsespanel, bevart redigeringsmodell/aktiv fane og separat handlingsrad.
- `src/TenantPlatform.Web/Components/Pages/Dimensions/DimensionEditorDialog.razor.css` — flex-/høyde-/overflow-regler som gjør innholdet rullbart og handlingene synlige.
- `tests/TenantPlatform.Dimensions.SmokeTests/ComponentChecks.cs` — målrettede kontroller av faner, knappetype, fanebytte, input, submit og valideringstilstand.
- Denne fase-9-rapporten — resultat og evidensgrense.

Faktisk verifisering fra rettelsen, før denne avslutningsrunden: målrettet Dimensions-prosjekt bygget med 0 feil og de to eksisterende NU1902/NU1903-varslene; `TenantPlatform.sln` bygget med 0 feil og de samme to varslene; Dimensions-runneren passerte 159 kontroller og oppga ingen databaseforbindelse/SQL/migrering/initialisering; `git diff --check` passerte. Kontrollene er ikke kjørt på nytt i avslutningsrunden.

Brukeren har deretter manuelt godkjent editoren etter kontroll av:

- språkfaner og bevart input ved fanebytte;
- scrolling ved lavt/smalt vindu og synlige Lagre/Avbryt-knapper;
- lagring og gjenåpning av alle språk samt lokaliserte navn i kontrollerte visninger;
- Avbryt uten lagring, valideringsfeil med bevart input og fallback ved manglende oversettelse;
- tastaturnavigasjon og synlig fokus.

Brukerens tidligere generelle tilbakemelding var at grensesnittet ser bra ut. Dette og den avgrensede editor-PASS-en er ikke en systematisk verifikasjon av alle browserflyter, roller, skjermstørrelser, nettlesere eller tilgjengelighetsteknologier.

## 12. Endelig status og gjenværende verifikasjonsbegrensninger

**IMPLEMENTATION COMPLETE — godkjent av bruker med dokumenterte verifikasjonsbegrensninger.**

- **Ferdig kildeimplementasjon:** Unified Dimensions fase 1–8 og den avgrensede editorrettelsen er ferdige. Ingen ny produksjonskode, testkode, migrasjon eller snapshot er endret i denne avslutningsrunden.
- **Faktisk gjennomført:** evidensen i avsnitt 1–2 og 8–11, inklusive full migrasjonskjede/sluttschema, faktisk demo-initialisering og eksportkontroller, de to gyldige idempotensscenarioene, AccountRegisters 41/exit 0, Agreements 78/exit 0 og den avgrensede manuelle editorverifikasjonen.
- **Uutført etter brukerens valg:** alle punktene nedenfor har status **NOT PERFORMED — bruker har valgt å avslutte videre verifikasjon.** Historiske NOT PERFORMED-resultater erstattes ikke med PASS.

Uutført scope:

1. AgreementAnalysis, AgreementFollowup, AgreementNotice, AgreementPeriods, AgreementProcessing, Leasing og MeetingRooms som PostgreSQL-runnere; ingen logg dokumenterer kjøring.
2. Generell/fullstendig concurrency-, deadlock- og rollbackverifikasjon utover samtidig reparent i AccountRegisters, samtidig avtaleedit og den konkrete upload-feilstien i Agreements.
3. Full PostgreSQL-dashboardverifikasjon, faktiske dynamiske dashboardqueries og ytelsesmåling på større grafer.
4. Full systematisk browser-/tilgjengelighetsmatrise på tvers av alle flyter, roller, skjermstørrelser, nettlesere, lys/mørk visning og skjermleser.
5. Alle required/multiple/leaf-only-/historikkkombinasjoner, alle provider-constraints, full fysisk kontosletting, parallelle/avbrutte initialiseringer og full rollback av analyse-/dokument-/motpartsgrafer.
6. Fullgraf-databasekomparator for alle domener utenfor de 15 eksporterte Magida-samlingene; Nordic Property, full Leasing, audit, outbox og alle fristtabeller inngikk ikke i eksportparet.

Videre testing er avsluttet etter brukerens valg. Ingen av begrensningene ovenfor er omklassifisert til PASS.
