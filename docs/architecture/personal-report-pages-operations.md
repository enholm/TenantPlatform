# Steg 9A: personlige rapportsider

«Mine sider» (`/my-pages`) administrerer personlige, kontoavgrensede rapporter. `/my-pages/{id}` åpner en lagret side med ferske data. Oppretting, manuell redigering, forhåndsvisning, lagring, flytt opp/ned og sletting inngår. Tittel er visningstekst; stabil GUID er identitet. Ingen AI, modellnøkkel, deling, eksport, bakgrunnsoppdatering eller rapportresultatlager kreves.

## Definisjon og eierskap

`PersonalPage` har backendfastsatt Id, AccountId, OwnerUserId og en PageDefinition med Version=1. Oppretting tar ikke inn eier eller konto. GUI bruker ferskt verifisert cookie; administrasjons-MCP bruker validert OAuth-subjekt og et foreslått AccountId som Application revaliderer. PlatformAdmin gir ikke automatisk medlemskap, datatilgang eller innsyn i andres sider.

Definisjonen inneholder:

- Title (1–100 tegn), ConnectionId og låst ProfileVersion.
- Eksakte QueryVersion/AggregateVersion fra registerets godkjente metadata for agreements_query/agreements_aggregate; forventet resultatkontrakt er schemaVersion 1.
- Period: NextMonths med Months 1–12, eller Fixed med Months=null og inklusive From/To-datoer.
- Typet AgreementReportFilter: søk, lagret status/type, arkivvalg, eksakte bygg-/enhets-/organisasjonsenhets-/geografiske ID-er og missingBuilding. Perioden eier datoene; filterets egne datogrenser/relative måneder må være null.
- Components: en permutasjon av Metric, BuildingChart og AgreementTable, nøyaktig én av hver i første versjon. Layout er bare Stacked.

Ingen rå HTML, kode, SQL, URL-er, script, evaluerte uttrykk eller frie datakildenavn inngår. Titler og kildeetiketter er ubetrodd tekst og rendres som tekst. Ukjente JSON-felter og ukjente enum-/kontraktverdier avvises. PageDefinition kan senere produseres av AI, men må gjennom samme Application-validering og eksplisitte lagring.

## Lagring og samtidighet

Domaincontexten TenantPlatformDbContext eier `public.personal_page_sets`, ikke OAuthDbContext. Sidedefinisjoner er domenedata, mens personlige OAuth-tokens fortsatt bare håndteres av eksisterende klientlager. Én rad per AccountId/OwnerUserId inneholder definisjonslisten og en Revision. Ingen rapportresultater, modellinnhold eller tokens lagres i denne tabellen.

Migrering: `20261019185214_AddPersonalReportPages`, generert med `bash scripts/add-migration.sh AddPersonalReportPages`. En midlertidig designfactory med en fiktiv tilkobling ble brukt under genereringen og deretter fjernet; Web-start og lokale hemmeligheter ble ikke brukt til å anvende migreringen. Migreringen skal bare anvendes gjennom kontrollert vanlig migreringspraksis. Normal Web-oppstart har fortsatt prosjektets eksisterende automatiske domenemigrering; ikke start en ny versjon ukontrollert mot drift.

Application kontrollerer fersk lokal bruker/konto og MCP-bruksrettighet ved list/read/preview/save/move/delete/run. Lister og oppslag er alltid begrenset til eier og konto, uten administratorunntak. En fremmed eller ukjent side røper ingen tittel/definisjon. Lagring kontrollerer også kilde/profil, personlig autorisering og eksakte godkjente verktøyversjoner.

Hele den personlige listen har én revisjon. Alle mutasjoner krever forventet revisjon. PostgreSQL advisory transaction lock per bruker/konto serialiserer oppretting/endring/rekkefølge, og revisjonen sammenlignes inne i transaksjonen. Samtidige faner får en eksplisitt konflikt. Slettes siste side, beholdes en tom liste med ny revisjon for å unngå at en gammel tom-revisjon blir gyldig igjen.

## Deterministisk kjøring gjennom MCP

PersonalPageRunner kaller bare eksisterende McpRegistryService.CallAsync med personlig OAuth, verifisert konto, valgt ConnectionId og eksakte metadatahashverdier. Det finnes ingen SQL-/GUI-/Application-forretningslesefallback eller modelladapter i rapportkjøreren. Eksisterende klientfabrikk mottar nå Application-operasjonens betrodde ICurrentActor; dette gjenbruker samme klient/lager også for validert MCP-administrasjon uten cookievideresending.

