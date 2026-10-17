# Intern delegert MCP-klient i Web (5B3)

Denne funksjonen kobler en innlogget TenantPlatform-bruker til den separate MCP-lesehosten. Den bruker OpenIddict Client.AspNetCore/SystemNetHttp 7.7.2, etablert OpenIddict JWT-validering og offisiell ModelContextProtocol-klient 2.2.0 med ekte Streamable HTTP. Ingen chatagent, modellkall, rapportmotor, servicekonto eller register over eksterne MCP-servere inngår.

## Aktivering og registrering

Funksjonen er deaktivert som standard. [Konfigurasjonsmalen](../../docker/web/internal-mcp.example.json) aktiveres ikke av Dockerfilen. Tilfør eksplisitt `InternalMcp`-konfigurasjon via driftsoppsett/mount eller miljøvariabler. Det kreves:

- `InternalMcp:Enabled=true`, ClientId, ClientSecret, Issuer, Resource og Callback.
- Samme eksakte HTTPS-issuer og resource som 5B1-utstederen. Resource er `/mcp`; callback er `/mcp-client/callback` på samme HTTPS-origin som Web/utstederen. Bruk kanoniske URL-er, ikke interne HTTP-adresser eller adresser fra verktøyresultater. Backend må kunne nå de offentlige TLS-navnene; eventuell intern DNS-ruting må bevare navn og sertifikatkontroll.
- En **egen confidential klient**, eksempelvis `tenantplatform-web-mcp`, i `OAuth:Clients` med eget navn/hemmelighet og nøyaktig én redirect URI, identisk med InternalMcp:Callback. Ikke gjenbruk ChatGPT-klienten. Lever samme vault-/secret-verdi til `InternalMcp:ClientSecret` og den valgte `OAuth:Clients:<indeks>:Secret`; ikke lagre den i repo/image/logger.
- Legg denne klient-ID-en til MCP-hostens eksplisitte `Mcp:Clients`-liste. Det skjer ikke automatisk gjennom Web-registreringen.
- Registrer/oppdater klienten gjennom eksisterende operatørkommando `--oauth-register-clients`, se [utstederdrift](oauth-issuer-operations.md). Den gir code+PKCE S256, read-scope/offline-access, resource-tillatelse og code-/refresh-/revoke-endepunkter. Ingen client credentials, OIDC-scopes, ID-token eller UserInfo innføres.

Manglende, deaktivert eller innbyrdes feil klientkonfigurasjon gir en utilgjengelig funksjon på `/mcp-client`. Det opprettes ingen alternativ identitet eller reserveflyt. Vanlig GUI-login beholdes. Utstederens allerede etablerte krav til nøkler/lager gjelder fortsatt ved `OAuth:Enabled=true`.

Dette steget endrer ikke compose, VM, reverse proxy eller vanlige deploymentkommandoer. Driftsaktivering er ikke gjort. Web og MCP må ha fungerende proxy/TLS og den eksakte callbacken før ekstern bruk.

## Schema og oppstart

`OAuthDbContext` har fått migreringen `20261017142957_AddInternalMcpCredentials`, generert med `bash scripts/add-migration.sh AddInternalMcpCredentials OAuthDbContext`. Den legger bare til `oauth.mcp_client_credentials`. Domenecontextens modell/migreringer er uendret, kontrollert med HasPendingModelChanges i PostgreSQL-fixturen. Ingen navigasjon/FK som importerer domenebrukere til OAuth-schemaet er lagt til.

Nye kolonner: lokal UserId som primærnøkkel for én intern forbindelse per bruker, ClientId/Issuer/Resource, beskyttet payload, grant-ID, access-/absolutt grantutløp og kortlivet pending-ID/utløp. Åpen state-korrelasjon og engangsredeeming håndteres av OpenIddict Client med eksisterende standard `oauth`-tokentabeller. Disse er **ikke** klientens access-/refresh-lager; selve legitimasjonen ligger separat og kryptert i den nye tabellen.

