# Avtaler fase 4–5: regler før implementering

Indeksregisteret er kontospesifikt (AccountId), uten begrensning i antall indekser.
Kontoadministrator registrerer indekser og nye revisjoner av periodeverdier.
Kontomedlemmer kan lese registeret. Avtalens eksisterende les/rediger/administrer-
rettigheter gjelder videre: rediger kan lage regler, forslag og utkast;
administrer (ansvarlig eller kontoadministrator) kan godkjenne/annullere.
Ingen ekstern innhenting, e-post, fakturautstedelse eller bokføring.

Indeksperioder identifiseres med første kalenderdag i måned/kvartal/år.
Sammenligningsperiode = justeringsdato forskjøvet et eksplisitt antall måneder,
deretter starten av indeksens periode. Eksakt basis-/sammenligningsperiode kreves.
En korreksjon gir en ny uforanderlig indeksrevisjon. Regelversjoner er daterte
snapshots med dokument-ID-er fra samme avtale. Nye regler erstatter ikke historikk.

Beregning: indeksratio minus 1, multiplisert med avtalt andel. Prosentpoeng
legges deretter til satsen, eller prosentpåslag multipliseres med justert pris.
Negativ endring kan forbys; gulv og tak anvendes på samlet sats mot basisprisen
etter tillegg. Pris avrundes til to desimaler AwayFromZero først til slutt.
Opprinnelig basis bruker alltid samme pris og indeksperiode (tillegg kumuleres
ikke). Sist gjeldende pris bruker foregående gjennomførings indeksperiode,
eller eksplisitt basisperiode før første gjennomføring. Fast prosent bruker
sist gjeldende pris. Tillatt ramme krever eksplisitt valgt prosentsats innenfor
rammen; ingen pris velges automatisk. Fast pris gir ingen justering.

Justeringstakt (måneder) er uavhengig av betalingstakt. Opprinnelig ankervindu
bevares ved AddMonths; referansedato, avtaleårsdag eller fast årlig dato støttes.
Første tillatte dato og regelens virkningsdato begrenser mulige kjøringer.
Forslag lagrer eksakte revisjoner, dokumenter, datoer og beregning. Godkjenning
beregner på nytt under lås; endrede forutsetninger gir utdatert forslag.
Avvisning/godkjenning er eksplisitt, datert og tilskrevet aktør.

Prisjustering kan gjelde fra neste hele betalingsperiode eller splitte på
virkningsdatoen. Uavhengige prisversjoner kompletterer eksisterende snapshots.
Segmenter bruker samme opprinnelige referanseperiode som nevner. Avrunding
fordeles som differanser av avrundede kumulative råbeløp per betalingshendelse;
summen tilsvarer avrundet samlet råbeløp. Segmentene deler betalingshendelsens
planlagte fakturadato. Søkevinduet klipper aldri et beløp.

Betalingsidentitet er linje-ID + opprinnelig referanseperiodestart, eller
linje-ID + «once». Prisversjon, segmentgrense og kjøring inngår ikke. Etter at
grunnlag er generert, låses betalingsfrekvens/forankring på berørte linjer for
å unngå overlappende nye identiteter. Pris, avslutning og korreksjon støttes.

Generering filtrerer planlagt fakturadato og grupperer bare innenfor konto,
avtale, motpart, valuta, retning og fakturadato. Inntekt og kostnad krever hvert
sitt eksplisitte tjenestekall med forventet retning. Inkluderte nullperioder
vises i prognosen, men skaper ikke betalingsgrunnlag. Vanlige nullpriser kan
lagres som dokumenterbart nullgrunnlag. Avgift er fortsatt uspesifisert,
beløp ekskl. avgift; ingen skatteregelmotor innføres.

Utkast har uforanderlige snapshot-revisjoner. Regenerering oppretter ny revisjon
og viser før/etter; godkjenning krever korrekt revisjon og uendret kildefingeravtrykk.
Godkjente snapshots endres aldri. Annullering krever grunn og bevarer historikk.
Annullert original frigir hendelser for ny generering, men kan ikke annulleres
før godkjente korreksjoner er annullert. Korreksjoner eier ikke nye krav.
Unik aktiv hendelsesreservasjon og transaksjon beskytter samtidige kjøringer.

Korreksjon refererer original og dens hendelser. Differanse = dagens beregnede
målbeløp − (original + alle godkjente, ikke annullerte korreksjoner).
Snapshot bevarer målsegmenter, tidligere nettobeløp, differanse og referanser
til snapshots som inngår. Én åpen korreksjon per original; godkjenning validerer
både kilder og godkjent netto på nytt. Null differanse oppretter ikke korreksjon.
Godkjente grunnlag sammenlignes med gjeldende mål og viser korreksjonsbehov.

