# Tjenesteforespørsler gjennom MCP og personlige rapporter (9E)

Status: **9E er implementert og automatisert verifisert.** Interaktiv 9E-kontroll og driftsaktivering gjenstår. Ingen driftsdatabase, ekte modelltransport eller containerbygg brukes i dette steget. Brukeren opplyste ved oppstart at siste rapportfunksjonalitet fra 9D er ferdig testet; kontrollene nedenfor gjelder nye 9E-endringer separat.

## Valg av område

Tjenesteforespørsler velges foran belegg/enheter og leasing. `ServiceRequestAccess.ReadScope` og eksisterende detaljlesing har et tydelig requester-/tenant-/leverandør-/administratoromfang. Lagret `ServiceRequestStatus`, bygg og tidspunkt gir nyttige lister og entydige, ikke-overlappende tellinger uten økonomiske beregninger.

Belegg har autoriserte liste-/detaljoperasjoner, men listen bevarer et dokumentert bredt administrativt legacyomfang og dagens gyldighetsberegning. Rapportbegreper som enhet, beleggsforekomst, nåværende belegg og ledighet må velges særskilt. Leasing har modne autoriserte lesere og rapporter, men finansieringsrevisjoner, betalingsplaner, fakturaallokering og oppfølging krever et større eksplisitt rapportutvalg. Ingen av disse to områdene eksponeres nå.

## Tilgang og metadata

Application `ServiceRequestReportingReader` tar bare konto, deklarativt filter, side/gruppering og kansellering. Den henter fersk `GetAccountActorScopeAsync` per kall og gjenbruker `ServiceRequestAccess.ReadScope`. Synlige forespørsler er unionen av egen bestilling, administrativt ressursomfang, leietakerorganisasjon og tildelt leverandørorganisasjon. Bygg-/organisasjonsvilkår fra samme rolletildeling holdes samlet. Konto og aktiv bruker/medlemskap kontrolleres først; PlatformAdmin gir ingen kontobypass.

Infrastructure gjenbruker nøyaktig samme `ServiceRequestData.Visible` som detaljlesingen. Delmengder kombineres nå som ID-er før radprojeksjon; det bevarer synlige rader og unngår at SQL-unionen må lese private kolonner. Autorisasjonsmodellen ligger fortsatt i Application. List og aggregate deler én SQL-filterfunksjon; tilgang avgrenses før Count/GroupBy/paging. Ingen initialisering, historikk, outbox, statusendring eller SaveChanges inngår.

Bygg og valgfri enhet slås bare opp i aktuell konto. En ugyldig/utenlandsk referanse blir null uten å fjerne en autorisert forespørsel. Rapporten returnerer ingen bestilleridentitet, e-postadresse, leverandørkontakt, skjemafelt, kommentar, meldingshistorikk, svartoken eller transportdata. `Title` er den faktisk lagrede, valgfrie tittelen; ingen tekst utledes fra skjemasvar eller e-post. Manglende tittel vises med eksisterende tomverdimerkning.

## Verktøykontrakter

Begge verktøy krever eksisterende MCP-bearerprofil med korrekt issuer, resource/audience, delegert bruker og klient, samt scope `tenantplatform.service_requests.read` **i tillegg til** det eksisterende basis-scope `tenantplatform.agreements.read`. Det gamle scope utvides ikke til et nytt domene. MCP-verktøy kaller Application, ikke GUI-fasader eller Infrastructure direkte.

| Verktøy | Parametere | Resultat |
|---|---|---|
| `service_requests_query` | `accountId` påkrevd; `search`, `status`, `buildingId`, `missingBuilding`, `page` valgfritt | SchemaVersion 1, konto, normalisert filter, faktisk TotalCount og paging, avgrensede rader |
| `service_requests_aggregate` | Samme filter, `groupBy` påkrevd: Building eller Status; ingen page | SchemaVersion 1, konto/filter, komplett SQL-partisjon og faktisk total |

Filtre kombineres med AND. `search` er trimmet, ikke-sensitivt delstrengsøk **bare i Title**, maksimalt 200 Unicode-tegn, uten kontrolltegn. Nulltittel utelates bare når et søkevilkår krever treff. Tomt/hvitt søk gir ingen søkeavgrensning. Status er lagret enum: Draft, Submitted, Approved, InProgress, Completed, Rejected, Cancelled, Failed. Ingen beregnet «åpen», arkivstatus eller historisk status konstrueres.