Migreringen er bare anvendt i isolerte testdatabaser her. Normal Web-oppstart migrerer ikke OAuth-schemaet. Drift må senere ta backup og bruke eksisterende eksplisitte `--oauth-migrate` med kontrollert driftskonfigurasjon før funksjonen aktiveres. Ingen utviklings-/produksjonsmigrering er gjennomført. Webs OAuth-DB-rolle trenger lese-/skriverettigheter til den nye tabellen og standard OpenIddict-state-/grant-/tokentabeller. MCP-hostens SELECT-rolle skal **ikke** få disse rettighetene.

## Brukerforløp og identitet

Lenken finnes på `/oauth/connections`, videre til `/mcp-client`. Siden bruker eksisterende Bootstrap og norske, britisk-engelske og svenske ressurser. Den er statisk serverrendret med vanlige forms, ikke et chatpanel eller en ny interaktiv circuit.

1. Brukeren åpner tilkoblingssiden med gyldig GUI-cookie. Application revaliderer lokal aktivstatus og innloggingsaktivering før tilkobling/status/bruk.
2. «Koble til» krever antiforgery, oppretter en ti minutters pending-binding til aktuell bruker og starter bibliotekets code+PKCE S256-flow. Fast klient, issuer, resource og callback brukes. SDK-et håndterer state, korrelasjonscookie, kryptert state-token og engangsredeeming.
3. Samtykket skjer hos eksisterende utsteder på samme Web-origin. Eksisterende cookie kan brukes; ved ny innlogging brukes dagens User/LoginAccount/passord og 5B1s beskyttede loginretur.
4. Callback bruker **ferskt verifisert GUI-cookie**, aldri OAuth-principal som GUI-innlogging. Access-tokenet signatur-/type-/issuer-/audience-/levetidsvalideres av OpenIddict Validation, og client_id/read-scope/delegasjons-ID kontrolleres. Application krever at token-subjektet, initiatoren i SDK-beskyttet state og GUI-brukeren er samme lokale bruker, og at pending-bindingen fortsatt gjelder. Den kontrollerer også den aktuelle utstedergrantens bruker/klient/gyldighet, refresh-samtykke og faktiske absolutte slutt. Ingen e-postkobling eller klientangitt UserId.
5. Testknappen henter egne kontoer via MCP. Velg en konto og test Attention. Kontoen valideres på nytt av MCP/Application. Kategorienes dato og separate tellinger vises; overlapp skal ikke summeres som unike avtaler. Tekniske payloads eller tokens vises ikke.
6. «Koble fra» sletter lokale brukbare credentials/pending-binding før bibliotekets standard revocation-forsøk. Ved nettverksfeil er forbindelsen fortsatt lokalt fjernet, og siden viser at utstederrevokering ikke kunne bekreftes. Bruk utstederens eksisterende tilkoblingsside for å kontrollere/tilbakekalle granten eksplisitt.

State fra en eldre tilkoblingsstart kan ikke fullføre etter ny start/frakobling. Brukerbytte eller manglende cookie ved callback binder ingen forbindelse til den gamle brukeren. Avslått/avbrutt samtykke lagrer ingen brukbare credentials. Lokal status er ikke et løfte om at en eksternt tilbakekalt grant fortsatt er aktiv: det oppdages ved refresh, med den dokumenterte JWT-restlevetiden.

## Lagring, nøkler og backup

Access-/refresh-payload krypteres med ASP.NET Core Data Protection. Purpose binder formatversjon, lokal UserId, ClientId, issuer og resource; ciphertext kan ikke flyttes til en annen bruker eller profil. Plaintext finnes bare kortvarig i backendminnet og i nødvendige HTTPS-protokollmeldinger. Tokens legges aldri i GUI-cookie, HTML, nettleserlagring, prompt, URL, logger eller delt DefaultRequestHeaders. OAuth-protokollens kode/state bruker standard URL-flyt; disse queryverdiene må heller ikke logges.

