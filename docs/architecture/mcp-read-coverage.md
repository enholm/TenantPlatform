# MCP-lesedekning – steg 9F

Status: **påbegynt, ikke ferdig**. Dette er en dekningsoversikt, ikke en gjentakelse av steg 4s arkitekturmigrering. En Application-operasjon som er migrert er ikke dermed publisert gjennom MCP. Produksjonsinngangene nedenfor er kontrollert mot arbeidskopien; siste del av steget skal avstemme både denne listen og steg 4-oversikten på nytt.

## Baseline og kontroll av 9E

9E finnes i kode: ServiceRequestReportingReader, ServiceRequestReportingData, service_requests_query/aggregate, ReportArea.ServiceRequests, PageDefinition v3, RequestPageDraft og deterministisk ServiceRequestPageSource. Eldre avtalesider er v1/v2 med Agreements som standardområde. Ingen oppgradering av gamle definisjoner er nødvendig for fristverktøyet i første 9F-uttrekk.

9E krever både personlig OAuth-scope, klientens AllowServiceRequests, profilbinding, verktøygodkjenning og tolv kolonneprivilegier. Registergodkjenning erstatter ikke OAuth eller SQL-tilgang. Brukerens rapporterte kildefeil ble undersøkt før 9F: scope-flagget ble aktivert, og brukeren rapporterte at alle tolv SELECT-kontrollene var false. Det er gitt en avgrenset GRANT-instruksjon; faktisk driftsaktivering eller vellykket interaktiv 9E-kjøring er ikke bekreftet her. Ingen driftsdatabase kontrolleres eller endres av testene.

## Konvensjoner

«Implementert» nedenfor gjelder bare angitt feltutvalg/verktøy. «Gjenstår» betyr reelt implementeringsarbeid, ikke en godkjent utelatelse. «Utelatt» krever en konkret faglig grunn. Vanlige datalister/detaljer er ikke utelatt fordi de krever nye porter eller tester.

L/D/A = egnet for liste, detalj eller aggregat. Alle nye områder trenger avgrensede DTO-er, stabile sider/svargrenser og felles autorisert filter før SQL-telling/gruppering. Før et område blir rapportkilde må det få en typet ReportArea-adapter og syntetiske opprettings-/redigeringsforløp. En detaljleser blir ikke automatisk rapportkilde.

## Avtaler