`buildingId` er en eksakt ikke-tom GUID med kontobegrenset referanseoppslag; ukjent/fremmed bygg gir null treff. `missingBuilding=true` betyr manglende gyldig byggreferanse i aktuell konto og kan ikke kombineres med buildingId. Det finnes ikke datofilter, relativ periode, tjenestetype-/leverandørfilter, beløp eller frie uttrykk i denne første kontrakten. Ukjente parametere, numeriske enumverdier og klientvalgt innringer avvises.

Rader: Id, valgfri Title, lagret Status, CreatedAt (UTC-instant), nullable SubmittedAt/CompletedAt og nullable Building/Unit (Id/Name). Tidspunktene er lagrede hendelsesfelter, ikke en tilstandshistorikk. Sortering er CreatedAt synkende, deretter Id stigende. Fast sidestørrelse 25, ønsket side 1–10000, side klemmes til faktisk siste side. TotalCount gjelder hele det autoriserte, filtrerte settet. `complete` betyr at alle treff finnes i dette svaret; en siste side med to av 27 rader er fortsatt ikke komplett. `hasMore`, `nextPage`, `limitReached` viser videre henting; ved sidegrensen må filteret snevres inn. Det er ingen snapshotgaranti mellom spørringer/sider.

Gruppering Status teller unike forespørsler per lagret status. Building teller per gyldig kontobygg; manglende/fremmed referanse gir key `missing`, name=null. Hver forespørsel er i nøyaktig én gruppe. Total er summen av den komplette SQL-partisjonen, ikke en ny racende Count-spørring eller sum av listesider. Maksimalt 100 grupper; 101 gir `RESULT_TOO_LARGE`, aldri trunkerte grupper eller misvisende total. Null treff gir total 0 og tom gruppe-/radliste.

Eksisterende HTTP-/MCP-grenser består: 16 KiB request, 64 KiB ferdig svar, frister, kansellering og kvoter. Et for stort svar gir feil uten delvis data. Ukjent/utilgjengelig konto gir samme sikre `RESOURCE_UNAVAILABLE`, manglende nytt scope gir `AUTHORIZATION_REQUIRED`, ugyldig input `INVALID_ARGUMENT`, avbrudd `CANCELLED`, andre feil `TEMPORARILY_UNAVAILABLE`. Rå SQL-/OAuth-/transportfeil eksponeres ikke.

## Eksplisitt aktivering, personlig OAuth og SELECT

Dette oppdraget utfører ikke følgende driftsendringer:

1. Operatøren velger hvilke OAuth-klienter som skal kunne be om forespørselslesing, med `OAuth:Clients:<index>:AllowServiceRequests=true`. Standard false bevarer tidligere klientfullmakter. Kjør eksisterende eksplisitte `--oauth-register-clients`-inngang; ingen klientoppdatering gjøres ved vanlig oppstart.
2. Innebygd klients scopes følger dette eksplisitte valget. Profilversjonen endres ved aktivering; gammel personlig autorisering oppgraderes ikke. Koble til på nytt og samtykk til forespørselslesing. Samtykkesiden forklarer den nye datatilgangen. Gamle JWT-er med bare avtalescope kan ikke kalle forespørselsverktøy. Utløp, refresh, audience, tilbakekalling og lokal fersk ressurstilgang følger eksisterende OAuth-regler; scope er ingen lokal rolle.
3. Kontoadministrator må oppdatere registerets profilbinding eksplisitt ved profilendring, oppdage de nye verktøyene, velge tillatt publikum/AutomaticRead og godkjenne hver eksakte metadataversjon. Ingen automatisk godkjenning. Lagrede avtalesider beholder definisjoner og bindinger; dersom operatøren endrer profilen, må en ny binding velges og lagres uttrykkelig gjennom eksisterende editor. Uendret standardoppsett beholder tidligere profilhash.
4. DBA kan gi den dedikerte MCP-leserrollen kun disse ekstra kolonnene, i tillegg til eksisterende SELECT for autorisasjon/bygg/enheter:

```sql
GRANT SELECT (
  "Id", "AccountId", "RequesterUserId", "RequesterOrganizationId",
  "AssignedServiceProviderOrganizationId", "BuildingId", "UnitId",
  "Title", "Status", "CreatedAt", "SubmittedAt", "CompletedAt"
) ON public.service_requests TO tenantplatform_mcp_reader;
```

Ingen SELECT på hele tabellen, service definitions, feltverdier, meldinger, outbox eller tokens er nødvendig. Ingen nye skriveprivilegier, eierskap, CREATE eller brede/default-privilegier skal gis. MCPs SaveChanges-vakt og privilegiekontroll beholdes. Manglende SELECT skal gi en sikker kildefeil; hosten reparerer ikke rollen. Testene oppretter bare egne engangsroller i den isolerte klyngen.

