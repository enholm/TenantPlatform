# Avtaledetaljer, oppfølgingshendelser og relasjoner – 9F checkpoint 4

Dette utvider eksisterende avtaleområde. Det gir ikke et nytt rapportområde, flere rettigheter eller automatisk godkjenning. Alle tre innganger kaller `AgreementReadExtensions` i Application med fersk felles autorisasjon. Infrastructure mottar betrodd scope og utfører konkrete, skrivefrie projeksjoner. GUI-fasadene og deres initialisering brukes ikke.

## Feltavstemming mot AgreementDetailsDto / ReadAsync

Den gamle GUI-DTO-en arver også skrivekommandoens felter. Den publiseres aldri direkte. Nåværende tilstand projiseres i SQL; person-/grant-/dokument-/noticehistorikk lastes ikke for MCP-detaljkallet.

| Felt i eksisterende DTO | Transport og konkret beslutning |
|---|---|
| Id, Title, Type, Status, StartDate, EndDate, NoticeDeadline, RenewalDate, IsArchived | Allerede publisert i `agreement`; beholdt |
| CounterpartyOrganizationId/CounterpartyName, BuildingId/Name, UnitId/Name, OrganizationElementId/Name, GeographicAreaId/Name | Allerede publisert som kontoavgrensede `{id,name}`-referanser i sammendraget. Ugyldig/fremmed relasjon gir null uten å fjerne avtalen. Hierarkinavn er sammendragets nodenavn, ikke GUI-ens fulle Path |
| Description, Terms | Nå publisert i `business`. Ordinære lagrede avtalebeskrivelser/vilkår, også når brukeren tidligere har godkjent dem fra et analyseforslag. Dette er ikke rått modell-/dokumentinnhold. Teksten behandles som ubetrodde data, aldri instruksjoner. Feltene utelates ikke bare fordi de er fritekst |
| Direction, Currency | Nå publisert som lagret retning/valuta, uten finanslinjer eller summer |
| IndexId, IndexName, IndexSetupNeedsReview | Nå publisert som kontoavgrenset `business.index` og review-flagg. Fremmed/manglende indeks gir null; ingen indeksverdier, kildekonfigurasjon eller personmetadata |
| Form, NoticeMode, NoticeCount, NoticeUnit, CurrentPeriodStartDate, AutoRenew, RenewalMonths | Nå publisert i `business` som lagrede forretningsregler og aktuell periode |
| NoticeSnapshot.Form/NoticeMode/NoticeCount/NoticeUnit/StartDate/PeriodStartDate/EndDate/RenewalDate/NoticeDeadline/AutoRenew/RenewalMonths/Terms | Aktuell tilstand dekkes av de navngitte feltene over. Ingen parallell serialisering av GUI-snapshotet |
| NoticeSnapshot.EffectiveDate/RegisteredCount/RegisteredUnit/CessationDate/RegisteredUtc | Nå publisert som TerminationEffectiveDate, TerminationNoticeCount, TerminationNoticeUnit, CessationDate og TerminationRegisteredUtc. Ingen aktøridentitet |
| DeadlinesInitialized, CreatedUtc, UpdatedUtc | Nå publisert som tilstands-/tidsmetadata. False og tom historikk er gyldig og utløser ingen backfill |
| OwnerUserId/OwnerName, CreatedByName, UpdatedByName, TerminationRegisteredByName, NoticeSnapshot.RegisteredByUserId | Fortsatt avgrenset personmetadata; ikke del av kontrakten |
| Access | Grants og person-/delingsvalg publiseres ikke |
| CanEdit, CanManageAccess, ResolveIndexSetup, BeginNewPeriod, NewPeriodStartDate | Handlingsflagg/kommandoverdier, ikke ordinære leseverdier; publiseres ikke |
| Revision | Teknisk skrivevern; publiseres ikke som ny skrivefullmakt |
| Documents | Separat metadatauttrekk gjenstår. Binærinnhold/StorageKey/lagringsadresser er utenfor |
| NoticeHistory | Separat, avgrenset regel-/oppsigelseshistorikk gjenstår. Rå BeforeJson/AfterJson, aktør og kommentarer åpnes ikke gjennom dette uttrekket. Nåværende felt må ikke fremstilles som historiske snapshots |

Detaljraden i dekningsoversikten forblir derfor delvis åpen for noticehistorikk. Dokumentmetadata har dessuten sin egen åpne rad. Nåværende notice-/periodefelt er dekket; historisk regelendring er ikke dekket av oppfølgingshendelsene nedenfor.

