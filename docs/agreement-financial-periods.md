# Avtalelinjer og forventede inntekter/kostnader – fase 1–3

Fase 4–5 viderefører denne modellen. Se [indekser, justeringer og grunnlag](agreement-adjustments-basis.md)
for utvidede prisregler, segmentering, avrunding og lagrede grunnlag. Avgrensningene
nedenfor beskriver den opprinnelige leveransen av fase 1–3.

## Spesifikasjon og beslutninger før implementering

Hovedavtalen er eksisterende Agreement med motpart, rettigheter og dokumentarkiv.
Den har én økonomisk retning (inntekt/kostnad) og én valuta. Eksisterende avtaler
beholdes uten antatt retning eller valuta inntil dette registreres. Nye
leverandøravtaler foreslår kostnad. Retning og valuta låses når en linje aktiveres;
motpart låses også da, slik at historiske dokument-/kundereferanser ikke flyttes.

En avtalelinje har stabil identitet. Linjeversjoner lagrer navn, beskrivelse,
leveransestart/-slutt, første betalbare dato, periode-/fakturaregler, status,
leveransegruppe og dokumentreferanser. En prisversjon lagrer antall og enhetspris
med egen virkningsdato. En leveransegruppe er en valgfri navngitt gruppering av
uavhengige linjer på samme avtale, uten felles økonomisk behandling.

Alle versjoner har registreringstid i UTC og aktør, atskilt fra virkningsdato.
Ingen linjer eller versjoner hard-slettes. Utkast kan få nye snapshots før
aktivering. Aktiverte leveranse-/betalingsstarter fryses; nye periode-/prisregler
må gjelde fra en senere periodegrense. Linje- og prisversjoner er append-only.
Pris/antall endres samlet som ny prisversjon. Det finnes ingen generell pause.

UI-datoer for leveransestart og siste leveransedag er inkluderende. Internt brukes
halvåpne intervaller [fra, til). Avslutning tar virkning dagen etter siste
leveransedag; deaktivering tar virkning fra oppgitt dato og krever begrunnelse.
Tidligere perioder beholdes ved begge handlinger. Avsluttede/deaktiverte linjer
gjenåpnes ikke i denne versjonen; en ny linje kan opprettes.

Leveransestart er når retten/tjenesten begynner. Første betalbare dato er når
separat betaling begynner. Før dette vises inkluderte perioder med beløp null.
Første betalbare dato kan angis direkte eller beregnes fra en annen linjes
leveransestart + et helt antall måneder. Kilde-ID, kildedato, måneder og resultat
lagres. Referanser må tilhøre samme avtale; sirkler avvises. Verdien beregnes ved
lagring/aktivering av utkast; aktiverte snapshots endres aldri ved kildeendring.

Beløp er per enhet per valgt frekvens: engang, måned, kvartal, halvår eller år.
Kalenderforankring bruker månedsstart, kvartalene jan/apr/jul/okt, halvårene
jan/jul og kalenderår. Datoforankring bruker opprinnelig dato + n * frekvensens
måneder. Hver grense beregnes fra originalen, så 31. januar → februar → 31. mars.
Forskudd planlegges på den faktiske betalbare periodens første dag; etterskudd
på dagen etter siste dag. Ingen forfalls-/helgedagsregel eller ekstern utsendelse.

Delbeløp = antall × enhetspris × aktive kalenderdager / hele referanseperiodens
kalenderdager. Decimal brukes hele veien. Hver betalbar rad avrundes til valutaens
minste enhet, MidpointRounding.AwayFromZero; summer summerer avrundede rader.
Ingen eksisterende valuta-/avgiftshåndtering er funnet. Første versjon begrenses
til NOK, SEK, DKK, EUR, GBP og USD (to desimaler), beløp ekskl. avgift. Dette er
forventede beløp, ikke regnskapsført inntekt eller regnskapsmessig periodisering.

Forhåndsvisningen filtreres på faktisk periodeoverlapp, ikke fakturadato. Søke-
intervallet klipper aldri beløpet. Engang har én stabil logisk hendelse per linje,
på første betalbare dato; forhåndsvisning forbruker den ikke og avslutter ikke
leveranseretten. Beregningen tar eksplisitte datoer og leser ingen klokke.

## Akseptansescenarioer

1. Parkering starter 16.01.2027, månedlig kalenderpris 3100: 16/31 × 3100 = 1600.
   En eksisterende leielinje på 10000 får fortsatt 10000 for januar.
2. Lisens leveres 15.03.2027, engang 24000. Årlig vedlikehold 12000 forankret
   15.03.2027, inkludert 12 måneder: første år 0; første betaling 15.03.2028
   gjelder 15.03.2028–14.03.2029 og er 12000. Lisensens sluttdato forblir tom.
3. Kalenderlinjer med pris 100 per valgt frekvens gir i 2027 henholdsvis
   12/4/2/1/1 betalingshendelser for måned/kvartal/halvår/år/engang.
4. Månedlig januarpris 3100, siste leveransedag 10.01.2027: 10/31 × 3100 = 1000.
   Andre linjer fortsetter. Deaktivering fra 11.01 gir samme økonomiske grense,
   men bevarer en separat status og begrunnelse.