| Application-inngang | Autorisasjon | MCP og aktuelle felt/filtre | Egnethet/effekter | Status |
|---|---|---|---|---|
| AgreementListService.ListAsync; AgreementReportingReader.ListAsync/CountAsync | GetAgreementListScopeAsync, synlige avtaler før data | agreements_list/query/aggregate; tittel, type, status, arkiv, kontoavgrensede relasjoner, sluttdatointervall; Building/Status-grupper | L/A; skrivefri; kontodato, ingen historiske snapshots | Implementert for dokumentert 8D/9D-utvalg |
| AgreementDetailsReader.ReadSummaryAsync; ReadAsync; AgreementReadExtensions.DetailsAsync | GetAgreementReadScopeAsync | agreements_details v2: sammendrag pluss ordinære tekster, retning/valuta, kontoavgrenset indeksreferanse, periode/notice/terminering og datoer; eksplisitt SQL-projeksjon | D; skrivefri; false init gyldig | Ordinære aktuelle felt implementert i checkpoint4; avgrenset noticehistorikk gjenstår. Dokumentmetadata har egen rad |
| AgreementAttentionService.GetAttentionAsync | GetAgreementListScopeAsync | agreements_attention: Notice/Renewal/Expired, 90 dager, overlappende grupper | A; skrivefri, ikke summer kategorier | Implementert |
| AgreementDashboardService.GetDashboardContractsAsync | Felles avtalelisteomfang og prioriteringsregler | agreements_prioritized: eksisterende fem-avtalers prioritet, eksplisitt avgrenset utvalg uten falskt TotalCount | L; skrivefri | Implementert i checkpoint2 |
| AgreementInsightsService.GetInsightsAsync | GetAgreementListScopeAsync; begrensede hierarkialternativer | agreements_insights: Type/Counterparty/Geography/Organization/Status; valgt hierarkinode, Direct; ikke arkiv, maks100 grupper/options | A; skrivefri; hierarki må ikke dobbelttelles | Implementert via agreements_insights for fem dimensjoner i checkpoint2. Owner er faglig utelatt fra MCP fordi person-ID/navn ville lage en personkatalog; GUI er uendret |
| AgreementFollowupReader.ReadAsync; AgreementFollowupPageReader.ReadAsync; AgreementReadExtensions.HistoryAsync | GetAgreementReadScopeAsync på konkret avtale | agreements_followup: lagrede fristforekomster. agreements_followup_history: dato/type/frist-ID, stabil CreatedUtc/ID, fast side25 og faktisk total | L/D; skrivefri, ingen GUI-initialisering eller konstruerte snapshots | Frister og upersonlige lagrede hendelser implementert; personer/kommentarer/kø og tekniske preferanser er eksplisitt utenfor |
| AgreementDeadlineReader.ReadAsync | GetAgreementListScopeAsync | agreements_deadlines: Mine/Type/Kind/Status/State/horizon/custom dates, full total og ufiltrerte fristkategorier; ingen AssignedUserId eller personkatalog i transport | L/A; reader skrivefri; GUI-fasade initialiserer separat | Implementert i checkpoint2, Mine er bare betrodd innringerrelativt |
| AgreementManagementService.GetOptionsAsync; AgreementReadExtensions.RelationsAsync | Skriveeditor krever Create/Edit; ny leser krever felles avtalescope og faktisk bruk i synlige avtaler | agreements_relations: kontoavgrensede motparter/bygg/enheter/hierarkinoder, navn/ID, søk og fast side25 | Oppslag; skrivefri, ikke administratorens ubrukte katalog | Ordinære leseoppslag implementert i checkpoint4 med eksplisitt smalere omfang. Samlet skriveeditor-/person-/delingsvalg publiseres ikke |
| AgreementFinancialService.GetLinesAsync/ForecastAsync; AgreementFinanceReader | Samme konkrete avtaleinnsyn som eksisterende finanslesing; fersk kontroll før/etter data | agreements_financial_lines: separate side25-versjoner/priser. agreements_forecast: konkret begrenset periode, eksisterende indeks-/periodekalkulatorer, én valuta og separate retninger | L/D; skrivefri, ingen generering;500 beregningsperioder og eksplisitte inputgrenser | Implementert i checkpoint5 i eksplisitt økonomiunderutvalg; ingen finansrapportadapter eller faktura-/basis-writes |
| AgreementFinancialService.PreviewBasisAsync, ListBasisAsync, GetAffectedBasesAsync, GetBasisAsync, GetBulkBasisOptionsAsync, PreviewBulkBasisAsync, ListPendingBasisApprovalsAsync | Eksisterende liste-/konkret avtaleinnsyn, fersk kontroll; ingen nye godkjenningsrettigheter | agreements_bases / agreements_basis v1: lagrede sammendrag og eksakt snapshot, retning/status/fakturadato, SQL-side25 og reelle totaler | Skrivefri lagret lesing; Freshness=NotEvaluated, ingen rå snapshots/aktører/handlingsflagg | Delvis implementert checkpoint7: liste/detalj for lagrede snapshots; aktualitetsvurdering, preview, berørte grunnlag, bulkvalg/-preview og godkjenningslesing gjenstår |
| AgreementIndexService.GetIndicesAsync | Eksisterende kontovise Application-listescope, ferskt revalidert | Code/Name/Description/SourceOrigin/Resolution og eksplisitte verdi-/revisjons-DTO-er | agreements_indices / agreements_index_values v1; egne sider25, Current/History, periodefilter; ingen konstruert serie | Implementert checkpoint6; private aktør-/reason-/handlingsfelt utelatt |
| AgreementFollowupWriter.GetReminderSettingsAsync | Innstillingsautorisasjon i Application | Kontoens tekniske reminderpreferanser | Konfigurasjonsoppslag | Utelatt: teknisk administrasjon, ingen ordinær rapportkilde |
| AgreementAnalysisService.GetAnalysesAsync, FindAnalysisOrganizationsAsync | Konkret avtale-/utkastinnsyn | Analysemetadata må skilles fra modellutkast, rå tekst og personfelt | D; eksisterende OpenAsync er en write og må aldri brukes som leser | Metadata gjenstår; rå modell-/dokumentinnhold utelatt etter oppdraget |
| AgreementDocumentService.DownloadAsync; dokumentmetadata i AgreementDetailsDto | Konkret avtaleinnsyn og kontodokument | Fil-ID/navn/media/size/category/dato; aldri StorageKey/bruker-ID | D/L | Metadata gjenstår; innhold/binærnedlasting utelatt etter oppdraget |
| AgreementNoticeRules | Rene regler, ikke selvstendig autorisert inngang | Gjeldende form/notice/periode/fornyelse/registrert terminering gjennom agreements_details v2, eksisterende datoer/Attention beholdes | D; ingen sideeffekter | Aktuelle ordinære felter implementert i checkpoint4; historiske regel-snapshots gjenstår på detaljraden, ingen bypass-port |

Oppfølgingsmetadata som bevisst ikke følger fristverktøyet: person-ID-er/navn, fritekstkommentarer, ansvarligkandidater, leverings-/kømottakere og handlingsflagg. Dette begrenser transportkontrakten uten å endre GUI eller lagret eierskap. Historikkens upersonlige hendelsesdato/type er nå dekket av agreements_followup_history; dette er ikke noticehistorikk eller full audit.

## Struktur, belegg og tjenester

