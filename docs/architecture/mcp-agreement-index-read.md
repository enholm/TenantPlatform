# Indeksregister og indeksverdier – 9F checkpoint 6

To skrivefrie MCP-kontrakter v1 er implementert gjennom `AgreementIndexReader` i Application og `AgreementIndexReadData` i Infrastructure. MCP er transportadapter. Ingen importer, kildehenting, initialisering, beregningsgrunnlag eller rapportadapter inngår. Operativ aktivering og interaktiv kontroll er ikke gjennomført.

## Tilgang og feltavstemming

Eksisterende `AgreementIndexService.GetIndicesAsync` bruker `GetAgreementListScopeAsync` og deretter **kontoens indeksregister**, uten avtale-/eier-/grantfilter. Listescopet krever betrodd aktør, valgt konto, aktiv bruker og aktuelt medlemskap. `CanReadAll` begrenser avtaleradene i andre lesere, men begrenser ikke denne eksisterende registerlesingen. Også et medlem uten avtalegrant har registerinnsyn. Å fjerne ett avtalegrant tilbakekaller derfor ikke registerinnsynet. PlatformAdmin uten medlemskap får ingen bypass.

Ny reader beholder akkurat denne lokale innsynsregelen og revaliderer listetilgang og aktør etter datalesing. Konto velges aldri som en ferdig autorisert klientfullmakt. Ukjent, fremmed og utilgjengelig konkret indeks gir samme `RESOURCE_UNAVAILABLE`. Et tilgjengelig register eller en indeks uten verdier kan gi et vellykket tomt resultat.

