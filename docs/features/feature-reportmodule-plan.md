# Rapportmodul — kartlegging og konkret implementasjonsplan

Dato: 13. november 2026. Status: **STEG 1–8 KILDEIMPLEMENTERT — STEG 9 DELVIS GJENNOMFØRT MED POSTGRESQL OG FAKTISK INTERACTIVESERVER/CHROME — full verifikasjonsmatrise gjenstår**.

Dette dokumentet konkretiserer [featurebeskrivelsen](feature-reportmodule.md) mot gjeldende kodebase. Den opprinnelige kartleggingen og Steg 1–8-kjøringene var databasefrie der de historiske kapitlene sier det. Den senere, isolerte PostgreSQL-/InteractiveServer-verifikasjonen står i kapittel 26 og [feature-reportmodule-verification.md](feature-reportmodule-verification.md); den endrer ikke de historiske kjøringsresultatene.

## 1. Grunnlag, avgrensning og hovedkonklusjon

Følgende er lest og brukt som autoritativt grunnlag:

- `Agents.md` og den oppgitte `AGENTS.md`-instruksen. Filene har samme innhold. Løsningen skal beholde Core/Infrastructure/Web-arkitekturen, direkte EF-tjenester i Web, korte factory-contexts og eksplisitt `AccountId`-avgrensning.
- Hele `docs/features/feature-reportmodule.md`.
- Faktisk kode for `/agreements`, `/leasing`, `/leasing/frameworks`, dagens dashboard, `/leasing/reports`, økonomiberegninger, dimensjoner, autorisasjon, EF-konfigurasjon, migrasjonsverktøy, Blazor-oppsett og eksisterende smoke-testmønster.

Hovedanbefalingen er en kontoavgrenset rapportmodul med:

1. lagrede `ReportPage`- og `ReportCard`-entiteter i Core/Infrastructure;
2. typed katalog-, side-, kjøre- og tretjenester i Web;
3. delte, modulspesifikke query scopes og filterapplikatorer som brukes både av listene og rapportmodulen;
4. server-side filtrering og aggregering;
5. en `InteractiveServer`-side på `/reports/{ReportPageId:guid}` med lokal arbeidskopi i redigeringsmodus;
6. Apache ECharts bak en liten JS-interop-komponent, samt server-side progressiv lasting av trær.

Det skal **ikke** innføres repository, MediatR/CQRS, et nytt Application-prosjekt eller et parallelt filterrammeverk. Eiendom (`BuildingId`, `UnitId`, utstyrslokasjon) skal ikke finnes i rapportbyggerens filter- eller grupperingskatalog. Valg av startside er uttrykkelig utenfor leveransen.

## 2. Faktisk teknologistakk og prosjektstruktur

| Område | Faktisk implementasjon | Konsekvens for rapportmodulen |
|---|---|---|
| Runtime | .NET 10. `TenantPlatform.Core`, `TenantPlatform.Infrastructure` og `TenantPlatform.Web` er hovedarkitekturen. Solution inneholder i tillegg `Serga.Website`. | Nye domeneentiteter/enums i Core, EF-konfigurasjon i Infrastructure og tjenester/UI i Web. Ingen ny arkitekturlayer. |
| UI | ASP.NET Core Blazor med Razor Components og `InteractiveServer`; registrert i `src/TenantPlatform.Web/Program.cs`, ruter i `Components/Routes.razor`. | Rapportbyggeren trenger interaktiv tilstand og følger eksisterende `InteractiveServer`-mønster. DbContext skal ikke leve i circuitet. |
| Presentasjon | Bootstrap 5, Bootstrap Icons, felles `app.css`, modulspesifikk `leasing.css` og enkelte scoped `.razor.css`-filer. Tre språk via `.resx`: nb-NO, en-GB, sv-SE. | Bruk eksisterende arbeidsflate, kort, tabell, fokus- og breakpointmønstre. Alle nye tekster skal finnes i tre ressurser. |
| Applikasjonskall | Blazor-komponenter kaller scoped tjenester/kontrakter direkte. Minimal API brukes hovedsakelig til login, nedlasting og eksport, se `AgreementEndpoints.cs` og `LeasingEndpoints.cs`. | Rapport-CRUD og kjøring bør være C#-tjenestekontrakter, ikke et nytt REST-lag. HTTP-endepunkt trengs bare for en senere fil-eksport hvis det inngår. ECharts mottar ferdige DTO-er via interop. |
| Database | EF Core 10/Npgsql/PostgreSQL. `IDbContextFactory<TenantPlatformDbContext>` i `Program.cs`; `ApplyConfigurationsFromAssembly` i `TenantPlatformDbContext`. Ingen global tenant-query-filter. | Alle rapportspørringer og rapportdefinisjoner må ha eksplisitt `AccountId`. Read-modeller bruker `AsNoTracking`; hver operasjon bruker kortlivet context. |
| Migrasjoner | EF-migrasjoner under `Infrastructure/Persistence/Migrations`; `scripts/add-migration.sh` bruker Infrastructure + Web startup. Runtime anvender kjeden i `DatabaseInitializer`. Offline design-time factory i `TenantPlatformDbContextDesignTimeFactory.cs` sperrer forbindelser. | Ny schemaendring skal være én ny migrasjon og snapshotoppdatering. Offline-sperren skal bevares. Ingen eksisterende migrasjon omskrives. |
| Diagrammer | Ingen ECharts, Chart.js, D3 eller annen diagramavhengighet finnes. Dashboardets donut er CSS `conic-gradient` i `InsightsWidget.razor`/`app.css`. | ECharts blir ny frontendkapabilitet. Bruk en versjonslåst lokal distribusjon og en liten, disposérbar JS-modul; ikke spre ECharts-kall i sidelogikken. |
| Drag/resize | Ingen generell dra-og-slipp- eller resize-støtte ble funnet. | Dette må implementeres som en avgrenset rapportgrid-adapter. Native HTML5 drag alene dekker ikke touch godt nok; anbefal pointer-events i liten JS-modul og tastaturknapper som tilgjengelig alternativ. |
| Validering | `EditForm`, `Input*`, `ValidationSummary`/`ValidationMessage`, native constraints og tjenestefeil med lokaliserte ressursnøkler. Ingen separat valideringsramme. | Rapportdefinisjoner valideres både i komponentens `EditContext` og autoritativt på serveren mot den typed katalogen. |
| Dato | `DateOnly` og `InputDate` brukes bredt. `AgreementReminderSchedule` beregner lokal dato fra IANA-tidssone. | Perioderegler bør være en ren `DateOnly`-resolver med injisert `TimeProvider`; sluttgrenser er inklusive. |
| Tester | Konsollbaserte smoke-prosjekter under `tests/`, flere med PostgreSQL og flere databasefrie med InMemory/pure calculators/`HtmlRenderer`. Testprosjektene ligger ikke i `TenantPlatform.sln`. | Følg eksisterende mønster med et avgrenset Reports-smokeprosjekt; kjør solution-bygg og testprosjekt eksplisitt. Providerkritisk SQL/concurrency må ha PostgreSQL-dekning. |

## 3. Kartlegging av Kontrakt (`/agreements`)

### 3.1 Scope, filtre og oppslagsverdier

Kildene er `Components/Pages/Agreements/Agreements.razor`, `Services/Agreements/AgreementDtos.cs`, `AgreementService.cs`, `AgreementService.Dimensions.cs` og `Services/Dimensions/DimensionListFilters.cs`.

| Filter | Faktisk semantikk og kilde |
|---|---|
| Søk | Case-insensitiv delstreng i `Agreement.Title` eller kontoavgrenset motpartsnavn. |
| Status | `AgreementStatus`: Draft, Active, Terminated, Expired. |
| Type | `AgreementType`: Lease, SoftwareLicense, ServiceMaintenance, Supplier, Other. |
| Eier | Synlige avtalers eiere. ID er `OwnerUserId`; visningsnavn kommer fra `User`. |
| Oppmerksomhet | `AgreementAttention`: Notice, Renewal, Expired. `AgreementDashboardQueries` bruker 90-dagers horisont for notice/renewal og aktiv/utløpt semantikk for expired. Dagens dato beregnes i kontoinnstilt tidssone. |
| Dynamiske dimensjoner | Én valgt verdi eller «Ikke angitt» per aktiv avtalemoduldimensjon. Foreldernode matcher seg selv og etterkommere. Flere dimensjoner kombineres med AND. Historiske dimensjoner/verdier tas med når de er brukt i det autoriserte datasettet. |
| Side | Side 25, sortert tittel og ID. Paginering er ikke et lagret rapportfilter. |

`DimensionListFilters.Catalog` og `Plan` er allerede et godt felles fundament: stabile GUID-er, lokaliserte navn, historiske valg, «ikke angitt», hierarkiske stier og normalisering av den tidligere flervalgsformen. Det skal gjenbrukes, ikke kopieres.

Listen tilbyr **ikke** egne dropdown-filtre for motpart, retning eller valuta. Motpart er derimot en eksisterende fast grupperingsdimensjon i dashboardet. Valuta og retning finnes i modellen og er nødvendige ved økonomiske måltall, men må introduseres som eksplisitte rapportvalg/kompatibilitetskrav, ikke feilaktig omtales som eksisterende listefiltre.

### 3.2 Grupperingsdimensjoner

`AgreementService.Dashboard.cs`, `AgreementDashboardDtos.cs` og `AgreementInsightHierarchy.cs` har allerede server-side gruppering for:

- type;
- motpart (`CounterpartyOrganizationId`);
- status;
- eier;
- hver aktiv, enabled avtalemoduldimensjon.

Hierarkiske dimensjoner skiller direkte tildeling, node/etterkommere og «ikke angitt». `AgreementInsightsDto` skiller unike avtaler fra forekomstsummen når en avtale har flervalg. Denne semantikken skal bevares i rapportkatalogen.

Eiendomsfeltene `Agreement.BuildingId` og `Agreement.UnitId` finnes i modellen og details-visningen, men skal eksplisitt utelates fra både filter- og grupperingskatalog.

### 3.3 Datoer og betydning

| Datogrunnlag | Faktisk felt/beregning | Sikker rapportsemantikk |
|---|---|---|
| Startdato | `Agreement.StartDate` | Punktdato i perioden. |
| Sluttdato | `Agreement.EndDate` | Punktdato i perioden; null betyr ingen registrert slutt. |
| Avtalen overlapper perioden | `StartDate <= to && (EndDate == null || EndDate >= from)` | Intervall med inklusive sluttgrenser. Dette finnes ikke som dagens listefilter, men støttes direkte av modellen. |
| Oppsigelsesfrist | `NoticeDeadline` | Punktdato. Dagens Attention-filter legger i tillegg på status og 90-dagers horisont; dette må ikke blandes med et generelt datofilter. |
| Fornyelsesdato | `RenewalDate` | Punktdato. |
| Opphør | `CessationDate` / `TerminationEffectiveDate` | To forskjellige datoer. De skal bare tilbys med forståelige navn dersom produktet ønsker oppsigelsesrapportering. |
| Linjen gjelder | `AgreementCalculatedPeriod.From/To` | Beregnet leverings-/opptjeningsperiode fra aktive linje- og prisversjoner. Overlapp og eventuell periodisering må være uttrykkelig. |
| Fakturadato | `AgreementCalculatedPeriod.InvoiceDate` | Avledes av advance/arrears og den logiske betalingsperioden; egnet punktdato for additive tidsserier. |
| Versjonsdatoer | `EffectiveFrom`, `RecordedUtc` | Styrer hvilken linje/pris som gjelder. Bør ikke tilbys som ordinær rapportperiode i første leveranse. |

`AgreementReminderSettings.TimeZoneId`, `AgreementReminderSchedule.Today` og `AgreementTimeZones` er den eneste etablerte kontoavgrensede «virksomhetstidssonen». Leasing gjenbruker samme innstilling/fallback flere steder. Teknisk anbefaling er å bruke denne i første leveranse, med `Europe/Oslo` som eksisterende fallback, og isolere oppslaget bak en `IReportClock`/perioderesolver slik at det senere kan flyttes til en generell kontoinnstilling.

### 3.4 Måltall og summeringsregler

| Måltall | Faktisk kilde | Regel/risiko |
|---|---|---|
| Antall avtaler | Autorisert `Accessible(...)`-scope | `COUNT DISTINCT Agreement.Id`; arkivstatus må være et eksplisitt scopevalg. Dagens liste inkluderer arkiverte, dashboard insights gjør ikke det. |
| Antall økonomilinjer | `AgreementLine` med publiserte `AgreementLineVersion`-rader | Tell stabil `LineId`, ikke versjonsrader. |
| Inntekt / kostnad | `AgreementPeriodCalculator` og `ForecastAsync` | Beløp beregnes fra aktive linjeversjoner, prisversjoner, frekvens, aktive dager, gratistid, indexprojeksjon og avrunding per logisk event. Ikke bruk `Quantity * UnitPrice` direkte over versjonsjoin. |

Valuta støttes bare i `NOK`, `SEK`, `DKK`, `EUR`, `GBP`, `USD` via `AgreementPeriodCalculator.Currencies`. Hver avtale har én valuta. Det finnes ingen kurs- eller omregningsmodell. Pengemåltall må derfor enten ha ett eksplisitt valutavalg eller returnere separate serier/totaler per valuta; en samlet sum er ugyldig.

`AgreementPeriodCalculator.Calculate` returnerer hele beregnede segmenter som overlapper forespurt intervall og avrunder per `EventKey`. Det beviser ikke automatisk en regnskapsmessig periodisering inn i måned/kvartal. Fakturadatogruppering er additiv uten ny fordelingsregel. «Gjelder perioden» med oppdeling over tidsgrupper krever en eksplisitt periodiseringsregel og egne avrundingstester.

### 3.5 Relasjoner og eksisterende ruter

- Konto er `TenantPlatform.Core.Accounts.Account`; dette er tenantroten.
- Konto → avtale: `Agreement.AccountId`.
- Avtale → økonomilinje: `AgreementLine.(AccountId, AgreementId)`.
- Linje → gjeldende/historiske definisjoner og priser: `AgreementLineVersion.LineId`, `AgreementPriceVersion.LineId`.
- Avtaledetalj: `/agreements/{agreementId}`.
- Linjeoversikt/økonomi: `/agreements/{agreementId}/lines`. Det finnes ingen egen stabil detaljrute per `LineId`; treets «Åpne» for en linje må derfor gå til denne eksisterende siden, eventuelt med en senere bakoverkompatibel fragmentmarkør dersom linjekomponenten får ID-anker.

Tredata må velge gjeldende linjesnapshot etter samme versjonsregler som økonomikoden. Å returnere alle versjonsrader som separate noder ville både duplisere linjer og feilrepresentere domenet.

## 4. Kartlegging av Leasing (`/leasing`, `/leasing/frameworks` og eksisterende `/leasing/reports`)

### 4.1 Scope, filtre og oppslagsverdier

Kildene er `Components/Pages/Leasing/LeasingList.razor`, `LeasingOverviewPage.razor`, `LeasingFrameworkListPage.razor`, `Services/Leasing/LeasingService.cs`, `LeasingService.AgreementDimensions.cs` og `DimensionListFilters.cs`.

| Filter | Faktisk semantikk og kilde |
|---|---|
| Søk | Case-insensitiv delstreng i rammeavtalens navn/nummer eller anskaffelsens navn/referanse. |
| Type/kind | `all`, `frameworks`, `acquisitions`, `orders`. I faktisk kode betyr `orders` frittstående anskaffelser (`FrameworkId == null`), ikke `LeasingOrder`-entitetene på `/leasing/purchase-orders`. Dette må få en entydig label i rapportbyggeren. |
| Status | Felles tekstfilter som parses separat mot `LeasingFrameworkStatus` eller `LeasingAcquisitionStatus`; irrelevante typer gir ingen treff. |
| Eier | `OwnerUserId` for rammeavtale/anskaffelse. |
| Dynamiske dimensjoner | Leasing-avtalenivådimensjoner, med samme single/«Ikke angitt»/etterkommer/AND-plan som Agreements. Anskaffelser under rammeavtale har snapshots av arvede valg og filtreres på disse effektive valgene. |
| Side | Side 25, sortert navn og ID. Ikke et lagret rapportfilter. |

Dagens `/leasing/reports` har i tillegg et eldre rapportfilter med finansselskap, valuta, status, fritekst, `AcquisitionsOnly`, ett eksakt dimensjonsverdi-ID-filter og datogrunnlagene Purchase/End/Due/Invoice/Event/Notice/Return. Det inneholder nyttige domene- og måltallsregler, men er **ikke** et skalerbart felles report-builder-API: det laster opptil 10 000 anskaffelser, gjør flere løkker/spørringer i minnet og beregner/paginerer etter materialisering. Det skal brukes som referanse og gradvis dele kalkulatorer/scope, ikke kalles ukritisk fra hvert nytt rapportkort.

### 4.2 To forskjellige dimensjonsnivåer

Leasing har to reelle dimensjonsmodeller som ikke må blandes:

1. **Avtalenivå**: `LeasingAgreementDimensionRule`, `LeasingFrameworkDimensionAssignment`, `LeasingAcquisitionDimensionAssignment`. Dette er det `/leasing` filtrerer på, og rammeavtalens verdier kopieres ned til underliggende anskaffelser. Dette er riktig scope for antall, kjøpsverdier og avtaletre.
2. **Linje-/fordelingsnivå**: `LeasingDimensionRule`, `LeasingDimensionSelection`, `LeasingAllocationDimension`, `LeasingAllocationRow`. `LeasingAllocationCalculator` fordeler netto/MVA eksakt over rader når fordelingen er komplett. Dagens `LeasingReportKind.Dimensions` bruker dette nivået og legger ufordelte kreditter/reverseringer i egne kontrollrader.

Anbefalt teknisk modell er separate grupperingstyper (`AgreementDimension` og `LineDimension`) med kompatibilitetsregler. Linjedimensjoner skal bare være tilgjengelige for linjebaserte kjøpsmåltall som har en definert fordelingsregel; de skal ikke dukke opp som om de var det samme som arvede avtalenivådimensjoner.