## agreements_details – eksplisitt kontrakt v2

Input er fortsatt obligatorisk accountId og agreementId (ikke-tomme UUID-er), ingen andre felt. Kontrakten har `schemaVersion="2"`, de eksisterende toppfeltene accountId/complete/snapshotConsistent/`agreement` og den nye, eksplisitte `business`-projeksjonen over. Summary-feltene er beholdt, men dette er en uttrykkelig versjonsendring, ikke en påstand om at strenge v1-konsumenter automatisk tåler nye felt.

Beskrivelse og outputSchema endrer registerets kontraktfingeravtrykk. Eksisterende godkjenning eller historikkbinding oppgraderes ikke automatisk. Kilden må oppdages og den eksakte nye versjonen godkjennes. Agenten forventer v2 for akkurat dette verktøyet og v1 for de øvrige egne leserne. `AgreementDetailsResult` v1 beholdes som gammel type; v2 bruker `AgreementBusinessResult`. Eksisterende lagrede rapporter bruker query/aggregate og endres ikke.

Application krever konkret avtaleinnsyn. Eier, Read og Edit har samme ordinære feltutvalg; arkiv eller Terminated innfører ikke nytt leseforbud. Complete betyr den eksplisitte projeksjonen, ikke alle vedlegg/historikker/økonomiske linjer. Summary og business leses sekvensielt og har ingen transaksjonssnapshotgaranti; snapshotConsistent=false.

Feltene trunkeres ikke. Eksisterende grense på hele MCP-resultatet gjelder fortsatt: et for stort svar gir RESULT_TOO_LARGE for hele kallet. Ingen stille tekstklipping eller partiell Complete=true.

## agreements_followup_history v1

Obligatorisk accountId/agreementId, valgfri page 1–10000 (standard1). Fast side25. Bare historikk for frister i den konkrete autoriserte avtalen og samme konto, med join via DeadlineId. Resultatet inneholder Coverage=`StoredDeadlineFollowupEventsOnly`, RequestedPage/Page/PageSize, TotalCount/ReturnedCount, HasMore/NextPage, Complete/SnapshotConsistent/LimitReached og Items:

- Id: historikkpostens stabile identitet.
- DeadlineId: den uforanderlige fristforekomsten, ikke personidentitet.
- CreatedUtc: lagret hendelsestidspunkt.
- Kind: Created, Replaced, Withdrawn, Started, Assigned, Commented, Completed, Reopened eller Archived, fra eksisterende enum.

Sortering er CreatedUtc stigende og Id som entydig siste nøkkel, også ved samme tidspunkt. Page over siste side klampes til siste side, med faktisk total og RequestedPage beholdt. HasMore uten videre side ved10000 gir LimitReached=true; ingen falsk fullstendighet eller snapshot på tvers av kall.

Historikkmodellen har ikke før-/etterstatus eller før-/etterdato. Disse konstrueres ikke fra dagens frist. Commented/Assigned kan vise at en hendelse finnes, men ingen kommentar, aktør, tildelt person, mottaker, kø eller fritekstpayload returneres. Dette er oppfølgingshistorikk, ikke fullstendig revisjonsspor, noticehistorikk eller historisk avtaletilstand. Eksisterende GUI-historikk og lagret eierskap endres ikke.

## agreements_relations v1

Obligatorisk accountId og kind: Counterparty, Building, Unit, OrganizationElement eller GeographicArea. Valgfri search (maks200 tegn, ingen kontrolltegn) og page1–10000. Ett relasjonsoppslag per kall; dette er ikke en vilkårlig dispatcher. Alle alternativene er samme type ordinært ID/navn-oppslag.

`AgreementManagementService.GetOptionsAsync` krever opprettings-/redigeringsadgang og returnerer skriveeditorens bredere katalog, inkludert personer. Den brukes derfor ikke. Ny Application-inngang definerer eksplisitt et smalere leseomfang: bare relasjoner faktisk brukt av nå synlige avtaler, med kontoavgrenset relasjonsjoin. Felles GetAgreementListScopeAsync gjelder før oppslaget. Ingen ny generell registerrettighet, tilgjengelighetsfullmakt eller administrator-katalog for vanlige lesere.

Resultatets Coverage er `RelationsUsedByVisibleAgreementsOnly`; Items inneholder bare Id/Name. Én rad per faktisk relasjon, selv om flere avtaler bruker den. Like navn med forskjellige ID-er beholdes. Søk er bokstavelig, case-insensitivt delstrengsøk i nodens navn; ingen frie uttrykk eller jokertegn. Sortering Name/Id, fast side25, faktisk total og samme paging-/grensesemantikk som historikk. Modellen skal vise alternativene og be om avklaring ved tvetydighet, aldri gjette ID fra navnet.

