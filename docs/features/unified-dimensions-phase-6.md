# Unified Dimensions — fase 6: dynamisk avtaledashboard

**Status: FERDIG for fase 6.** Dashboardet bruker fire faste grupperinger og ordinære, konfigurerte dimensjoner. Hierarki, unike opptellinger, overlapp, direkte tildelinger, drill/back og beskyttelse mot sene svar er implementert og kontrollert databasefritt. Ingen PostgreSQL- eller browserverifikasjon er utført. Fase 7 er ikke startet.

## 1. Arbeidsgrunnlag og omfang

- Arbeidskatalog: `/Users/morten/Dev/TenantPlatform`.
- Branch: `main`.
- Startcommit: `f2f2d67338fd05e59624423aad675ec4f8ca415d`.
- Grunnlag: `unified-dimensions.md`, fase-1–5-rapportene og fase-1-pakken, brukerens fase-6-prompt, innlimte AGENTS.md-instruksjoner og repoets `Agents.md`. Faktisk dashboardkode, eksisterende tester, autorisasjon, hierarkihjelpere og fase-5-generator er kontrollert.
- Ved fasestart fantes alle godkjente, sporede/usporede fase-1–5-endringer. SHA-256 for **713 filer** ble registrert i `/tmp/tenantplatform-phase6-start-sha256.json`; manifestets SHA-256 er `5699c689219b7baa2a4fed8c868e20991024fb8f053daadf328c1803ed4e0ba4`.
- Ingen reset, branchbytte, commit, merge, push, deploy, AgentOrchestrator eller rettighetsutvidelse. Ingen tidligere filer er slettet. Utenfor de oppførte overlappende filene er tidligere arbeid byteuendret.
- Ingen skjema-/migrasjons-, Core-, Infrastructure-, demo-, Leasing- eller avtaleøkonomi-endring. Ingen nye pakkeavhengigheter, autorisasjonsarkitektur eller hierarkilagringsmodell.

| Status fra fasestart | Fil |
|---|---|
| Endret | `src/TenantPlatform.Web/Services/Agreements/AgreementDashboardDtos.cs` |
| Endret | `src/TenantPlatform.Web/Services/Agreements/AgreementService.Dashboard.cs` |
| Endret | `src/TenantPlatform.Web/Services/Agreements/IAgreementService.cs` |
| Ny | `src/TenantPlatform.Web/Services/Agreements/AgreementInsightHierarchy.cs` |
| Endret | `src/TenantPlatform.Web/Components/Pages/Dashboard/InsightsWidget.razor` |
| Endret | `src/TenantPlatform.Web/Resources/TenantPlatformResources.nb-NO.resx` |
| Endret | `src/TenantPlatform.Web/Resources/TenantPlatformResources.en-GB.resx` |
| Endret | `src/TenantPlatform.Web/Resources/TenantPlatformResources.sv-SE.resx` |
| Endret | `tests/TenantPlatform.Dashboard.SmokeTests/Program.cs` |
| Endret | `tests/TenantPlatform.Dashboard.SmokeTests/TenantPlatform.Dashboard.SmokeTests.csproj` |
| Endret | `tests/TenantPlatform.Dashboard.SmokeTests/README.md` |
| Ny | `tests/TenantPlatform.Dashboard.SmokeTests/HierarchyChecks.cs` |
| Ny | `tests/TenantPlatform.Dashboard.SmokeTests/DynamicWidgetChecks.cs` |
| Ny | `docs/features/unified-dimensions-phase-6.md` |

## 2. Eksisterende filtre og scope

Før endringen hadde InsightsWidget seks hardkodede valg og ett hierarkivalg. **Innsikt omfatter alle tilgjengelige, uarkiverte avtaler, uansett status.** Widgeten har ikke egne søke-, status-, type- eller datofiltre. Dette scopet er beholdt; ingen nye forretningsfiltre er innført.

