# Kontoavtalefrister, prioritering og innsikt – 9F checkpoint 2

Status: tre nye skrivefrie MCP-verktøy er implementert og automatisert verifisert. Full MCP-, MCPClient- og AgreementList-suite mot isolert PostgreSQL, solution-build og ni Node-kontroller bestod etter siste produksjonsendring. Eksakte kommandoer og fortsettelsespunkt står i moderniseringsplanens37. Driftsaktivering og interaktiv kontroll er ikke utført. Tidligere 9E-driftsfeil er ikke erklært løst.

Alle tre bruker eksisterende Application-lesere, fersk lokal aktør/medlemskap/ressursomfang og personlig OAuth gjennom versjonsbundne registerkall. Ingen GUI-fasade, initialiseringsmotor eller systemjobb kalles. Det eksisterende avtalescope-et gjelder disse avtaleoperasjonene; det gir ingen fullmakt til andre domener. Nye verktøy må oppdages, klassifiseres, gis eksplisitt tilgang og godkjennes per eksakt metadataversjon. Ingen klientregistrering, personlig grant eller registerbinding oppgraderes automatisk.

## agreements_deadlines v1

Application: `AgreementDeadlineReader.ReadAsync`, SQL: `AgreementDeadlineData.QueryAsync`. Kontoens faktiske `GetAgreementListScopeAsync` filtrerer avtaler før fristtelling, kategorier og sidehenting. `Mine` sammenligner eksplisitt AssignedUserId eller arvet avtaleeier med scope.UserId; klienten kan bare velge boolsk mine, aldri UserId/AssignedUserId eller en ferdig scope. Manglende medlemskap og global PlatformAdmin uten kontotilgang gir ikke data.

| Parameter | Betydning/validering |
|---|---|
| accountId | Obligatorisk UUID, ikke tom; verifiseres i Application |
| mine | Boolsk, standard false; betrodd innringerrelativt filter |
| type, kind, status | Eksakte lagrede AgreementType/DeadlineKind/FollowupStatus-enumnavn; ingen nye statusregler |
| state | Current (standard), Replaced, Withdrawn eller All; All gir null i eksisterende readerfilter |
| includeCompleted | Boolsk, standard false; eksplisitt status overstyrer dette som før |
| horizonDays | 30, 60 eller 90 (standard); øvre grense er kontodato + dager, ingen nedre grense, så forfalte beholdes |
| customDates | Boolsk, standard false; true bruker valgfrie inklusive from/to i stedet for horisont |
| from, to | yyyy-MM-dd; krever customDates=true, From ≤ To; begge utelatt med true betyr alle datoer |
| search | Trimmet delstreng i avtaletittel eller motpartsnavn, maksimalt200 Unicode-tegn, ingen kontrolltegn |
| page | Heltall1–10000, standard1 |

CustomDates og eksplisitt horizonDays sammen avvises i transporten, slik at ignorerte vilkår ikke fremstår som brukt. Dato uten CustomDates avvises også. Ukjente argumenter, inklusive personidentiteter og owner-filtre, gir INVALID_ARGUMENT.

Utdata: schemaVersion1, accountId, vanlig paging/fullstendighetsinformasjon, `dataCoverage=StoredOccurrencesOnly`, categories og items. Rader har bare ID, AgreementId, AgreementTitle, kontoavgrenset Counterparty, AgreementType, Kind, DueDate, DaysLeft, Status, State og UpdatedUtc. Ingen person-ID/navn, ResponsibleValid, Owners-alternativer, historikk, kommentarer eller leveringsmetadata returneres. SQL-projeksjonen i eksisterende dataadapter er begrenset til feltene den eksisterende Application-rowen bruker; den materialiserer ikke lenger hele Agreement-/Deadline-entiteter for hver rad. Den eksisterende readeren beregner fortsatt ansvarliggyldighet og kontoavgrensede etiketter internt; disse er ikke en MCP-personkatalog.

Fast side25 sortert DueDate/ID. TotalCount gjelder det faktiske filtrerte, autoriserte fristesettet. For høy side klemmes til siste faktiske side. Complete betyr at hele dette lagrede fristesettet er i ett svar, ikke at alle avtaler er initialisert. En siste side med tre av28 treff er ikke komplett. HasMore/NextPage/LimitReached beskriver videre henting; ved side10000-grensen finnes ingen stille videre truncering. Ingen snapshotgaranti mellom spørringer eller kall.

