# MCP-tilkoblingsregister (6A og steg 7)

6A bygger videre på [5B3](internal-mcp-client-operations.md). Web har et minimalt, statisk serverrendret register på `/mcp-client/registry`, lenket fra `/mcp-client`. Ingen chat, modell, rapportmotor eller konsoll for vilkårlige verktøykall er lagt til. Egen MCP-host eksponerer fortsatt nøyaktig tre verktøy. Funksjonen krever eksplisitt aktivering; [konfigurasjonsmalen](../../docker/web/mcp-registry.example.json) lastes ikke automatisk.

## Steg 7: tilgang og tilgjengelig verktøykilde

Steg 7 er implementert og automatisert verifisert lokalt 17. oktober 2026 og bygger videre på samme register, OAuth-klient, krypterte credentials og katalog. Reglene i dette avsnittet har forrang fremfor den historiske 6A-beskrivelsen nedenfor. Ingen agent, rapportmotor, modellkall, egendefinerte roller eller bekreftelsesflyt er implementert.

### Seks uavhengige nivåer

1. Felles `mcp-connections.manage` krever fortsatt kontoomfattende AccountAdmin og medlemskap. Det gir rett til å konfigurere, ikke automatisk bruke en forbindelse eller et verktøy.
2. `mcp-connections.use` er generell inngangsrett. I tillegg må tilkoblingens lokale brukerutvalg tillate innringeren.
3. Verktøyet må tillate den samme innringeren. Det kan arve tilkoblingens brukerutvalg eller begrense det ytterligere; det kan aldri utvide det.
4. Den aktuelle metadataversjonen må være godkjent av administrator, med eksplisitt lokal bruksklasse.
5. Brukeren må ha sin egen aktive personlige autorisering for den aktuelle definisjonen og profilen.
6. Målserveren håndhever fortsatt egne data-/handlingsrettigheter. Lokal katalog er ikke bevis for at en eksternt tilbakekalt grant eller målserverrettighet fortsatt gjelder; avvisning kan først komme ved tokenbruk/refresh.

Application `McpRegistryService` og den rene `McpAccessPolicy` eier disse lokale beslutningene. Infrastruktur leser rå, kontoavgrensede medlems-/rollefakta og lagrer Application-bestemte policyer. Ingen ny rollemodell eller separat rettighetsevaluator er lagt i Infrastructure. Fersk bruker, medlemskap, generell bruksrettighet, definisjon, profil, brukerutvalg, verktøyversjon, godkjenning og bruksklasse kontrolleres på nytt ved bruk, også etter venting/refresh. UI-skjuling er ikke autorisasjon.

### Brukerutvalg og omfang

| Tilkoblingspolicy | Betydning |
|---|---|
| Closed | Ingen bruksadgang. Standard for nye definisjoner. Administrasjon er fortsatt mulig for autorisert kontoadministrator. |
| AccountRoles | Eksplisitt valgt kompatibilitets-/breddepolicy: kontoens brukere med generell MCP-bruksrettighet. |
| Selected | Union av uttrykkelig valgte lokale brukere eller eksisterende rolletildelinger i aktuell konto. Tom liste gir ingen tilgang. |

Verktøy har Closed, Inherit eller Selected. Inherit bruker akkurat tilkoblingens brukerutvalg. Selected krever **både** tilkoblingsadgang **og** treff i verktøyets eget utvalg. Det er ingen generell deny-motor, prioritering mellom regler eller administratoropprettede roller.

En brukerreferanse må ha medlemskap i valgt konto ved lagring. En rolletildeling består av rolle, valgt konto, eventuell organisasjon og eventuell bygning. Et rolletreff må matche én faktisk, komplett tildeling med samme rolle og samme organisasjons-/bygningsomfang. En kontoavgrenset rolletildeling matcher ikke automatisk en snevrere tildeling; ingen rettighet fra én tildeling kombineres med omfang fra en annen. Kontoomfattende roller kan velges fra de eksisterende enumverdiene; scoped rollevalg hentes fra eksisterende, gyldige tildelinger i aktuell konto. Referanser til fremmede scopes/navn filtreres før materialisering. Global PlatformAdmin gir ingen kontobypass.