Eiendom finnes som `LeasingEquipment.BuildingId` og vises som lokasjon i utstyrsrapporten. Dette skal ikke tilbys som filter eller gruppering i rapportmodulen. Et eventuelt informativt felt i nodens detaljpanel må ikke bli en skjult filter-/grupperingsmulighet.

### 4.3 Datoer og betydning

| Datogrunnlag | Faktisk felt/beregning | Sikker rapportsemantikk |
|---|---|---|
| Rammeperiode | `LeasingFramework.AcquisitionFrom/AcquisitionTo` | Punktgruppering på fra/til eller intervalloverlapp. Dette er tillatt anskaffelsesperiode, ikke økonomisk periodisering. |
| Kjøpsdato | `LeasingAcquisition.PurchaseDate` | Punktdato; dagens liste bruker den som start. |
| Opprinnelig sluttdato | `LeasingAcquisition.EndDate` | Finansielt originalfelt. |
| Avtalt sluttdato | `LeasingLifecycle.AgreedEndDate` | Operativ dato etter godkjente livssyklushendelser; dagens rapport-`End` bruker denne. |
| Varseldato / returfrist / lukket dato | `LeasingLifecycle.NoticeDate`, `ReturnDueDate`, `ClosedDate` | Separate livssyklusdatoer. |
| Hendelsesdato | `LeasingLifecycleEvent.Date` | Punktdato, med decision/status som eget felt. |
| Fakturadato | `LeasingAcquisition.InvoiceDate` er legacyreferanse; reelle dokumenter har `LeasingInvoice.InvoiceDate`. | Rapporten må velge hvilken kilde den bruker. Fakturakontroll bruker godkjente `LeasingInvoice`-dokumenter. |
| Betalingsperiode | `LeasingPlanTerm.PeriodFrom/PeriodTo` | Forpliktelsens dekningsperiode. Ingen generell periodiseringsalgoritme finnes. |
| Forfallsdato | `LeasingPlanTerm.DueDate` | Punktdato; dagens Payments-rapport filtrerer på denne. |
| Utstyr | `RegisteredDate`, `StatusDate` | Punktdatoer for utstyrs-/hendelseskort. |

Det eksisterende `LeasingReportDate`-valget er radtypeavhengig: felt som ikke finnes på en rad er null og faller ut av datofilteret. Den nye katalogen bør derfor angi kompatible datogrunnlag per måltall, ikke vise alle syv valg for alle kort.

### 4.4 Måltall og beløpsregler

| Måltall | Faktisk kilde | Regel/risiko |
|---|---|---|
| Antall rammeavtaler/anskaffelser | Autoriserte `Frameworks`/`Acquisitions` scopes | Tell distinkte IDs. Ikke tell en ramme og dens barn som samme enhet. |
| Rammegrense | `LimitNet`, `LimitVat` | Netto, MVA og brutto er separate. Valuta kommer fra rammeavtalen. |
| Brukt/reservert/tilgjengelig | `LeasingService.Capacity` og `LeasingOrderCalculator.Reserved` | Brukt baseres på registrerte anskaffelser minus **released** beløp; tilgjengelig = limit - used - reserved. Må ikke rekonstrueres fra listebeløpet. |
| Anskaffelsesverdi | `NetTotal`, `VatTotal`, `GrossTotal` og justeringsfelter | Dagens liste trekker credit + reversed; Capacity trekker released; dagens Agreements-rapport viser original gross. Dette er tre ulike, legitime grunnlag og krever eksplisitte måltallsnavn. |
| Varelinjeverdi | `LeasingCalculator.Line` | Netto og MVA rundes per linje, midpoint away from zero, før summering. |
| Dimensjonsfordelt kjøpsverdi | `LeasingAllocationCalculator` | Bruk bare komplette fordelingsrader. Ufullstendige/ufordelte justeringer må vises separat, aldri proporsjoneres uten regel. |
| Finansiert beløp | `FinancedAmount`/finansieringssnapshot | Skilles fra kjøpsverdi. Revisjon med null `EffectiveFrom` betyr ukjent historisk virkning, ikke kjøpsdato. |
| Planlagt betaling | Aktiv `LeasingPaymentPlan` og `LeasingPlanTerm.Net/Vat/Gross` | Bare aktiv plan; manglende/`NeedsReview` må fremgå. Ikke summer draft/replaced sammen med aktiv. |
| Fakturert/avvik/ufordelt | Godkjent `LeasingInvoice`, ikke-reversert `LeasingPaymentAllocation`, kreditnotafortegn og `LeasingPaymentCalculator.Status` | En faktura skal ikke gjentas per mulig anskaffelse. Ufordelt beløp tilhører dokumentet én gang. |
| Utstyr | `LeasingEquipment` og `LeasingItem.CountableEquipment` | Distinkt utstyr-ID. Varelinjens Quantity er ikke automatisk lik registrert utstyrsantall. |

Alle leasingvalutaer valideres mot samme seks koder som Agreements. Ingen valutakurser finnes. Resultater må alltid beholde valutakoden og aldri lage én total på tvers av valutaer.

### 4.5 Relasjoner og eksisterende ruter

- Konto → rammeavtale: `LeasingFramework.AccountId`.
- Rammeavtale → anskaffelse: nullable `LeasingAcquisition.FrameworkId`. Frittstående anskaffelser er reelle og må fortsatt vises.
- Anskaffelse → varelinje: `LeasingItem.(AccountId, AcquisitionId)`.
- Varelinje → utstyr: `LeasingEquipment.(AccountId, AcquisitionId, ItemId)`.
- Rammeavtale: `/leasing/frameworks/{id}`.
- Anskaffelse: `/leasing/acquisitions/{id}`.
- Varelinje har ingen egen rute; nærmeste eksisterende post er anskaffelsesdetaljen.
- Utstyr har ingen egen ID-rute. Eksisterende rute er `/leasing/equipment?acquisition={acquisitionId}`; den åpner anskaffelsens utstyrsflate, ikke valgt utstyr direkte.

For tree-kravet anbefales konto som rot, rammeavtaler som neste nivå og en eksplisitt lokaliserbar «Uten rammeavtale»-gren for frittstående anskaffelser. Denne syntetiske noden må ha en stabil, serverdefinert nodekey og kan ikke forveksles med en domene-ID. Varelinjer/utstyr bruker stabile faktiske GUID-er. «Åpne» går til nærmeste eksisterende rute som angitt over; ingen nye postruter skal oppfinnes skjult i rapportarbeidet.

## 5. Tenantavgrensning og rettigheter

### 5.1 Eksisterende håndheving

- `CurrentUserContextService` leverer valgt konto, men både `AgreementService.RequireMemberAsync` og `LeasingService.Member` verifiserer autentisering, valgt `CurrentAccountId`, aktivt medlemskap og aktiv bruker i databasen.
- Agreements-scope er `AccountId` + account-admin, eier eller `AgreementAccess`. Edit/Manage er strengere og valideres separat. Se `AgreementService.Accessible`/`RequireAsync`.
- Leasing rammeavtaler er normalt synlige for account-admin/eier; anskaffelser for account-admin, egen eier eller eier av rammeavtalen. Spesialroller kan gi bredere scope i rapport/livssyklus/order-stier. Se `LeasingService.Frameworks`, `Acquisitions`, `ReadAcquisitions` og `TenantAuthorizationService`.
- `LeasingReportReader` og `LeasingReportExporter` gir account-wide lesing i dagens leasingrapporter. De er ikke det samme som vanlig Leasing-tilgang.
- Menyen bruker `GetNavigationPermissionsAsync`, men UI-skjuling er ikke serverautorisasjon.

### 5.2 Anbefaling for rapportmodulen

1. Hver lagret side og hvert kort skal ha `AccountId` og account-aware FK-er.
2. Sideoppslag skal alltid kreve valgt konto + aktivt medlemskap + eksplisitt siderettighet. Ukjent, fremmed eller utilgjengelig GUID skal håndteres som eksisterende not-found/access-denied-mønster uten datalekkasje.
3. Rapportdata skal alltid beregnes med **betrakterens nåværende** modultilgang. En lagret side eller URL skal aldri utvide scope. Dette må gjelde preview, diagram, tabell, tre, tresøk, detaljpanel og åpneknapp.
4. Agreements-kort bruker samme `Accessible`-scope som listen. Leasing-kort bruker samme scope som det valgte datasettet; bare brukere med dagens rapportleserrolle får account-wide leasingdata.
5. Oppslagskatalogen må bygges fra autoriserte data slik at navn på skjulte eiere, motparter, dimensjonsverdier eller poster ikke lekker.
6. Page CRUD og data-read bør være separate checks. En bruker kan eventuelt få se en delt side, men dataene skal fortsatt snevres inn individuelt.

Den private querylogikken i de to store tjenestene bør trekkes ut til interne, testbare Web-hjelpere, for eksempel `AgreementQueryScope` og `LeasingQueryScope`, og brukes av både eksisterende lister og rapportmodulen. Dette er gjenbruk av samme logikk, ikke et repository.

## 6. Eksisterende UI-støtte og faktiske gap

| Krav | Finnes i dag | Plan |
|---|---|---|
| Responsive kort/tabeller | Bootstrap grid, `dashboard-grid`, `leasing-summary-grid`, `.table-responsive`, mobile breakpoints. | Gjenbruk farger, surfaces, spacing og breakpointmønster; nytt rapportgrid kollapser til én kolonne på smale skjermer. |
| Diagrammer | Bare CSS-donut med tilgjengelig tabell i `InsightsWidget`. | ECharts wrapper med loading/empty/error, `ResizeObserver`, dispose og redusert bevegelse; tabell-/tekstalternativ for data. |
| Sammenhengstre | Ingen grafkomponent. | ECharts `tree`, orientering LR, progressive children/search fra server, zoom/pan, valgt node og separat Blazor-detaljpanel. |
| Drag/resize | Ikke funnet. | Avgrenset JS grid-adapter med pointer-events og Blazor callbacks. Tastaturkontroller for flytt/endre størrelse. Ingen redigeringshåndtak i visningsmodus. |
| Skjemavalidering | `EditForm`, native/DataAnnotations/manual servicevalidering. | Typed editor model + `EditContext`/`ValidationMessageStore`, deretter full servervalidering mot katalog. Modulbytte rydder inkompatible valg før save. |
| Datofilter | `InputDate` og `DateOnly`; eksisterende tjenester bruker inklusive datoer. | Felles perioderedigerer som viser både regel og beregnet fra/til. Custom krever gyldig `from <= to`. |
| Ulagrede endringer | Ingen `NavigationLock`, location-changing handler eller `beforeunload`-mønster ble funnet. Flere editorer har kopimodell, men Leasing Cancel er bare navigasjon. | Ny rapportside må eie en dyp arbeidskopi, bruke `NavigationLock` for intern/ekstern navigasjon og liten JS `beforeunload`-hook for nettleserlukking/reload. Hook fjernes ved save/cancel/dispose. |
| Modal/fokus | Bootstrapmodals og nyere dimensjonseditor har scrollbar body, sticky footer-lignende struktur, tabs og fokusstil. | Gjenbruk modalstruktur, knapptypene og fokusmønstrene. Egen fokusfelle/return focus må verifiseres hvis Bootstrap modal-JS ikke eier dialogen. |
| Concurrency | GUID `Revision`, EF concurrency token og ressursfeil i Agreements/Leasing/Dimensions. | `ReportPage.Revision` og expected revisions for alle sider berørt av en edit session. Konflikt skal ikke overskrive arbeidskopien. |

## 7. Anbefalt arkitektur og eksplisitt gjenbruk

### 7.1 Moduladaptere, ikke parallelle definisjoner

Opprett i `TenantPlatform.Web/Services/Reports`:

- `IReportCatalogService`: returnerer filtrerbare felt, oppslagsverdier, datoalternativer, måltall, grupperinger og kompatible visualiseringer for valgt modul og bruker.
- `IReportExecutionService`: validerer en typed kortdefinisjon og utfører server-side aggregat/tabell.
- `IReportTreeService`: roots/children/search/detail med opaque stabile nodekeys.
- `IReportPageService`: side-CRUD, duplisering, atomisk arbeidskopilagring og concurrency.
- `AgreementReportModule` og `LeasingReportModule`: tynne adaptere over delte scopes, filtre og domene-kalkulatorer.

For å forhindre drift:

1. Flytt kun den nødvendige query-sammensetningen fra `AgreementService.ListAsync` til en intern `AgreementListQuery`/`AgreementQueryScope`: autorisert base, katalog og `ApplyFilter` uten paging. Listen fortsetter å bruke den.
2. Gjør tilsvarende for `LeasingService.ListAsync`, med separate framework/acquisition scopes og nøyaktig dagens kind/status/eier/dimensjonssemantikk.
3. Behold `DimensionListFilters` som eneste implementasjon av hierarkisk filterplan.
4. La katalogmetadata peke på typed enum-/felt-ID-er. Ingen dynamiske LINQ-feltnavn eller lokaliserte navn lagres som identitet.
5. Gjenbruk `AgreementPeriodCalculator`, `LeasingCalculator`, `LeasingAllocationCalculator`, Capacity/Reserved-reglene og betalingsstatusreglene. Hvis private metoder må deles, flytt dem til små interne domenehjelpere med regresjonstester; ikke dupliser formlene.

### 7.2 Query pipeline

Hvert vanlig kort følger samme serverpipeline:

1. verifiser bruker, valgt konto og side/kort;
2. last og valider katalogidentiteter;
3. resolve relativ periode mot kontoens tidssone;
4. bygg autorisert modulscope;
5. anvend samme filtre som listen;
6. anvend valgt, kompatibelt datogrunnlag;
7. projiser én eksplisitt grain per måltall;
8. aggreger på serveren, med valuta som obligatorisk nøkkel for penger;
9. returner begrenset, ferdig DTO med beregnet periode og metadata.

EF-translaterbare tellinger og enkle summer skal bli SQL. Agreement-perioder og enkelte Leasing-beregninger som ikke kan oversettes, må først snevre inn autoriserte ID-er og relevante versjoner i databasen og deretter bruke de eksisterende rene kalkulatorene på serveren. Det er fortsatt server-side behandling; store datasett trenger hard grense/cancellation og senere måling, ikke overføring til nettleseren.

### 7.3 ECharts-integrasjon

- Legg en versjonslåst, lisensiert ECharts-build lokalt under `wwwroot/lib/echarts/` fordi prosjektet ikke har npm-byggepipeline. Dokumenter versjon og lisens.
- Én `report-charts.js` eksporterer create/update/resize/dispose og tree actions. Den skal ikke kjenne domeneobjekter, bare serialiserbare chart DTO-er.
- Én Blazor `ReportChart.razor` eier element-ID, JS-referanse, `IAsyncDisposable`, loading/empty/error og tabellalternativ.
- Bruk ECharts dataset/series med data levert fra serveren. Ikke bygg EF-/filtersemantikk i JavaScript.
- Sanitér/encode tekst; ikke bruk lagrede titler som HTML. Linknavigasjon skjer gjennom verifisert node/card metadata, ikke formatter-HTML.

## 8. Forslag til datamodell og migrasjon

### 8.1 Entiteter

`ReportPage` i Core:

- `Id`, `AccountId`;
- `Name`;
- `OwnerUserId` eller annen tilgangsidentitet etter produktbeslutning P1;
- `CreatedUtc`, `CreatedByUserId`, `UpdatedUtc`, `UpdatedByUserId`;
- `Revision` som concurrency token.

`ReportCard` i Core:

- `Id`, `AccountId`, `ReportPageId`;
- `Name`, `Module`, `Visualization`;
- `DateBasis`, `Metric`, `Aggregation`, `GroupingKind`, nullable `GroupingDimensionId`;
- `PeriodRule`, nullable `CustomFrom`, `CustomTo`;
- `DefinitionVersion`;
- `FiltersJson` (`jsonb`) med typed modulfilter og stabile GUID-er;
- `DisplayJson` (`jsonb`) for presentasjonsvalg som ikke påvirker datascope;
- `GridColumn`, `GridRow`, `GridWidth`, `GridHeight`, `SortOrder`;
- ingen egen valutaomregningsverdi; valuta er filter/resultatdimensjon.

Anbefalte enums/value contracts: `ReportModule`, `ReportVisualization`, `ReportPeriodRule`, `ReportTimeGrouping`, `ReportGroupingKind`, modulspesifikke `AgreementReportDateBasis`/`LeasingReportDateBasis` og metric IDs. En opaque string er lettere å migrere dårlig og bør unngås for kjernedefinisjonen.

`FiltersJson` er hensiktsmessig fordi de to modulene har ulike, dynamiske filtersett og kort kjøres enkeltvis. JSON skal likevel serialiseres gjennom versjonerte C#-kontrakter og valideres mot katalogen; det skal aldri være vilkårlig klient-LINQ. Felles felt som styrer constraints/kompatibilitet beholdes som kolonner.

### 8.2 Relasjon og constraints

- `report_pages`: PK `Id`, alternate key `(AccountId, Id)`, account-FK, bruker-FK-er, index `(AccountId, Name)`, `Revision` concurrency token.
- `report_cards`: PK `Id`, alternate key `(AccountId, Id)`, composite FK `(AccountId, ReportPageId)` til siden, unique `(AccountId, ReportPageId, Id)`, indekser for side/sortering.
- Servervaliderte lengder og check constraints for enumverdier, custom date pair og positive gridstørrelser.
- Side + kort slettes eksplisitt i én transaksjon etter bekreftet expected revision. Dette gjør antallet kort og auditoperasjonen tydelig; ikke stol på en skjult klientdelete.
- En side-duplikat oppretter nye page/card GUID-er og kopierer definisjoner/layout dypt.

Cross-page flytting berører minst to sider. `SaveReportWorkspaceRequest` skal derfor bære forventet revisjon og ny kortliste for alle berørte sider. Tjenesten:

