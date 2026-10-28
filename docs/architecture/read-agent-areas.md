# Samtalebundet områdevalg – 9F checkpoint 3

Områdevalget ble implementert i checkpoint3. Checkpoint5 gjenbruker samme uforanderlige samtalevalg for avtalenes tre katalogunderutvalg. Se [finans- og katalogkontrakten](mcp-agreement-finance-read.md), [dekningsoversikten](mcp-read-coverage.md) og moderniseringsplanens avsnitt42 (checkpoint7). Tidligere9F videreføres nå under steg10; se moderniseringsplanens avsnitt43–44. STR-001 utvider mekanismen med eget strukturutvalg, mens bred MCP-dekning fortsatt er ufullført.

## Områder og katalog

Brukeren velger én tilgjengelig MCP-tilkobling og et eksplisitt område før en vanlig samtale starter. `ReadAgentAreas` i Application har en eksakt tilordning av egne kontraktnavn:

| Chatområde | Egne verktøy |
|---|---|
| Avtaler – oversikt og rapport (`Agreements`) | agreements_list, agreements_attention, agreements_query, agreements_aggregate, agreements_prioritized, agreements_insights, agreements_relations |
| Avtaler – detaljer og oppfølging (`AgreementDetails`) | agreements_query, agreements_details, agreements_followup, agreements_deadlines, agreements_followup_history, agreements_relations |
| Avtaler – økonomi (`AgreementFinance`) | agreements_query, agreements_details, agreements_relations, agreements_financial_lines, agreements_forecast, agreements_indices, agreements_index_values, agreements_bases, agreements_basis |
| Tjenesteforespørsler (`ServiceRequests`) | service_requests_query, service_requests_aggregate |
| Struktur – bygg (`Structure`) | buildings_query, buildings_aggregate; eget personlig strukturscope og eksakt godkjenning, ingen rapportforslag |
| Ekstern kilde (`External`) | Bare en tilkobling med faktisk ekstern registerprofil; dens allerede tillatte lesekatalog beholdes uten TenantPlatform-domeneinndeling |

Navneprefiks og modellforslag brukes ikke til områdetilhørighet. Et eksternt verktøy med et kjent avtalenavn forblir eksternt. `accounts_list` er fortsatt et kontovalg-/testverktøy utenfor agentkatalogen. Eksisterende klassifiserings-/sikkerhetsfiltre gjelder også eksterne kilder.

Områdevalgene og antall lesekilder per område beregnes fra registerets tilgjengelige, personlig autoriserte og eksakt versjonsgodkjente `AutomaticRead`-verktøy. Et område uten slike verktøy tilbys ikke. Ved start/bytte kontrolleres katalogen på nytt i Application; klientvalget er bare et forslag. Ingen verktøy godkjennes, ingen OAuth-fullmakt utvides og ingen ressursregel erstattes av områdevalget.

Samtalen lagrer område sammen med eksisterende bruker-/konto-/tilkoblingsbinding på serveren. Alle spørsmål bruker dette området. HTTP krever kjent område ved start, avviser manglende/ukjent/numerisk område og avviser områdefelt på spørsmål og andre handlinger som ikke er start/bytte. Et modellkall til et alias utenfor samtalens katalog avvises. Eksisterende fersk autorisasjon og revalidering av tidligere kilders versjon/tilgang består.

Standardgrensen er fortsatt tolv modellverktøy. Den kontrolleres etter områdeavgrensningen og på nytt med eventuelt rapportforslagverktøy. Med hele dagens godkjente katalog er det7 oversiktslesere pluss ett forslag,6 detaljlesere,9 økonomilesere eller2 forespørselslesere pluss ett forslag. Struktur har2 lesere og intet lokalt rapportverktøy; se [byggkontrakt og aktivering](mcp-building-read-contracts.md). Bare oversikt og tjenesteforespørsler får rapportforslag; backend velger disse ved redigering. De tre avtalevalgene har eksplisitt felles Parent=Agreements i samme mekanisme. Eksterne profiler får ikke eget rapportforslagverktøy. Lavere konfigurert grense eller en for stor ekstern katalog gir `ToolLimit`, uten modellkall eller stille utelatelse.

## Bytte, kansellering og sene svar