Ukjente enumverdier, manglende/misformet JSON-policy eller ugyldige tildelinger gir avvisning. Policydata inneholder bare lokale referanser, aldri credentials. Bare administrator får bruker-/rollekandidater og lagrede tilgangsutvalg i GUI. Vanlige brukere ser ikke uautoriserte definisjoner, skjemaer eller andre brukeres personlige status. Å konfigurere en tildeling oppretter ikke medlemskap, rolle, ekstern identitet eller OAuth-grant.

### Katalog for senere konsumenter

`AvailableToolsAsync` returnerer bare lokalt tilgjengelige verktøy for betrodd innringer i valgt konto. Identiteten er `(ConnectionId, Name)`, supplert med godkjent `Version`, forbindelsens navn, beskrivelse, input-/outputskjema og lokal `UseClass`. Ingen token, grant-ID, personlig bruker-/rolleliste eller autorisert scope inngår.

Utvalget krever aktiv definisjon, riktig lastet profil/credential-epoke, fersk lokal tilgang, egen brukbar credential-payload med gjenværende lokal grantlevetid, fullstendig katalog fra siste 24 timer, godkjent metadataversjon og matchende verktøypolicy. Bare AutomaticRead og ConfirmationRequired returneres. Statuslesing kontrollerer også at personlig payload kan dekrypteres med riktig bruker-/profilbinding og inneholder nødvendig legitimasjon. Den verken skriver, fornyer tokens, initialiserer data eller kaller målserveren. Definisjonsrevisjonen kontrolleres igjen før metadata returneres. Fjernserverens grant-/datatilgang må fortsatt håndheves der; en lokal status er ingen online introspection.

Senere konsumenter bruker `CallAsync(connectionId, toolName, version, arguments, cancellationToken)` med versjonen de fikk fra katalogen. Alle kontroller utføres igjen, og den oppgitte metadataversjonen må fortsatt stemme. Et tidligere katalogresultat er ingen fullmakt. 6As kalloverlast uten eksplisitt versjon beholdes for de etablerte avgrensede testene; den håndhever de samme aktuelle policyene og er ingen vei rundt godkjenning eller bruksklasse. Ingen generell HTTP-kjørekonsoll er lagt til.

Guid.Empty-plassen til 5B3 inngår aldri i registerkatalogen. Registerkall med denne ID-en avvises, uten fallback til den gamle klienten. De tre manuelle, typede 5B3-lesetestene beholdes separat og gir ingen katalog-/agentadgang til registrerte definisjoner.

### Lokal bruksklasse og godkjenning

Unclassified er standard for ny/endret metadata og kan ikke brukes. Før positiv versjonsgodkjenning må administrator velge og lagre AutomaticRead eller ConfirmationRequired. Klassevalget avledes aldri fra navn, beskrivelse eller readOnlyHint. Det er en lokal tillitspolicy, ikke teknisk bevis for sideeffektfrihet. Feilklassifisering kan ikke repareres av en generisk MCP-klient.

AutomaticRead tillater den eksisterende automatiske kallinngangen når alle øvrige krav er oppfylt. ConfirmationRequired vises i den tilgjengelige katalogen, men CallAsync avviser med sikker `CONFIRMATION_REQUIRED`. Ingen klientparameter kan omgå dette, og det finnes ingen generell bekreftet skriveinngang i steg 7. Unclassified og ukjente klasser avvises.

Uendret metadataversjon beholder eksplisitt klasse og godkjenning. Endret metadata mister godkjenning og får Unclassified; et eksisterende verktøys snevre brukerutvalg beholdes, så ny oppdagelse ikke utilsiktet utvider det. Nye verktøy arver tilkoblingens brukerutvalg, men mangler klasse og godkjenning. Tilgang, klasse og godkjenning er separate innstillinger. Å fjerne én av dem stopper neste kall uten at andre nivåer endres.