Attention og kontraktlisten er separate funksjoner: kommende oppsigelse/fornyelse gjelder aktive avtaler innen dag 0–90; utløpt gjelder aktive/utløpte avtaler med sluttdato før kontodagen. Prioritetslisten viser inntil fem avtaler med eksisterende dato-/tittel-/ID-sortering. Attention-klikkets AgreementFilter og ListAsync er uendret. GetAttentionAsync og GetDashboardContractsAsync er sammenlignet med gammel kilde og er uendret; deres produksjonsquery-metoder og eksisterende tester er beholdt.

Hierarkivalget er fortsatt en filtrering av innsiktskonteksten, nå supplert med knapper for drill/back. `RefreshVersion` kan be eierkomponenten oppdatere gjeldende datasett uten å miste en fortsatt gyldig kontekst; den legger ikke til et nytt tjenestefilter eller endrer scope.

## 3. Entydig grupperingskontrakt og dynamisk katalog

`AgreementInsightGrouping` inneholder enten:

- `Fixed`: Type, Counterparty, Status eller Owner fra enumen AgreementInsightDimension, som nå bare beskriver de fire faste valgene.
- `DimensionId`: ordinær GUID for en dimensjon.

Validate krever nøyaktig én discriminator, kjent enumverdi og ikke-tom dimensjons-ID. Det finnes ingen dedikerte Geography-/Organization-varianter, gammel GetInsights-overload eller gammel GroupHierarchy-metode. Razor-tilstand bruker den typede kontrakten; native select verdier har uttrykkelige `fixed:`-/`dimension:`-prefikser. Lokalisert navn/kode brukes ikke som dimensjonsidentitet.

`GetInsightDimensionsAsync` krever valgt konto og aktivt medlemskap gjennom eksisterende RequireMemberAsync. Den leser kontoens aktive dimensjoner med en enabled Agreements-regel og bruker DimensionLocalization.Name med kontostandard og fase-2-fallback. Ordinære medlemmer og read-only mottakere trenger ikke edit- eller registeradministrasjonsrett. DimensionService brukes ikke.

GEO og DEPT behandles likt med alle andre dimensjoner. Inaktive, deaktiverte, ukonfigurerte og fremmede dimensjoner er utelatt; GetInsightsAsync kontrollerer også tilgjengeligheten på nytt ved direkte forespørsel. De fire faste valgene og deres navn, gruppering og sortering er beholdt.

## 4. Aggregering og totalfelter

`AgreementInsightHierarchy.Aggregate` er ren produksjonskode som både tjenesten og testene kjører. Den tar en dimensjonsdefinisjon, verdimetadata, autoriserte avtale-ID-er og eksplisitte `(AgreementId, ValueId)`-par.

1. DimensionHierarchy.Build bygger og validerer den komplette dimensjonsgrafen, med eksisterende sortering og uten fast dybdegrense. Inaktive verdier/forfedre beholdes.
2. Avtale-ID-er og tildelingspar dedupliseres. Ukjent tildelt verdi eller tildeling utenfor det projiserte avtalesettet gir kontrollert feil, ikke «Ikke angitt» eller stille undertelling.
3. Per eksplisitt verdi holdes et HashSet av direkte avtale-ID-er. En iterativ foreldretraversering legger samme avtale-ID i hvert forfedresett. Felles forfedre telles derfor bare én gang, selv ved flere søsken eller samtidig forelder/etterkommer.
4. Non-admin får bare noder med synlige tildelinger og deres forfedre. Admin får hele kontoens aktuelle dimensjonshierarki.
5. Grupper beregnes fra sett, aldri ved summering av barn. Bare umiddelbare barn/røtter vises som ordinære nodegrupper.

| DTO-felt | Betydning |
|---|---|
| `TotalUniqueAgreements` | Unike synlige, uarkiverte avtaler i hele widgetens datasett, også uten valg i dimensjonen. Endres ikke bare fordi man driller. |
| `ContextUniqueAgreements` | På toppnivå: samme unike datasett. Ved valgt node: unike avtaler tildelt noden eller dens etterkommere. |
| `Groups[].Count` | Unike avtaler i akkurat den viste gruppen. |
| `GroupSum` | Sum av de viste gruppenes Count; prosentnevner, **ikke** unik total. |
| `SelectedNodeId` | Validert hierarkikontekst, eller null på toppnivå. |
| `Options` | Bare noder brukeren har tilgang til, med ID, ParentId, sti, dybde og aktivitet. |
| `AllowMultiple` | Global metadata som gir forklaring om mulig overlapp; filtrerer ikke historiske tildelinger. |

