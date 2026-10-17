# MCP-lesehost: oppsett og verifikasjon (5B2)

TenantPlatform.Mcp er en separat .NET 10-resource-server. Den refererer Application og Infrastructure, aldri Web. Web er OAuth-utstederen fra [5B1](oauth-issuer-operations.md). Ingen intern OAuth-klient, agent eller rapportmotor inngår i selve 5B2-hosten. Den interne delegerte Web-klienten er senere implementert i [5B3](internal-mcp-client-operations.md), uten nye MCP-verktøy eller Web-referanse fra MCP-produksjonsprosjektet. Agent og rapportmotor er fortsatt utsatt. Ingen bakgrunnsjobber, migrering, seed eller initialisering registreres/startes av MCP.

## Konfigurasjon og oppstart

Bruk [malen](../../docker/mcp/appsettings.example.json) som oppsettreferanse. Monter faktisk konfigurasjon eller bruk miljøvariabler, eksempelvis `Mcp__Issuer`, `Mcp__Resource`, `Mcp__Clients__0`, `Mcp__ConnectionString`. Hemmelig connection string skal leveres gjennom beskyttet driftskonfigurasjon; ikke legg passord i repo, image, kommandohistorikk eller logger. MCP har ingen signing-/encryption-private nøkler, ingen cookie-key ring og intet OAuth-datalager.

- Issuer er den eksakte faste HTTPS-issuer fra Web, inklusive eventuell avsluttende skråstrek. Resource skal være fast HTTPS-URL med `/mcp` som sti. Klientlisten inneholder eksakte registrerte OAuth-client IDs. Ingen fri DCR eller identitetskobling gjennom e-post.
- `AllowedHosts` settes til faktisk offentlig vertsnavn. `AllowedOrigins` er eksakte HTTPS-origins; tom liste avviser alle Origin-headere. Kall uten Origin er tillatt for serverklienter. Dette er Origin-validering; hosten tilbyr ikke et generelt CORS-grensesnitt for nettleserapper.
- `TrustedProxies` inneholder bare konkrete IP-adresser til betrodde reverse proxies, ett videresendingsledd. Ved tom liste installeres ikke forwarded-header-middleware; tomme trust-lister gir aldri tillit til alle. Host-header brukes ikke til issuer, resource eller metadata-URL.
- HTTPS avsluttes i kontrollert proxy. Kestrel-port 8080 skal bare nås fra denne proxyen. Proxyen må bevare Authorization, Accept, Content-Type, MCP-Protocol-Version, Mcp-Method og Mcp-Name, støtte Streamable HTTP/SSE-respons og sette maks 16 KiB body og ca. 15 sekunders timeout. Ingen body-/header-/querylogging for MCP-ruten. Begrens også anonym trafikk i proxyen før tokenvalidering.
- Fast metadata: `/.well-known/oauth-protected-resource/mcp`. Liveness: `/health/live`, uten database-/nøkkel-/versjonsdetaljer. MCP `/mcp` krever bearer også under initialisering/discovery/tool-oppdagelse. Origin-sjekken gir ikke autentisering.
- Oppstart gjør kun en SELECT-basert privilegiekontroll mot PostgreSQL-katalogene. For privilegert DB-bruker stopper hosten. Den oppretter/reparerer ikke roller eller tabeller. Ordinær start: `dotnet run --project src/TenantPlatform.Mcp` med eksplisitt isolert/lokalt eller ferdig etablert driftsoppsett. Ikke bruk vanlig utviklingsdatabase som testfixture.

## Leserolle og database

Drift må separat opprette en dedikert LOGIN uten superuser, createdb, createrole, replication, bypassrls, eierskap eller arvede skriveroller. Den må ha CONNECT til domenedatabasen, USAGE på public og **bare SELECT på disse åtte tabellene**:

`accounts`, `users`, `user_accounts`, `user_account_roles`, `agreements`, `agreement_access`, `organizations`, `agreement_reminder_settings`.

Listen er kontrollert mot AccountContextData, AuthorizationData og AgreementList-/AttentionData med isolerte PostgreSQL-kall. Autorisasjonsadapteren er felles og har flere metoder; MCP-komposisjonen eksponerer bare de tre readerne, og rollen får ingen rettigheter til de øvrige tabellene. Ingen tilgang til `login_accounts`, OAuth-schema, frister, historikk, dokumenter, audit eller leasing er nødvendig. Ingen sekvensrettigheter eller DEFAULT PRIVILEGES for fremtidige tabeller. Sørg for at PUBLIC ikke gir CREATE på public eller skriverettigheter på tabeller. Brukeren skal heller ikke eie databasen/schemaet, ha schemaoppretting eller privilegerte funksjoner som omgår leserollen.