### GUI og drift

På `/mcp-client/registry` kan administrator fortsatt administrere navn, aktivstatus og valgt lastet operatørprofil. Nye forms velger tilkoblingsutvalg, lokale brukere/eksisterende rolletildelinger, verktøyets arv/innsnevring og bruksklasse. Godkjent versjon vises separat. Policyendringer lagres med kontoavgrenset radlås og revisjonsvern og krever ingen restart. Policyforms har 64 KiB bodygrense, høyst 100 tildelinger og 20 KiB lagret policy; MCP-kallargumenter har fortsatt 16 KiB-grensen fra 6A.

Brukeren ser egne tilgjengelige tilkoblinger og personlig status, til-/frakobling og sine tilgjengelige verktøy. Lokaliserte årsaker skiller manglende egen autorisering, deaktivert definisjon, manglende/endret operatørprofil, ufullstendig/foreldet katalog, manglende godkjenning/klasse/tilgang og bekreftelseskrav. Administrator kan se «ingen bruksadgang» for seg selv selv om vedkommende kan administrere. Ingen andres personlige status eller interne protokollfeil vises. Operatørprofiler krever fortsatt konfigurasjon/restart som i 6A; dette løses ikke med en GUI-policyendring.

Migreringen `20261017155801_AddMcpAccessPolicies` er generert gjennom eksisterende migreringsskript for OAuthDbContext. Den legger bare til policy-/klassekolonner i registertabellene. Eksisterende definisjoner får AccountRoles og eksisterende verktøy Inherit med tomme tildelingslister. Bare verktøy som allerede var godkjent i 6A får AutomaticRead; øvrige får Unclassified. Dette bevarer eksisterende utføringsadgang uten å godkjenne nye verktøy eller friske opp gammel katalog/legitimasjon. Nye definisjoner starter Closed. Credentials og den reserverte legacyplassen endres ikke. En nedgradering til 6A mister nye innsnevringer og må ikke behandles som en sikker, automatisk rollback.

Faktisk siste verifikasjon: solution og sju testprosjekter bygget, alle sju suiter bestod, seks med isolert PostgreSQL og Dashboard med eksisterende InMemory-/SQL-/komponentoppsett. Klyngen ble stoppet og fjernet. Direkte Application-policytester, ekte MCP-kall, kompatibilitetsmigrering, SSR-/formkontroller og hele 5B3-/6A-regresjonen inngår. Eksisterende pakkevarsler består. Docker-daemonen er utilgjengelig; faktisk containerbygg er ikke gjennomført i steg 7. Se moderniseringsplanens 26.3 for konkrete kontroller og manuell rest.

Migreringen anvendes fortsatt bare i isolerte tester her. Før senere drift: backup, eksplisitt OAuth-migrering med riktig DDL-identitet og oppdatert runtime. Ingen automatisk OAuth-migrering/seed, driftsaktivering eller endring av MCP-hostens tre verktøy er innført. Faktisk automatisert verifikasjon og manuell rest for steg 7 står i moderniseringsplanens avsnitt 26; 6A-resultatene nedenfor er historiske. Interaktiv GUI på tre språk, containerbygg og virkelig proxy/TLS/egress må fortsatt skilles fra TestServer-/PostgreSQL-tester.

## Modell, kontoeierskap og kompatibilitet

`oauth.mcp_servers` har stabil Guid-ID, AccountId, visningsnavn, godkjent profil-ID/-fingeravtrykk, aktivstatus, konfigurasjons-/katalogrevisjon, separat credential-epoke og resultat av siste oppdagelse. Endepunkt, issuer, resource, scopes og klient velges gjennom en operatørgodkjent profil; GUI har ingen fri URL-/hemmelighetsregistrering. De lastede operatørprofilene er felles for plattformen og kan velges av kontoadministratorer; en separat kontoallowlist per profil inngår ikke. Flere definisjoner kan velge samme profil. Profilen er en autentiserings-/nettverkspolicy, ikke et delt brukergrant eller en datascope.

