# Avtalefrister, oppfølging og påminnelser

## Bruk og datakilde

- `/agreements/follow-up`: én rad per frist, med mine/alle, ansvarlig, avtaletype,
  fristtype, status, forekomst, søk, datointervall og sideinndeling (25).
  Standard: ubehandlede/pågående gjeldende frister til og med neste 90 dager,
  **uten nedre datogrense**, slik at overtid beholdes.
- `/agreements/{id}`: registrerte avtaledata, egen oppfølgingsdel med handlinger
  og append-only historikk, og separat påminnelseslogg/innstillinger.
- `/agreements/reminder-settings`: AccountAdmin setter kontoens aktivering,
  terskler, tidssone og lokalt utsendelsestidspunkt.
- Eksisterende opprettelse/redigering får valgfri `RenewalDate`.
  Den utledes verken fra sluttdato, fritekst eller automatisk fornyelse.

Datoene synkroniseres fra Agreement gjennom AgreementNoticeRules og
AgreementDeadlineSynchronizer. Beregnet NoticeDeadline er avledet fra regelen og
RenewalDate; manuell frist beholdes som angitt. CessationDate kommer fra registrert
oppsigelses virkningsdato og lagret varighet. Se [oppsigelsesregler](agreement-notice-rules.md).
AgreementDeadline representerer en konkret forekomst med egen ID, dato, type og
registrert periodestart (Agreement.CurrentPeriodStartDate). Vanlig korrigering av
Agreement.StartDate endrer ikke periodeidentiteten. Ny periode velges eksplisitt ved redigering.
Endring av dato eller periodestart erstatter forekomsten; fjerning trekker den
tilbake. Selv retur til samme dato oppretter en ny, ubehandlet forekomst.
Gamle kommentarer, ansvarstildelinger, fullføringer og påminnelseslogger beholdes.

Synkronisering, historikk, kansellering av ventende påminnelser og avtaleendring
lagres atomisk i samme EF SaveChanges-transaksjon. Forekomstenes oppdateringer
bruker avtalens eksisterende Revision-token. Parallelle endringer avvises;
historikkposter har ingen redigerings-/sletteoperasjon.

Oppfølgingsstatus er ubehandlet, under vurdering eller ferdig behandlet,
uavhengig av om forekomsten er gjeldende, erstattet eller trukket tilbake.
Fullføring krever en kommentar og påvirker bare valgt frist.
Gjenåpning gjenbruker ikke en sendt påminnelse. Ingen handling eller dato i
denne modulen fornyer, sier opp eller endrer avtalestatus automatisk.

## Tilgang og ansvar

Eksisterende avtaletilgang gjelder også lister, oppslag, historikk og logger.
Lesere kan se; redigerere kan følge opp, kommentere, velge oppfølgingsansvarlig
og endre avtalespesifikke påminnelser. Bare AccountAdmin/avtaleansvarlig kan
fortsatt bytte avtaleansvarlig og tildele avtaletilgang. Kontoens standarder
kan bare endres av AccountAdmin.

Uten overstyring følger oppfølgingen nåværende avtaleansvarlig. En eksplisitt
ansvarlig må være aktivt kontomedlem med redigeringstilgang til avtalen.
Tildeling oppretter ingen tilgang. Ved bortfalt medlemskap/tilgang flagges
ansvarlig som ugyldig; ingen vilkårlig mottaker velges i stedet.
Arbeideren kontrollerer dette på nytt før transportkallet.

Alle tenant-spørringer er avgrenset med AccountId. Bakgrunnsjobben bruker
eksplisitt konto-ID, ikke en innlogget brukers kontekst. DbContexts er kortlivede.
Transportkallet skjer etter at databasekonteksten er lukket, uten åpen transaksjon.

## Arkivering og tidligere data

Dagens repository hadde **ingen fysisk sletting av avtaler**. Det er derfor
ikke innført sletting av utkast. Ny arkivering for AccountAdmin/avtaleansvarlig
bevarer avtale, dokumenter og historikk, trekker tilbake gjeldende frister,
stopper fremtidige påminnelser og sperrer redigering. Lesing, nedlasting og
tilgangsadministrasjon er fortsatt tilgjengelig. Arkiverte avtaler er merket
i registeret; tilbakeføring fra arkiv er ikke implementert.