De faste grupperingene leser unik total eksplisitt med CountAsync over samme autoriserte query. Dynamiske grupper bruker den unike ID-mengden fra den autoriserte venstrejoinen. GroupSum beregnes separat i DTO-en. UI deler Count på GroupSum, og både legendetekst og CSS-gradient bruker samme nevner. Ved GroupSum=0 vises tomtilstand uten prosentdivisjon eller gradient.

Eksempel fra testene: fire synlige avtaler kan gi rottall 3 og 1 pluss én «Ikke angitt», altså GroupSum=5. Roten med tre avtaler får **60 %**, ikke 75 %. Ved en underkontekst er unik total 3, men direkte-gruppen 2 og barnegruppene 2+1 gir GroupSum=5. Direkte forelder/etterkommer-overlapp beholdes uten å blåse opp forelderens unike total.

## 5. Drill-down, direkte-gruppe og Ikke angitt

Toppnivå viser røtter. Nodeknapper og den merkede hierarkivelgeren kan åpne en node; neste resultat viser umiddelbare barn. «Tilbake ett nivå» bruker validert ParentId, og «Toppnivå» velger null. Full sti viser konteksten. Blader kan åpnes og viser sine direkte tildelinger; admin kan også åpne en gyldig tom kontekst og få kontrollert tomtilstand.

AgreementInsightGroupKind skiller Fixed, Node, Direct og Unspecified. Bare **Node** har NodeId og en nodeknapp. En direkte-gruppe har egen nøkkel, tydelig direkte-etikett og **NodeId=null**; den kan ikke feiltolkes som en ordinær hierarkinode. Ved drill i en forelder bidrar parent-only-avtaler bare til direkte-gruppen. En avtale med både forelder og barn kan bidra til både direkte- og barnegruppen, men bare én gang i konteksttotalen. Direkte-gruppen er ikke en ekstra ordinær foreldrenode i samme prosentgrunnlag som barna.

«Ikke angitt» er avtaler helt uten eksplisitt tildeling i den valgte dimensjonen og finnes bare på toppnivå. En inaktiv tildeling rulles fortsatt opp og blir ikke «Ikke angitt». Globale endringer til single-select eller LeafOnly skjuler ikke gamle foreldre-/flervalgstildelinger i opptellingen. Ingen ancestor-rader skrives eller opprettes.

Sykluser, utilgjengelige foreldre eller inkonsistente tildelingsreferanser avvises med DashboardInvalidHierarchy. Vilkårlig fremmed/skjult selected-ID avvises med UnauthorizedAccessException før noe resultat med nodenavn returneres. Det utføres ingen automatisk reparasjon av metadata.

## 6. Oppdatering, kontekst og asynkron tilstand

Widgeten laster tilgjengelig katalog og aktuelle data på nytt ved gruppering, navigasjon, kontoendring, retry eller endret RefreshVersion.

- Grupperingsbytte nullstiller node. Kontobytte nullstiller node/katalog og går til fast Type, slik at ID-er ikke gjenbrukes mellom kontoer eller dimensjoner.
- En fortsatt gyldig kontekst beholdes ved refresh. Dersom tilgang/datasett har fjernet den, avviser serveren nodeforespørselen. Widgeten gjør deretter én kontrollert toppnivåforespørsel og forklarer reset med lokalisert tekst. Den gjetter ikke en skjult sti.
- Er selve dimensjonen ikke lenger aktiv/tilgjengelig, brukes en tilgjengelig fast gruppering og toppnivå.
- Data settes til null umiddelbart når en nyere forespørsel overtar. Gamle antall/hierarkinavn vises ikke under den nye lastefasen.
- Hver innlasting har økende forespørselsversjon og eget CancellationTokenSource. Både katalog-, data-, feil- og finally-publisering kontrollerer at forespørselen fortsatt er gjeldende. Gamle svar forkastes også når tjenesten ignorerer cancellation. Dispose ugyldiggjør forespørslene.