`oauth.mcp_client_credentials` har nå sammensatt nøkkel `(UserId, ConnectionId)`. Kryptering og profilkontroll binder lokal bruker, tilkoblings-ID, profilfingeravtrykk/epoke, klient-ID, issuer og resource. Legitimasjonen tilhører brukeren; administrator ser aldri andre brukeres tokens eller personlig tilkoblingsstatus. `oauth.mcp_tools` har sammensatt nøkkel `(ConnectionId, Name)`, metadata, versjon, tidspunkt og godkjenning. Like verktøynavn hos ulike servere har dermed ulike identiteter.

**Eksisterende 5B3-forbindelse beholdes separat:** ConnectionId=Guid.Empty og tom profilversjon er en reservert legacyplass, ikke en kontoeid serverdefinisjon. Migreringen beholder bruker, grant, utløp og ciphertext uendret og bruker fortsatt den gamle Data Protection-purpose og låsenøkkelen for akkurat denne plassen. `/mcp-client` beholder sine tre typede lesetester. Den er ikke en snarvei for å bruke eller administrere registerets definisjoner. Deaktivering av en definisjon stopper den definisjonens nye kall; den tilbakekaller ikke andre uavhengige OAuth-grants eller legacyforbindelsen.

En kontoadministrator kan opprette en kontoeid definisjon med den innebygde `tenantplatform`-profilen. Hver bruker autoriserer denne separat. Ingen gammel legitimasjon kopieres til kontoer, tolkes som en ny forbindelse eller gjenbrukes på tvers av definisjoner. Legacygranten kan beholdes eller frakobles av sin eier. En senere avvikling av legacygrensesnittet krever en egen, eksplisitt overgang; 6A avvikler det ikke.

## Application-autorisasjon

Application `McpRegistryService` bruker betrodd aktør og felles `AuthorizationService`. Det finnes to nye systemrettigheter:

| Rettighet | Eksplisitt legacy-mapping |
|---|---|
| `mcp-connections.manage` | AccountAdmin med kontoomfattende tildeling og medlemskap. Organisasjons-/bygningsavgrenset administrator får ikke denne fullmakten. |
| `mcp-connections.use` | Alle kjente UserRole-verdier med en faktisk tildeling i valgt medlemskonto; snevrere rolletildeling gir bare tilkoblingsbruk. |

Aktivt medlemskap alene uten en av disse rolletildelingene gir ikke registerbruk. Ukjent rolle avvises. Global PlatformAdmin gir ingen kontobypass. Administrasjon gjelder oppretting, redigering, deaktivering, oppdagelse og versjonsgodkjenning. Til-/frakobling og bruk gjelder bare egen lokal UserId, aldri en klientangitt bruker-ID. Valgt konto kommer fra ferskt autentisert GUI-cookie og må stemme med Application-evalueringen. Endepunktene reautentiserer cookie; de bruker ikke et gammelt Blazor-circuit-snapshot.

Medlemskap, aktiv bruker, LoginAccount.IsEnabled, bruksrettighet og aktiv/uendret serverprofil kontrolleres på nytt ved bruk og etter eventuell refresh, før MCP-kallet. Godkjenningen kontrolleres også på nytt etter venting/refresh. Oppdagelse gir ingen kjørerettighet. Egen/ekstern MCP-server må dessuten selv autorisere data og handlinger. En definisjon eller godkjenning gir ingen nye TenantPlatform-grants, administratorrettigheter eller identitet som tjenestebruker.

Ved deaktivert/fjernet/endret profil kan brukeren fortsatt slette egne lokale credentials, med eksplisitt beskjed om at ekstern revokering ikke er bekreftet. Gamle tokens sendes ikke gjennom den nye profilen. Å fjerne lokal bruksrettighet/medlemskap stopper nye kall; det er ikke det samme som å slette eller tilbakekalle en OAuth-grant hos målserveren.

## To autentiseringsprofiler

