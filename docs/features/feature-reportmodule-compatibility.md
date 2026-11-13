# Rapportmodul — kompatibilitetsmatrise for første leveranse

Dato: 13. november 2026. Status: **IMPLEMENTERT KONTRAKT, VALIDERING OG KILDEKJØRING FOR AGREEMENTS/LEASING — PostgreSQL UVERIFISERT**.

Dette dokumentet er den produktgodkjente kompatibilitetsmatrisen for rapportdefinisjoner. Den samme typed matrisen ligger i `ReportCompatibilityMatrix` og brukes av katalog, lagringsvalidator, normalisering og resultatvalidering. Lokalisert tekst er presentasjon; enum- og GUID-identiteter er autoritative.

## 1. Felles regler

- Alle rapportdefinisjoner har en lagret relativ eller egendefinert rapportperiode. Perioden og eventuell tidsgruppering er separate valg.
- `KeyFigure` krever ett måltall uten gruppering. `Line` krever tidsgruppering. `Bar` krever nøyaktig én tids- eller kategorigruppering. `Table` kan brukes med eller uten gruppering.
- `Donut` tillates bare for ikke-monetære måltall med én eksklusiv kategorigruppering. Monetære, negative eller overlappende grupper avvises.
- `RelationshipTree` er relasjonsbasert: ingen metric, aggregation eller aggregeringsgruppering. Perioden avgrenser treets underliggende modulscope. Selve trekjøringen tilhører Steg 8.
- Penger får `ReportCurrencyRule.PartitionByCurrency`. Ingen omregning eller standardvaluta finnes. Resultatserier og tabeller må skilles per valuta; ett monetært KPI krever nøyaktig én valuta.
- Eiendom, `BuildingId`, `UnitId` og utstyrslokasjon er ikke filter eller gruppering. Leasing-linje-/fordelingsdimensjoner er utsatt og avvises.
- Gruppering med en `AllowMultiple`-dimensjon bruker forekomst i hver valgt gruppe. Unik total beregnes før gruppeutvidelsen; gruppesummer er merket ikke-additive, og beløp fordeles aldri mellom grupper.

## 2. Agreements

| Metric-ID | Grain | Aggregation | Datogrunnlag | Tidsgruppering | Kategorigruppering | Visualiseringer | Valuta/additivitet | Eksisterende kilde |
|---|---|---|---|---|---|---|---|---|
| `AgreementCount` | unik avtale | `CountDistinct` | `AgreementPeriodOverlap` | Ja | type, motpart, status, eier, avtaledimensjon | KPI, bar, line, donut*, table | ingen valuta; unik total før eventuell flerverdiutvidelse | `Agreement.Id`, `StartDate` til faktisk slutt (`CessationDate` eller `EndDate`) |
| `AgreementIncomeAmount` | beregnet økonomiperiode/segment for inntektsavtale | `Sum` | `AgreementInvoiceDate` | Ja | type, motpart, status, eier, avtaledimensjon | KPI, bar, line, table | alltid separat per valuta; unik sum før gruppeutvidelse | `AgreementPeriodCalculator.Calculate`, `Direction=Income`, `InvoiceDate`, skatteeksklusive `Amount` |
| `AgreementCostAmount` | beregnet økonomiperiode/segment for kostnadsavtale | `Sum` | `AgreementInvoiceDate` | Ja | type, motpart, status, eier, avtaledimensjon | KPI, bar, line, table | alltid separat per valuta; unik sum før gruppeutvidelse | `AgreementPeriodCalculator.Calculate`, `Direction=Cost`, `InvoiceDate`, skatteeksklusive `Amount` |

\* Donut er bare gyldig med en eksklusiv fast gruppering eller en dynamisk dimensjon med `AllowMultiple=false`, og runtime-resultatet kan ikke være negativt eller overlappende.

### Agreements-datoer og dobbelttelling

