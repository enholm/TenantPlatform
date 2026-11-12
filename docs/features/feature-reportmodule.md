# Feature: Rapportmodul med egendefinerte rapportsider

## Oppdrag

Implementer en rapportmodul hvor brukere kan bygge rapportkort fra kontrakt- og leasingmodulene, og organisere dem på egendefinerte rapportsider. Bruk Apache ECharts til diagrammer og sammenhengstrær. Følg prosjektets eksisterende arkitektur, komponenter, design og tilgangskontroll.

Undersøk først implementasjonen bak `agreements`- og `leasing`-URL-ene. Disse skal være kilden til hvilke filtre, dimensjoner, datatyper og relasjoner rapportmodulen støtter. Ikke baser implementasjonen på antatte feltnavn eller eksempeldata.

## 1. Datagrunnlag og modulavhengige valg

Brukeren skal først velge modul: Kontrakt eller Leasing.

Valgt modul bestemmer tilgjengelige:

- Filtre og filterverdier.
- Måltall og summeringsmetoder.
- Grupperingsdimensjoner.
- Relasjoner som kan vises i sammenhengstrær.
- Lenker til underliggende poster.

Gjenbruk eksisterende filterdefinisjoner, oppslagsverdier og spørrelogikk fra modulenes listesider der det er mulig. Unngå separate filterdefinisjoner som senere kan avvike fra modulene.

Ved modulbytte skal inkompatible valg fjernes eller tilbakestilles. Brukeren skal ikke kunne lagre ugyldige kombinasjoner.

**Eiendom skal ikke inngå i rapportbyggerens filtre, grupperinger eller standardrapporter.** Dette gjelder rapportmodulen og innebærer ikke at eksisterende eiendomsfunksjonalitet skal fjernes fra resten av produktet.

## 2. Rapportperiode

Hvert rapportkort skal kunne ha en rapportperiode. Støtt både relative perioder og egendefinert fra-/til-dato.

Relative perioder skal minst omfatte:

- Inneværende, forrige og neste måned.
- Inneværende, forrige og neste kvartal.
- Inneværende, forrige og neste tertial.
- Inneværende, forrige og neste halvår.
- Inneværende, forrige og neste år.
- Hittil i år.
- Siste 12 måneder, definert som et rullerende intervall frem til dagens dato.

Inneværende år og tilsvarende valg betyr hele kalenderperioden. «Hittil i år» betyr fra 1. januar til dagens dato.

Relative perioder lagres som regler og beregnes på nytt når rapporten åpnes. En rapport lagret med «Inneværende år» skal automatisk følge årsskiftet. Vis også den beregnede datoperioden slik at utvalget er tydelig.

Bruk virksomhetens konfigurerte tidssone. Definer datogrenser konsekvent, slik at siste dag i perioden inkluderes uten feil ved tidsstempler.

**Datoens betydning må være eksplisitt.** Gjenbruk modulens datofiltersemantikk der den finnes. Dersom modulen har flere relevante datoer, skal brukeren kunne velge datagrunnlag med forståelige navn. Skill eksempelvis mellom poster datert i perioden, avtaler som overlapper perioden og inntekts-/utgiftslinjer som gjelder perioden. Tilby bare alternativer datamodellen faktisk støtter.

## 3. Måltall og gruppering

Brukeren skal kunne velge et modulrelevant måltall og en støttet summeringsmetode, for eksempel antall eller sum. Tilgjengelige valg skal komme fra datamodellen.

Tidsgruppering skal støtte:

| Gruppering | Definisjon |
|---|---|
| Måned | Kalendermåned |
| Kvartal | Jan–mar, apr–jun, jul–sep, okt–des |
| Tertial | Jan–apr, mai–aug, sep–des |
| Halvår | Jan–jun, jul–des |
| År | Kalenderår |

I tillegg skal modulens definerte dimensjoner kunne brukes til gruppering, med unntak av eiendom.

Rapportperiode og gruppering skal være separate valg. Brukeren skal eksempelvis kunne velge «Inneværende år» som periode og «Kvartal» som gruppering.

Sorter tidsgrupper kronologisk og inkluder år i etikettene når utvalget går over flere år. Håndter delvise perioder og manglende verdier konsekvent. Ikke summer beløp i ulike valutaer uten en eksplisitt løsning for valuta.

## 4. Rapportkort og visualiseringer

Støtt disse visningene:

- Nøkkeltall.
- Stolpediagram.
- Linjediagram.
- Donutdiagram.
- Tabell.
- Sammenhengstre fra venstre mot høyre.

Tilby bare visualiseringer som passer til valgt datagrunnlag. Sammenhengstrær viser relasjoner og skal ikke kreve et aggregert måltall eller tidsgruppering.

Hvert kort skal lagre navn, modul, filtre, rapportperiode, eventuelt datogrunnlag, måltall, gruppering, diagramtype og relevante visningsinnstillinger.

Kort skal kunne redigeres, dupliseres, slettes og flyttes mellom rapportsider. Duplisering oppretter en uavhengig kopi.

## 5. Sammenhengstrær

Støtt følgende strukturer der relasjonene finnes i datamodellen:

- Kontrakt: Konto → avtaler → inntekts-/utgiftslinjer.
- Leasing: Konto → rammeavtaler → anskaffelser → varelinjer → utstyr.

Avklar hva «konto» tilsvarer i eksisterende datamodell, og gjenbruk denne entiteten.

Filtre og rapportperiode skal avgrense relevante poster. Behold nødvendige overordnede noder, slik at treff fortsatt vises i sin sammenheng.

Trevisningen skal støtte:

