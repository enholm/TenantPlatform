# Avtalefrister gjennom MCP – første uttrekk i 9F

Status: implementert og automatisert verifisert med full MCP-/MCPClient-suite mot isolert PostgreSQL, solution-build og ni Node-kontroller. Eksakte kommandoer og checkpoint står i moderniseringsplanens 36. Ingen driftsaktivering eller interaktiv kontroll er utført her. Hele 9F er ikke ferdig; se [dekningsoversikten](mcp-read-coverage.md).

## Kontrakt v1

`agreements_followup` tar bare `accountId` og `agreementId` som obligatoriske UUID-er (ikke tomme), samt valgfri `page` heltall 1–10000 (standard 1). Ukjente argumenter avvises; klienten kan ikke velge innringeridentitet. Verktøyet krever eksisterende avtale-OAuth-scope og en aktiv lokal bruker med valgt konto, medlemskap og konkret innsyn i avtalen gjennom `GetAgreementReadScopeAsync`. Eierskap, Read/Edit-grants, kontoroller, arkiv og PlatformAdmin følger eksisterende Application-regler; ingen ny rolle eller generell bypass innføres. Frisk autorisasjon utføres også for gjentatte kall.

Verktøyet kaller `AgreementFollowupPageReader.ReadAsync`, en begrenset projeksjon av oppfølgingsbrukstilfellet. Den deler konkret avtaleautorisasjon og `AgreementReminderSchedule.Today` med den etablerte readeren. `AgreementFollowupPageData` bruker `AgreementReadQueries.Visible` på nytt og konto-/avtalefilter før Count og paging. Infrastructure tar ingen egne rollebeslutninger. MCP inneholder bare validering av transportargumenter og DTO-mapping; ingen Web-fasade eller initialiseringsmotor brukes.

Resultatet har `schemaVersion="1"`, konto-/avtale-ID, `deadlinesInitialized`, `isArchived`, pagingfelter og `items`. Hver rad har bare ID, Kind, DueDate, DaysLeft, Status, State og UpdatedUtc. Enumverdier er lagrede domeneverdier som strenger:

- Kind: Notice, Expiry, Renewal, Cessation.
- Status: Untreated, InReview, Completed.
- State: Current, Replaced, Withdrawn.

Alle lagrede forekomster inngår, også Completed og erstattede/tilbaketrukne datoer. Dette er ikke en liste med bare fremtidige eller aktive frister. En forekomst er ikke en unik avtale; ikke presenter fristantallet som antall avtaler. Arkivering fjerner ikke det autoriserte innsynet. Ingen ny horisont eller datofilter er innført.

DaysLeft er DueDate minus kontodato i kalenderdager ved ett UTC-referansetidspunkt fra kallstart, med lagret reminder-tidssone eller Europe/Oslo når settings mangler. UpdatedUtc er lagret teknisk endringstidspunkt. Dette gir ingen historisk tidsserie eller garanti om tilstand på en tidligere dato. Manglende settings opprettes ikke.

## Paging, fullstendighet og feil

Fast side25, sortert State/DueDate/ID. TotalCount gjelder alle autoriserte lagrede forekomster for valgt avtale. For høy side klemmes til faktisk siste side. `complete` betyr at alle treff inngår i det ene svaret; en siste side med to av 27 treff er ikke komplett. `hasMore`, `nextPage`, `limitReached` gir eksplisitt videre henting. Over 250000 forekomster krever et senere, eksplisitt viderehentingsdesign; side10000 varsler grensen, og ingen falsk komplett total/utlisting returneres. Ingen atomisk snapshotgaranti mellom Count/rader eller flere kall (`snapshotConsistent=false`).

`deadlinesInitialized=false` med tom liste er et gyldig resultat. Det betyr ikke at avtalen definitivt ikke har planlagte datoer; det betyr at ingen fristforekomster er lagret. Agenten får dette i den faktiske kilden. Ingen backfill eller settingsinitialisering utføres. Revision, audit og historikk skal være urørt.

Ukjent/utenlandsk/utilgjengelig avtale gir samme `RESOURCE_UNAVAILABLE`. Feil UUID/side/ukjent argument gir `INVALID_ARGUMENT`. Kansellering gir `CANCELLED`. Andre feil gir `TEMPORARILY_UNAVAILABLE`, uten SQL-/interne detaljer. Gjeldende 16 KiB request-/64 KiB ferdig svargrense og kvoter beholdes; overflow gir hel feil, ingen delvis data. Ingen identity-/historikk-/køobjekter lastes av SQL-projeksjonen.

## Privilegier og aktivering