Hver kjøring gjør høyst to forretningskall:

1. agreements_aggregate med Building-gruppering og definisjonens filter/periode. Relative måneder normaliseres av målserveren med eksisterende kontodato/tidssone.
2. agreements_query med samme konkrete normaliserte datofilter og ønsket tabellside. Ingen ekstra listesider hentes for å regne totaler.

Komplett aggregat gir både nøkkeltall og diagram. Rapporten verifiserer schemaVersion/konto/filter, gruppeunikhet, sum/total og fullstendighet. Lagret relativ hensikt endres ikke av forhåndsvisning eller åpning. En ny lasting eller tabellside normaliserer perioden på nytt. Nettleserklokken bestemmer ikke kontodatoen.

Visningen inkluderer faktisk periode, hentetidspunkt, tabellside/telling og fullstendighets-/sidegrenseinformasjon. Separate kall har snapshotConsistent=false; aggregat og liste kan observere mellomliggende endringer. Ulike totaler vises med denne begrensningen, ikke som et atomisk snapshot. Manglende bygg vises i missing-gruppen etter 8D-reglene. Diagrammet bruker innebygd HTML meter per bygg, med samme tall i en tilgjengelig teksttabell og overskrifter; ingen ny diagramavhengighet, CDN eller tredjepartslasting.

## MCP-flater og privilegier

**Leseflaten** i TenantPlatform.Mcp på `/mcp` er uendret med SELECT-rolle, SaveChanges-vakt og seks leseverktøy. Den får ikke tilgang til personal_page_sets og ingen skriverettigheter.

**Administrasjonsflaten** i Web på `/mcp/pages` er separat, deaktivert som standard og har fem verktøy: personal_pages_list, personal_pages_read, personal_pages_save (opprett/endre), personal_pages_delete og personal_pages_move. Den kjører samme Application-brukstilfeller som GUI. Scope `tenantplatform.pages.manage` kreves i tillegg til read-scope; cookie, body-/query-token eller et rent lesetoken er ikke tilstrekkelig. User.Id kommer fra validert JWT-sub, aldri fra verktøyargumentene.

Begge flater tilhører samme logiske OAuth-resource/audience i OAuth:Resource, selv om administrasjonsadapteren ligger på Web-origin. Administrasjonsklienten må bruke eksplisitt operatørkonfigurert Web-URL og riktig canonical resource i autorisasjonsflyten. Innkommende bearer videresendes aldri som rapportlegitimasjon; datakall bruker brukerens eksisterende personlige registerforbindelse. Ingen ny parallell OAuth-klient, servicekonto eller administratoridentitet innføres.

Aktivering av administrasjons-MCP krever:

- OAuth og en gyldig InternalMcp-konfigurasjon fra eksisterende oppsett.
- PersonalPages:Administration:Enabled=true og en separat ConnectionString fra beskyttet konfigurasjon.
- OAuth:Clients:n:AllowPersonalPages=true bare for eksplisitt valgte klienter, etterfulgt av kontrollert --oauth-register-clients.
- Nytt eksplisitt samtykke til tenantplatform.pages.manage. Samtykkesiden beskriver skriveretten. Eksisterende grants/tokens oppgraderes ikke; Webs vanlige interne lesetilkobling ber fortsatt bare om read/offline_access.

Definisjonsforbindelsen trenger CONNECT/USAGE og **bare SELECT, INSERT, UPDATE på public.personal_page_sets**, uten eierskap, privilegerte medlemskap, schemaoppretting eller tilgang til øvrige bruker-/OAuth-/avtaledata. Sletting av en side oppdaterer listen, derfor trengs ikke DELETE. En privilegiekontroll avviser en bred identitet før administrasjonsverktøyene bruker den. Webs eksisterende tjenester revaliderer brukere og register/OAuth gjennom sine etablerte porter; definisjonsskriveren har ingen separat rettighetsmodell.