- Utvidelse og sammentrekking av grener.
- Panorering og zoom.
- Søk på relevante navn, referanser og identifikatorer.
- Treffliste som skiller poster med like navn ved å vise kontekst.
- Automatisk utvidelse av grenen frem til valgt treff.
- Markering av valgt node.
- Detaljpanel med informasjon tilpasset nodetypen.
- «Åpne»-knapp til den faktiske posten via eksisterende applikasjonsruter.

Søk skal respektere rapportens utvalg og brukerens tilgang. Bruk stabile ID-er, ikke visningsnavn, til å identifisere noder.

## 6. Egendefinerte rapportsider

Brukeren skal kunne opprette, gi navn til, duplisere og slette rapportsider.

Duplisering skal kopiere alle kort, filtre, perioderegler, visningsinnstillinger, størrelser og plasseringer. Kopien skal kunne endres uavhengig av originalen.

Sletting av en side skal kreve bekreftelse som viser hvor mange rapportkort som slettes.

Kort skal kunne flyttes med dra-og-slipp og endre størrelse. Diagrammene skal tilpasse seg kortstørrelsen. Lagre oppsett per side og støtt et lesbart oppsett på smale skjermer.

### 6.1. Egen URL per rapportside

Hver lagrede rapportside skal ha en egen, stabil URL som åpner akkurat denne siden direkte i vanlig visningsmodus. Her betyr «rapport» en rapportside med ett eller flere rapportkort; dette kravet krever ikke en egen URL for hvert kort.

- Følg prosjektets eksisterende rutemønster. Et mulig mønster er `/reports/:reportPageId`.
- Bruk en stabil identifikator. URL-en skal fortsatt fungere etter at siden får nytt navn.
- Direkte åpning, bokmerking og oppdatering av nettleseren skal vise riktig rapportside med lagrede kort og oppsett.
- En duplisert side får egen identifikator og egen URL.
- Respekter eksisterende innlogging, tenantavgrensning og rettigheter. URL-en gir ikke ekstra tilgang.
- Håndter slettede eller utilgjengelige sider med prosjektets etablerte feil- og navigasjonsmønstre.
- Ikke legg ulagrede endringer eller sensitive filterverdier i URL-en.

URL-støtten skal implementeres i denne runden for å gjøre det mulig å bruke en rapportside som brukerens startside senere.

**Utenfor denne runden:** Ikke implementer valg av startside, brukerinnstilling for startside, «Sett som startside»-knapp eller automatisk navigering til rapporten etter innlogging. Eksisterende startsideoppførsel skal ikke endres.

## 7. Visningsmodus og redigeringsmodus

Siden skal åpnes i vanlig visningsmodus.

I visningsmodus kan brukeren lese rapporter, søke i trær, utvide grener, åpne detaljer og navigere til underliggende poster. Skjul redigeringsknapper og håndtak.

«Rediger side» åpner en arbeidskopi. Her kan brukeren endre kort, rapportinnstillinger og oppsett.

- **Lagre endringer:** Validerer og lagrer arbeidskopien samlet.
- **Avbryt:** Forkaster alle endringer siden redigeringen startet, inkludert endringer på kort og flytting av kort mellom sider.

Hvis midlertidige filtre tilbys i visningsmodus, skal disse ikke overskrive rapportens lagrede standardfiltre. Vis tydelig når slike filtre er aktive.

Varsle ved navigering bort fra ulagrede endringer. Håndter samtidige endringer slik at en bruker ikke overskriver en annen brukers arbeid uten beskjed.

## 8. Tilgang, lagring og databehandling

Gjenbruk eksisterende tenantavgrensning og rettigheter. Tilgang skal håndheves på serveren for både rapportdata og underliggende poster.

Filtrering og aggregering skal utføres på serveren. Unngå å hente hele datasett til nettleseren. Store sammenhengstrær skal kunne lastes trinnvis; søk må fortsatt kunne finne relevante poster som ennå ikke er lastet inn.

Lagre rapportdefinisjoner og oppsett, og hent oppdaterte data når rapportene vises. Vis tydelige tilstander for lasting, tomme utvalg og feil.

## 9. Akseptansekriterier

Implementasjonen er klar når følgende er verifisert:

1. Modulvalg gir filtre og dimensjoner som samsvarer med eksisterende kontrakt- og leasingmoduler.
2. Eiendom tilbys ikke som filter eller gruppering.
3. Rapportperiode og tidsgruppering kan velges uavhengig.
4. Relative perioder følger kalenderen automatisk, også ved årsskifte.
5. Lagrede rapporter kan åpnes igjen med riktige innstillinger og oppsett.
6. Redigering, duplisering og flytting fungerer for kort og sider uten at kopier påvirker originalen.
7. «Avbryt» gjenoppretter alle endringer gjort i redigeringsøkten.
8. Søk i sammenhengstrær finner også skjulte noder og utvider korrekt gren.
9. Detaljpanel og åpneknapper bruker riktig post og eksisterende ruter.
10. Tenantavgrensning og rettigheter håndheves på serveren.
11. Eksisterende funksjonalitet på kontrakt- og leasingsidene fungerer som før.
12. Hver rapportside kan åpnes direkte via en stabil URL, også etter navneendring og oppdatering av nettleseren.
13. Dupliserte rapportsider får egne URL-er, og slettede eller utilgjengelige sider håndteres korrekt.
14. Valg av rapport som startside er ikke implementert, og eksisterende startsideoppførsel er uendret.

Lag relevante tester for perioderegler, modulavhengige utvalg, tilgangskontroll, lagring/avbryt og direkte åpning via URL. Bruk fiktive data kun i tester eller eksplisitt demomodus.
