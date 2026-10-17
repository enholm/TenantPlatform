# Innebygd MCP-leseassistent (8A)

8A bygger på [registeret og konsumentkatalogen](mcp-connection-registry-operations.md), [den delegerte klienten](internal-mcp-client-operations.md) og [OAuth-utstederen](oauth-issuer-operations.md). GUI har panelet `/agent`, lenket fra brukermenyen. Application styrer agentløkken; modellen velger blant lokale function-definisjoner, og Application utfører bare versjonsbundet `McpRegistryService.CallAsync`. Det er ingen direkte datalesing, legacyfallback eller OpenAI-hostet MCP-tilkobling.

## Aktivering og datadeling

Deaktivert som standard. Manglende modell, nøkkel eller gyldige grenser gjør assistenten utilgjengelig uten å hindre vanlig GUI. Bare serverkonfigurasjon kan velge modell/nøkkel. Ingen modell eller pris er forhåndsvalgt. Modellen må støtte Responses API og function calling. Følgende er en **inaktiv konfigurasjonsillustrasjon**, ikke en fil som lastes automatisk:

```json
{
  "ReadAgent": {
    "Enabled": false,
    "Model": "",
    "ApiKey": "",
    "Limits": {
      "PromptCharacters": 4000,
      "ContextBytes": 196608,
      "TotalModelBytes": 1048576,
      "Tools": 12,
      "Rounds": 5,
      "Calls": 8,
      "ResultBytes": 65536,
      "TotalResultBytes": 131072,
      "AnswerCharacters": 12000,
      "OutputTokens": 4096,
      "Seconds": 60,
      "ConcurrentRunsPerUser": 1
    }
  }
}
```

Bruk miljøets beskyttede konfigurasjon, eksempelvis `ReadAgent__ApiKey`; aldri nøkkel i repo, image, nettleser eller kommandohistorikk. Egress går fra Web til den faste `https://api.openai.com/v1/responses`, uten redirects, cookies, HTTP-proxy eller automatiske retries i adapteren. OAuth/MCP bruker fortsatt sine operatørgodkjente profiler og eksisterende nettverksvern. Ikke aktiver body-/header-/sensitive logging i app, proxy eller overvåking. Den dedikerte modellklientens HttpClient-loggere er fjernet. Ingen prompt-/svar-/verktøylogger er lagt til.

Panelet opplyser **før bruk** at spørsmål, godkjente verktøybeskrivelser/skjemaer og hentede data sendes til OpenAI. Brukeren velger én forbindelse og aksepterer datadelingen før serveren oppretter samtalen. `store=false` og ingen `previous_response_id`, provider-conversation, Files API eller innebygde nett-/fil-/kodeverktøy. Dette er ikke lokal AI eller en påstand om null oppbevaring hos leverandøren; drifts-/produktansvarlig må avklare leverandørvilkår, databehandling og tillatt innhold før aktivering.

## Tilgang, kontekst og verktøy

Bare katalogens godkjente `AutomaticRead`-verktøy på den **uttrykkelig valgte** forbindelsen inngår. Bekreftelsesverktøy er ikke tilgjengelige. Hashalias binder stabil ConnectionId, originalnavn og metadataversjon; kolliderende originalnavn på andre servere gir andre alias. Modellen får aldri tokens, lokal innringer-ID, administratorstatus eller ferdig autorisert datascope.

For egen TenantPlatform-profil brukes bare `agreements_list` og `agreements_attention`. `accounts_list` tas ikke inn i denne kontoavgrensede agenten. Kontoen er valgt gjennom vanlig, verifisert Web-kontovalg. Eget verktøys `accountId` får en synlig konstant i modelldefinisjonen og kontrolleres igjen før utføring; et annet kontoforslag avvises. Eget resultats schemaVersion/konto kontrolleres også. Eksterne skjemaer endres ikke, og deres eventuelle AccountId har ingen antatt lokal betydning.

JsonSchema.Net **8.0.5** (MIT; .NET 8-kompatibel pakke brukt fra .NET 10) validerer argumenter. Første versjon aksepterer et uttrykkelig begrenset skjemaformat: rotobjekt, typer/objektegenskaper/required/additionalProperties, items, enum/const, annotasjoner, tall-/lengde-/antallsgrenser, uniqueItems og UUID-format. Bare eventuell 2020-12 `$schema` støttes. Referanser, regex, sammensetnings-/utvidelsesvokabular og ukjente nøkkelord avvises for hele kjøringen, uten å omskrive eksterne skjemaer. Ingen skjemaressurser lastes; validatoren bruker et isolert register med avstengt fetch. Dybde og skjemastørrelse er begrenset, og duplikate JSON-nøkler avvises. Responses `strict=false` er eksplisitt fordi normalisering til strict ellers kan endre godkjente valgfrie felt. Lokal validering er fortsatt obligatorisk.