Akseptanse: 1000 og indeks100→104 gir1040; andel70% gir1028;
+2pp gir1060; ×1,02 gir1060,80. Ramme5% godtar3%, avviser6% og tomtvalg.
3100→6200 fra16.januar gir1500+3200=4700 (15/31 og16/31).
Nesteperiode-alternativet gir januar3100/februar6200. Godkjent1000→mål1100
krever+100; deretter mål1050 krever−50 (ikke+50). Omkjøring og samtidighet skal
bevise én aktiv hendelse mot PostgreSQL. Fase1–3 sine scenarioer skal bestå.

## Implementerte forutsetninger og presiseringer

Fase 1–3 sine prisversjoner var koblet én-til-én til linjeversjonens sekvens.
Nye `Independent`-prisversjoner utvider dette uten å aktivere gamle utkast.
Eksisterende data får `Independent=false`. En linje kan dermed beholde vilkår og
leveranse mens prisen får en egen virkningsdato. Samme virkningsdato kan korrigeres
med høyere sekvens; motoren velger én gjeldende revisjon per dato. Ingen to priser
behandles samtidig for samme segment. Fremtidige allerede registrerte priser
avslutter den nye prisens virkning; manuell korrigering endrer ikke dem automatisk.

Ved «sist gjeldende pris» brukes den eksakte sammenligningsrevisjonen som lå bak
forrige gjennomførte justering under regelen. En senere indekskorreksjon endrer
ikke denne historiske koblingen. Før første justering brukes siste revisjon i
regelens eksplisitte basisperiode. En mellomliggende manuell prisendring krever
en ny regel som kobler den nye prisversjonen til riktig indeksperiode. Dette
hindrer at en vilkårlig pris arver et ubegrunnet indeksgrunnlag.

Gulv, tak, andel og reduksjonsregelen gjelder endringen relativt til valgt
basispris, ikke en egen, udefinert alternativ sammenligningspris. Opprinnelig
basis regnes på nytt mot den opprinnelige prisen; siste pris kan derfor avvike
fra denne basisprisen. Beregningsvisningen viser både sats mot basis og faktisk
endring fra sist gjeldende pris.

Grunnlagssnapshots inneholder kontonavn/ID, motpartnavn/ID, avtaletittel/ID,
segmenter med beregningsdata/versjons-ID-er/dokument-ID-er, og komplette
justeringsforslag med regel og eksakte indeksrevisjoner der dette gjelder.
Aktører og tidspunkter lagres på generering, snapshot, godkjenning og annullering.
Dokumentnedlasting gjenbruker arkivets serverbaserte tilgangskontroll.

En korreksjonsdetalj refererer originalens stabile hendelses-ID, bevarer
målsegmentene og viser mål minus godkjent netto. Alle tidligere godkjente,
ikke annullerte korreksjonssnapshots inngår eksplisitt i nettoreferansene.
Nye snapshot-revisjoner er append-only, også ved regenerering av utkast.
Annullering av en korreksjon fjerner den fra netto og kan gi nytt korreksjonsbehov.
Originalen kan først annulleres når både utkast og godkjente korreksjoner er
annullert. Dens historiske snapshots og godkjenningsopplysninger beholdes.

## Kode og brukerflyt

- Domene: `Core/Agreements/AgreementFinancialProcessing.cs`; prisversjonen utvides
  i eksisterende `AgreementLine.cs`.
- Persistens: `AgreementFinancialProcessingConfiguration.cs`, eksisterende
  pris-konfigurasjon og DbContext. Sammensatte fremmednøkler bevarer kontogrensen.
- Tjenester: eksisterende `AgreementService` utvides med partial-filene
  `AgreementService.Adjustments.cs` og `AgreementService.Bases.cs`.
- Ren beregning: `AgreementAdjustmentCalculator.cs` og utvidet eksisterende
  `AgreementPeriodCalculator.cs`. DTO-er finnes i `AgreementProcessingDtos.cs`.
- `/agreements/indices`: register og verdirevisjoner.
- `/agreements/{id}/adjustments`: regelhistorikk, kommende/blokkerte justeringer,
  forslag, beslutning og datert manuell priskorrigering.
- `/agreements/{id}/basis`: forhåndsvisning/generering etter planlagt fakturadato.
- `/agreements/bases`: separate inntekts-/kostnadsoversikter.
- `/agreements/bases/{id}`: snapshots, godkjenning, regenerering, annullering og
  korreksjon. Linjesiden og navigasjonen lenker til funksjonene.
- 120 nye lokaliseringsnøkler er lagt til i alle tre språk, uten duplikater.

