# Lagrede beregningsgrunnlag – 9F checkpoint 7

`agreements_bases` og `agreements_basis` v1 gir en sammenhengende, skrivefri flyt for å finne et lagret grunnlag og lese ett konkret snapshot. Application eier kontrakter, validering, referansekontroll og tilgang. Infrastructure utfører kontoavgrenset SQL. MCP er transportadapter. Ingen finansrapportadapter, rapportforslag, generering, godkjenning, korrigering eller operativ aktivering inngår.

## Autorisasjon og eksisterende oppførsel

Eksisterende `AgreementFinancialService.ListBasisAsync` bruker `GetAgreementListScopeAsync`, avgrenser grunnlag til `AgreementReadQueries.Visible`, konto og lagret retning, og leser snapshot med grunnlagets gjeldende revisjon. `ReadBasisListAsync` har et skjult Take(500) før snapshot-join. Den eksisterende GUI-banen er beholdt. Ny `AgreementBasisReader` bruker en separat `IAgreementBasisReadData`-port med telling og paging i SQL, uten dette taket eller rå entitetsretur.

`GetBasisAsync` bruker `GetAccountActorScopeAsync`, kontobegrenset grunnlag→avtale-oppslag og konkret `GetAgreementReadScopeAsync` gjennom OpenBasis/Open før og etter åpning av lesetransaksjonen. Ny detaljleser følger samme lokale tilgangsregel: betrodd aktiv bruker, riktig valgt konto, medlemskap og konkret avtaleinnsyn. Ingen ekstra godkjenningsfullmakt kreves for dette eksisterende innsynet. Read/Edit/eier og kontoens eksisterende ReadAll-omfang beholdes; arkiv fjerner ikke vanlig lesing. PlatformAdmin gir ingen kontobypass.

Listen anvender eksisterende synlighets-, konto- og retningsfilter **før** Count/Skip/Take. Valgfritt konkret agreementId autoriseres også separat; fremmed/ukjent/utilgjengelig avtale gir samme feil. Etter datahenting revalideres aktivt medlemskap/aktør og alle returnerte avtalers synlighet gjennom en fersk kontekst og samme eksisterende SQL-predikat. Detaljen revaliderer konkret avtaleinnsyn før retur. Dette er ferske kontroller, ingen ny atomisk grant-/sesjonsrevokeringsgaranti.