Endret klienttillatelse eller tilbakekalt OAuth-grant gjør ikke allerede signaturvaliderte JWT-er umiddelbart ugyldige. Deres eksisterende korte levetid består. Application kontrollerer likevel aktiv lokal bruker, medlemskap og rolletilgang på hvert datakall. Registeret kontrollerer i tillegg personlig autorisering, verktøyversjon og godkjenning for hvert rapport-/agentkall. Generell Web-sesjonsrevokering er fortsatt en egen oppgave.

## Rapportsider og kompatibilitet

PageDefinition v3 har `Area=ServiceRequests`, en egen typet `RequestFilter`, `Period=All` med null måneder/datoer, og ingen avtalevilkår i det bakoverkompatible Filter-feltet. V1/v2 betyr fremdeles Agreements når Area mangler; v1-konverteringen og rå original for revisjonskontroll er bevart. Ukjente versjoner/områder og kryssdomenevilkår avvises. En side har én tilkobling, ett område og backendbundne eksakte profil-/verktøyversjoner. Modellforslag kan ikke velge identiteter, referanse-ID-er, bindinger eller fullmakt.

To avgrensede Application-datakildeadaptere leser sine respektive kontrakter. Felles runner eier aktørkontroll, personlige registerkall, versjoner, frister, svargrense og revisjonsvern. Ingen datalesing går direkte til lokal DB. Det nye området har Metric, BuildingChart (Building/Status) og paginert AgreementTable; de to siste navnene er eksisterende wire-tokens for gjenbrukte diagram-/tabellkomponenter, ikke avtale-data i forespørselsrapporten. Kolonner: Title, Status, Building, Unit, CreatedAt, SubmittedAt, CompletedAt. Maksimalt ett av hver komponent, ordnet, minst én; tabell krever minst én tillatt kolonne.

Metric/diagram deler ett aggregate; tabell alene bruker ett query; kombinasjonen bruker ett aggregate og ett query. Begge mottar samme normaliserte filter. Ingen atomisk snapshot antas på tvers av kall. Felles renderer bruker områdets faste felt-/etikettadapter og textContent; den viser ekte stolper, lokaliserte statusetiketter, tomverdier og videre sidehenting. Tidspunkt vises som UTC/ISO, uten en oppfunnet lokal rapportdato.

Eksisterende chatutkast, separat preview/lagring, original/kopi, deterministisk endringsoversikt og revisjonskonflikter gjenbrukes. Lagret side kan kjøres med ReadAgent deaktivert uten modellkall. Eksisterende kilderevalidering gjelder også forespørselsdata; endret/tilbakekalt evidens kan ikke brukes som fersk konklusjon. Området til en allerede åpnet side kan ikke byttes i et modellforslag. Perioder, navneoppløsning av bygg og andre domener/filterutvidelser krever et senere eksplisitt kontraktsteg.

## Eksempelprompter

- «Lag en side som heter Mine tjenesteforespørsler. Ta med alle datoer og statuser. Vis antall, et stolpediagram fordelt på status og en paginert tabell med tittel, status, bygg og opprettet tidspunkt.»
- «Lag en rapport over fullførte tjenesteforespørsler, uten tidsavgrensning. Vis antall og et stolpediagram fordelt på bygg, uten tabell.»
- Etter åpning via Rediger med AI: «Endre rapporten til bare innsendte tjenesteforespørsler. Behold byggdiagrammet og legg til en paginert tabell med tittel, status og innsendt tidspunkt.»