Alle databaseoperasjoner bruker kortlivede factory-contexts. Finansielle
skriveoperasjoner låser konto, deretter avtale, i PostgreSQL-transaksjonen.
Dette gjør også indekskorrigering og godkjenning gjensidig konsistente.
Eksisterende avtaleskriving beskyttes videre av avtalens revisjonsfelt.
Aktive hendelsesreservasjoner har et filtrert unikt indekskrav i databasen;
godkjente justeringer har unik linje/justeringsdato, og hver original kan bare
ha én åpen korreksjon. Snapshot-fingeravtrykk normaliserer desimaltall slik at
PostgreSQLs numeriske skala ikke feilaktig gjør et manuelt valg utdatert.

## Migrering og verifikasjon

Migreringen `20260919214314_AddAgreementAdjustmentsAndBases` er opprettet med
`bash scripts/add-migration.sh AddAgreementAdjustmentsAndBases`. Den legger til
nye tabeller, begrensninger og nullable justeringsreferanse / Independent=false
på prisversjoner. Ingen eksisterende avtaler eller prisverdier omskrives.
Migreringen er kun anvendt i den isolerte lokale testdatabasen. Den ordinære
oppstartsmigreringen vil anvende den ved neste oppstart av applikasjonen;
prosjektets konfigurerte database er ikke endret av dette arbeidet.

Verifisert:

- `dotnet build TenantPlatform.sln`: ingen feil eller advarsler.
- `tests/TenantPlatform.AgreementProcessing.SmokeTests`: 77 kontroller bestod,
  inkludert de obligatoriske scenarioene, PostgreSQL-samtidighet og faktiske
  unike/fremmednøkkelbegrensninger, oppgradering med eksisterende prisversjon,
  utdaterte forslag/utkast, dokumentgrenser og rolle-/kontoisolasjon.
- Eksisterende `AgreementPeriods`, `Agreements`, `AgreementFollowup` og
  `AgreementNotice` smoke-tester bestod.
- Testene kjøres som eksisterende konsollbaserte smoke-tester, uten nye pakker:
  `AGREEMENT_TEST_CONNECTION='Host=...;Database=tenant_agreement_tests;...'
  dotnet run --project tests/TenantPlatform.AgreementProcessing.SmokeTests`.
  Hver kjøring oppretter eget skjema i denne eksplisitte testdatabasen.
- Nettleser mot isolert testmiljø: opprettet indeks, registrerte 100 og 104,
  lagret årsregel på månedslinje, beregnet/godkjent 1000→1040, forhåndsviste og
  genererte grunnlag, bekreftet omkjøring uten duplikat og godkjente/låste det.
  Pris 1100 fra 16. januar ga synlig korreksjonsbehov og separat, godkjent
  differanse 30,97 NOK; originalen ble stående på 1040,00 NOK.
- Ingen faktisk e-post, fakturautstedelse eller ekstern transport er brukt.

## Avgrensninger og fase 6

Registerets metadata er uforanderlige etter opprettelse; indeksverdier korrigeres
med nye revisjoner. Regler registreres i stigende virkningsdato. Indeksoppløsning
er måned/kvartal/år, sammenligningsforskyvning er eksplisitte kalendermåneder.
Det finnes ingen interpolering, ekstern indeksimport eller bakgrunnsgodkjenning.

Søk etter kommende justeringer og generering begrenses til 366 dager per kjøring.
Grunnlagsoversikten viser siste 500 tilgjengelige grunnlag per retning.
Regenerering tar ikke inn nye, ureserverte hendelser i et eksisterende utkast;
ny generering håndterer dem. Endret planlagt fakturadato på et utkast krever
annullering og ny generering. Korreksjoner følger originalens fakturadato.
Kontolåsen er konservativ og serialiserer finansielle skriveoperasjoner innenfor
kontoen; eventuell kø-/batchoptimalisering tilhører automatiseringsfasen.

Valuta og avgiftsavgrensning fra fase 1–3 videreføres: én valuta per avtale,
NOK/SEK/DKK/EUR/GBP/USD, to desimaler, ekskl. avgift uten beregnet avgift.
Engangslinjens stabile hendelse bevares; prisjustering av slike linjer støttes
ikke i denne versjonen. Hovedavtalens oppsigelse/arkivering avslutter fortsatt
ikke linjene automatisk. Godkjente grunnlag omskrives aldri ved slike endringer.

Fase 6: eksterne økonomi-/betalingsintegrasjoner, juridiske fakturanumre og
fakturautstedelse, bokføring, betalingsordre, automatisk innhenting av indekser,
planlagte kjøringer, utsendelse og øvrig automatisert drift. Ingen «sendt»,
«betalt» eller «bokført»-status er introdusert her.