AgreementDeadlineInitializer etablerer manglende forekomster i batcher ved
oppstart av jobb eller første relevante lesing. Den bruker eksisterende
datoer uten friteksttolking og endrer aldri disse datoene.
DeadlinesInitialized og avtalens revisjon gjør kjøringen idempotent, også ved
parallelle arbeidere/redigering. Historikken viser system som aktør ved backfill.
Oversikten fungerer uten at varsling aktiveres.

## Aktivering og klokke

Manglende kontoinnstilling betyr **avslått**. Standardterskler er 90, 30 og 7
dager før. Kontoens aktivering er hovedbryter; en avtale kan arve, ha egne
terskler eller deaktivere. Terskler valideres som 1–20 unike heltall i området
0–36500; 0 er fristdagen. Ingen per-frist-konfigurasjon.

Account hadde ingen tidssone. Modulen lagrer derfor eksplisitt TimeZoneId og
SendAt per konto, med **Europe/Oslo, kl. 08:00** som standard. Serverens lokale
tidssone brukes ikke. TimeProvider er injiserbar, og tekniske tider er UTC.
Ved ugyldig DST-klokkeslett flyttes utsendelsen til neste gyldige minutt;
ved dobbelt klokkeslett brukes den siste forekomsten.

En BackgroundService kontrollerer hvert minutt. Køelementer blir først
sendbare på beregnet lokal dato og klokkeslett. Kun aktive, ikke-arkiverte
avtaler med gjeldende, uavklarte frister og aktiverte påminnelser varsles.
Oppsamling etter nedetid/sen registrering bruker senest passerte terskel og
markerer eldre som forbigått. Ingen før-frist-påminnelser sendes etter fristdagen.
Overtid beholdes i oversikten.

## Transport og sikker lokal testing

Det finnes SMTP-transport og BackgroundService-mønster fra før. SMTP-adapteren
gjenbrukes via IEmailSender. ServiceRequest-outboxen gjenbrukes ikke som tabell:
den krever ServiceRequestId og skriver bestillingshistorikk. Den nye,
avgrensede AgreementReminder-køen har ingen slik kobling.

Standardtransport er `Capture` i **alle miljøer**. Den skriver ferdige JSON-filer
utenfor wwwroot og sender ingen e-post. Filnavnet er kø-ID; samme melding fanges
bare én gang. Konfigurer applikasjonsadresse for korrekte lenker:

```json
{
  "AgreementReminders": {
    "WorkerEnabled": true,
    "TransportMode": "Capture",
    "ApplicationBaseUrl": "http://localhost:5519",
    "CapturePath": "App_Data/agreement-reminders"
  }
}
```

Katalogen er relativ til content root og ignorert av Git. Hold den privat,
ikke eksponer den via statisk filserver/symlenke, og bruk varig volum ved behov.
Capture-filer inneholder mottaker, tittel og den korte e-postteksten.
De inneholder ingen kontraktsvedlegg eller oppfølgingskommentarer.

Reell levering krever alle disse:
1. Kontoaktivering i UI.
2. `AgreementReminders:TransportMode=Smtp`.
3. HTTPS-adresse i `AgreementReminders:ApplicationBaseUrl`.
4. Eksisterende `Email`-konfigurasjon: Host, Port, EnableSsl, Username,
   Password, FromAddress, FromName.
5. Et miljø som ikke er Development, Test eller Testing; disse avviser SMTP.

Miljøvariabler bruker `AgreementReminders__...`. WorkerEnabled=false stopper
jobbsløyfen helt, men oversikt, backfill ved lesing og manuell oppfølging virker.
Deaktivering stopper nye transportkall; en allerede akseptert e-post kan ikke
trekkes tilbake.

E-post bruker mottakerens PreferredLanguage (nb-NO, en-GB, sv-SE), med nb-NO som
fallback ved ukjent språk. Meldingen inneholder bare avtalenavn, fristtype/dato,
oppfordring til oppfølging og konfigurert avtalenke. Den eksisterende
påminnelses-/arbeiderfunksjonaliteten sender ingenting i testene.