Serveren fortsetter å avvise direkte skjulte/fremmede node-ID-er; UI-reset er leseflyt for en foreldet kontekst, ikke svekket autorisasjon. Ved fortsatt tilgangsfeil på toppnivå vises eksisterende feil-/retry-tilstand.

## 7. Tilgang og vern mot lekkasje

Tre forskjellige mengder holdes atskilt:

1. **Definisjoner:** kontoens aktive, Agreements-tilgjengelige dimensjoner er en ordinær lesekatalog for aktive medlemmer.
2. **Noder:** non-admin får bare verdier som er tildelt synlige avtaler og deres forfedre. En definisjon kan derfor finnes i katalogen mens Options er tom.
3. **Avtaler:** telleren starter fra Accessible med AccountId, konto-admin, eier eller eksplisitt deling, og utelukker arkiverte. Read-deling er tilstrekkelig; platform-admin-flagg gir ikke account-admin-tilgang.

Alle account-owned lesinger har eksplisitt AccountId. Dimensjons-/regel-/verdilesing avgrenses før metadata brukes; assignments avgrenses på konto/dimensjon og joines med den autoriserte avtalequeryen **før materialisering**. Ingen kontoavtaler hentes for senere filtrering i Razor. Ingen edit-/administrasjonskatalog brukes som dashboardtilgang.

Hele dimensjonens metadata kan ligge i tjenestens lokale minne under grafvalidering, men skjulte grener fjernes før DTO-er returneres til non-admin. Selected-node må tilhøre den tillatte mengden; feilmeldinger inneholder ingen skjulte navn. Tester dekker eier, read-deling, medlem uten delte avtaler, konto-admin, platform-admin uten konto-adminrett, inaktiv bruker, skjult node, fremmed node/dimensjon og kontoavvik.

## 8. Spørringer og ytelse

De faste grupperingene beholder SQL GroupBy og eksisterende kontoavgrensede navnejoins. Unik total leses separat som CountAsync. Dynamisk GetInsightsAsync leser:

- én tilgjengelig dimensjonsdefinisjon med nødvendige felt;
- verdier for akkurat konto/dimensjon, projisert til ID/forelder/navn/kode/sortering/aktivitet;
- én venstrejoin fra synlige uarkiverte avtaler til assignments for valgt konto/dimensjon, projisert til AgreementId og nullable ValueId.

Venstrejoinen beholder avtaler uten valg og gir både hele ID-settet og tildelingspar i samme projeksjon. Dupliserte join-rader telles ikke som ekstra avtaler. Ingen økonomi-/dokumentgraf, query per node/avtale eller databaseforespørsel per forfader. Katalogen leser kontostandard og dimensjoner med oversettelsesprojeksjon; antall databaseforespørsler avhenger ikke av antall noder/forfedre. Eksisterende medlems-/rolleoppslag utføres ved hver tjenestegrense.

La V være synlige avtaler, A eksplisitte tildelingspar, N verdier og D hierarkidybde. Settopptelling bruker omtrent O(V + A·D) arbeid og opptil O(V + N + A·D) settlagring. Eksisterende hierarkibygger sorterer søsken og bygger stier, med tillegg for navnelengde/dybde; visningsgrupper sorteres også. Dette er en bevisst ren beregning etter avgrenset projeksjon, ikke en ny hierarkilagring. Store grafer bør belastningstestes senere; ingen PostgreSQL-plan-/ytelsesmåling er utført.

Flere metadata-/opptellingslesinger er ikke et verifisert PostgreSQL-snapshot. Konkurrerende databaseendringer og providerens faktiske query-eksekvering er en senere verifikasjonsport. Det er ikke innført transaksjons-/testprovider-bypass for å gjøre InMemory til relasjonelt bevis.

