# Innebygd MCP-leseassistent (8A/8B/8D)

8B viderefører 8As leseagent med midlertidig samtalekontekst. 8A bygger på [registeret og konsumentkatalogen](mcp-connection-registry-operations.md), [den delegerte klienten](internal-mcp-client-operations.md) og [OAuth-utstederen](oauth-issuer-operations.md). GUI har panelet `/agent`, lenket fra brukermenyen. Application styrer agentløkken; modellen velger blant lokale function-definisjoner, og Application utfører bare versjonsbundet `McpRegistryService.CallAsync`. Det er ingen direkte datalesing, legacyfallback eller OpenAI-hostet MCP-tilkobling.

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
      "HistoryTurns": 6,
      "HistoryBytes": 262144,
      "ServerHistoryBytes": 16777216,
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

For egen TenantPlatform-profil brukes `agreements_list`, `agreements_attention` og 8Ds nye `agreements_query`, `agreements_aggregate`, `agreements_details`, når de er eksplisitt godkjent. Se [felt, filtre, grupperinger og SELECT-krav](mcp-agreement-read-contracts.md). `accounts_list` tas ikke inn i denne kontoavgrensede agenten. Kontoen er valgt gjennom vanlig, verifisert Web-kontovalg. Eget verktøys `accountId` får en synlig konstant i modelldefinisjonen og kontrolleres igjen før utføring; et annet kontoforslag avvises. Eget resultats schemaVersion/konto kontrolleres også. Eksterne skjemaer endres ikke, og deres eventuelle AccountId har ingen antatt lokal betydning.

JsonSchema.Net **8.0.5** (MIT; .NET 8-kompatibel pakke brukt fra .NET 10) validerer argumenter. Første versjon aksepterer et uttrykkelig begrenset skjemaformat: rotobjekt, typer/objektegenskaper/required/additionalProperties, items, enum/const, annotasjoner, tall-/lengde-/antallsgrenser, uniqueItems og UUID-format. Bare eventuell 2020-12 `$schema` støttes. Referanser, regex, sammensetnings-/utvidelsesvokabular og ukjente nøkkelord avvises for hele kjøringen, uten å omskrive eksterne skjemaer. Ingen skjemaressurser lastes; validatoren bruker et isolert register med avstengt fetch. Dybde og skjemastørrelse er begrenset, og duplikate JSON-nøkler avvises. Responses `strict=false` er eksplisitt fordi normalisering til strict ellers kan endre godkjente valgfrie felt. Lokal validering er fortsatt obligatorisk.

Én samtale binder betrodd lokal bruker, verifisert konto og én forbindelse. Dette er et serververn mot automatisk dataoverføring til andre forbindelser. Bytte av forbindelse krever sletting og ny samtale. Spørsmål, fullførte svar, konkrete kallargumenter, verktøyversjon, evidens-ID og SHA-256-fingeravtrykk kan beholdes i minnet. Kallene lagres som avhengigheter, ikke som instruksjoner til modellen om å utføre dem igjen. Ingen provider-outputelementer, skjult/kryptert reasoning, tokens eller credentials lagres i turhistorikken. Rå MCP-resultater beholdes bare innen aktiv kjøring.

### 8B: konservativ gjenhenting før kildeavhengig kontekst brukes

1. Fersk lokal bruker/konto, generell bruksrett, aktuell forbindelse og godkjent AutomaticRead-katalog kontrolleres før historikk brukes. Hver fullført tur bærer hele den transitive kildeavhengigheten: et tidligere modellsvar er ikke ufarlig tekst. Også svar basert på tidligere svar avhenger av alle kildene som inngikk i konteksten.
2. Application gjenhenter de nødvendige tidligere kildene gjennom samme **versjonsbundne** `McpRegistryService.CallAsync`, med fersk autorisasjon, validerte argumenter og egen kontobinding. Semantisk identiske kall med identisk resultat deler evidensidentitet. Ingen generell SQL-/Application-forretningslesing eller ny MCP-port brukes.
3. Bare når alle disse resultatene er byte-identiske med de lagrede fingeravtrykkene kan tidligere svar spilles inn sammen med **nyhentede** resultater. Historiske function-call/output-par er ferdig utført og revalidert evidens, ikke nye modellbestillinger. De får backendgenerert kildeproveniens og markeres som revaliderte tidligere kilder. Nye modellbestilte kall markeres separat. Gamle sitatnumre tilhører sitt opprinnelige svar; evidens-ID binder dem til aktuelle kilder.
4. Endret innhold, ressursinnsyn, side-/totalinformasjon eller verktøyversjon ugyldiggjør **alle** tidligere kildeavhengige svar og kall i samtalen. Bare brukerens opprinnelige spørsmål beholdes som intensjon. Modellen får disse spørsmålene og aktuell katalog og kan be om nye kall; den får ikke de gamle svarene, resultatene eller avledede kallargumentene. UI opplyser om fjernet kontekst. Avvist/utilgjengelig tidligere kilde stanser meldingen, ugyldiggjør svarene og viser en forståelig feil. Ugyldiggjøringen beholdes selv om meldingen feiler, slik at et senere vellykket svar også tømmer gammel GUI-kontekst.
5. Lokale rettigheter og verktøyversjoner kontrolleres fortsatt under løkken. Tidligere ressursavhengigheter gjenhentes dessuten før senere modellrunder og før endelig svar lagres/leveres. Alle faktiske gjenhentinger teller i eksisterende kall-, resultat- og tidsbudsjetter. Historikk og ferske resultater som sendes på nytt teller på nytt i modellinputbudsjettet. Om budsjettet ikke rekker, gis eksplisitt feil, uten stille gjenbruk/trunkering.

