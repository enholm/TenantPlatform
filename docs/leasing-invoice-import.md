# Leasing fase 3: fakturaimport

Fakturaimport finnes under **Leasing → Fakturaimport** og fra en anskaffelses detaljside. Originaldokument, versjonerte tolkeresultater, brukerens kontrollutkast, godkjent dokument og anskaffelsens varelinjer lagres separat. Opplasting/tolking endrer ingen økonomiske summer. Godkjenning registrerer dokumentet internt i leasingmodulen; det opprettes ingen regnskapspost eller betaling.

## Formater

- XML: UBL 2.1 `Invoice` med typekode 380 og `CreditNote` med typekode 381. UBL-versjon kan være utelatt, slik den ofte er i EHF/Peppol BIS Billing 3.0. `CustomizationID` kan være tom eller den eksplisitte BIS Billing 3.0-identifikatoren. Andre profiler, typekoder og eksplisitte versjoner avvises.
- Direkte parsing av leverandøridentitet, dokumentreferanser, datoer, valuta, leverandørens linje-ID, enhet, prisgrunnlag, linjerabatter/-tillegg, dokumentrabatter/-tillegg og separate mva-kategorier/satser. Dokumentjusteringer blir egne linjer. Prisrabatter inne i UBL `Price` er allerede reflektert i `PriceAmount` og trekkes ikke fra på nytt.
- Én dokumentvaluta. Separat avgiftsvaluta, kildeskatt, underlinjer og negative ordinære faktura-/kreditnotalinjer støttes ikke. Kreditnota bruker positive krediterte størrelser; fortegnet anvendes én gang ved godkjenning. Dette er en avgrenset importør, ikke en full EHF Schematron-validator eller Peppol-mottaker.
- PDF, PNG og JPEG: automatisk tolking når adapteren er konfigurert, ellers full manuell kontroll. Original PDF vises i nettleserens PDF-visning, bilder som bilde. XML lastes ned; strukturerte kildedata vises i kontrollbildet.
- Et dokument tilhører én anskaffelse. Ingen automatisk enhetskonvertering eller dimensjonsklassifisering. Kontroller at enheten stemmer ved matching.

Filer lagres med den eksisterende private dokumentlagringen (`AgreementDocumentStorage`), filsignaturkontroll og konfigurerbar størrelsesgrense, normalt 20 MB. XML leses uten DTD eller eksterne entiteter og med størrelsesgrense. Maksimalt 500 kontrollerte linjer, inklusive dokumentjusteringer.

## Konfigurasjon av PDF og bilder

Adapteren gjenbruker prosjektets OpenAI Responses-integrasjon og eksisterende modell/nøkkelkonfigurasjon. Automatisk ekstern fakturatolking er **av som standard**. Aktiver eksplisitt:

```json
{
  "LeasingInvoiceInterpretation": {
    "Enabled": true,
    "PdfTextExecutable": "pdftotext"
  },
  "AgreementAnalysis": {
    "Model": "<konfigurert modell med PDF/bilde og strukturert JSON-støtte>"
  }
}
```

Sett `AgreementAnalysis:ApiKey` via etablert hemmelighetskonfigurasjon, for eksempel `AgreementAnalysis__ApiKey`; ikke legg nøkler i kildekoden. Aktivering innebærer at fakturatekst eller originaldokument sendes til den etablerte OpenAI-tjenesten. Uten eksplisitt aktivering, nøkkel og modell gjøres ingen ekstern forespørsel.

Installer Popplers `pdftotext` på verten for lokalt PDF-tekstuttrekk, eller angi absolutt programsti. Alle sider leses med sideskift bevart. Ved manglende program, utilstrekkelig tekst eller en side uten brukbar tekst sendes hele PDF-en til den konfigurerte tolkeadapteren. Sett tom programsti for alltid å bruke originalen. Prosessen kjører uten shell med tids- og tekstgrenser; midlertidige filer har privat tilgang og slettes etterpå.

Adapteren bruker et eksplisitt JSON-skjema, `store=false`, ingen verktøy og instruksjoner om å behandle dokumentinnhold som ubetrodde data. Manglende felt og usikkerheter beholdes. Ingen konstruerte sikkerhetsscorer. Sidetall og eventuelle markeringer fra tolkeren bevares og vises som referanser; markeringene tegnes ikke som overlegg i PDF-en. Brukeren må alltid kontrollere innholdet.

## Kontroll og økonomi