- Avtaleantall inkluderer en avtale når avtaleperioden overlapper rapportperioden. Ved tidsgruppering telles unik `Agreement.Id` i hver kalenderbucket den overlapper. Dette er forekomster på tvers av tid; totalen beregnes separat som unike avtaler i hele utvalget.
- Økonomiske rapporter bruker den beregnede `InvoiceDate` fra eksisterende billing-timing-regler. `AgreementPeriodCalculator` fordeler avrunding over logisk `EventKey`; rapportkjøringen skal summere kalkulatorresultatet én gang før dimensjonsutvidelse.
- «Gjelder perioden» som økonomisk datogrunnlag og ny dagperiodisering er uttrykkelig utsatt. `AgreementPeriodOverlap` kan derfor ikke kombineres med økonomiske metrics.
- Agreements-listens eksisterende søk, status, type, eier, attention og autoriserte avtaledimensjonsfiltre er filtergrunnlaget. Matrisen endrer ikke liste- eller arkivsemantikk.

## 3. Leasing

| Metric-ID-er | Grain | Aggregation | Datogrunnlag | Tidsgruppering | Kategorigruppering | Visualiseringer | Valuta/additivitet | Eksisterende kilde |
|---|---|---|---|---|---|---|---|---|
| `LeasingFrameworkCount` | unik rammeavtale | `CountDistinct` | `LeasingFrameworkAcquisitionWindowOverlap` | Ja | kind, status, eier, avtalenivådimensjon | KPI, bar, line, donut*, table | ingen valuta; unik total før gruppeutvidelse | `LeasingFramework.Id`, `AcquisitionFrom`–`AcquisitionTo` |
| `LeasingAcquisitionCount` | unik anskaffelse | `CountDistinct` | `LeasingAcquisitionPurchaseDate` | Ja | kind, status, eier, effektiv avtalenivådimensjon | KPI, bar, line, donut*, table | ingen valuta; unik total før gruppeutvidelse | `LeasingAcquisition.Id`, `PurchaseDate` |
| `LeasingOriginalPurchaseNet/Vat/Gross` | anskaffelse | `Sum` | `LeasingAcquisitionPurchaseDate` | Ja | kind, status, eier, effektiv avtalenivådimensjon | KPI, bar, line, table | separat per valuta; unik sum før gruppeutvidelse | `NetTotal`, `VatTotal`, `GrossTotal`; opprinnelig kjøpsverdi |
| `LeasingAdjustedPurchaseNet/Vat/Gross` | anskaffelse | `Sum` | `LeasingAcquisitionPurchaseDate` | Ja | kind, status, eier, effektiv avtalenivådimensjon | KPI, bar, line, table | separat per valuta; unik sum før gruppeutvidelse | netto: `NetTotal-CreditNetTotal-ReversedNetTotal`; MVA tilsvarende; brutto er netto+MVA etter justering |
| `LeasingCapacityUsedNet/Vat/Gross` | rammeavtale, nåbilde | `Sum` | `LeasingFrameworkAcquisitionWindowOverlap` | **Nei** | kind, status, eier, avtalenivådimensjon | KPI, bar, table | separat per valuta; `CurrentSnapshot`; ikke historisk additiv | eksisterende `Capacity`: registrerte anskaffelser, netto `NetTotal-ReleasedNetTotal`, MVA tilsvarende; brutto følger `LeasingCalculator.Used` |

\* Samme donutbegrensning som for Agreements count.

### Leasing-grain, filtre og datoer

- Framework-metrics godtar kind `All`/`Frameworks` og framework-status `Open`/`Closed`/`Finished`.
- Acquisition-metrics godtar kind `All`/`Acquisitions`/`StandaloneAcquisitions` og acquisition-status `Draft`/`Registered`/`Cancelled`.
- Søk, eier og effektive avtalenivådimensjoner følger den delte Leasing-listekatalogen. Anskaffelser under rammeavtale bruker fortsatt de lagrede, arvede dimensjonssnapshotene.
- Original og justert kjøpsverdi er kohortverdier på anskaffelsens `PurchaseDate`; de er ikke hendelseshistorikk for tidspunktet kreditnotaen ble registrert.
- Kapasitetsforbruk er en nåverdi for rammeavtaler hvis anskaffelsesvindu overlapper rapportperioden. Rapportperioden avgrenser hvilke rammeavtaler som inngår, men måltallet beregnes fra dagens registrerte anskaffelser og frigivelser. Tidsgruppering og line chart avvises for å hindre at nåverdien fremstilles som historisk utvikling.
- Reserverte ordre inngår ikke i metricen «kapasitetsforbruk etter frigivelse»; den bruker eksisterende `Used`-regel. Eventuelle senere metrics for limit, reservation eller available må få egne identiteter og produktbeslutning.