Én samtale binder betrodd lokal bruker, verifisert konto og én forbindelse. Dette er et serververn mot automatisk dataoverføring til andre forbindelser, også når brukeren har tilgang til flere. Ny melding sender bare det nye spørsmålet, ikke tidligere spørsmål/svar/resultater. Innen samme kjøring kontrolleres lokal tilgang og alle brukte kilders godkjente versjon på nytt før mer kontekst sendes og før ferdig svar leveres. Tilbakekalling som oppdages under henting stanser resultatet før neste modellrunde. Ekstern grant-/ressursrevokering følger fortsatt målserverens/tokenflytens regler, ikke kontinuerlig introspection eller en ny atomisk revokeringsgaranti.

Bare eierskap/konto/forbindelse, utløp og kansellering lagres i minnet. Ingen varig chatlogg eller tokens i samtalelageret. Samtaler utløper etter 30 minutter; høyst fire metadataoppføringer per bruker og 1000 totalt. Ny samtale/avbryt fjerner den aktuelle oppføringen. Vellykket bruker-/kontobytte og logout i Web avbryter og fjerner alle brukerens samtaler på denne instansen. Panelet kontrollerer også fersk cookie-/kontokontekst hvert andre sekund og tømmer visningen ved endring/utilgjengelig kontroll. Gamle nettleser-cookies i andre økter blir ikke generelt revokert av dette.

**Én-instanspilot:** samtaletilstand og kvoter er prosesslokale. Kjør bare én aktiv Web-instans med agenten. Lastbalansert/flerinstans agent krever felles kansellering, samtalebinding og kvoter før aktivering; sticky sessions alene gir ikke global brukergrense eller sikker konto-/brukerbyttekansellering.

## GUI, grenser og feil

Panelet er en statisk Blazor-komponent i eksisterende layout/Bootstrap med en liten nettleseradapter. Hver POST får eget HTTP-/DI-scope, fersk cookieautentisering, antiforgery og Application-autorisasjon. Ingen request-scoped tjenester holdes i en circuit; ingen backendvideresending av cookies til modell/MCP. Nettleseren bruker ordinære same-origin requests.

Modellsvar, kildenavn og status rendres som tekst, ikke HTML/Markdown. Ingen automatisk lasting av modellbilder, iframes eller lenker. Kildelisten opprettes fra faktiske kall. Egne listekilder viser faktisk forespurt/returnert side, sidestørrelse, returnert antall, filtrert TotalCount, hasMore/nextPage, complete, snapshotConsistent og limitReached. Attention viser kontodato, egne kategoritall og horisont, med fast varsel om overlapp. Eksterne kilder har et eget varsel om at fullstendighet/paginering følger deres kontrakt. Ingen totalsummer på tvers av sider/kategorier beregnes automatisk.

| Grense | Standard og håndheving |
|---|---|
| Spørsmål / svar | 4000 / 12000 UTF-16-tegn. For langt svar avvises helt. |
| Kontekst | 192 KiB serialisert input/verktøy per modellrunde, med adaptergrense som også reserverer 8 KiB til instruksjoner/framing. Providerrespons maksimalt ContextBytes. |
| Samlet modellinput | 1 MiB per melding. Hver retransmisjon teller på nytt, med konservativ 8 KiB reserve per runde. |
| Verktøy / runder / kall | 12 / 5 / 8. For stort utvalg gir eksplisitt feil, ikke stille utelatelse. Operatør/administrator må avgrense utvalget eller velge lovlige grenser. |
| MCP-resultat / samlet nye resultater | 64 / 128 KiB. Ingen stille trunkering. Eksisterende MCP-/transportgrenser gjelder samtidig. |
| Modellutdata | max_output_tokens 4096; ufullført/refusert/ukjent output gir intet ferdig svar. Skjult reasoning vises/logges ikke. |
| Tid / samtidighet | 60 sekunder samlet agentbudsjett; 1 kjøring per bruker på tvers av samtaler. Ingen ventekø. |
| HTTP | 96 KiB innkommende JSON, 5 sekunders bodylesing innen totalbudsjettet. Ingen klientvalgt bruker-ID eller URL. |