Dette kontrollerer faktisk ressursutvalg og data fra målserveren; verktøyadgang alene beviser ikke tilgang til gamle avtaler. Et fjernet avtalegrant gir derfor nytt listeinnhold og forkaster også gamle avledede svar. Det er fortsatt **ingen atomisk revokering eller konsistent database-snapshot gjennom modellrundene**. Tilgang/data kan endres mellom kontroller, og bytesammenligning er konservativ også ved ufarlige endringer i rekkefølge/volatile metadata. Eksterne målservere må håndheve sine rettigheter og levere korrekt, fullstendig kontrakt; identiske bytes er ikke online introspection eller en uavhengig kontroll av deres autorisasjonsmodell. AutomaticRead er fortsatt et eksplisitt lokalt tillitsvalg for de ekstra lesekallene.

Samtaler utløper absolutt etter 30 minutter; høyst fire per bruker, 1000 samtaler og 1000 aktive/avsluttende leases totalt. Aktivt arbeid har også utløpsdeadline. Et teknisk minnetimer rydder utløpte oppføringer minst hvert minutt; alle innganger avviser dem fra utløpstidspunktet. Timeren starter ingen agent, jobb eller databaselesing. Fullførte turer telles/bytesbelastes med konservativ kostnad `1024 + 4 × UTF-8-serialisering` per tur. Standard er seks turpar, 256 KiB belastning per samtale og 16 MiB samlet beholdt historikk inklusive reserverte lease-snapshots. Harde maksimum er 12 turpar, 1 MiB per samtale og 64 MiB samlet. Dette er et konservativt lagerbudsjett, ikke en påstand om eksakt CLR-heap/RSS; aktive modell-/MCP-buffere har fortsatt sine egne byte-, tids- og samtidighetsgrenser.

For store historikker mister eldste **hele turpar** med kilder, med eksplisitt varsel. Globalt fjernes eldste tur fra eldste inaktive samtale, med opprettelsesrekkefølge som stabil nøkkel ved likt utløp. Ved oppstart reserveres også plass til aktivt historikksnapshot. Hvis plass ikke kan frigjøres sikkert, beholdes ikke ny tur, eller kjøringen avvises som opptatt. GUI får faktisk antall beholdte turer og beskjed om et helt svar ikke kunne beholdes. Ingen modellbasert oppsummering eller tilfeldig tekstkapping.

Bare vellykket, ferdig svar kan committes én gang under den aktive leasen. Samtidige meldinger i én samtale avvises; per-brukergrensen omfatter også slettede/kansellerte leases helt til de avsluttes. Sletting frigjør dermed ikke kvote eller reservert historikkminne tidlig. Kansellerte, utløpte eller slettede kjøringer kan ikke gjenopplive samtalen med et sent svar. **Avbryt** stopper aktiv melding og beholder fullførte turer. **Ny samtale**, forbindelsesbytte, bruker-/kontobytte og logout fjerner relevant innhold og kansellerer arbeid. Sikkerhetsmessig tilbakekalling som oppdages ved lokal kontekstkontroll fjerner brukerens kontekst. Generell revokering av andre Web-cookies er fortsatt et eget spor.

GUI viser sammenhengende spørsmål/svar med kilder per svar. Gamle kildeavhengige svar skjules under kontroll og vises igjen bare etter vellykket revalidering; ved ugyldiggjøring vises spørsmålene uten de gamle svarene. Det finnes ingen historikk-GET som kan omgå revalidering. Nettleserlasting/restart gjenoppretter ikke samtalen, og GUI opplyser om dette. Ved utløp tømmes visningen. Modellens forståelse av oppfølgingsreferanser er testet med syntetisk protokoll, ikke en reell modellprøve. Legacyverktøyet agreements_list har fortsatt ingen byggfelt. 8D gir bygg/dimensjoner gjennom agreements_query og SQL-gruppering gjennom agreements_aggregate; agenten skal bruke de nye godkjente verktøyene for slike spørsmål. Ingen felt skal konstrueres når kilden mangler dem.

**Én-instanspilot:** samtaletilstand og kvoter er prosesslokale. Kjør bare én aktiv Web-instans med agenten. Lastbalansert/flerinstans agent krever felles kansellering, samtalebinding og kvoter før aktivering; sticky sessions alene gir ikke global brukergrense eller sikker konto-/brukerbyttekansellering.

## GUI, grenser og feil