## 4. Relasjonstre

| Modul | Grain | Datogrunnlag | Metric/aggregation | Gruppering | Visualisering | Relasjonskilde |
|---|---|---|---|---|---|---|
| Agreements | `Relationship` | `AgreementPeriodOverlap` | ingen | ingen | `RelationshipTree` | konto → autoriserte avtaler → økonomilinjer |
| Leasing | `Relationship` | `LeasingAcquisitionPurchaseDate` | ingen | ingen | `RelationshipTree` | konto → autoriserte rammeavtaler → anskaffelser → varelinjer → utstyr |

P6 om presentasjon av frittstående Leasing-anskaffelser er fortsatt åpen og blokkerer implementasjonen av treet, men ikke den strukturelle definisjonsvalideringen.

## 5. Definisjonsvalidering

`ReportDefinitionCompatibilityValidator` kontrollerer samlet:

1. module, metric, aggregation, date basis og visualization mot matrisen;
2. perioderegel med `ReportPeriodSelection.Validate`;
3. filterverdier og dynamiske GUID-er mot brukerens autoriserte `ReportModuleCatalog`;
4. Leasing kind/status mot metricens framework/acquisition-grain;
5. fast og dynamisk grouping mot metricens grain;
6. gjensidig utelukkelse mellom tids- og kategorigruppering;
7. visualiseringskrav og donutens definisjonsbaserte sikkerhetskrav;
8. ekskludering av utsatt linje-/fordelingsscope og ukjente enumidentiteter.

Feil returneres som `ReportDefinitionValidationException.Issues`, der hvert issue har stabilt feltnavn og lokalisert resource key. `ReportPageService` validerer hele workspace-requesten før entiteter muteres og normaliserer aldri en lagringsrequest i stillhet.

## 6. Resultatvalidering

Noen krav kan først avgjøres etter kjøring. `ReportResultMetadata` beskriver observerte valutaer, om verdier er partisjonert per valuta, negative verdier og overlappende grupper. `ReportResultValidator` håndhever:

- monetære resultater må ha gyldig valuta;
- flere valutaer krever separate serier/resultater;
- monetært KPI krever nøyaktig én valuta;
- donut avvises dersom resultatet har negative verdier eller overlappende grupper.

Kontrakten beregner ikke resultater og omregner ikke valuta. Steg 4–5 fyller metadata fra faktisk autorisert resultat og stopper resultatet når valideringen feiler. Dette er databasefritt verifisert; Npgsql-oversettelse er ikke verifisert.

## 7. Normalisering for senere UI

`ReportDefinitionNormalizer` er ren og deterministisk. Den beholder kataloggyldige filtre og kompatible valg, men fjerner feil module-identiteter, feil grain, ugyldig date basis/aggregation/grouping/time grouping og inkompatibel visualization. Ved både tids- og kategorigruppering foretrekkes tidsgruppering bare for `Line`; ellers beholdes kategorigrupperingen. KPI fjerner gruppering.

Normalisering returnerer eksplisitte feltendringer og velger aldri valuta, metric, date basis eller økonomisk semantikk som erstatning. Samme input etter første normalisering gir ingen nye endringer. Serverlagring bruker validatoren, ikke normalisering.

## 8. Utsatt

- Økonomisk Agreement-periodisering etter «gjelder perioden».
- Leasing-linje-/kostnadsfordelingsdimensjoner.
- Nye metrics utover tabellene, inkludert kapasitetshistorikk, limit, reserved og available.
- Valutaomregning.
- Rapport-UI, ECharts og endelig visningspolicy utover de implementerte resultatkontraktene.
- P6 og selve sammenhengstreet.