Beskyttelsen gjenbruker 5B1s vedvarende `OAuth:CookieKeyDirectory` og sertifikatbeskyttede key ring. OpenIddict Client bruker de eksisterende RSA signing-/encryption-sertifikatene til sin særskilte state-tokenprofil; ingen utviklings-/ephemeral-sertifikatfallback legges til. Tokenformater/purposes skiller state, utstedertokens og lokal payload. Tidligere sertifikater beholdes gjennom etablert rotasjonskonfigurasjon.

Ta konsistent backup av OAuth-databasen, key ring og nødvendige gamle/aktive PFX-er med separat beskyttelse av passord/hemmeligheter. Behold nøkler som trengs for inflight state (10 minutter) og lokal legitimasjon/grants (maks åtte timer), og for gjenoppretting av relevante backuper. Tap av key ring/private dekrypteringsnøkler krever ny innlogging/tilkobling; ingen ukryptert reservevei finnes. Alle Web-replikaer må dele samme auth-database, vedvarende key ring/sertifikater, konfigurasjon og Data Protection-applikasjonsdiscriminator/content-root, som allerede er nødvendig for delte GUI-cookies. Verifiser dette før trafikk fordeles mellom replikaer. Normal container har samme `/app`-content-root; ulike mapper/applikasjonsnavn må ikke antas å dele beskyttelsen automatisk.

Klienthemmelighet endres koordinert hos operatørregistreringen og alle Web-instanser. Gamle instanser med feil hemmelighet kan føre til at en refresh må avsluttes og ny tilkobling kreves; dette er sikker stans, ikke automatisk fallback. Ikke endre issuer/resource/klientprofil mens gammel forbindelse skal beholdes. Profilendring krever frakobling/nytt samtykke; gammel legitimasjon sendes aldri til en ny adresse.

## Refresh, samtidighet og feil

Application beholder utstedergrantens opprinnelige absolutte slutt; den setter aldri «nå + åtte timer» ved refresh. Access-fornyelse skjer ved mindre enn omtrent 30 sekunders gjenværende levetid. Alle operasjoner på én brukers interne forbindelse koordineres med en deterministisk PostgreSQL advisory transaction lock, på tvers av Web-prosesser. Ingen prosesslokal Semaphore er eneste vern.

Koordineringstransaksjonen holder bare låsen. Credential-lagringer bruker egne korte contexts/commits. **Før refresh sendes, committes en lokalt ubrukbar tilstand uten tokens.** Ved suksess valideres nye tokens og samme subjekt/grant/absolutte slutt før ny kryptert payload committes. Hvis prosessen dør, HTTP-svaret går tapt, requesten kanselleres eller lagring feiler etter forbruk, blir forbindelsen ubrukbar og krever nytt samtykke. Den gamle roterte refresh-tokenen kan ikke bli aktiv igjen gjennom rollback. Dette ofrer tilgjengelighet ved tvetydige feil for å unngå token-gjenbruk. SDK-ets HTTP-resilience-pipeline er eksplisitt uten automatisk retry for kode/refresh/revokering.

Låsen holdes også under MCP-lesekallet, slik at frakobling og refresh ikke konkurrerer med bruk av lokal legitimasjon. Det serialiserer kall per bruker i første versjon; ulike brukere kan kjøre samtidig. Tapt databasetilkobling/prosess frigjør låsen. Testene bruker samtidige kall fra uavhengige Web-serviceprovidere mot samme PostgreSQL, med nøyaktig én faktisk refresh. MCP-hostens separate én-instanspilot for ratekvoter gjelder fortsatt; klientens databasekoordinering opphever ikke den begrensningen.

Frontendbudsjett er 20 sekunder, MCP-klientbudsjett 15 sekunder og OAuth/JWKS HTTP-klienter 10 sekunder. Request-cancellation føres gjennom Application, databaseventing, bibliotekene og HTTP. MCP-responsen er begrenset til 64 KiB. Bare accounts_list, agreements_list og agreements_attention kan kalles gjennom klientlaget, med strukturerte versjonerte DTO-er. MCP-bearer settes per transport/request. Cookies er deaktivert i backend-HTTP-klientene, automatiske redirects er deaktivert, OAuth-backchannel er bundet til fast issuer-origin og MCP til eksakt resource-URL. Ingen tokens sendes til URL-er fra verktøyresultater. Validering-/metadata-/transportfeil gir sikre koder/lokaliserte beskjeder. Ingen direkte SQL-/Application-reservevei finnes for MCP-data.

