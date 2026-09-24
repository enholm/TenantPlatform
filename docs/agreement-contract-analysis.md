# Kontraktsanalyse i avtaleregisteret

## Funksjonalitet

«Last opp og analyser kontrakt» finnes i avtaleregisteret for brukere som kan opprette avtaler, og på detaljsiden for brukere med redigeringstilgang. Dialogen tar imot én hovedkontrakt og flere vedlegg. Filene kan åpnes og fjernes før analyse. Hovedkontrakt og vedlegg sendes i ett kall til OpenAI Responses API.

Alle elleve temaer vises, også «Ikke funnet». Flere funn per tema og flere kilder per funn støttes. Dokumentstatus, konflikter, uklar motpart og andre begrensninger vises eksplisitt. Ufullstendig dokumentbehandling blokkerer godkjenning. Verdier, status, partstilknytning, motpart og organisasjonskobling kan korrigeres; opprinnelige AI-verdier, sitater, kilder og responsens strukturerte innhold beholdes separat.

Nye avtaler bruker samme `AgreementFields` og `AgreementNoticeEditor` som eksisterende opprettelsesflyt, og samme backendvalidering. Avtaledatoer og obligatoriske felt bekreftes manuelt, ikke utledes automatisk fra analysen. For eksisterende avtaler legges en ny analyseversjon og dokumenter til. Bare motpartskoblingen kan endres gjennom denne flyten. Tidligere analyser og dokumenter beholdes. En endret avtaleversjon eller låste finansielle opplysninger blokkerer endring av motpart.

Organisasjoner foreslås først ut fra normalisert organisasjonsnummer, deretter eksakt normalisert navn. Omtrentlige treff slås ikke sammen. Valg og bekreftelse av motpart skjer før godkjenning. Ved samtidige godkjenninger serialiseres organisasjonsmatching på kundens databaselås. Hvis en annen godkjenning allerede har opprettet organisasjonen, må brukeren oppdatere treffene og velge denne. Organisasjonens eksisterende grunndata endres ikke. Adresse lagres i analysesnapshotet, siden organisasjonsregisteret ikke har et adressefelt.

## Serverkonfigurasjon

Sett begge verdier gjennom miljøvariabler eller eksisterende .NET user secrets:

- `AgreementAnalysis__ApiKey`: OpenAI API-nøkkel, bare på serveren.
- `AgreementAnalysis__Model`: eksplisitt modell-ID med støtte for PDF-/bildeinnhold, Responses API og strict Structured Outputs. Bruk gjerne en fast modellversjon.

For lokal utvikling brukes nøklene `AgreementAnalysis:ApiKey` og `AgreementAnalysis:Model` med `dotnet user-secrets` for `src/TenantPlatform.Web`. Legg aldri en virkelig API-nøkkel i `appsettings.json`, kildekode eller frontend. Tom modell/API-nøkkel gir en lokalisert konfigurasjonsfeil uten å fjerne opplastede dokumenter.

Dokumentene bruker eksisterende `AgreementDocuments:RootPath` utenfor wwwroot og eksisterende filvalidering. Alle instanser som behandler samme database må dele dette dokumentlageret. Det er ingen ny frontend- eller mappingavhengighet.

Ved Docker-kjøring leser Compose verdiene fra `docker/.env`, og `docker/compose.yml` sender dem videre til web-containeren. Etter endring må containeren opprettes på nytt, for eksempel med `docker compose -f docker/compose.yml --env-file docker/.env up -d --no-deps --force-recreate web`. En vanlig container-restart laster ikke nye miljøvariabler. Lokal kjøring med `dotnet run` eller VS Code leser ikke `docker/.env` automatisk; bruk user secrets eller miljøvariabler i prosessen som starter applikasjonen.

## Filgrenser og analysebegrensninger

- PDF i denne versjonen. DOCX, regneark og bilder må eksporteres til PDF før analyse. Det ordinære dokumentarkivet beholder sine eksisterende formater.
- Én hovedkontrakt, maksimalt ti filer inkludert vedlegg.
- Maksimalt 20 MiB per fil, eller den lavere eksisterende grensen `AgreementDocuments:MaxFileSizeBytes`.
- Maksimalt 40 MiB samlet per analyse.
- Filer kontrolleres av det eksisterende lageret (sikkert filnavn, PDF-signatur og EOF). Dette er ikke en antivirusmotor eller full PDF-parser.
- Maksimalt fem minutter per API-kall og 20 000 output tokens. Refusal, ufullstendige API-svar og ugyldig skjema kan prøves på nytt. Et nytt API-forsøk kan medføre nytt forbruk.
- API-skjemaet valideres også lokalt: påkrevde felt, typer, enumverdier, grenser, komplette kategorier, dokumentdekning og gyldige dokument-ID-er i sitater.
- PDF-lesbarhet, sitatnøyaktighet og sidetall vurderes av modellen og må kontrolleres av brukeren; de verifiseres ikke av en separat lokal OCR-/PDF-motor. Skannede eller store dokumenter kan mislykkes selv innenfor bytegrensene.
- Kontoens navn er tilgjengelig virksomhetskontekst. Systemet har ikke en eksplisitt juridisk «egen virksomhet»-identitet med organisasjonsnummer. Modellen instrueres til å markere uklar rolle og aldri gjette; brukerbekreftelse er alltid obligatorisk.
- Ved konflikt med en endret avtale må brukeren starte et nytt utkast mot den oppdaterte avtalen. Resultater fra utkast er tilgjengelige i den åpne dialogen; det er ikke lagt til et register for å gjenåpne utkast etter navigasjon.