Eksisterende avtalescope er tilstrekkelig; ingen ny OAuth-fullmakt brukes for dette avgrensede avtaleinnsynet. Det nye verktøyet må oppdages, klassifiseres AutomaticRead, gis eksplisitt publikum og godkjennes per eksakt metadataversjon. Eksisterende approvals oppgraderes ikke automatisk. Personlig OAuth og registerets versjonsbundne kall brukes også av agenten.

Operatøren må særskilt gi MCP-leserrollen følgende ekstra SELECT dersom dette ikke allerede er gitt. Eksemplet bruker eksisterende lokalt dokumentert rollenavn; velg faktisk runtime-rolle i hvert miljø. Implementeringen kjører ikke denne kommandoen i driftsdatabase:

```sql
GRANT SELECT (
  "Id", "AccountId", "AgreementId", "Kind", "DueDate", "Status", "State", "UpdatedUtc"
) ON public.agreement_deadlines TO tenantplatform_mcp_reader;
```

Tidligere dokumenterte SELECT på avtaler, tilganger/medlemskap og reminderinnstillingenes tidssone trengs fortsatt. Ingen nye SELECT på `agreement_deadline_history` eller `agreement_reminders`, ingen person-/transport-/historikkkolonner i fristtabellen og ingen brede/default-privilegier. Eksisterende SaveChanges-vakt, ikke-eierskap og forbud mot CREATE/skriveprivilegier beholdes. Hosten reparerer ikke manglende rettigheter.

## Agent og rapporter

Agenten har dette verktøyet i sin faste tillatte egenkatalog, men bare når registeret gjør eksakt versjon tilgjengelig med personlig autorisering og godkjenning. Konto låses av backend. Verktøyalias, schema-/argumentvalidering, størrelse/kallbudsjett, kildeproveniens og fersk revalidering av samtalekilder følger eksisterende flyt. Pagingfakta knyttes til kilden av backend.

Dette er en detalj-/fristkilde, ikke et nytt rapportområde med aggregat. Den endrer ikke avtalenes v1/v2 eller tjenesteforespørslers v3. Den kan ikke brukes som et vilkårlig nytt dataverktøy i sidedefinisjoner. Lagrede sider bruker fremdeles sine typede kilder og kjører uten modell.

Eksempelspørsmål etter oppdagelse/godkjenning:

1. «Finn avtalen Magida Plaza og vis de lagrede fristene. Er fristene initialisert?»
2. «Vis neste side med frister for denne avtalen. Ta også med fullførte og erstattede forekomster.»
3. «Hvilke av disse lagrede fristene har passert kontodatoen?» (Bare de hentede sidene; hent alle nødvendige sider før en fullstendig konklusjon.)

## Kontroller

Automatiske tester i `FollowupReadChecks` sammenligner direkte autorisert Application med faktisk OAuth/MCP/PostgreSQL, Read/eier, null initialisering, alle lagrede tilstander, rekkefølge/paging/total/fullstendighet, UTC-midnatt/tidssone etter DST, ukjente/fremmede ressurser og spoofet identitet, arkiv, tilbakekalling, avbrudd, samtidige kortlivede context-er og urørt Revision/historikk/init. Den faktiske MCP-rollen har bare åtte fristkolonner; SELECT på AssignedUserId/historikkkommentarer og UPDATE avvises. Samlet MCP-suite tester transportavbrudd, kvoter, store svar og JWT-/audience-isolasjon.

Agentens syntetiske forløp bruker produksjonens OAuth-/MCP-biblioteker og verktøyet, verifiserer pagingproveniens, kilde-revalidering og at fjernet grant hindrer gammel kilde i å gjeninntre i samtalen. Ingen ekte modellkall. Manuelt gjenstår drifts-SELECT, eksplisitt registergodkjenning, et spørsmål mot kjent avtale, videre sidehenting, uinitialisert og arkivert avtale samt tilbakekalling i annen økt. Containerbygg er fortsatt utsatt.


Checkpoint2 legger i tillegg til [kontofrister, prioritering og innsikt](mcp-agreement-read-extensions.md). Den separate kontofristleseren trenger AssignedUserId internt for Mine/ansvarliggyldighet, slik at en delt runtime-rolle får én ekstra eksplisitt kolonne. Denne agreements_followup-projeksjonen bruker fortsatt bare de åtte oppgitte fristkolonnene, og ingen av verktøyene returnerer personidentiteten. Den opprinnelige åtte-kolonners avvisningsprøven beholdes før testrollen utvides, og suppleres med avvist CompletedByUserId/historikk/UPDATE etterpå.