Eksisterende OAuth-scope `tenantplatform.agreements.read` beholdes: dette er avtaleområdets eksisterende indeksregister, ikke et generelt nytt domene eller en fullmakt til administrasjon. Den generelle avtaledatabeskrivelsen i eksisterende samtykke må vurderes ved aktivering som beskrevet i [finanskontrakten](mcp-agreement-finance-read.md#oauth-tilgangsbeslutning). Nye verktøy gir ikke klientregistrering, samtykke, profil- eller registergodkjenning automatisk. Eksterne direkte klienter følger OAuth-scope og Application-regler; internregisterets verktøyhash er ikke en ekstra OAuth-grant for eksterne klienter.

| Lagret felt | Transport |
|---|---|
| Id, Code, Name, Description, Resolution | Registerets ordinære forretningsmetadata; Resolution er Month, Quarter eller Year |
| Source | **Bare `SourceOrigin`**, med policyen nedenfor; aldri rå Source |
| Verdiens Id, Period, PeriodKey, Revision, Superseded, Value, PublishedDate | Eksplisitt verdi-DTO; PeriodKey er periodens revisjonsidentitet |
| AccountId/IndexId | Konto/konkret indeks i svaromslaget; aldri frie scopeobjekter |
| ActorUserId, RecordedUtc, Reason, CanManage og skriveobjekter | Utelatt |

`Source` er i dagens editor et obligatorisk fritekstfelt på opptil 2000 tegn. Det er ikke validert som en offentlig kildeadresse. Derfor brukes en konservativ transportpolicy i **Application**: bare en absolutt HTTP(S)-adresse med DNS-vert, uten userinfo eller kontrolltegn, kan gi sin origin (skjema/vert/eventuell port). Hele stien, query og fragment fjernes, også hvis de ser harmløse ut. IP-adresser, andre skjemaer, innloggingsinformasjon og vilkårlig fritekst gir null. Null betyr «manglende eller tilbakeholdt», ikke at kilden beviselig mangler. Eierskapet til vertsnavnet eller kildens offentlige tilgjengelighet bekreftes ikke. Ingen DNS-oppslag, lenkevalidering eller nettverkskall utføres. Original Source og de eksisterende GUI-/skrivekontraktene endres ikke.

## Verktøy og paging

`agreements_indices` v1:

- Obligatorisk `accountId`; valgfri `search`, `resolution`, `page`.
- Search er trimmet, bokstavfølsomt bokstavelig delstrengsøk i Code/Name; maksimalt 200 tegn og ingen kontrolltegn. Ingen frie uttrykk. Resolution er eksakt enum, ikke numerisk verdi eller vilkårlig felt.
- Svar: schemaVersion, accountId, Coverage=`AccountIndexRegister`, SnapshotConsistent=false og Data med paginerte IndexEntry-poster.
- Sortering Code stigende, Id som siste entydige nøkkel. Ingen verdier lastes sammen med registeret.

`agreements_index_values` v1:

- Obligatorisk `accountId`, `indexId`; valgfri `view` (`Current` som standard, eller `History`), `from`, `to`, `page`.
- Datoer er konkrete yyyy-MM-dd, inklusive grenser på **lagret Period**. En ensidig grense er tillatt. Omvendt intervall og DateOnly.MinValue avvises. Ingen normalisering til kvartal/år, implisitt «i dag», as-of eller publiseringsfilter.
- Svar inkluderer indexId, faktisk View/From/To, Coverage=`CurrentStoredRevisions` eller `StoredRevisionHistory`, SnapshotConsistent=false og Data med verdi-DTO-er.
- Sortering Period synkende, Revision synkende, Id stigende. Bare valgt indeks i aktuell konto leses; historikk for alle indekser kan ikke lastes ubundet.

Begge bruker eksisterende sidekontrakt med 25 poster, page 1–10000, RequestedPage/Page, TotalCount/ReturnedCount, HasMore/NextPage, Complete og LimitReached. Side over siste side klampes; RequestedPage beholdes. Totalen gjelder hele det filtrerte utvalget. Complete er bare true når hele utvalget er i svaret. Ved side 10000 og flere poster er NextPage=null og LimitReached=true; snevrere filtre må brukes. Ingen stille avkutting eller summering av revisjoner som unike perioder. Tomt utvalg har side1/total0 og Complete=true.

Hvert kall bruker én kortlivet context med RepeatableRead og `SET TRANSACTION READ ONLY`, sekvensielle konto-/indeksbegrensede spørringer og eksplisitte kolonneprojeksjoner. Count og side deler snapshot innen kallet; `SnapshotConsistent=false` betyr at flere MCP-kall/sider ikke deler snapshot. Eksisterende request-, SQL-, tids- og svarstørrelsesgrenser gjelder. For stort svar gir hel `RESULT_TOO_LARGE`; ingen teksttrunkering. Request-kansellering føres gjennom autorisasjon, transaksjon og spørringer.

## Perioder, korreksjoner og prognose

Resolution Month/Quarter/Year betyr eksisterende periodejustering til første dag i måned/kvartal/år ved registrering. Leseren viser lagrede perioder uten å fylle hull, flytte datoer eller konstruere en tidsserie. Skriveflytens positive verdi, presisjon8 desimaler, periode- og siste-revisjonsvern endres ikke.

`PublishedDate` er valgfri registrert publiseringsdato, separat fra indeksperioden. En verdi kan ha null eller en senere publiseringsdato. Leseren hevder ikke at den var kjent på en tidligere dato.

Revision økes ved korreksjon; en korreksjon kan flytte Period og beholde PeriodKey. Superseded markerer erstattet verdi. Current velger høyeste ikke-supersederte revisjon per PeriodKey **før periodefilteret brukes**, etter samme utvalgsregel som dagens prognosekalkulator. En gammel verdi skal ikke gjenoppstå fordi den nye revisjonen har flyttet til en dato utenfor filteret. Current betyr gjeldende **revisjon**, ikke indeksnivået på dagens dato: også registrerte fremtidige Period kan inngå uten et periodefilter. History beholder alle lagrede revisjoner, også supersederte. Dermed kan en historikkside inneholde flere rader for samme periode. Eksisterende unikhetskrav `(AccountId, IndexId, Period, Revision)` og unik registerkode per konto beholdes; Id er likevel siste stabile nøkkel.

`AgreementAutomaticIndexCalculator.Project` velger disse gjeldende revisjonene og deretter siste Period≤beregningsdato. Den bruker **ikke PublishedDate som as-of-filter**. Den eksisterende prognosen bruker også avtalevalg, effektive linjer/priser, baseline/reset og avrunding; en enkelt indeksverdi er ikke en ferdig pris eller prognose. Ingen kalkulatorregel er endret av dette uttrekket. Bruk `agreements_forecast` for faktisk beregning og dens eksisterende grenser, ikke konstruer den fra en ufullstendig side verdier.

## Agent, godkjenning og privilegier

Eksplisitt `ReadAgentAreas`-tilordning legger begge verktøy i **Avtaler – økonomi**, sammen med query, details, relations, financial_lines og forecast: maksimalt 7 lesere, uten rapportforslag. Standardgrensen 12 er uendret; lavere konfigurert grense gir ToolLimit før modellen brukes, uten stille utelatelse. Oversikt/detaljutvalg får ikke disse verktøyene. Eksterne profiler følger fortsatt sin egen godkjente katalog.

Agenten bruker personlig OAuth, ett kontobundet samtalevalg og registerets eksakte, ferskt kontrollerte verktøyversjon. Kildeproveniens har faktisk verktøy, argumenter og pagineringsfakta. Beholdte MCP-resultater hentes på nytt og fingeravtrykk sammenlignes før tidligere svar kan brukes. Endret synlig indeksmetadata, verdi/revisjon eller tilbakekalt aktiv kontotilgang ugyldiggjør avhengig kontekst. Dette gjelder de faktisk konsulterte sidene/filtrene; en side på 25 beviser ikke resten av registeret eller historikken. Skjulte Source-detaljer er ikke datakilder modellen får bruke.

Nødvendig ekstra SELECT utover checkpoint 4–5 er bare følgende kolonner på agreement_indices; det er dokumentasjon for senere kontrollert aktivering, **ikke kjørt i drift**:

```sql
GRANT SELECT ("Code", "Description", "Source", "Resolution") ON public.agreement_indices TO tenantplatform_mcp_reader;
```

Id/AccountId/Name på indekser og Id/AccountId/IndexId/Period/Revision/Superseded/PeriodKey/Value/PublishedDate på verdier er allerede dokumentert for finansleseren. ActorUserId, Reason og RecordedUtc kreves ikke. Ingen heltabellprivilegier, UPDATE, CREATE, eierskap eller skriverolle gis. Isolerte tester gir akkurat disse SELECT-kolonnene og kontrollerer avvist privat lesing og skriving, med SaveChanges-vakt på direkte readerkall.

Operatør må selv oppdage begge nye verktøy, kontrollere kontrakter/klassifisering, velge tillatt publikum og AutomaticRead-bruk, samt godkjenne **eksakt metadataversjon**. Eksisterende verktøy åpner dem ikke. Manglende SQL-/OAuth-/registeradgang gir sikker kildefeil, aldri automatisk reparasjon.

Sikker feilmapping: INVALID_ARGUMENT, RESOURCE_UNAVAILABLE, RESULT_TOO_LARGE, CANCELLED, TEMPORARILY_UNAVAILABLE. Ingen rå databasefeil eller skjulte metadata returneres.

## Eksempelspørsmål og manuell kontroll

1. «Finn tilgjengelige månedsindekser med KPI i kode eller navn. Vis neste side hvis registeret er større enn 25.»
2. «Vis gjeldende lagrede verdier for indeks [ID] fra 1. januar til 31. desember 2026. Skill indeksperiode fra publiseringsdato.»
3. «Vis revisjonshistorikken for samme indeks og periode. Forklar hvordan en supersedert verdi skiller seg fra verdien dagens prognose bruker.»

Interaktivt gjenstår: oppdagelse/godkjenning og minste SELECT i riktig miljø, synlig økonomiutvalg, faktiske kildeetiketter/paging, Current mot History, korreksjon og kontotilgang fjernet i annen økt. Kontroller eksisterende avtale-/forespørselssider med AI deaktivert. Tidligere uavklarte manuelle kontroller og 9E-driftsfeil er fortsatt ubekreftet. Containerbygg/8C er fortsatt utsatt.

Faktisk automatisert verifikasjon og neste uttrekk føres i [moderniseringsplanens checkpoint6](modernization-plan.md#41-steg-9f-checkpoint-6-indeksregister-og-revisjonsverdier).

Checkpoint7 utvider det eksisterende økonomiutvalget til ni lesere med [lagrede grunnlag](mcp-agreement-basis-read.md). Indekskontraktene og gjeldende-/historikksemantikken over er uendret. Standardgrensen12 og eksplisitt ny verktøygodkjenning gjelder fortsatt.