## Kø, samtidighet og leveringsbegrensninger

Unik databaseindeks på (AccountId, DeadlineId, DaysBefore) beskytter
forekomst/terskel-identiteten. En kort PostgreSQL advisory-transaksjonslås per
forekomst serialiserer planleggingen og unngår låsekonflikter ved parallelle
fler-radsinnsettinger. Selve utsendingen reserveres med én atomisk betinget
UPDATE, reservation-ID og ti minutters reservasjon. Status og reservation-ID
er concurrency-tokens, slik at en planlegger ikke overskriver sendt tilstand.

Før transport kontrolleres aktiv konto/avtale, arkivering, forekomst, status,
konfigurasjon, terskel, kalenderdato og mottaker på nytt. Utdaterte elementer
hoppes over. Det finnes uunngåelig et lite tidsvindu mellom siste kontroll og
ekstern transport; en allerede påbegynt/akseptert utsending kan ikke tilbakekalles.

Transportfeil gir maksimalt tre forsøk, med 2 og 4 minutters forsinkelse.
Andre køelementer og kontoer fortsetter. Loggen viser terskel, mottaker,
planlagt tidspunkt, status, forsøk, siste forsøk, aksepttid, transport-ID når
tilgjengelig og en kort lokalisert feilkode. Rå SMTP-feil/hemmeligheter lagres ikke.

SMTP har ingen idempotency-støtte eller transport-ID i dagens adapter.
Et utløpt Processing-element får terminal feil «ukjent leveringsutfall» og
sendes **ikke** automatisk på nytt. En transportfeil med tvetydig utfall kan
likevel ha nådd mottakeren før retry; det er **ingen exactly-once-garanti**.
Ukjente/sluttfeilede leveringer må vurderes manuelt; egen retry-knapp er ikke
implementert. «Akseptert av transport» betyr ikke innbokslevering eller lesing.
Capture-prefiks i loggen betyr lokal testfangst.

Sendte og hoppede/kansellerte terskler beholdes som behandlet ved gjenåpning,
endring av innstillinger eller reaktivering. Fremtidige, tidligere ubehandlede
terskler kan fortsatt varsles. En ny fristforekomst får egne identiteter.

## Migrering og verifikasjon

Generert med prosjektets script:
`20260916154805_AddAgreementFollowupAndReminders`.
Den legger til felter og nye tabeller uten å fjerne eksisterende data.
Bruk vanlig `bash scripts/update-database.sh` i et autorisert miljø, eller
eksisterende automatisk migrering ved oppstart. Ingen delt database eller
produksjonsdatabase ble migrert under utviklingen.

Den nye konsolltesten følger eksisterende smoke-testmønster:

```sh
AGREEMENT_TEST_CONNECTION='Host=127.0.0.1;Port=55449;Username=agreement_test;Database=tenant_agreement_tests' \
  dotnet run --project tests/TenantPlatform.AgreementFollowup.SmokeTests
```

Den krever eksplisitt disponibel database med dette navnet, oppretter eget
skjema per kjøring og tester mot ekte PostgreSQL. Klokken og transporten er
kontrollerte; SMTP-spionen feiler om ekte transport forsøkes brukt. Testene
dekker tilgang/tenantgrenser, tildeling, dato-/periodebytte, kommentarer,
fullføring/gjenåpning, arkivering, 90/30/7/0, DST/skuddår, parallelle arbeidere,
oppsamling, bortfalt tilgang, avbrutt reservasjon, retry og idempotent backfill.

Kjørt 16.09.2026:
- Ny oppfølgings-smoketest, eksisterende avtale-/dokumenttester og
  eksisterende møteromstester: bestått mot isolert PostgreSQL.
- Full migreringskjede og EF-snapshot/modellkontroll: bestått.
- Nettleser: standardvisning med overtid, start/fullføring med påkrevd kommentar,
  gjenåpning, historikk, datoendring med ny forekomst, kontoinnstillinger og lokal
  bakgrunnsbehandling. Loggen viste én fanget 7-dagerspåminnelse og 90-/30-dager
  som forbigått, med capture-ID og uten reell e-post.