| Application-inngang | Autorisasjon | Aktuelle felt/filtre og dagens MCP | Egnethet/effekter | Status |
|---|---|---|---|---|
| BuildingService.GetBuildingsAsync/GetBuildingAsync | Buildings.Open på konto / Buildings.Read på konkret bygg; eksplisitt bred PropertyAdmin-liste er legacy | ID/navn/adresse; relasjonsnavn i avtaler er ikke full byggdekning | L/D/A; skrivefri | Gjenstår |
| UnitService.GetUnitsAsync/GetUnitAsync | Units.Open / Units.Read; listeunntak gir ikke detaljfullmakt | ID/navn/type/IsActive/bygg/parent; byggfilter | L/D/A; skrivefri; antall enheter er ikke belegg | Gjenstår |
| UnitService.GetParentCandidatesAsync | Units.Create på bygg | Redigeringsvalg | Oppslag | Utelatt: støtte for skriveeditor, dekk ordinært hierarki gjennom enhetslesing |
| OrganizationService.GetOrganizationsAsync/GetOrganizationAsync | GetOrganizationListAccessAsync / Organizations.Read; legacy listeomfang bevares eksplisitt | ID/navn/type/IsActive/nummer; ingen kontaktpersoner automatisk | L/D/A; skrivefri | Gjenstår |
| AccountRegisterService.ReadOrganizationElementsAsync/ReadGeographicAreasAsync | AccountStructure.Manage | Hierarkier/foreldre/path; avtalerelasjonenes navn er bare delvis dekning | L/D/A; entitetsretur må projiseres | Gjenstår |
| AccountService.GetAccountsAsync/GetAccountAsync; AccountContextReader.ReadAsync/ReadSelectedNameAsync | Plattformadministrasjon holdes adskilt fra eget medlemskap | accounts_list dekker egne MemberAccount ID/navn, ikke plattformkonto- eller adminopplysninger | L/D | Egne kontovalg implementert; plattformadminliste utelatt fra ordinær bedriftslesekatalog |
| OccupancyService.GetOccupanciesAsync/GetOccupancyAsync | Occupancies.Open / konkret ressurslesing, eksisterende brede listeunntak | Konto/bygg/tenantOrganization/unit, ValidFrom/To, leietaker/enhet | L/D/A; skrivefri; dagens Today bruker TimeProvider.GetLocalNow (vertens lokale dato), ikke avtalens reminder-tidssone; perioder og overlapp er ikke historiske snapshots | Gjenstår |
| OccupancyService.GetHierarchyConflictsAsync | Autorisert kontroll på valgt enhet og periode | Overlapp mot foreldre/barn; sensitivt hvem/når | Oppslag for skrivevalidering | Utelatt som skriveeditorverktøy; ordinære perioder må fortsatt dekkes separat |
| ServiceDefinitionService.GetServiceDefinitionsAsync/GetServiceDefinitionAsync | ServiceDefinitions.Open/Read på konto/definisjon | Kode, lokaliserte navn/beskrivelse, aktiv status | L/D/A; skrivefri | Gjenstår |
| ServiceDefinitionService.GetProvidersAsync/GetProviderAsync/GetProviderOrganizationOptionsAsync | Definisjons-/leverandørkonfigurasjonsregler | Leverandørorganisasjon og innstillinger | L/D; kontakt-/bestillingstekst må fjernes | Ordinær leverandørtilknytning gjenstår; credentials/transportkonfigurasjon utelatt |
| ServiceDefinitionFieldService.GetFieldAsync | Foreldredefinisjon og feltinnsyn | Feltdefinisjoner/type/valg, ikke skjemasvar | D | Gjenstår |
| ServiceCatalogService.GetAvailableServicesAsync/GetServiceAsync/GetAvailableRequestLocationsAsync | GetAccountActorScopeAsync + Navigation.TenantPortal; brukerens tillatte tjenester og plasseringer | Lokalisert tjeneste og lovlig bygg/enhet, ikke administratorens katalog | L/D | Gjenstår |
| ServiceRequestReportingReader.ListAsync/CountAsync | ServiceRequestAccess.ReadScope før filter/SQL-gruppering | service_requests_query/aggregate; tittel, lagret status, bygg/enhet, CreatedAt/SubmittedAt/CompletedAt; Search/Status/Building/MissingBuilding; All-periode | L/A; skrivefri; 8 statuser, ingen beregnet «åpen» eller historikk | Implementert i 9E; rapportadapter v3 |
| ServiceRequestService.GetMyRequestsAsync/GetAssignedRequestsAsync/GetAdminRequestsAsync | Application requester/provider/admin, scoped bygg/organisasjon | 9E gir unionen av tillatt innsyn; rolle-spesifikke visninger og andre felt er ikke alle dekket | L/A | 9E-felt implementert; egne visninger/øvrige ordinære felt gjenstår |
| ServiceRequestService.GetRequestAsync | Konkret ReadScope | Request-ID/detalj; skjemasvar, kommentarer og e-post er ikke del av 9E | D | Avgrenset detalj gjenstår; private fritekst-/e-postfelt utelatt fra generell lesekatalog |
| ServiceRequestService.GetAvailableProvidersAsync/ComposeProviderRequestAsync | Assign/leverandørbestillingsregler | Skrivevalg og e-postutkast med mottakerdata | Ingen rapport | Utelatt: skriveforberedelse/privat e-post; ingen bestilling eller utsending |

## Leasing, økonomi, møterom