**TenantPlatform:** gjenbruker 5B3s eksakte klientregistrering, callback, signatur-/issuer-/audience-/type-/levetidsvalidering, lokalt Guid-subjekt, initiator/GUI-bruker og faktisk lokal grant med opprinnelig absolutt slutt. Ingen ny egen serverprofil eller ekstra verktøy i MCP-hosten. Eksisterende operatørregistrering hos Web og klientlisten hos MCP må fortsatt være riktig.

**Ekstern OAuth:** OpenIddict Client håndterer authorization code, PKCE S256, state, korrelasjon, engangsinnløsning, klientautentisering og HTTPS-backchannel. Registreringen er operatøropprettet; ingen DCR/CIMD eller providerspesifikke skript. Lokal cookie, forseglede initiator-/tilkoblings-/pendingverdier og profilregistrering må stemme ved callback. Den eksterne identiteten kan være en annen personidentifikator. Det kreves verken lokal Guid-sub, lokalt utstedergrant, JWT-format eller lokal verifisering av ekstern access-JWT. Et opaque token er en legitimasjon mottatt fra det validerte OAuth-forløpet, ikke et bevis for en lokal brukeridentitet. Det foretas ingen uvalidert JWT-dekoding, e-postkobling eller GUI-innlogging som ekstern bruker.

Første eksterne profil krever en klienthemmelighet på minst 32 tegn og støtter confidential klient med `client_secret_post`, code+S256, query-callback, refresh og en eksplisitt resource. Utstederen må levere access-utløp (`expires_in`), og tilkoblingen krever refresh-token ved første samtykke. UserInfo og OIDC-innlogging er ikke nødvendig. Ved vellykket refresh uten ny refresh-token beholdes den forrige i henhold til tokenresponsen; nettverksfeil før bekreftet respons fører aldri til retry. Leverandører som trenger andre auth-metoder, cross-origin protokollendepunkter, obligatorisk UserInfo, dynamisk registrering eller særskilte protokolltilpasninger er utenfor denne profilen.

Eksterne forbindelser får en lokal maksimal levetid på åtte timer fra fullført autorisering, uavhengig av leverandørens eventuelle lengre grant. Refresh forlenger ikke denne. Utstederen kan ha kortere levetid eller tilbakekalle tidligere; dette oppdages ved tokenbruk/refresh. Ingen påstand om online kontroll av eksterne grants. Lokal sletting, standard tokenrevokering, utstedergrant og signaturvalidert JWT-restlevetid er fortsatt forskjellige forhold. Generell Web-cookie-/sesjonsrevokering inngår ikke.

## Operatørkonfigurasjon og restart

Aktiver først eksisterende OAuth og InternalMcp korrekt, deretter `McpRegistry:Enabled=true`. Samme vedvarende DB, Data Protection-key ring, applikasjonsdiscriminator og sertifikater må brukes mellom replikaer. Registry alene aktiverer ingen OAuth-utsteder eller alternativ klientløsning.