- Språkressurser kontrollert for manglende statiske nøkler og duplikater.

## Søkbar IANA-tidssonevelger

`/agreements/reminder-settings` bruker `AgreementTimeZoneSelect`: et søkefelt som
filtrerer en vanlig Bootstrap-stylet HTML-select. Begge har eksplisitte labels.
Søk etter f.eks. Oslo, Europe eller America/Chicago; søket endrer aldri valgt
verdi. Gjeldende valg beholdes i listen selv om det ikke matcher søket. Valg skjer
med mus eller nettleserens vanlige tastaturnavigasjon (Tab, mellomrom, piltaster,
Enter). Ingen ny UI-pakke eller JavaScript-komponent er innført.

`AgreementTimeZones` bygger en alfabetisk, duplikatfri liste én gang per prosess
fra .NET 10 `TimeZoneInfo.GetSystemTimeZones()`. IANA-ID-er beholdes; Windows-ID-er
konverteres gjennom `TryConvertWindowsIdToIanaId`. Hvert valg kontrolleres mot
samme `AgreementReminderSchedule.Zone` som påminnelsesjobben bruker. UTC er
eksplisitt tilgjengelig, og Europe/Oslo er fortsatt standarden. Det vises ikke
faste offsetter. Sommer-/vintertidsreglene og eksisterende håndtering av tvetydige
eller manglende klokkeslett er uendret.

Datakilden er runtime/operativsystemets tzdata og ICU, uten nettverksoppslag per
sidevisning eller ny NuGet-avhengighet. Repositoryets Dockerfile bruker standard
`mcr.microsoft.com/dotnet/aspnet:10.0` på Linux; prosjektet bruker ikke invariant
Globalization eller NLS. Windows-drift krever ICU med IANA-støtte. Runtime-listen
kan variere mellom OS; Windows-listen kan representere flere IANA-steder med én
konvertert ID. Standardverdien og et støttet lagret alias legges derfor til ved
behov. Oppdater runtime/OS-tidssonedata og start prosessen på nytt for ny katalog.
Se [Microsofts dokumentasjon om Windows/IANA-konvertering](https://learn.microsoft.com/en-us/dotnet/api/system.timezoneinfo.tryconvertwindowsidtoianaid?view=net-10.0).

Serveren krever en støttet IANA-ID, og avviser Windows-ID-er, ugyldige verdier og
OS-lokale tzfiler som `localtime`. Støttede eldre aliaser gjenkjennes av ICU og
beholdes nøyaktig, også når de ikke finnes i standardlisten. En ugyldig lagret
verdi vises som den er, med lokaliserte instruksjoner om å velge på nytt; ingen
fallback lagres automatisk. Eksisterende tekstkolonne, Revision, AccountId-filter
og AccountAdmin-krav er uendret. Ingen migrering er nødvendig.

Ved tidssoneendring oppdaterer neste planleggingskjøring `ScheduledUtc` for
ventende og automatisk repeterbare mislykkede påminnelser. Sendte/avsluttede rader
blir ikke sendt igjen. Kontroll før sending leser innstillingene på nytt og kan
sette en reservert påminnelse tilbake til Pending når den ennå ikke er aktuell.
Dette skjer ikke synkront ved lagring, og en allerede påbegynt transport kan ikke
trekkes tilbake. Denne eksisterende oppførselen er beholdt.

Verifisert med `dotnet build TenantPlatform.sln` og utvidet
`TenantPlatform.AgreementFollowup.SmokeTests` mot isolert PostgreSQL-skjema:
alle katalogverdier kan brukes av scheduler, standard/lagring/gjenlesing,
US/Central-alias, ugyldig/Windows-ID, ugyldig eldre verdi, kontotilgang og
omplanlegging uten duplikater. Norsk UI er kontrollert i nettleser med Oslo som
valgt, søk etter Oslo/Europe/America/Chicago, tastaturvalg av Chicago, lagring og
omlasting. Windows-runtime og skjermleser er ikke kjørt i dette miljøet. Testene
bruker simulert transport; UI-testen har bakgrunnsutsending deaktivert.
