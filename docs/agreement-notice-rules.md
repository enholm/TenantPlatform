# Avtaleformer og oppsigelsesregler

## Modell og autoritativ datokilde

`Agreement.Form` skiller tidsbegrenset avtale, avtale med fornyelse og løpende
avtale uten fornyelse. `Legacy` er kompatibilitet for eksisterende, tvetydige
avtaler. Nye avtaler må velge en av de tre avklarte formene.

- Tidsbegrenset krever sluttdato, uten fornyelsesfelt; manuell oppsigelsesfrist er valgfri.
- Fornyelse krever neste fornyelsesdato; oppsigelsesregel er ingen, manuell eller
  beregnet før fornyelse. Automatisk fornyelse og fornyelsesmåneder er fortsatt
  metadata. Det finnes ingen automatisk fornyelsesjobb, import eller API som
  endrer fornyelsesdatoen utenom AgreementService. Ingen slik jobb er innført.
- Løpende krever positiv oppsigelsestid og dager/måneder, uten sluttdato,
  fornyelsesdato, fornyelsesperiode eller fast oppsigelsesfrist. Vilkår er valgfri fritekst.

For beregnet frist er `RenewalDate`, `NoticeCount` og `NoticeUnit` autoritative.
`NoticeDeadline` lagres for eksisterende søk/oppfølging, men beregnes på serveren
ved hver lagring og synkronisering. En klientlevert beregnet dato ignoreres.
Manuell dato endres ikke når fornyelsesdatoen endres.

`AgreementNoticeCalculator` bruker DateOnly.AddDays/AddMonths, med negativt
antall før fornyelse og positivt antall etter oppsigelsens virkningsdato.
31.03.2027 minus én måned blir 28.02.2027; 31.01.2028 pluss én måned blir
29.02.2028. Ingen helge-/helligdagsjustering, avrunding eller 30-dagersmåneder.
Oppsigelsesfrist før startdato tillates. Ugyldig enum, manglende/ikke-positiv
varighet og dato-overflyt gir lokaliserte valideringsfeil. Varigheten er et heltall.

## Registrering, korrigering og tilbaketrekking

Detaljsiden bruker `RecordTerminationAsync` i eksisterende AgreementService.
Bare løpende avtaler kan registrere oppsigelse. Én aktiv registrering lagres på
Agreement, sammen med virkningsdato, snapshot av varighet, registrerende bruker
og UTC-tid. Det finnes derfor ingen liste som kan få flere aktive registreringer.
Avtalestatus endres ikke. Ingen juridisk oppsigelse eller e-post sendes til motparten.

Opphørsdato er avledet fra virkningsdato pluss den **lagrede** varigheten.
Endring av generell oppsigelsestid påvirker bare fremtidige registreringer.
Korrigering krever begrunnelse og bruker opprinnelig snapshot-varighet; original
registrerende bruker/tid beholdes, mens korrigerende bruker/tid føres i historikken.
For å bruke nye vilkår på en oppsigelse må eksisterende registrering trekkes tilbake
og en ny registreres eksplisitt.

Tilbaketrekking fjerner den aktive registreringen og trekker tilbake opphørsfristen.
Registrering, korrigering, tilbaketrekking, vilkårsendring og periodebytte føres som
append-only poster i `agreement_notice_history`, med før-/etter-snapshots i JSON,
aktør, UTC-tid og eventuell kommentar. Det finnes ingen redigerings- eller
sletteoperasjon for historikken. Tidligere verdier vises på detaljsiden.

Bytte av avtaleform varsler hvilke felter som fjernes, og normal lagring bekrefter.
Irrelevante verdier normaliseres på serveren og tidligere verdier beholdes i
historikken. Aktiv registrert oppsigelse må trekkes tilbake før formbytte.

## Perioder, frister og påminnelser

Eksisterende AgreementDeadline/PeriodStartDate brukes videre. Agreement har en
egen `CurrentPeriodStartDate`, initialisert fra opprinnelig startdato. En vanlig
korrigering av StartDate endrer ikke periodeidentiteten. Ved faktisk fornyelse
velges «Dette er en faktisk ny avtaleperiode» i redigeringsskjemaet og en senere
periodestart angis. Neste fornyelsesdato må være etter denne. Dette er en eksplisitt
registrering av periodebytte, ikke en automatisk fornyelsesjobb.