Chatpanelet viser områdevelgeren ved siden av tilkoblingen. Bytte mens en samtale, aktiv forespørsel eller et ulagret utkast finnes krever bekreftelse på at historikk og utkast fjernes. Avbryt beholder området og utkastet. Et bekreftet bytte kansellerer nettleserforespørselen og rydder den lokale visningen før svaret ventes på.

Application krever eksplisitt akseptert nullstilling, validerer nytt område mot samme tilkobling og erstatter samtalen med en ny ID. Den gamle kjøringen kanselleres; historikk, kildekontekst og utkast fjernes. Den nye samtalen har ingen arvet historikk eller rapportutkast. Eksisterende kvoter frigjøres ikke tidlig ved kansellering. Et sent commit fra den gamle kjøringen avvises av sesjonslageret.

Nettleserens generasjonskontroll og bruker-/kontokontekst gjør sene begin-, spørsmål- og byttesvar ubrukelige. Returnerte foreldede samtale-ID-er slettes når de er kjent. Hvis område endres før begin har svart, brukes det nye valget ved neste start; den gamle begin-responsen får ikke starte et spørsmål. Ved feil etter bekreftet bytte vises ingen gammel historikk eller utkast på den nye samtalen.

Dette gir isolasjon i dagens prosesslokale samtalelager. Det innfører ikke varig samtalelagring, flerinstansdeling eller generell Web-sesjonsrevokering.

## Rapportsider

`Endre med AI` henter den lagrede siden og validerer tilgang i backend. Den lagrede definisjonens rapportområde bestemmer samtalens chatområde. Modellen får et forslagsskjema med akkurat dette området; forslag om annet område avvises før nye rapportdata hentes. Eksisterende konto-/eier-/tilkoblingsbinding, original/kopi, endringsoversikt, eksplisitt lagring og revisjonsvern består.

Chatområder og rapportområder har separate typer og eksplisitt konvertering. Flere avtaleverktøy gir ikke automatisk nye diagrammer eller rapportkilder. Bare eksisterende typede `ReportAreas.Sources` tilbys i manuell oppretting. En ny manuell side kan velge Avtaler eller Tjenesteforespørsler; bytte varsler om at ulagrede innstillinger forkastes. En lagret sides backendområde endres ikke av URL-parameteren. Definisjoner v1/v2/v3 og modellfri kjøring bruker sidens egen datakildeadapter og påvirkes ikke av chatvelgerens aktuelle valg.

## Aktivering og manuelle kontroller

Ingen nye scopes, klienttillatelser, databaseprivilegier eller operative registreringer kreves for selve områdevalget. Tidligere dokumenterte verktøy trenger fortsatt ordinær oppdagelse, klassifisering, tilgang og eksakt versjonsgodkjenning. Et område som mangler godkjente lesere skal være utilgjengelig; områdevalg skal ikke brukes til å reparere manglende OAuth-samtykke eller SELECT-rettigheter.

Interaktiv nettleserkontroll gjenstår:

1. Velg Avtaler og Tjenesteforespørsler på samme tilkobling; kontroller egne kataloger og forklaringen om at valget ikke gir tilgang. Prøv også en ekstern profil.
2. Lag et ulagret utkast. Avbryt et områdebytte og kontroller at utkastet består. Bekreft deretter byttet og kontroller tom historikk/utkast.
3. Bytt område under kjøring og skift konto i en annen økt. Sene svar skal ikke dukke opp i ny kontekst.
4. Åpne gamle avtalesider og en forespørselsside med `Endre med AI`; kontroller riktig backendbestemt område, forhåndsvisning, original/kopi og separat lagring.
5. Opprett manuelt i begge rapportområder og kjør lagrede sider med AI deaktivert. Kontroller språk og varsler på nb-NO/en-GB/sv-SE.

Brukerens tidligere 9E-driftsfeil er fortsatt ubekreftet. Isolerte tester dokumenterer programoppførsel, ikke faktisk driftsaktivering. Containerbygg/8C er fortsatt utsatt.

Checkpoint6 legger de to eksplisitte [indeksleserne](mcp-agreement-index-read.md) i økonomiutvalget: maksimalt7 lesere, fortsatt uten rapportforslag og med uendret standardgrense12. Ingen ny GUI-velger, samtalebinding eller profiloppgradering.

Checkpoint7 legger [lagrede grunnlag](mcp-agreement-basis-read.md) i økonomiutvalget: maksimalt9 godkjente lesere, uendret standardgrense12 og ingen rapportforslag. Rapportområder og nye samtalebindinger er ikke endret.