Dette er støttede deklarasjoner; syntetiske modellsvar beviser verktøy-/utkastkontrakten, ikke en levende modells språkforståelse. Ustøttede ønsker skal avklares, ikke erstattes med oppdiktede status-/periode-/økonomiregler. OpenAI [Function calling](https://developers.openai.com/api/docs/guides/function-calling) er kontrollert for objektskjema og eksplisitt strict=false; eksisterende adapter og lokale valideringsgrenser beholdes.

## Verifikasjon og manuell kontroll

Verifikasjon 25. oktober 2026:

- `dotnet build TenantPlatform.sln`: exitkode 0 etter siste produksjonsendring. Ingen nye kompilatorvarsler; eksisterende NU1902/NU1903 for System.Security.Cryptography.Pkcs består.
- `MCP_CLIENT_TEST_REQUESTS_ONLY=1 bash scripts/test-agreements-postgres.sh TenantPlatform.McpClient.SmokeTests`: exitkode 0 etter siste produksjonsendring. Ny 9E-kjede bruker syntetisk Responses-transport og reelle OAuth-/MCP-biblioteker/PostgreSQL: eksplisitt klassifisering/godkjenning, ordinært agentsvar/proveniens, revaliderte samtalekilder, prompt/preview/separat lagring, faktisk SSR, redigering/endringsoversikt, gjenåpning og paging med AI av, sperret kryssdomene, ukjent område i samlingen, tilbakekalt godkjenning og nektet automatisk versjonsoppgradering. Også etterfølgende interne klientregresjoner bestod. Filteret finnes kun i testeksekverbaren og endrer ingen produksjonskvoter eller autentisering.
- `MCP_CLIENT_TEST_COMPONENTS_ONLY=1 bash scripts/test-agreements-postgres.sh TenantPlatform.McpClient.SmokeTests`: exitkode 0 etter siste produksjonsendring. Eksisterende avtalesiders sju komponentkombinasjoner, begge grupperinger, All/NextMonths, gamle v1-definisjoner uten nye felter, minnekonvertering uten skriving, versjons-/revisjonsvern og AI-avslått kjøring bestod, sammen med interne klientregresjoner.
- `bash scripts/test-agreements-postgres.sh TenantPlatform.Mcp.SmokeTests TenantPlatform.OAuth.SmokeTests TenantPlatform.AgreementList.SmokeTests`: exitkode 0 etter siste produksjonsendring. Nye Application-lesere prøves med kolonnebegrenset SQL-rolle og SaveChanges-vakt; bestiller/leietaker/leverandør/byggadministrator/kontoadministrator/PlatformAdmin, samme filtre i list og SQL-aggregate, null-/fremmed metadata, statusser, paging, parallell lesing, kansellering, overflow og tilbakekalling inngår. Faktisk samtykke/PKCE/token/MCP, nektet avtalescope alene, SELECT uten Comment/ReplyToken/meldinger og blokkert SQL-skriving inngår. Eksisterende ServiceRequestChecks og øvrige liste-/autorisasjons-/rapportlesere i AgreementList-suiten bestod i InMemory og PostgreSQL. OAuth-suiten bestod uendret. Alle klynger ble stoppet og fjernet.
- JS-syntakskontroll og ni Node-kontroller i AgentPanelChecks.cjs/PersonalPagesPanelChecks.cjs bestod. Ny typed forespørselseditor, nøytralt avtalefilter, bare All-periode, forespørselskolonner/statusetiketter, reelle stolper/nullverdier/paging og eksisterende sikker rendering inngår. Nye ressursnøkler er like og unike i alle tre språk; XML og diffkontroll bestod.

- `env -u MCP_CLIENT_TEST_EDIT_ONLY -u MCP_CLIENT_TEST_COMPONENTS_ONLY -u MCP_CLIENT_TEST_REQUESTS_ONLY bash scripts/test-agreements-postgres.sh TenantPlatform.McpClient.SmokeTests TenantPlatform.Mcp.SmokeTests TenantPlatform.OAuth.SmokeTests TenantPlatform.AgreementList.SmokeTests`: **exitkode 0**, etter siste produksjonsendring. Alle fire fulle suiter bestod: hele 9A–9E, agent-/samtale-/register-/policyregresjoner, OAuth-protokoll/tilbakekalling, MCP-protokoll/privilegier og eksisterende Application-/tjenesteforespørselsregresjoner. Den fulle kjøringen inkluderer den nye område-/versjonsvakten og SSR-kontrollen. Testklyngen ble stoppet og fjernet.

Den samlede kjøringen og de målrettede kjøringene omfatter samme siste produksjonskode. Ingen levende modell-/SMTP-kall eller ukontrollert database ble brukt. Tidligere stoppede/feilede utviklingskjøringer regnes ikke som bestått; isolerte klynger ble fjernet.

Manuelt etter eksplisitt aktivering: kontroller nytt samtykke, profil-/verktøygodkjenning, de tre promptene, synlig diagram, tabell/paging/tomverdier, endringsoversikt, separat lagring/kopi, gjenåpning med AI av, alle tre språk og konto-/brukerbytte. Sammenlign bestiller, leietaker, leverandør og administrator, og prøv fjernet rolle/medlemskap eller verktøygodkjenning ved ny lasting. Prøv også en eldre avtaleside. Disse 9E-prøvene er separate fra brukerens allerede fullførte 9D-test. Ingen containerbygg er gjort; 8C er fortsatt utsatt.