| Application-inngang | Autorisasjon | Aktuelle felt/filtre, dagens MCP | Egnethet/effekter | Status |
|---|---|---|---|---|
| LeasingReader.ListAsync/GetAsync/OptionsAsync | LeasingReader.Scope/CanRead; listen har smalere legacyomfang enn detalj/report, eier/rammeeier/handlingsroller | Ingen MCP. Ramme/anskaffelse, type/status/eier/part/valuta; fjern personidentitet og rå entities | L/D/A; skrivefri | Gjenstår, ikke erstatt med generell adminrolle |
| LeasingReader.NewAcquisitionAsync | Eier-/rammestatusregler | Nytt redigeringsobjekt | Ingen lagring men skriveforberedelse | Utelatt som MCP-leseverktøy |
| LeasingEquipmentService.ListEquipmentAsync | LeasingUserAccess + konkret anskaffelses-/eieromfang | Equipment status/search/acquisition, teller ikke automatisk finansposter | L/D/A | Gjenstår |
| LeasingOrderReader.ListOrdersAsync/GetOrderAsync/AcquisitionOrdersAsync/OrderLinkCandidatesAsync/LimitChangesAsync/CanChangeLimitAsync | LeasingUserAccess + foreldre-/ordreeier og handlingsroller | Ordre, koblinger, limitthistorikk; egne valuta-/revisjonsregler | L/D/A for ordinære data | Ordre/limithistorikk gjenstår; kandidater/CanChangeLimit utelatt som skriveforberedelse |
| LeasingLifecycleService.GetLifecycleAsync | LeasingUserAccess.CanReadLifecycle + anskaffelse/foreldre | Vilkår/hendelser/replacementreferanser, siste revisjon/closure | D/L; må bevare lagret historie | Gjenstår |
| LeasingPaymentService.GetPaymentsAsync/ListPaymentPlansAsync | PaymentReader / foreldreomfang | Planversjon/status/valuta/terminer, økonomiske snapshots | L/D/A; ingen blandet valutasum | Gjenstår |
| LeasingPurchaseInvoiceService.ListInvoicesAsync/GetInvoiceAsync/PurchaseInvoicesAsync/PreviewInvoiceAsync | LeasingUserAccess faktura-/godkjennings-/foreldreomfang | Faktura, linjer, valuta/status/snapshots/preview | L/D/A; preview transaksjon/effekter må etterprøves | Gjenstår; rå OCR/dokumentinnhold og tokens utelatt |
| LeasingRentalInvoiceService.ListRentalInvoicesAsync/GetRentalInvoiceAsync | LeasingUserAccess faktura-/avstemmingsregler | Faktura, allokering, avvik, korreksjon/reversering som historikk | L/D/A | Gjenstår, ingen nye overgangsregler eller writes |
| LeasingReportService.GetLeasingReportAsync/GetLeasingDashboardAsync | LeasingUserAccess, ReadReports; ExportReports særskilt | Eksisterende rapporttype/periode/valuta/finansieringspart, eksakte definisjoner per rapport | L/A; GetLeasingDashboardAsync kaller ListLeasingFollowupsAsync med kontovis initialisering og er derfor ikke skrivefri; rapportens ReadAsync er separat | Gjenstår; dashboard krever utskilt skrivefri lesing; eksportfilformatering er adapter og publiseres ikke som binær |
| LeasingFollowupService.ListLeasingFollowupsAsync/LeasingNotificationHistoryAsync/GetLeasingNotificationSettingsAsync | LeasingUserAccess / personlig konto | Fristtype/status/to/owner; ikke bland notifications/read-markering med lesing | L/A; ListLeasingFollowupsAsync kjører initialization.RunAccountAsync før lesing og er ikke skrivefri | Forretningsoppfølging gjenstår og krever skrivefri Application-inngang; tekniske varslingsinnstillinger utelatt |
| LeasingDocumentService.DownloadAsync/ReadPaymentPlanSourceAsync; originalfakturanedlasting | Foreldre-/faktura-/dokumentautorisasjon | Metadata må skilles fra StorageKey/innhold/arktolking | D/L | Metadata gjenstår; binærinnhold/import-grid utelatt etter oppdraget |
| DimensionService.GetAsync | Leasing klassifikasjons-/registeromfang | Kostnadssted/dimensjonshierarki, ikke rå entities | L/D/A | Gjenstår |
| MeetingRoomService.ListAsync/GetAsync/GetBuildingsAsync | MeetingRoomAccess.RequireAsync/CanManage | Rom ID/navn/bygg/kapasitet/aktiv; ingen ExternalResourceEmail/calendaridentitet | L/D/A; skrivefri | Rom gjenstår; konfigurasjonens byggvelger utelatt som egen duplikatkilde |
| RoomBookingService.ListAsync/GetAsync/IsAvailableAsync | MeetingRoomAccess.RequireAsync(false): aktivt kontomedlem; ingen ny eierbegrensning i lesing | Rom, intervall UTC, status og tilgjengelighet (aktivt rom/bygg, ingen Confirmed-overlapp); Subject/Description/organisator krever særskilt metadatautvalg | L/D/A; halvåpent intervall StartUtc < to og EndUtc > from; ingen historisk utnyttelsesgrad fra dagens bookingliste | Gjenstår |
| CalendarIntegrationService.ListAsync; ICalendarProvider.GetAvailabilityAsync | Administrativ integrasjon / ubrukt teknisk providerport | Provider-/integrasjonsnavn, aldri tokens/credentials | Teknisk oppslag | Utelatt: integrasjonsadministrasjon og ekstern providertransport, ikke ordinær MCP-rapport |