## 9. UI og lokalisering

11 nye ressursnøkler er lagt til i alle tre språk. De 24 literalnøklene widgeten og den nye valideringsfeilen bruker, er kontrollert for én forekomst og ikke-tom verdi i nb-NO, en-GB og sv-SE. Eksisterende faste labels, status-/typeetiketter, scope-, laste-, feil-, retry- og tomtekster gjenbrukes.

Widgeten viser valgt gruppering, full hierarkikontekst, back/top, unike totaler, gruppesum, antall og prosenter. Overlapp forklares når AllowMultiple=true **eller** historiske valg faktisk gir GroupSum større enn ContextUniqueAgreements. Historisk/inaktiv status har tekstlig merking. Native selects har labels, nodeknapper har lokaliserte tilgjengelige navn, tabellen har caption/kolonne-/radoverskrifter, og diagrammet er aria-hidden med tekstlig legend. Betydning formidles ikke bare med farge/ikon.

## 10. Faktiske tester og resultater

| Kommando/kontroll | Resultat |
|---|---|
| `dotnet build TenantPlatform.sln --no-restore` | PASS; fire prosjekter, 0 feil, 0 advarsler etter produksjonsendringene |
| `dotnet build <prosjektsti> --no-restore` for alle 13 `tests/*/*.csproj` | PASS; alle exit 0 |
| `dotnet build tests/TenantPlatform.Dashboard.SmokeTests --no-restore` | PASS; også etter siste testutvidelse |
| `env -u DASHBOARD_PREVIEW_PATH dotnet run --project tests/TenantPlatform.Dashboard.SmokeTests --no-build --no-restore` | PASS; **107 PASS-linjer**, inklusive nye produksjons-/komponentkontroller |
| `dotnet run --project tests/TenantPlatform.Dimensions.SmokeTests --no-build --no-restore` | PASS; 91 kontroller |
| `dotnet run --project tests/TenantPlatform.AgreementDimensions.SmokeTests --no-build --no-restore` | PASS; 166 kontroller |
| `dotnet run --project tests/TenantPlatform.DemoDimensions.SmokeTests --no-build --no-restore` | PASS; full felt-/grafekvivalens, 732688 assertions inklusive gjentatte strukturkontroller |
| `python3 docs/features/unified-dimensions-phase-1/derive-reference.py --check` før og etter arbeidet | PASS; alle tre JSON-filer byteidentiske |
| SHA-256 mot fasestart og kontroll av migrasjonsfilsett | PASS; 89 migrasjonsfiler inkl. snapshot, tidligere rapporter/referanser, demo og Leasing byteuendret |
| Ressurs-XML, aktive legacy-dashboardtreff og Attention-/kontraktkodekontroll | PASS; 24 nøkler per språk, ingen gamle grupperingssymboler/metode, uendrede Attention-/kontraktmetoder |
| `git diff --check` | PASS |
| PostgreSQL-spørringer eller vanlig Web-start | IKKE UTFØRT |
| Browserinteraksjon, layout, fokus, tastatur og skjermleser | IKKE UTFØRT |

Alle 13 testprosjekter er bygget eksplisitt: AccountRegisters, AgreementAnalysis, AgreementDimensions, AgreementFollowup, AgreementNotice, AgreementPeriods, AgreementProcessing, Agreements, Dashboard, DemoDimensions, Dimensions, Leasing og MeetingRooms. De **ni databasebundne runnerne er bare kompilert**, ikke kjørt. Eksisterende cacheadvarsler NU1902/NU1903 for Pkcs 10.0.0 og MSB9008 om tidligere Application-prosjekt forekommer fortsatt i enkelte testbygg; de er ikke reparert med uvedkommende kildeendringer eller undertrykt.

Lokale logger: `/tmp/tenantplatform-phase6-build.log`, `tenantplatform-phase6-test-builds.log`, `tenantplatform-phase6-dashboard-build.log` og `tenantplatform-phase6-{Dashboard,Dimensions,AgreementDimensions,DemoDimensions}-run.log`, alle under `/tmp/`. De er ikke produksjonsavhengigheter.