1. validerer at alle side-ID-er tilhører konto og er redigerbare;
2. åpner én transaksjon og låser berørte sider deterministisk etter ID;
3. kontrollerer alle revisjoner;
4. validerer hele nye grafen før mutasjon;
5. lagrer flytting/edits atomisk og gir alle berørte sider ny revision.

Dermed kan Avbryt være rent klient-side forkasting: ingen databaseoperasjon skjer før samlet Lagre.

## 9. Tjeneste-API og ruting

### 9.1 C#-kontrakter

Anbefalt minimum:

```text
IReportPageService
  ListAsync(account)
  GetAsync(account, pageId)
  CreateAsync(account, request)
  SaveWorkspaceAsync(account, workingCopyWithExpectedRevisions)
  DuplicateAsync(account, pageId, expectedRevision)
  DeleteAsync(account, pageId, expectedRevision)

IReportCatalogService
  GetAsync(account, module)

IReportExecutionService
  ExecuteAsync(account, cardDefinition)

IReportTreeService
  GetRootAsync(account, definition)
  GetChildrenAsync(account, definition, nodeKey)
  SearchAsync(account, definition, query, limit)
  GetNodeAsync(account, definition, nodeKey)
```

Alle metoder tar konto fra den verifiserte komponentkonteksten, men tjenesten verifiserer den på nytt. Requesten får aldri lov til å bytte konto. Catalog og execute skal returnere samme beregnede periode, slik at UI kan vise hva regelen faktisk betyr.

### 9.2 Ruter

- `/reports` — liste/opprettelse av tilgjengelige rapportsider.
- `/reports/{ReportPageId:guid}` — stabil, bokmerkbar side i visningsmodus.
- Redigeringsmodus anbefales som tilstand på samme stabile rute, ikke sensitive eller ulagrede data i query string. En ufarlig `?mode=edit` kan brukes som UI-intensjon, men autorisasjon og arbeidskopi må fortsatt være server-/circuitstyrt.

Navneendring påvirker ikke GUID-ruten. Duplikat får ny GUID. Slettet/fremmed side følger eksisterende access-denied/not-found-praksis. Ingen kortdefinisjon, filterverdi eller arbeidskopi skal serialiseres i URL.

Ingen `GetStartPage`, loginredirect, brukerpreferanse eller «Sett som startside» endres i denne leveransen.

## 10. Periode- og grupperingsmodell

`ReportPeriodRule` lagres som regel, ikke løste datoer:

- Current/Previous/Next Month;
- Current/Previous/Next Quarter;
- Current/Previous/Next Tertial;
- Current/Previous/Next HalfYear;
- Current/Previous/Next Year;
- YearToDate;
- Rolling12Months;
- Custom.

`ReportPeriodResolver.Resolve(rule, today, customFrom, customTo)` skal være ren og testbar. Kalenderperioder starter på første dag og slutter på siste dag; kvartal er 3, tertial 4 og halvår 6 måneder. YTD er 1. januar–today. Teknisk anbefaling for «siste 12 måneder» er det inklusive intervallet `today.AddMonths(-12).AddDays(1)`–`today`, slik at intervallet ikke inneholder én dag mer enn tolv måneder. Custom lagrer to `DateOnly`-verdier.

Tidsgruppering er et separat enum og bruker kalendergrenser:

- måned `yyyy-MM`;
- kvartal Q1–Q4;
- tertial T1–T3;
- halvår H1–H2;
- år.

Resultatnøkkelen skal ha maskinverdier `(year, bucket)` og lokalisert label separat. Serveren fyller tomme tidsbøtter med null når måltallet er additivt og definert; «ikke angitt» brukes for manglende kategoriverdi, ikke for manglende dato som egentlig faller utenfor et datofilter.

## 11. Risikoanalyse og obligatoriske guardrails

### 11.1 Dobbelttelling

| Risiko | Guardrail |
|---|---|
| Join avtale → linje → prisversjon | Velg én eksplisitt grain. Count agreements med distinct ID; amount fra `AgreementPeriodCalculator`, aldri rå versjonsjoin. |
| Flervalgsdimensjon | En post kan forekomme i flere grupper. Returner både `UniqueEntityCount` og `GroupOccurrenceCount`; merk penger/antall som ikke-additive på tvers av gruppene. Ikke presenter gruppesum som unik total. |
| Hierarkisk dimensjon | Forelder inkluderer etterkommere, men samme assignment skal telles én gang per valgt gruppe. Bruk ID-sett som `AgreementInsightHierarchy`, ikke join-sum over alle ancestors. |
| Ramme + anskaffelser | Ikke summer limit og anskaffelsesverdi i samme serie. Kind bestemmer grain og kompatible metrics. |
| Anskaffelse → varelinje → utstyr | Parentbeløp skal ikke replikeres per child. Varelinje- og utstyrskort har egen grain. |
| Betalingsplanversjoner | Bare aktiv plan for forventet betaling. Ikke summer draft/replaced. Stable installment-ID brukes ved fakturaallokering. |
| Fakturaallokering | Godkjent dokument og ikke-reversert allokering; ufordelt dokumentbeløp én gang. Kreditnota har negativt fortegn. |
| Arvede Leasing-dimensjoner | Bruk anskaffelsens effektive snapshot direkte; ikke join både ramme- og anskaffelsestildeling. |

### 11.2 Valuta

Ingen valutakursmodell finnes. Servervalidering skal:

- alltid gruppere pengeresultater minst på Currency;
- tillate ett KPI-tall bare når filteret gir én valuta;
- ellers returnere ett KPI per valuta eller avvise inkompatibel visualisering med tydelig melding;
- aldri velge kontovaluta eller NOK implisitt;
- bevare valuta ved drilldown og tabell.

### 11.3 Periodisering

- Agreement-fakturadato er punktbasert og additiv.
- Agreement «gjelder perioden» trenger produktbeslutning om full periodeverdi versus dagbasert opptjening. Dersom dagbasert velges, må eksisterende eventavrunding og totalbevaring videreføres over bucketgrenser.
- Leasing kjøpsverdi er punktbasert på PurchaseDate med dagens modell.
- `LeasingPlanTerm.PeriodFrom/To` beskriver dekning, men ingen kode fordeler beløpet over kalenderbøtter. Ikke innfør lineær periodisering uten beslutning og tester.
- Credits, reversals og releases betyr forskjellige ting. Hvert metric-ID skal ha én navngitt formel og en forklarende UI-tekst.

### 11.4 Skala og tre

- Vanlige kort returnerer bare aggregater eller serverpaginert tabell.
- Treets root/children lastes per node. Søk kjøres server-side over hele autoriserte/filtrerte utvalget og returnerer begrenset treffliste med ancestor-path, ikke hele treet.
- Nodekey bør være `module:type:id` eller en typed DTO; serveren parser og revaliderer type/ID. Syntetiske noder får separat type.
- Sett konfigurerte grenser for tabellrader, grupper, søketreff og eksport. Vis «snevr inn» i stedet for å kutte stille.

## 12. Avgrensede implementasjonssteg

### Steg 0 — avklar produktbeslutninger og frys metric-katalog

**Avhengighet:** ingen.
**Berører:** dette plan-/featuregrunnlaget, ingen runtimekode.
**Arbeid:** besvar P1–P7 i kapittel 14; skriv endelig tabell over metric/date/grouping-kompatibilitet og labels.
**Tester:** review mot modell og formler.
**Ferdig når:** ingen metric eller sideoperasjon har uklar tilgang, valuta- eller periodiseringssemantikk.

### Steg 1 — del listefiltre og autoriserte query scopes

**Avhengighet:** Steg 0 for kataloggrenser.
**Berører:** `AgreementService.cs`, `AgreementService.Dimensions.cs`, `LeasingService.cs`, `LeasingService.AgreementDimensions.cs`, nye interne Web-hjelpere og eksisterende smoke-tester.
**Arbeid:** trekk ut autorisert base + filterapplikasjon uten å endre liste-DTO eller UI. Behold paging i listetjenestene. Bygg typed report catalog oppå samme kode og `DimensionListFilters`. Ekskluder property eksplisitt.
**Tester:** filterparitet for søk/status/type/kind/eier/attention/dimensjoner, historiske valg, descendant og AND; tenant- og ressursisolasjon; eksisterende Agreements/Leasing/Dimensions tester.
**Ferdig når:** samme fixtures gir samme IDs før/etter refaktor, og katalogen kan ikke tilby et filter som querylaget ikke kan validere/anvende.
**AC:** 1, 2, 10, 11.

### Steg 2 — perioderegler og kompatibilitetsvalidator

**Faktisk status 12. november 2026:** **FERDIG.** Perioderegler, kontoens lokale dato, tidsbucketnøkler, den produktgodkjente module/grain/date/metric/grouping/visualization-matrisen, typed katalog, definisjonsvalidator, resultatvalideringskontrakt og ren normalisering er implementert og databasefritt verifisert. Matrisen er dokumentert i [feature-reportmodule-compatibility.md](feature-reportmodule-compatibility.md). Rapportkjøring tilhører senere steg og er ikke implementert.

**Avhengighet:** Steg 0.
**Berører:** nye Report-enums/value contracts i Core eller rene Web DTO-er, perioderesolver og databasefrie tests.
**Arbeid:** implementer alle regler, custom dates, timezone-resolved today, tidsbucketnøkler og validator for module/date/metric/grouping/visualization. Modulbytte normaliserer inkompatible valg i én funksjon som både UI og server bruker.
**Tester:** måned/kvartal/tertial/halvår/år ved grenser og skuddår, YTD, rullerende 12, årsskifte, custom invalid, uavhengig periode/gruppering, alle ugyldige kombinasjoner.
**Ferdig når:** relative regler kan roundtrippes uten løste datoer og viser korrekt beregnet intervall.
**AC:** 3, 4.

### Steg 3 — persistence, migration og page service

**Avhengighet:** P1 tilgangsmodell, Steg 2 contracts.
**Berører:** Core-entiteter, DbContext, ny `ReportConfiguration`, ny migrasjon/snapshot, `IReportPageService`, DI, autorisasjonstjeneste.
**Arbeid:** modell/constraints, konto-FK-er, CRUD, dyp duplisering, bekreftet sletting, atomisk workspace save og expected revisions. Bevar offline design-time factory.
**Tester:** modellmetadata, PostgreSQL migration fresh/upgrade/down-up etter repoets testpraksis, cross-account IDs, create/read/rename/stable ID, deep duplicate, delete count, atomic cross-page move, stale revisions og rollback.
**Ferdig når:** en lagret graf kan lastes identisk, duplikat er uavhengig og konflikt aldri overskriver.
**AC:** 5, 6, 10, 12, 13.

### Steg 4 — Agreements-kjøring

**Faktisk status 12. november 2026:** **FERDIG innenfor bestilt kilde- og databasefritt scope.** Autorisert Agreements-kjøring, avtaleantall, fakturadatobaserte inntekter/kostnader, grupperinger, tidsbøtter, valutaoppdeling, resultatmetadata og serverpaginert tabell er implementert som dokumentert i kapittel 21. Npgsql/PostgreSQL-oversettelse og ytelse er fortsatt uverifisert fordi denne runden uttrykkelig forbød databaseforbindelser.

**Avhengighet:** Steg 1–2.
**Berører:** `AgreementReportModule`, execution DTO-er, deling av økonomikalkulasjon ved behov.
**Arbeid:** distinct counts, valgte faste/dynamiske grupperinger, sikre datoalternativer, valutaoppdelte økonomimåltall og serverpaginert tabell. Ikke implementer property.
**Tester:** filterparitet; owner/access; arkivscope; multi-value unique vs occurrence; hierarchy; blandet valuta; linje-/prisversjoner; advance/arrears; delperiode og avrunding etter P4.
**Ferdig når:** totals kan avstemmes mot `ForecastAsync`/dashboard for samme definerte scope uten versjonsdobling.
**AC:** 1–4, 10–11.

### Steg 5 — Leasing-kjøring

**Avhengighet:** Steg 1–2 og P3/P5.
**Berører:** `LeasingReportModule`; eventuelt små delte kalkulatorhjelpere fra `LeasingService.Reports.cs`/`Orders.cs`.
**Arbeid:** distinct counts, framework/acquisition grain, eksplisitte value/capacity/payment metrics, avtalenivådimensjoner og eventuelt linjedimensjoner etter beslutning. Serveraggreger før paging.
**Tester:** kind/status/eier/dimensjonsparitet, arvede snapshots, standalone, account/report-reader scope, original/credit/reversed/released metrics, reserved, active plans, invoices/credit notes/unallocated, komplett/ufullstendig allocation, blandet valuta.
**Ferdig når:** hvert metric avstemmes mot navngitt eksisterende kalkulator/rapportgrunnlag og ingen skjult blanding av grunnlag finnes.
**AC:** 1–4, 10–11.

### Steg 6 — stabil side-URL, view/edit og full arbeidskopi

**Avhengighet:** Steg 3–5.
**Berører:** nye `Components/Pages/Reports/*`, NavMenu/permissions, ressurser, scoped CSS, page service.
**Arbeid:** `/reports` og `/reports/{guid}`, loading/empty/error, view default, Edit page med dyp working copy, add/edit/duplicate/delete/move cards, Save workspace, Cancel reset, unsaved navigation warning. Ingen startsideendring.
**Tester:** HtmlRenderer/serviceharness for direkte URL, refresh/load, rename, duplicated URL, inaccessible/deleted page, view controls hidden, invalid module switch, cancel after every edit type inklusive cross-page move, save conflict, three languages.
**Ferdig når:** Cancel utfører null writes og den innlastede saved DTO-en er byte-/feltmessig uendret; view-mode kan bokmerkes.
**AC:** 5–7, 12–14.

### Steg 7 — ECharts, responsive grid, drag og resize

**Avhengighet:** Steg 4–6.
**Berører:** lokal ECharts-asset, `report-charts.js`, `ReportChart.razor`, `ReportGrid.razor`, scoped CSS.
**Arbeid:** KPI/bar/line/donut/table compatibility, resize lifecycle, responsive single-column, drag/resize kun edit, keyboardalternativ og lesbar dataalternative.
**Tester:** JS/component lifecycle, rerender uten dobbel chartinstans, dispose, resize, saved layout roundtrip, keyboard move/resize, smal markup; målrettet faktisk browserkontroll for pointer/touch/fokus/ECharts.
**Ferdig når:** diagram følger kortet uten clipping/lekkasje, view-mode har ingen handles, og layout er lesbar på smal skjerm.
**AC:** 5–7, 11.

### Steg 8 — progressive sammenhengstrær

**Avhengighet:** Steg 1, 4–5, 7 og P6.
**Berører:** tree service/module adapters, ECharts tree wrapper, search/detail UI.
**Arbeid:** root/children/search/detail, ancestors for treff, autoexpand, selection, context labels, åpneknapper til eksisterende ruter. Filtre/periode avgrenser treff; nødvendige ancestors beholdes.
**Tester:** skjult/unloaded node funnet, like navn med ulik path, filtered-out avvist, ancestor expansion, tenant/access per endpoint, standalone Leasing, no individual line/item/equipment route fallback, zoom/pan/keyboard/browser.
**Ferdig når:** serveren kan finne en ikke-lastet autorisert node og klienten åpner nøyaktig path uten å laste hele treet.
**AC:** 8–10.

### Steg 9 — samlet regresjon, ytelse og leveranserapport

**Avhengighet:** alle steg.
**Berører:** Reports-smoke, eksisterende Agreements/Leasing/Dimensions/Dashboard tester, dokumentasjon.
**Arbeid:** bygg, databasefri suite, separat disponibel PostgreSQL-testdatabase, provider/concurrency/rollback, browsermatrise på målrettede flows, query count/timing og forklarte størrelsesgrenser.
**Tester:** alle ovenfor, `dotnet build TenantPlatform.sln`, eksplisitt Reports-prosjekt og relevante eksisterende prosjekter.
**Ferdig når:** AC-matrisen har faktisk evidens eller eksplisitt begrensning; eksisterende lister og startside er uendret.
**AC:** 1–14.

## 13. Akseptansekriterier → steg og evidens

| AC | Primær implementasjon | Påkrevd evidens |
|---:|---|---|
| 1 | 1, 4, 5 | Samme filterkatalog/query-fixtures som eksisterende lister. |
| 2 | 1, 4, 5 | Negativ katalog-/save-test for Building/Unit/Location. |
| 3 | 2, 6 | Uavhengig modellbinding og lagringsroundtrip. |
| 4 | 2 | Pure clock/timezone boundary tests, særlig årsskifte. |
| 5 | 3, 6 | PostgreSQL roundtrip av full side/kort/layout. |
| 6 | 3, 6, 7 | Deep-copy IDs og atomisk save/flytting/layout. |
| 7 | 6 | Working-copy snapshot før/etter Cancel, null writes. |
| 8 | 8 | Server search i ikke-lastet branch + autoexpand-test. |
| 9 | 8 | Node type/ID/path/detail og eksisterende route assertions. |
| 10 | 1, 3–5, 8 | Cross-account, removed membership og role/resource tests på alle innganger. |
| 11 | 1, 4, 5, 9 | Eksisterende Agreements/Leasing/Dashboard/Dimensions-regresjon. |
| 12 | 3, 6 | Direkte route, rename og refresh med samme GUID. |
| 13 | 3, 6 | Duplikat GUID, delete/inaccessible route. |
| 14 | 6, 9 | Git-/kodekontroll: ingen startsidepreferanse, knapp eller loginendring. |

## 14. Produktbeslutninger som ikke kan utledes av kodebasen

Disse må avklares før de respektive stegene; de er ikke tekniske spørsmål med ett korrekt svar:

**P1 — BESLUTTET: eierskap og deling av rapportsider.** En side er privat for oppretteren og aktiv konto. Oppretteren kan lese og administrere egne sider så lenge modulkravet fortsatt er oppfylt. Account-admin kan lese og administrere alle sider på kontoen. Deling er ikke implementert. En kopi eies av brukeren som utfører kopieringen, også når brukeren er administrator. Sider eller kort gir aldri utvidet tilgang til underliggende moduldata.

