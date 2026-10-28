# Operasjonsoversikt for utvidet MCP-lesedekning – steg 10A

Baseline fastsatt 26. oktober 2026 mot arbeidskopien etter tidligere 9F checkpoint 7. **Gjeldende status etter første 10B-uttrekk: STR-001 er implementert og automatisert verifisert**, med buildings_query/aggregate og eksplisitt strukturutvalg. Ingen rapportadapter eller STR-002 er levert. [Faktisk verifikasjon og neste byggrapportuttrekk](modernization-plan.md#44-steg-10b-str-001--byggliste-og-byggaggregat), [kontrakt/aktivering](mcp-building-read-contracts.md). 10As opprinnelige rekkefølge beholdes; PreviewBasisAsync ligger fortsatt under10F.

## Telling, sporbarhet og avgrensning

C01–C46 betegner de gamle tabellradene i [dekningsoversikten](mcp-read-coverage.md), i opprinnelig rekkefølge. Krysstabellen nedenfor gjengir hver inngang og knytter den til stabile operasjons-ID-er. C15 er en ren regelhjelper og peker til samme noticehistorikk som C02: ingen ekstra operasjon. Én operasjon er et meningsfullt leseformål/variant, ikke én intern SQL-metode eller nødvendigvis ett MCP-verktøy. Samme metode kan ha ulike selvstendige varianter (f.eks. seks leasingrapporttyper). Metoder som dekker samme formål vises på samme rad; dokumentmetadata har en eksplisitt sjekkliste over foreldrevarianter og kan ikke lukkes etter bare én av dem.

F = implementert **bare innen angitt ordinært transportutvalg**, D = delvis, I = ikke MCP-implementert, U = uavklart og må undersøkes, X = konkret begrunnet utelatt. U er ikke ferdig. At eksisterende Application er migrert/testet i steg 4 gir ikke F for MCP. Vanlig restarbeid er ikke en ekstern blokkering. Den eksplisitte restkolonnen og delstegets felles akseptansekriterier gjelder begge for lukking.

De gamle **28 selvstendige åpne/delvise samleradene** består: C02, C10, C13–14, C16–17, C19–20, C22, C24–27, C29–30, C32, C34–45. C15 er referansehjelper, ikke en 29. rest. De øvrige gamle radene beholder ferdigstatus eller begrunnet utelatelse. Ingen samlerad lukkes av denne omorganiseringen.

N01–N03 er nye funn utenfor den opprinnelige 46-raders baseline og er merket Nytt i tabellen. Senere funn får neste N-ID og egne tilleggstall; gamle ID-er gjenbrukes ikke. Det er ikke beregnet prosent fordi et registeroppslag og økonomisk avstemming har svært ulik kompleksitet.

## Primær fremdriftsmåling


| Delsteg/domene | Implementert | Delvis | Ikke implementert | Uavklart | Utelatt | Sum |
|---|---:|---:|---:|---:|---:|---:|
| 10B | 2 | 0 | 7 | 0 | 4 | 13 |
| 10C | 0 | 0 | 2 | 0 | 1 | 3 |
| 10D | 2 | 3 | 10 | 0 | 2 | 17 |
| 10E | 0 | 0 | 5 | 0 | 3 | 8 |
| 10F | 16 | 2 | 8 | 0 | 5 | 31 |
| 10G | 0 | 0 | 38 | 0 | 9 | 47 |
| **Totalt inkl. nye funn** | 20 | 5 | 70 | 0 | 24 | 119 |

Fast oppsplittet C-baseline: **116 operasjoner** (nå 20 implementert, 5 delvis, 68 ikke implementert, 0 uavklart, 23 utelatt). Tillegg N01–N03: **3 operasjoner** (2 ikke implementert, 1 utelatt). Alle tillegg er i 10G. Totalt119. 10A endret bare dokumentasjon; første10B har siden lukket én eksisterende operasjon uten å endre baselineantallet. Alle28 gamle restrader består fordi C16 fortsatt mangler detaljlesing.

## Autorisasjon, effekter og felles ferdigkriterium

Alle konto-/ressursreferanser er forslag; Application henter betrodd aktør og ferske fakta, også ved retry. Global PlatformAdmin er ikke kontofullmakt. Nye kontrakter går via personlig OAuth, eksplisitt scope/klienttillatelse/samtykke, profilbinding, oppdagelse/klassifisering og eksakt registergodkjenning. Ingen automatisk godkjenning eller bred SELECT. SQL avgrenser før paging/telling/gruppering; Infrastructure bestemmer ikke en parallell rettighetsmodell. Én samtale/side beholder konto, bruker, område og én tilkobling. Tolvgrensen etter områdevalg inkluderer forslag; ingen stille trunkering.

Autorisasjonskoder nedenfor viser faktiske Application-regler, ikke nye MCP-scopes:

| Kode | Application-regel og kjente effekter |
|---|---|
| B-L / B-D | Buildings.Open på konto / Buildings.Read på konkret bygg. PropertyAdmins brede legacylistelesing gir ikke generell detaljfullmakt. Begge leser uten Commit. |
| U-L / U-D | Units.Open på konto / Units.Read på konkret enhet. Samme listeskille; byggfilter er ikke autorisasjon. Ingen Commit. |
| O-L / O-D | GetOrganizationListAccessAsync krever aktivt medlemskap og Organizations.Read med PropertyAdmin eller kontovid AccountAdmin; konkret Organizations.Read har eksplisitt bred PropertyAdmin-ressursregel. Ingen Commit. |
| H | AccountStructure.Manage med betrodd valgt konto; hierarkilesing er ikke en ny vanlig medlemsrett. Ingen Save. |
| C-L / C-D | Occupancies.Open med bred administrativ legacylistelesing / Occupancies.Read på ressurs. Today er vertens lokale dato fra TimeProvider, ikke avtaletidssonen. Ingen Commit. |
| S-L / S-D / S-F | ServiceDefinitions.Open / Read på konkret definisjon / Read på feltets foreldredefinisjon i GetFieldAsync. Felt-ID må tilhøre denne definisjonen. Ingen Commit i lesemetodene. |
| S-C | Aktivt kontosubjekt og Navigation.TenantPortal, bestillbar katalog. Locations bruker egne leietakertildelinger og dagens lokale dato; legacy AccountAdmin/PropertyAdmin-selector gir ikke request-create. Ingen Commit. |
| S-R | ServiceRequestAccess.ReadScope; bestiller/leietaker/tildelt leverandør/admin og bygg-/organisasjonsomfang. 9E bruker autorisert union; rollevisninger er ikke automatisk identiske. Ingen Save/init. |
| R | MeetingRoomAccess.RequireAsync(false): aktivt kontomedlem; romprojeksjon avhenger også av CanManage. Bookinglesing har ingen generell eierbegrensning. Halvåpen UTC-overlapp; ingen Save ved lesing. |
| A-L / A-D | GetAgreementListScopeAsync / konkret GetAgreementReadScopeAsync, eier/admin/grants og kontomedlemskap; Read er ikke Edit. Readerne er skrivefrie, GUI-initialisering er separat. |
| L-L / L-D / L-O | LeasingReader.Scope, CanRead og LeasingUserAccess. ListAsync snevrer inn til admin/eiere og limitgodkjenner for rammer; detalj kan tillate flere rapport-/handlingsroller. Options har særskilt OptionsReader/eid root. Ordre bruker egen ordre-/forelder-/eierregel. Lesing uten Commit. |
| L-limit / L-life / L-pay | LimitChanges: rammeeier/admin/ApproveLimit. Lifecycle: CanReadLifecycle eller relevante eiere. Betaling: PaymentReader/foreldreomfang og gjeldende handlingsroller; innsyn er ikke write-fullmakt. Ingen Commit i ordinære lesere. |
| L-inv / L-inv/editable | Faktura-/anskaffelses-/ramme-/ordreeier og LeasingUserAccess. Preview krever Editable og revisjon i eksisterende arbeidssesjon, ikke bare listetilgang. Ingen Commit i preview-metoden; hele portbanen skal bevises skrivefri før MCP. |
| L-report / L-export | Eksisterende ReportScope med lokale rapport-/eierregler; ExportReports kontrolleres særskilt når export=true. Dashboard og followuplisten initialiserer i dag konto og kan ikke kalles som skrivefri MCP-lesing. |
| L-E / L-class / L-doc | Utstyr: CanReadLifecycle/anskaffelses-/rammeeier. Klassifikasjon: CanReadLifecycle/CanReadOrders eller eid root. Dokument: konkret kontodokument og autorisert forelder/faktura; strøm og StorageKey utelates. |

**Kriterium K, per I/D-rad:** Resten i raden er levert med smal versjonsbundet DTO, dokumentert felt-/filter-/null-/dato-/tellesemantikk, eksakt tilgangsavgrensning og fullstendighets-/overflowstrategi. Direkte Application-avvisnings-/tilbakekallingsprøver, faktisk OAuth/MCP og isolert PostgreSQL viser liste/aggregatsamsvar, fremmede referanser, minste SELECT og skrivevern uten initialisering. Alle nødvendige adaptere/agentområder, kontrakter og regresjoner er oppdatert. Ingen GUI-fasade/DB-fallback, rå entiteter, credentials eller skjulte writes. En X-rad krever bevart begrunnelse og fravær av uønsket transporteksponering; F-rader krever bare bevaring/regresjon ved endring. Operativ aktivering registreres separat og kan ikke utledes av grønne tester.

## Verifikasjonsreferanser

E10B: [STR-001 full verifikasjon](modernization-plan.md#442-verifikasjon-og-aktivering) og [målrettet kontrakt](mcp-building-read-contracts.md#verifikasjon-og-manuell-kontroll). Dette er en ny kontroll etter10A; referansene nedenfor er den tidligere baselinen.

Dette er tidligere dokumentert verifikasjon, **ikke nye kjøringer i 10A**. E0: [medlemskontovalg/OAuth/MCP fra 5B2](modernization-plan.md#23-steg-5b2-separat-autentisert-mcp-lesehost) og senere MCP-regresjoner. E8D: [8D-kontraktene og verifikasjonen](modernization-plan.md#30-steg-8d-avtaleutvalg-dimensjoner-aggregater-og-detaljlesing). E9D: [9D og senere regresjoner](modernization-plan.md#34-steg-9d-fleksible-rapportkomponenter-og-tabellvisning). E9E: [9E](modernization-plan.md#35-steg-9e-tjenesteforespørsler-som-nytt-rapportområde) og [kontrakt](mcp-service-request-read-contracts.md). E1/E2/E4/E5/E6/E7: historiske checkpoint 1/2/4/5/6/7 i moderniseringsplanens avsnitt 36/37/39/40/41/42. E7 inkluderer full MCP/MCPClient-, AgreementPeriods-/AgreementProcessing-PostgreSQL og Node-kontroller etter siste produksjonsendring. I/D uten E har ingen påstått ny MCP-verifikasjon; [steg 4s sluttkontroll](step-4-migration-inventory.md#1935-steg-4-implementert-og-sluttkontrollert) gjelder Application-migreringen, ikke MCP-dekning.

Kilder kontrollert i kode: [Application](../../src/TenantPlatform.Application), [MCPs manuelle katalog/dispatch](../../src/TenantPlatform.Mcp/ReadTools.cs), [eksakt områdetilordning](../../src/TenantPlatform.Application/Agent/ReadAgentAreas.cs), [rapportområder](../../src/TenantPlatform.Application/PersonalPages/ReportAreas.cs), [steg 4-inventar](step-4-migration-inventory.md). Se også kontraktene for [detalj/historikk/relasjoner](mcp-agreement-detail-history-relations.md), [økonomi](mcp-agreement-finance-read.md), [indeks](mcp-agreement-index-read.md) og [grunnlag](mcp-agreement-basis-read.md).


## 10B – Struktur og organisasjon

K gjelder hver rad sammen med konkret rest nedenfor. Status gjelder MCP, ikke drift eller automatisk rapportstøtte.

| ID | Gammel rad | Application-metode / variant | Behov og avgrenset leseomfang | Tilgang/effekt | MCP-verktøy | Status | Verifikasjon | Konkret rest → ferdigkriterium (og K) |
|---|---|---|---|---|---|---|---|---|
| STR-001 | C16 | `BuildingReportingReader.ListAsync/CountAsync` (samme Open-regel som BuildingService.GetBuildingsAsync) | Byggliste: ID/navn/adresse/postnummer/by og unik SQL-total/City | B-L; ferskt revalidert før/etter SQL | buildings_query / buildings_aggregate v1 | Implementert | E10B | Bevar side25/Name/Id, eksakte City-grupper og skrivevern. Byggrapportadapter følger separat; STR-002 er fortsatt I. |
| STR-002 | C16 | `BuildingService.GetBuildingAsync` | Konkret bygg: ordinære felt; relaterte enheter og tellefelt vurderes særskilt | B-D | — | Ikke implementert | — | Smal detalj; avstem UnitCount/TenantCount/OpenRequestCount og enhetsinnsyn før eksponering. |
| STR-003 | C17 | `UnitService.GetUnitsAsync` | Enhetsliste, byggfilter, type/aktiv/parent | U-L | — | Ikke implementert | — | SQL-side og autorisert type-/byggaggregat; hierarchy IDs uten skjult datautvidelse. |
| STR-004 | C17 | `UnitService.GetUnitAsync` | Enhet med kontobygg/parent, ChildUnitCount/OccupancyCount | U-D | — | Ikke implementert | — | Detaljprojeksjon; egne tillatelser og betydning for relasjonstellinger. |
| STR-005 | C18 | `UnitService.GetParentCandidatesAsync` | Parentvalg i skriveeditor | Units.Create på bygg | — | Utelatt | — | Skriveforberedelse; ordinært parenthierarki dekkes av enhetslesing, ingen generell kandidatkatalog. |
| STR-006 | C19 | `OrganizationService.GetOrganizationsAsync` | Organisasjoner: navn/nummer/type/aktiv | O-L | — | Ikke implementert | — | Side/filter og Type-aggregat; bevar eksplisitt bred PropertyAdmin-regel. |
| STR-007 | C19 | `OrganizationService.GetOrganizationAsync` | Konkret organisasjon og eksisterende OccupancyCount | O-D | — | Ikke implementert | — | Smal detalj, ingen kontaktpersonkatalog; dokumenter tellefeltets omfang. |
| STR-008 | C20 | `AccountRegisterService.ReadOrganizationElementsAsync` | Organisasjonsenheter: ID/navn/parent/aktiv/beskrivelse | H | — | Ikke implementert | — | Paginerte noder og eksplisitt parent/path-semantikk; ingen rå entiteter eller endret Manage-regel. |
| STR-009 | C20 | `AccountRegisterService.ReadGeographicAreasAsync` | Geografiske noder: ID/navn/parent/aktiv/beskrivelse | H | — | Ikke implementert | — | Samme avgrensede hierarkikontrakt; grensefeil fremfor stille tapt undertre. |
| STR-010 | C21 | `AccountService.GetAccountsAsync` | Plattformens kontoadministrasjonsliste | Platform.OpenAccounts/PlatformAdmin | — | Utelatt | — | Administrativ plattformkatalog, ikke medlemsbasert MCP-forretningsinnsyn. |
| STR-011 | C21 | `AccountService.GetAccountAsync` | Plattformadministrasjonens kontodetalj | Platform.ReadAccount/PlatformAdmin | — | Utelatt | — | Administrative kontofelt holdes utenfor; ikke utvid accounts_list. |
| STR-012 | C21 | `AccountContextReader.ReadAsync` | Innlogget brukers egne MemberAccount ID/navn | Aktiv betrodd bruker; ferskt medlemskap | accounts_list | Implementert | E0 | Bevar angitt kontrakt; regresjon ved endring. |
| STR-013 | C21 | `AccountContextReader.ReadSelectedNameAsync` | Navnet på valgt GUI-konto | Betrodd valgt konto | — | Utelatt | — | UI-komposisjon; egne kontonavn finnes i accounts_list. Ingen ekstra MCP-verktøy nødvendig. |

## 10C – Belegg

K gjelder hver rad sammen med konkret rest nedenfor. Status gjelder MCP, ikke drift eller automatisk rapportstøtte.

| ID | Gammel rad | Application-metode / variant | Behov og avgrenset leseomfang | Tilgang/effekt | MCP-verktøy | Status | Verifikasjon | Konkret rest → ferdigkriterium (og K) |
|---|---|---|---|---|---|---|---|---|
| OCC-001 | C22 | `OccupancyService.GetOccupanciesAsync` | Beleggsperioder; bygg/leietakerorganisasjon/enhet, ValidFrom/To | C-L | — | Ikke implementert | — | Felles SQL-filter for liste/count; tell beleggsrader, ikke antatt ledighet eller unike personer. |
| OCC-002 | C22 | `OccupancyService.GetOccupancyAsync` | Én lagret beleggsperiode med kontorelasjoner | C-D | — | Ikke implementert | — | Smal detalj, dato/null og fremmede relasjoner; ingen personmetadata. |
| OCC-003 | C23 | `OccupancyService.GetHierarchyConflictsAsync` | Foreldre-/barnkonflikter ved redigering | Konkret enhet/periode | — | Utelatt | — | Skrivevalidering; ikke publiser som generell katalog over hvem som bor hvor. |

## 10D – Tjenester og tjenesteforespørsler

K gjelder hver rad sammen med konkret rest nedenfor. Status gjelder MCP, ikke drift eller automatisk rapportstøtte.

| ID | Gammel rad | Application-metode / variant | Behov og avgrenset leseomfang | Tilgang/effekt | MCP-verktøy | Status | Verifikasjon | Konkret rest → ferdigkriterium (og K) |
|---|---|---|---|---|---|---|---|---|
| SRV-001 | C24 | `ServiceDefinitionService.GetServiceDefinitionsAsync` | Definisjonsliste: kode/lokalisert navn/kategori/aktiv | S-L | — | Ikke implementert | — | Side, filter og Category-aggregat; språkfallback beholdes. |
| SRV-002 | C24 | `ServiceDefinitionService.GetServiceDefinitionAsync` | Definisjonsdetalj inkl. felt og ordinær leverandørkobling | S-D | — | Ikke implementert | — | Projiser listefelt, beskrivelse og regler; fjern bestillingsadresse/integrasjonsfelt. |
| SRV-003 | C25 | `ServiceDefinitionService.GetProvidersAsync` | Leverandørkoblinger per definisjon: organisasjon/aktiv/default | S-D | — | Ikke implementert | — | Kontobegrensede navn, paging; ingen RequestEmailAddress eller transport. |
| SRV-004 | C25 | `ServiceDefinitionService.GetProviderAsync` | Konkret leverandørkobling per definisjon | S-D | — | Ikke implementert | — | Samme ordinære feltutvalg, avvis fremmed provider/forelder. |
| SRV-005 | C25 | `ServiceDefinitionService.GetProviderOrganizationOptionsAsync` | Aktive tilgjengelige leverandørorganisasjoner | S-L | — | Ikke implementert | — | Avgrens oppslaget med søk/paging; bevar eget Open-omfang, ikke bruk det som request-innsyn. |
| SRV-006 | C26 | `ServiceDefinitionFieldService.GetFieldAsync` | Feltdefinisjon/type/valg/lokaliserte etiketter | S-F | — | Ikke implementert | — | Smalt skjema uten skjemasvar; den faktiske metoden krever Read på definisjonen. |
| SRV-007 | C27 | `ServiceCatalogService.GetAvailableServicesAsync` | Innringers aktive bestillbare tjenester | S-C | — | Ikke implementert | — | Separat katalogkontrakt fra administratorlisten; side/språk. |
| SRV-008 | C27 | `ServiceCatalogService.GetServiceAsync` | Bestillbar tjeneste inkl. feltdefinisjoner | S-C | — | Ikke implementert | — | Konkret tilgjengelig tjeneste, ingen brukerens skjemasvar. |
| SRV-009 | C27 | `ServiceCatalogService.GetAvailableRequestLocationsAsync` | Lovlige bygg/enheter for innringer | S-C | — | Ikke implementert | — | Søk/paging og datoreferanse; legacy adminselector gir ikke bestillingsfullmakt. |
| SRV-010 | C28 | `ServiceRequestReportingReader.ListAsync` | 9E-liste med Search/Status/Building/MissingBuilding og faktiske datoer | S-R | service_requests_query | Implementert | E9E | Bevar angitt kontrakt; regresjon ved endring. |
| SRV-011 | C28 | `ServiceRequestReportingReader.CountAsync` | 9E-unik total og Status/Building-grupper | S-R | service_requests_aggregate | Implementert | E9E | Bevar angitt kontrakt; regresjon ved endring. |
| SRV-012 | C29 | `ServiceRequestService.GetMyRequestsAsync` | Bestillers egen visning; ordinære listefelt | S-R | service_requests_query (autorisert union, ikke disse eksakte visningene) | Delvis | E9E | 9E-unionen er ferdig; implementer eksplisitt visningsfilter og avstem øvrige ordinære felt før lukking. |
| SRV-013 | C29 | `ServiceRequestService.GetAssignedRequestsAsync` | Tildelt leverandørs visning; ordinære listefelt | S-R | service_requests_query (autorisert union, ikke disse eksakte visningene) | Delvis | E9E | 9E-unionen er ferdig; implementer eksplisitt visningsfilter og avstem øvrige ordinære felt før lukking. |
| SRV-014 | C29 | `ServiceRequestService.GetAdminRequestsAsync` | Administrativ visning med eget filter; ordinære listefelt | S-R | service_requests_query (autorisert union, ikke disse eksakte visningene) | Delvis | E9E | 9E-unionen er ferdig; implementer eksplisitt visningsfilter og avstem øvrige ordinære felt før lukking. |
| SRV-015 | C30 | `ServiceRequestService.GetRequestAsync` | Én forespørsel: status/tjeneste/lokasjon/faktiske tidspunkter | S-R | — | Ikke implementert | — | Egen detaljkontrakt; private skjemasvar/kommentarer/e-post/personer utelates eksplisitt. |
| SRV-016 | C31 | `ServiceRequestService.GetAvailableProvidersAsync` | Tildelingsvalg | Assign/konkret request | — | Utelatt | — | Skriveforberedelse med eget ressursomfang; providerlesing dekkes separat uten tildelingsfullmakt. |
| SRV-017 | C31 | `ServiceRequestService.ComposeProviderRequestAsync` | E-postutkast til leverandør | Bestillingsregel på request | — | Utelatt | — | Privat mottaker-/bestillingstekst; ingen utsending eller generell MCP-lesing. |

## 10E – Møterom og booking

K gjelder hver rad sammen med konkret rest nedenfor. Status gjelder MCP, ikke drift eller automatisk rapportstøtte.

| ID | Gammel rad | Application-metode / variant | Behov og avgrenset leseomfang | Tilgang/effekt | MCP-verktøy | Status | Verifikasjon | Konkret rest → ferdigkriterium (og K) |
|---|---|---|---|---|---|---|---|---|
| ROM-001 | C44 | `MeetingRoomService.ListAsync` | Romliste, includeInactive, kapasitet/bygg/aktiv | R | — | Ikke implementert | — | Paging og bygg-/kapasitetsfiltre; bevar CanManage-basert synlighet. |
| ROM-002 | C44 | `MeetingRoomService.GetAsync` | Romdetalj uten ekstern kalenderidentitet | R | — | Ikke implementert | — | Konkret kontorom, fjern ExternalResourceEmail/integrasjonsdata. |
| ROM-003 | C44 | `MeetingRoomService.GetBuildingsAsync` | Konfigurasjonens byggvelger | MeetingRooms.Manage | — | Utelatt | — | Skriveeditorens duplikatvelger; full bygglesing under 10B med sine egne regler. |
| ROM-004 | C45 | `RoomBookingService.ListAsync` | Bookinger på kontorom i UTC-intervall | R | — | Ikke implementert | — | Begrens intervall/paging; halvåpen overlapp, lagret status; Subject/Description/organisator utelates. |
| ROM-005 | C45 | `RoomBookingService.GetAsync` | Én booking: rom/tid/status | R | — | Ikke implementert | — | Ingen ny eierbegrensning; ingen person/fri tekst i generell MCP-detalj. |
| ROM-006 | C45 | `RoomBookingService.IsAvailableAsync` | Tilgjengelighet i ett intervall | R | — | Ikke implementert | — | Gjenbruk aktivt rom/bygg og Confirmed-overlapp; ingen reservasjon eller historisk utnyttelsesgrad. |
| ROM-007 | C46 | `CalendarIntegrationService.ListAsync` | Integrasjonsadministrasjon | MeetingRooms.Manage | — | Utelatt | — | Teknisk konfigurasjon med identiteter; ikke ordinær rapportkilde. |
| ROM-008 | C46 | `ICalendarProvider.GetAvailabilityAsync` | Ekstern providertransport | Teknisk adapter, ikke autorisert brukerinngang | — | Utelatt | — | Ikke en selvstendig MCP-inngang; ordinær lokal tilgjengelighet dekkes av RoomBookingService. |

## 10F – Avtaler

K gjelder hver rad sammen med konkret rest nedenfor. Status gjelder MCP, ikke drift eller automatisk rapportstøtte.

| ID | Gammel rad | Application-metode / variant | Behov og avgrenset leseomfang | Tilgang/effekt | MCP-verktøy | Status | Verifikasjon | Konkret rest → ferdigkriterium (og K) |
|---|---|---|---|---|---|---|---|---|
| AGR-001 | C01 | `AgreementListService.ListAsync` | Dokumentert avtaleutvalg med listefiltre | A-L | agreements_list | Implementert | E8D | Bevar angitt kontrakt; regresjon ved endring. |
| AGR-002 | C01 | `AgreementReportingReader.ListAsync` | Query med dato/arkiv/relasjonsfiltre og paging | A-L | agreements_query | Implementert | E9D | Bevar angitt kontrakt; regresjon ved endring. |
| AGR-003 | C01 | `AgreementReportingReader.CountAsync` | Komplett SQL-total og Building/Status-grupper | A-L | agreements_aggregate | Implementert | E9D | Bevar angitt kontrakt; regresjon ved endring. |
| AGR-004 | C02 | `AgreementDetailsReader.ReadSummaryAsync` | Autoriserte sammendragsfelt | A-D | agreements_details v2 (sammendrag) | Implementert | E4 | Bevar angitt kontrakt; regresjon ved endring. |
| AGR-005 | C02 | `AgreementDetailsReader.ReadAsync / AgreementReadExtensions.DetailsAsync` | Ordinære aktuelle felt og notice-snapshots før/etter | A-D | agreements_details v2 | Delvis | E4 | Aktuelle felt ferdige; upersonlig paginert NoticeHistory gjenstår. Dokument/analysedata følger C13/C14. |
| AGR-006 | C03 | `AgreementAttentionService.GetAttentionAsync` | Separate overlappende Notice/Renewal/Expired-kategorier | A-L | agreements_attention | Implementert | E8D | Bevar angitt kontrakt; regresjon ved endring. |
| AGR-007 | C04 | `AgreementDashboardService.GetDashboardContractsAsync` | Eksisterende prioritert femutvalg, ikke full liste | A-L | agreements_prioritized | Implementert | E2 | Bevar angitt kontrakt; regresjon ved endring. |
| AGR-008 | C05 | `AgreementInsightsService.GetInsightsAsync (Type/Status/Counterparty/Geography/Organization)` | Fem tillatte dimensjoner, Direct/selected-hierarki | A-L | agreements_insights | Implementert | E2 | Bevar angitt kontrakt; regresjon ved endring. |
| AGR-009 | C05 | `AgreementInsightsService.GetInsightsAsync (Owner)` | Eierdimensjon | A-L | — | Utelatt | — | Person-ID/navn ville gi personkatalog; ingen rettighetsendring i GUI. |
| AGR-010 | C06 | `AgreementFollowupReader.ReadAsync / AgreementFollowupPageReader.ReadAsync` | Lagrede frister, alle tilstander; ingen initialisering | A-D | agreements_followup | Implementert | E1 | Bevar angitt kontrakt; regresjon ved endring. |
| AGR-011 | C06 | `AgreementReadExtensions.HistoryAsync` | Paginert upersonlig lagret oppfølgingshistorikk | A-D | agreements_followup_history | Implementert | E4 | Bevar angitt kontrakt; regresjon ved endring. |
| AGR-012 | C06 | `AgreementFollowupReader.ReadAsync (person/kø/preferanser)` | Ansvarlig/kandidater/kommentarer/levering | A-D + egne metadataregler | — | Utelatt | — | Personkatalog, kø/transport, fritekst og teknisk konfigurasjon holdes utenfor generell katalog. |
| AGR-013 | C07 | `AgreementDeadlineReader.ReadAsync` | Kontofrister med Mine/tilstand/status/dato og egne kategorier | A-L | agreements_deadlines | Implementert | E2 | Bevar angitt kontrakt; regresjon ved endring. |
| AGR-014 | C08 | `AgreementReadExtensions.RelationsAsync (avledet fra AgreementManagementService.GetOptionsAsync)` | Relasjoner faktisk brukt i synlige avtaler | A-L | agreements_relations | Implementert | E4 | Bevar angitt kontrakt; regresjon ved endring. |
| AGR-015 | C08 | `AgreementManagementService.GetOptionsAsync (full skriveeditor)` | Ubrukte admin-/person-/delingsvalg | Create/Edit, ikke ordinært read | — | Utelatt | — | Publiser ikke editorens fulle katalog. Ordinære synlige relasjoner er dekket uten ekstra rettigheter. |
| AGR-016 | C09 | `AgreementFinancialService.GetLinesAsync / AgreementFinanceReader` | Linjer med separate versjons-/pris-sider | A-D | agreements_financial_lines | Implementert | E5 | Bevar angitt kontrakt; regresjon ved endring. |
| AGR-017 | C09 | `AgreementFinancialService.ForecastAsync / AgreementFinanceReader` | Prognose innen begrenset periode, retning/valuta | A-D | agreements_forecast | Implementert | E5 | Bevar angitt kontrakt; regresjon ved endring. |
| AGR-018 | C10 | `AgreementFinancialService.ListBasisAsync / AgreementBasisReader.ListAsync` | Lagrede grunnlag per retning/status/fakturadato | A-L | agreements_bases | Implementert | E7 | Bevar SQL-side25, reell total og Freshness=NotEvaluated. |
| AGR-019 | C10 | `AgreementFinancialService.GetBasisAsync / AgreementBasisReader.DetailAsync` | Gjeldende/eksakt historisk snapshot, separat faktisk aktualitetsvurdering | A-D | agreements_basis | Delvis | E7 | Lagret snapshot ferdig; Stale/NeedsCorrection mot dagens kilder er ikke vurdert. Lag avgrenset vurdering uten rå snapshots/feilbeløp0. |
| AGR-020 | C10 | `AgreementFinancialService.PreviewBasisAsync` | Nytt beregnet forslag etter fakturadato, ikke lagret grunnlag | A-D | — | Ikke implementert | — | READ ONLY-kilder; avgrens hele ±13-måneders mellomarbeid, retning/valuta og hele hendelser. |
| AGR-021 | C10 | `AgreementFinancialService.GetAffectedBasesAsync` | Berørte ikke-kansellerte grunnlag med faktisk Stale/NeedsCorrection | A-D | — | Ikke implementert | — | Batch/paging og gjenbruk aktuellhetsregler; ikke ubundet GetBasis per ID. |
| AGR-022 | C10 | `AgreementFinancialService.GetBulkBasisOptionsAsync` | Synlige Income-avtaler med kontomotpart | A-L | — | Ikke implementert | — | Avgrens ordinært bulk-leseutvalg; ingen person/handlingskatalog eller ubundet liste. |
| AGR-023 | C10 | `AgreementFinancialService.PreviewBulkBasisAsync` | Beregnet fleravtaleutvalg med claims/korreksjonsregler | A-L + konkret A-D per avtale | — | Ikke implementert | — | Input-/arbeidsgrenser; fjern Fingerprint/handlingsflagg; eksplisitt ufullstendig/feil, ikke falskt finansresultat. |
| AGR-024 | C10 | `AgreementFinancialService.ListPendingBasisApprovalsAsync` | Synlige Draft-kandidater og beregningsgrunnlag for vurdering | A-L + A-D; CanApprove særskilt | — | Ikke implementert | — | Side/kildegrenser; grunninnsyn gir ikke godkjenning; GUI-feilfallback0 må aldri bli gyldig beløp. |
| AGR-025 | C11 | `AgreementIndexService.GetIndicesAsync (register)` | Kode/navn/beskrivelse/renset kilde/oppløsning | A-L | agreements_indices | Implementert | E6 | Bevar angitt kontrakt; regresjon ved endring. |
| AGR-026 | C11 | `AgreementIndexService.GetIndicesAsync (Current/History-verdier via AgreementIndexReader)` | Publiserte perioder og revisjoner, ikke konstruert serie | A-L | agreements_index_values | Implementert | E6 | Bevar angitt kontrakt; regresjon ved endring. |
| AGR-027 | C12 | `AgreementFollowupWriter.GetReminderSettingsAsync` | Kontotekniske preferanser | Innstillingsautorisasjon | — | Utelatt | — | Teknisk administrasjon, ingen ordinær rapportkilde. |
| AGR-028 | C13 | `AgreementAnalysisService.GetAnalysesAsync` | Godkjent analysemetadata knyttet til avtalen | A-D | — | Ikke implementert | — | Paginert ID/status/dato, aldri modelltekst, rå dokument, tokens/aktør; eksisterende entitetsretur er ikke DTO. |
| AGR-029 | C13 | `AgreementAnalysisService.FindAnalysisOrganizationsAsync` | Eksakt navn-/nummermatch for autorisert utkast | Utkast: konkret CanEdit, ellers kontovid Agreements.Create; gyldig utkast | — | Ikke implementert | — | Bevar særskilt utkastautorisasjon, smalt kontoorganisasjonsoppslag; Open må ikke forveksles med GUI OpenAsync-write. |
| AGR-030 | C14 | `AgreementDetailsReader.ReadAsync (Documents-metadata)` | Dokument-ID/navn/media/størrelse/kategori/dato | A-D | — | Ikke implementert | — | Egen paginert projeksjon, ikke skjult i bred DTO; ingen StorageKey/opplaster/person. |
| AGR-031 | C14 | `AgreementDocumentService.DownloadAsync` | Original binærnedlasting | A-D + kontodokument | — | Utelatt | — | Binærinnhold er utenfor MCP-lesekatalog; metadata beholdes som reelt restarbeid. |

## 10G – Leasing

K gjelder hver rad sammen med konkret rest nedenfor. Status gjelder MCP, ikke drift eller automatisk rapportstøtte.

| ID | Gammel rad | Application-metode / variant | Behov og avgrenset leseomfang | Tilgang/effekt | MCP-verktøy | Status | Verifikasjon | Konkret rest → ferdigkriterium (og K) |
|---|---|---|---|---|---|---|---|---|
| LEA-001 | C32 | `LeasingReader.ListAsync (kind=framework)` | Rammer med eksisterende søk/status/eierfilter | L-L | — | Ikke implementert | — | Avgrenset DTO/filter/paging; bevar forskjellen på legacy liste og detalj/report-omfang. |
| LEA-002 | C32 | `LeasingReader.ListAsync (kind=acquisition)` | Anskaffelser med eksisterende søk/status/eierfilter | L-L | — | Ikke implementert | — | Avgrenset DTO/filter/paging; bevar forskjellen på legacy liste og detalj/report-omfang. |
| LEA-003 | C32 | `LeasingReader.GetAsync (framework=true)` | Konkret ramme og ordinære vilkår/kapasitet | L-D | — | Ikke implementert | — | Avgrenset DTO/filter/paging; bevar forskjellen på legacy liste og detalj/report-omfang. |
| LEA-004 | C32 | `LeasingReader.GetAsync (framework=false)` | Konkret anskaffelse og ordinære finans-/varefelt | L-D | — | Ikke implementert | — | Avgrenset DTO/filter/paging; bevar forskjellen på legacy liste og detalj/report-omfang. |
| LEA-005 | C32 | `LeasingReader.OptionsAsync` | Ordinære parts-/rammeoppslag, ikke personvalg/handlingsflagg | L-O | — | Ikke implementert | — | Avgrenset DTO/filter/paging; bevar forskjellen på legacy liste og detalj/report-omfang. |
| LEA-006 | C33 | `LeasingReader.NewAcquisitionAsync` | Nytt anskaffelsesobjekt i minnet | Eier/åpen ramme eller administrator | — | Utelatt | — | Skriveforberedelse; ingen ny ordinær lesekilde. |
| LEA-007 | C34 | `LeasingEquipmentService.ListEquipmentAsync` | Utstyr: status/search/acquisition og relasjoner | L-E | — | Ikke implementert | — | SQL-grenser/totals, rens person/fri tekst og dokumentkoblinger; ikke bland utstyrsantall med finansposter. |
| LEA-008 | C35 | `LeasingOrderReader.ListOrdersAsync` | Ordreliste per valgfri ramme | L-O | — | Ikke implementert | — | Paginert smal lesing; valuta/revisjon og foreldreautorisasjon, ingen godkjenningsfullmakt. |
| LEA-009 | C35 | `LeasingOrderReader.GetOrderAsync` | Konkret ordre/linjer/leveranser og eksisterende reservasjon | L-O | — | Ikke implementert | — | Paginert smal lesing; valuta/revisjon og foreldreautorisasjon, ingen godkjenningsfullmakt. |
| LEA-010 | C35 | `LeasingOrderReader.AcquisitionOrdersAsync` | Ordrer koblet til én autorisert anskaffelse | L-D | — | Ikke implementert | — | Paginert smal lesing; valuta/revisjon og foreldreautorisasjon, ingen godkjenningsfullmakt. |
| LEA-011 | C35 | `LeasingOrderReader.LimitChangesAsync` | Lagret limitthistorikk per ramme, uten aktør/reason | L-limit | — | Ikke implementert | — | Paginert smal lesing; valuta/revisjon og foreldreautorisasjon, ingen godkjenningsfullmakt. |
| LEA-012 | C35 | `LeasingOrderReader.OrderLinkCandidatesAsync` | Koblingskandidater | Ordre-/eierregler | — | Utelatt | — | Skriveforberedelse; ordinære koblinger dekkes av AcquisitionOrdersAsync. |
| LEA-013 | C35 | `LeasingOrderReader.CanChangeLimitAsync` | Handlingsflagg | ApproveLimit | — | Utelatt | — | Skriveforberedelse; ikke en forretningsdatakilde. |
| LEA-014 | C36 | `LeasingLifecycleService.GetLifecycleAsync` | Vilkår/hendelser/closure/replacement/utstyrsreferanser | L-life | — | Ikke implementert | — | Lagret paginert historie; parent-/referansevalidering og siste revisjon; ingen endring/reversering. |
| LEA-015 | C37 | `LeasingPaymentService.GetPaymentsAsync` | Anskaffelsens planer/finansiering/terminer og avstemming | L-pay | — | Ikke implementert | — | Separate smale paginerte delsett, valuta/snapshots/revisjoner; ikke rå dokument/historikkaktører. |
| LEA-016 | C37 | `LeasingPaymentService.ListPaymentPlansAsync` | Planliste med search/currency/finance/status | L-pay | — | Ikke implementert | — | Autorisert SQL-side/telling, nullbeløp/planversion, ingen blandet valutasum. |
| LEA-017 | C38 | `LeasingPurchaseInvoiceService.ListInvoicesAsync` | Innkjøpsfakturaliste | L-inv | — | Ikke implementert | — | DTO/paging/total og parentinnsyn; rens OCR/review-JSON/dokumentinnhold/token/aktør. |
| LEA-018 | C38 | `LeasingPurchaseInvoiceService.GetInvoiceAsync` | Konkret faktura og linjer/snapshots | L-inv | — | Ikke implementert | — | DTO/paging/total og parentinnsyn; rens OCR/review-JSON/dokumentinnhold/token/aktør. |
| LEA-019 | C38 | `LeasingPurchaseInvoiceService.PurchaseInvoicesAsync` | Fakturaer og linjer for én anskaffelse | L-inv | — | Ikke implementert | — | DTO/paging/total og parentinnsyn; rens OCR/review-JSON/dokumentinnhold/token/aktør. |
| LEA-020 | C38 | `LeasingPurchaseInvoiceService.PreviewInvoiceAsync` | Revisjonsbundet godkjenningspreview med faktisk kapasitet/reservasjon | L-inv/editable | — | Ikke implementert | — | Eksisterende OpenAsync(false)/Editable/Purchase/Order, ingen Commit i metoden; READ ONLY-test av port/hjelpere og grenser før MCP; ikke anta vanlig fakturaleserett gir preview. |
| LEA-021 | C39 | `LeasingRentalInvoiceService.ListRentalInvoicesAsync` | Leiefakturaliste med search/currency/finance/status | L-inv | — | Ikke implementert | — | Paginert ordinær DTO, faktiske snapshots/valuta, ingen overgangswrites eller rå tolkingsdata. |
| LEA-022 | C39 | `LeasingRentalInvoiceService.GetRentalInvoiceAsync` | Leiefaktura/linjer/allokering/avvik/korreksjons- og reverseringshistorikk | L-inv | — | Ikke implementert | — | Paginert ordinær DTO, faktiske snapshots/valuta, ingen overgangswrites eller rå tolkingsdata. |
| LEA-023 | C40 | `LeasingReportService.GetLeasingReportAsync (kind=Agreements, export=false)` | Eksisterende Agreements-rader og valutasplittede totaler | L-report | — | Ikke implementert | — | Egen variantavstemming av datoakse/utvalg/totals, eksplisitt overflow over dagens 10000-grense; ingen falsk komplett side. |
| LEA-024 | C40 | `LeasingReportService.GetLeasingReportAsync (kind=Capacity, export=false)` | Eksisterende Capacity-rader og valutasplittede totaler | L-report | — | Ikke implementert | — | Egen variantavstemming av datoakse/utvalg/totals, eksplisitt overflow over dagens 10000-grense; ingen falsk komplett side. |
| LEA-025 | C40 | `LeasingReportService.GetLeasingReportAsync (kind=Dimensions, export=false)` | Eksisterende Dimensions-rader og valutasplittede totaler | L-report | — | Ikke implementert | — | Egen variantavstemming av datoakse/utvalg/totals, eksplisitt overflow over dagens 10000-grense; ingen falsk komplett side. |
| LEA-026 | C40 | `LeasingReportService.GetLeasingReportAsync (kind=Payments, export=false)` | Eksisterende Payments-rader og valutasplittede totaler | L-report | — | Ikke implementert | — | Egen variantavstemming av datoakse/utvalg/totals, eksplisitt overflow over dagens 10000-grense; ingen falsk komplett side. |
| LEA-027 | C40 | `LeasingReportService.GetLeasingReportAsync (kind=InvoiceControl, export=false)` | Eksisterende InvoiceControl-rader og valutasplittede totaler | L-report | — | Ikke implementert | — | Egen variantavstemming av datoakse/utvalg/totals, eksplisitt overflow over dagens 10000-grense; ingen falsk komplett side. |
| LEA-028 | C40 | `LeasingReportService.GetLeasingReportAsync (kind=Equipment, export=false)` | Eksisterende Equipment-rader og valutasplittede totaler | L-report | — | Ikke implementert | — | Egen variantavstemming av datoakse/utvalg/totals, eksplisitt overflow over dagens 10000-grense; ingen falsk komplett side. |
| LEA-029 | C40 | `LeasingReportService.GetLeasingReportAsync (export=true)` | Særskilt autorisert eksportutvalg, samme seks rapporttyper | L-export | — | Ikke implementert | — | Bevar separat ExportReports; begrenset data-/sidekontrakt; CSV/XLSX/binærformatering utelates som adapter. |
| LEA-030 | C40 | `LeasingReportService.GetLeasingDashboardAsync` | Eksisterende dashboard og sammenhengende rapportkategorier | L-report; skriver i dag | — | Ikke implementert | — | Skill READ ONLY-Application-inngang fra ListLeasingFollowupsAsync/initialisering før MCP; bevar tallbetydning. |
| LEA-031 | C41 | `LeasingFollowupService.ListLeasingFollowupsAsync` | Lagrede frister, kind/status/to/owner og total | L-life; skriver i dag | — | Ikke implementert | — | Ny skrivefri reader; ingen initialization.RunAccountAsync, personkatalog eller read-markering; false init må være eksplisitt. |
| LEA-032 | C41 | `LeasingFollowupService.LeasingNotificationHistoryAsync` | Administratorens lagrede varslingshistorikk som upersonlig hendelsesstatus | LeasingUserScope.IsAdministrator | — | Ikke implementert | — | Vurder konkret feltutvalg og paging; ingen mottakere/transport eller markering-som-lest. |
| LEA-033 | C41 | `LeasingFollowupService.GetLeasingNotificationSettingsAsync` | Tekniske varslingsinnstillinger | LeasingUserScope.IsAdministrator | — | Utelatt | — | Konfigurasjon, ikke ordinær rapportkilde. |
| LEA-034 | C42 | `LeasingReader.GetAsync (framework=true) (dokumentmetadata)` | Dokumentmetadata for ramme | L-doc | — | Ikke implementert | — | Paginert navn/media/størrelse/dato, konkret foreldreinnsyn og referansegrenser; ingen StorageKey/aktør eller binærinnhold. |
| LEA-035 | C42 | `LeasingReader.GetAsync (framework=false) (dokumentmetadata)` | Dokumentmetadata for anskaffelse | L-doc | — | Ikke implementert | — | Paginert navn/media/størrelse/dato, konkret foreldreinnsyn og referansegrenser; ingen StorageKey/aktør eller binærinnhold. |
| LEA-036 | C42 | `LeasingOrderReader.GetOrderAsync (dokumentmetadata)` | Dokumentmetadata for ordre | L-doc | — | Ikke implementert | — | Paginert navn/media/størrelse/dato, konkret foreldreinnsyn og referansegrenser; ingen StorageKey/aktør eller binærinnhold. |
| LEA-037 | C42 | `LeasingPaymentService.GetPaymentsAsync (dokumentmetadata)` | Dokumentmetadata for betalingsplan/anskaffelse | L-doc | — | Ikke implementert | — | Paginert navn/media/størrelse/dato, konkret foreldreinnsyn og referansegrenser; ingen StorageKey/aktør eller binærinnhold. |
| LEA-038 | C42 | `LeasingPurchaseInvoiceService.GetInvoiceAsync (dokumentmetadata)` | Dokumentmetadata for innkjøpsfaktura | L-doc | — | Ikke implementert | — | Paginert navn/media/størrelse/dato, konkret foreldreinnsyn og referansegrenser; ingen StorageKey/aktør eller binærinnhold. |
| LEA-039 | C42 | `LeasingRentalInvoiceService.GetRentalInvoiceAsync (dokumentmetadata)` | Dokumentmetadata for leiefaktura | L-doc | — | Ikke implementert | — | Paginert navn/media/størrelse/dato, konkret foreldreinnsyn og referansegrenser; ingen StorageKey/aktør eller binærinnhold. |
| LEA-040 | C42 | `LeasingDocumentService.DownloadAsync` | Binær ramme-/anskaffelses-/ordrefil | L-doc | — | Utelatt | — | Binærinnhold utelatt; ordinære metadata i egen rad. |
| LEA-041 | C42 | `LeasingDocumentService.ReadPaymentPlanSourceAsync` | Ark/import-grid fra dokument | Plan/foreldreinnsyn | — | Utelatt | — | Rå dokument-/importinnhold utelatt; ingen parsing som generell MCP-kilde. |
| LEA-042 | C42 | `LeasingPurchaseInvoiceService.DownloadInvoiceAsync` | Binær innkjøpsfaktura | L-inv + dokument | — | Utelatt | — | Originalinnhold utelatt; metadata er ikke utelatt. |
| LEA-043 | C42 | `LeasingRentalInvoiceService.DownloadRentalInvoiceAsync` | Binær leiefaktura | L-inv + dokument | — | Utelatt | — | Originalinnhold utelatt; metadata er ikke utelatt. |
| LEA-044 | C43 | `DimensionService.GetAsync` | Kontoens dimensjoner/verdier/parenthierarki | H | — | Ikke implementert | — | Smal paginert katalog, ingen rå entiteter; behold AccountStructure.Manage, ikke anta leasingrolle nok. |
| LEA-045 | Nytt | `LeasingClassificationService.ClassificationCatalogAsync` | Dimensjonsregler og tilgjengelige klassifikasjonsverdier | L-class | — | Ikke implementert | — | Nyoppdaget N01: DTO/side, CanReadLifecycle/CanReadOrders eller eid root; ingen administrativ regelendring. |
| LEA-046 | Nytt | `LeasingEquipmentService.EquipmentBuildingsAsync` | Utstyrsbygg som ordinært filteroppslag | Aktivt kontomedlem | — | Ikke implementert | — | Nyoppdaget N02: kontobegrenset side/søk; bevar faktisk medlemskapsregel uten å arve utstyrs-/detaljfullmakt. |
| LEA-047 | Nytt | `LeasingOrderReader.NewOrderAcquisitionAsync` | Anskaffelsesforslag fra ordre | Ordre-/eierregler | — | Utelatt | — | Nyoppdaget N03: skriveforberedelse; utelatt som NewAcquisitionAsync, ikke ny lesekilde. |

## Kryssavstemming av alle gamle rader

| Gammel ID | Opprinnelig inngang (uendret rekkefølge) | Konkrete operasjoner |
|---|---|---|
| C01 | AgreementListService.ListAsync; AgreementReportingReader.ListAsync/CountAsync | AGR-001, AGR-002, AGR-003 |
| C02 | AgreementDetailsReader.ReadSummaryAsync; ReadAsync; AgreementReadExtensions.DetailsAsync | AGR-004, AGR-005 |
| C03 | AgreementAttentionService.GetAttentionAsync | AGR-006 |
| C04 | AgreementDashboardService.GetDashboardContractsAsync | AGR-007 |
| C05 | AgreementInsightsService.GetInsightsAsync | AGR-008, AGR-009 |
| C06 | AgreementFollowupReader.ReadAsync; AgreementFollowupPageReader.ReadAsync; AgreementReadExtensions.HistoryAsync | AGR-010, AGR-011, AGR-012 |
| C07 | AgreementDeadlineReader.ReadAsync | AGR-013 |
| C08 | AgreementManagementService.GetOptionsAsync; AgreementReadExtensions.RelationsAsync | AGR-014, AGR-015 |
| C09 | AgreementFinancialService.GetLinesAsync/ForecastAsync; AgreementFinanceReader | AGR-016, AGR-017 |
| C10 | AgreementFinancialService.PreviewBasisAsync, ListBasisAsync, GetAffectedBasesAsync, GetBasisAsync, GetBulkBasisOptionsAsync, PreviewBulkBasisAsync, ListPendingBasisApprovalsAsync | AGR-018, AGR-019, AGR-020, AGR-021, AGR-022, AGR-023, AGR-024 |
| C11 | AgreementIndexService.GetIndicesAsync | AGR-025, AGR-026 |
| C12 | AgreementFollowupWriter.GetReminderSettingsAsync | AGR-027 |
| C13 | AgreementAnalysisService.GetAnalysesAsync, FindAnalysisOrganizationsAsync | AGR-028, AGR-029 |
| C14 | AgreementDocumentService.DownloadAsync; dokumentmetadata i AgreementDetailsDto | AGR-030, AGR-031 |
| C15 | AgreementNoticeRules | Regelhjelper; AGR-005 / C02, ikke ekstra operasjon |
| C16 | BuildingService.GetBuildingsAsync/GetBuildingAsync | STR-001, STR-002 |
| C17 | UnitService.GetUnitsAsync/GetUnitAsync | STR-003, STR-004 |
| C18 | UnitService.GetParentCandidatesAsync | STR-005 |
| C19 | OrganizationService.GetOrganizationsAsync/GetOrganizationAsync | STR-006, STR-007 |
| C20 | AccountRegisterService.ReadOrganizationElementsAsync/ReadGeographicAreasAsync | STR-008, STR-009 |
| C21 | AccountService.GetAccountsAsync/GetAccountAsync; AccountContextReader.ReadAsync/ReadSelectedNameAsync | STR-010, STR-011, STR-012, STR-013 |
| C22 | OccupancyService.GetOccupanciesAsync/GetOccupancyAsync | OCC-001, OCC-002 |
| C23 | OccupancyService.GetHierarchyConflictsAsync | OCC-003 |
| C24 | ServiceDefinitionService.GetServiceDefinitionsAsync/GetServiceDefinitionAsync | SRV-001, SRV-002 |
| C25 | ServiceDefinitionService.GetProvidersAsync/GetProviderAsync/GetProviderOrganizationOptionsAsync | SRV-003, SRV-004, SRV-005 |
| C26 | ServiceDefinitionFieldService.GetFieldAsync | SRV-006 |
| C27 | ServiceCatalogService.GetAvailableServicesAsync/GetServiceAsync/GetAvailableRequestLocationsAsync | SRV-007, SRV-008, SRV-009 |
| C28 | ServiceRequestReportingReader.ListAsync/CountAsync | SRV-010, SRV-011 |
| C29 | ServiceRequestService.GetMyRequestsAsync/GetAssignedRequestsAsync/GetAdminRequestsAsync | SRV-012, SRV-013, SRV-014 |
| C30 | ServiceRequestService.GetRequestAsync | SRV-015 |
| C31 | ServiceRequestService.GetAvailableProvidersAsync/ComposeProviderRequestAsync | SRV-016, SRV-017 |
| C32 | LeasingReader.ListAsync/GetAsync/OptionsAsync | LEA-001, LEA-002, LEA-003, LEA-004, LEA-005 |
| C33 | LeasingReader.NewAcquisitionAsync | LEA-006 |
| C34 | LeasingEquipmentService.ListEquipmentAsync | LEA-007 |
| C35 | LeasingOrderReader.ListOrdersAsync/GetOrderAsync/AcquisitionOrdersAsync/OrderLinkCandidatesAsync/LimitChangesAsync/CanChangeLimitAsync | LEA-008, LEA-009, LEA-010, LEA-011, LEA-012, LEA-013 |
| C36 | LeasingLifecycleService.GetLifecycleAsync | LEA-014 |
| C37 | LeasingPaymentService.GetPaymentsAsync/ListPaymentPlansAsync | LEA-015, LEA-016 |
| C38 | LeasingPurchaseInvoiceService.ListInvoicesAsync/GetInvoiceAsync/PurchaseInvoicesAsync/PreviewInvoiceAsync | LEA-017, LEA-018, LEA-019, LEA-020 |
| C39 | LeasingRentalInvoiceService.ListRentalInvoicesAsync/GetRentalInvoiceAsync | LEA-021, LEA-022 |
| C40 | LeasingReportService.GetLeasingReportAsync/GetLeasingDashboardAsync | LEA-023, LEA-024, LEA-025, LEA-026, LEA-027, LEA-028, LEA-029, LEA-030 |
| C41 | LeasingFollowupService.ListLeasingFollowupsAsync/LeasingNotificationHistoryAsync/GetLeasingNotificationSettingsAsync | LEA-031, LEA-032, LEA-033 |
| C42 | LeasingDocumentService.DownloadAsync/ReadPaymentPlanSourceAsync; originalfakturanedlasting | LEA-034, LEA-035, LEA-036, LEA-037, LEA-038, LEA-039, LEA-040, LEA-041, LEA-042, LEA-043 |
| C43 | DimensionService.GetAsync | LEA-044 |
| C44 | MeetingRoomService.ListAsync/GetAsync/GetBuildingsAsync | ROM-001, ROM-002, ROM-003 |
| C45 | RoomBookingService.ListAsync/GetAsync/IsAvailableAsync | ROM-004, ROM-005, ROM-006 |
| C46 | CalendarIntegrationService.ListAsync; ICalendarProvider.GetAvailabilityAsync | ROM-007, ROM-008 |

## Rapportstøtte og aktivering – egne leveranser

MCP-status i radene er lesedekning for chat/andre konsumenter. Bare **Agreements og ServiceRequests** har deterministiske rapportadaptere i dagens `ReportAreas.Sources`. Avtaleøkonomi, indeks, grunnlag, detaljer og oppslag blir ikke rapportkilder bare fordi et verktøy finnes. Alle foreslåtte nye rapportområder nedenfor er **ikke implementert**, og skal være typede adaptere med deklarative filter-/kolonnevalg. De er delstegsaksept, ikke ekstra eksisterende Application-metoder i opptellingen av eksisterende metoder. Endres avgrensningen, føres det eksplisitt i checkpoint.

| Delsteg | Konkret avgrenset rapportleveranse og ferdigkriterium | Chat/oppslag uten rapportadapter |
|---|---|---|
| 10B | Byggliste: nøkkeltall for unike bygg, diagram etter lagret City og paginert tabell med navn/adresse/postnummer/by. Enheter: unike enheter, Type eller Building, tabell med navn/type/aktiv/bygg/parent. Organisasjoner: unike organisasjoner, Type, tabell med navn/nummer/type/aktiv. Én side velger én av disse tre typede kildene på én tilkobling; ingen join eller beleggstall. Samme autoriserte filtre i query/aggregate, dokumentert missing-gruppe og overflow. Hver kilde skal gjennom prompt→preview→lagring, redigering og modellfri kjøring før rapportdelen lukkes. Leveres i denne rekkefølgen, ikke tre samtidige implementeringer. | Konkrete detaljer og kontoens to hierarkier. Hierarkienes parent/path er lagret struktur, ikke ekstra organisasjoner eller en aggregert historisk serie. |
| 10C | Beleggsrader med bygg/leietakerorganisasjon/enhet og ValidFrom/To. Unikt antall **beleggsrader**, gruppe Building eller TenantOrganization, paginert tabell med samme periodefilter. Avstem dagens IsActive (`ValidFrom <= Today`, null/inklusive ValidTo) før kontrakt. Ingen ledighetsgrad, kapasitetsprosent eller historiske snapshots. Ny typet adapter med samme komplette opprettings-/redigerings-/modellfrie kjede. | Enkeltperiode og referanseoppslag; ingen egen konflikt-/skrivevalideringskilde. |
| 10D | Bevar 9E som rapportkilde og utvid eksplisitte My/Assigned/Admin-visninger med samme query/aggregate-filter; ingen uniontotal merket som «mine». Definisjonskatalog får en avgrenset kilde med kode/navn/kategori/aktiv, unik total og Category-grupper, uten providers/felt i selve tabellen. Ingen rapportadapter for provider-/felt-/lokasjonsdetaljer. Begge listkildenes endringer må ha syntetisk prompt, preview, lagring/redigering og kjøring med AI av. | Bestillbar katalog, felt og ordinære leverandørkoblinger har ulike tilgangsregler; et administrativt definisjonsfilter kan ikke gjenbrukes som leietakerens katalogautorisasjon. |
| 10E | Romkilde: unik romtotal, gruppe Building, paginert tabell navn/kapasitet/aktiv/bygg. Bookingkilde: unik lagret bookingtotal med rom og begrenset UTC-intervall, gruppe lagret Status eller Room, tabell tid/rom/status uten Subject/Description/organisator. Én kilde per side; ingen beleggsprosent eller historisk kapasitetsserie. Begge får samme komplette rapportkjede og dato-/null-/tomsettprøver. | IsAvailable er et konkret øyeblikksoppslag, ikke et diagram eller en reservasjon. Kalenderadministrasjon/providertransport er X. |
| 10F | Bevar eksisterende avtalesider v1/v2 og tjenesteforespørsler v3. Ingen ny finans-/indeks-/basisrapportadapter i dette delsteget: disse er eksplisitt avgrensede chat-/konsumentberegninger, detalj-/register-/snapshotkilder med egen retning, valuta, revisjon og freshness. Ingen summering av fremtidige forslag og lagrede grunnlag. Dette er en konkret grense, ikke en ubestemt «rapport senere»-oppgave. Ferdig når eksisterende avtalerapportregresjoner fortsatt består og I/D-radene er lukket i sitt angitte MCP-omfang. | PreviewBasis, berørte/bulk/godkjenningslesing, upersonlig noticehistorikk, dokument-/analysemetadata. Ingen approval/generering/andre skriverier. |
| 10G | Første leasingrapportkilde er `GetLeasingReportAsync(kind=Agreements, export=false)` for anskaffelser: antall, lagret Status-gruppe og tabell navn/referanse/status/valuta/sluttdato. Ingen blandet valutasum. De fem andre rapporttypene får **avgrensede MCP-resultatkontrakter med egne faktiske datoakser/valutasplittede totaler**, ikke fem nye personlige sideadaptere. Capacity, Dimensions, Payments, InvoiceControl og Equipment skal alle ha verifiserte totals/overflow/tilgang før delsteget lukkes. Avgrenset Agreements-adapter skal ha prompt, preview, eksplisitt lagring/redigering og AI-av-kjøring; øvrige typer konsumeres via MCP uten sideadapter i 10G. | Rammer, ordrer, livsløp, planer, fakturadetaljer, dimensjoner og dokumentmetadata; skrivefri oppfølging/dashboard krever eget uttrekk. ExportReports gir bare eksportutvalg, ikke ny MCP-skrivefullmakt eller filtransport. |

For nye rapportkilder skal typede filtre ikke endre område/identitet/eier/tilkobling fra modellen. Tellinger må være SQL-baserte på samme autoriserte sett som listen; ingen summering av listesider. Normaliserte filtre, tidsreferanse, `complete`/`hasMore`/total og manglende snapshotgaranti må valideres av adapteren. Ved for mange grupper eller for stort enkeltobjekt: eksplisitt grensefeil med krav om snevrere filter, ingen stille trunkering/falske totaler. Hierarkier kan hentes sidevis som flate noder med parent-ID; komplett undertre kan bare hevdes etter full henting innen dokumenterte grenser.

**Drift, separat fra alle F-statusene:** nye scopes/klientflagg/samtykke/profilbindinger må vurderes per domene. Navnene `tenantplatform.structure.read`, `tenantplatform.occupancies.read`, `tenantplatform.services.read`, `tenantplatform.meeting_rooms.read` og `tenantplatform.leasing.read` er forslag til 10B–10G, ikke eksisterende konfigurasjon eller vedtatte rettigheter. Eksisterende service_requests-scope skal ikke antas å gi administrativ servicekatalog. Scope er bare transportgrense; Application avgjør ressursinnsyn. Avtalescope gir aldri struktur-/leasingfullmakt.

Hvert delsteg skal dokumentere eksakte SELECT-kolonner/joinreferanser, ingen DML eller brede tabellprivilegier; kreve eksplisitt klientregistrering og personlig samtykke når nødvendig; oppdagelse/klassifisering/eksakt versjonsgodkjenning og eventuell eksplisitt gjenbinding av lagrede sider. Kode-/testleveranse utfører ikke disse driftsstegene. Først en separat dokumentert prøve med riktig personlig bruker/konto bekrefter operativ aktivering. Tidligere 9E-feil er fortsatt ubekreftet. Containerbygg/deployment 8C, generell Web-cookie-/sesjonsrevokering og flerinstansstøtte er separate spor, ikke skjulte 10A-krav.

## Implementerbar 10B-rekkefølge og første uttrekk

**STR-001 er nå levert. Neste oppdrag er bare den avgrensede Bygg-rapportadapteren** med prompt→preview→eksplisitt lagring/redigering→kjøring uten AI. 10A-planens opprinnelige slice og ferdigkriterium nedenfor beholdes som sporbar beskrivelse av hva som er verifisert; de skal ikke implementeres på nytt.

Kodegrunnlaget for denne rekkefølgen: [BuildingService](../../src/TenantPlatform.Application/Buildings/BuildingService.cs), [BuildingData](../../src/TenantPlatform.Infrastructure/Buildings/BuildingData.cs), [UnitService](../../src/TenantPlatform.Application/Units/UnitService.cs), [OrganizationService](../../src/TenantPlatform.Application/Organizations/OrganizationService.cs), [AccountRegisterService](../../src/TenantPlatform.Application/AccountSettings/AccountRegisterService.cs) og [felles evaluator/legacyunntak](../../src/TenantPlatform.Application/Security/AuthorizationService.cs). Eksisterende direkte [BuildingChecks](../../tests/TenantPlatform.AgreementList.SmokeTests/BuildingChecks.cs) prøver blant annet bred PropertyAdmin-liste mot avvist detalj på annet bygg; de er grunnlag, ikke tilstrekkelig bevis for fremtidig MCP.

1. **STR-001 først:** byggliste med søk/paging og autorisert SQL-aggregat. Deretter dens avgrensede Bygg-rapportadapter som beskrevet over. Ikke kall `BuildingService.GetBuildingAsync` for hver rad eller bruk avtalers `agreements_relations` som full byggkatalog.
2. STR-002: detalj med konkret Buildings.Read. Eksisterende `BuildingData.ReadDetailsAsync` teller alle enheter i bygget, distinkte leietakerorganisasjoner gjennom lagrede belegg **uten periodefilter**, og requests hvor status ikke er Completed/Cancelled/Rejected/Failed. `Units` inneholder alle byggets enheter. Dette er ikke automatisk «dagens leietakere» eller requesterens egen forespørselsliste. Avstem relasjonenes metadata-/telleinnsyn før kontrakt; ikke finn på en annen åpenstatus. Hvis tellerne krever eget lesekriterium, hold dem eksplisitt utenfor første detaljversjon og la STR-002 være D frem til avklaringen/leveransen er fullført.
3. STR-003/004: enheter med bygg/parent og UnitType/IsActive, deretter Enheter-rapportkilden. Hierarkiet krever kontobegrenset parent og bygg, stabil ID-sortering og syklus-/manglende-nodehåndtering uten stille tapt struktur. `ChildUnitCount`/`OccupancyCount` må få dokumentert omfang før detaljen lukkes.
4. STR-006/007: organisasjonsliste/detalj og Organisasjoner-rapportkilden. `OrganizationNumber` er ordinært organisasjonsfelt, ikke grunnlag for automatisk identitetskobling. Bevar `GetOrganizationListAccessAsync` og den uttrykkelige brede PropertyAdmin-organisasjonsregelen; ikke gjør den lik byggdetaljreglene.
5. STR-008/009: organisasjons-/geografihierarkier med eksisterende AccountStructure.Manage, ingen implicit medlemsrett via avtalerelasjoner. Foreldre/path/aktivitet projiseres; paginert nodehenting og valgt-nodeinnsyn må være eksplisitt.

**Første produksjonsslice er nøyaktig STR-001, ikke PreviewBasisAsync og ikke hele 10B:** foreslå `BuildingReportingReader` i Application med typet søk og et felles Open-autorisert listescope; konkret `BuildingReportingData` i Infrastructure for query/count/group; `BuildingTools` i MCP med `buildings_query` og `buildings_aggregate`. Dette er planlagte fil-/kontraktnavn, ikke implementerte API-er. Begge bruker Buildings.Open-kontoinnsyn og det eksisterende brede PropertyAdmin-listeunntaket. Detailrettigheter eller grants fra avtaler må ikke inngå som snarvei. Konto er betrodd verifisert kontekst, ikke klientvalgt scope.

Foreslått første kontrakt: `accountId`, valgfritt navn-delstrengsøk (trimmet, maksimalt 200 tegn), `page` 1–10000 bare på query. Faste sider 25; Name/ID som stabile sorteringsnøkler. Returner ID/Name/AddressLine1/PostalCode/City fra eksisterende listemodell. Aggregate gir unik byggtotal og én valgfri City-gruppering på **lagret eksakt byverdi**; null/tom by er én eksplisitt missing-gruppe, ingen geografisk normalisering/antatt kommune. Maks 100 grupper, ellers grensefeil uten delvis total. Autorisasjons-/ukjent konto-feil skal ikke lekke eksistens; kansellering og hostens eksisterende svargrense gjelder uten stille utelatelse. Ingen UnitCount, TenantCount, OpenRequestCount, Units, nye statusregler eller detaljinnsyn i denne slicen.

**STR-001 er ferdig når** begge skrivefrie MCP-kontraktene med felles filter og faktisk total er levert, eksakt scope/klient-/profil-/SELECT-kontrakt er dokumentert og testet, agenten kan bruke et eksplisitt strukturutvalg innen tolvgrensen, og direkte Application + faktisk OAuth/MCP + isolert PostgreSQL dekker AccountAdmin/PropertyAdmin-legacy, manglende medlemskap, deaktivering/tilbakekalling, PlatformAdmin uten konto, fremmed konto, paging/like navn/nullby/overflow/avvisning og SaveChanges-/database-skrivevern. Eksisterende avtale- og 9E-flyter må fortsatt bestå. Bygg-rapportadapteren er neste sammenhengende slice, med separat rapportaksept; STR-001 alene lukker ikke 10B eller markerer rapportstøtte ferdig.

## 10H – sluttavstemming

Avstem alle 119 operasjons-ID-er, alle C01–C46 og tillegg N01–N03 mot produksjonskatalog, Application-regler og steg 4-oversikten. I/D/U er reelle rester; X må ha konkret begrunnelse, og nye funn tilføyes med egen ID og endret tilleggstall. Kontroller også innganger utenfor leasing, tekniske adapterunntak, manglende felt/varianter, autorisasjon som eventuelt bare ligger i Infrastructure, og skjulte initialiserings-/skriveveier. Bevar person-/credential-/binær-/skriveutelatelser fra dekningsoversiktens sikkerhetsavsnitt; slettbarhetskontroller, editorforslag og systemjobber blir ikke rapportkilder fordi de returnerer et resultat.

Sluttkriterium: alle avtalte ordinære leserader er F, eller konkret dokumentert X etter reell vurdering; ingen uløste I/D/U eller oversette ordinære innganger. De avgrensede rapportleveransene ovenfor er verifisert, med gamle sidedefinisjoner og AI-av-kjøring bevart. Skill denne implementerings-/automatiseringsstatusen fra aktivering og manuelle kontroller. Ingen påstand om at alle historiske DTO-felt eller private GUI-funksjoner skal MCP-publiseres.