## Lagring, isolering og opprydding

Migrasjonen `20260924183950_AddAgreementContractAnalysis`, opprettet med `bash scripts/add-migration.sh AddAgreementContractAnalysis`, legger til:

- `agreement_analyses`: utkast, lease for pågående analyse, originalt resultat, godkjent motpart og godkjenningsmetadata.
- `agreement_analysis_files`: midlertidige filreferanser og kobling til dokumentarkivet etter godkjenning.
- `agreement_findings`: kategori, originale/godkjente verdier, statuser og parter.
- `agreement_finding_sources`: uendrede sitater, punkt/overskrift, sidetall og arkivdokument.
- `agreement_ai_usage`: respons-ID, faktisk rapportert modell, kunde, bruker, analyse-ID, input-/cache-/outputtokens, tidspunkt og kallstatus. Ingen kontraktstekst eller resultater. Null tokenverdier betyr at forbruk ikke ble rapportert, ikke null kostnad.

Godkjenning låser utkastet og bruker én databasetransaksjon for organisasjon, avtale, dokumenter og funn. Filene promoteres gjennom metadata uten kopiering/flytting under godkjenningen. Gjentatte godkjenninger returnerer samme avtale-ID. Utkast er begrenset til oppretteren, aktivt kundemedlemskap og gjeldende avtalerettigheter. Arkiverte resultater og dokumenter bruker avtalens eksisterende leserettigheter. Alle brukerrettede spørringer er avgrenset til konto.

Forkasting sletter resultat og midlertidige filer, men beholder forbruksmetadata. Lukking godkjenner aldri. `AgreementAnalysisCleanupWorker` kjører ved oppstart og hvert 15. minutt, rydder utløpte utkast etter 24 timer og prøver mislykket filsletting igjen. Foreldreløse filer etter prosesskrasj ryddes etter to døgn, etter global kontroll mot både dokumentarkivet og analysefilene. Denne interne kontrollen må bevisst beskytte filer fra alle kunder.

En analyselease varer ti minutter og API-kallet er begrenset til fem minutter. Et krasj eller nettverksbrudd kan gi ukjent forbruk (`Started`/`FailedOrUnknown`) hvis svaret aldri mottas; rapporterte tokens blir ikke estimert. Forbruksmetadata lagres før resultatvalidering og separat fra godkjenning. Det er foreløpig ikke et forbruks-GUI.

API-kallet bruker `store:false`, innebygde base64-PDF-felt og ingen verktøy/nettsøk. Det opprettes derfor ingen separate Files API-objekter som må slettes. `store:false` er ikke et løfte om null oppbevaring hos leverandøren. Dokumenttekst og analysestrenger tas ikke inn i vanlig revisjonslogg, og HTTP-feiltekster fra leverandøren vises/logges ikke.

Migrasjonen er kun kjørt i isolert testdatabase under implementeringen. Prosjektets vanlige automatiske migrering ved applikasjonsoppstart gjelder fortsatt.

## Verifisering

Kjør den nye smoke-suiten med en disponibel PostgreSQL-database som heter `tenant_agreement_tests`:

```sh
AGREEMENT_TEST_CONNECTION='Host=127.0.0.1;Database=tenant_agreement_tests;Username=...' \
  dotnet run --project tests/TenantPlatform.AgreementAnalysis.SmokeTests
```

Suiten oppretter eget skjema, bruker syntetiske PDF-fixtures og mocker HTTP-transporten til OpenAI. Skjema og filer fjernes etter kjøring. Den tester migrasjoner, flere vedlegg/funn/kilder, manglende opplysninger, uklar identitet, konflikter, organisasjonsmatching, tenant-/brukerisolering, tilgangsroller, originaldata kontra korrigeringer, versjonering, dobbeltklikk, samtidige godkjenninger, transaksjonsrollback, API-feil, refusal, ufullstendige resultater, forbruk, revisjonslogg og opprydding.

Verifisert under implementeringen:

- `dotnet build TenantPlatform.sln`: bestått uten feil eller advarsler.
- `TenantPlatform.AgreementAnalysis.SmokeTests`: 79 beståtte kontroller mot isolert PostgreSQL og mockede HTTP-svar.
- Eksisterende `Agreements`, `AgreementFollowup`, `AgreementNotice`, `AgreementPeriods` og `AgreementProcessing` smoke-suiter: alle bestått mot samme separate testinstans, med egne testskjemaer.
- Ressursnøkler for den nye dialogen kontrollert i norsk, engelsk og svensk. `git diff --check` bestått.

Reelle OpenAI-kall, modelltilgang, PDF/OCR-kvalitet, faktisk fakturering og visuell nettlesertesting er ikke verifisert med kundedata eller aktiv API-nøkkel.

## API-dokumentasjon kontrollert ved implementering

- [File inputs](https://developers.openai.com/api/docs/guides/file-inputs): innebygde PDF-data og flere filer i ett kall.
- [Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs): strict JSON Schema under `text.format`, eksplisitt håndtering av refusal og ufullstendige svar.
- [Responses API migration guide](https://developers.openai.com/api/docs/guides/migrate-to-responses): Responses-format og `store:false`.