Alle administrasjonsverktøy er utenfor rapportkatalogen. Mutasjoner annonseres ikke som readOnly. Agenten utelater hele personal_pages_-prefikset også på eksterne profiler; ingen AutomaticRead-klassifisering gjør disse verktøyene tilgjengelige for automatisk agentbruk. GUI krever eksplisitte knapper og antiforgery for writes.

## Aktivering av rapportsider

1. Anvend domenemigreringen kontrollert i ønsket miljø. Det er ikke gjort mot utvikling/produksjon i dette oppdraget.
2. Behold 8Ds målrettede SELECT-rettigheter på leseflaten. Ingen nye SELECT-rettigheter trengs der for personlige sider.
3. Brukeren velger konto, kobler seg personlig til i registeret, og administrator godkjenner gjeldende agreements_query/agreements_aggregate som AutomaticRead med riktig brukerutvalg.
4. Åpne Mine sider, velg forbindelse, angi tittel og NextMonths=3, forhåndsvis og lagre. Relative datoer lagres som hensikt. Endre eller flytt siden fra samme personlige liste.

Rapportsider/GUI krever ikke at administrasjons-MCP aktiveres. ReadAgent kan være deaktivert og modellnøkkel mangle. Hvis kilde/profil/metadataversjon er endret, beholdes den gamle bindingen med utilgjengelig-status; brukeren må eksplisitt velge aktuell godkjent binding og lagre. Ingen automatisk valg av annen kilde/verktøy eller ny godkjenning.

## Grenser, feil og levetid

- Maksimum 20 sider per bruker/konto, 16 KiB per definisjon, 512 KiB lagret liste, to faste datakilder og tre komponenter.
- Høyst to forretningskall per kjøring, maksimalt 64 KiB per innkommende MCP-resultat og 128 KiB GUI-resultat. Eksisterende 8D-gruppegrense 100, tabellsider på 25/page 1–10000 og transportkvoter beholdes.
- 40 sekunders kjørebudsjett, én aktiv operasjon per bruker, maksimalt 200 aktive operasjoner på instansen. Kvoten frigjøres først ved faktisk avslutning. HTTP-GUI har 45 sekunder og 32 KiB requestgrense.
- Administrasjons-MCP har 32 KiB request, 128 KiB endelig response, 15 sekunders HTTP-budsjett og 60 forespørsler/minutt per sub uten kø. Origin-headere avvises; serverklienter bruker bearer. Flaten har egen discovery på /.well-known/oauth-protected-resource/mcp/pages.
- Ingen rapportresultatcache eller automatisk dataoppdatering. Bare åpning, forhåndsvisning, oppdaterknapp og tabellsidebytte henter data. En teknisk kontekstkontroll hvert annet sekund oppdager cookie-/kontobytte uten å hente rapportdata.

Tomt autorisert resultat er vellykket null med komplette aggregatfakta. Tilgang/tilkobling, kilde-/kontraktendring, serverfeil, grenseoverskridelse, konflikt og avbrudd gir egne statuser; feil er ikke null avtaler. Visningen tømmes før ny lasting og ved feil. Konto-/brukerbytte og logout kansellerer aktive operasjoner på instansen. Klienten kontrollerer fersk kontekst før levering og avviser sene svar etter avbrudd/bytte. Ingen felles/brukeruavhengig resultatcache finnes.

Dette er fortsatt én-instansdrift. Generell Web-cookie-/sesjonsrevokering og distribuerte kvoter/kansellering er separate oppgaver. Data kan endres mellom autorisasjon og neste kontroll; ingen atomisk revokering eller database-snapshot gjennom flere MCP-kall loves.

## Videre til 9B

AI kan senere lage samme PageDefinition v1 som et utkast, med samme kilde-/filter-/komponentbegrensninger, validering og eksplisitte lagring. 9A har ingen AI-designer, deling, publisering, eksport, eksterne rapportkontrakter, jobber, nye brukerroller eller flerinstansstøtte. Containerbygg/deployment i 8C er fortsatt utsatt. Reell nettleser-/driftsprøve av de nye sidene må skilles fra automatiserte SSR-/DOM-/TestServer-kontroller.

## Faktisk verifikasjon av 9A

Etter siste produksjonsendring bestod `dotnet build TenantPlatform.sln` med exitkode 0. Eksisterende NU1902/NU1903 for System.Security.Cryptography.Pkcs består. Web refererer nå til ModelContextProtocol.AspNetCore 2.2.0 og OpenIddict.Validation.AspNetCore 7.7.2, de samme versjonene som allerede brukes i løsningen; ingen pakkeoppgraderinger er gjort.