Konfigurasjon har harde øvre grenser: prompt 16000 tegn, kontekst 512 KiB, samlet modellinput 2 MiB, 32 verktøy, 10 runder, 20 kall, enkeltresultat 64 KiB, samlede resultater 256 KiB, svar 32000 tegn, 8192 output tokens, 120 sekunder og 4 samtidige kjøringer per bruker. Endring krever kontrollert restart; ingen GUI-konfigurasjon av modell/hemmeligheter.

Avbryt føres til modell-HTTP, MCP-klient og videre til målserver/databasens cancellation når støttet. Eksisterende 15-sekunders MCP-kall, OAuth-grenser og MCP-hostens kortere budsjett er beholdt; agenten prøver ikke å presse hele løkken inn i ett MCP-budsjett. En mislykket kilde/modell, revokert adgang, grense eller ufullført respons stanser hele meldingen med forståelig lokal feilmelding. Ingen mislykket henting vises som null treff eller et ferdig delvis svar. Ny melding kan hente på nytt. Ingen forretningswrites/initialisering; eventuell nødvendig OAuth-tokenfornyelse kan fortsatt oppdatere den personlige credential-plassen. `AutomaticRead` er et eksplisitt administratortillitsvalg, ikke et teknisk bevis på en ekstern servers faktiske effekter.

## Verifikasjon

```text
dotnet build TenantPlatform.sln
bash scripts/test-agreements-postgres.sh TenantPlatform.McpClient.SmokeTests TenantPlatform.OAuth.SmokeTests TenantPlatform.Mcp.SmokeTests TenantPlatform.AccountRegisters.SmokeTests TenantPlatform.AgreementList.SmokeTests TenantPlatform.Agreements.SmokeTests TenantPlatform.Dashboard.SmokeTests
node --check src/TenantPlatform.Web/wwwroot/js/read-agent.js
```

Agentkontrollene ligger i den eksisterende McpClient-suiten og bruker produksjonsadapteren mot syntetiske Responses-protokollsvar, faktiske OAuth-/MCP-biblioteker og isolert PostgreSQL. Testtransport og modellfixture finnes bare i testassemblyet; ingen produksjonsinnstilling aktiverer testidentitet, test-URL eller syntetisk modell. Vanlig Web-oppstart, utviklings-/produksjonsdatabase, ekte modellkall og ekte utsendinger inngår ikke. Se [moderniseringsplanens 8A-status](modernization-plan.md#27-steg-8a-innebygd-mcp-basert-leseassistent) for faktisk sluttverifikasjon.

Før bruk i drift gjenstår separat reell modellprøve med godkjente syntetiske data/nøkkel, interaktiv GUI på alle tre språk, flere nettleserøkter, faktisk containerbygg, TLS/proxy/egress og leverandørens faktiske grant-/feiloppførsel. Ingen av disse følger automatisk av TestServer-verifikasjon. Generell Web-sesjonsrevokering, rapportdesigner, bakgrunnsagent, skrive-/bekreftelsesflyt og vedvarende samtalehistorikk er egne senere oppgaver.

## Kontrollerte primærkilder

Kontrollert 17. oktober 2026: [OpenAI function calling](https://developers.openai.com/api/docs/guides/function-calling), [Responses API](https://developers.openai.com/api/reference/responses/overview), [stateless reasoning-kontinuitet](https://developers.openai.com/api/docs/guides/reasoning#preserve-reasoning-without-stored-responses) og [offisiell .NET-støtte](https://developers.openai.com/api/docs/libraries). Den offisielle OpenAI **2.14.0**-pakkens [publiserte metadata](https://api.nuget.org/v3-flatcontainer/openai/2.14.0/openai.nuspec) har .NET 10-target og MIT-lisens. Biblioteket er kontrollert, ikke installert: adapteren følger den eksisterende Responses-HTTP-konvensjonen fra avtaleanalyse, separat fra dens arbeidsflyt, med egne bytegrenser og ingen SDK-retries. Ingen modell-/prisvalg er utledet fra dokumentasjonseksemplenes modellnavn.

Skjemavalidering: [bibliotekets dokumentasjon](https://docs.json-everything.net/schema/basics/) og [valgt 8.0.5-pakkes lisens/targets](https://api.nuget.org/v3-flatcontainer/jsonschema.net/8.0.5/jsonschema.net.nuspec). Den valgte pakken er eksplisitt MIT; nyere pakkeutgaver kan ha andre vilkår for distribuerte binærpakker og skal vurderes ved en senere oppgradering.
