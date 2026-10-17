# Steg 5B1: drift og verifikasjon av OAuth-utstederen

[Tilkoblingsregisteret fra 6A](mcp-connection-registry-operations.md) bruker det samme OAuth-schemaet med flere klientcredentials og kontoeide definisjoner/verktøy. Ny migrering anvendes fortsatt bare gjennom eksplisitt driftsmigrering. Eksterne OAuth-profiler registreres hos sine egne utstedere, ikke gjennom TenantPlatforms `--oauth-register-clients`.

Status 17. oktober 2026: implementert lokalt; ingen ekstern publisering, utviklings-/produksjonsmigrering eller ekstern ChatGPT-test. OAuth er deaktivert uten eksplisitt `OAuth:Enabled=true`. Vanlig GUI bruker fortsatt eksisterende cookie og lokale User/LoginAccount. MCP-host og intern OAuth-klient inngår ikke i 5B1. MCP-resource-serveren er senere implementert og testet separat i [5B2](mcp-read-host-operations.md), inklusive faktisk tokenprofil og JWT-restlevetid etter grantrevokering. Den interne delegerte Web-klienten er senere implementert i [5B3](internal-mcp-client-operations.md). Der beskrives separat klientregistrering/callback, den nye klientlagringstabellen, kryptering, refresh-koordinering og frakobling. Agent og rapportmotor er fortsatt utsatt.

## Komponenter og grenser

- Web registrerer OpenIddict Server, protokollendepunkter, beskyttede interaksjoner, samtykke og cookie-/HTTP-adaptere. `/oauth/connections` viser egne tilkoblinger, med lenke fra brukermenyen når funksjonen er aktivert.
- Application `OAuthAccessService` tar betrodd aktørsnapshot og beslutter lokal adgang fra rå User.IsActive/LoginAccount.IsEnabled-fakta. Den bestemmer åttetimersgrensen og eierskap ved lesing/tilbakekalling. Ingen EF-, OpenIddict-, HTTP- eller Blazor-avhengighet er lagt til Application.
- Infrastructure implementerer brukerfakta og lagring gjennom standard OpenIddict-managers. `OAuthDbContext` bruker schema `oauth` og `oauth.__EFMigrationsHistory`, uten endring av domenekontekstens modell/migreringer. Konteksten lever i HTTP-/kommandoscopet, ikke i en interaktiv Blazor-circuit.
- Authorization code/refresh, PKCE S256, klientautentisering, koder, tokenkjeder og standard requestvalidering håndteres av biblioteket. Entity caching er deaktivert hos utstederen slik at klient-/grantstatus leses fra lageret. Ingen degraded mode, fri DCR, implicit, password grant eller client credentials.