### Hva som faktisk er testet

- **Produksjonsaggregering:** fire nivåer, ulike dybder/flere røtter, felles forfader, søskenoverlapp, eksplisitt forelder+etterkommer, parent-only, direct, unassigned bare øverst, inactive-only, global single/LeafOnly-endring, nullnevner, dupliserte par, syklus, orphan og ukjent tildeling. Begge totale mengder og GroupSum kontrolleres separat.
- **Produksjonstjeneste med EF InMemory:** autorisert query/projeksjon, alle fire faste grupperinger, dynamisk active/enabled-katalog, språk/ID, eier/read/admin/medlem uten tilgang, inaktiv bruker, konto-/node-/dimensjonsavvisning. Dette er ikke bevis på PostgreSQL-oversettelse, kjøring, FK-er eller låsing.
- **Rendering:** faktiske AttentionWidget, ContractsWidget og InsightsWidget, laste-/feil-/retry-/tomtilstand, dynamiske labels, legend/prosenter, direct/historical/back, totalskillet og språk nb/en/sv. Ny og gammel relativ dato-/prioritets-/Attention-/listekontroll er beholdt.
- **Kalte komponenthandlinger:** grouping, NavigateAsync, BackAsync, refresh/OnParametersSetAsync og retry/LoadAsync via refleksjon på HtmlRenderer-dispatcher. Valgt kontekst, payload til tjeneste, data og renderet HTML kontrolleres. Tilgangs-/datasettendring beholder gyldig kontekst eller resetter ugyldig til topp. Account-/dimensjons-/refresh-race, sent katalogsvar og sen feil testes med en stub som bevisst ignorerer cancellation. Gamle antall skjules mens nytt svar er ventende.
- **Statisk:** Razor event-bindinger, labels, ID-/kind-skille, querygrenser/projeksjoner, arkitektur og bevaring. Eksisterende prioritetsquerys ToQueryString-kontroll kjøres uten åpnet forbindelse. Ingen nye PostgreSQL-queryer er hevdet eksekvert.
- **Ingen browserpåstand:** HtmlRenderer og refleksjonskall er ikke DOM-klikk, layout eller tastaturbevis. Den eksisterende opt-in previewen skriver bare HTML med stilreferanser; preview-variabelen var fjernet i testprosessen. Ingen vanlig app eller browserpreview ble startet.

## 11. Integrert demo-scenario

Testen kaller uendret **MagidaDemoData.Create**, ikke en testgenerator, og sender grafen til samme AgreementInsightHierarchy som tjenesten bruker. Alle 300 avtaler har null legacy-FK-er, så opptellingen kan ikke komme fra gammel klassifisering.

- GEO: 300 unike avtaler, fire røtter med **75 hver**; GroupSum=300.
- DEPT: 300 unike avtaler, fem røtter med **60 hver**; GroupSum=300.
- For **alle 36 GEO- og 30 DEPT-noder** sammenlignes konteksttotal, direkte-antall og hver umiddelbare barnegruppe mot frossen referanse. Det inkluderer land/hovedsteder, divisjoner/avdelinger og alle blader. Ingen Ikke angitt-gruppe finnes under en node.
- GEO-land/hovedsteder beholder 19/19/19/18 per kontinent; avdelingene beholder 12 hver. Ingen redundant ancestor-persistens eller endring i produksjonsgrafen er innført.

Dette er databasefri generering og aggregering, ikke faktisk databasebasert demo-initialisering.

## 12. Bevaring og manuell verifikasjon

Alle **89 eksisterende migrasjonsfiler, Designer-filer og gjeldende snapshot** er byteidentiske med fase-6-start. Ingen nye migrasjoner eller konkrete skjemaavvik er funnet. Snapshot-endringen som vises i samlet Git-diff, kommer fra tidligere godkjente faser.

