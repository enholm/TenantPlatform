# Avtaleregister og dokumentarkiv

## Sider og struktur

- `/agreements`: tilgjengelige avtaler, søk på tittel/motpart, filtre for status/type/ansvarlig og serverbasert sideinndeling (25 per side).
- `/agreements/create`: opprettelse for AccountAdmin.
- `/agreements/{id}`: avtaledata, dokumenter, opprettet/endret-informasjon og tilgangsadministrasjon.
- `/agreements/{id}/edit`: redigering.
- `/agreements/documents/{documentId}/download`: autentisert nedlasting med vedleggsfilnavn, `no-store` og `nosniff`.

Domenet ligger i Core/Agreements, EF-konfigurasjon og lagring i Infrastructure,
og applikasjonsservice/DTO-er og Blazor-sider i Web. Ressurser er oppdatert i nb-NO,
en-GB og sv-SE. Ingen nye pakker eller generelle rammeverk er introdusert.

## Modell og tilgang

Agreement bruker eksisterende Organization som motpart, uten krav om innlogging
eller egen plattformkonto. Bygg og leieenhet er valgfrie. Avtaleansvarlig må være
en aktiv bruker med medlemskap i samme konto. Datoene er DateOnly; historikktider
er UTC. Sluttdato må være minst startdato. Oppsigelsesdato kan være før startdato
og gjelder eksplisitt gjeldende periode. Positiv fornyelsesperiode er valgfri
og krever automatisk fornyelse. Status endres manuelt; ingen datoavhengige jobber.

AccountAdmin kan opprette og administrere alle kontoens avtaler. Avtaleansvarlig
kan redigere, laste opp og administrere tilganger, inkludert eierskifte.
Read gir lesing/nedlasting; Edit gir i tillegg redigering/opplasting, men ikke
eierskifte eller tilgangstildeling. AccountAdmin og ansvarlig beholder sine
rettigheter uavhengig av eksplisitte tildelinger. Byggroller og global
plattformadministratorrolle gir ingen ekstra avtaletilgang.

Alle serviceoperasjoner kontrollerer gjeldende AccountId, aktivt medlemskap og
avtaletilgang. En tidligere kontobruker mister også gamle tildelinger.
Navigasjonen bruker samme grunnregler via TenantAuthorizationService.
Medlemskap kontrolleres på nytt ved hver operasjon; tillatelser i UI er ikke
sikkerhetsgrensen. Globale User-oppslag brukes bare for navn til referanser i
allerede autoriserte avtaler.

Kortlivede DbContexts opprettes gjennom IDbContextFactory. Ingen context holdes
under filoverføring. Opplasting kontrollerer rettigheter både før og etter
streaming. Revision er en Guid concurrency-token. Redigering, tilgangsendring
og opplasting oppdaterer samme revisjon; utdaterte og samtidige endringer avvises
med beskjed om å laste siden på nytt. Opprettet/endret viser siste endring, ikke
en full revisjonslogg.

Dokument- og tilgangstabeller bruker sammensatte konto/avtale-fremmednøkler.
Eksisterende organisasjons-, bygg-, enhets- og brukerreferanser følger
prosjektets enkle fremmednøkkelmønster med kontovalidering i servicen.
Restrictive delete beskytter avtaler og dokumenthistorikk. Organisasjoner,
bygg og enheter får også en lokalisert slettesperre. Ingen slettefunksjon for
avtaler/dokumenter er tilgjengelig. Medlemskap kan fortsatt fjernes uten å
slette historikk.

## Lagring og konfigurasjon

Standard er en lokal katalog utenfor wwwroot:
`App_Data/agreement-documents`, relativt til applikasjonens content root.
Katalogen er ignorert i Git og opprettes ved første opplasting.
Konfigurer en varig, skrivbar katalog før drift:

```json
{
  "AgreementDocuments": {
    "RootPath": "/var/lib/tenantplatform/agreement-documents",
    "MaxFileSizeBytes": 20971520
  }
}
```

Miljøvariabler: `AgreementDocuments__RootPath` og
`AgreementDocuments__MaxFileSizeBytes`. Standardgrensen er 20 MiB.
Oppstart avviser katalog direkte under wwwroot og ikke-positive grenser.
Ikke eksponer lagringskatalogen via webserver, statisk filtilbyder eller symlenker.
Flere applikasjonsinstanser trenger samme varige lagringsvolum.
Sikkerhetskopier database og dokumentvolum samlet.

Hver opplasting får en ny tilfeldig lagringsnøkkel, uavhengig av filnavn.
Klientens MIME-type brukes ikke: PDF/PNG/JPEG kontrolleres med filsignaturer,
og DOCX/XLSX med ZIP-struktur og OpenXML-innholdstyper, med grenser for
arkiv/XML-størrelse og avvisning av VBA-prosjekter. Dette er filtypevalidering,
ikke antivirus eller full dokumenttolking. Nedlasting skjer som attachment.

Filen strømmes til en midlertidig fil med kontroll av faktisk antall byte.
Etter validering flyttes den til endelig nøkkel før metadata lagres.
Avbrudd og valideringsfeil rydder midlertidig fil; mislykket metadataregistrering
rydder endelig fil når databasen bekrefter at metadata ikke finnes.
Ved ukjent commit-resultat/bevisst utilgjengelig database beholdes filen og
hendelsen logges, slik at en mulig fullført registrering aldri mister innholdet.
Prosesskrasj kan etterlate uregistrerte eller midlertidige filer; automatisk
oppryddingsjobb og distribuert transaksjon inngår ikke. Ingen eksisterende
dokumentfil overskrives.

## Migrering

`20260916143423_AddAgreementsAndDocumentArchive` oppretter agreements,
agreement_access og agreement_documents med indekser, fremmednøkler og
valideringsbegrensninger. Generert med:

```sh
bash scripts/add-migration.sh AddAgreementsAndDocumentArchive
```

Bruk prosjektets vanlige migreringsprosedyre i autorisert miljø:

```sh
bash scripts/update-database.sh
```

Applikasjonen anvender også ventende migreringer ved oppstart.
Implementasjonen er bare migrert mot isolert testdatabase, ikke eksisterende
delte databaser eller produksjon.

## Verifikasjon

```sh
dotnet build TenantPlatform.sln
AGREEMENT_TEST_CONNECTION='Host=127.0.0.1;Port=55449;Username=agreement_test;Database=tenant_agreement_tests' \
  dotnet run --project tests/TenantPlatform.Agreements.SmokeTests
```

Testene krever en disponibel PostgreSQL-database med nøyaktig navnet
tenant_agreement_tests og oppretter et unikt skjema per kjøring. De starter
ikke Web-host eller e-postarbeidere. Eksisterende skjemaer slettes ikke.
Testfiler får egen midlertidig katalog.

Kjørt 16.09.2026: full migreringskjede og samsvar mellom snapshot/modell,
kontoisolasjon, medlemskap, rolle-/tildelingsskille, krysskontoreferanser,
kontraktsdatoer, fornyelse, samtidige lagringer, dokumenttilgang, filtype/størrelse,
avbrutt opplasting, opprydding ved revisjonskonflikt og databasebeskyttelse.
Praktisk nettleserkontroll mot isolert database: opprettelse uten eiendom/sluttdato,
oppsigelsesdato før startdato, redigering/status, listefilter, tilgangstildeling,
PNG-opplasting, nedlasting og avvisning av falsk PDF. Ingen ekte e-post ble sendt.

Automatiske varsler/fornyelser, fysisk sletting, full dokumentversjonering,
OCR, signering og økonomifunksjoner er utenfor denne versjonen.

