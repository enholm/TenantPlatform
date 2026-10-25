# Personlige rapportsider (9A–9D)

**Gjeldende kontrakt:** se [9D](#9d-valgfrie-komponenter-gruppering-og-kolonner) nedenfor. Beskrivelsene av v1 og obligatoriske komponenter i 9A–9C er historikk; nye lagringer bruker v2.

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

## 9B: rapportutkast fra chat

9B bygger videre på 9A uten ny migrering eller lagringsmodell. Fra `/agent` kan brukeren velge sin egen TenantPlatform-tilkobling og skrive:

> Lag en side med avtaler som utløper de neste tre månedene, med antall, diagram per bygg og avtaletabell.

Chatten viser tittel, datakilde, faktisk filter/arkivvalg, periodehensikt og en deterministisk MCP-forhåndsvisning. En oppfølging som «Kall den Kommende avtaleutløp, og ta bare med aktive avtaler» reviderer samme ulagrede utkast. Knappen **Lagre under Mine sider** lagrer den viste revisjonen og viser lenke til den faktiske siden. «Lagre den» i en melding utfører ingen write. Manuell konfigurering i Mine sider er fortsatt tilgjengelig.

### Modellforslag og binding

Den lokale function-handlingen `propose_report_page` er et strukturert forslag, ikke et administrasjons-MCP-verktøy. Den tilbys bare på egen profil med begge godkjente rapportverktøy og ved en eksplisitt brukerforespørsel om oppretting/revidering. En liten språkport gjenkjenner opprettingsord som «lag/opprett/create/make/build/skapa» sammen med side/rapport og endringsord ved eksisterende utkast. Ved andre formuleringer kan brukeren presisere «Lag en rapportside …». Kildetekst kan ikke aktivere denne handlingen. Modellinstruksjonene krever avklaring eller et uttrykkelig støttet alternativ ved økonomi, andre diagrammer/komponenter/layout/kilder eller valg av lagret side fra fritekst uten eksplisitt 9C-redigeringskontekst.

Forslaget `ReportPageProposal` er kun hensikt: tittel, PagePeriod, søk/status/type/arkiv, missingBuilding og eventuelt eksakt byggnavn, samt de tre eksisterende komponentene i ønsket rekkefølge og Stacked. Det er ikke en parallell kjørbar sidedefinisjon. Backend konverterer straks til den faktiske PageDefinition v1 og bruker PersonalPageService.Validate. Modell-/nettleserangitt bruker, konto, ConnectionId, profil, kontrakt-/metadataversjon, side-ID eller eierskap godtas ikke som forslag. Disse bindes av backend. Avanserte enhets-/organisasjons-/geografifiltre kan fortsatt settes manuelt i 9A; 9B finner ikke på interne referanser for å oppfylle slike ønsker.

JSON parses med faste camelCase-felt, dybdegrense, strenge enumverdier og avvisning av ukjente/dupliserte felt. Ukjent komponent, ugyldig periode, kode-/layoutfelt eller ugyldig kombinasjon avvises før et nytt utkast publiseres. Forslagshandlingen avslutter modellrunden; modellens fritekst eller oppdiktede tall blir aldri rapportforhåndsvisning. Responses bruker fortsatt `strict=false` og obligatorisk lokal validering; protokollformen følger [offisiell OpenAI function calling-dokumentasjon](https://developers.openai.com/api/docs/guides/function-calling). Ingen pakke-, modell- eller providerbytte inngår.

Et byggnavn løses mot en komplett, autorisert Building-gruppering gjennom eksisterende MCP, uten å velge fra en tilfeldig listeside. Eksakt navn uten hensyn til store/små bokstaver må gi én gyldig lokal bygg-ID. Null eller flere treff krever avklaring; en gruppegrense gir feil, aldri et vilkårlig valg. Navnebindingen kontrolleres igjen ved separat preview/lagring. Bare bygg som forekommer i autoriserte avtaler kan oppdages på denne måten.

### Midlertidig tilstand og kildeavhengigheter

AgentReportDraft v1 har servergenerert Id/Revision/TargetPageId, PageDefinition, modellens hensiktsforslag, forventet personlig listerevisjon og lagringsstatus. Én gjeldende definisjon beholdes per samtale, bundet til eksisterende bruker/konto/forbindelse og absolutte 30 minutters levetid. Det lagres ingen previewresultater i samtalen eller databasen. Rapportvisningen i chat og Mine sider deler `report-renderer.js` og den faktiske PersonalPageRunner; ingen modellrunde brukes til å tegne resultatene.

Hvis modellen først leste avtaledata før forslaget, beholder utkastet disse konkrete kildeavhengighetene med verktøyversjon, argumenter og fingeravtrykk. De revalideres før modellgjenbruk, preview og lagring, også når vanlig turhistorikk er trimmet. Endret/utilgjengelig avhengighet hindrer videre bruk; endrede avledede utkast fjernes fremfor å bli modellkontekst. 8Bs vanlige transitive historikk-/revokeringsregler består. Previewkallene er ikke modellinput og blir ikke lagt i historikken bare for rendering. Brukerhensikt, validert definisjon, kortlevd preview og lagringsstatus er atskilt.

Et vellykket endringsforslag får ny revisjon først etter gyldig, fersk preview og aktuell lease. Et ugyldig/avbrutt endringsforslag beholder siste gyldige utkast. GUI fjerner gamle previewdata og krever ny preview før lagring etter feil; kilde-/tilgangsugyldiggjøring fjerner utkastvisningen. Ny samtale, sletting, utløp, konto-/bruker-/tilkoblingsbytte fjerner relevant tilstand og kansellerer arbeid. Avbryt beholder siste vellykkede definisjon; sene svar kan ikke committes eller erstatte nyere GUI-tilstand.

### Eksplisitt lagring og grenser

`/agent/report-save` er en separat cookie-/antiforgery-beskyttet handling. Klienten sender bare samtale-ID, utkast-ID og presentert revisjon. Serverens versjon brukes; ingen ny klientdefinisjon godtas. Fersk personlig tilgang, kildebinding, eventuelle ressursavhengigheter og rapportkjøring kontrolleres før samme 9A-brukstilfelle persisterer den eksakte definisjonen. Lagring kaller ingen modell og trenger ingen bekreftende modelltekst. Den kan fullføres med modellen deaktivert så lenge den allerede autoriserte samtalen/utkastet fortsatt finnes.

En servergenerert, stabil opprettings-ID og 9As konto-/eieravgrensede transaksjon/revisjon hindrer duplikater ved gjentatt klikk eller ved tapt svar etter commit. Samtidige samtaleoperasjoner avvises som opptatt. Et gjenkjent, allerede opprettet resultat krever fortsatt fersk autorisasjon; endret definisjon eller listerevisjon medfører konflikt uten blind retry. Slettes siden senere, opprettes den ikke stille på nytt med en gammel revisjon. Idempotens er avgrenset til utkastets levetid og den stabile sideidentiteten, ikke et varig chat-/kommandoarkiv.

Eksisterende grenser gjelder fortsatt: 16 KiB forslag/definisjon, tre komponenter, to faste rapportverktøy, 20 sider per bruker/konto, 25 tabellrader per side, 100 grupper og maksimalt 64 KiB per MCP-envelope. Et HTTP-preview er begrenset til 128 KiB. Ett forslag bruker normalt to MCP-forretningskall, eller tre ved byggoppslag, innen agentens eksisterende kall-/resultat-/modell-/tidsbudsjett; forslagshandlingen teller også som ett kall. Modellinput og output har fortsatt de eksisterende grensene.

Det midlertidige utkastet med hensikt/binding/dependenser er begrenset til 32 KiB og to modellavledede kildeavhengigheter. Globalt belastes høyst 4 MiB utkastminne med konservativt `1024 + 4 × JSON-bytes`, inklusive snapshots holdt av aktive/avsluttende leases. Fire samtaler per bruker, 1000 samtaler/leases og eksisterende per-bruker-samtidighet beholdes. For mange avhengigheter krever en enklere/ny samtale. Separat preview/lagring revaliderer høyst to avhengigheter før og etter rapportlesingen og gjør høyst sju MCP-forretningskall med byggoppslag, under samme HTTP-tidsgrense; rapportkjøreren har fortsatt eget 40-sekundersvern. MCP-leseflatens 60 HTTP-forespørsler/minutt er uendret og kan nås før forretningskallbudsjettet fordi protokollinitialisering også teller.

Ingen personal_pages_save/delete/move er åpnet som automatiske modellverktøy. Administrasjons-MCP og eksisterende leseflates SELECT-rolle/skrivevern er uendret. 9B innfører ikke ny OAuth-klient, serviceidentitet, deling, eksport, bakgrunnsoppdatering, varig chat eller flerinstansstøtte. Aktivering krever fortsatt kontrollert 9A-migrering og faktisk nettleserprøve; dette oppdraget anvender ingen migrering mot utvikling/produksjon.

### Feilsøking av modellkall ved rapportønske

En feil i første 9B-versjon eksporterte rapportverktøyets argumentrot som `type: ["object", "null"]`. Dette kunne avvise modellforespørselen før et forslag ble laget og vises som ModelUnavailable. Eksporten er rettet til objektrot; lokal validering og nullable underfelter beholdes. Syntetisk HTTP-regresjon avviser den tidligere formen og prøver faktisk modelladapter. Kjør bare denne testen uten database med `dotnet run --project tests/TenantPlatform.McpClient.SmokeTests -- --model-protocol-only`. Etter rettelsen bestod også solution-build og hele MCP-klientsuiten med isolert PostgreSQL (9B/9A/agent/register/OAuth); testklyngen ble fjernet. Dette bekrefter lokal protokollform og regresjoner, ikke godkjenning fra en ekte modellleverandør. Se [oppfølgingsstatus](modernization-plan.md#324-oppfølging-ugyldig-rot-i-modellens-rapportverktøyskjema).

Ved fritekstsvar om at rapportutkast ikke kan opprettes: adapteren sender nå eksplisitt aktuell kapabilitet som betrodd instruksjon, basert på faktisk Application-levert forslagshandling. Tidligere assistentavvisninger skal ikke overstyre dette. Handlingen krever fortsatt egen kilde med både agreements_query og agreements_aggregate tilgjengelig; teksten er ingen ny fullmakt. «Avtaler for juli–september» krever avklaring mellom sluttdato og aktive avtaler i perioden. Aktiv-i-periode er ikke støttet av dagens rapportkontrakt. Se [oppfølgingen](modernization-plan.md#325-oppfølging-modellen-avviser-tilgjengelig-rapportutkast). Etter endringen bestod solution-build, den separate protokolltesten og hele MCP-klientsuiten (agent/9B/9A/register/OAuth) mot isolert PostgreSQL; klyngen ble fjernet. Ekte modellatferd må fortsatt prøves separat.

Et komplett støttet prøveønske er: «Lag en rapportside som heter Mine kontrakter, med avtaler som utløper de neste tre månedene, antall, diagram per bygg og en paginert avtaletabell.» Periodefri rapport og bare tabell inngår fortsatt ikke i kontrakten; agenten skal avklare eller tilby et støttet alternativ.

### Automatisert verifikasjon av 9B

Solution-build og samlet regresjonskjøring av McpClient, Mcp, OAuth, AgreementList, Agreements, AccountRegisters og Dashboard bestod med exitkode 0. Alle sju testprosjektene ble bygget; de seks første suitene brukte isolert PostgreSQL og Dashboard sitt eksisterende InMemory-/SQL-/komponentoppsett. Klyngen ble stoppet og fjernet. Eksisterende NU1902/NU1903 består; ingen nye pakker, pakkeoppgraderinger eller migreringer inngår i 9B. Etter siste justering av tilkoblingsfeilstatus bestod ny solution-build og `bash scripts/test-agreements-postgres.sh TenantPlatform.McpClient.SmokeTests` også med exitkode 0; den ekstra klyngen ble stoppet og fjernet. Denne kjøringen var før skjemarettelsen dokumentert nedenfor.

AgentReportChecks bruker produksjonens modelladapter mot syntetiske Responses-svar og reelle OAuth-/MCP-biblioteker. Naturlige eksempelmeldinger gir faktisk PageDefinition v1 og MCP-preview. Oppfølging, fast/relativ periode, tittel/status, tomt utkastlager før klikk, «lagre den» uten write, cookie-/antiforgery-lagring, faktisk lenke, eksakt revisjon, doble/gjentatte klikk og listerevisjonskonflikt er prøvd. Ukjente/dupliserte felt, kode-/layout-/komponent-/periodefeil, forsøk på konto/eier/kilde/versjon, oppdiktet/tvetydig/entydig bygg, tilbakekalling, isolasjon, avbrudd/sene svar og slettet samtale inngår. Endret ressursgrunnlag etter historikktrimming hindrer lagring; endring under modellens aktuelle forslag hindrer publisering. Lagret side åpnes uten modell/ReadAgent, og eksisterende lesechat, manuelle sider og leserollens skrivevern er beholdt.

Alle fire Node-kontroller for agent/rapportsider bestod, sammen med syntakskontroll av de tre GUI-scriptene, like/unike 19 nye språkressurser og diffkontroll. Dette er faktisk adapterkode med syntetisk DOM/transport, ikke en nettleser. Ingen ekte modellkall er utført. Virkelig naturlig språkforståelse, modellvalg i miljøet, alle språk, tastatur/skjermleser, flerøktsoverløp og brukerens fulle nettleserforløp må prøves separat etter kontrollert 9A-migrering. Se også [samlet 9B-status](modernization-plan.md#32-steg-9b-rapportutkast-fra-naturlig-språk-i-chatten).

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

## 9C: redigering av lagrede sider gjennom samtale

9C viderefører samme modelladapter, AgentReportDraft, PageDefinition v1, PersonalPageService og PersonalPageRunner. Ingen ny migrering, datakilde, automatisk skriveagent eller utvidet rapportkontrakt er innført. Se [samlet implementasjons- og verifikasjonsstatus](modernization-plan.md#33-steg-9c-redigering-av-lagrede-personlige-rapportsider).

### Brukerforløp

1. Åpne en eksisterende personlig side og velg **Endre med AI**. Lenken åpner `/agent?editPage=<id>`. Sidenavnet hentes autorisert på serveren; URL-parameteren er bare et ressursvalg.
2. Bekreft eksisterende datadelingsopplysning og start redigeringen, eller send første spørsmål. Backend åpner en **ny samtale** med sidens verifiserte datatilkobling og kjører faktisk MCP-preview. Ingen gammel chats data/historikk sendes til en annen forbindelse. GET/SSR oppretter ingen samtale, skriver ingen definisjon og gjør ingen modell-/forretningsdatakall.
3. Be for eksempel om «Vis de neste seks månedene i stedet for tre», «Ta bare med aktive avtaler», «Endre tittelen til Kommende avtaleutløp» eller «Flytt avtaletabellen over diagrammet». Modellen får samme begrensede hensiktsrepresentasjon som i 9B, med opplysning om at backend har åpnet en redigeringskontekst.
4. Chatten viser hvilken original som redigeres, backendberegnet før/etter-oversikt og faktisk forhåndsvisning. Tekst fra modellen er ikke bevis på endring eller lagring. Originalen forblir urørt.
5. **Lagre endringer** oppdaterer originalen. **Lagre som ny side** oppretter én personlig kopi. **Forkast endringer** fjerner hele redigeringssamtalen uten databaseendring. En chatmelding, også «lagre», utfører ingen av disse writes. Etter lagring vises egen status for oppdatering/kopi og lenke basert på backendbekreftet side-ID. Åpning av siden/Mine sider henter aktuell definisjon på nytt. Etter bekreftet lagring åpnes en ny redigeringsøkt via Endre med AI for flere endringer; en fullført utkastrevisjon blir ikke automatisk en ny original.

Vanlig manuell redigering og kjøring fra Mine sider fungerer med ReadAgent deaktivert og uten modellnøkkel. AI-lenken vises når modellen er driftskonfigurert; manglende kilde/verktøy/OAuth hindrer åpning av redigeringskonteksten med en sikker feil. Ingen automatisk oppgradering eller godkjenning skjer.

### Binding, endringer og støttet omfang

AgentReportDraft beholder en AgentReportEdit med opprinnelig side-ID og faktisk originaldefinisjon, i tillegg til verifisert aktør/konto/tilkobling i samtalen, opprinnelig listerevisjon, aktuell utkastrevisjon og separat servergenerert kopi-ID. Følgeforslag beholder **opprinnelig** listerevisjon; en vellykket preview oppdaterer aldri denne til en nyere lagringsrevisjon.

Modellen får ikke side-/kopi-ID, eier, lagringsrevisjon, profil- eller verktøyversjoner som redigerbare felt. Den kan ikke velge en eksisterende side gjennom en fritekst-ID eller et navn; siden må åpnes eksplisitt fra den autoriserte flaten. Tittel, kildeetiketter, gammel modelltekst og lagret hensikt behandles som ubetrodd innhold. personal_pages_save/delete/move er fortsatt utelatt fra automatisk agentkatalog.

Før/etter-oversikten beregnes i Application fra original og foreslått faktisk definisjon: tittel, periode, søk, status, type, arkiv, bygg/manglende bygg og komponentrekkefølge. Web lokaliserer bare presentasjonen. Modellens forklaring påvirker ikke oversikten. Relative måneder beholdes som relativ hensikt, og de faktiske normaliserte datoene vises av samme runner som før.

De tre komponentene Metric, BuildingChart og AgreementTable, Stacked og fast periode/NextMonths 1–12 gjelder fortsatt. «Avtaler for en periode» krever avklaring mellom EndDate og aktive avtaler gjennom perioden; den siste beregningen er ikke implementert. Lagrede enhets-, organisasjons- og geografireferanser beholdes på serveren og sendes ikke som valgbare modell-ID-er. Eksisterende byggreferanse beholdes når forslaget ikke erstatter den med et entydig verifisert byggnavn eller MissingBuilding. Fjerning/endring av øvrige lagrede referansefiltre krever den eksisterende manuelle redigeringen. Modellen instrueres om disse grensene; backend håndhever dem uavhengig av modelltekst.

### Revisjon, idempotens og ny original

`/agent/report-edit`, `report-update`, `report-copy`, `report-discard` og `report-reload` bruker cookieidentitet, fersk Application-autorisasjon og antiforgery. Mutasjoner tar bare samtale-/utkast-ID og presentert utkastrevisjon; original, definisjon og forventet lagringsrevisjon kommer fra serverens kontekst. Oppdatering/kopi må stemme med faktisk konteksttype. Ukjente identitets-/definisjonsfelt i HTTP-body avvises.

Oppdatering går gjennom PersonalPageService.UpdateFromDraftAsync og samme PersistAsync/validering/SQL-transaksjonsport som eksisterende GUI/MCP-administrasjon. Den sjekker original, eier-/kontobundet rad og forventet listerevisjon. Den servergenererte utkastrevisjonen brukes som stabil commit-revisjon i eksisterende samlingsrad. Et forsøk etter tapt svar kan bare gjenkjennes som samme oppdatering dersom **både** denne commit-revisjonen og nøyaktig definisjon stemmer. Et urelatert identisk innhold med en annen revisjon gir konflikt. Ingen ekstra tabell, varig kommandoarkiv, automatisk merge eller blind retry er lagt til.

Kopi bruker samme CreateFromDraftAsync som 9B, stabil server-ID, gjeldende kildeautorisasjon og 9As maksimalt 20 sider. Gjentatte forespørsler oppretter ikke flere kopier. Etter bekreftet lagring kan samme handling bekreftes på nytt med fersk autorisasjon og uendret lagret definisjon; bytte mellom oppdatering og kopi på en ferdig lagret revisjon avvises. En slettet side gjenopprettes ikke fra gammel kontekst.

Endret side **eller annen endring i personlig sidelistens revisjon** gir HTTP 409 ved lagring. Utkastet bevares, men GUI fjerner gamle previewtall. **Last inn aktuell versjon på nytt** krever en synlig bekreftelse om at utkastet forkastes. Backend lager da en ny samtale fra den aktuelle lagrede originalen og erstatter gammel kontekst først etter vellykket fersk preview. Ingen gammel endring anvendes automatisk. Feilet reload beholder serverutkastet; ved tilgangs-/kildeugyldiggjøring tømmes beskyttet GUI-innhold. Endret datatilkobling krever ny åpning fra siden med nytt bekreftet kontekstvalg.

Ved tapt lagringssvar loves ikke suksess i GUI: klienten viser feil inntil en autorisert gjentakelse eller ny innlasting bekrefter resultatet. Samtidige operasjoner bruker eksisterende lease/kvote. Sene svar kan ikke erstatte nyere utkast; sent returnerte nye samtaler fjernes av adapteren.

### Tilgang og grenser

Fersk personlig side-, konto-, medlemskaps-, verktøy-/versjons- og OAuth-tilgang kreves ved åpning, preview og lagring. Eksisterende original er ingen fullmakt til data. De to faktiske forretningskallene går fortsatt bare gjennom McpRegistryService og målserverens Application-lesere. Ingen SQL-/GUI-/legacyfallback for rapportdata. Leseflatens SELECT-rolle, constraints, historikk-/kildeavhengighetsvern og oppstarts-/driftsavgrensning er beholdt.

Eksisterende levetid 30 minutter, fire samtaler per bruker, 1000 samtaler/leases, én aktiv agentoperasjon per bruker og tids-/transport-/modellgrenser gjelder. Et nytt 9B-utkast er fortsatt høyst 32 KiB. Et redigeringsutkast tillates høyst **64 KiB**, fordi originaldefinisjonen også beholdes; hver faktisk definisjon er fortsatt høyst 16 KiB. Det globale konservativt belastede utkastminnet er fortsatt **4 MiB**, inklusive aktive lease-snapshots. Ingen original eller preview legges i varig chathistorikk. Modellrepresentasjonen inneholder bare begrenset hensikt; hele originalen sendes ikke til modellen.

Konto-/brukerbytte, logout, ny samtale, forkasting og utløp fjerner relevant kontekst og kansellerer aktivt arbeid. Manglende side eller lokal tilgang kan ikke levere gammel preview. Feil/avbrudd under forslag bevarer siste gyldige utkast. Dette er fortsatt én-instansdrift uten atomisk tilbakekalling eller konsistent snapshot mellom flere MCP-kall. Generell Web-cookie-/sesjonsrevokering er en separat oppgave.

### Verifikasjon og manuell kontroll

Før 9C ble 9Bs komplette MCP-klientsuite kjørt mot isolert PostgreSQL og alle fire eksisterende DOM-/transportkontroller bestod. 9C testes med syntetiske Responses-svar, faktisk cookie/CSRF, OAuth/MCP og isolert PostgreSQL. Etter siste produksjonsendring bestod solution-build og de komplette MCP-klient-, MCP-server- og OAuth-suitene mot isolert PostgreSQL. En etterfølgende målrettet 9C-kjøring med de siste injeksjonsforsøkene bestod også, uten mellomliggende produksjonsendring. Begge klynger ble stoppet og fjernet. De fem faktiske JS-adapterkontrollene bestod med syntetisk DOM/transport, og 21 nye språkressurser er like/unike på alle tre språk. Eksisterende NU1902/NU1903 består. Dette er ikke faktisk modell-/nettleserprøve. Se moderniseringsplanen for eksakte kommandoer og kontroller.

Manuell kontroll etter kontrollert driftsaktivering:

1. Åpne en lagret side, velg Endre med AI, bekreft datadeling og prøv de fire eksempelendringene. Kontroller originalnavn, komponentrekkefølge og lokaliserte før/etter-verdier mot faktisk preview.
2. Kontroller at originalen er uendret før knappetrykk. Prøv oppdatering, kopi og forkasting hver for seg. Kontroller Mine sider og faktiske lenker etterpå.
3. Endre samme side eller en annen personlig side i en annen fane. Lagring skal gi konflikt; utkastet skal ikke anvendes på ny original. Avbryt reloadbekreftelsen, og bekreft den deretter for å åpne fersk original.
4. Prøv tapt nettverkssvar, dobbelt klikk, avbrudd, sene svar, kontobytte/logout, fjernet side og tilbakekalt tilgang. Ikke vis gamle tall som ferske.
5. Prøv norsk, engelsk og svensk, tastatur/skjermleser og flere faner. Åpne oppdatert side uten ReadAgent/modellnøkkel. Dette er ikke dekket av syntetisk DOM alene.

Ingen ekte/betalte modellkall, utviklings-/produksjonsdatabase, driftsmigrering, vanlig Web-oppstart, containerbygg, deploy, commit, push eller Serga-endring inngår. Container/deployment i 8C er fortsatt utsatt.

## 9D: valgfrie komponenter, gruppering og kolonner

PageDefinition **v2** støtter ett til tre elementer i Components, uten duplikater: Metric, BuildingChart og AgreementTable. Rekkefølgen er visningsrekkefølgen; Layout er fortsatt Stacked. Navnet BuildingChart beholdes i kontrakten av kompatibilitetshensyn, men GroupBy kan nå være Building eller Status. Alle komponenter deler samme periode og filter. Ingen ny sortering, fri layout, flerkildekombinasjon, økonomi, uttrykk, deling eller eksport inngår.

Columns er en ordnet enumliste uten duplikater eller ukjente verdier, maksimalt åtte. Når AgreementTable er valgt, kreves minst én kolonne. Uten tabell kan listen være tom eller beholde tidligere kolonnevalg til senere redigering. Tillatte kolonner peker på eksisterende felt i agreements_query:

| Kolonne | MCP-felt og betydning |
|---|---|
| Title | title, lagret avtaletittel |
| Type | type, lagret avtaletype; lokaliseres i visningen |
| Status | status, lagret status; lokaliseres i visningen |
| Building | building?.name, kontobegrenset byggreferanse; manglende vises med eksisterende mangler-etikett |
| StartDate | startDate, lagret startdato |
| EndDate | endDate, lagret sluttdato; fortsatt feltet perioden filtrerer på |
| NoticeDeadline | noticeDeadline, lagret oppsigelsesfrist |
| RenewalDate | renewalDate, lagret fornyelsesdato |

Datoene er eksisterende kontraktfelter, uten ny utledning av aktiv-i-periode eller oppfølgingsfrister. Manglende valgfrie datoer vises som «—». MCP-kontraktens faste Title/ID-sortering, sidestørrelse 25 og sidegrense 10000 er uendret. Ingen eier-/personmetadata tilføyes. Renderer bruker en fast feltliste og textContent; ingen vilkårlige objektnøkler, HTML eller kode evalueres.

### Eldre sider og uttrykkelig lagring

PageDefinitions.Current validerer v1 før eksplisitt konvertering i minnet: samme komponentrekkefølge og alle tre komponenter, Building-gruppering, kolonnene Title → Building → EndDate. Filter, periode, kilde og eksakte profil-/verktøyversjoner beholdes. Ukjent definisjonsversjon gir Version-feil med forståelig GUI-tekst, også ved list/read og en mutasjon som gjelder en annen side i samme samling. Hele JSON-samlingen avvises før dagens serializer kan skrive om ukjente fremtidige felt. Ingen databaseoppdatering skjer ved list/read/run eller åpning av en AI-redigering. En eldre JSON-definisjon trenger ikke de nye feltene.

Ved redigering beholdes den rå lagrede originalen separat til revisjonskontroll. Endringsoversikten sammenligner konverterte betydninger, så formatkonverteringen alene lager ingen falsk innholdsendring. Bare eksplisitt manuell lagring, AI-lagringsknappen eller et autorisert administrasjons-save persisterer v2, under samme transaksjon og revisjonsvern. Ingen migrering eller masseskriving trengs. Rekkefølge/sletting av sider omskriver ikke deres definisjonsversjoner. Den separate administrasjons-MCP-flaten annonserer nå v2 i sitt genererte save-skjema og sine beskrivelser; en eventuell registrert administrasjonsklient må håndtere endrede metadata gjennom vanlig eksplisitt oppdagelse/godkjenning. Ingen godkjenning oppgraderes automatisk. Leseverktøyenes metadata/kontrakter er uendret.

### Minimal MCP-plan og tilgang

Application utleder planen fra den validerte definisjonen:

| Komponentsett | Forretningskall per kjøring |
|---|---|
| Bare Metric, bare diagram, Metric + diagram | Ett agreements_aggregate |
| Bare tabell | Ett agreements_query |
| Tabell + Metric og/eller diagram | Ett agreements_aggregate, deretter ett agreements_query |

Metric og diagram deler det samme komplette aggregatet, med unik total og komplette, ikke-overlappende grupper. Status-grupper bruker eksisterende enum-nøkler; Building beholder missing-gruppen for manglende/ugyldig kontoreferanse. Serverresultatet angir faktisk kjørt Components, GroupBy og Columns; en eldre komponentliste fra SSR i en allerede åpen fane overstyrer ikke den nye kjøringsplanen. Manglende tabell/aggregat returneres som null, aldri konstruerte nulltall. Ingen listesidesummering eller skjult henting av tabellrader gjøres for en ren graf-/nøkkeltallside.

Første faktiske verktøy normaliserer relativ periode etter målserverens kontodato/tidssone. Når to kall brukes, sendes aggregatets konkrete filter/datoer til query. En ren tabell lar query normalisere perioden. Application verifiserer resultatets filter/kontrakt mot hensikten; nettleseren beregner ingen kalender. En kjøring gir fortsatt ingen konsistent snapshotgaranti: snapshotConsistent=false, og forskjeller mellom aggregat og liste er mulige ved samtidige endringer. Paging og complete/hasMore/limitReached vises uendret og ærlig.

Egen profil, personlig OAuth, lokal bruksrett og profilversjon kontrolleres fortsatt. Bare verktøy som planen bruker må være godkjent AutomaticRead og ha nøyaktig lagret versjon. En metric-side kan derfor kjøre uten query-godkjenning, mens en tabellside kan kjøre uten aggregate-godkjenning. Kilden må ha minst ett av disse tilgjengelig. Tidligere bindinger bevares også når verktøyet midlertidig ikke brukes; å legge komponenten tilbake oppgraderer ikke binding eller godkjenning. Manglende/utdatert binding gir feil og krever et eksplisitt kildevalg/lagring gjennom manuell konfigurering. Ingen SQL-/Application-lesefallback innføres.

**Separat navneoppslag i chat:** et foreslått byggnavn trenger i tillegg en godkjent, versjonsbundet Building-aggregate for entydig navneløsning og senere kontroll. Dette gjelder også et tabellutkast med byggnavn. Selve lagrede kjøringsplanen bruker bare de valgte komponentenes verktøy; en allerede lagret bygg-ID trenger ikke nytt navneoppslag. Modellavledede kildeavhengigheter beholder sine egne revalideringskall og budsjetter.

Eksisterende 40-sekunders kjøregrense, samtidighet, 64 KiB per MCP-resultat, 128 KiB GUI-respons, 100 grupper, kontroller av kilde/tilgang før og etter kjøring samt SELECT-rolle/SaveChanges-vakt beholdes. Ingen modellkall, resultatcache eller bakgrunnsoppdatering i lagret rapportkjøring.

### Oppretting og redigering

Manuell konfigurering viser av/på og posisjon for hver komponent, Building/Status-gruppering samt av/på og posisjon for hver tillatt kolonne. Like posisjoner for aktive valg avvises. Dette er visningsrekkefølge, ikke listesortering.

propose_report_page bruker de samme enumene og Application-valideringen. Forslaget er fortsatt bare hensikt; backend eier identiteter, kilde-/verktøybindinger og revisjoner. Et nytt «Lag bare et diagram …» gjenkjennes også uten ordet rapport/side. Modellen får hele gjeldende hensikt og skal bevare valg brukeren ikke endrer. Utelatt/null gruppering og kolonneliste normaliseres til gjeldende utkastvalg, eller Building og eldre standardkolonner for et nytt utkast. En eksplisitt tom kolonneliste godtas bare uten tabell. Ukjente/dupliserte verdier avvises, uten alternativ rapport eller skjult lagring. Funksjonsskjema og Responses strict=false med obligatorisk lokal validering følger fortsatt [offisiell OpenAI-dokumentasjon](https://developers.openai.com/api/docs/guides/function-calling).

Backend beregner endringer i komponenter/rekkefølge, gruppering og kolonner/rekkefølge i tillegg til eksisterende felt. Preview viser den valgte presentasjonen. Lagre, oppdatere original og lagre kopi er fortsatt separate brukerhandlinger; konflikt beholder hensikten og overskriver ikke siden.

### Verifikasjon og manuell kontroll

24. oktober 2026 bestod solution-build, målrettet 9D-suite, hele MCPClient-/MCP-/OAuth-suitene mot isolert PostgreSQL og seks GUI-adapterkontroller etter siste produksjonsendring. Alle sju komponentkombinasjoner, faktiske kallantall, eldre/fremtidige versjoner, prompt→utkast→eksplisitt lagring, konflikt og kjøring med AI deaktivert inngår. Klyngene ble stoppet og fjernet. Eksisterende NU1902/NU1903 består; ingen nye kompilatorvarsler. Se [detaljert verifikasjon](modernization-plan.md#343-verifikasjon-og-manuell-rest). Testene bruker syntetisk Responses-transport, faktiske OAuth-/MCP-biblioteker og isolert PostgreSQL. Ingen ekte eller betalte modellkall, vanlig Web-start eller utviklings-/produksjonsdatabase inngår.

Manuelle prøver som gjenstår:

1. Opprett en diagramrapport med aktiv-status og eksplisitt periode fra chat. Kontroller at bare diagrammet vises og ingenting lagres før klikk.
2. Åpne en eldre side og rediger med AI: fjern tabellen, bytt til Status, legg til Title/Building/Status/EndDate og flytt Metric nederst. Kontroller preview, endringsoversikt og separat original-/kopilagring.
3. Gjenta samme valg manuelt, inkludert bare tabell og bare Metric; kontroller kolonnerekkefølge, lokaliserte type-/statusetiketter, datoer og sidebytte.
4. Endre samme side i en annen fane; bekreft konflikt uten overskriving. Fjern nødvendig verktøygodkjenning/OAuth eller medlemskap; ingen gammel preview eller falske nulltall skal bli stående.
5. Deaktiver AI og åpne lagrede v1/v2-sider med personlig MCP. Ingen modellnøkkel skal kreves. Konto-/brukerbytte og avbrudd skal fortsatt tømme gamle resultater.

Reell modellforståelse og faktisk nettleser-/TLS-bruk er ikke bevist av syntetiske kontroller. Containerbygg/deployment (8C) er fortsatt utsatt.


### Oppfølging 25. oktober 2026: uten sluttdatobegrensning

V2 har nå også Period.Kind=All med Months/From/To=null. Det sender verken endsWithinMonths, endDateFrom eller endDateTo til MCP, og omfatter derfor også avtaler med null sluttdato. All er eksplisitt hensikt, ikke en kunstig bred datoperiode. Status=Active er fortsatt lagret status, og Archive=Exclude er et separat filter. En eldre v1-definisjon kan ikke angi All; dens eksisterende betydning bevares. NextMonths og Fixed beholder tidligere regler. Ingen MCP-/databaseskjemaendring er nødvendig.

Manuell editor tilbyr «Uavhengig av sluttdato», agentens forslagsskjema/instruksjoner støtter All, og preview viser den lokaliserte etiketten. Samme filter brukes ved kombinerte aggregate-/query-kall og ved senere åpning. Original/kopi og eksplisitt lagring følger samme vern som ellers.

Brukerens eksempel «Lag en side som heter Aktive avtaler per bygg … uavhengig av sluttdato … utelat arkiverte … ikke nøkkeltall eller avtaletabell» var ikke representerbart før denne utvidelsen. Det alene fastslår **ikke** hvorfor den konkrete kjøringen gav ModelUnavailable: den gamle meldingen dekket både HTTP-feil og andre problemer. Reelle modellkall er ikke gjenskapt her.

Modelladapteren skiller nå trygt mellom HTTP 400/422 (forespørselen avvist), 401 (API-legitimasjon), 403 (tilgang), 404 (ressurs/modell), 429 (kvote/frekvensgrense), tilkoblingsfeil og uventet svarformat. Øvrige HTTP-feil beholder sikker utilgjengelig-status. Rå providerbody, prompt, tokens og exceptionmeldinger eksponeres/logges ikke; ingen automatisk retry er lagt til. Dette følger [offisiell OpenAI-feildokumentasjon](https://developers.openai.com/api/docs/guides/error-codes). En feilklasse peker på hva som må undersøkes, uten å påstå at alle slike feil er løst ved All.

Etter ny oppstart/rebuild må det konkrete spørsmålet prøves i en **ny samtale**, med eksplisitt preview før lagring. Faktisk modell- og nettleserprøve gjenstår. Solution-build, seks GUI-adapterkontroller, separat syntetisk modellprotokoll og målrettet MCPClient-suite med isolert PostgreSQL bestod 25. oktober. Den konkrete teksten, null/gammel/fremtidig sluttdato, arkiv, eksplisitt lagring, gjenåpning og både kombinert og tabellren All-plan inngår. Testklyngen ble fjernet. Se moderniseringsplanens avsnitt 34.4 for presist omfang; dette er ikke en ny full kjøring av alle suiter.


### Oppfølging 25. oktober 2026: synlige stolpediagrammer

Den tidligere chart-rendereren viste grupper i en talltabell med nettleserens native meter-felt. Den er erstattet med et tydelig horisontalt stolpediagram for både Building og Status, felles for chatpreview og lagrede sider. Etikett og eksakt antall står ved hver stolpe; alle rader deler samme kolonnebredder og skala fra null til største gruppetall. Stolpelengde viser antall, ikke prosent av totalen. Null gir null fylt bredde. Chart-komponenten lager ikke lenger en synlig gruppetabell; en separat AgreementTable vises bare dersom den er valgt i definisjonen.

Lokal CSS gir responsive stolper også uten Bootstrap-lastingen. På smale beholdere ligger etiketten over stolpen; tallene forblir synlige. Tekst lages fortsatt med textContent, og dekorative spor skjules for hjelpeteknologi mens etikett/antall er tilgjengelig tekst. Ingen ny pakke, nye datakall, modellkall eller definisjonsendring inngår. Eksisterende sider trenger ingen ny lagring. Last nettlesersiden helt på nytt for å erstatte den gamle JS-/CSS-visningen i en åpen fane.

Solution-build og sju Node-adapterkontroller bestod. Syntetisk headless Chrome-visning er visuelt kontrollert på bred skjerm og med 320 px beholder; geometri bekrefter samme skala for 10/5/0 og synlige tall uten horisontal klipping. Dette er ikke en interaktiv helsideprøve mot Web eller en skjermlesertest. Ingen utviklings-/produksjonsdatabase eller ekstern modell ble brukt.

## 9E: tjenesteforespørsler

Et eksplisitt rapportområde ServiceRequests med v3-definisjon utvider nå den samme rapport-/utkastkjeden. Avtalenes v1/v2 betyr fortsatt Agreements, også når Area mangler. [Ny kontrakt, aktivering, SELECT, prompter og separat teststatus](mcp-service-request-read-contracts.md) beskriver det avgrensede området. Tidligere rapporttester er historikk; brukerens sist fullførte 9D-prøve bevares. Nye 9E-kontroller dokumenteres separat i moderniseringsplanen avsnitt 35.