5. Kostnadsavtalen gir bare kostnadssummer, aldri utgående fakturagrunnlag.
6. To linjer kan referere samme dokument-ID. Dokument fra annen avtale/konto
   avvises på serveren og i databasereferansene.
7. Forankring 31.01.2028 gir grensene 31.01, 29.02, 31.03 og 30.04.
8. Kalenderpris 100 i januar, ny pris 120 fra 01.02.2027: januar 100, februar 120.
   Endring fra 15.02 avvises. Søk bare 10.–12. januar viser fortsatt januarbeløp 100.

Ingen indeksregulering, fakturakjøring, betaling, purring, selvbetjening eller
økonomiintegrasjon innføres. Fremtidige faser kan bruke stabile linje-/versjons-
og hendelsesidentifikatorer. Account er tenantgrensen; motpart er eksisterende
Organization innen denne kontoen, ikke en ny tenantmodell.

## Implementering og migrering

Domenet ligger i `Core/Agreements/AgreementLine.cs`. EF-konfigurasjonen bruker
sammensatte fremmednøkler som låser linjer, kildehenvisninger, leveransegrupper og
dokumentreferanser til samme AccountId og AgreementId. Applikasjonstjenesten
`AgreementService.Lines.cs` gjenbruker avtalens eksisterende tilgangskontroll og
revisjonskontroll. Hver operasjon bruker kortlivet factory-context; lesing av
beregningsgrunnlaget skjer i én repeatable-read-transaksjon. Beregningen i
`AgreementPeriodCalculator.cs` er ren og har ingen database-/klokkeavhengighet.

UI finnes på `/agreements/{id}/lines`, med lenke fra avtaledetaljene. Norske,
engelske og svenske ressurser er oppdatert. Ingen nye pakkeavhengigheter.

Migreringen `20260919163903_AddAgreementFinancialLines` er generert med
`bash scripts/add-migration.sh AddAgreementFinancialLines`. Den legger til nullable
retning/valuta på eksisterende avtaler og nye tabeller; eksisterende avtaler og
dokumentidentiteter bevares. Sett retning og valuta eksplisitt før første linje.
Migreringen er bare anvendt på en isolert lokal testdatabase under utvikling.
Eksisterende oppstartsmigrering vil anvende den ved neste ordinære oppstart;
den er ikke anvendt mot prosjektets konfigurerte database her.

## Verifikasjon

- `dotnet build TenantPlatform.sln`: ingen feil eller advarsler.
- Ny konsollbasert testsuite etter prosjektets eksisterende testmønster:
  `tests/TenantPlatform.AgreementPeriods.SmokeTests`. Kjøres med
  `AGREEMENT_TEST_CONNECTION` satt til en isolert PostgreSQL-database med navn
  `tenant_agreement_tests`, og `dotnet run --project` mot testprosjektet.
  53 kontroller dekker scenarioene ovenfor, migrering fra forrige modell,
  samtidige/stale skriveroperasjoner, dokumentgrenser, tilgang, datert
  pris/antall/frekvens, kildeavhengigheter og bivirkningsfri gjentakelse.
- Eksisterende Agreements, AgreementFollowup og AgreementNotice smoke-tester bestod.
- Nettleser mot isolert testmiljø: lagret retning/valuta, opprettet/aktivert linje,
  bekreftet januarprognose 1600,00 (16/31), avsluttet samme linje 25. januar og
  bekreftet 1000,00 (10/31), bevart versjonshistorikk og uendret annen linje.
- Ressursfilene inneholder de samme 73 Finance-nøklene uten duplikater.
- Ingen faktiske e-poster eller eksterne økonomikall er sendt.

## Begrensninger og videre faser

Aktiverte linjer endres ved en ny grense som er senere enn sist registrerte
virkningsdato, og som er periodegrense i både gammel og ny regel. Tilbakedaterte
innsettinger mellom registrerte versjoner, omgjøring/gjenåpning og endring av
aktiverte engangslinjer støttes ikke. Engangslinjer kan avsluttes/deaktiveres.
Historiske snapshots bevares; dette er ikke en full «slik visste vi det da»-
rapporteringsfunksjon med valgfri registreringstidsgrense.

Linjenes økonomiske livsløp styres eksplisitt per linje. Hovedavtalens status,
oppsigelsesregistrering, sluttdato og arkivering avslutter ikke automatisk
linjene. Avslutt linjene før arkivering dersom prognosen skal stoppe;
arkivering låser redigering etter eksisterende regler. Ingen stille kobling til
påminnelser eller juridiske oppsigelser er innført.

Forhåndsvisning begrenses til ti år og 20 000 rader. Beløp er ekskl. avgift og
støttede valutaer har to desimaler. Ingen valutakonvertering eller avansert
prisendring midt i perioden. Leveransegrupper er enkle navngitte grupper.

Fase 4–6 kan bygge videre med indeksregister og automatiske prisjusteringer,
fakturagrunnlag/fakturakjøringer, betaling, purring, selvbetjening og integrasjon
mot økonomisystem. Ingen av disse er implementert i denne leveransen.