**P2 — BESLUTTET: hvem kan opprette/redigere/slette.** Aktive kontomedlemmer med tilgang til minst én av Agreements eller Leasing kan opprette private sider. Eieren kan administrere egne sider og account-admin kan administrere alle sider i kontoen. Vanlige brukere kan bare flytte kort mellom egne sider; administrator kan flytte mellom sider vedkommende kan administrere uten at sideeieren endres. En eksisterende kortdefinisjon fra en modul brukeren har mistet tilgang til kan bevares uendret, men ikke endres for å omgå modulautorisasjon.

**P3 — GODKJENT: Leasing-kjøpsverdier.** Første leveranse tilbyr separate måltall for original kjøpsverdi, kjøpsverdi etter kreditnota/reversering og kapasitetsforbruk etter frigivelse. Hvert sett har netto, MVA og brutto der den eksisterende beregningsregelen støtter det. De slås ikke sammen til et generisk «Sum»-måltall.

**P4 — GODKJENT: Agreements-beløp og periode.** Første leveranse bruker den beregnede fakturadatoen for økonomiske kontraktsrapporter. Avtaleantall kan bruke avtaleperiodens overlapp. Økonomisk «gjelder perioden»-grunnlag og ny dagperiodisering er utsatt og tilbys ikke av kompatibilitetsmatrisen.

**P5 — GODKJENT: Leasing-dimensjoner.** Første leveranse tilbyr bare effektive avtalenivådimensjoner. Linje- og kostnadsfordelingsdimensjoner er utsatt, er ikke valgbare rapportdimensjoner og avvises av validatoren.

**P6 — GODKJENT: frittstående anskaffelser i treet.** Frittstående Leasing-anskaffelser vises under en lokalisert, syntetisk «Uten rammeavtale»-gren under konto. Grenen har egen typed identitet, er ikke en rammeavtale og vises bare når den har relevante, autoriserte etterkommere.

**P7 — GODKJENT: multiverdi ved gruppering.** En post inngår i hver relevant gruppe. Unik total beregnes på autorisert grunnlag før gruppeutvidelse, gruppene merkes som ikke-additive, og beløp fordeles aldri automatisk mellom gruppene.

Følgende er tekniske valg som kan tas uten produktbeslutning:

- Konto betyr eksisterende `Account`.
- GUID er stabil sideidentitet og inngår i `/reports/{guid}`.
- Relative perioder lagres som enumregler.
- `AgreementReminderSettings.TimeZoneId` brukes via isolert resolver i første leveranse.
- Penger summeres aldri på tvers av valuta.
- Edit bruker lokal dyp arbeidskopi; kun samlet Save skriver.
- ECharts lastes som versjonslåst lokal asset.
- Eiendom utelates fra katalog og standardkort.
- Startsidefunksjonen forblir urørt.

## 15. Antakelser og ikke-mål

- «Kontrakt» i featurebeskrivelsen tilsvarer eksisterende Agreements-modul.
- `/leasing` omfatter i dag både rammeavtaler og anskaffelser; `orders` i listefilteret er frittstående anskaffelser, ikke innkjøpsordre-entiteten.
- Rapportdefinisjoner lagrer konfigurasjon, ikke resultatsnapshots. Data beregnes på nytt ved åpning.
- Midlertidige view-filtre er ikke nødvendige i første avgrensede implementasjon. Hvis de kommer, ligger de kun i circuit state og endrer ikke saved definition.
- Eksport, planlagt distribusjon, e-post, startsidevalg og valutakurser er utenfor denne featurebeskrivelsen med mindre de bestilles separat.
- Ingen eiendomsfilter/-gruppering introduseres. Eksisterende eiendomsfunksjoner i Agreements/Leasing endres ikke.
- Ingen ny individuell rute for linje, varelinje eller utstyr inngår; tree open bruker nærmeste eksisterende rute.

## 16. Opprinnelig anbefalt første implementasjonsoppgave

Start med **Steg 1: felles, autorisert filter-/querygrunnlag og typed module catalog, uten schema eller ny rapport-UI**. Oppgaven bør bevare dagens `/agreements`, `/leasing` og `/leasing/frameworks` bit-for-bit i oppførsel, la de eksisterende listetjenestene bruke de nye interne scope-/filterhjelperne, og legge databasefrie paritets- og tenanttester. Dette reduserer den største langsiktige risikoen — parallelle filterdefinisjoner — før lagring, diagrammer og tre bygges oppå kontrakten.

## 17. Faktisk resultat for Steg 1

Status 12. november 2026: **FERDIG innenfor avtalt scope**. Senere steg i kapittel 12 er ikke startet.

### 17.1 Implementert

- `AgreementListQuery` eier nå det delte, interne querygrunnlaget for autorisert Agreements-scope, attention, dimensjonsfilter, søk/status/type/eier, projeksjon, eieroppslag og dimensjonskatalog. `AgreementService.ListAsync` beholder paging, sortering og eksisterende liste-DTO-er, men bruker hjelperen.
- `LeasingListQuery` eier separate autoriserte scopes for rammeavtaler og anskaffelser, samt kind, dimensjoner, eier, søk, status, felles projeksjon, eieroppslag og dimensjonskatalog. `LeasingService.ListAsync` beholder paging, sortering og liste-DTO-er. `orders` betyr fortsatt frittstående anskaffelser, og limit-reader utvider fortsatt bare rammeavtalescopet.
- Begge modulene bruker fortsatt `DimensionListFilters.Catalog` og `Plan` for historiske valg, «Ikke angitt», parent/descendant og AND. Ingen parallell dimensjonsfiltermotor er introdusert.
- `Services/Reports` inneholder en typed, konto- og ressurssnevret katalog med egne enum-/record-identiteter for Agreements- og Leasing-filtre, faste grupperinger og dynamiske dimensjoner. Dynamiske dimensjoner og verdier beholder eksisterende GUID-er.
- Katalogen validerer tilbudte enum-, eier- og dimensjonsidentiteter mot den materialiserte autoriserte katalogen. Ukjente, fremmede, feilmodul- og utilgjengelige valg avvises.
- Leasing-avtaledimensjoner er eksplisitt skilt fra `LeasingLineAllocation`. Linje-/fordelingsscope er registrert som utsatt og tilbys ikke som filter eller gruppering i Steg 1.
- Eiendom, `BuildingId`, `UnitId` og utstyrslokasjon finnes ikke i rapportkatalogens typed filter- eller grupperingsidentiteter. Eksisterende eiendomsfunksjoner utenfor rapportkatalogen er urørt.
- Nye katalogtekster bruker ressursnøkler i nb-NO, en-GB og sv-SE. Øvrige labels peker på eksisterende ressursnøkler eller allerede lokaliserte dynamiske dimensjonsnavn.
- Katalogkall materialiserer alle DTO-er innenfor Agreement-/Leasing-tjenestenes kortlivede factory-context. Verken `IQueryable` eller `DbContext` returneres fra en operasjon.

### 17.2 Faktisk verifikasjon

| Kontroll | Resultat | Evidens/begrensning |
|---|---|---|
| Ny `TenantPlatform.Reports.SmokeTests` | PASS, 49 kontroller | EF InMemory: forventede ID-er og sortering; søk/status/type/kind/eier/attention; historisk/unspecified/descendant/AND; arvet Leasing-snapshot; frittstående anskaffelser; limit-reader; tenant-/ressursscope; eiendomsekskludering; typed katalog og avvisning av ugyldige valg. Ingen PostgreSQL-oversettelse. |
| `TenantPlatform.AgreementDimensions.SmokeTests` | PASS, 166 kontroller | InMemory og offline metadata; ingen databaseforbindelse eller SQL. |
| `TenantPlatform.Dimensions.SmokeTests` | PASS, 228 kontroller | Databasefri rendering/service/metadata/migrasjonsaudit; ingen databaseforbindelse, SQL eller migrasjonsanvendelse. |
| `TenantPlatform.Dashboard.SmokeTests` | PASS | InMemory samt testens eksisterende SQL-genereringskontroll. Prosjektet skriver fortsatt en eksisterende MSB9008-advarsel om en manglende gammel `TenantPlatform.Application`-referanse, men bygger og kjører ferdig. |
| `dotnet build TenantPlatform.sln` | PASS | 0 feil. To eksisterende NuGet-sikkerhetsadvarsler for `System.Security.Cryptography.Pkcs` 10.0.0 (NU1902/NU1903). |
| Agreements-/Leasing-smoke mot PostgreSQL | IKKE KJØRT | Steg 1 forbød database- og appoppstart. Denne runden beviser derfor ikke Npgsql/PostgreSQL-oversettelse av de refaktorerte listespørringene. |

Testene med InMemory dokumenteres kun som kontrakt-, filter- og isolasjonsdekning. De presenteres ikke som bevis for PostgreSQL-oversettelse, queryplan eller ytelse.

### 17.3 Avvik og eksplisitt utsatt

- På tidspunktet for Steg 1 var ingen report entities, schema, migrasjon, snapshot, side, rute, kort, kjøring, periode-/datogrunnlag, måltall, økonomisk beregning eller ECharts-integrasjon implementert. Senere schema-/sidetjenestearbeid er dokumentert i kapittel 19.
- Katalogen publiserer bare dagens verifiserte listefiltre og kategoriske grupperinger. Den publiserer ikke måltall, valuta-/periodiseringsregler eller kompatibilitetsvalg som avhenger av P1–P7.
- Historiske/inaktive dimensjoner beholdes som filtervalg når de er brukt i autorisert scope, men tilbys ikke som nye grupperingsdimensjoner; dette følger skillet mellom listekatalogen og dagens aktive/enabled dashboardgrupperinger.
- Eksisterende Agreements-liste inkluderer fortsatt arkiverte avtaler, mens attention/dashboard-scope fortsatt ekskluderer dem. Dette er bevart, ikke harmonisert.
- Den uavhengige temporale delen av Steg 2 er senere implementert som dokumentert i kapittel 18, og Steg 3 i kapittel 19. Steg 4–9 er fortsatt **IKKE STARTET**.

### 17.4 Neste avgrensede oppgave som ble valgt

Den rene, databasefrie delen av Steg 2 — perioderegler, tidsbøtter og validering av periode-/tidsgrupperingskontraktene — ble gjennomført uten å foregripe P1–P7. Resultatet er dokumentert i kapittel 18.

## 18. Faktisk resultat for den uavhengige delen av Steg 2

Historisk mellomstatus 12. november 2026: **DELVIS FERDIG**. Den temporale kontrakten var ferdig innenfor det daværende bestilte scopet; kompatibilitetsdelen ble senere ferdigstilt som dokumentert i kapittel 20.

### 18.1 Implementert

- `ReportPeriodRule` har typed identiteter for current/previous/next måned, kvartal, tertial, halvår og år, samt `YearToDate`, `Rolling12Months` og `Custom`.
- `ReportPeriodSelection` lagrer regelen og bare eventuelle custom-datoer. Relative regler beholder enumregelen ved JSON-roundtrip og beregnes på nytt mot eksplisitt `today`.
- `ReportPeriodResolver` er ren og databasefri. Den returnerer inklusive `DateOnly`-grenser, bruker fire måneder per tertial og følger den eksplisitte regelen `today.AddMonths(-12).AddDays(1)`–`today` for siste 12 måneder.
- Valideringen avviser ukjent perioderegel, manglende custom-dato, omvendt custom-intervall og custom-datoer sammen med en relativ regel.
- `ReportTimeGrouping` og `ReportTimeBuckets` støtter måned, kvartal, tertial, halvår og år. Hver bucket har typed nøkkel, stabil ikke-lokalisert maskinnøkkel, år, bucketnummer og fulle kalendergrenser.
- Bucketens `OverlapFrom`/`OverlapTo` beskriver første/siste delintervall separat. Delvis dekning endrer ikke bucketidentitet eller kalendergrenser.
- `ReportTemporalSelection` holder periode og nullable tidsgruppering som separate valg og validerer bare deres egne kontrakter. Den fastsetter ingen metric-, dato- eller visualiseringskompatibilitet.
- `IReportLocalDateService` bruker injisert `TimeProvider`, verifiserer valgt konto og aktivt medlemskap, leser eksisterende `AgreementReminderSettings.TimeZoneId` og bruker eksisterende `Europe/Oslo`-fallback. Selve konverteringen gjenbruker `AgreementReminderSchedule.Today`.
- Tjenesten er registrert scoped i DI. Ingen eksisterende Agreements-/Leasing-datoberegning eller kontoinnstilling er endret.
- Valideringsnøkler er lagt til i nb-NO, en-GB og sv-SE. Tidsbucketetiketter er ikke introdusert; maskinidentitet og en senere lokalisert label er derfor fortsatt adskilt.

### 18.2 Faktisk verifikasjon

| Kontroll | Resultat | Evidens/begrensning |
|---|---|---|
| `TenantPlatform.Reports.SmokeTests` | PASS, 106 kontroller totalt | 49 tidligere Steg 1-kontroller og 57 nye temporale kontroller. Alle perioderegler, måned-/årsskifte, skuddår/februar, kvartal/tertial/halvår, YTD, rullerende 12, custom-feil, ukjente enumverdier, JSON-roundtrip, uavhengig periode/gruppering, kronologi, delvise buckets og lokal dato rundt UTC-midnatt/DST er dekket. |
| Lokal datotjeneste | PASS, databasefri InMemory-fixture | Eksisterende `Europe/Oslo`-fallback, lagret `UTC`, sommertid og medlemskapsavvisning er kontrollert med fast `TimeProvider`. Ingen ekstern database ble kontaktet. |
| `dotnet build TenantPlatform.sln` | PASS | 0 feil. Eksisterende NU1902/NU1903-advarsler for `System.Security.Cryptography.Pkcs` 10.0.0 består. |

Ingen PostgreSQL-test er nødvendig for den rene resolveren/bucketlogikken. Datotjenestens EF-oppslag er kun kjørt med InMemory; ingen databaseforbindelse, appinstans eller SQL-kjøring inngår i denne runden.

### 18.3 På dette tidspunktet utsatt del av Steg 2

- Ingen modulspesifikke datogrunnlag er publisert eller koblet til måltall.
- Ingen måltall, summeringsmetoder, visualiseringer eller økonomisk periodisering er definert.
- Ingen komplett `module/date/metric/grouping/visualization`-validator er laget. En slik validator uten endelig kompatibilitetsmatrise ville ha kodet antakelser som produktregler.
- I denne historiske mellomstatusen var P3, P4, P5 og P7 ennå ikke bestemt. De er senere uttrykkelig **GODKJENT** med beslutningene i kapittel 14 og implementert i kompatibilitetsmatrisen i kapittel 20.
- Steg 2 kan først markeres helt ferdig når metric/date/grouping/visualization-matrisen er besluttet og den tilsvarende typed validatoren er implementert og testet.

### 18.4 Produktbeslutninger som på dette tidspunktet gjensto

- P1 og P2 er besluttet og implementert i Steg 3 som dokumentert i kapittel 19.
- Dette var kravet ved mellomstatusen. P3, P4, P5 og P7 og den endelige metric/date/grouping/visualization-matrisen er senere **GODKJENT** og ferdigstilt; de skal ikke avklares på nytt.
- P6 kan vente til trescope i Steg 8, men må avgjøres før treimplementasjonen starter.

## 19. Faktisk resultat for Steg 3

Status 12. november 2026: **KILDEIMPLEMENTASJON FERDIG.** Entiteter, EF-modell, migrasjon og sidetjeneste er implementert. Databasefri tjenesteverifikasjon og offline migrasjonsgenerering er gjennomført. Faktisk PostgreSQL-anvendelse, constraints, radlåsing og transaksjonsrollback er fortsatt uverifisert fordi ingen eksplisitt disponibel rapporttestdatabase var tilgjengelig eller godkjent i denne runden.

### 19.1 Datamodell og migrasjon

- `ReportPage` lagrer stabil GUID, `AccountId`, privat `OwnerUserId`, navn, GUID-basert concurrency-`Revision` og opprettet/endret-metadata.
- `ReportCard` lagrer stabil GUID, konto og side, navn, typed modul/periode/tidsgruppering/visualisering, definisjonsversjon, versjonert filter-JSON, custom-datoer, nullable identiteter for datogrunnlag/måltall/aggregering, groupingidentitet, display-JSON samt sortering og gridplassering.
- `ReportModule`, `ReportPeriodRule` og `ReportTimeGrouping` er flyttet til Core sammen med de nye persistence-enumene, slik at Core-entitetene bruker de samme typed identitetene som Web-kontraktene uten arkitekturbrudd.
- EF-konfigurasjonen har account-aware alternate keys og composite FK fra kort `(AccountId, ReportPageId)` til side `(AccountId, Id)`, restriktive konto-/bruker-FK-er, concurrency token, `jsonb`, lengdegrenser, indekser og check constraints for enums, custom-periode, grouping-shape, definisjonsversjon og layout.
- Migrasjonen `20261112212606_AddReportPages` og tilhørende designer/snapshot er generert med repoets offline design-time factory. Factoryens offline-sperre er bevart. Migrasjonen er ikke anvendt.
- Et avgrenset `dotnet ef migrations script` for bare denne migrasjonen ble generert offline og kontrollert for tabeller, nøkler, composite FK, constraints og indekser.

### 19.2 Sidetjeneste og tilgangsmodell

