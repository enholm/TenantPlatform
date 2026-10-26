# Avtaleøkonomi og katalogunderutvalg – 9F checkpoint 5

Implementert som to skrivefrie, konkrete avtaleoperasjoner. Finansrapportsider, indekskatalog, beregningsgrunnlag, faktura/betaling og nye domener inngår ikke. Verktøyene går gjennom personlig OAuth, vanlig registeroppdagelse/klassifisering, eksplisitt tilgang og eksakt metadatagodkjenning. Ingen operativ aktivering er utført.

Indekskatalog og verdier er senere levert i [checkpoint6](mcp-agreement-index-read.md). Tabellen og katalogantall nedenfor viser det oppdaterte økonomiutvalget med ni verktøy; selve finanskontraktene fra checkpoint5 er uendret.

## Katalogutvalg og samtaler

Eksisterende `ReadAgentArea` er samtalens samlede, uforanderlige katalogvalg. To ekstra verdier representerer **underutvalg av Avtaler**, med `ReadAgentAreas.Parent` som eksplisitt foreldertilordning. Det er ingen parallell sesjon, ny mutable rettighetsmodell eller separat klientangitt autorisert scope.

| Valg i chatpanelet | Serverbundet verdi | Eksakte egne verktøy |
|---|---|---|
| Avtaler – oversikt og rapport | Agreements (beholdt verdi) | agreements_list, agreements_attention, agreements_query, agreements_aggregate, agreements_prioritized, agreements_insights, agreements_relations |
| Avtaler – detaljer og oppfølging | AgreementDetails | agreements_query, agreements_details, agreements_followup, agreements_deadlines, agreements_followup_history, agreements_relations |
| Avtaler – økonomi | AgreementFinance | agreements_query, agreements_details, agreements_relations, agreements_financial_lines, agreements_forecast, agreements_indices, agreements_index_values, agreements_bases, agreements_basis |
| Tjenesteforespørsler | ServiceRequests | service_requests_query, service_requests_aggregate |
| Ekstern kilde | External | Eksisterende profilbasert, personlig autorisert/godkjent ekstern katalog, uten tvungen TenantPlatform-inndeling |

Hvert valg er fortsatt bare snittet med faktisk tilgjengelige og godkjente AutomaticRead-verktøy. Delt query-/referanselesing lar brukeren finne en konkret avtale uten fri dispatcher eller nye rettigheter. Et underutvalg kan ha bare en godkjent leser; valg av «økonomi» gjør ikke manglende finansverktøy tilgjengelige. Ukjente verdier og kall til alias utenfor samtalens utvalg avvises i Application.

GUI bruker samme velger og lokaliserer hele område-/oppgavenavnet. Samme eksisterende bekreftelse, servervaliderte switch-area, ny samtale-ID, kansellering og generasjonsvern gjelder ved bytte mellom to avtaleunderutvalg. Historikk, kildekontekst og ulagret utkast forkastes etter varsel; sene svar får ikke gjenopplive dem. Én tilkobling beholdes. Område-/underutvalgsfelt kan ikke overstyres på Ask.

Backend velger alltid Agreements/oversikt ved AI-redigering av en lagret avtaleside, og ServiceRequests ved forespørselsside. Bare disse valgene får rapportforslagverktøy når oppgaven krever det. Detalj-/økonomivalg får ikke rapportforslag, selv om query er tilgjengelig. Ingen finansiell rapportadapter tilbys. Manuell rapportkonfigurering og lagrede v1/v2/v3-definisjoner er uendret.

Standardgrensen er fortsatt12: oversikt har maksimalt7 lesere +1 forslag, detaljer6, økonomi9, forespørsler2 +1 forslag. Lavere konfigurert grense gir eksplisitt ToolLimit, inklusive forslaget. Ingen stille trunkering. Eksisterende lokal appkonfigurasjon er bevart; ingen grense er hevet av dette steget. Eldre Agreements-samtaler betyr nå oversiktsutvalget; tidligere detaljkilder utenfor dette utvalget kan ikke gjenbrukes i historikk. Velg detalj-/økonomiutvalg eksplisitt og start isolert kontekst ved behov.

## Bekreftet lesing og autorisasjon