- Kontrollbildet viser original/kildeforslag og redigerbare data ved siden av hverandre på stor skjerm. Utkast kan lagres, avvises eller tolkes på nytt. Ny tolking erstatter aldri korrigeringer automatisk. Eksplisitt erstatning krever ny innholdsbekreftelse.
- Matching mot eksisterende varelinje dokumenterer kjøpet uten å legge verdien til på nytt. Flere fakturalinjer og delleveranser kan kobles til samme linje. Dokumentert antall/netto og gjenstående udokumentert netto vises på anskaffelsen. Pris, antall og dimensjoner på eksisterende linjer bevares. Korrigering av selve kjøpet gjøres eksplisitt i anskaffelsesredigeringen med vanlig historikk og validering.
- Nye varelinjer bruker fase 2-komponenten og gjeldende dimensjons-/fordelingskrav. Dokumentjusteringer bevares eksplisitt i linjens nettobeløp og vises separat. Negative dokumentrabatter kan klassifiseres direkte eller fordeles med prosent/antall; fase 2s beløpsfordeling med negative inndata støttes ikke.
- Kjøpsdato endres ikke ved matching. Ved ny anskaffelse kan fakturadato foreslås, men kjøpsdato må bekreftes. Finansiert beløp og vilkår må registreres etter eksisterende krav.
- Pengebeløp avrundes til to desimaler, midpoint away from zero. Antall bruker inntil fire desimaler. Prisgrunnlag normaliseres til prosjektets fire desimaler for enhetspris; eventuell differanse bevares som eksplisitt linjejustering. Mva grupperes etter både kategori og sats; kumulativ avrunding fordeler gruppens øredifferanse deterministisk på linjene.
- Oppgitte linjesummer, mva-grupper og totaler må stemme eksakt med beregningen. Det finnes ingen skjult toleranse. Et eksplisitt betalingsavrundingsbeløp på høyst ±1 valutaenhet, med to desimaler, godtas i beløpet til betaling. Forskudd og betalingsavrunding endrer ikke kjøpsverdi eller ramme. Beregnede summer kan overtas med en eksplisitt kontrollhandling, med opprinnelige tolkeresultater bevart.
- Sikker duplikat: samme filhash eller samme ikke-tomme leverandøridentitet, dokumenttype og fakturanummer innen kontoen blant godkjente dokumenter. Andre treff på nummer eller dato/valuta/beløp krever begrunnet overstyring. Reverserte/avviste dokumenter blokkerer ikke korrigert ny import. Identiteter normaliseres ved å fjerne skilletegn og bruke store bokstaver; ulike land-/mva-prefikser må kontrolleres manuelt.
- Kreditnota må kobles til godkjente fakturalinjer på samme anskaffelse. Antall, netto og mva kan ikke overkrediteres. En rammeavtales regel om frigjøring må være uttrykkelig valgt før kreditnota godkjennes. Regelen kan ikke endres mens godkjente kreditnotaer finnes. Kreditering endrer ikke finansiering, renter eller leasingperiode.
- Forhåndsvisningen viser gjeldende verdi, endring, ny verdi og rammeeffekt. Godkjenning validerer på nytt og låser kontoraden i samme transaksjon som summer, dokumentkoblinger og historikk. Endret anskaffelsesrevisjon etter kontroll blokkerer gammel godkjenning. Gjentatt godkjenning/reversering gir ikke dobbel effekt.
- Godkjente dokumenter er låst. Reversering krever begrunnelse og ny rammekontroll. Avhengige krediteringer/koblinger må reverseres først. Varelinjer, kildekoblinger og originaler bevares; reverserte opprettelser vises som egne motposter i kjøpsverdien og frigjør rammen. Endrede importlinjer må korrigeres eksplisitt før reversering.

## Drift, tilgang og migrering

En vedvarende databasekø bruker behandlingslås med 10 minutters utløp og fem minutters tolketidsgrense. Den kan gjenoppta arbeid etter omstart. Teknisk status og forretningsstatus er separate. Nytt forsøk legger til et tolkeresultat og beholder brukerutkastet.

Kontoadministrator, rammeansvarlig og anskaffelsesansvarlig følger eksisterende leasingrettigheter. Ukoblede dokumenter er tilgjengelige for opplaster og kontoadministrator. Serveren kontrollerer konto og tilgang på lesing, originalnedlasting, endring og godkjenning. Ordinære logger inneholder ikke dokumentinnhold eller tolkerens svar; detaljer ligger i tilgangsstyrt behandlingshistorikk.

Migrering `20260927171506_AddLeasingInvoiceImport` er additiv: fakturaer, fakturalinjer, tolkeresultater og historikk, samt beløpsjusteringer og nullable kreditnotaregel. Eksisterende beløpsfelt får ingen verdiendring; nye motpostfelt starter på null. Kreditnotaregelen starter som uavklart. Gamle fakturanummer/-datoer vises som eldre referanseinformasjon. Ingen fiktive fakturalinjer opprettes. Dokumentvedlegg og dimensjoner beholdes.

Migreringen er verifisert i isolerte testskjemaer, ikke anvendt på applikasjonsdatabasen under implementeringen. Applikasjonens eksisterende oppstartsmigrering vil anvende den ved neste vanlige oppstart.

## Verifisering

Se [leasingtestene](../tests/TenantPlatform.Leasing.SmokeTests/README.md). De bruker PostgreSQL, produksjonens audit-interceptor, syntetiske dokumenter og mock HTTP for ekstern tolking. De dekker migrering, parsing, avstemming, delleveranser uten dobbelttelling, kreditregler/full og delvis kreditering, duplikater mellom PDF/XML, retry, samtidighet, reversering og tenant-/eiertilgang. Blazor-komponentene rendres og kontrolleres uten nettleser. Bootstrap-grid og vanlige tastaturtilgjengelige skjemaelementer gjenbrukes; manuell nettleser-/skjermlesertest og tolking mot en virkelig ekstern konto er ikke kjørt.

Format- og integrasjonsreferanser: [Peppol CreditNote](https://docs.peppol.eu/poacc/billing/3.0/syntax/ubl-creditnote/tree/), [linjeberegning](https://docs.peppol.eu/poacc/billing/3.0/rules/ubl-peppol/PEPPOL-EN16931-R120/), [betalingsbeløp](https://docs.peppol.eu/poacc/billing/3.0/rules/ubl-tc434/BR-CO-16/), [OpenAI structured outputs](https://developers.openai.com/api/docs/guides/structured-outputs), [PDF-input](https://developers.openai.com/api/docs/guides/file-inputs).