DaysLeft og kategoridato følger readerens ene UTC-referanse fra kallstart og `AgreementReminderSchedule.Today` med kontoens lagrede/default reminder-tidssone. Ingen ekstra klokkeoppslag eller initialisering legges til. Tom liste kan skyldes manglende lagrede forekomster; den beviser ikke fravær av fremtidige avtalefrister.

**Categories gjelder før alle listefiltre**, også Mine, Search, status og dato. Grunnsettet er synlige, ikke-arkiverte, Current og ikke-Completed forekomster:

- Overdue: DueDate før kontodatoen.
- Next30: kontodato gjennom kontodato+30, inklusive.
- MissingResponsible: felles eksisterende tildelingspredikat er false (aktivt kontomedlem som er eier, AccountAdmin eller Edit-mottaker kreves). Read-mottaker kan inngå i Mine selv om vedkommende ikke er en gyldig ny ansvarlig etter tildelingsregelen. Dette gir ingen ny handlingsfullmakt.

MissingResponsible kan overlappe begge datokategorier. `beforeListFilters=true` og `groupsOverlap=true` gjør dette eksplisitt. Disse tallene er ikke en filtrert rapportaggregator eller unike avtaletall. Selve listen beholder gammel arkiv-/filtersemantikk og kan vise arkiverte rader. Agenten viser en lokalisert forklaring om kategoriomfang og mulig manglende initialisering.

## agreements_prioritized v1

Bare accountId tillates. Verktøyet kaller `AgreementDashboardService.GetDashboardContractsAsync`. Det bruker felles avtalelisteomfang og eksisterende prioriteringsregler: Expired-kandidater først, seneste passerte EndDate først; deretter Notice-kandidater etter NoticeDeadline, med Title/ID som stabile siste nøkler. Felles Attention-regler og kontodato beholdes. Unionen dedupliserer samme avtale som kvalifiserer flere steder. Arkiverte avtaler utelates.

Dette er et **faglig definert utvalg på høyst fem**, ikke en paginert full liste. Resultatet har Today, selectionLimit5, ReturnedCount, selectionComplete=true, isCompleteAgreementList=false, snapshotConsistent=false og items (ID/tittel/type/EndDate). Det konstrueres verken et falskt TotalCount eller en påstand om at resten av kontoen er hentet. Bruk agreements_query/aggregate for uttømmende lister/tall. Agentens egne kildefakta viser en lokalisert advarsel om det begrensede utvalget.

## agreements_insights v1

Obligatorisk accountId og dimension: Type, Status, Counterparty, Geography eller Organization. Valgfri selected UUID gjelder bare Geography/Organization; ellers INVALID_ARGUMENT. Application `AgreementInsightsService.GetInsightsAsync` eier validering, konkret valgt-node-innsyn og hierarkiregler. Infrastructure teller bare synlige, ikke-arkiverte avtaler før SQL-gruppering. Dette verktøyet har ingen avtale-listefiltre eller historisk datoakse.

Resultatet har Dimension, Selected, dataScope=NonArchivedVisibleAgreements, TotalCount, complete=true, groupsOverlap=false, snapshotConsistent=false, Groups og Options. En gruppe har Key, Name, Type/Status hvis relevant, Count og Direct. Options har bare ID, Depth og Path for de Application-tillatte kontonodene; for begrenset bruker bare relevante noder og forfedre, ikke alle ledige organisasjons-/geografialternativer. Motpart og hierarkietiketter er kontoavgrensede.

Type/Status er lagrede dimensjoner. Manglende/ugyldig relasjon gir missing med null navn. Uten selected viser hierarki rotgrupper og missing. Med selected viser det direkte tildelinger til valgt node (Direct=true) og dens barns undertre. Totalen er bare dette undertreet. Gruppene overlapper ikke innen ett slikt resultat, men svar fra flere nivåer må aldri summeres. Den lagrede hierarkitilknytningen er ikke en historisk tidsserie. Ukjent, fremmed eller ikke-tillatt valgt node gir samme RESOURCE_UNAVAILABLE.

Maksimalt100 grupper **og**100 options; overskridelse gir RESULT_TOO_LARGE for hele resultatet, aldri trunkerte grupper/totaler. Ved mange noder trengs senere eksplisitt navigasjon/paging, ikke en automatisk større grense.

Den eksisterende GUI-dimensjonen Owner er bevisst utelatt fra denne MCP-kontrakten: den ville eksponere en personkatalog med person-ID/navn som grupper. Dette er en avgrensning av metadata, ikke en endring av GUI eller lokale eier-/grantregler. Ugyldige og Owner-dimensjoner avvises også ved direkte verktøykall. Fritekst, vilkår, dokument-/modellinnhold, økonomi og grants inngår ikke.

