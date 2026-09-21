# Indeksregulering, redigering og økonomiske grunnlag

Avtalen velger én indeks fra `/agreements/indices`. Hver linje har bare valget
«Indeksreguleres». Linjer med nei beholder gjeldende pris. Linjer med ja krever
avtalens indeks. Manuelle feilregistreringer rettes med vanlig linjeredigering.
Det finnes ingen nye linjeregler, prosenttillegg, rammer, gulv/tak eller planer.

## Automatisk beregning og historikk

Registeret lagrer **indeksnivåer**, ikke ferdige reguleringssatser. Registrer eller
korriger nivået én gang i `/agreements/indices`. Prognosen og fakturagrunnlaget
beregner deretter alle kvalifiserte linjer automatisk med samme kalkulator.
Det finnes ingen handling for å foreslå eller godkjenne en prisendring per linje.
Godkjenning av selve faktura-/kostnadsgrunnlaget beholder eksisterende rettigheter.

Eksempel: registrert månedspris 1 000 fra 2026, indeks 101 i 2026 og 105 i 2027:
2026 gir 1 000 per måned; 2027 gir 1 000 × 105 / 101 = **1 039,60** per måned.
Dette gjelder også om prognosen bare forespørres for 2027.

- Den registrerte prisen bruker indeksnivået som gjelder ved prisens virkningsdato
  (eller ved linjens start dersom den er senere) som utgangspunkt.
- Hvert senere registrerte nivå regulerer hele prisen fra indeksperiodens start:
  forrige beregnede pris × nytt nivå / forrige nivå. Hvert steg avrundes til to
  desimaler (AwayFromZero). Også nedgang brukes fullt ut.
- Mangler senere nivåer, videreføres siste kjente pris. Hvis startnivå mangler,
  blir første tilgjengelige nivå et utgangspunkt; det oppfinnes ingen sats.
- Prisrettelser oppretter et nytt prisutgangspunkt fra «Gjelder fra». Endring av
  bare antall eller betalingsfrekvens nullstiller ikke indeksberegningen.
- Slås regulering av, fryses beregnet pris. Ved påslag brukes nivået på
  påslagsdatoen som nytt utgangspunkt, uten å ta igjen den avslåtte perioden.
- Første indeksvalg på avtalen gjelder fra avtalestart. Senere bytte gjelder fra
  dagens UTC-dato (tidligst avtalestart). Indeksvalgene versjoneres, slik at byttet
  ikke omskriver tidligere beregningsperioder. Ny indeks bruker nivået på
  byttedatoen som basis og påvirker prisen ved neste registrerte periode.

Prognosen er en leseberegning: den oppretter ingen priser, reguleringsforslag
eller godkjenninger. Gjentatte kjøringer gir samme resultat og kan ikke regulere
samme pris på nytt. Nødvendige data lastes per avtale, uten databasespørringer
per linje eller indeksperiode. Tidligere manuelt godkjente prisversjoner er
prisankere og blir ikke indeksregulert dobbelt.

Hvert beregnet segment bærer sin automatiske indeksforklaring: indeksnavn/ID,
basis- og sammenligningsperioder, eksakte verdier og revisjoner, gammel/ny pris,
prosent og virkningsdato. Dette vises i prognosen og på grunnlaget. Ved generering
lagres forklaringen i grunnlagets eksisterende JSON-øyeblikksbilde. Eldre manuelle
reguleringer beholdes som lesbar historikk på avtalens reguleringsside.

Indeksregisteret har Rediger for både metadata og perioder. Perioderedigering
oppretter en ny revisjon og krever begrunnelse. Alle erstattede revisjoner bevares med `Superseded = true`, både ved endring
av verdi og flytting av periode; bare siste gjeldende revisjon deltar i ny beregning.
Dupliserte perioder, ugyldige nivåer og utdaterte redigeringsforsøk avvises.
Korrigerte nivåer brukes i nye prognoser og beregninger. Berørte utkast blir
utdaterte og må regenereres. Godkjente grunnlag endres aldri automatisk; avvik
behandles med den eksisterende korreksjonsflyten.

Indeksmetadata kan bare endres av kontoadministrator; perioderesolusjonen må være
forenlig med registrerte perioder. Kilde/URL er beskrivende metadata.

## Aktive linjer

Rediger åpner også aktive engangslinjer. Navn, beskrivelse, dokumentreferanser,
antall, pris, start/slutt, første betalbare dato, frekvens, forankring og forskudd/
etterskudd kan rettes. Dokumenter må tilhøre samme konto og hovedavtale.