Alle ni tidligere fase-/referansefiler er urørt. derive-reference.py --check reproduserer fortsatt demo-reference.json (`18c1e1083523112418b9f36bd8d4ab3c196cc86aac8924d4254ea64590a67701`), legacy-inventory.json (`21f2e6dbe2331c51df799232ff2eefd2f30c39c227873336532b0bd787cb16c5`) og migration-source-sha256.json (`0b9f2a30126b7a69e2232d376c1def6e70da1b23411daa234b09dc9d23e3046e`). Fase-1-manifestet gjelder den frosne commiten; fase-6-startkontrollen inkluderer også senere godkjente additive migrasjoner.

Senere sjekkliste i separat autorisert miljø:

1. Åpne widgeten med nb/en/sv, lys/mørk visning og smal/bred skjerm. Kontroller tabell/diagram, lange stier, meny, labels, tabrekkefølge, fokus og skjermleser.
2. Naviger med mus/tastatur gjennom minst fire nivåer, blad, tom admin-gren, back/top, direkte foreldrevalg og Ikke angitt. Kontroller at alle prosentene bruker gruppesummen.
3. Gjør raske konto-/grupperings-/oppdateringsbytter med forsinkede svar. Kontroller at gamle tall/navn ikke gjenoppstår, og at gyldig kontekst beholdes ved relevant oppdatering.
4. Kjør nye katalog-/venstrejoin-/GroupBy-spørringer i godkjent PostgreSQL-testmiljø, med read-only, eier, deling, admin, skjulte grener og fremmede ID-er. Kontroller provideroversettelse, faktisk filtrering, samtidige metadata-/tilgangsendringer og query-planer/større datamengder.
5. Kjør separat godkjent migrasjons-/database-/demoverifikasjon og relevante databasebundne regresjonssuiter; gjenta GEO/DEPT-nodeopptellingen mot frossen referanse. Ingen av disse portene er passert av denne kjøringen.

## 13. Overlevering til fase 7

Ingen kjente uferdige implementasjonspunkter innen fase 6. Browser-/PostgreSQL-/ytelsesverifikasjon gjenstår som angitt. Ingen generell legacy-fjerning er startet.

Gjenværende aktive legacy-forbrukere som fase 7 må gjennomgå:

- Core/Accounts: GeographicArea og OrganizationElement; Agreement har fortsatt de to nullable legacy-ID-feltene.
- Infrastructure: DbSets, to registerkonfigurasjoner og AgreementConfiguration-FK-er, aktivt snapshot og historisk migrasjonskjede.
- Web/Services/Agreements: AgreementDtos, Structure-partialen og legacy-mapping/options/round-trip/validering i AgreementService. Disse skal fjernes samlet med siste forbruker, uten å nullstille eksisterende data gjennom en mellomfase.
- Konto: GeographicAreaService, OrganizationElementService, delte registerhjelpere, de to administrasjonssidene, Program-DI, NavMenu/rutegjenkjenning og AccountService.CanDeleteAccountAsync-sperrer.
- Ressurser og eldre AccountRegisters-/Agreements-tester. Gamle dashboardressursnøkler kan ryddes senere; de brukes ikke som grupperingsvalg nå.
- SeedIds-metoder med tidligere hierarkinavn brukes fortsatt bevisst som stabile verdi-ID-er, og DemoDimensions-testen bruker gammel hierarkihjelper som ekvivalensgrunnlag. Disse er ikke legacy-entitetsgenerering og må behandles semantisk ved opprydding.

Reell Organization/motpart, adresser, bygg/enheter, Leasing og avtaleøkonomi skal fortsatt bevares. Fase 7 skal startes med en ny manuell prompt; ikke med automatisk sletting, legacy-synkronisering eller databaseskjemaendring her.

- PostgreSQL-migrasjonskjøring: **NOT PERFORMED — POST_RUN_VERIFICATION**
- Opprettelse av ny database og databasebasert demo-initialisering: **NOT PERFORMED — POST_RUN_VERIFICATION**
- PostgreSQL-basert spørringskjøring, tilgangsfiltrering og regresjon: **NOT PERFORMED — POST_RUN_VERIFICATION**