`AgreementFinancialService.GetLinesAsync/ForecastAsync` krever eksisterende konkret `GetAgreementReadScopeAsync` gjennom Require(..., edit:false), før og etter `BeginAsync(..., false)`. Koden har ingen ekstra finansiell leserolle utover dette innsynet. Read/Edit/eier gir samme lesefelt; arkiv fjerner redigering, ikke dette grunnleggende innsynet. PlatformAdmin er fortsatt adskilt fra ordinær kontotilgang.

Infrastructure `AgreementFinancialData.BeginAsync(write:false)` starter RepeatableRead, men tar ingen konto-/avtale-FOR UPDATE-låser. Den laster avtale, linjer, versjoner, priser, indeksvalg/verdier og også dokumenter, aktørnavn, grunnlag/events/proposals. Konstruktøren lager bare HashSets i minnet. Lesemetodene kaller ikke CommitAsync eller SaveChanges, og det finnes ingen initialisering/generering langs disse lesebanen. CommitAsync avviser write:false. Dette er en konklusjon fra kallkjeden, ikke en antakelse om at rollback alene gjør lesing ufarlig.

GUI-porten er likevel for bred for MCP. Ny `AgreementFinanceReader` i Application bruker samme konkrete autorisasjon med en separat, smal `IAgreementFinanceReadData`-port. Infrastrukturprojeksjonene velger eksplisitte kolonner og leser ikke dokument-/person-/basis-/proposalobjekter. Prognosens datagrunnlag hentes i én RepeatableRead-transaksjon med `SET TRANSACTION READ ONLY`; ingen skrivelås eller commit. Autorisasjon revalideres etter datalesing. Ingen sterkere garanti om atomisk grantrevokering eller snapshot på tvers av kall innføres.

GUI-ets beregningskropp er trukket ut uendret til `AgreementForecastCalculation`; den gamle tjenesten delegerer dit. Ny reader bruker samme funksjon og de eksisterende `AgreementAutomaticIndexCalculator` og `AgreementPeriodCalculator`. De har valgfrie kanselleringskontroller og beregningsgrense; gamle kall beholder standardgrensen20000 og samme beløpsregler. MCP bruker sin strengere500-grense og request-token. Ingen ny økonomisk beregningsmodell.

## OAuth-/tilgangsbeslutning

Eksisterende `tenantplatform.agreements.read` beholdes for disse **konkrete avtaleoperasjonene**. Gjeldende OAuthConsent bruker den generelle OAuthReadAccess-teksten om å kunne lese «avtaledata du har tilgang til» i medlemskontoene; den avgrenser ikke samtykket til de opprinnelige listekolonnene. Begrunnelsen er at lokale Application-regler allerede gir avtaleleseren akkurat dette finansinnsynet, og at avtalescope er en ressurslesefullmakt, ikke en begrensning til de opprinnelige listekolonnene. Det gir ikke kontoens øvrige økonomi, leasing, betalinger eller registeradministrasjon.

Vi innfører derfor ikke en ny lokal finansrolle eller et nytt OAuth-scope i dette uttrekket. Opt-in i registerkonsumenten skjer gjennom eksplisitt godkjenning av to nye verktøy og valgt økonomiutvalg. Eksisterende grants, klientregistreringer, samtykke- eller profilversjoner oppgraderes ikke. Ingen driftsregistrering kjøres automatisk. Eksterne direkte MCP-klienter håndheves av samme avtalescope og lokale ressursautorisasjon; deres OAuth-grant er ikke en verktøyhash-godkjenning fra det interne registeret. En ekstern klient som bruker det brede avtalescopet kan dermed bruke de publiserte avtaleøkonomiverktøyene når brukeren har lokalt innsyn. Dette må kommuniseres i klientens datadelings-/samtykkebeskrivelse før aktivering. Hvis produktet krever et separat, brukerfornyet OAuth-samtykke for finansfelt, må et eget finansscope med klient-/samtykke-/profilstøtte innføres før slik aktivering; områdevalg alene erstatter ikke dette.

## agreements_financial_lines v1