Datoendring erstatter fristforekomsten etter eksisterende regler, men beholder
periodeidentiteten. Ny periode erstatter alle gjeldende forekomster også når
fristdatoen er uendret. Nye forekomster er ubehandlet. Gamle kommentarer,
ferdigstatus og påminnelseslogger beholdes på gamle forekomster.

Løpende avtaler får ingen frist før oppsigelse er registrert. Deretter brukes den
avgrensede fristtypen `Cessation` («Planlagt opphør»); EndDate er fortsatt tom,
så samme opphør får ikke både utløps- og opphørsfrist. Eksisterende oversikt,
filtre, oppfølging og lokaliserte e-postmaler støtter denne typen.

Synkronisering, historikk, erstatning av frister, kansellering av ventende og
automatisk repeterbare mislykkede påminnelser, samt ny Revision lagres atomisk.
Eksisterende køplanlegger etablerer påminnelser for nye forekomster ved neste
kjøring. Unik køidentitet, mottakertilgangssjekk og vern mot opphentingsstormer
er uendret. En transport som allerede har sendt kan ikke tilbakekalles; eksisterende
kontroll umiddelbart før sending gjelder fortsatt.

Alle interaktive operasjoner bruker konto-/medlemskapskontroll og eksisterende
lese-/redigeringsrettigheter, eksplisitt AccountId og kortlivet fabrikk-DbContext.
Agreement.Revision serialiserer også oppsigelse mot samtidig redigering/fornyelse.

## Migrering

Generert med `bash scripts/add-migration.sh AddAgreementNoticeRules`.
Kun den nye migreringen har fått datamapping:

- RenewalDate satt → fornyelse.
- EndDate satt, ingen RenewalDate/AutoRenew/RenewalMonths → tidsbegrenset.
- Ellers → Legacy, inkludert AutoRenew uten neste fornyelsesdato.

Alle eksisterende NoticeDeadline-verdier blir manuelle. Ingen varighet eller dato
finnes på. CurrentPeriodStartDate settes til StartDate. Eksisterende forekomster,
ferdigstatus, historikk, Revision, initialiseringsflagg og påminnelseskø røres ikke.
Migreringen starter ingen jobb. Legacy kan fortsatt redigeres uten tap av gamle
datoer, men skjemaet ber tydelig om avklaring. Eksisterende automatisk migrering
ved applikasjonsstart gjelder også denne migreringen.

## Verifikasjon

Kjørt mot isolerte skjemaer i lokal `tenant_agreement_tests`, med simulert transport:

```sh
dotnet build TenantPlatform.sln
dotnet run --project tests/TenantPlatform.AgreementNotice.SmokeTests
dotnet run --project tests/TenantPlatform.Agreements.SmokeTests
dotnet run --project tests/TenantPlatform.AgreementFollowup.SmokeTests
```

Testprogrammene krever `AGREEMENT_TEST_CONNECTION` og avviser andre databasenavn.
Notice-testene bygger gammelt skjema, legger inn eksisterende manuelle avtaler,
ferdig oppfølging og kø, og migrerer deretter til ny modell. De dekker beregninger,
serverautoritet, manuell frist, periodeidentitet, historikk, registrering/snapshot,
korrigering, tilbaketrekking, kansellering, idempotens, tenant-/rolletilgang og
samtidig registrering og samtidig periode-/regelendring. De eksisterende testsettene dekker fortsatt dokumentarkiv,
tilgangsstyring og køens leveranse-/retry-/tilgangsvern. Oppfølgingstestene bruker
eksplisitte legacy-fixtures for å bevare opprinnelige fristkombinasjoner.

Norsk UI er kontrollert i nettleser mot den isolerte databasen: umiddelbar
beregningsvisning (månedsslutt og endret antall), lagring av beregnet regel,
formbytte til løpende, registrering 15.01.2027 → 15.04.2027, begrunnet korrigering
og tilbaketrekking. Engelsk og svensk er ressurskontrollert, ikke manuelt gjennomgått
i nettleser. Automatisk fornyelse og juridisk utsendelse inngår ikke.

Ingen delt database eller produksjon er migrert. Ingen reelle e-poster er sendt.
Alle nye ressurser finnes i nb-NO, en-GB og sv-SE.