For hver ekstern profil angis `Id` (stabil ASCII alfanumerisk/bindestrek, maks 60), navn, eksakt HTTPS Issuer, MetadataAddress, Endpoint, Resource, klient-ID, klienthemmelighet, callback og scopes. MetadataAddress er obligatorisk og må ha samme origin som issuer. Angi utstederens faktiske OAuth-metadataadresse, eksempelvis `/.well-known/oauth-authorization-server`; klienten antar ikke OpenID-discovery eller brukerinnlogging med OIDC. Biblioteket validerer metadata og issuer; adressen gis gjennom SDK-ets [ConfigurationEndpoint](https://github.com/openiddict/openiddict-core/blob/7.7.2/src/OpenIddict.Client/OpenIddictClientRegistration.cs). [RFC 8414](https://www.rfc-editor.org/rfc/rfc8414.html#section-3) beskriver OAuth-metadata; den faktiske URI-en konfigureres eksplisitt også ved issuer med sti. Callback må være på samme origin som Webs eksisterende callback og ha eksakt sti `/mcp-client/registry/callback/{Id}`. Resource og Endpoint må ha samme origin; de kan ha forskjellige stier. Operatøren registrerer klient/callback/scopes/resource hos den eksterne test-/produksjonsutstederen separat. Ingen ekstern tjeneste konfigureres automatisk.

Hemmeligheten leveres gjennom beskyttet serverkonfigurasjon/secret-mount eller miljøvariabler, aldri repo, image, kommandolinje, databasefelt for GUI eller modell. Den returneres ikke i Application-profilkontrakten. Profilfingeravtrykket er av ikke-hemmelige protokoll-/adressevalg; det publiserer ikke en hash av klienthemmeligheten. Koordinert hemmelighetsrotasjon for samme klient kan bevare bindingen, men feil/tvetydig refresh krever fortsatt ny autorisering.

Registreringene lastes ved prosessoppstart gjennom OpenIddict AddRegistration. Dette er **ikke dynamisk klientregistrering eller støttet hot reload**. Operatøren må endre konfigurasjonen og restarte alle Web-instanser kontrollert før nye profiler kan velges. Databasedefinisjoner kan deretter opprettes uten restart med de allerede lastede profilene. Fjernet/endret profil vises utilgjengelig. Administrator må lagre korrekt lastet profil; adresse-/issuer-/resource-/klient-/scopeendring skifter credential-epoke og fjerner godkjenninger. Deretter kreves ny personlig autorisering og oppdagelse/godkjenning. Å bytte tilbake til gammel profil gjenoppliver ikke gamle credentials. Under en utrulling med ulike konfigurasjoner vil avvikende instanser avvise bruk; ikke presenter dette som sømløs reload.

## Utgående nettverk

GUI velger bare profiler som operatøren allerede har lastet. HTTPS kreves uten userinfo, query eller fragment. OAuth-discovery/token/JWKS/øvrig backchannel er bundet til den aktuelle registreringens issuer-origin; en discovery-respons kan ikke flytte hemmelighet eller token til en annen origin. Autorisasjonsadressen kontrolleres før browserredirect. Automatiske HTTP-redirects og cookies i backend er slått av. MCP-bearer sendes per request til profilens eksakte endepunkt, aldri til URL-er fra resultater eller beskrivelser.

Registerets backend-transporter bruker DNS-oppløsning og kontroll av faktisk IP i ConnectCallback, og kobler socket til den samme godkjente IP-en. Dette unngår en ny DNS-oppløsning mellom kontroll og tilkobling. Systemproxy brukes ikke. Loopback, link-local, kjente metadataadresser, multicast og uspesifiserte adresser avvises også når de finnes i operatørens private liste. Øvrige private/reserverte adresser krever konkrete IP-er i profilens `AllowedPrivateAddresses`; egen profil har `McpRegistry:InternalAllowedPrivateAddresses`. Ingen slik verdi kan angis i GUI. TLS validerer fortsatt det konfigurerte vertsnavnet. Nettverkspolicyen er ingen erstatning for egress-regler og forsvarlig operatørgodkjenning av et offentlig vertsnavn. Når registeret aktiveres, får det delte interne OAuth-backchannel også registerets DNS/IP-policy; godkjenn private issuer-adresser eksplisitt. Legacy-MCP-transporten og den særskilte JWT-validatoren beholder sine faste operatøradresser og 5B3s opprinnelige pinning; de gjøres ikke GUI-konfigurerbare.

## Verktøykatalog og grenser

Oppdagelse skjer med personlig autorisert MCP-klient og ekte `tools/list`. SDK-ets rå side-API brukes: maksimalt fem sider, 100 verktøy og begrenset cursor, ikke ubegrenset automatisk paging. Hvert svar har 64 KiB transportgrense, hvert navn maks 128 tegn, beskrivelse 4000 tegn, hvert skjema 16000 tegn og samlet lagret oppdagelse 256 KiB. Samme navn to ganger i én katalog avvises. Bare JSON-objektskjema lagres. Skjemaer lagres/vises som data; ingen URL-referanser lastes ned og ingen beskrivelser gjøres til instruksjoner. ReadOnlyHint beholdes ikke som tillitsgrunnlag.

Ny oppdagelse merker først forrige katalog ufullstendig. Feil/avbrudd kan ikke etterlate den som aktuell. For mange sider/verktøy gir eksplisitt ufullstendig katalog; byte-/metadataoverskridelse gir sikker feil. Ingen totalsum hevdes for en avkortet katalog. Etter fullstendig oppdagelse må administrator godkjenne en konkret metadataversjon. Samme uendrede versjon kan beholde godkjenning; ny/endret versjon krever ny godkjenning. Administrator kan fjerne godkjenning også ved ufullstendig/foreldet katalog, og senere oppdagelse gjenoppliver ikke en fjernet godkjenning. Katalogen regnes som foreldet etter 24 timer og stanser kjøring til ny fullstendig oppdagelse. UI viser dato, ufullstendig/foreldet status og godkjenning separat. Lagret godkjenning er ingen garanti for at en fjernserver ikke endrer oppførsel mellom oppdagelsene.

Application `CallAsync` er det felles backendgrunnlaget for senere konsumenter. Det krever tilgjengelig profil, egen legitimasjon, aktuell/fullstendig katalog og godkjent navngitt versjon. Ingen produksjons-HTTP-rute eksponerer vilkårlig kjøring. Argumenter må være JSON-objekt og maks 16 KiB; 6A innfører ikke en ny JSON Schema-motor. Målserveren må validere argumentenes domene-/skjemaregler. Resultatet er ubetrodd MCP-data, maks 64 KiB, ikke instruksjoner. Transporten fra 5B3 bufferer et avgrenset HTTP-svar før parsing; permanent åpne SSE-strømmer/fullføringer utover tidsbudsjettet inngår ikke i kompatibilitetsløftet for første profil. Den gamle klientens typede metoder kan ikke brukes på en registrert forbindelse for å omgå godkjenningen.

Konfigurasjons-, oppdagelses- og godkjenningsendringer bruker PostgreSQL-transaksjon, kontoavgrenset radlås og revisjonsvern. Samtidig konfigurasjon/katalog/godkjenning kan ikke overskrives av et eldre nettverkssvar. Credential-/refreshlåsen er nå per bruker og tilkoblings-ID, på tvers av Web-instanser. Mekanismen fra 5B3 med separat durabel «forbrukt» lagring før refresh er beholdt; andre forbindelser har egne låser. Kansellering og budsjett er fortsatt 20 sekunder HTTP, 15 sekunder MCP og 10 sekunder OAuth. Det legges ikke til generelle retries.

## Migrering, drift og verifikasjon

Ny OAuth-migrering er generert med `bash scripts/add-migration.sh AddMcpConnectionRegistry OAuthDbContext`. Den utvider credential-nøkkelen og legger til server-/verktøytabeller i OAuth-schemaet. Domenemodellens migreringer og produksjonens MCP-prosjekt er uendret. Ikke migrer utvikling/produksjon som test. Senere driftsmigrering krever backup og eksplisitt `--oauth-migrate` som dokumentert i 5B1; ingen ny oppstartsmigrering eller seed er innført. Webs auth-rolle trenger CRUD på de nye tabellene; MCPs SELECT-rolle skal fortsatt ikke se OAuth-schemaet. Nedgradering med flere credentials per bruker kan ikke gjøres tapsfritt til den gamle primærnøkkelen; planlegg tilbakeføring før driftsmigrering.

Testene ligger i eksisterende `TenantPlatform.McpClient.SmokeTests`, inklusive `RegistryChecks.cs`. De bruker produksjonens klient/utstederkomposisjon og ekte OpenIddict-/MCP-biblioteker. Ekstern fixture er en egen TestServer-utsteder med ikke-lokal identitet, eksplisitt OAuth-metadata uten OpenID-discovery og to syntetiske MCP-ruter. Bare testassemblyet har nettverksruting og feilinjeksjon. Ingen testidentitet eller bypass kan aktiveres gjennom produksjonskonfigurasjon. Runneren oppretter privat PostgreSQL/socket og stopper/fjerner den etterpå. Detaljerte faktisk gjennomførte bygg og kjøringer står i moderniseringsplanens avsnitt 25.

Faktisk verifikasjon 17. oktober 2026: solution og de sju berørte testprosjektene bygget uten feil etter siste implementasjonsendring. McpClient-, OAuth-, Mcp-, AccountRegisters-, AgreementList-, Agreements- og Dashboard-suitene bestod i én avsluttende kjøring, de seks første med isolert PostgreSQL, Dashboard med eksisterende InMemory-/SQL-/komponentoppsett. Klyngen ble stoppet og fjernet. Eksisterende pakkevarsler består. Ingen driftsdatabase ble brukt; ny migrering er bare anvendt i test. Se moderniseringsplanens 25.4 for konkrete kontroller.

Manuell rest før drift: nettleser på alle tre språk; admin, vanlig medlem og konto-/brukerbytte; intern/ekstern samtykke og feilcallback; deaktivert profil; gammel/ufullstendig katalog; endret godkjent verktøy; lokal frakobling ved nettverksfeil; faktisk flere Web-instanser med delte nøkler; reell DNS/privat IP/TLS/egress/proxy; containerbygg. Syntetiske TestServer-kall beviser ikke kompatibilitet med en bestemt leverandør eller ChatGPT.

Offisielle kilder kontrollert 17. oktober 2026: [OpenIddict-klientintegrasjon](https://documentation.openiddict.com/guides/getting-started/integrating-with-a-remote-server-instance.html), [ASP.NET Core og registreringsvalg](https://documentation.openiddict.com/integrations/aspnet-core), [OpenIddict 7.7.2 registreringsmodell](https://github.com/openiddict/openiddict-core/blob/7.7.2/src/OpenIddict.Client/OpenIddictClientOptions.cs) og [C# SDKs eksplisitte side-API](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/pagination/pagination.html). Kontrollert restart er vår bevisste livssyklus for statisk konfigurerte registreringer, ikke en påstand om at OpenIddict gir ferdig dynamisk profiladministrasjon. Ingen pakkeoppgradering inngår.

## 8D: nye avtalelesere

TenantPlatform-profilen oppdager nå seks verktøy. De nye agreements_query, agreements_aggregate og agreements_details krever samme eksplisitte tilgang, AutomaticRead-klassifisering og godkjenning av metadataversjon som øvrige verktøy. Oppdagelse gir ingen automatisk adgang. Se [8D-kontraktene](mcp-agreement-read-contracts.md), inkludert de fire nye målrettede SELECT-rettighetene som operatør må etablere separat. Legacyklienten på /mcp-client beholder sine tre tidligere verktøy.

## 9A/9B: rapportkonsument og utkast i chat

[Personlige rapportsider](personal-report-pages-operations.md) bruker samme personlige, godkjente agreements_query/agreements_aggregate på egen TenantPlatform-profil. 9B legger til en lokal strukturert forslagshandling i chatten; den er ikke et nytt oppdagbart MCP-verktøy og trenger ingen ekstra AutomaticRead-godkjenning. Brukeren må fortsatt velge forbindelsen og godta agentens datadeling. Utkast/preview oppretter ingen lagret side. Bare sidedefinisjonen persisteres ved den separate **Lagre under Mine sider**-knappen, gjennom eksisterende Application-sideadministrasjon. Previewresultater persisteres ikke.

GUI-/chatlagring krever ikke aktivering av administrasjons-MCP eller ekstra OAuth-scope på brukerens lesetilkobling. Den separate `/mcp/pages`-flaten beholder sitt ekstra scope, eksplisitte samtykke og begrensede definisjonsskriver som beskrevet i 9A. personal_pages_-verktøy utelates fortsatt fra automatisk agentbruk. Ingen ny OAuth-klient eller utvidelse av MCP-leserollens skriverettigheter inngår i 9B.