Obligatorisk accountId/agreementId (UUID), valgfri eksakt lineId og versionsPage/pricesPage (1–10000, standard1). Ukjent/fremmed linje innen valgt avtale gir samme RESOURCE_UNAVAILABLE. Ingen klientidentitet eller ferdig scope kan leveres.

Resultat: schemaVersion1, accountId/agreementId, Coverage=`StoredLineAndPriceHistory`, SnapshotConsistent=false og Data:

- Direction/Currency fra avtalen (kan være null før oppsett); LineCount er **alle linjer i avtalen før valgfritt lineId-filter**.
- Versions: selvstendig side25 med RequestedPage/Page/PageSize, TotalCount/ReturnedCount, HasMore/NextPage, Complete/LimitReached. Poster er sortert LineId/Sequence/Id.
- Prices: egen tilsvarende side25 og egne totaler, samme stabile sortering. Det ene sidetallet påvirker ikke den andre strømmen.

En versjon inneholder Id/LineId/Sequence, EffectiveFrom/RecordedUtc, Name/Description, StartDate/EndDate/FirstPayableDate, konto-/avtaleavgrenset PayableSourceLineId og lagret PayableSourceStartDate/PayableOffsetMonths, Frequency/Anchor/AnchorDate/BillingTiming, Status og IndexRegulated. Et prisgrunnlag inneholder Id/LineId/Sequence, Independent, EffectiveFrom/IndexBaseDate, Quantity/UnitPrice og RecordedUtc.

Dette dekker de relevante forretningsfeltene fra `AgreementLinesDto` og `AgreementLineDetails` uten å publisere entiteter. Linjens Id brukes via LineId; ActivatedUtc er ikke en pris-/periodeparameter og publiseres ikke som en ny tilstandsregel. Ingen egen linjetype finnes: navn/beskrivelse, lagret status og frekvens (Once/Monthly/Quarterly/HalfYearly/Yearly) brukes uten oppdiktet klassifikasjon. Revision/CanEdit/ActorNames, ActorUserId/Reason, AdjustmentId og alle dokumentobjekter/-koblinger utelates. SourceLine-ID valideres også mot samme konto/avtale før den vises.

Alle lagrede versjoner/priser beholdes, inklusive utkast og fremtidige EffectiveFrom. EffectiveFrom betyr virkningsdato; RecordedUtc betyr registreringstid. Høyeste sekvens er ikke automatisk «dagens verdi». Betalingsstart med kilde bruker den allerede lagrede snapshotsammenhengen, ikke ny beregning fra en endret kilde. Prisens Independent er en eksisterende regel: selvstendig pris versus prisparet til en linjeversjon med samme sekvens. Listefelt er historikk, ikke fakturaer eller realiserte transaksjoner.

Ingen ubegrenset liste av versjoner/priser. Page over siste side klampes med faktisk total og RequestedPage beholdt. Ved10000 og mer data er NextPage=null/LimitReached=true. Complete gjelder hele den filtrerte strømmen, ikke bare at siden ble hentet. Ingen totaler skal summeres som beløp eller blandes med versjonens antall. Hele svarstørrelsesgrensen gjelder i tillegg, uten teksttrunkering.

## agreements_forecast v1

Obligatorisk accountId/agreementId og konkrete from/to i yyyy-MM-dd. Begge datoer er inklusive, from må være gyldig/ikke standard, from≤to, to.Year≤9998, og DayNumber-forskjellen≤366 (opptil367 inklusive kalenderdager). Ingen modellvalgt tidssone, implicit «i dag» eller frie uttrykk.

Resultat: Coverage=`CalculatedForecastNotActualTransactions`, Complete=true bare når hele beregningen lykkes, SnapshotConsistent=false på tvers av kall, og Data med From/To, Direction/Currency, IndexSetupNeedsReview, separate Income/Cost og Periods. Hver periode har LineId/LineName, LineVersionId/PriceVersionId, Direction/Currency, ReferenceFrom/ReferenceTo, From/To, InvoiceDate, Quantity/UnitPrice, ActiveDays/ReferenceDays, Amount og Included. Ingen rå dokument-/indexobjekter, EventKey, ExplanationKey, person eller skrivekommando returneres.

Faktisk domenesemantikk:

1. Ikke-Draft-linjeversjoner og tilhørende prissekvenser inngår; selvstendige priser inngår etter dagens regel. Senere registrerte korreksjoner erstatter eldre definisjoner fra virkningsdato. Bare Active-versjoner produserer perioder; Ended/Deactivated avgrenser etter eksisterende effektivsekvenslogikk. Historiske godkjente priser er registrert grunnlag, ikke nye proposals.
2. Start/EffectiveFrom, eventuell sluttdato og neste versjon avgrenser perioden. Once bruker FirstPayableDate. Gjentakelse bruker eksisterende Calendar/Date-anker og månedsfrekvens. Before-first-payable-deler markeres Included og har ingen betalbar Amount. Advance/Arrears bestemmer fakturadatoen for den logiske hendelsen.
3. Forespørselens intervall velger **overlappende logiske beregningsperioder**. Hele overlappende referanse-/prisdelperioder beholdes, også utenfor forespurte datoer. Dette er ikke en ny dag-for-dag-kontantstrøm; klienten må ikke klippe datoer og beregne beløpet på nytt. ActiveDays/ReferenceDays forklarer eksisterende forholdsmessighet.
4. Prisgrenser og automatisk indeksregulering kan dele periodene. Automatisk projeksjon bruker avtalens registrerte indeksvalg, siste ikke-supersederte revisjon per indeksperiode, eksisterende baseline/reset-regler og registrerte indekstall. Ingen fremtidige indekstall predikeres. PublishedDate er ikke et nytt as-of-filter; dagens kalkulator velger etter Period. Ved IndexSetupNeedsReview eller manglende valg brukes registrert pris uten automatisk regulering som før, med review-flagget i resultatet. Ingen manglende indeksverdi konstrueres.
5. Indekspris rundes etter eksisterende prisforhold til to desimaler AwayFromZero. Beløp fordeles ved kumulativ todesimalsavrunding AwayFromZero over hele den logiske hendelsen før overlappsfilter. Registrert Quantity/UnitPrice har databasepresisjon18,4. Ingen ny avgifts-/MVA-transformasjon eller netto/brutto-påstand innføres: kalkulatoren har ingen avgiftsfelt.
6. Én avtale har én retning og valuta. Income og Cost returneres separat etter eksisterende kontrakt; ingen netting eller valutakonvertering. Dette er beregnet prognose, ikke godkjent grunnlag, faktura eller betaling. Ingen generering/lagring utføres.

Manglende retning/valuta eller aktuell pris og eksisterende beregningsavvisninger gir CALCULATION_UNAVAILABLE for hele resultatet. En tom, korrekt konfigurert avtale kan legitimt gi null perioder/null summer. Dette må skilles fra feil og manglende oppsett.

Harde readergrenser:250 linjeversjoner,500 priser,100 indeksvalg,100 indekser,1000 indeksverdier for de valgte indeksene; én ekstra rad brukes bare til å oppdage overskridelse. Ingen delvis snapshotberegning. Maks500 beregningsperioder **før** overlappsfilter beskytter også mellomarbeid; over grensen gir hel RESULT_TOO_LARGE. Indeks-/periodeløkkene og alle EF-spørringer får request-kansellering. Vertens eksisterende10s budsjett, SQL8s og endelige svargrense gjelder fortsatt. Ingen ubegrenset forecast eller stille avkutting.

## Kilder, feilmapping og drift

Agentens faktiske Source inkluderer valuta, retning, dekning, periode, fullstendighet, totale linjeversjoner/priser eller antall prognoseperioder. Hele de versjonsbundne argumentene/resultatfingeravtrykkene beholdes som andre transitive kilder og hentes på nytt før historie/utkast brukes. Endret pris, indeksgrunnlag eller fjernet tilgang ugyldiggjør gammel avledet kontekst. Ingen direkte SQL-/GUI-/Application-datalesefallback i agenten.

Feil: INVALID_ARGUMENT for transportargumenter, RESOURCE_UNAVAILABLE for ukjent/fremmed/utilgjengelig ressurs, CALCULATION_UNAVAILABLE for avvist eksisterende beregningsgrunnlag, RESULT_TOO_LARGE for reader-/svargrenser, CANCELLED og TEMPORARILY_UNAVAILABLE. Ingen rå exceptions, ActorNames, credentials eller dokumentinnhold.