Runtime har i tillegg en SaveChangesInterceptor som avviser både synkron og asynkron SaveChanges, også tomme kall. Den erstatter ikke DB-rettigheter: rå SQL/ExecuteUpdate må også nektes av PostgreSQL. Eksisterende Application-autorisasjon, ikke SQL-rollen, bestemmer tenant-/ressursinnsyn. Ingen nye RLS-/rollemodeller eller schemaendringer er innført.

## Tokenprofil og tilbakekalling

OpenIddict Validation.AspNetCore + SystemNetHttp **7.7.2** bruker HTTPS-discovery/JWKS fra fast issuer, cached av biblioteket. Ingen introspection, authorization-entry- eller token-entry-kontroll mot OAuth-lageret. Nøkkelrotasjon krever overlapp i Web/JWKS som beskrevet i 5B1; en kald host må kunne hente metadata/nøkler.

Profilen fra reell 5B1-utstedelse er `typ=at+jwt`, RS256, eksakt issuer, MCP-resource som audience, lokal Guid `sub`, faktisk **`client_id`**, read-scope `tenantplatform.agreements.read`, exp og eventuell nbf. Signert OpenIddict-delegasjons-ID må også finnes; dette er en profilkontroll, ikke et online oppslag eller en bekreftelse på at granten fortsatt er aktiv. Maksimalt klokkeavvik er 30 sekunder. ID-token, feil type/signatur/algoritme/issuer/audience/utløp/nbf, manglende scope, uregistrert klient eller ikke-lokalt subjekt avvises. Ingen cookie, alternativ auth-scheme eller konfigurerbar testidentitet finnes i produksjonshosten.

OAuth-revokering stopper kode-/refresh-kjeden hos Web. Allerede utstedt JWT kan fortsatt brukes frem til utløp, normalt maksimalt fem minutter + 30 sekunder. MCP kontrollerer ikke grant online. Application kontrollerer User.IsActive, medlemskap, roller og avtalegrants ferskt ved hvert kall, også etter tidligere vellykkede kall. LoginAccount.IsEnabled og generell Web-cookie-/sesjonsrevokering er separate forhold; passordsperring skal ikke omtales som umiddelbar global sesjonsrevokering.

## Verktøy, grenser og feil