## Sikkerhet, administrasjon og tekniske innganger

UserAdministrationService.GetUsersAsync/GetUserAsync/GetUserAccessAsync/GetAvailableAccountsAsync/GetAvailableOrganizationsAsync/GetAvailableBuildingsAsync utelates fra generell MCP-lesekatalog: de er bruker-/medlems-/grantadministrasjon og kan røpe person- og tilgangsdata. Dette er uttrykkelig utenfor 9F. Ordinære organisasjoner/bygg må likevel dekkes med sine egne ressurslesere.

NavigationReader/HomeReader er UI-komposisjon. Navigasjon/tilgangsflagg er ikke forretningsdata; Home-tall kan bare regnes som dekket når de underliggende autoriserte domenekontraktene faktisk er implementert. Home er derfor ikke en snarvei rundt områder merket «gjenstår».

OAuthAccessService, AuthenticationSessionService, McpRegistryService og PersonalPageService er kontrollplan/personlige definisjoner. Eksisterende personlige sideverktøy beholdes; de skal ikke åpnes som automatisk modellkatalog eller gi generelt grantinnsyn. CanRegisterRentalInvoiceAsync og tilsvarende handlingsflagg er skriveforberedelse, ikke en generell bedriftsdatakilde. Login, kontobytte, preferanser, initialization og systemjobber er writes/orkestrering og utelates. Rene datalagringsporter regnes ikke som selvstendige beskyttede brukstilfeller. Dokument-/providerporter skal aldri være transportinnganger.

## Agentgrenser og rapportområder

Checkpoint5 gjenbruker serverbundet områdevalg for tre avtaleunderutvalg: oversikt/rapport (7 lesere + eventuelt forslag), detaljer/oppfølging (6 lesere) og økonomi (5 lesere). Tjenesteforespørsler har2 lesere + eventuelt forslag. Eksterne profiler beholder sin personlig godkjente katalog. Standardgrensen12 er uendret og inkluderer forslag; ingen stille trunkering. Se [katalogutvalg og finanskontrakter](mcp-agreement-finance-read.md).

agreements_followup er en detalj-/fristkilde i chat, ikke et nytt rapportområde. Avtaler og tjenesteforespørsler beholder eksisterende typede adaptere, definisjoner, explicit preview/save og kjøring uten AI. Belegg/struktur/leasing/rom skal først bli rapportområder når deres list/aggregate-semantikk og testsammenheng er implementert. Ingen direkte DB-fallback, servicekonto eller sammenslåing av områder innføres.


## Checkpoint 1 og fortsettelse

46 operasjonsrader, 36 helt/delvis åpne rader (flere metoder per rad). Gjenstående ordinære lesinger er ikke faglig utelatt. Neste konkrete uttrekk er kontofrister fra AgreementDeadlineReader.ReadAsync uten GUI-initialisering og uten person-/owner-alternativer; deretter prioriterte avtaler/innsikt, struktur, belegg, tjenester/katalog, leasing og rom/booking. Før katalogen overstiger agentgrensen skal eksplisitt områdevalg innføres. Kontroller nye domene-scopes, samtykke og profilversjon eksplisitt; ikke gjenbruk avtalescope som generell fullmakt til nye områder.