- `IReportPageService` tilbyr `ListAsync`, `GetAsync`, `CreateAsync`, `SaveWorkspaceAsync`, `DuplicateAsync` og `DeleteAsync` gjennom korte factory-contexts; lesing bruker `AsNoTracking`.
- Serveren utleder alltid aktiv konto, bruker, medlemskap, administratorstatus og moduladgang. Create-/workspace-kontraktene har ingen klientstyrt konto eller eier.
- Vanlige brukere ser og administrerer bare egne private sider. Account-admin kan administrere alle sider i aktiv konto. Duplikat får nye side-/kort-GUID-er og eies av aktøren; originalens eier endres ikke.
- Workspace-requesten beskriver den **fulle ønskede kortlisten for hver berørt side**. Kort som utelates fra alle berørte sider slettes, et kjent kort oppført på en annen berørt side flyttes med samme GUID, og null kort-ID oppretter en ny servergenerert GUID. Både kilde- og målside må derfor inngå ved flytting.
- Alle berørte sider valideres før mutasjon. Relasjonell kjøring åpner én transaksjon og låser sidene med `FOR UPDATE` i sortert GUID-rekkefølge. Expected revision kreves per side, alle berørte sider får ny revision, og `DbUpdateConcurrencyException` oversettes til en egen konflikt som lar senere UI beholde arbeidskopien.
- Sletting kontrollerer expected revision og fjerner kort og side eksplisitt i samme transaksjon. Duplisering kopierer definisjon, periode, filtre, display og layout dypt.
- Et uendret eksisterende kort kan bevares etter tapt tilgang til kortets modul. Endring av definisjonen krever nåværende modultilgang og ny katalogvalidering. Dette gir ikke dataadgang; rapportkjøring skal senere autorisere moduldata separat.

### 19.3 Valideringsgrense

- Struktur, navn/lengder, layout, enums, definisjonsversjon, periode, filteridentiteter/-verdier og kataloggrupperinger valideres. Eiendom kan ikke lagres som filter eller gruppering fordi den ikke finnes i den autoriserte katalogen.
- Ved ferdigstillelsen av Steg 3 brukte tjenesten en restriktiv midlertidig kompatibilitetsgrense. Denne er senere erstattet av `ReportDefinitionCompatibilityValidator` i Steg 2-fullføringen, se kapittel 20.
- Tomme sider og matrisegyldige kort kan lagres. Uendrede eksisterende definisjoner kan fortsatt bevares etter tapt modultilgang, men kan ikke endres eller kjøres uten gjeldende tilgang.

### 19.4 Faktisk verifikasjon

| Kontroll | Resultat | Evidens/begrensning |
|---|---|---|
| `TenantPlatform.Reports.SmokeTests` | PASS, 128 kontroller totalt | 49 Steg 1-, 57 temporale og 22 nye sidetjenestekontroller. De nye dekker privat listing/read, admin, cross-account, manglende moduladgang, create/read/rename/stabil ID, roundtrip, atomisk modellert flytting, ukjente ID-er, layout/katalogvalidering, stale revision uten delvis InMemory-mutasjon, dyp kopi, eksplisitt delete, tapt modultilgang og fjernet medlemskap. |
| `dotnet build TenantPlatform.sln` | PASS | 0 feil. Eksisterende NU1902/NU1903-advarsler for `System.Security.Cryptography.Pkcs` 10.0.0 består. |
| Migrasjonsgenerering | PASS, offline | `scripts/add-migration.sh AddReportPages` brukte design-time factory uten databaseforbindelse og oppdaterte migrasjon/designer/snapshot. |
| Avgrenset migrasjons-SQL | PASS, offline generering | Generert SQL inneholder forventede tabeller, PK/AK, account-aware FK, check constraints og indekser. SQL-en ble ikke kjørt mot PostgreSQL. |
| PostgreSQL migration/concurrency/rollback | **IKKE VERIFISERT** | Ingen disponibel rapporttestdatabase ble brukt. InMemory beviser ikke PostgreSQL-check constraints, `jsonb`, `FOR UPDATE`, EF concurrency-token, faktisk transaksjonsrollback eller down/up. |

Ingen appinstans, databaseforbindelse eller databaseoperasjon ble startet. Migrasjonen er ikke anvendt mot utviklings-, test- eller produksjonsdatabase.

### 19.5 Senere status

- P1–P5 og P7 er nå lukket. Den endelige matrisen og validatoren er dokumentert i kapittel 20.
- Ved avslutningen av Steg 3 gjensto P6; den er senere godkjent og implementert i Steg 8, se kapittel 25.
- PostgreSQL-verifikasjon av Steg 3 er fortsatt åpen; databasefrie Steg 2-kontroller erstatter ikke denne.

## 20. Faktisk resultat for fullføring av Steg 2

Status 12. november 2026: **FERDIG innenfor avtalt scope**. P3, P4, P5 og P7 er registrert som **GODKJENT**. Den autoritative førsteleveransematrisen er dokumentert i [feature-reportmodule-compatibility.md](feature-reportmodule-compatibility.md) og uttrykt i `ReportCompatibilityMatrix`.

### 20.1 Implementert

- Core har stabile typed identiteter for grain, metric, aggregation, datogrunnlag, beløpskomponent, valutaregel og additivitet. Dynamiske dimensjoner og verdier bruker fortsatt eksisterende GUID-er.
- Matrisen beskriver tre Agreements-metrics og elleve Leasing-metrics med navngitt eksisterende beregningsregel, filter-grain, datogrunnlag, tidsstøtte, faste/dynamiske grupperinger, visualiseringer, valuta og additivitet.
- Agreements-beløp bruker `AgreementPeriodCalculator` og beregnet fakturadato. Økonomisk overlappsperiodisering kan ikke aktiveres. Avtaleantall bruker avtaleperiodens overlapp.
- Leasing tilbyr separate original-, justert- og kapasitetsverdier i netto/MVA/brutto. Kapasitet er et nåbilde etter frigivelse; tidsgruppering og linjevisualisering avvises.
- Katalogen og `ReportDefinitionCompatibilityValidator` bruker samme typed matrise. Validatoren kontrollerer modul, metric, aggregation, date basis, periode, filter/grain, grouping, visualisering og utsatte scopes, og returnerer feltrettede ressursnøkler.
- `ReportResultValidator` håndhever resultatavhengige valuta- og donutkrav uten å beregne resultater. Penger konverteres aldri eller summeres skjult på tvers av valutaer; monetært KPI krever én faktisk valuta.
- Flervalgsdimensjoner gir metadata for unik total før gruppeutvidelse og ikke-additive gruppeforekomster. Donut avvises for overlappende flerverdigrupper.
- `ReportDefinitionNormalizer` er ren, deterministisk og idempotent. Den beholder kompatible valg, fjerner inkompatible valg eksplisitt og velger ikke ny metric, valuta eller økonomisk semantikk.
- `ReportPageService` bruker den ferdige validatoren ved endrede/nye kort. Tilgang, full-request-validering før entitetsmutasjon, atomisk workspace-lagring, concurrency og bevaring av uendrede kort etter tapt modultilgang er beholdt.
- Nye brukertekster og valideringsmeldinger finnes i nb-NO, en-GB og sv-SE.

### 20.2 Faktisk verifikasjon

| Kontroll | Resultat | Evidens/begrensning |
|---|---|---|
| `TenantPlatform.Reports.SmokeTests` | PASS, 168 kontroller totalt | De nye kontrollene dekker alle publiserte metrics, katalog/validator-paritet, grain/dato, fakturadato, utsatt periodisering, tre Leasing-verdisett, kapasitet uten historisk tidsserie, avtalenivådimensjoner, flerverdi/additivitet, donut, syntetisk valutaresultat, ukjente typed identiteter, deterministisk normalisering og sidetjenestens ferdige validator. Eksisterende tilgangs- og workspace-kontroller består. |
| `TenantPlatform.AgreementDimensions.SmokeTests` | PASS, 166 kontroller | InMemory og offline metadata; kjørt fordi katalogen viderefører den felles dimensjonsfilter-DTO-en. Ingen databaseforbindelse eller SQL. |
| `TenantPlatform.Dimensions.SmokeTests` | PASS, 228 kontroller | Databasefri komponent-/servicekontroll og offline strukturaudit av alle 47 migrasjoner. Direkte fixtures er oppdatert med `AllowMultiple`, og auditens eksplisitte liste over additive migrasjoner etter legacy-fjerningen inkluderer den allerede kildeimplementerte `AddReportPages`. Ingen databaseforbindelse, SQL-kjøring eller migrasjonsanvendelse. |
| `dotnet build TenantPlatform.sln --no-restore` | PASS | 0 feil. To eksisterende NuGet-sikkerhetsadvarsler for `System.Security.Cryptography.Pkcs` 10.0.0 (NU1902/NU1903) består. |

Smoke-testene er databasefrie og bruker ren logikk og EF InMemory. De beviser ikke PostgreSQL-oversettelse, migrasjonsanvendelse, `jsonb`, `FOR UPDATE`, concurrency eller faktisk rollback. Ingen databaseforbindelse eller appinstans ble startet.

### 20.3 Fortsatt avgrenset eller åpent

- Rapportkjøring, SQL-aggregering, økonomiske nyberegninger, UI, ECharts og selve sammenhengstreet er ikke implementert.
- Ved denne Steg 2-kjøringen gjensto P6. Den er senere godkjent og implementert i Steg 8, se kapittel 25.
- Agreements «gjelder perioden» med dagperiodisering, Leasing linje-/fordelingsdimensjoner, kapasitetshistorikk, valutakurser og nye metrics er eksplisitt utsatt, ikke uavklarte deler av denne matrisen.
- Steg 3 sin PostgreSQL-runtimeverifikasjon står fortsatt åpen.

### 20.4 Anbefalt neste avgrensede oppgave

Implementer Agreements-kjøring i Steg 4 for `AgreementCount`, `AgreementIncomeAmount` og `AgreementCostAmount` på det delte autoriserte Agreements-grunnlaget. Bruk avtaleoverlapp bare for count, kalkulatorens fakturadato for beløp, valutaoppdeling uten omregning og unik total før eventuell dimensjonsutvidelse. Hold UI, Leasing-kjøring og ny periodiseringslogikk utenfor denne avgrensningen.

## 21. Faktisk resultat for Steg 4 — Agreements-kjøring

Status 12. november 2026: **KILDEIMPLEMENTASJON FERDIG innenfor avtalt scope**. Ingen senere rapportmodul, UI eller databaseverifikasjon er startet.

### 21.1 Tjenestekontrakt, autorisasjon og scope

- `IReportExecutionService.ExecuteAsync` tar aktiv konto og en typed `ReportExecutionRequest`, reautoriserer Agreements for brukeren som faktisk kjører rapporten, validerer definisjonen mot brukerens autoriserte katalog og kompatibilitetsmatrisen, løser perioden mot kontoens lokale dato og delegerer til `AgreementReportModule`.
- Rapportside-/korteier inngår ikke i dataautorisasjonen. Adapteren verifiserer autentisering, valgt konto, aktivt medlemskap, modultilgang og brukerens aktuelle agreement grants på nytt.
- `AgreementReportModule` bygger på `AgreementListQuery.Authorized`, `ApplyAttention`, `ApplyDimensions`, `Project` og `ApplyRecordFilters`. Søk, status, type, eier, attention, historiske dimensjonsvalg, «Ikke angitt», etterkommere og AND beholder derfor listesemantikken.
- Arkivscope følger Agreements-listen: arkiverte avtaler inngår i vanlig scope. Attention følger fortsatt dashboardsemantikken og ekskluderer arkiverte gjennom det eksisterende querygrunnlaget.
- Alle tenant-eide bulkoppslag har eksplisitt `AccountId`. Context og entiteter forlater ikke operasjonen; resultatet består bare av DTO-er.

### 21.2 Avtaleantall

- `AgreementCount` bruker `StartDate` til faktisk slutt (`CessationDate ?? EndDate`) og teller distinkte `Agreement.Id` i hele rapportperioden.
- Faste grupperinger for type, status, motpart og eier bruker stabile maskinnøkler og databaseaggregerte distinct counts. Dynamiske avtaledimensjoner bruker den autoriserte dimensjonskatalogens GUID-er og paths.
- Manglende dimensjonstildeling får en stabil «ikke angitt»-gruppe. Flere tildelinger gir én forekomst per valgt gruppe, mens den unike totalen beregnes før utvidelsen.
- Ved tidsgruppering kan samme avtale telles i flere overlappende kalenderbøtter. Resultatmetadata markerer derfor bøttene som ikke-additive, og periodetotalen forblir unik.

### 21.3 Inntekt og kostnad på fakturadato

- Bare `AgreementIncomeAmount` og `AgreementCostAmount` kjøres. Retningene behandles separat og beholder kalkulatorens eksisterende positive beløp og avtalevaluta.
- `AgreementPeriodCalculator.CalculateInvoicePeriods` er trukket ut som delt, ren inngang og brukes nå både av eksisterende basis-preview/generering og rapportadapteren. Den beregner et utvidet vindu på ±13 måneder, velger komplette logiske `EventKey`-hendelser på beregnet `InvoiceDate`, fjerner included/gratis-segmenter fra beløpet og beholder alle betalbare segmenter i valgt hendelse.
- Dette viderefører aktive linjeversjoner, prisversjoner, frequency/anchor, gratisperiode, automatic index projection, advance/arrears og kumulativ avrunding per `EventKey`. Rapporten utfører ingen egen `Quantity × UnitPrice`-formel og summerer hvert kalkulatorsegment nøyaktig én gang før gruppering.
- Fakturadatogruppering er punktbasert. Økonomisk «gjelder perioden» og dagperiodisering avvises fortsatt av kompatibilitetsvalidatoren.
- Penger returneres alltid separat per valuta. Det finnes ingen standardvaluta eller omregning. Et KPI med faktisk flere valutaer avvises av `ReportResultValidator`; tomme resultater er gyldige uten at en valuta oppfinnes.

### 21.4 Resultater, paging og grenser

- Resultatet inneholder løst periode/lokal dato/tidssone, totals per valuta, kategorigrupper, kronologiske kalenderbøtter med full- og overlappsgrenser, valgfri tabellside og metadata for grain, enhet, valuta, additivitet, unik total, gruppeforekomster og presentasjonsbegrensninger.
- Additive fakturadatoserier fyller kalenderbøtter med null for observerte valutaer. Et helt tomt resultat beholder bøttene uten å finne på en valuta. Overlap count fyller null/count per bøtte, men merkes ikke-additivt over tid.
- Tabellresultater sorteres deterministisk. Count-rader sorteres etter tittel/ID; finanssegmenter etter fakturadato, avtale, `EventKey`, dato, linje og prisversjon. Paging skjer etter beregning, mens totals alltid gjelder hele autoriserte utvalget.
- Tabellsidestørrelse er 1–100. Tids-/dimensjonsberegning materialiserer maksimalt 10 000 autoriserte avtaler. Finanskjøring tillater maksimalt 1 000 avtaler, 20 000 linjeversjoner, 20 000 prisversjoner og 20 000 beregnede segmenter, og et fakturadatointervall med maksimalt 366 dagers differanse. Overskridelse gir den lokaliserbare feilen `ReportExecutionTooBroad`; data trunkeres aldri stille.
- Enkle totals og faste count-grupperinger aggregeres i databasen. Dynamiske dimensjoner og overlappende tidsbøtter materialiserer bare det innsnevrede autoriserte headergrunnlaget. Finansgrunnlaget lastes i bulk for autoriserte IDs før den eksisterende kalkulatoren kjøres; det finnes ingen per-avtale databasespørring.

### 21.5 Faktisk verifikasjon

| Kontroll | Resultat | Evidens/begrensning |
|---|---|---|
| `TenantPlatform.Reports.SmokeTests` | PASS, 189 kontroller totalt | 21 nye execution-kontroller dekker direkte tenant-/ressurstilgang, listefilterparitet, arkivscope, distinct count, overlap/tidsbøtter, faste/dynamiske grupper, historisk/unspecified/hierarki, flerverdi, advance/arrears, gratisperiode, linje-/prisversjoner, automatisk indeks, segmenter og `EventKey`-avrunding, separate inntekt/kost, flere valutaer, tomme utvalg, tabellpaging, resultatvalidering og eksplisitt størrelsesgrense. EF InMemory beviser ikke Npgsql-oversettelse. |
| Beregningsavstemming | PASS i deterministisk fixture | Advance-/gratis-/indeks-/versjonscaset gir 1 155,48 NOK, inkludert en todelt juli-hendelse som avrundes samlet. Annual arrears på 1 200 EUR finnes på 1. januar året etter leveringsåret. Kostnadscaset gir 100 GBP i valgt fakturadatointervall. Samme `CalculateInvoicePeriods` brukes av basis-preview/generering og rapportkjøring. |
| `TenantPlatform.AgreementDimensions.SmokeTests` | PASS, 166 kontroller | InMemory/offline metadata, ingen databaseforbindelse eller SQL. |
| `TenantPlatform.Dimensions.SmokeTests` | PASS, 228 kontroller | Databasefri komponent/service og offline migrasjonsaudit; ingen databaseforbindelse eller migrasjonsanvendelse. |
| `TenantPlatform.Dashboard.SmokeTests` | PASS | InMemory og eksisterende offline SQL-generering; ingen applikasjonsdatabase eller appoppstart. |
| PostgreSQL-avhengige Agreements-testprosjekter | BUILD PASS | `AgreementPeriods`, `AgreementProcessing` og `Agreements` bygger med 0 feil, men ble ikke startet fordi oppgaven forbød databaseforbindelser. Deres PostgreSQL-scenarioer er derfor **IKKE KJØRT**. |
| `dotnet build TenantPlatform.sln --no-restore` | PASS | 0 feil. To eksisterende NuGet-sikkerhetsadvarsler for `System.Security.Cryptography.Pkcs` 10.0.0 (NU1902/NU1903) består. |

### 21.6 Åpne verifikasjonsgrenser

- Npgsql-oversettelse av de nye autoriserte count-, filter- og faste grouping-spørringene er **IKKE VERIFISERT**. InMemory-resultatene skal ikke tolkes som providerbevis.
- Faktisk query count, PostgreSQL-queryplan, indeksbruk, ytelse ved grensene og cancellation mot Npgsql er ikke målt.
- Steg 3 sin PostgreSQL-verifikasjon av migrasjon, `jsonb`, `FOR UPDATE`, concurrency og rollback står fortsatt åpen.
- Leasing-kjøring, UI, ECharts og sammenhengstre er ikke startet. P6 er fortsatt utsatt til Steg 8.