Panelet er en statisk Blazor-komponent i eksisterende layout/Bootstrap med en liten nettleseradapter. Hver POST får eget HTTP-/DI-scope, fersk cookieautentisering, antiforgery og Application-autorisasjon. Ingen request-scoped tjenester holdes i en circuit; ingen backendvideresending av cookies til modell/MCP. Nettleseren bruker ordinære same-origin requests.

Modellsvar, kildenavn og status rendres som tekst, ikke HTML/Markdown. Ingen automatisk lasting av modellbilder, iframes eller lenker. Kildelisten opprettes fra faktiske kall. Egne listekilder viser faktisk forespurt/returnert side, sidestørrelse, returnert antall, filtrert TotalCount, hasMore/nextPage, complete, snapshotConsistent og limitReached. Attention viser kontodato, egne kategoritall og horisont, med fast varsel om overlapp. 8D-aggregater viser gruppering, unik total, gruppeantall, fullstendighet, overlapp og snapshotstatus. Eksterne kilder har et eget varsel om at fullstendighet/paginering følger deres kontrakt. Ingen totalsummer på tvers av sider/kategorier beregnes automatisk.

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
node --test tests/TenantPlatform.McpClient.SmokeTests/AgentPanelChecks.cjs
```

Solution, sju relevante testprosjekter og alle sju suiter i kommandoen over bestod 17. oktober 2026 etter siste produksjonsendring (seks med isolert PostgreSQL, Dashboard med eksisterende InMemory-/SQL-/komponentkontroller). Ekstra McpClient-kjøring med granttilbakekalling under endelig modellrespons bestod også, inklusive avvist commit og fravær av avvist svar i neste kontekst. Begge private testklynger ble stoppet og fjernet. Node-kontrollen bestod; dette er syntetisk DOM, ikke nettleserverifikasjon. Eksisterende NU1902/NU1903 består.

Agentkontrollene ligger i den eksisterende McpClient-suiten og bruker produksjonsadapteren mot syntetiske Responses-protokollsvar, faktiske OAuth-/MCP-biblioteker og isolert PostgreSQL. Testtransport og modellfixture finnes bare i testassemblyet; ingen produksjonsinnstilling aktiverer testidentitet, test-URL eller syntetisk modell. Vanlig Web-oppstart, utviklings-/produksjonsdatabase, ekte modellkall og ekte utsendinger inngår ikke. Se [moderniseringsplanens 8B-status](modernization-plan.md#28-steg-8b-midlertidig-sammenhengende-samtalekontekst) for faktisk sluttverifikasjon.

Før bruk i drift gjenstår separat reell modellprøve med godkjente syntetiske data/nøkkel, interaktiv GUI på alle tre språk, flere nettleserøkter, faktisk containerbygg, TLS/proxy/egress og leverandørens faktiske grant-/feiloppførsel. Ingen av disse følger automatisk av TestServer-verifikasjon. Generell Web-sesjonsrevokering, rapportdesigner, bakgrunnsagent, skrive-/bekreftelsesflyt og vedvarende samtalehistorikk er egne senere oppgaver.

## Kontrollerte primærkilder

Kontrollert 17. oktober 2026: [OpenAI function calling](https://developers.openai.com/api/docs/guides/function-calling), [Responses API](https://developers.openai.com/api/reference/responses/overview), [stateless reasoning-kontinuitet](https://developers.openai.com/api/docs/guides/reasoning#preserve-reasoning-without-stored-responses) og [offisiell .NET-støtte](https://developers.openai.com/api/docs/libraries). Den offisielle OpenAI **2.14.0**-pakkens [publiserte metadata](https://api.nuget.org/v3-flatcontainer/openai/2.14.0/openai.nuspec) har .NET 10-target og MIT-lisens. Biblioteket er kontrollert, ikke installert: adapteren følger den eksisterende Responses-HTTP-konvensjonen fra avtaleanalyse, separat fra dens arbeidsflyt, med egne bytegrenser og ingen SDK-retries. Ingen modell-/prisvalg er utledet fra dokumentasjonseksemplenes modellnavn.

Skjemavalidering: [bibliotekets dokumentasjon](https://docs.json-everything.net/schema/basics/) og [valgt 8.0.5-pakkes lisens/targets](https://api.nuget.org/v3-flatcontainer/jsonschema.net/8.0.5/jsonschema.net.nuspec). Den valgte pakken er eksplisitt MIT; nyere pakkeutgaver kan ha andre vilkår for distribuerte binærpakker og skal vurderes ved en senere oppgradering.

## 8D: operatøroppfølging

De fire nye SELECT-tabellene og nye verktøy må aktiveres kontrollert som beskrevet i [avtalekontraktene](mcp-agreement-read-contracts.md#select-rolle-og-aktivering). Ingen automatisk godkjenning eller omgåelse av registerpolicy. Brukeren opplyser at lokal reell modellprøve, GUI og proxy/TLS er utført før dette steget; nye 8D-forløp krever egen manuell kontroll. Containerbygg/deployment i 8C er fortsatt utsatt.