Følgende kjøring bestod med exitkode 0, og testklyngen ble stoppet og fjernet:

```sh
bash scripts/test-agreements-postgres.sh \
  TenantPlatform.McpClient.SmokeTests TenantPlatform.Mcp.SmokeTests \
  TenantPlatform.OAuth.SmokeTests TenantPlatform.AgreementList.SmokeTests \
  TenantPlatform.Agreements.SmokeTests TenantPlatform.AccountRegisters.SmokeTests \
  TenantPlatform.Dashboard.SmokeTests
```

Alle sju testprosjektene ble bygget. De seks første suitene bruker isolert PostgreSQL; Dashboard bruker sitt eksisterende InMemory-/SQL-/komponentoppsett. Den nye migreringen er bare anvendt i isolerte testdatabaser.

`PersonalPageChecks.cs` bruker produksjonens Application-brukstilfeller, faktisk PostgreSQL, reelle OAuth-/MCP-biblioteker og TestServer. Den dekker:

- Opprett/lagre/åpne/endre/flytt/slett, backendbundet eier/konto, fremmed bruker/konto/side, vanlig medlems personlige eierskap og samtidig redigering med eksplisitt revisjonskonflikt.
- Avvist ukjent/manipulert definisjon, 20-siders grense og 101 bygggrupper som eksplisitt feil uten tap av lagrede definisjoner.
- 27 avtaler fordelt på komplett aggregat/diagram og to tabellsider, manglende bygg og autorisert tomt resultat. Transportobservasjon bekrefter faktiske MCP-kall og gjenbruk av samme servernormaliserte filter.
- Relativ hensikt etter ny lasting over kontodatogrense med ulike lagrede tidssoner, uten at de konkrete previewdatoene lagres som hensikt.
- Fjernet avtalegrant, tilbakekalt verktøygodkjenning, manglende personlig legitimasjon og endret godkjent metadata uten automatisk gjenbinding.
- Samtidighetsgrense, kansellering gjennom produksjonens cookie-/kontobytte og avvisning av sene svar. ReadAgent er deaktivert og modellnøkkelen tom under rapportprøvene; ingen modelladapter kalles.
- SSR-rendering med sikker titteltekst, cookie-/antiforgery-beskyttet kjøring, reell PKCE/samtykke for ekstra scope, verktøyoppdagelse og CRUD gjennom administrasjons-MCP. Rent lesetoken avvises, og administrasjonsscope gir ingen tilgang til en annen persons side eller fremmed konto.
- Egen definisjonsskriver avvises ved brede databaseprivilegier og kan ikke endre avtaler. Eksisterende MCP-suite bekrefter fortsatt SELECT-rolle, rå-SQL-skriveforbud, avvist credential-lesing, SaveChanges-vakt og manglende skjult initialisering.

Node-syntakskontroll av personal-pages.js/read-agent.js og `node --test tests/TenantPlatform.McpClient.SmokeTests/PersonalPagesPanelChecks.cjs tests/TenantPlatform.McpClient.SmokeTests/AgentPanelChecks.cjs` bestod. De tre syntetiske DOM-/transporttestene kjører de faktiske GUI-adapterne, inklusive manuell serialisering/lagring, sikker diagram-/tabelltekst, tømming ved feil/avbrudd og avvisning av sene svar/kontekstbytte. Dette er ikke interaktiv nettleserverifikasjon. De 51 nye side-/samtykkeressursene har like, unike nøkler på alle tre språk. Diff-/whitespacekontroll bestod.

Før bruk i ønsket miljø gjenstår kontrollert migrering og eventuell separat aktivering av administrasjons-MCP. Gjør deretter en faktisk nettleserprøve: opprett tremånedersrapporten, lagre/gjenåpne, bla i tabellen, rediger, flytt og slett; kontroller tastatur/skjermleser og alle språk, endret godkjenning, to faner med revisjonskonflikt og tømming ved konto-/brukerbytte/logout. Ingen vanlig Web-oppstart, utviklings-/produksjonsdatabaseendring, ekte utsending/modellkall, containerbygg, deploy, commit, push eller Serga-endring inngår i denne verifikasjonen.