### 21.7 Anbefalt neste avgrensede oppgave

Før ny funksjonsbredde anbefales en separat, eksplisitt godkjent PostgreSQL-verifikasjonsrunde for Steg 3 og de providerkritiske Agreements-spørringene i Steg 4, med disponibel testdatabase og uten å bruke utviklingsdatabasen. Dersom neste oppgave fortsatt skal være kildeimplementasjon uten database, er Steg 5 Leasing-kjøring den naturlige avgrensningen.

## 22. Faktisk resultat for Steg 5 — Leasing-kjøring

Status 13. november 2026: **KILDEIMPLEMENTASJON FERDIG innenfor avtalt databasefritt scope**. PostgreSQL-oversettelse og ytelse er fortsatt uverifisert. Steg 3 sin tidligere migrasjon er uendret og ikke anvendt.

### 22.1 Felles kjøreinngang, autorisasjon og scope

- IReportExecutionService ruter nå typed definisjoner til AgreementReportModule eller LeasingReportModule, men bruker samme katalog, kompatibilitetsvalidator, lokale kontodato, periodeoppløsning, resultatsjekk og tabellkontrakt.
- Leasing-kjøringen reautoriserer den faktiske brukeren mot valgt konto, aktivt medlemskap og gjeldende Leasing-tilgang. Lagret rapportside, eier eller administratoren som opprettet definisjonen utvider ikke dataadgangen.
- Adapteren bruker LeasingListQuery.AuthorizedFrameworks, AuthorizedAcquisitions, ApplyKind, ApplyRecordFilters og ApplyDimensions. Rammeavtaler og anskaffelser beholder dermed de separate listescope-reglene for administrator, eier og limit-reader.
- Søk, kind, typeavhengig status, eier, historiske dimensjonsvalg, «Ikke angitt», foreldrenode med etterkommere og AND mellom dimensjoner bruker samme querygrunnlag som listene. Listeverdien «orders» normaliseres fortsatt til frittstående anskaffelser og har ingen kobling til LeasingOrder.
- Anskaffelsesfiltre og -gruppering leser bare LeasingAcquisitionDimensionAssignment, altså anskaffelsens effektive snapshot. Rammeavtalens assignment joines ikke inn en gang til. Eiendom og utsatte linje-/fordelingsdimensjoner finnes fortsatt ikke i rapportkatalogen.
- Alle tenant-eide oppslag har eksplisitt AccountId; context og entiteter forlater ikke operasjonen.

### 22.2 Implementerte måltall og økonomisk avstemming

Matrisens elleve Leasing-måltall er implementert:

1. LeasingFrameworkCount teller distinkte rammeavtaler der AcquisitionFrom–AcquisitionTo overlapper rapportperioden.
2. LeasingAcquisitionCount teller distinkte anskaffelser med PurchaseDate i perioden.
3. LeasingOriginalPurchaseNet/Vat/Gross bruker lagret NetTotal, VatTotal og GrossTotal.
4. LeasingAdjustedPurchaseNet/Vat/Gross trekker kreditnota og reversering fra riktig netto-/MVA-komponent. Den felles LeasingPurchaseValueQuery brukes nå både av Leasing-listen og rapportadapteren, slik at justert listeverdi og rapportverdi ikke har parallelle formler.
5. LeasingCapacityUsedNet/Vat/Gross bruker rammeavtaler som overlapper perioden, men beregner dagens registrerte anskaffelser etter frigivelse. LeasingCapacityQuery er trukket ut fra eksisterende Capacity og brukes både av Leasing-tjenesten og rapportadapteren. Netto er NetTotal minus ReleasedNetTotal, MVA tilsvarende, og brutto avstemmes mot LeasingCalculator.Used.

Kapasitetsforbruk inkluderer ikke reserverte ordre, rammegrense eller tilgjengelig kapasitet. Original kjøpsverdi, justert kjøpsverdi og frigitt kapasitet er tre separate grunnlag. Betalinger, betalingsplaner, fakturadokumentmåltall, finansiert beløp, limit, reserved og available inngår ikke i den godkjente matrisen og er derfor ikke implementert i Steg 5.

### 22.3 Gruppering, valuta, tabell og skalavern

- Faste grupperinger er kind, typeavhengig status og eier. Dynamisk gruppering bruker bare Leasing-avtaledimensjoner. Flerverdi lar én post forekomme i hver valgt gruppe, mens unik total beregnes før utvidelsen og metadata markerer gruppene ikke-additive.
- Framework count bruker overlappende kalenderbøtter og separat unik periodetotal. Acquisition count og kjøpsverdier bruker punktdatoen PurchaseDate. Kapasitetsmåltall er CurrentSnapshot; tidsgruppering og line chart avvises av matrisen.
- Penger returneres som separate verdier per faktisk valuta uten omregning. Monetært KPI med flere valutaer avvises av eksisterende ReportResultValidator.
- Tabeller returnerer egne begrensede Agreement-/Leasing-rad-DTO-er gjennom felles tabellkontrakt, deterministisk sortering, sidestørrelse 1–100 og total for hele utvalget uavhengig av paging.
- Distinct counts, enkle summer, valutapartisjonering og faste grupperinger utføres som databaseaggregerbare queries. Kapasitet lastes i bulk for maksimalt 1 000 autoriserte rammeavtaler og bruker én account-avgrenset gruppert anskaffelsesspørring. Dimensjons-/overlappsberegning som krever serverminne tillater maksimalt 1 000 autoriserte headers. Grensen gir ReportExecutionTooBroad; ingen data trunkeres stille og ingen per-post-spørring utføres.

### 22.4 Faktisk verifikasjon

| Kontroll | Resultat | Evidens/begrensning |
|---|---|---|
| TenantPlatform.Reports.SmokeTests | PASS, 223 kontroller totalt | 34 nye Leasing execution-kontroller dekker direkte tenant-/ressurstilgang, framework-/anskaffelsesscope, limit-reader, kind/status/eier/søk, hierarki/AND/unspecified/historisk snapshot, framework/underliggende/frittstående grain, flerverdigrupper, alle elleve metrics, eksisterende linjeavrunding, original kontra kredit/reversering kontra frigivelse, netto/MVA/brutto, valutaoppdeling og KPI-avvisning, tidsbøtter, paging, tomt utvalg og eksplisitt størrelsesgrense. EF InMemory beviser ikke Npgsql-oversettelse. |
| Økonomisk fixtureavstemming | PASS, databasefri | Originale totals er 200 EUR + 400 NOK netto, 40 EUR + 100 NOK MVA og 240 EUR + 500 NOK brutto. Etter kredit/reversering er de 180/36/216 EUR og 385/96,25/481,25 NOK. Alpha-rammens brukte kapasitet er 92 netto + 23 MVA = 115 NOK, identisk med LeasingCalculator.Used; reserverte beløp og rammegrense inngår ikke. |
| TenantPlatform.AgreementDimensions.SmokeTests | PASS, 166 kontroller | InMemory/offline metadata. Kjørt som Agreements-/felles dimensjonsregresjon etter endringen i felles resultatkontrakt og kjøreruting. Ingen databaseforbindelse eller SQL. |
| TenantPlatform.Dimensions.SmokeTests | PASS, 228 kontroller | Databasefri service-/komponentregresjon og offline migrasjonsaudit. Leasing-filtre, snapshots og dimensjonsregler består. Ingen databaseforbindelse, SQL eller migrasjonsanvendelse. |
| TenantPlatform.Leasing.SmokeTests | BUILD PASS, **IKKE KJØRT** | PostgreSQL-runneren bygger med 0 feil. Den ble ikke startet fordi dette steget forbød databaseforbindelser og PostgreSQL-tester. |
| dotnet build TenantPlatform.sln --no-restore | PASS | 0 feil. Eksisterende NU1902/NU1903-advarsler for System.Security.Cryptography.Pkcs 10.0.0 består. |
| git diff --check | PASS | Ingen whitespace-feil etter Steg 5-endringene. |

### 22.5 Åpne verifikasjonsgrenser og utsatt scope

- Npgsql-oversettelse av nye Leasing-filter-, union-, group/sum-, DateOnly- og tabellspørringer er **IKKE VERIFISERT**. InMemory-testene er ikke providerbevis.
- PostgreSQL-queryplaner, indeksbruk, faktisk query count, ytelse ved 1 000-grensene og cancellation mot Npgsql er ikke målt.
- Den PostgreSQL-avhengige Leasing-runnerens concurrency-, rollback-, invoice-, payment-, lifecycle- og capacity-scenarioer er ikke kjørt i denne runden. Byggestatus er ikke funksjonell PASS.
- Steg 3 sin PostgreSQL-verifikasjon av migrasjon, jsonb, FOR UPDATE, concurrency og rollback står fortsatt åpen. Steg 4 sine providerkritiske Agreements-spørringer er også fortsatt uverifisert mot PostgreSQL.
- Ingen rapport-UI, stabil rapportside-rute, ECharts, drag/resize eller sammenhengstre er implementert. P6 gjelder fortsatt først når Steg 8 starter.
- Leasing linje-/fordelingsdimensjoner, kapasitetshistorikk, valutaomregning og metrics utenfor matrisen er fortsatt uttrykkelig utsatt.

### 22.6 Ferdigstatus og anbefalt neste avgrensning

Steg 5 er **ferdig som kildeimplementasjon og databasefri verifikasjon**. Neste naturlige kildeoppgave er Steg 6: stabil rapportside-URL, tydelig view/edit-modus og en full klientarbeidskopi der Avbryt forkaster alle kort-, layout- og flytteendringer. PostgreSQL-verifikasjon av Steg 3–5 beholdes som en separat, eksplisitt testaktivitet og skal ikke regnes som gjennomført av denne statusen.

## 23. Faktisk resultat for Steg 6 — rapportside-UI og arbeidskopi

Status 13. november 2026: **KILDEIMPLEMENTASJON FERDIG innenfor avtalt databasefritt scope**. Steg 7 og 8 er ikke startet. Ingen databaseforbindelse eller applikasjonsinstans ble startet.

### 23.1 Ruter, visningsmodus og sideadministrasjon

- `/reports` viser brukerens/administratorens tilgjengelige private rapportsider og støtter oppretting, duplisering og sletting med sidens kortantall i bekreftelsen. Duplikatet navigerer til den nye stabile ID-en.
- `/reports/{ReportPageId:guid}` laster den konkrete siden direkte og åpner alltid i visningsmodus. Side-ID og URL endres ikke ved navneendring. Sideadministrasjon er skjult mens en arbeidskopi redigeres.
- Navigasjonen viser Rapporter når brukeren har Agreements- eller Leasing-tilgang. Sidetjenesten reautoriserer konto, medlemskap, sideeierskap/admin og modul ved alle lesinger og writes; UI-et gir ingen utvidet data- eller modultilgang.
- Hvert lagret kort kjører isolert. KPI, grupper, tidsserier og detaljer vises lesbart; bar-, line- og donutvalg får en tilgjengelig tabell som midlertidig presentasjon. Et ugyldig/utilgjengelig kort får egen feiltilstand uten å stoppe andre kort. Sammenhengstre viser eksplisitt utsatt-status og definisjonen endres ikke.

### 23.2 Redigering og full lokal arbeidskopi

- `ReportWorkspaceState` lager dype kopier av side, kort, definisjon, periode, filterliste, typed filteridentiteter og filterverdier. Oppretting, kortredigering, duplisering, sletting, navn og tilgjengelige opp/ned-knapper endrer bare arbeidskopien; eksisterende layoutverdier beholdes.
- Korteditoren bruker modulens autoriserte katalog for filtre, dynamiske dimensjoner, grupperinger og kompatibilitetsvalg. Modul-, metric- og visualiseringsendringer kjøres gjennom den eksisterende normaliseringen, og tilbakestilte felt vises. «Bruk endringer» oppdaterer bare arbeidskopien.
- Flytting laster målsidens lagrede snapshot og revision én gang og bevarer tidligere lokale endringer. `BuildRequest` sender alle og bare endrede sider med forventet revision i ett `SaveWorkspaceAsync`-kall. Kort-ID beholdes ved flytting.
- «Avbryt» bygger alle berørte sider på nytt fra lagrede snapshots og utfører ingen tjenestewrite. En åpen korteditor blokkerer side-save og avbryt. Ved concurrency beholdes arbeidskopien; brukeren kan eksplisitt forkaste den og laste serverversjonen på nytt.
- `NavigationLock` varsler ved faktiske ulagrede endringer for intern og ekstern navigasjon. Kortkjøring bruker cancellation, definisjonsfingeravtrykk og generasjonsnummer slik at irrelevant rerender ikke kjører på nytt og et sent svar ikke kan erstatte et nyere resultat.
- Nye tekster finnes i nb-NO, en-GB og sv-SE. Dialoger og kort bruker labels, headings, tabellcaption, status-/alertroller, knappetyper og responsive Bootstrap-oppsett.

### 23.3 Faktisk verifikasjon

| Kontroll | Resultat | Evidens/begrensning |
|---|---|---|
| `TenantPlatform.Reports.SmokeTests` | PASS, 237 kontroller totalt | 14 nye kontroller dekker stabile ruter, visningsmarkup på nb-NO/en-GB/sv-SE, fravær av redigeringskontroller i visningskort, dyp kopi uten delte muterbare referanser, lokale create/edit/delete/duplicate/reorder-operasjoner, full cancel, to-siders move-request, revisions, bevart kort-ID og beholdt arbeidskopi ved avvist save. Eksisterende kontroller dekker stabil ID ved rename, ny ID ved duplicate, tilgang, atomisk workspace-save, konflikt uten delvis write og validering. |
| `dotnet build TenantPlatform.sln --no-restore` | PASS | 0 feil. De eksisterende NU1902/NU1903-advarslene for `System.Security.Cryptography.Pkcs` 10.0.0 består. |
| `git diff --check` | PASS | Ingen whitespace-feil. |

Smoke-testene bruker ren logikk, `HtmlRenderer` og EF InMemory. De er ikke faktisk nettleser-, JavaScript-, InteractiveServer- eller PostgreSQL-verifikasjon. Komponentkontrollene verifiserer tilgjengelig renderet markup, men ikke visuell layout, fokusflytting eller nettleserens native refresh/close-varsel. Korteditorens interaktive normaliserings- og valideringsflyt er kompilert og bygger på servicekontrollene, men er ikke kjørt gjennom en ekte nettleser i denne runden.

### 23.4 Åpne runtimegrenser og utsatt scope

- PostgreSQL-runtimeverifikasjon av Steg 3–5 er fortsatt åpen: migrasjonsanvendelse, `jsonb`, `FOR UPDATE`, concurrency/rollback og Npgsql-oversettelse/ytelse er ikke bevist av denne kjøringen.
- Direkte URL, server circuit, navigasjonsvarsel, dialogfokus, responsivitet, cancellation/stale-response under reell latency og full brukerflyt for sideadministrasjon/flytting er ikke browserverifisert.
- På tidspunktet for Steg 6-rapporten var ECharts/layout og sammenhengstre/P6 ikke startet. De senere resultatene står i kapittel 24–25.
- Startsidevalg, deling, nye rettigheter, nye metrics, nye økonomiske regler og Leasing linje-/fordelingsdimensjoner er fortsatt utenfor scope.

### 23.5 Ferdigstatus og anbefalt neste avgrensning

Steg 6 er **kildeferdig og databasefritt verifisert innenfor grensene over**. Neste avgrensede kildeoppgave kan være Steg 7 (ECharts og eksplisitt layoutinteraksjon), mens PostgreSQL-verifikasjon av Steg 3–5 fortsatt behandles som en separat aktivitet og ikke som implisitt PASS.

## 24. Faktisk resultat for Steg 7 — ECharts og responsivt rapportgrid

Historisk status ved avslutningen av Steg 7 den 13. november 2026: **KILDEIMPLEMENTASJON FERDIG OG MÅLRETTET NETTLESERVERIFISERT innenfor avtalt databasefritt scope**. Steg 8 og P6 var da ikke startet. Ingen databaseforbindelse eller applikasjonsinstans ble startet.

### 24.1 Lokal ECharts-distribusjon og diagramlivssyklus

- Apache ECharts `5.6.0` er vendoret som den offisielle npm-distribusjonens `dist/echarts.min.js` under `wwwroot/lib/echarts/`. SHA-256 er `bf4a223524e40b77c304bec67e1222cf551f14880cf42c69dc046558e11c07b1`. Kilde-URL, versjon og hash er dokumentert i `VERSION.md`; pakkens Apache-2.0 `LICENSE` og `NOTICE` ligger ved siden av filen. Det finnes ingen ECharts-CDN- eller frontendbyggeavhengighet.
- `ReportChart` eier sin stabile elementreferanse, importerer `report-charts.js` først etter interaktiv render og disponerer JS-objekt, `ResizeObserver`, tema-`MutationObserver`, ventende animation frame og ECharts-instans. Adapteren gjenbruker en eventuell eksisterende instans, oppdaterer options uten reinitialisering, følger Bootstrap-temaendringer, ignorerer nullstørrelse og resizer når elementet senere blir målbart. `JSDisconnectedException`, cancellation, JS-loadfeil og dispose håndteres uten å fjerne tabellalternativet.
- `ReportChartMapper` lager et begrenset chart-DTO for bar, line og donut fra det ferdige serverresultatet. Serverens gruppenøkler beholdes; tidsbøtter sorteres etter år/bucket; valutaer blir separate serier; fraværende serieverdier forblir `null`; og ingen summering, filtrering eller økonomisk beregning gjøres i JavaScript.
- Donut forsvares på nytt mot tidsserie, valuta, negative verdier, overlappende medlemskap og ikke-additive grupper. En ugyldig mapping viser en lokalisert feil, mens den tilgjengelige resultattabellen beholdes. Manglende data gjøres aldri om til null uten støtte i resultatkontrakten.
- ECharts bruker canvas, `tooltip.renderMode = richText`, ingen HTML-formatter og tekstdata som ordinære option-verdier. Lagrede etiketter sendes dermed ikke som ukodet HTML. ARIA-beskrivelse, synlig tabellalternativ og forklaring om ikke-additive grupper beholdes. `prefers-reduced-motion` slår av animasjon.