Agenten knytter faktiske total-/gruppe-/fullstendighetsdata til kilden og viser lokalisert forklaring om arkiv og hierarkinivå. Gamle kilder hentes på nytt før gjenbruk i samtalen. Ingen direkte datalesefallback.

## Privilegier og drift

Prioritering og innsikt trenger bare de allerede dokumenterte SELECT-tabellene for avtaler, autorisasjon, motparter og kontoens hierarkier/innstillinger. Kontofrister trenger i tillegg til checkpoint1s åtte fristkolonner én intern relasjonskolonne for Mine og eksisterende ansvarliggyldighet:

```sql
GRANT SELECT ("AssignedUserId") ON public.agreement_deadlines TO tenantplatform_mcp_reader;
```

Dette forutsetter checkpoint1s SELECT på Id, AccountId, AgreementId, Kind, DueDate, Status, State, UpdatedUtc. AssignedUserId er et internt spørringsfelt og returneres aldri fra verktøyet. Rollen får ikke hele tabellen, historikk, kommentarer, kø, CompletedByUserId, CREATE eller writes. Runtime-kontrollen og SaveChanges-vakten er uendret. Ingen operative GRANT-er er kjørt. Testene bevarer først checkpoint1s åtte-kolonners prøve med avvist AssignedUserId, gir deretter den ene eksplisitte kolonnen i testklyngen og kontrollerer avvist CompletedByUserId/historikk/UPDATE.

Nye verktøy krever vanlig registeroppdagelse og eksplisitt godkjenning. Etter checkpoint4 har serveren fjorten verktøy. [Serverbundet områdevalg](read-agent-areas.md) begrenser modellkatalogen: elleve avtalelesere eller to forespørselslesere, med eventuelt ett rapportforslagverktøy. [Detalj-/historikk-/relasjonskontrakten](mcp-agreement-detail-history-relations.md) beskriver de nye tilleggene. Standardgrensen tolv er uendret og inkluderer forslaget. Lavere eksplisitt konfigurasjon feiler fremdeles tydelig uten trunkering. Områdevalg er ikke OAuth-samtykke eller ressursautorisasjon; det endrer verken disse MCP-kontraktene eller deres versjonsgodkjenning.

Disse tre er chat-/oversiktskilder i avtaledomenet, ikke nye rapportområder. Eksisterende rapportdefinisjoner v1/v2/v3 og faste query/aggregate-adaptere er uendret; ingen skjult ny datakilde i lagrede sider. Type-/hierarkidiagrammer blir ikke automatisk støttet av rapportsidene gjennom dette chatverktøyet. Nye typedeklarasjoner og report adapters må komme med egne tester før slike diagrammer tilbys.

Felles feil: INVALID_ARGUMENT, RESOURCE_UNAVAILABLE, RESULT_TOO_LARGE, CANCELLED og TEMPORARILY_UNAVAILABLE uten rå SQL-/transportfeil. Gjeldende request-/svargrenser, kvoter, frister og kansellering består.

## Spørsmål og manuell kontroll

1. «Vis mine lagrede frister for alle datoer, også fullførte. Forklar hva kategoritallene gjelder.»
2. «Hvilke avtaler er prioritert nå? Er dette alle avtalene mine?»
3. «Fordel de synlige, ikke-arkiverte avtalene på motpart, og vis deretter geografihierarkiet.»

Etter operatørens eksplisitte SELECT og registergodkjenning: prøv tomt/uinitialisert datagrunnlag, Mine mot eksplisitt tildeling og arvet eier, status/State/alle datoer, kategorier med aktive listefiltre, neste/siste side, maks fem prioriterte med flere kandidater, valgt hierarkinodes Direct/barn og tilbakekalt grant i annen økt. Kontroller kildeforklaringer på nb-NO/en-GB/sv-SE. Verifiser en eksisterende avtaleside og forespørselsside uten AI. Ingen interaktiv kontroll er utført i dette checkpointet; tidligere 9E-driftsfeil står ubekreftet.

Gjeldende katalogutvalg fra checkpoint5 er dokumentert i [avtaleøkonomi og underutvalg](mcp-agreement-finance-read.md). De opprinnelige katalogtallene i checkpoint4 er historikk; nå fordeles verktøyene på eksplisitte avtaleunderutvalg med uendret tolvgrense. Eksisterende detalj-/historikk-/relasjonskontrakter er beholdt.