Bare `accounts_list`, `agreements_list`, `agreements_attention` er registrert. Kontrakter og felt er i [MCP-planens avsnitt 4](mcp-read-host-plan.md#4-første-verktøyutvalg-og-nøyaktige-kontrakter). Alle bruker eksplisitte DTO-er; OwnerUserId/OwnerName/Owners, grants, dokumenter, økonomi, vilkår og transportmetadata eksponeres aldri automatisk. Returnert tekst er ubetrodd data, ikke instruksjoner.

Hvert verktøykall oppretter et eget DI-scope, binder ett frosset aktørsnapshot fra SDK-ens validerte principal og foreslått konto, og lar Application verifisere medlemskap/omfang. Ingen delt muterbar konto mellom parallelle kall eller RPC-ID-basert identitet.

Grenser: body 16 KiB; hele HTTP-svaret maks 64 KiB (inklusive RPC-/SSE-framing); verktøyresultat maks 60 KiB for å reservere framingplass. Overskridelse gir `RESULT_TOO_LARGE`, eventuelt HTTP 413 hvis den komplette envelopen overskrider 64 KiB. Ingen delpayload sendes. Kontooversikt over 100 gir feil for hele resultatet. Liste har faste sider på 25, page 1–10000, 200 Unicode-tegn i søk og eksakte enumstrenger. Søket trimmes; kontrolltegn og fremmede argumenter avvises. Manglende valgfrie felt er ikke det samme som klientangitt null.

TotalCount er Application-queryens reelle filtrerte total, faktisk sideklamping synliggjøres, `complete` gjelder bare hele resultatet i ett svar, og `snapshotConsistent=false`. Ved sidegrensen og flere treff beholdes hasMore=true, nextPage=null og limitReached=true. Snevr inn filtrene. Attention bruker Application-dato og egne fullstendige kategorier; kategoriene kan overlappe og skal **ikke summeres til et unikt totalantall**.

10 sekunders samlet HTTP-budsjett, inklusive autentisering og databasearbeid; SQL command timeout 8 sekunder. HTTP-avbrudd og SDK-cancellation føres videre til Application/EF. Maks fire samtidige HTTP-kall og 60 forespørsler/minutt per lokal sub på tvers av klienter, uten ventekø. Oppdagelse/initialisering teller også. Dette er en **én-instanspilot**; felles kvotekoordinering må på plass før flere replikaer.

Verktøyfeil: INVALID_ARGUMENT, RESOURCE_UNAVAILABLE (samme for fremmed/ukjent konto), RESULT_TOO_LARGE, CANCELLED eller TEMPORARILY_UNAVAILABLE. Transport bruker 401/403 med fast bearer resource_metadata/scope, 413 for størrelse, 429 for kvote og 503/504 for tekniske feil/tidsbudsjett. Ingen interne exceptions returneres fra verktøyhandlerne. Biblioteket eier MCP-protokollfeil. Sensitiv framework-/EF-/OpenIddict-/HTTP-client-/MCP-/Npgsql-logging filtreres bort i hostkomposisjonen; ingen tokens, cookies, argumenter, søk, titler, payload eller SQL-parametre skal logges av proxy/monitorering heller. Denne første versjonen gir ikke detaljert request-telemetri.

## Bygg, tester og ekstern akseptanse

`dotnet build TenantPlatform.sln`

`bash scripts/test-agreements-postgres.sh TenantPlatform.OAuth.SmokeTests TenantPlatform.Mcp.SmokeTests TenantPlatform.AgreementList.SmokeTests TenantPlatform.Agreements.SmokeTests TenantPlatform.AccountRegisters.SmokeTests TenantPlatform.Dashboard.SmokeTests`

Runneren lager bare en privat /tmp-klynge uten TCP, eget `tenant_mcp_tests` og en reell mcp_reader-rolle. Admin brukes bare til migrering/fixture og målrettede testendringer; MCP bruker leserollen. TestHost ruter kun discovery/JWKS til reell in-process Web-utsteder og bruker den faktiske login-/samtykke-/PKCE-flyten. Felles issuerhjelpere er bare kompilert inn i testprosjektene. Testdekoratorer for store svar/avbrudd er også bare i testassemblyet, uten produksjonsflagg eller bypass.

Containerkommando fra reporot: `docker build --file docker/mcp/Dockerfile --tag tenantplatform-mcp:5b2 .`. Restore-laget kopierer Mcp, Core, Application og Infrastructure; repoet har ingen ekstra Directory.Build/Packages-/NuGet-/global.json-/lock-inputfiler. Runtime er aspnet:10.0, ikke-root APP_UID, port 8080 og `dotnet TenantPlatform.Mcp.dll`. Konfigurasjonsmal er ikke aktivert i imaget. Ingen compose-/VM-/Serga-endring inngår.

Solution og seks testprosjekter bygger, og kommandoen over bestod med alle seks suiter 17. oktober 2026, de fem første med isolerte PostgreSQL-kontroller. Begge MCP-protokollveier, faktiske OAuth-tokens, leserolle, kvoter, full envelopegrense og også kansellering av en faktisk låsventende PostgreSQL-SELECT er prøvd. Docker-byggforsøk feilet fordi daemonen var utilgjengelig; isolert lokal restore av Dockerfilens eksakte input bestod. Se moderniseringsplanens 5B2-status for fullstendig verifikasjon. Containerbygg, virkelig TLS/proxy, manuell OAuth-GUI og ekstern ChatGPT-tilkobling må skilles fra TestServer-/PostgreSQL-verifikasjon. Ingen påstand om ekstern ChatGPT-kompatibilitet før faktisk registrering/callback/TLS og klientprøve er gjennomført.

Offisielt grunnlag kontrollert 17. oktober 2026: [C# SDK 2.2.0](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.2.0), [stateless og protokollkompatibilitet](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/stateless/stateless.html), [SDK-ressursservereksempel](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/samples/ProtectedMcpServer/Program.cs), [MCP-autorisasjon](https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization), [OpenIddict API-validering](https://documentation.openiddict.com/guides/getting-started/implementing-token-validation-in-your-apis.html). MCP- og OpenIddict-versjonene er eksplisitte prosjektreferanser, faktisk restored og bygget; øvrige pakker er ikke oppgradert.