### 24.2 Grid- og kollisjonsalgoritme

- Gridet bruker den eksisterende kontrakten `GridColumn`, `GridRow`, `GridWidth`, `GridHeight` og `SortOrder`; ingen ny lagringsmodell er innført. Desktop har 12 kolonner og 3-rem rader. `SortOrder` bestemmer stabil behandlings-/leserekkefølge, mens koordinater og størrelse bestemmer plassering.
- Normalisering behandler kort deterministisk etter `SortOrder` og ID. Hvert kort prøver lagret rad/kolonne først, deretter første ledige kolonne på samme rad og så neste rad. Ved aktiv flytting får det flyttede kortet prioritet på målposisjonen, og kolliderende kort plasseres deterministisk rundt det. Bredde klampes til 1–12 ved lesing av lagret kontrakt.
- Interaktiv resize bruker minimum 3 kolonner × 3 rader og maksimum 12 kolonner × 12 rader. Dette er presentasjonsgrenser innenfor eksisterende servervalidering; schemaet er uendret. Kortinnhold kan scrolle innenfor den valgte høyden.
- Under 768 px bytter CSS til én lesbar kolonne. Denne ombrekkingen endrer aldri arbeidskopien eller lagrede desktopkoordinater.
- Eksisterende lagrede kollisjoner vises deterministisk uten automatisk databasewrite. Når brukeren faktisk endrer arbeidskopien, normaliseres alle berørte koordinater og sendes gjennom den eksisterende workspace-kontrakten ved samlet lagring.

### 24.3 Flytting, resize og tilgjengelig alternativ

- `ReportGrid` viser dra- og resize-håndtak bare i redigeringsmodus. Den lille JS-adapteren bruker Pointer Events for mus/touch, `touch-action: none` bare på håndtakene, lokal snapping under gesten og én .NET-callback ved fullføring. Klikk på kortinnhold, diagram, tabell eller andre knapper starter ingen gest.
- Den flyttede/resizede plasseringen vises direkte med outline. `Escape` og `pointercancel` gjenoppretter startkoordinatene. Alle event listeners fjernes ved dispose.
- Generasjonsnummer følger hver redigeringsøkt. Både JS og Blazor avviser callback fra en eldre arbeidskopi eller avsluttet redigering. Callback endrer bare `ReportWorkspaceState`; databasewrite skjer fortsatt bare ved «Lagre endringer».
- Opp/ned-knappene er bevart. Et eget lokalisert tastaturalternativ gjør kortet smalere/bredere/lavere/høyere gjennom samme klampede layoutsti. Status annonseres først etter fullført endring, ikke for hver pointerbevegelse.
- Layoutfeltene inngår fortsatt i arbeidskopiens fingerprint og `SaveWorkspaceAsync`. «Avbryt» gjenoppretter posisjon og størrelse for alle berørte sider. Layout-rerender endrer ikke kortdefinisjonens fingerprint og kjører derfor ikke rapportspørringen på nytt.

### 24.4 Faktisk verifikasjon

| Kontroll | Resultat | Evidens/begrensning |
|---|---|---|
| `TenantPlatform.Reports.SmokeTests` | PASS, 255 kontroller totalt | Nye databasefrie kontroller dekker separate valutaserier, stabil gruppenøkkel, kronologisk tidsrekkefølge, bevart `null`, additivitetsmetadata, gyldig/ugyldig donut, deterministisk kollisjon/prioritet, størrelsesgrenser, layout-roundtrip, full cancel, tastatur-resize, read-only uten håndtak, tilgjengelig markup, avvisning av gammel generation og at layout-only rerender ikke kjører rapporten igjen. |
| `node tests/TenantPlatform.Reports.SmokeTests/js/report-modules.test.mjs` | PASS | Mock-harness dekker ECharts create/update/hidden-size/resize/dispose, `ResizeObserver`-opprydding, redusert bevegelse, bevart null, rich-text tooltip, én pointer-commit, snapping, pointercancel og fjerning av pointer-/Escape-handlers. Dette er ikke faktisk ECharts-canvas- eller nettleserverifisering. |
| `node tests/TenantPlatform.Reports.SmokeTests/browser/run-browser-harness.mjs` | PASS, Chrome 157.0.8103.78 | Faktisk headless Chrome: 43 kontroller i CSS-viewport `1440×913` og 44 i `500×757` (forespurte vinduer `1440×1000` og `390×844`; Chrome har en minste headless-bredde på denne plattformen). Se 24.5 for testet scope og avgrensning. |
| `dotnet build TenantPlatform.sln --no-restore` | PASS | 0 feil. De eksisterende NU1902/NU1903-advarslene for `System.Security.Cryptography.Pkcs` 10.0.0 består. |
| `git diff --check` | PASS | Ingen whitespace-feil. |

### 24.5 Målrettet, isolert nettleserverifikasjon

- En ny, repeterbar harness under `tests/TenantPlatform.Reports.SmokeTests/browser/` starter en flyktig loopback-server og headless Chrome. Den laster produksjonsmodulene `report-charts.js` og `report-grid.js`, den vendorede ECharts 5.6.0-filen, komponent-CSS og relevant repository-CSS direkte. Diagram- og pointerlogikk er ikke kopiert til en testimplementasjon.
- Syntetiske nettleserdata produseres av en særskilt databasefri fixture-modus i Reports-smoke. Dermed brukes de faktiske `ReportChartMapper`, `ReportLayoutEngine` og `ReportWorkspaceState` til chart-DTO-er, kollisjon, resizegrenser og tastaturalternativets arbeidskopiresultater. Harnessen starter verken apphost, Blazor-circuit eller database.
- Diagramkontrollene består for bar, line og donut, flere samtidige instanser, separate og stabilt ordnede valutaserier, kronologisk rekkefølge over årsskifte, bevarte manglende verdier, tomt resultat, avvist ugyldig donut, lang etikett med spesialtegn/HTML-lignende tekst, oppdatering uten dobbel instans/canvas, synlig resize, nullstørrelse og senere synliggjøring, temaoppdatering, redusert bevegelse, dispose og gjenoppretting. Tooltip bruker faktisk ECharts rich-text/canvas; lagret tekst opprettet ingen HTML-elementer eller scriptkjøring.
- Gridkontrollene består for musedrag med snapping, minimum/maksimum resize, deterministisk kollisjonsfixture, Escape, `pointercancel`, klikk/pointeraktivitet i diagram og handling uten drag, scrolling utenfor håndtak, read-only uten håndtak eller mutasjon, tastaturknapper med fokus/status og samme C#-arbeidskopisti, desktop-grid, smal énkolonnevisning uten overskriving av desktopverdier, syntetisk touch-pointer, dispose av pågående gest/stale callback og uendret ECharts-instans ved layoutendring.
- Ingen feil i produksjonsmodulene ble påvist. Under etableringen ble selve runneren korrigert til å vente på en eksplisitt ferdigmelding fra reell browser-tid i stedet for Chromes virtuelle klokke; dette var en harnessfeil, ikke en endring i produksjonskoden. Temaet injiserer Bootstrap 5.3-lignende fargevariabler før `data-bs-theme` endres, fordi repoets lokale Bootstrap 5.1-distribusjon ikke inneholder fargevariablene fra Bootstrap 5.3.8 som den faktiske appskallet laster.
- Kjør på nytt fra repo-roten med `node tests/TenantPlatform.Reports.SmokeTests/browser/run-browser-harness.mjs`. Kommandoen skriver browser/version, faktisk CSS-viewport, antall kontroller og en midlertidig katalog med JSON-resultat per viewport.

### 24.6 Åpne runtimegrenser og utsatt scope

- Harnessen verifiserer faktisk canvas-rendering, options/livssyklus, DOM-sikkerhet, ResizeObserver og browserens layout/eventhåndtering, men ikke visuell pikselperfeksjon, manuell tooltipplassering eller hele rapportsidekomponenten i Blazor InteractiveServer.
- Pointerkontrollene er syntetiske browser-`PointerEvent`s. Mus- og touch-typer går gjennom den faktiske produksjonsadapteren, men fysisk mus, touchskjerm og stylus, subjektiv snappingfølelse og operativsystem-/browserkombinasjoner utenfor den kjørte Chrome-versjonen er ikke verifisert.
- Blazor InteractiveServer-livssyklus, circuit-disconnect, reell .NET interop over circuit, native navigasjons-/refreshvarsler og Avbryt gjennom en faktisk serverøkt er fortsatt **IKKE VERIFISERT**. C# smoke dekker arbeidskopi, generation og full cancel databasefritt; dette er ikke runtimebevis for circuit/navigasjon.
- Testet smal CSS-viewport var `500×757`, som aktiverer produksjonens breakpoint under 768 px. Eksakt `390px`, øvrige skjermstørrelser og en full browser-/tilgjengelighetsmatrise er ikke testet. Temaobservasjonen er verifisert med de forventede CSS-variablene, men ekstern Bootstrap 5.3.8 ble ikke lastet av den isolerte, nettverksuavhengige harnessen.
- PostgreSQL-runtimeverifikasjon av Steg 3–5 er fortsatt åpen: migrasjon, `jsonb`, `FOR UPDATE`, concurrency/rollback, Npgsql-oversettelse og ytelse er ikke bevist av Steg 7.
- Ved avslutningen av Steg 7 var sammenhengstre, nodedetaljer, tresøk og P6 ikke startet. De er senere implementert i Steg 8, se kapittel 25. Ingen nye metrics, økonomiske regler, deling, startside eller rettigheter ble innført i Steg 7.

### 24.7 Ferdigstatus og neste avgrensning

Steg 7 har **tilstrekkelig målrettet, databasefri nettleserverifikasjon til å kunne gå videre til Steg 8**, med de uttrykkelige runtimegrensene over. Denne statusen starter eller implementerer ikke Steg 8 og gjør ingen PostgreSQL- eller InteractiveServer-påstand.

## 25. Faktisk resultat for Steg 8 — progressive sammenhengstrær

Status 13. november 2026: **KILDEIMPLEMENTASJON FERDIG OG MÅLRETTET DATABASEFRITT NETTLESERVERIFISERT innenfor grensene under**. P6 er godkjent og implementert. Ingen databaseforbindelse, migrasjon eller TenantPlatform-applikasjonsinstans ble startet.

### 25.1 Typed tjeneste, struktur og autorisasjon

- `IReportTreeService` tilbyr `GetRootAsync`, paginert `GetChildrenAsync`, serverbasert `SearchAsync` og `GetNodeAsync`. Kontraktene returnerer begrensede node-, path-, treff- og detalj-DTO-er; EF-entiteter og `IQueryable` forlater aldri den kortlivede factory-contexten.
- `ReportTreeNodeId` skiller modul, nodetype og faktisk entitets-GUID. Syntetiske noder har egen `ReportTreeSyntheticNode`-identitet og kan ikke forveksles med en rammeavtale. Navn og lokaliserte etiketter brukes aldri som identitet.
- Agreements følger konto → autoriserte avtaler → økonomilinjer. Linjer velges som siste lagrede `AgreementLineVersion` per stabil `LineId` (høyeste `Sequence`), slik at versjoner ikke blir dupliserte trenoder.
- Leasing følger konto → nødvendige rammeavtaleforfedre → autoriserte anskaffelser → varelinjer → faktisk registrert utstyr. Frittstående anskaffelser følger P6 via den lokaliserte syntetiske grenen «Uten rammeavtale». Grenen utelates når den ikke har relevante etterkommere og har ingen falsk åpnehandling. Utstyr bruker faktisk `LeasingEquipment.Id`; `LeasingItem.Quantity` lager aldri antatte utstyrsnoder.
- Hver operasjon validerer rapportdefinisjon/katalog, valgt konto, aktivt medlemskap, modultilgang og eksisterende Agreements-/Leasing-ressursscope på nytt. Den forespurte noden må fortsatt ligge i det filtrerte utvalget. Sideeierskap eller adminrett til rapportsiden utvider ikke moduldatarettighetene. Manipulerte modul-/nodeidentiteter, fremmed konto og filtrerte noder avvises.
- Detaljer bruker bare eksisterende felt. Verifiserte mål er `/agreements/{id}`, `/agreements/{id}/lines`, `/leasing/frameworks/{id}`, `/leasing/acquisitions/{id}` og `/leasing/equipment?acquisition={id}`. Varelinjen faller tilbake til anskaffelsen; syntetisk node har ingen URL. Ingen beløp eller nye økonomiske regler er lagt til.

### 25.2 Filter-, periode- og lastesemantikk

- Agreements bruker samme autoriserte base, record-filtre, attention-dato, `DimensionListFilters`, historiske valg, «Ikke angitt», etterkommere og AND-semantikk som listen. Rapportperioden velger avtaler hvis faktisk avtaleperiode overlapper perioden. De siste linjesnapshotene vises som relasjonskontekst under en valgt avtale; linjene periodiseres ikke separat.
- Leasing bruker samme autoriserte acquisition-scope, kind/status/eier/søk og effektive avtalenivådimensjoner som listen. Rapportperioden velger anskaffelser etter inklusiv `PurchaseDate`, i samsvar med matrisen. Rammeavtalen er en nødvendig forfader og ikke et selvstendig periodetreff. Varelinjer og faktisk utstyr er relasjonskontekst under en valgt anskaffelse og får ingen ny periodisering.
- Rot og første barneside lastes ved åpning. Øvrige barn lastes ved utvidelse. Tjenesten krever eksplisitt offset og sidegrense 1–100; UI-et bruker 50 og viser «Last flere» når `NextOffset` finnes. Det skjer ingen stille trunkering. UI-søk bruker maksimalt 25 treff, mens tjenestekontrakten tillater opptil 50.
- Server-søk går i hele det aktuelle autoriserte/filterte scopes, også når grenen ikke er lastet. Avtaletittel/motpart/linjenavn/-beskrivelse og Leasing-navn/referanse/varelinje/varenummer/utstyrsbeskrivelse/serie-/intern-ID inngår. Treffet inneholder typed ID og full ancestor-path. Klienten laster manglende sider i rekkefølge, utvider pathen og åpner detaljpanelet etter stabil ID.
- Definisjonsfingeravtrykket omfatter konto, modul, datogrunnlag, perioderegel/-datoer og serialiserte filtre. Definisjonsendring avbryter pågående operasjoner, øker generation og tømmer lokal trecache. Kortets layout inngår ikke og utløser derfor ikke full trelasting. Søk, utvidelser, valgt node og ECharts-visning er lokal presentasjonstilstand og muterer ikke `ReportWorkspaceState` eller lagret definisjon.

### 25.3 UI, ECharts og tilgjengelig alternativ

- `ReportCardView` kjører ikke den aggregerte `ReportExecutionService` for `RelationshipTree`, men viser den nye `ReportTree`. Korteditoren skjuler metric, aggregation og grouping for treet og bruker modulmatrisens tree-date-basis; filtre og periode beholdes.
- `ReportTreeChart` utvider den lokale `report-charts.js`-adapteren med ECharts `tree`: venstre-til-høyre, pan/zoom, markert node, stabile ID-er, lange avkortede etiketter, rich-text-tooltip, redusert bevegelse, temaoppdatering, resize, update uten ny instans og full dispose. Zoom og senter beholdes ved dataoppdatering.
- Diagramklikk velger detaljer. Utvidelse/sammentrekking er en egen handling i den parallelle semantiske `<ul role="tree">`-visningen, med egne ikke-submit-knapper, `aria-expanded`, `aria-selected`, eksplisitt «Last flere» og lesbar kontekst. Canvas eller mus er derfor ikke eneste betjening.
- Detaljpanelet har loading/error uten å fjerne treet, lokaliserte feltetiketter, verifisert «Åpne»-mål, lukkeknapp og Escape. Fokus flyttes til detaljoverskriften og returneres til søkefeltet ved lukking. Oppsettet går til én kolonne på smal skjerm.
- Målrettet gjennomgang av asynkron flyt avdekket at to detalj- eller barnelastinger innen samme definisjonsgeneration kunne fullføres i motsatt rekkefølge. Dette er rettet med en egen detaljgeneration og per-node-generationsnummer i tillegg til definisjons- og søkegeneration. Et sent svar kan dermed ikke erstatte nyere detaljinnhold eller barneside. Ingen diagramfeil ble påvist i den faktiske Chrome-harnessen.

### 25.4 Faktisk verifikasjon

| Kontroll | Resultat | Evidens/begrensning |
|---|---|---|
| `TenantPlatform.Reports.SmokeTests` | PASS, 278 kontroller | EF InMemory/ren logikk/`HtmlRenderer`. Nye kontroller dekker begge strukturer, P6, tom syntetisk gren, stabile typed ID-er/like navn med ulik path, siste `LineId` uten versjonsduplikat, perioder, hierarkiske/arvede dimensjonsfiltre, standalone-kind, tenant/ressursscope, filtrert/manipulert node, søk i ikke-lastet gren, ancestor-path, paginering, faktiske equipment-ID-er, detaljer/ruter, syntetisk node uten rute og tilgjengelig komponentmarkup uten aggregert kjøring. Dette beviser ikke Npgsql-oversettelse. |
| `node tests/TenantPlatform.Reports.SmokeTests/js/report-modules.test.mjs` | PASS | Mock-harness dekker tree option/type/orientering/pan-zoom, stable ID, rich-text, valgt node, update med bevart zoom/senter, generation-bundet klikk, resize og dispose uten handler-/instanslekkasje. |
| `node tests/TenantPlatform.Reports.SmokeTests/browser/run-browser-harness.mjs` | PASS, Chrome 157.0.8103.78 | Faktisk lokal ECharts-canvas: 50 kontroller ved `1440×913` og 51 ved `500×757` (forespurte vinduer `1440×1000`/`390×844`). Treet renderer, bruker LR/roam, behandler HTML-lignende tekst som tekst, gjenbruker instans/canvas, bevarer zoom, resizer og disponerer. Harnessen er databasefri og starter ikke Blazor/apphost. |
| `TenantPlatform.AgreementDimensions.SmokeTests` | PASS, 166 kontroller | InMemory og offline metadata; ingen databaseforbindelse eller SQL. |
| `TenantPlatform.Dimensions.SmokeTests` | PASS, 228 kontroller | InMemory, komponenter og offline design-time metadata; ingen databaseforbindelse, SQL eller migrasjonsanvendelse. |
| `dotnet build TenantPlatform.sln` | PASS | 0 feil. Eksisterende NU1902/NU1903 for `System.Security.Cryptography.Pkcs` 10.0.0 består. |
| `git diff --check` | PASS | Ingen whitespace-feil. |