Nødvendig ekstra SELECT er dokumentert nedenfor, ikke operativt utført. Indekser krever allerede checkpoint4s Id/AccountId/Name. De øvrige tabellene får bare disse kolonnene; intet eierskap, CREATE, heltabellprivilegium eller writes:

```sql
GRANT SELECT ("Id", "AccountId", "AgreementId") ON public.agreement_lines TO tenantplatform_mcp_reader;
GRANT SELECT ("Id", "AccountId", "AgreementId", "LineId", "Sequence", "EffectiveFrom", "RecordedUtc", "Name", "Description", "StartDate", "EndDate", "FirstPayableDate", "PayableSourceLineId", "PayableSourceStartDate", "PayableOffsetMonths", "Frequency", "Anchor", "AnchorDate", "BillingTiming", "Status", "IndexRegulated") ON public.agreement_line_versions TO tenantplatform_mcp_reader;
GRANT SELECT ("Id", "AccountId", "AgreementId", "LineId", "Sequence", "Independent", "EffectiveFrom", "IndexBaseDate", "Quantity", "UnitPrice", "RecordedUtc") ON public.agreement_price_versions TO tenantplatform_mcp_reader;
GRANT SELECT ("Id", "AccountId", "AgreementId", "IndexId", "Sequence", "EffectiveFrom") ON public.agreement_index_selections TO tenantplatform_mcp_reader;
GRANT SELECT ("Id", "AccountId", "IndexId", "Period", "Revision", "Superseded", "PeriodKey", "Value", "PublishedDate") ON public.agreement_index_values TO tenantplatform_mcp_reader;
```

Operatør må vurdere scope-/datadelingsbeskrivelsen over, gi eksplisitte privilegier i riktig miljø, oppdage og klassifisere begge nye verktøy, velge lovlig publikum/bruk og godkjenne eksakt versjon. Verken områdevalg eller allerede godkjente gamle verktøy gjør dette automatisk. Manglende SQL-/registeradgang gir sikker kildefeil, aldri automatisk reparasjon. SaveChanges-vakt og databasebruker uten skriveadgang beholdes.

## Eksempelspørsmål og manuell kontroll

1. Velg «Avtaler – økonomi»: «Finn avtalen [tittel] og vis de lagrede linjeversjonene og prisgrunnlaget, også neste side. Skill virkningsdato fra registreringsdato.»
2. «Beregn prognosen for [avtale] fra1. januar til31. mars2027. Oppgi valuta, retning, indeksforutsetninger og hva som er beregnet fremfor fakturert.»
3. «Forklar hvorfor prognosen for [avtale] har perioder utenfor valgt datointervall og inkluderte perioder med null betalbart beløp.»

Manuelt gjenstår valg/bytte av de tre avtaleutvalgene med aktiv kjøring og ulagret utkast, tydelige språk-/kildeetiketter, nye godkjenninger/privilegier, reell linje-/pris-paging, manglende oppsett, endret pris og grant i annen økt. Kontroller at Endre med AI velger oversikt og at ingen finansrapportforslag tilbys i økonomiutvalget. Eksisterende avtale-/forespørselssider skal fortsatt kjøre uten AI.

Tidligere interaktive kontroller og 9E-driftsfeil er fortsatt ubekreftet. Isolerte tester er ingen operativ aktivering. Containerbygg/8C er utsatt; ingen driftsmigrering, deploy, commit, push eller Serga-endring inngår.

Checkpoint6 utvider økonomiutvalget med to skrivefrie [indeksverktøy](mcp-agreement-index-read.md). Indeksregister/verdier er nå selvstendig MCP-dekket; beregningsgrunnlag og finansrapportsider gjenstår fortsatt. Resten av prognose-/linjekontrakten og scopevurderingen over er uendret.

Checkpoint7 gir [lagret grunnlagsliste og snapshotdetalj](mcp-agreement-basis-read.md). Økonomiutvalget har nå9 verktøy. Preview, aktualitetsvurdering, bulk og godkjenningslesing er fortsatt åpne; ingen finansrapportadapter er opprettet.