Lokal frakobling, RFC-tokenrevokering, utstederens eksplisitte grantrevokering og allerede utstedt JWT er forskjellige forhold. En bekreftet standard token-revocation er ikke et løfte om at signaturvaliderte access-JWT-er slutter umiddelbart. MCP revaliderer fortsatt lokal User.IsActive, medlemskap, roller og avtalegrants. Klienten kontrollerer dessuten LoginAccount.IsEnabled før bruk. Generell Web-cookie-/sesjonsrevokering er ikke implementert i 5B3.

## Verifikasjon og videre arbeid

`dotnet build TenantPlatform.sln`

`bash scripts/test-agreements-postgres.sh TenantPlatform.McpClient.SmokeTests TenantPlatform.OAuth.SmokeTests TenantPlatform.Mcp.SmokeTests TenantPlatform.AccountRegisters.SmokeTests TenantPlatform.AgreementList.SmokeTests TenantPlatform.Agreements.SmokeTests TenantPlatform.Dashboard.SmokeTests`

Den nye suiten bruker reell utsteder/client og MCP-host, standardbibliotekenes transport, ekte PostgreSQL med isolert `tenant_mcp_client_tests` og SELECT-rolle for MCP. Bare testassemblyet ruter fiktive HTTPS-adresser gjennom TestServer og injiserer nettverksfeil/testklokke. Ingen produksjonsflagg kan aktivere dette. Ingen reelle modell-, e-post- eller ChatGPT-kall brukes.

Solution og sju berørte testprosjekter bygget, og de sju suitene i kommandoen over bestod 17. oktober 2026, seks med isolerte PostgreSQL-kontroller. Docker-daemonen var utilgjengelig, så faktisk containerbygg er ikke utført; lokal restore med Web-Dockerfilens eksakte input bestod. Interaktiv GUI og virkelig proxy/TLS er ikke testet. Se moderniseringsplanens 5B3-status for fullstendig verifikasjon. Før drift: test begge samtykkevalg, ny innlogging, brukerbytte/utlogging, kontovalg, tilbakekalt tilgang, utløp, frakoblingsfeil, restart og routing mellom faktiske Web-instanser med vedvarende nøkler. Proxy/APM må utelate Authorization, cookies, callback-query, formdata, søk og MCP-payloads. Klienten filtrerer sensitive SDK-/HTTP-client-logger og request-start-URL-logging; ikke innfør eksterne payloadloggere som omgår dette.

Agent, modellintegrasjon, rapportmotor, flere interne klientprofiler, ubetjent langtidsrapportering, eksterne serverregistre, generell sesjonsrevokering og opprydding av foreldede klient-/state-rader er separate senere oppgaver. Gjenbruk klientlaget fra en betrodd request/operasjonskontekst; ikke lagre en request-scoped tjeneste i en langvarig Blazor-circuit eller singleton. OAuth/MCP-SDK-typer ligger i Web-adapterne; Application eier brukerbinding, tilkoblingslivsløp og refresh-/utløpsregler, Infrastructure bare teknisk lagring/låsing.

Offisielt grunnlag kontrollert 17. oktober 2026: [OpenIddict Client](https://documentation.openiddict.com/guides/getting-started/integrating-with-a-remote-server-instance.html), [ASP.NET Core-integrasjon](https://documentation.openiddict.com/integrations/aspnet-core), [7.7.2 klientkode og tokenflyter](https://github.com/openiddict/openiddict-core/tree/7.7.2/src/OpenIddict.Client), [MCP C# SDK 2.2.0](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.2.0). Ren OAuth gir ikke automatisk en autentisert brukerprincipal; derfor valideres access-JWT særskilt og lokal GUI-identitet beholdes.