Den isolerte nettleserharnessen kjøres på nytt fra repo-roten med `node tests/TenantPlatform.Reports.SmokeTests/browser/run-browser-harness.mjs`. Den laster produksjonsmodulen, lokal ECharts 5.6.0 og repository-CSS direkte; den kopierer ikke trelogikken.

### 25.5 Begrensninger og ferdigstatus

- Reports-smoke bruker EF InMemory. PostgreSQL/Npgsql-oversettelse av de nye trespørringene, queryplaner/query count, store datamengder, ytelse og provider-spesifikke forskjeller er **IKKE VERIFISERT**. PostgreSQL-baserte Agreements- og Leasing-smoke ble bevisst ikke startet i dette databasefrie steget.
- Den faktiske rapportsiden i Blazor InteractiveServer, circuit-disconnect, .NET↔JS-interop over circuit, reell nettverkslatency/cancellation og native navigasjon/refresh er **IKKE VERIFISERT**. Generasjons-/cancellationvern og arbeidskopi-isolasjon er databasefritt kontrollert, men er ikke et runtimebevis for disse forholdene.
- Headless Chrome-harnessen verifiserer faktisk ECharts-rendering og responsive CSS-bredder, men ikke en full browser-/tilgjengelighetsmatrise, fysisk touch/stylus, pikselperfeksjon eller manuell skjermleserbruk. Automatisk path-utvidelse er kontrollert gjennom tjenestepath og komponentlogikk, ikke gjennom en faktisk InteractiveServer-sesjon.
- Ingen nye økonomiske regler, dagperiodisering, valutakonvertering, Leasing-linje-/kostnadsfordelingsdimensjoner, deling eller startsidefunksjon er innført.

Steg 8 er **kildeferdig og tilstrekkelig målrettet databasefritt verifisert til å gå videre til Steg 9**, med de uttrykkelige begrensningene over. Den senere Steg-9-verifikasjonen er dokumentert i kapittel 26 og [feature-reportmodule-verification.md](feature-reportmodule-verification.md).

## 26. Faktisk resultat for Steg 9 — samlet verifikasjon

Status 13. november 2026: **DELVIS GJENNOMFØRT.** Kildeimplementasjonen fra Steg 1–8 er bevart. Senere runtimeverifikasjon har anvendt hele migrasjonskjeden og rapportmigrasjonens down/up i en isolert PostgreSQL 17.10-klynge, kjørt Reports-fixturen gjennom Npgsql (288 PASS) og kjørt faktisk publisert Blazor InteractiveServer med Chrome 157 på desktop og smal viewport (25 PASS per viewport).

Detaljert miljø, kommandoer, datamengde, avstemming, queryplanstikkprøver, feilrettelser, regresjonsresultater og matrisen for alle 14 akseptansekriterier står i [feature-reportmodule-verification.md](feature-reportmodule-verification.md).

En providerfeil ble rettet: semantisk uendret `jsonb` ble tidligere sammenlignet som tekst og kunne feilaktig kreve modultilgang etter PostgreSQL-normalisering. Målrettet PostgreSQL-regresjon består. To ugyldige InMemory-fixture-referanser og en bytebasert JSON-testforventning ble også korrigert uten produksjonsregelendring.

Full Leasing PostgreSQL-smoke er ikke PASS: runneren stopper i en eksisterende kostnadsfordelingstest som forventer 25 % VAT selv om dens frittstående purchase-helper lager 0 % VAT. Feilen er diagnostisert og skilt fra rapportmodulen; testen ble ikke endret for å få grønt resultat. Full actual-app treeflyt, to samtidige browsersesjoner, circuit disconnect/reconnect, full språk-/tilgjengelighets-/browsermatrise, fysisk touch og komplett ytelses/query-count-matrise er fortsatt ikke gjennomført.

Steg 9 og hele funksjonen skal derfor ikke beskrives som fullt verifisert. Samlet status er **klar for neste leveransesteg med dokumenterte begrensninger**, ikke «alle akseptansekriterier PASS».

## 27. Senere UI-justering — valgt kort og felles oppsettspalett

Status 13. november 2026: **KILDEIMPLEMENTASJON FERDIG OG MÅLRETTET DATABASEFRITT NETTLESERVERIFISERT.** Dette er en etterfølgende interaksjonsjustering av redigeringsmodus og endrer ikke Steg 9-statusen, rapportdefinisjoner, lagringsmodell eller databaseverifikasjon.

- Redigeringsmodus har nå ett valgt kort identifisert med stabil `ClientId`. `ReportCardSelectionState` er bare midlertidig UI-tilstand, påvirker ikke `ReportWorkspaceState.IsDirty`, lagres ikke og ryddes ved sletting, flytting til annen side, Avbryt, lagring, modusbytte, parameterbytte og dispose. Tomt canvas fjerner valget, og Enter/Space kan velge et fokusert kort.
- Det gamle drahåndtaket er fjernet. Musedrag starter fra kortflaten først etter en eksplisitt 6 CSS-pikslers terskel. Knapper, lenker, skjemaelementer og resize-håndtak er unntatt. Touch beholder vanlig scrolling og krever 450 ms langt trykk før kortdrag; bevegelse før aktivering avbryter dragkandidaten.
- Bare valgt kort viser resize-håndtak og markeringsramme. Eksisterende piksel-/gridberegning, vertikal resize, snapping, kollisjon, Escape, pointercancel, generation-vern og én layoutcallback ved fullført gest er bevart.
- Rediger, dupliser, slett, flytt til annen tillatt rapportside og eksisterende flytte-/størrelseskommandoer ligger samlet i én lokalisert, fixed verktøypalett. Kortene inneholder ikke lenger egne handlingsknapper eller sidevelgere. Rediger gjenbruker samme editor og beholder valget; duplisering bruker workspace-reglene og velger kopiens nye ID; sletting og sideflytting rydder valget og flytter fokus til paletten. Sideflytting bruker fortsatt autorisert sideoppslag og atomisk workspace-lagring/Avbryt.
- Paletten kan flyttes via eget håndtak, klampes til viewport, korrigeres ved resize og tilbakestilles. Under 768 px bruker den en fast kompakt plassering med intern scrolling ved behov. Palettplassering og kortvalg er lokal UI-tilstand og endrer verken arbeidskopi eller rapportkjøring.
- `report-grid.js` har én delt gestlås for kortdrag, resize og palettdrag. Dispose og generation-skifte avbryter aktive/påbegynte kortgester; palettdrag ryddes separat og kan ikke mutere rapportlayouten.

### 27.1 Faktisk verifikasjon

| Kontroll | Resultat | Evidens/begrensning |
|---|---|---|
| `TenantPlatform.Reports.SmokeTests` | PASS, 286 kontroller | Databasefri InMemory/ren logikk/`HtmlRenderer`. Komponentkontroller dekker valgt markup, resize bare på valgt kort, stable/stale selection callbacks, alle paletthandlinger og tillatte målsider. Workspace-kontroller dekker korrekt ID etter omrekkefølge, uavhengig kopi med nytt valg, rydding ved sletting/sideflytting og full Avbryt. |
| `node tests/TenantPlatform.Reports.SmokeTests/js/report-modules.test.mjs` | PASS | Mock-harness dekker valg med stabil ID, klikk og bevegelse under terskel uten layoutcommit, kortflatedrag, kontrollunntak, resizegeometri, pointercancel, palettgrenser/reset og at palettdrag ikke endrer layout. Dette er ikke faktisk browser-rendering. |
| `node tests/TenantPlatform.Reports.SmokeTests/browser/run-browser-harness.mjs` | PASS, Chrome 157.0.8103.78 | Faktisk headless Chrome: 78 kontroller ved `1440×913` og 67 ved `500×757`. Dekker mus, tastatur og syntetisk touch, drag over diagramflate, terskel, horisontal/vertikal resize, selection/clear, kort uten handlingskontroller, alle paletthandlinger mot stabil ID etter omrekkefølge, kopi-valg, fokus/rydding, palettflytting/reset/scroll/resize, smal plassering, Escape/pointercancel/dispose og uendret view mode. |
| `dotnet build TenantPlatform.sln --no-restore` | PASS | 0 feil; eksisterende NU1902/NU1903 for `System.Security.Cryptography.Pkcs` 10.0.0 består. |
| `git diff --check` | PASS | Ingen whitespace-feil etter justeringen. |

Den isolerte harnessen starter ikke apphost, Blazor InteractiveServer eller database. Fysisk touch/stylus, full skjermlesermatrise og faktisk circuit-/navigasjonslivssyklus er fortsatt ikke verifisert av denne justeringen. Ved avslutningen av denne justeringen viste redigeringskortene fortsatt bare redigeringsinnhold; den senere passive forhåndsvisningen er dokumentert i kapittel 28.

## 28. Senere UI-justering — faktisk, passiv kortforhåndsvisning

Status 13. november 2026: **KILDEIMPLEMENTASJON FERDIG OG MÅLRETTET DATABASEFRITT NETTLESERVERIFISERT.** Endringen bruker eksisterende rapport-/tretjenester og endrer ikke rapportdefinisjon, arbeidskopi, lagringsmodell, schema eller migrasjoner.

### 28.1 Resultater og passiv presentasjon

- `ReportPage` bygger `ReportCardDto` direkte fra hvert `ReportCardDraft`. «Bruk endringer» i eksisterende editor oppdaterer fortsatt bare arbeidskopien; den endrede definisjonen blir deretter grunnlaget for en ny forhåndsvisning. Åpen editor kjører ingen rapport per tastetrykk.
- `ReportCardView` brukes nå i både visnings- og redigeringsmodus. KPI, tabell, stolpe, linje og donut bruker samme `IReportExecutionService`, kompatibilitetsvalidator, autorisasjon, kontolokale dato, perioderesolver og resultatkontrakt som visningsmodus. Lasting, tomt resultat, ugyldig definisjon, manglende tilgang og feil beholdes per kort.
- Redigeringsmodus setter eksplisitt passiv presentasjon. Tabellrader rendres uten lenker. ECharts får `tooltip.show=false`, `legend.selectedMode=false` og `series.silent=true`; treet får i tillegg `roam=false`. Kortinnholdet har derfor ingen søk, nodevalg, detaljpanel, ekspanderingsknapper, lenker eller andre fokuserbare kontroller bak kortflaten. Selve gridkortet er fortsatt tastaturfokus og valg-/dragflate, og valgt kort beholder resize-håndtaket.
- Diagramadapteren oppdaterer eksisterende ECharts-instans når modell eller interaksjonsmodus endres. Layout, resize og navn endrer ikke kjøregrunnlaget eller reinitialiserer diagrammet. Vanlig visningsmodus beholder tooltip, legendetoggling, tree pan/zoom, nodesøk, ekspandering, detaljer og navigasjon.
- Passivt sammenhengstre bruker `IReportTreeService` og laster deterministisk bare rot pluss første side på første nivå (standard 12 noder, konfigurerbart 1–100). Det rendres både som passivt ECharts-tre og lesbar tekstliste. Lokalisert tekst forklarer at forhåndsvisningen er begrenset; `NextOffset` markeres som trunkert. Ingen heltrelasting eller cached omgåelse av tilgang/filter brukes.

### 28.2 Cacheidentitet, levetid og ferskhet

- `ReportPreviewSession` er registrert `Scoped`, altså per aktiv Blazor circuit/request, og alle nøkler er i tillegg bundet til autentisert bruker, valgt konto og en unik rapportside-/arbeidskopiøkt. `ReportPage.Dispose` og parameter-/sidebytte avslutter økten og kansellerer/fjerner alle dens oppføringer. Dette er ikke en global cache.
- Kanonisk identitet omfatter modul, definisjonsversjon, typed filter-/dimensjonsidentiteter og verdier, perioderegel, faktisk løst fra-/til-intervall, lokal dato og tidssone, datogrunnlag, metric, aggregation, grouping, time grouping og relevant datashape. Tekstsøk normaliseres som eksisterende spørring (trim/lowercase), og uordnede filtre sorteres uten å endre noen intern verdiorden. Table page/page size og treets nivå/page size inngår.
- Navn, kort-ID, side-ID utover øktpartisjoneringen, plassering, størrelse, sortering, valgt kort og palettplassering inngår ikke. Key figure/bar/line/donut kan dele samme aggregatresultat når tjenestens request/data er lik; tabell er separat fordi den krever rader/paging. Exact visualiseringskompatibilitet valideres fortsatt før hvert cacheoppslag. `DisplayJson` er ikke med fordi dagens tjenestekjede ikke leser det og det påvirker derfor ikke resultatdata.
- Standardgrensene er fem minutters alder, 64 oppføringer, fire samtidige tjenestekjøringer og 12 første-nivå-treenoder. Verdiene kan overstyres under konfigurasjonsseksjonen `ReportPreview`. Fullførte least-recently-used-oppføringer fjernes ved kapasitetsgrensen; en side med bare maksimum pågående unike requests får en eksplisitt, lokalisert busy-feil fremfor ubegrenset vekst.
- Hvert uttak validerer gjeldende bruker/konto, modultilgang, autorisert katalog/definisjon og kontolokal dato før en cachehit kan returneres. Bruker-/kontoskifte og tilgangsavvisning tømmer økten. Relative regler får ny nøkkel når lokal dato/løst intervall endres. Paletten tilbyr «Oppdater forhåndsvisning», som pensjonerer gjeldende oppføring og kjører på nytt. Feil og cancellation lagres aldri som varige cachetreff.
- Cacheobjektet beholder egne kopier av resultatlistene og returnerer nye samlingskopier til hvert kort. Duplikater kan derfor dele beregningen uten å dele muterbar diagram-/UI-tilstand. Save lagrer fortsatt bare definisjon/layout. Avbryt bruker lagret definisjon; et utkastresultat kan ikke vises i visningsmodus fordi cacheidentiteten følger kjøregrunnlaget.

### 28.3 Samtidighet, cancellation og sene svar

- Identiske parallelle requests samles til ett pågående cachearbeid. En abonnent bruker egen cancellation; underliggende kjøring avbrytes først når ingen abonnenter gjenstår, økten avsluttes eller konteksten invalideres. Semaphore-grensen gjelder samlet aggregate- og trepreviewarbeid.
- `ReportCardView` bruker stabil kort-ID, kjøredefinisjonsfingeravtrykk, refresh-versjon, egen `CancellationTokenSource` og monoton generation. Resultat, feil og loadingstatus oppdateres bare når alle fortsatt tilhører siste generation. Definisjonsendring, sletting, flytting, Save, Avbryt, sidebytte og dispose kan derfor ikke la et sent svar overskrive et nyere kort.
- En tvungen oppdatering pensjonerer forrige cacheoppføring. En allerede abonnert forbruker kan fullføre den, men den gamle fullføringen får aldri erstatte den nye cacheverdien. Cancellation vises ikke som brukerfeil.

### 28.4 Faktisk verifikasjon

| Kontroll | Resultat | Evidens/begrensning |
|---|---|---|
| `TenantPlatform.Reports.SmokeTests` | PASS, 316 kontroller | Databasefri ren logikk, EF InMemory og `HtmlRenderer`. Nye kontroller dekker faktisk draft-definisjon, ingen ny execution ved layout/navn, ny execution ved kjørerelevant endring, filterkanonisering, chart-datadeling kontra tabellshape med ny resultatvalidering, gjenbruk av autorisert første-nivå-tre mellom passiv/interaktiv presentasjon, parallelldeduplisering, uavhengige resultatkopier, abonnentcancellation, retry etter feil, refresh, utløp, LRU-kapasitet, lokalt datoskifte, bruker-/tilgangsskifte, øktavslutning, tvungen A/B-race, maksimal samtidighet, passivt tre/tabell uten kontroller eller postlenker og komponentnivåets sene resultat/feil. Dette er ikke Npgsql-bevis. |
| `node tests/TenantPlatform.Reports.SmokeTests/js/report-modules.test.mjs` | PASS | Mock-harness dekker passive chart-/tree-options, ingen tree callback, update, resize og dispose med produksjonsmodulen. |
| `node tests/TenantPlatform.Reports.SmokeTests/browser/run-browser-harness.mjs` | PASS, Chrome 157.0.8103.78 | Faktisk lokal ECharts og produksjons-JS: 81 kontroller ved `1440×913`, 70 ved `500×757`. Verifiserer passive bar/tree-options, ingen nodecallback, ingen fokuserbare kortkontroller, valg/drag/resize over ekte canvas, palett, desktop/mobil CSS og fortsatt interaktivt ordinært chart/tree. |
| `dotnet build TenantPlatform.sln --no-restore` | PASS | 0 feil; eksisterende NU1902/NU1903 for `System.Security.Cryptography.Pkcs` 10.0.0 består. |
| `git diff --check` | PASS | Ingen whitespace-feil. |

Den statiske Chrome-harnessen bruker syntetiske DTO-er fra faktisk C# mapper/layoutmotor og produksjons-JS/CSS, men starter ikke Blazor eller database. Faktisk apphost/InteractiveServer-circuit, Npgsql-cancellation, PostgreSQL-query count/ytelse, fysisk touch/stylus, manuell skjermleser og full browsermatrise er derfor fortsatt **IKKE VERIFISERT** i denne justeringen. Ingen ny PostgreSQL-kjøring ble startet.