OAuth-scope er fortsatt `tenantplatform.agreements.read`, vurdert mot samme eksisterende finansleseregel som [finanskontrakten](mcp-agreement-finance-read.md#oauth-tilgangsbeslutning). Leseinnsyn er ingen godkjenningsrett. Nye verktøy krever separat oppdagelse, klassifisering, publikum/bruksvalg og eksakt versjonsgodkjenning for registerkonsumenter. Ingen klientregistrering, personlig OAuth-fullmakt eller profil oppgraderes automatisk. Direkte eksterne MCP-klienter følger OAuth og Application-reglene; internregisterets hashgodkjenning er ikke en egen ekstern OAuth-grant.

## Liste: agreements_bases v1

Argumenter:

- `accountId` og `direction` er obligatoriske. Direction er eksakt Income/Cost og gjelder **lagret grunnlagsretning**, ikke en ny klassifisering.
- Valgfritt `agreementId`, `status` (Draft/Approved/Cancelled), `from`/`to` og `page`.
- Datoer er yyyy-MM-dd, inklusive grenser på grunnlagets **InvoiceDate**, med ensidig grense tillatt. DateOnly.MinValue, omvendt intervall, ukjente enumverdier/argumenter og ugyldige ID-er avvises. Ingen frie søkeuttrykk eller implisitt «i dag».
- Page er 1–10000, standard 1. Sidestørrelsen er 25.

Svar: schemaVersion1, accountId, Coverage=`StoredCalculationBases`, SnapshotConsistent=false og Data med RequestedPage/Page/PageSize, TotalCount/ReturnedCount, HasMore/NextPage, Complete/LimitReached og Items. Sortering er GeneratedUtc synkende, Id stigende som entydig siste nøkkel. Side over siste side klampes; RequestedPage beholdes. Ved side 10000 og mer data er NextPage=null/LimitReached=true; bruk snevrere filtre. Complete gjelder hele det filtrerte utvalget, ikke bare vellykket sidehenting. Ingen stille avkutting ved 500 eller in-memory-paging. Antall grunnlag er ikke antall unike avtaler; korreksjoner er egne grunnlag.

Hvert sammendrag inneholder:

| Felt | Betydning |
|---|---|
| Id, AgreementId, OriginalBasisId | Lagret grunnlag, autorisert avtale og eventuell konto-/avtalevalidert originalreferanse |
| Direction, InvoiceDate, Status | Lagret retning, grunnlagets fakturadato og **nåværende lagret** grunnlagsstatus |
| CurrentRevision, SnapshotRevision, GeneratedUtc | Gjeldende revisjon, valgt snapshotrevisjon og grunnlagets genereringstid uten aktør |
| AgreementTitle, CounterpartyId/Name, Currency, TaxTreatment | Lagrede snapshotetiketter, kontoverifisert motpart og snapshotets økonomiske metadata |
| Amount | Sum av **lagrede hendelsesbeløp**, ikke ny beregning eller sum av segmentenes målbeløp |
| PeriodFrom/To | Min/Max av valgte snapshots lagrede segmenters From/To; null når ingen segmenter finnes |
| EventCount, SegmentCount, Freshness | Faktiske antall i valgt snapshot og alltid NotEvaluated |

Liste bruker gjeldende snapshotrevisjon for hver rad. Manglende eller ugyldig snapshot filtreres ikke stille bort: hele kallet feiler. Dermed er totalen fortsatt antall autoriserte grunnlag, og en feilet side fremstår ikke som en komplett liste med nullbeløp.

## Detalj: agreements_basis v1

Obligatorisk accountId/basisId. Valgfri positiv int32 `revision` velger et **eksakt lagret snapshot**; uten argumentet brukes CurrentRevision. Ukjent/fremmed/utilgjengelig grunnlag eller valgt revisjon gir RESOURCE_UNAVAILABLE uten eksistensdetaljer. Ingen ubundet uthenting av alle snapshots.

Svar: schemaVersion1, accountId, Coverage=`StoredCalculationBasisSnapshot`, SnapshotConsistent=false og Data med Summary som over og Events. SnapshotRevision kan være historisk mens Summary.Status og CurrentRevision fortsatt kommer fra dagens lagrede header. Det finnes ingen historisk godkjenningsstatus per snapshot; den konstrueres ikke.

Hendelser har LineId, Amount, PreviousAmount, TargetAmount og Segments. Interne EventKey-strenger publiseres ikke. En korreksjons Amount er en lagret differanse; PreviousAmount og TargetAmount må holdes adskilt. Segmentene kan beskrive målbeløpet og trenger **ikke** summere til korreksjonens differanse. Ingen ny beløps-/MVA-/valutaberegning utføres; ikke summer forskjellige valutaer eller revisjoner til én sum.

Segmenter viser LineId/LineName, LineVersionId/PriceVersionId, ReferenceFrom/To, From/To, InvoiceDate, Quantity/UnitPrice, ActiveDays/ReferenceDays, Amount og Included. Segmentenes InvoiceDate og periodegrenser er lagrede felt, ikke normalisert til listefilter eller grunnlagets headerdato. TaxTreatment tillater dagens publiserte `ProcessingTaxExclusive`-nøkkel; ingen ny avgiftsmodell eller ukjent skattebehandling gjettes.

Dette er **lagret beregningsgrunnlag**, eventuelt med lagret Approved-status. Det er verken et nytt beregnet forslag, en faktura, en betaling eller dokumentasjon på at en slik transaksjon finnes. OriginalBasisId og lagrede differansefelt uttrykker korreksjonshistorikk; de utfører ingen korreksjon.

## Avgrenset parsing og referanser

Dagens skriver lagrer uversjonert, PascalCase `AgreementBasisData` som jsonb. Ny Application-projeksjon støtter eksplisitt denne kjente formen; den deserialiserer ikke hele GUI-/Core-entiteter. Ukjent root-/hendelses-/segmentfelt (inkludert en ny SchemaVersion), manglende relevante felt, feil typer/enumerasjoner, identitetsmismatch eller ugyldige økonomiske data gir SNAPSHOT_UNAVAILABLE. Manglende beløp blir aldri null eller null kroner. PostgreSQL håndhever gyldig JSON-syntaks; semantisk ugyldige snapshots testes separat.

Grenser: 65536 tegn per valgt snapshot, JSON-dybde 32, 100 hendelser, totalt 250 segmenter og 100 PreviousSnapshotIds. SQL caster jsonb til text og størrelsesbegrenser før materialisering; for stor verdi sendes ikke til Application. Application sjekker størrelsen igjen og fører kansellering gjennom parsing. Over grensen gir hel RESULT_TOO_LARGE. Eksisterende MCP-grense på endelig svar kan også avvise et ellers gyldig stort snapshot; ingen skjult trunkering eller delvis sum. Brukeren må da bruke en smalere liste, eller behandle dette som en eksplisitt kontraktgrense for detaljen.

Root AccountId/AgreementId må samsvare med autorisert lagringsomslag og Direction med lagret grunnlag. Hver hendelses/segments LineId og segmentets AgreementId/Direction/Currency må samsvare med snapshotet. Motparts-ID-er, linjer, linjeversjoner/priser, originalgrunnlag og tidligere snapshotreferanser slås opp i batch gjennom aktuell konto; Application krever dessuten riktig avtale og linje. En gyldig konto-ID inne i JSON er ingen fullmakt til andre ressurser. Ugyldige referanser avviser hele snapshotet uten fremmede navn eller delbeløp.

Snapshotetiketter er historiske, ikke nåværende navn. De returneres først etter identitets-/referansekontroll. AccountName, PreviousSnapshotIds, dokumentkoblinger, IndexAdjustments, Adjustments og annen intern beregningskontekst blir ikke transportfelt. Innholdet i utelatte dokument-/proposalobjekter derefereres ikke; leseren hevder ikke å ha validert disse private objektene. Ingen rå DataJson/Fingerprint, person/aktør, reason/kommentar, dokument-/modellinnhold, lagringsadresse eller handlingsflagg returneres.

## Aktualitet og transaksjoner

Eksisterende GetBasisAsync åpner `BeginAsync(write:false)` med RepeatableRead. Den laster brede kildesett og kaller Source/CurrentBasisData for ikke-kansellerte grunnlag. Draft sammenlignes med lagret Fingerprint; godkjent original vurderes gjennom en hypotetisk korreksjon og ikke-null differanser. Eksisterende valideringsavvisning kan sette Stale=true. Denne banen kaller ikke Commit/SaveChanges eller initialisering, men beregningen og datamengden er bredere enn en lagret snapshotlesing.

Ny reader **kjører ikke denne aktualitetsvurderingen** og returnerer verken Stale=false eller NeedsCorrection=false. Freshness=`NotEvaluated` gjelder alle lagrede statuser/revisjoner. Godkjent betyr lagret godkjenning, ikke at grunnlaget matcher dagens priser eller avtalevilkår. Eventuell fremtidig vurdering må gjenbruke og avgrense eksisterende regler separat.

Data og batchreferanser leses sekvensielt i én kortlivet RepeatableRead/READ ONLY-transaksjon. Ingen skrivelås, Commit, SaveChanges, initialisering eller generator kalles. Fersk avsluttende autorisasjon bruker oppdatert kontekst utenfor datasnapshotet. Flere MCP-kall/sider deler ikke snapshot; derfor SnapshotConsistent=false. Vertens tids-/SQL-/størrelsesgrenser og kansellering beholdes.

## Agent og nødvendige privilegier

Begge verktøy tilordnes eksplisitt Avtaler – økonomi i ReadAgentAreas. Dette utvalget har nå maksimalt **ni lesere**, med uendret standardgrense 12 og ingen rapportforslag. Katalogen er fortsatt snittet av personlig autorisert, tilgjengelig og eksakt godkjent AutomaticRead. En lavere grense gir ToolLimit før modellen kalles. Eksterne profiler beholder sine egne godkjente kontrakter.

Agenten får faktisk lagret datadekning/freshness og faktiske kilde-ID-er/versjoner/paging. Eksisterende versjonsbundne MCP-kall revaliderer hele den transitive kildehistorikken før gjenbruk. Endret synlig status, revisjon eller snapshot ugyldiggjør avhengig historie; tilbakekalt avtaleinnsyn avviser detaljkilden og endrer listetreffene. Ingen direkte datalesefallback, servicekonto eller finansrapportadapter. Lagrede avtale-/forespørselsrapporter bruker sine eksisterende adaptere uten AI.

Nye SELECT-behov nedenfor er bare dokumentert, **ikke operativt utført**. Eksisterende autorisasjons-/avtalefinanskolonner brukes for ressurs- og referanseoppslag.

```sql
GRANT SELECT ("Id", "AccountId", "AgreementId", "Direction", "InvoiceDate", "OriginalBasisId", "Status", "Revision", "GeneratedUtc") ON public.agreement_bases TO tenantplatform_mcp_reader;
GRANT SELECT ("Id", "AccountId", "AgreementId", "BasisId", "Revision", "DataJson") ON public.agreement_basis_snapshots TO tenantplatform_mcp_reader;
```

Ingen GeneratedBy/ApprovedBy/CancelledBy/ActorUserId, Reason, Fingerprint, heltabellprivilegier eller skriverettigheter kreves. Isolerte testroller får bare disse tilleggene; SaveChanges-vakt og avvist privat SQL beholdes. Senere aktivering krever korrekte kolonneprivilegier, vanlig oppdagelse/klassifisering/publikum og **eksakt ny verktøygodkjenning**. Eksisterende OAuth-samtykke/registergodkjenning åpner dem ikke automatisk.

## Eksempelspørsmål og manuell kontroll

1. «Finn lagrede inntektsgrunnlag for [avtale] med fakturadato i januar 2026. Vis totalen og neste side.»
2. «Vis detaljene i grunnlag [ID], med lagret status, beløp, valuta og perioder. Er aktualitet mot dagens vilkår vurdert?»
3. «Hent revisjon 1 av samme grunnlag. Skill valgt snapshot fra dagens lagrede status, og skill eventuelle korreksjonsbeløp fra målsegmentene.»

Manuelt gjenstår aktivering/godkjenning, kilde- og revisjonsvisning i økonomiutvalget, navigasjon fra liste til detalj, ugyldig/for stort snapshot, paging og tilbakekalling/statusendring i annen økt. Tidligere uavklarte interaktive kontroller og 9E-driftsfeil er fortsatt ubekreftet. Containerbygg/8C er fortsatt utsatt. Faktisk automatisert verifikasjon og resterende grunnlagsoperasjoner føres i moderniseringsplanens checkpoint 7.

[Automatisert verifikasjon og presist neste uttrekk](modernization-plan.md#42-steg-9f-checkpoint-7-lagrede-beregningsgrunnlag) er ført i checkpoint 7. Hele grunnlagsraden og hele9F er fortsatt ikke ferdigstilt.