Økonomiske endringer oppretter nye linje-/prisversjoner med stabil linje-ID,
aktør, tidspunkt, begrunnelse og «Gjelder fra». Datoen kan være tilbake i tid
eller inne i en betalingsperiode. Velg datoen rettelsen skal gjelde fra; ved
retting av opprinnelig start brukes den opprinnelige eller tidligere startdatoen.
En senere registrert rettelse erstatter eldre registreringer fra valgt dato,
også når den opprinnelige startdatoen var registrert for sent. Eldre versjoner
beholdes i historikken.
Beskrivende endringer og dokumentendringer oppretter linjehistorikk uten en
ny prisversjon eller krav om økonomisk begrunnelse.

Forskudd faktureres ved betalbar periodes start; etterskudd dagen etter slutt.
En oppdelt logisk betalingshendelse har én fakturadato. Siste segment bestemmer
forskudd/etterskudd, og datoen beregnes over hendelsens betalbare segmenter.
Ingen ekstra konfigurerbar fakturadatoregel eller ny fakturaintegrasjon innføres.

Linjesiden viser lenker til berørte grunnlag:

- Utkast blir utdaterte og kan ikke godkjennes før kontrollert regenerering.
  Hvis fakturadatoen er endret, kanseller utkastet og generer på nytt.
- Godkjente øyeblikksbilder forblir uendret. Bruk eksisterende «Opprett korreksjon»
  på grunnlaget for differansen mot gjeldende data og tidligere korreksjoner.
- Hvis en ny frekvens/forankring erstatter hendelsesidentiteten, krediteres den
  gamle hendelsen gjennom korreksjonsflyten. Generer de nye hendelsene separat.
  Kontroller og godkjenn korreksjonene sammen med de nye grunnlagene.
- Dokumentøyeblikksbilder på godkjent grunnlag leses ikke fra dagens referanser.

Kostnader og utgående fakturagrunnlag behandles separat. Alle tjenesteoperasjoner
verifiserer AccountId og eksisterende avtaletilgang; redigering gir ikke rett til
å godkjenne. Godkjente grunnlag, priser og historiske revisjoner overskrives aldri.

## Migrering

`20260920010742_AutomaticAgreementIndexPricing` viderefører forenklingen med
avtalens indeksvalghistorikk og intern prisbasisdato for antallsendringer.
Eksisterende indeksvalg får en historikkrad fra avtalestart. Priser, dokumenter
og godkjente øyeblikksbilder blir ikke omskrevet. Gamle ventende manuelle forslag
markeres utdaterte. Tidligere indeksbytter før denne historikken fantes kan ikke
rekonstrueres; eksisterende prisversjoner og godkjente øyeblikksbilder bevares.
Denne migreringen er bare testet mot disponibel, isolert PostgreSQL.


`20260919235529_SimplifyAgreementIndexAndLineEditing` er laget med prosjektets
migreringsscript. Den er bare kjørt i den disponible testdatabasen under utvikling.
Ingen delt database er oppdatert. Migreringen kjøres ellers av vanlig oppstart.

- Linjer uten regulering/med fast pris får nei.
- Indekslinjer får ja. Én felles indeks kan settes på avtalen.
- Flere forskjellige indekser gir ingen tilfeldig valgt indeks.
- Avanserte regler (bl.a. andel, tillegg, gulv/tak, begrenset nedgang, originalpris,
  periodeforskyvning eller annen plan/timing) og fremtidig regelkonfigurasjon gir
  `IndexSetupNeedsReview`. Også rene prosent-/rammeregler krever avklaring.
- Nåværende priser, alle prisversjoner, gjennomførte reguleringer, dokumentkoblinger
  og betalingsdatoer bevares. Ventende gamle forslag blir utdaterte.
- Leveransegruppetabellen, koblingsfeltet og aktive tjeneste-/UI-kontrakter fjernes.
  Linjene tilhører hovedavtalen direkte. Betalingsstart kan fortsatt beregnes fra
  en annen linje, og eksisterende beregnede datoer beholdes.
- Gamle regeltabeller og regel-DTO i historiske beregningsøyeblikksbilder er kun
  sporbarhet. Ingen aktiv tjeneste kan opprette slike regler, og beregningen leser
  dem ikke. Historiske avanserte forklaringer kan fortsatt vises.

Berørte avtaler viser varsel på detalj- og redigeringssiden. Brukeren
velger riktig indeks, kontrollerer linjenes ja/nei-valg og bekrefter i
avtaleredigering at fremtidig regulering bruker 100 %. Inntil da blokkeres bare
ny regulering. Vanlig redigering og behandling med gjeldende pris fungerer.