[Første fristkontrakt, nødvendige privilegier, aktivering og eksempelspørsmål](mcp-agreement-followup-contract.md). Gjeldende testresultat og presist fortsettelsespunkt står i [moderniseringsplanens avsnitt 36](modernization-plan.md#36-steg-9f-mcp-lesedekning--checkpoint-1). Ingen opprettelse av operative roller, OAuth-klienter eller godkjenninger inngår i dette checkpointet.


<a id="checkpoint-2--gjeldende-status"></a>

## Checkpoint 2 – tidligere status

46 operasjonsrader, **33 helt/delvis åpne rader**. De tre radene for kontoavtalefrister, prioriterte avtaler og innsikt er nå håndtert med eksisterende autoriserte Application-readere og avgrensede MCP-kontrakter. Owner-dimensjonens personkatalog er konkret utelatt, ikke skjøvet over i Infrastructure eller publisert som rå persondata. [Kontrakter, filtre, telling/hierarki, privilegier og spørsmål](mcp-agreement-read-extensions.md).

Tolv serververktøy, elleve egne agentverktøy, tolv modellverktøy med forslag. Ingen grense er hevet eller katalog stille trunkert. **Neste konkrete handling er eksplisitt, servervalidert og samtalebundet områdevalg med tydelig GUI og nullstilling av historikk/utkast ved bytte**, før et nytt verktøy legges til. Deretter resterende ordinære avtaledetalj-/historikk-/økonomi-/dokumentmetadata og de åpne domenene nedenfor. Nye domenescopes, klienttillatelser, samtykke og profilbinding må implementeres uttrykkelig.

Fortsatt åpne avtaleinnganger: AgreementDetailsReader.ReadAsync (ordinære felt utover sammendrag), AgreementFollowupReader.ReadAsync (upersonlig hendelseshistorikk), AgreementManagementService.GetOptionsAsync (ordinære relasjoner), AgreementFinancialService.GetLinesAsync/ForecastAsync/PreviewBasisAsync/ListBasisAsync/GetAffectedBasesAsync/GetBasisAsync/GetBulkBasisOptionsAsync/PreviewBulkBasisAsync/ListPendingBasisApprovalsAsync, AgreementIndexService.GetIndicesAsync, AgreementAnalysisService.GetAnalysesAsync (avgrenset metadata) og dokumentmetadata fra AgreementDetailsDto. Binærinnhold, rå modell-/dokumenttekst og person-/grantkatalog er fortsatt utenfor.

Struktur/organisasjon, belegg, tjenester/katalog/forespørselsdetaljer, leasing/økonomi/metadata og rom/bookinger har fortsatt de eksakte åpne inngangene i tabellene over. Leasing-oppfølging og dashboard trenger fortsatt autorisert skrivefri Application-lesing før publisering. Rapportområder utenfor de to eksisterende må leveres med typede kilder, promptutkast/redigering, faktisk preview/eksplisitt lagring og modellfri kjøring. Hele 9F er ikke ferdig eller sluttavstemt.

Tidligere 9E-driftsfeil og manglende bekreftelse på vellykket interaktiv kjøring er fortsatt ikke løst gjennom grønne tester. Ingen operative GRANT-er, klientregistrering eller godkjenninger er kjørt. Testresultat etter siste produksjonsendring og fullstendig fortsettelsespunkt står i moderniseringsplanens avsnitt37.

<a id="checkpoint-3--gjeldende-status"></a>

## Checkpoint 3 – tidligere status

46 operasjonsrader, fortsatt **33 helt/delvis åpne rader**. Bare nødvendig områdeinfrastruktur er ferdigstilt i denne kjøringen; ingen nye datauttrekk er publisert eller dekningsrader lukket.

Eksplisitt områdevalg er implementert i Application og chatpanelet for Avtaler/Tjenesteforespørsler, med egen External-behandling basert på faktisk registerprofil. Eksakt navnetilordning av egne kontrakter, tilgjengelig/versjonsgodkjent katalog og uendret tolvgrense gjelder før modellen kalles. Samtalen binder området på serveren. Bekreftet områdebytte kansellerer gammel kjøring, sletter gammel historie/kilder/utkast og oppretter en isolert samtale på samme tilkobling. Sene svar får ikke gjeninnføre data. Lagret sides backendverifiserte rapportområde styrer AI-redigering; manuell oppretting kan fortsatt velge blant de to faktisk støttede rapportområdene. Detaljverktøy blir ikke automatisk rapportkilder. [Faktisk oppførsel, grense, eksterne profiler og manuelle kontroller](read-agent-areas.md).

**Neste konkrete uttrekk:** undersøk `AgreementDetailsReader.ReadAsync` og eksisterende `agreements_details`-sammendrag. Utvid ordinære typede detaljfelt med avgrenset transportkontrakt og oppdatert versjonsgodkjenning, uten personkatalog, rå dokument-/modellinnhold eller nye skjulte skriverier. Deretter upersonlig paginert oppfølgingshistorikk og autoriserte ordinære relasjonsoppslag fra `AgreementManagementService.GetOptionsAsync`. Avgrens dokumentmetadata, økonomi/indeks/grunnlag og resterende domener etter de eksakte åpne radene over. Bevar ferdige checkpoint1/2-readere; ikke bruk GUI-initialiseringsfasader.

Sluttavstemming mot produksjonskode og steg4-oversikten gjenstår. Leasing-oppfølging/dashboard krever fortsatt skrivefri Application-lesing. Nye domener krever egne vurderte scopes, klienttillatelser, samtykke, profilbinding og minste nødvendige SELECT-rettigheter. Ingen operative endringer er utført; 9E-driftsfeilen står fortsatt ubekreftet. Faktisk verifikasjon og presist checkpoint står i moderniseringsplanens avsnitt38.


<a id="checkpoint-4--gjeldende-status"></a>

## Checkpoint 4 – tidligere status

46 operasjonsrader, **30 helt/delvis åpne rader**. Tre rader er ferdigstilt i sitt eksplisitte ordinære leseomfang: upersonlig oppfølgingshistorikk, autoriserte relasjonsoppslag og aktuelle notice-/periodefelt. Detaljraden er fortsatt delvis åpen for avgrenset noticehistorikk; dokument-/analysemetadata har egne åpne rader. Ikke tolk de tre prioriterte uttrekkene som full dekning av GUI-DTO-en.

[Kontrakten](mcp-agreement-detail-history-relations.md) avstemmer feltene i AgreementDetailsDto, forklarer ordinær beskrivelse/vilkår, v2 og ny godkjenning for agreements_details, hendelsesmodellen uten konstruerte før-/etterverdier og relasjoner brukt av synlige avtaler. To nye verktøy (agreements_followup_history og agreements_relations) har vanlig eksplisitt områdetilordning/godkjenning. Fjorten serververktøy gir elleve avtalelesere pluss ett forslag i avtaleområdet. Standardgrensen12 er beholdt; ingen undergruppe er nødvendig ennå. Neste nye avtaleleser trenger et eksplisitt serverbundet underutvalg før katalogen vokser.

Neste konkrete uttrekk er AgreementFinancialService.GetLinesAsync/ForecastAsync med faktiske valuta-/periode-/autorisasjonsregler. Deretter indeksverdier og beregningsgrunnlag, samt avgrenset noticehistorikk og dokument-/analysemetadata. Bevar gjeldende detalj-/historikk-/relasjonsprojeksjoner. Nye data skal ikke lastes via GUI-initialisering eller en skrivepreview som antas skrivefri uten kontroll.

De øvrige åpne domeneradene og kravet om sluttavstemming mot produksjon/steg4 gjelder fortsatt. Tidligere 9E-driftsfeil og interaktiv aktivering er ubekreftet. Testresultat etter siste produksjonsendring og presist fortsettelsespunkt føres i moderniseringsplanens avsnitt39. Ingen operative GRANT-er, klientregistrering, migrering, deploy, commit eller push inngår.


<a id="checkpoint-5--gjeldende-status"></a>
## Checkpoint 5 – tidligere status

46 operasjonsrader, **29 helt/delvis åpne rader**. Bare raden GetLinesAsync/ForecastAsync er nå ferdigstilt utover checkpoint4. Indekskatalog/-verdier og alle beregningsgrunnlagsoperasjoner har fortsatt egne åpne rader; intern bruk av indeksverdier i prognose er ikke MCP-publisering av indeksregisteret.

Katalogunderutvalg er implementert i eksisterende samtalebinding, med oversikt/rapport, detaljer/oppfølging og økonomi som eksplisitte barn av avtaleområdet. Samme byttevarsel, kansellering, isolasjon og ferske registerkontroller gjelder. Backend velger oversikt ved AI-redigering av avtaleside; økonomi-/detaljutvalg får ingen rapportforslag. [Kontrakter, feltavstemming, beregningssemantikk, scopebeslutning og SELECT-behov](mcp-agreement-finance-read.md). Hele9A–9E og gamle definisjoner skal bevares; ingen finansiell rapportadapter er opprettet.

Neste konkrete produksjonsuttrekk er AgreementIndexService.GetIndicesAsync: avgrenset indeks-/verdilesing med ordinære felt, versjoner og publiseringssemantikk, kontoautorisasjon og eksplisitte nye verktøygodkjenninger. Beregningsgrunnlag følger med hver operasjons konkrete lese-/godkjenningsregler; PreviewBasis må ikke antas skrivefri uten avstemming av sin faktiske port. Deretter avgrenset noticehistorikk, analyse-/dokumentmetadata og resterende domener etter tabellene. Hele9F og sluttavstemming mot produksjon/steg4 gjenstår.

Verifikasjon etter siste produksjonsendring og presist fortsettelsespunkt står i moderniseringsplanens avsnitt40. Tidligere interaktive kontroller og9E-driftsfeil er fortsatt ubekreftet. Ingen drifts-GRANT, klientregistrering, migrering, deploy, commit eller push er utført; containerbygg er fortsatt utsatt.


<a id="checkpoint-6--gjeldende-status"></a>
## Checkpoint 6 – tidligere status

46 oversiktsrader, **28 selvstendige helt/delvis åpne operasjonsrader**. Tellingen følger checkpoint5: hjelperaden AgreementNoticeRules er ikke en selvstendig autorisert inngang; dens åpne noticehistorikk telles på detaljraden og lukkes ikke her. Indeksraden er nå lukket for dokumentert ordinært leseomfang med `agreements_indices` og `agreements_index_values` v1. Private aktør-/reason-/skrivefelt og rå Source er eksplisitt utelatt. [Kontrakt, Current/History, kilderensing, SELECT og aktivering](mcp-agreement-index-read.md). Kontoens eksisterende registerinnsyn er bevart; registeret blir ikke implisitt begrenset til synlige avtaler.

Økonomiutvalget har nå7 eksplisitt tilordnede lesere. Standardgrensen12, personlig OAuth, eksakt registergodkjenning, versjonsbundne kall, kildefingeravtrykk og fersk autorisasjon er uendret. Ingen ny rapportadapter eller rapportforslag; gamle v1/v2/v3-sider bevares.

Neste konkrete uttrekk er **AgreementFinancialService.ListBasisAsync**, fulgt av **GetBasisAsync** som sammenhengende leseflyt for lagrede grunnlag. `ReadBasisListAsync` avgrenser allerede til synlige avtaler og aktuell konto/retning, men bruker Take(500) uten eksplisitt videre side/total og returnerer Basis/Snapshot-entiteter. Lag en smal, reelt paginert kontrakt med lagret snapshotbeløp/valuta/retning/status/revisjon; undersøk referanser og metadata før transport. Ikke eksponer rå DataJson, Fingerprint, aktør/reason, dokumenter, handlingsflagg eller generering/godkjenning.

`GetBasisAsync` bruker konkret avtaleinnsyn gjennom OpenBasis/Open og beregner dessuten Stale/NeedsCorrection mot dagens kilder for ikke-kansellerte grunnlag. Lagret snapshot og ny beregning må skilles eksplisitt; en ny ren snapshotleser må ikke hevde å ha vurdert disse flaggene. Preview-/bulk-/godkjenningsrader blir stående åpne til hver faktisk inngang er håndtert. Ingen basisoperasjon er MCP-implementert i checkpoint6.

Faktisk verifikasjon og presist fortsettelsespunkt føres i moderniseringsplanens avsnitt41. Tidligere interaktive kontroller og9E-driftsfeil er fortsatt ubekreftet; hele9F og sluttavstemming mot produksjon/steg4 gjenstår. Ingen operativ aktivering, migrering, deploy, commit, push eller Serga-endring; containerbygg/8C er fortsatt utsatt.


## Checkpoint 7 – gjeldende status

46 oversiktsrader og fortsatt **28 selvstendige helt/delvis åpne operasjonsrader**. Ingen hel rad lukkes av liste/detalj: den samlede grunnlagsraden er bare delvis dekket. Hjelperaden AgreementNoticeRules viser fortsatt samme åpne noticehistorikk som detaljraden, uten en ekstra autorisert inngang.

Implementert: `agreements_bases` og `agreements_basis` v1 gjennom `AgreementBasisReader` og en konkret SQL-lesesesjon. Listen har eksisterende konto-/synlighets-/retningsregler før telling og paging, side25 og ingen skjult500-grense. Detaljen velger gjeldende eller eksakt historisk snapshotrevisjon. Lagret status/revisjon, snapshotbeløp/valuta og målsegmenter skilles fra prognose/faktura/betaling. Aktualitet er eksplisitt **NotEvaluated**, uten falske Stale/NeedsCorrection-flagg. Kildeproveniens og transitive registerkall er bevart. [Kontrakt, parsing, referanser, SELECT, aktivering og eksempler](mcp-agreement-basis-read.md). Økonomiutvalget har nå9 eksplisitte lesere, med uendret grense12 og uten rapportforslag.

Undersøkte, fortsatt upubliserte innganger:

| Metode | Faktisk leseregel/kallkjede og avgrensning før videre MCP |
|---|---|
| PreviewBasisAsync | Open(false,false): konkret avtaleinnsyn før/etter åpning; CheckBasisDirection krever lagret retning/valuta. InvoicePeriods bruker utvidet beregningsintervall ±13 måneder og velger hele hendelser etter **fakturadato**, ikke prognosens overlapp. Ingen Commit/Save/init langs denne banen; gammel port laster bredt og beregningsarbeidet trenger eksplisitte grenser. |
| GetAffectedBasesAsync | Open(false,false) på konkret avtale, alle ikke-kansellerte grunnlag, deretter GetBasisAsync per ID med ny autorisasjon. Filtrerer på faktisk Stale/NeedsCorrection. Ingen Commit/Save/init, men ubundet historikk/kildelasting og gjentatte beregninger; må få avgrenset aktualitetslesing som gjenbruker reglene. |
| GetBulkBasisOptionsAsync | GetAgreementListScopeAsync og ReadBulkOptionsAsync: synlige **Income**-avtaler med motpart i samme konto. Ingen skriverkall; hele ubundne listen er støtte for bulkhandling og er ikke ny generell motpartskatalog. |
| PreviewBulkBasisAsync | GetAccountActorScopeAsync, listescopets Income-valg og Open(false,false) per avtale med aktør-/ressursrevalidering. BulkPreview bruker InvoicePeriods, aktive claims, Stale/korreksjonsregler, teknisk Fingerprint og CanEdit for Ready/Followup. Ingen Commit/Save/init; SQL-/beregningsgrenser, handlings-/metadataavgrensning og sikker feilbehandling kreves. Feil kan logges gjennom ReportFailure; dette er observabilitet, ikke generering. |
| ListPendingBasisApprovalsAsync | Listescope og synlige Draft-kandidater, konkret GetBasisAsync per kandidat, eventuelt historiske/originale snapshots som periodeanker. CanApprove, arkiv og Stale avgjør handlingsflagget, ikke grunnleggende innsyn. Ingen godkjenning/Commit/Save/init; feilbanen bruker i dag GUI-fallback med beløp0/feilnøkkel og kan logge gjennom ReportFailure. Denne fallbacken skal ikke publiseres som et gyldig finansresultat. |

Presist neste uttrekk: **AgreementFinancialService.PreviewBasisAsync**. Skill eksplisitt nytt beregnet forslag fra den nå ferdige lagrede lesingen. Gjenbruk InvoicePeriods/Source-reglene i Application med en smal, READ ONLY-kildeport og grenser for hele mellomarbeidet inklusive ±13-månedersperioden. Bevar konkret autorisasjon/retning/valuta, fakturadato-utvalg og hele logiske hendelser; ikke kall en GUI-fasade eller gi brede dokument-/aktørprivilegier. Vurder egen tydelig beregnings-/katalogkontrakt før nytt verktøy. Deretter berørte grunnlag/aktualitetsregler og de øvrige radene over. Ingen av disse fem inngangene er MCP-implementert i checkpoint7.

Faktisk verifikasjon og checkpoint føres i moderniseringsplanens avsnitt42. Tidligere uavklarte interaktive kontroller og9E-driftsfeil er fortsatt ubekreftet. Ingen operative GRANT-er, klientregistrering, migrering, deploy, commit, push eller Serga-endring; containerbygg/8C er fortsatt utsatt. Hele9F og sluttavstemming gjenstår.