OpenIddict 7.7.2 ble kontrollert mot [offisiell serverveiledning](https://documentation.openiddict.com/guides/getting-started/creating-your-own-server-instance), [EF-integrasjon](https://documentation.openiddict.com/integrations/entity-framework-core) og [stabil utgivelse](https://github.com/openiddict/openiddict-core/releases/tag/7.7.2). Pakken krever EF Core Relational 10.0.11 på net10.0; Infrastructure har derfor oppdatert direkte EF Core/Relational fra 10.0.10 til 10.0.11. Npgsql 10.0.3, EF Design-/InMemory-referanser og øvrige pakker er beholdt. EF CLI 10.0.10 rapporterte versjonsforskjellen ved generering, men genererte migreringen uten feil; globalt verktøy er ikke oppgradert. Kompatibiliteten er prøvd mot faktisk isolert PostgreSQL.

## Konfigurasjon før aktivering

Ingen faktisk driftsadresse eller hemmelighet er lagt i repoet. `OAuthIssuerOptions` krever følgende ved aktivering, også utenfor Production; det finnes ingen test-/development-bypass i produksjonsregistreringen:

| Nøkkel | Betydning |
|---|---|
| `OAuth:Enabled` | Standard false. Deaktivert registrerer ingen OAuth-protokoll-/tilkoblingsruter. |
| `OAuth:Issuer` | Fast offentlig HTTPS-issuer på Web-origin, eksempel `https://app.example.no/`. Eksakt verdi må samsvare med discovery/iss og fremtidig validator. Ikke utledet fra Host-/forwarded-headers. |
| `OAuth:Resource` | Én kanonisk MCP-resource, eksempel `https://mcp.example.no/mcp`. Eksakt verdi kreves i authorize, code exchange og refresh. |
| `OAuth:ConnectionString` | Secret-referanse til auth-lagringen. Kan bruke samme PostgreSQL-database som Web med separat schema og egnet auth-rolle; ingen ny DB-prosess er nødvendig. |
| `OAuth:SigningCertificatePath/Password` | Absolutt secret-mount-sti og passord til vedvarende RSA PFX med privat nøkkel, minst 2048 bit, gyldig sertifikat. Ingen automatisk nøkkelgenerering. |
| `OAuth:EncryptionCertificatePath/Password` | Separat RSA PFX for koder/refresh og beskyttelse av cookie-key ring. Må være en annen nøkkel enn signeringssertifikatet. |
| `OAuth:CookieKeyDirectory` | Absolutt, vedvarende katalog for ASP.NET Data Protection. Katalogen må kunne skrives av Web-identiteten og beskyttes som hemmelig materiale. |
| `OAuth:PreviousSigningCertificates:{n}:Path/Password` | Tidligere signeringssertifikater for overlapp ved rotasjon; bare fra beskyttet konfigurasjon. |
| `OAuth:PreviousEncryptionCertificates:{n}:Path/Password` | Tidligere krypteringssertifikater for gamle koder/refresh og key ring. Behold så lenge lagrede Data Protection-nøkler trenger dem, også etter sertifikatets utløp. |
| `OAuth:Clients:{n}:ClientId/DisplayName/Secret/RedirectUris:{n}` | Operatørstyrte confidential klienter. Unikt ID, navn, unik hemmelighet på minst 32 tegn og eksakte HTTPS-callbacks uten wildcard. Ingen klient registreres automatisk ved vanlig oppstart. |

Konfigurasjon leveres fra driftsmiljøets secret-/konfigurasjonslager, ikke private nøkler i image eller kommandolinje. For miljøvariabler brukes .NETs vanlige `__` mellom ledd. Verken eksempeldomener eller testklient/-nøkler skal kopieres til produksjon. Faktisk ChatGPT-callback og klientautentiseringsmetode må bestemmes ved senere ekstern test; ingen ChatGPT-klient er opprettet her. Forhåndsregistrerte confidential klienter kan bruke bibliotekets etablerte secret-autentisering; ingen CIMD/DCR annonseres.

Utstederen krever HTTPS og S256. Produksjon trenger riktig DNS, TLS-terminering, allowlistede reverse proxies og videresendt scheme før autentisering; ingen endring av VM/proxy er utført. Publiser bare de nødvendige Web-/protokollrutene. Slå ikke på body-/sensitive logging i proxy, ASP.NET eller EF. OpenIddict-loggfiltreringen i Web bruker Warning for å unngå request-/tokenpayload fra bibliotekets Information-logger. PII/sensitive IdentityModel-logging er ikke aktivert.

## Migrering og klientregistrering

Migreringen er generert med `bash scripts/add-migration.sh AddOAuthIssuer OAuthDbContext`. Skriptets eksisterende ettargumentsform velger fortsatt domenekonteksten. OAuth-designfactory bruker kun en fiktiv design-connection; generering starter ikke vanlig Web eller migrerer data.

**Følgende driftskommandoer er dokumentasjon, ikke kjørt mot utvikling/produksjon.** De bruker miljøets konfigurasjon og må utføres med riktige separate rettigheter:

```text
dotnet TenantPlatform.Web.dll --oauth-migrate
dotnet TenantPlatform.Web.dll --oauth-register-clients
```

Begge er eksplisitte operatørmoduser som avslutter før vanlig Web-oppstart, domenemigrering, plattform-/demoinitialisering og HostedServices. De kan ikke kombineres. Første kommando migrerer bare OAuthDbContext med schema-/DDL-identitet. Andre bruker OpenIddict-managers til å opprette/oppdatere oppgitte klienter, permissions, PKCE og callbacks idempotent. Den fjerner ikke andre klienter implisitt; klientavvikling krever eksplisitt grant-/klienthåndtering. Vanlig Web-oppstart utfører ingen OAuth-migrering eller automatisk klientregistrering. Manglende OAuth-tabeller må håndteres før funksjonen publiseres.

Runtime trenger CRUD på standard OAuth-tabeller, men ikke schemaeierskap/DDL. Periodisk opprydding av utløpte tokenrader er ikke installert som ny HostedService; planlegg avgrenset teknisk opprydding via bibliotekets managers før bred drift, uten domenejobber eller brukerbypass. Den eksisterende domeneforbindelsen brukes fortsatt for lokale bruker-/innloggingsfakta. Migrationshistorikk og tabeller er separate fra domenets historie. Ingen OAuth-grants provisionerer brukere, medlemskap eller roller.

## Innlogging, samtykke og tilbakekalling

`/connect/authorize` håndterer GET/POST etter OpenIddict-validering. Uinnlogget GET går til vanlig `/login` med en kortlivet, Data Protection-beskyttet OAuth-interaksjon. Login returnerer bare til den faste lokale authorize-ruten med de forseglede parameterne. OAuth-login verifiserer eksisterende passord og lokal aktivstatus uten kontovalg/LastAccountId-skriving; ordinær GUI-login uten interaksjon beholder eksisterende kontovalg og navigasjon.

Samtykke binder verifisert lokal bruker til klient, resource, scope og øvrige validerte parametre. POST krever antiforgery og uendret beskyttet interaksjon. Lesetilgangen omfatter brukerens til enhver tid tilgjengelige medlemskontoer, uten nye lokale rettigheter. Refresh krever uttrykkelig godkjenning av `offline_access`; uten det utstedes ikke refresh-token. AccountId velges først og kontrolleres av senere MCP/Application-kall, ikke av utstederen.

`/connect/token` revaliderer lokal aktivstatus/innloggingsaktivering og grantens subjekt, klient, status og absolutte slutt før code exchange/refresh. Access-token er signert JWS med sub fra lokal User.Id, resource som audience og nødvendig protokollmetadata. Ingen cookie-roller, medlemslister, e-post eller forretningsdata kopieres. Koder/refresh beholder bibliotekets kryptering. Grant varer absolutt åtte timer; access-token varer maksimalt fem minutter og aldri forbi grantens slutt. Refresh roteres med null gjenbruksleeway og kan ikke forlenge granten. En OpenIddict-policyhook avkorter de ferdig klargjorte access-/refresh-prinsipalenes absolutte utløp før serialisering/lagring. Dermed kan heller ikke behandlingstid etter beregning av relativ levetid skyve utløpet forbi grantens slutt; privat avkortingsmetadata fjernes fra access-tokenet.

På `/oauth/connections` kan aktiv bruker se/tilbakekalle egne tilkoblinger. Eierskap håndheves i Application fra betrodd aktør, også ved direkte POST med en annens ID. Tilbakekalling revokerer authorization og tokenkjeden via biblioteket. Videre fornyelse stoppes; lokal signaturvalidering i kommende MCP kan fortsatt akseptere allerede utstedt JWT til utløp pluss maksimalt 30 sekunders toleranse. Vanlig logout tilbakekaller ikke OAuth-grants, og generell Web-cookie-/circuit-revokering er ikke bygget om.

## Backup, restart og rotasjon

Ta samordnet, kryptert backup av OAuth-tabeller, klientkonfigurasjon, alle nødvendige signerings-/krypteringssertifikater og persistent Data Protection-key ring. Gi Web-identiteten minst mulig filtilgang; mount aldri nøkler i et offentlig statisk filområde. Test gjenoppretting før ekstern bruk. Gammel DB-backup kan inneholde tidligere gyldige grants; ved usikker revokeringshistorikk må de tilbakekalles og brukerne koble til på nytt.

Ved signeringsrotasjon monteres ny gyldig nøkkel og gammel nøkkel beholdes i PreviousSigningCertificates. Biblioteket velger sertifikat etter gyldighet/utløp; sørg for at ny nøkkel faktisk foretrekkes, og kontroller JWKS og signerte tokens i testmiljø før utrulling. Behold gammel validering gjennom levetiden til utestående koder/refresh (opptil åtte timer), ikke bare access-tokenets fem minutter.

Ved krypteringsrotasjon må gamle nøkler beholdes i PreviousEncryptionCertificates både for OAuth og Data Protection. Cookie-key ring har eget nøkkellivsløp; gamle dekrypteringssertifikater kan trenges langt lenger enn åtte timer så lenge key ring-filer er kryptert med dem. Ikke slett gamle key ring-filer eller privatnøkler bare fordi et OAuth-token er utløpt. Flytting fra dagens Data Protection-plassering må bevare eksisterende ring og applikasjonsdiscriminator, eller planlegges som en engangs ny GUI-innlogging. Dette er ingen generell sesjonsmigrering/SSO-omlegging.

Restart med samme test-PFX-er og key ring er faktisk testet med både eksisterende cookie og refresh. Full rotasjon med overlapp og faktisk reverse proxy/sertifikatdrift er driftskontroller før ekstern pilot; de er ikke erklært kjørt.

## Automatisert og manuell akseptanse

Ny `TenantPlatform.OAuth.SmokeTests` bruker produksjonens auth-komposisjon, loginhandler og consent-/connection-komponenter på ASP.NET TestServer med HTTPS-request-URI. Den bruker ekte OpenIddict, standard SQL-lagring og isolert PostgreSQL. TestServer er ikke en ekte TLS-/proxy-/nettlesertest. Testnøkler/passord/klienter lages bare i testprosjektet i en midlertidig mappe; ingen prod-switch kan aktivere en testidentitet. Databasenavn/socket avvises hvis de ikke tilhører runnerens disponibel-klynge.

Kjør isolert med:

```text
bash scripts/test-agreements-postgres.sh TenantPlatform.OAuth.SmokeTests
```

Suite og eksisterende regresjoner kontrollerer innlogging/retur, GUI-login, samtykke/avslag/CSRF/manipulering, klient/PKCE/kodegjenbruk/callback/resource/scope, refresh-rotasjon og gjenbruk, deaktivert/inaktiv bruker før godkjenning/code exchange/refresh, egne/andres grants, faktisk kryptografisk tokenvalidering med Microsoft IdentityModel, levetidsgrenser med styrt TimeProvider og simulert forsinkelse før tokenklargjøring, restart, loggrensing og deaktivert hostprofil. Endelige kjøreresultater er samlet i [moderniseringsplanens 5B1-status](modernization-plan.md#22-steg-5b1-oauth-utsteder-i-web).

Gjenstående manuell kontroll: innlogging fra vanlig nettleser med/uten GUI-konto, samtykke/avslag, brukermeny og egne tilkoblinger på alle tre språk, utløp/tilbakekalling i flere økter og ny lasting etter lokal deaktivering. Ekte TLS/proxy, offentlige domener og callbacks, nøkkelbackup/rotasjon og containerbygg må verifiseres før ekstern drift. Ingen MCP-transport, intern klient, ChatGPT, agent eller rapportmotor er implementert/testet i 5B1.