Følgende rapport kan kjøres for en autorisert konto etter migrering:

```sql
SELECT "Id", "Title", "IndexId"
FROM agreements
WHERE "AccountId" = :account_id AND "IndexSetupNeedsReview"
ORDER BY "Title";
```

Historiske detaljer finnes i `agreement_adjustment_rules`, avgrenset med samme
AccountId og AgreementId. Rapporten bør gjennomgås før første reguleringskjøring.
Nedmigrering kan ikke rekonstruere fjernede grupper og avvises eksplisitt; bruk
sikkerhetskopi ved behov for tilbakeføring.

## Verifikasjon

`TenantPlatform.AgreementProcessing.SmokeTests` bruker isolert PostgreSQL-skjema
og tester migrering av eksisterende data, automatisk prognose og grunnlag (101 → 105), mange linjer uten manuelle handlinger,
leseberegning uten sideeffekter, pris-/antallsrettelser, historiske indeksbytter,
verdi-/perioderevisjoner, aktive rettelser, utkast, godkjente grunnlag,
korreksjoner, dokumenter og konto-/rettighetsgrenser. Den inkluderer in-process
Razor-rendering av aktive linje- og indeksredigeringshandlinger med lagring.
Dette tester ikke nettleserens SignalR-/DOM-hendelser. Chrome og innebygd nettleser
var utilgjengelige for live UI-verifikasjon i utviklingsøkten.

```sh
AGREEMENT_TEST_CONNECTION='Host=127.0.0.1;Port=55449;Username=agreement_test;Database=tenant_agreement_tests' \
  dotnet run --project tests/TenantPlatform.AgreementProcessing.SmokeTests
```

## Samlet generering på tvers av avtaler

Avtaleoversikten har handlingen «Generer fakturagrunnlag» til
`/agreements/bases/generate`. Generering fra den enkelte avtalen er fortsatt
tilgjengelig. Siden finner alle tilgjengelige inntektsavtaler i valgt Account,
med filtre for avtalepart og avtale. Avsluttede avtaler utelukkes ikke på status;
periodemotoren avgjør hvilke linjeperioder som inngår. Arkiverte avtaler og
avtaler som bare kan leses, vises med forklaring og kan ikke genereres.

Intervallet gjelder planlagt fakturadato, er inklusivt og har samme grense på
366 dagers differanse som enkeltgenereringen. Standard er inneværende måned.
Forhåndsvisningen viser beregnede perioder, eksisterende grunnlag, nye beløp og
oppfølgingsbehov per avtale. Alle summer er eksklusive avgift og grupperes per
valuta. Resultatet er ikke paginert; «Velg alle» gjelder alle kvalifiserte avtaler
i hele det filtrerte resultatet. Linjedetaljer bruker samme komponent som
forhåndsvisningen fra en enkelt avtale.

`GenerateBulkBasisAsync` koordinerer den eksisterende genereringskjernen.
Hver avtale får egen kortlivet DbContext og transaksjon. Tilgang, retning,
beregningsdata og eksisterende grunnlag kontrolleres på nytt under eksisterende
konto-/avtalelås før utkast opprettes. Fingeravtrykket omfatter fakturaintervall,
økonomiske data, indeksberegning og kansellerte grunnlag. Nye hendelseskrav fra
en samtidig vellykket kjøring ugyldiggjør ikke forhåndsvisningen: disse
rapporteres som eksisterende. Kanselleringer krever derimot ny forhåndsvisning,
fordi de kan øke beløpet som skal opprettes.

Utdaterte utkast og nødvendige korreksjoner blokkerer samlet generering for den
berørte avtalen. De overskrives ikke. Feil returneres per avtale og hindrer ikke
øvrige transaksjoner. Brukerens filtre og utvalg bevares, og ferdigbehandlede
avtaler sendes ikke på nytt ved neste klikk. Etter retting kjøres ny
forhåndsvisning før nytt forsøk. Godkjenning og videre behandling skjer fortsatt
fra grunnlagsoversikten.

Kjøringen er sekvensiell og skjer mens siden er tilkoblet; ingen ny bakgrunnskø
eller varig kjøringshistorikk er innført. Ved avbrudd kan forhåndsvisning og
kjøring gjentas trygt. Svært store avtaleutvalg er ikke lasttestet.
Regresjonstestene omfatter tjenesteflyten og rendering/handlinger i de faktiske
Razor-komponentene; dette erstatter ikke en nettlesertest av visuell utforming.