Synlige arkiverte avtaler og deres inaktive, fortsatt tilknyttede relasjoner beholdes. Ubrukte relasjoner, relasjoner brukt bare av utilgjengelige avtaler og fremmede/manglende referanser utelates. Hierarkinoder må være direkte tilknyttet en synlig avtale; ingen automatisk utvidelse til administratorens hele tre eller alle forfedre/etterkommere. Ingen person-/eier-/medlemsvalg. Slike oppslag gir ikke innsyn i flere detaljer enn eksisterende kontoavgrensede referanser i autoriserte sammendrag.

## Agent, feil og minste SELECT

Eksakt områdetilordning inkluderer begge nye verktøy i Agreements. Det er nå fjorten serververktøy: accounts_list, elleve avtalelesere og to forespørselslesere. Avtalechatten har elleve lesere pluss eventuelt ett forslag = tolv. Dermed trengs ikke et nytt underutvalg i dette checkpointet. Konfigurert lavere grense feiler tydelig før modellkall; ingen stille trunkering eller større standardgrense. Neste nye avtaleleser vil kreve et eksplisitt underutvalg før katalogen kan vokse videre.

Agenten bruker personlig OAuth og eksakt versjonsbundet registerkall. Begge nye kilder gir faktisk pagingproveniens og revalideres før historikk gjenbrukes, som andre 8B-kilder. Ingen direkte datalesefallback, profiloppgradering, automatisk godkjenning eller ny rapportadapter.

Detalj/relasjoner benytter allerede tillatte avtale-/registerkolonner. I tillegg trengs følgende avgrensede SELECT for indeksnavn og hendelser (dokumentasjon, **ikke operativt utført**):

```sql
GRANT SELECT ("Id", "AccountId", "Name")
ON public.agreement_indices TO tenantplatform_mcp_reader;
GRANT SELECT ("Id", "AccountId", "DeadlineId", "Kind", "CreatedUtc")
ON public.agreement_deadline_history TO tenantplatform_mcp_reader;
```

Ingen hel historikk-/indekstabell, ActorUserId, AssignedUserId i historikk, Comment, indekskildekonfigurasjon eller writes er gitt. Eksisterende avtalescope gjelder disse avtaleoperasjonene; ingen nye domenescopes innføres. SaveChanges-vakten, databasebruker uten eierskap/skriveadgang og runtime-kontrollen beholdes. Manglende SELECT gir sikker kildefeil, ikke fallback eller automatisk GRANT.

Felles feil er INVALID_ARGUMENT, RESOURCE_UNAVAILABLE (samme for ukjent/fremmed/utilgjengelig konkret ressurs), RESULT_TOO_LARGE, CANCELLED og TEMPORARILY_UNAVAILABLE. Rå SQL, transportfeil, tokens og credentials returneres ikke.

## Eksempelspørsmål og manuell kontroll

1. «Finn avtalen med [tittel], og vis beskrivelsen, vilkårene og gjeldende oppsigelsesregel. Skill lagret dato fra historikk.»
2. «Vis lagrede oppfølgingshendelser for [avtale], også neste side. Er dette et fullstendig revisjonsspor?»
3. «Hvilke bygg med navn Plaza brukes av avtaler jeg kan lese? Vis alternativene med ID før du velger ved like navn.»

Etter operatørens eksplisitte SELECT og ny registeroppdagelse/klassifisering/godkjenning: kontroller beskrivelse/vilkår og nullreferanser, Read/Edit/eier/arkiv, historikkens neste/siste side og like tidspunkt, relasjoner med samme navn og tilbakekalt grant i en annen økt. Kontroller at gamle v1/v2/v3-rapportsider og AI-redigering fortsatt virker, også modellfritt etter lagring. Ingen interaktiv verifikasjon eller driftsaktivering er utført. Tidligere 9E-driftsfeil står fortsatt ubekreftet; containerbygg/8C er utsatt.

Gjeldende katalogutvalg fra checkpoint5 er dokumentert i [avtaleøkonomi og underutvalg](mcp-agreement-finance-read.md). De opprinnelige katalogtallene i checkpoint4 er historikk; nå fordeles verktøyene på eksplisitte avtaleunderutvalg med uendret tolvgrense. Eksisterende detalj-/historikk-/relasjonskontrakter er beholdt.
