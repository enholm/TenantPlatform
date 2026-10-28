# Byggliste og byggaggregat – STR-001

Steg 10B, avgrenset til bygglistens ordinære data. `buildings_query` og `buildings_aggregate` har kontraktversjon/schemaVersion **1**. Byggdetaljer (STR-002), enhetslister, UnitCount/TenantCount/OpenRequestCount, belegg og byggrapportadapter er ikke med. Status for faktisk verifikasjon føres nederst; operativ aktivering er separat.

## Kallkjede og tilgang

MCPs `ReadTools` binder validert OAuth-subjekt og foreslått konto i en ny `OperationActor`/DI-scope per kall. `BuildingReportingReader` kontrollerer ferskt aktiv bruker, valgt konto og medlemskap gjennom `GetAccountActorScopeAsync`, deretter samme `Buildings.Open` som `BuildingService.GetBuildingsAsync`. Dette **bevarer PropertyAdmins dokumenterte brede kontolisteunntak**, også med tildeling til bare ett bygg. Ingen avtalefullmakt eller detaljautorisasjon utledes fra listen. Kontovid AccountAdmin beholder sitt eksisterende innsyn. Et vanlig medlem eller global PlatformAdmin uten kontotilgang får ikke data. Autorisasjon/aktør revalideres etter SQL-lesingen før resultat returneres.

`BuildingReportingData` anvender Application-godkjent konto og samme søk før SQL-liste/telling/gruppering. Ingen egen rollemodell, `GetBuildingAsync` per rad, GUI-fasade, initialisering, SaveChanges, historikk/audit-write eller bakgrunnsjobb kalles. Hver metode bruker egen kortlivet DbContext med sekvensielle operasjoner. Ingen EF/IQueryable/HTTP/MCP-SDK i Application.

## Argumenter og filter

| Argument | Query | Aggregate | Betydning |
|---|---|---|---|
| accountId | Påkrevd | Påkrevd | Ikke-tom UUID i D-format; Application revaliderer konto mot betrodd innringer. |
| search | Valgfritt | Valgfritt | Trimmet delstreng i byggnavn; maksimalt 200 Unicode-kodepunkter etter trimming, kontrolltegn avvises også før trimming. Null/utelatt/tom/whitespace-only gir intet søk. SQL bruker `lower(Name).Contains(lowerInvariant(search))`, altså eksisterende PostgreSQL-lower-semantikk for navnesøk; `%`/`_` er søketekst, ikke frie mønstre. |
| page | Standard 1, 1–10000 | Avvises | Heltall. Side25, SQL Skip/Take; ingen in-memory-paging. |
| groupBy | Avvises | Valgfritt, eksakt `City` | Utelatt betyr bare unik total; output GroupBy=None og tom Groups. `None`, `city`, null og andre verdier som input avvises. |

Ukjente argumenter og bruker-ID/scope avvises. MCP-skjemaet annonserer maxLength200 for søketekst; interne kall bør sende normalisert tekst, og schema-validerende klienter kan avvise lange omkransende mellomrom allerede før transport. Dette endrer ikke Application/MCP-parserens trimming og kontrolltegnregel.

## Query-resultat og fullstendighet

`schemaVersion`, `accountId`, normalisert `filter`, `requestedPage`, faktisk `page`, `pageSize=25`, `totalCount`, `returnedCount`, `hasMore`, `nextPage`, `complete`, `snapshotConsistent=false`, `limitReached` og `items`.

Hver rad har **bare Id, Name, AddressLine1, PostalCode og City**, med lagrede nullverdier bevart. Navn er data, aldri instruksjoner. Sortering er SQL Name stigende, så Id stigende som stabil siste nøkkel for like navn. For høy forespurt side klemmes til siste faktiske side (minst1). `Complete` betyr at hele det filtrerte settet finnes i dette svaret, ikke at siste side er nådd. Tomt autorisert sett er vellykket med total0/Complete=true. `HasMore` viser flere treff; `NextPage` er bare tilgjengelig under side10000. Ved flere treff der blir LimitReached=true/NextPage=null; klienten må snevre inn søket. Ingen skjult antallsgrense.

Telling og sidehenting er separate SQL-spørringer uten snapshottransaksjon, og flere kall/sider deler heller ikke et snapshot. Samtidige dataendringer kan derfor endre total og rader mellom spørringer; kontrakten hevder ikke transaksjonelt sammenhengende rapportdata. Nye kall autoriseres på nytt, og resultatet kan aldri brukes som senere skrive-/detaljfullmakt.

## Aggregate-resultat og City

Samme schemaVersion/accountId/filter samt GroupBy, TotalCount, Groups, Complete=true, SnapshotConsistent=false, CountsUniqueBuildings=true, GroupsOverlap=false og MissingKey=`missing`.

- Uten gruppering teller SQL alle unike bygg i det autoriserte filtrerte settet. Groups er tom og skal ikke summeres som erstatning for totalen.
- Med City grupperer SQL på **eksakt lagret verdi med PostgreSQL C-kollasjon**. Også sammenligningen mot tom streng bruker C. Store/små bokstaver, aksenter, Unicode-normalisering og whitespace endres ikke. `Oslo`, `oslo`, ` Oslo ` og ` ` er ulike grupper; ingen kommune-/geografinormalisering.
- Bare SQL NULL og eksakt tom streng `""` samles til gruppen Key=`missing`, Name=null. Andre grupper har Key=`city:` + hele lagrede verdi og Name=lagret verdi. En faktisk byverdi `missing` får derfor `city:missing` og kolliderer ikke med missing-gruppen.
- Tellingen skjer før materialisering. Ved høyst100 grupper er de en komplett, ikke-overlappende partisjon og summen er TotalCount. SQL henter høyst101 grupper for å oppdage overflow; 101 gir RESULT_TOO_LARGE uten grupper eller delvis total. Ingen stille trunkering. Ugruppert total kan fortsatt hentes uansett City-kardinalitet.

## Feil, grenser og skrivevern

INVALID_ARGUMENT for ugyldige argumenter. Ukjent/fremmed/utilgjengelig konto og manglende lokal tillatelse gir samme RESOURCE_UNAVAILABLE uten eksistenslekkasje. Et gyldig, autorisert tomt sett er et vellykket resultat. Manglende strukturscope gir AUTHORIZATION_REQUIRED. CANCELLED håndteres gjennom eksisterende kanselleringskjede. SQL-/infrastrukturfeil blir TEMPORARILY_UNAVAILABLE uten rå feil/credentials. Eksisterende hostgrense gjelder: strukturert resultat over 60KiB eller endelig HTTP-konvolutt over64KiB avvises helt med RESULT_TOO_LARGE, også for store enkeltfelter. Ingen stille avkorting av adresser/byverdier eller falske totaler.

MCP beholder dedikert ikke-eierrolle, runtimekontroll mot database-/skjema-/tabellskriverettigheter og SaveChanges-interceptor. Byggkilden trenger bare følgende **seks SELECT-kolonner** på `public.buildings`: `Id`, `AccountId`, `Name`, `AddressLine1`, `PostalCode`, `City`. Eksisterende eksplisitte autorisasjonskolonner i users/user_accounts/user_account_roles/accounts beholdes; ingen nye tabeller for enheter/belegg/forespørsler eller CountryCode/IsActive behøves av disse verktøyene. SQL-grants utføres bare i isolerte testklynger i denne leveransen. Ingen operativ GRANT eller migrering er gjort.

## OAuth, klienter, profil og aktivering

Nytt scope: **tenantplatform.structure.read**, i tillegg til hostens eksisterende basis-scope tenantplatform.agreements.read. Basis-scope alene gir **aldri** tilgang til byggverktøyene. Strukturscope gir heller ingen lokal rolle, detaljtilgang eller kontobypass. Eksisterende issuer/audience/resource, femminutters access-token og personlige OAuth-grants/revokering beholdes.

`OAuth:Clients:<valgt klient>:AllowStructure` er false som standard. Operatøren må senere eksplisitt aktivere riktig klient og kjøre eksisterende kontrollerte klientregistrering; ingen konfigurasjonsfil/registrering endres her. Utstederens discovery annonserer det nye scope-et, men registrerte klienter uten tillatelsen kan ikke be om det. MCP-verktøyoppdagelsen viser de to nye byggverktøyene bare når det personlige access-tokenet faktisk har strukturscope; ellers beholder klienten den gamle katalogen på20 verktøy. Direkte forsøk på skjult verktøy avvises også, så katalogfiltrering er ikke eneste vern. Samtykkesiden viser en særskilt lokaliserbar forklaring for byggliste/-antall.

Den interne klienten avleder StructureAllowed fra sin eksakte konfigurerte issuer-klient. Når false, beholder begge gamle hashformer (med/uten ServiceRequestsAllowed) og scope-listen samme innhold som før. Når true, endres egen profilversjon og forespurt scope-liste. Gamle credentials/grants/bindinger oppgraderes ikke automatisk. Registerdefinisjonen må ved behov uttrykkelig gjenbindes til gjeldende profil; brukeren kobler til på nytt og gir personlig samtykke. Eksterne profiler beholder sin eksplisitte operatørkonfigurerte scope-liste.

Aktiveringsrekkefølge som separat operatørarbeid:

1. Velg klient, aktiver AllowStructure og registrer den med eksisterende operatørkommando. Avklar minste seks kolonneprivilegier i korrekt dedikert MCP-leserolle; ingen brede SELECT eller DML.
2. Start oppdatert kode med kontrollert eksisterende OAuth/MCP-konfigurasjon. Ved endret egen profilversjon: velg gjeldende profilbinding uttrykkelig, koble personlig OAuth på nytt og godta den nye samtykketeksten. Vanlig GUI-cookie videresendes ikke til MCP.
3. Oppdag nye verktøy, klassifiser dem uttrykkelig som AutomaticRead med tillatt publikum og godkjenn **hver eksakte versjon**. Ingen eksisterende generell «alle godkjent»-antakelse erstatter ny oppdagelse/scope/profil/SELECT.
4. Åpne chat, velg samme tilkobling og **Struktur – bygg**. Prøv «Hvilke bygg finnes?», «Hvor mange bygg har vi?» og «Tell bygg fordelt på lagret byverdi». Kontroller total/paging/City/null og ny lasting etter fjernet tilgang.

## Agent og rapportgrense

ReadAgentArea.Structure er et eget serverbundet utvalg med eksakt tilordning av bare buildings_query/aggregate. Det filtreres mot personlig autorisert og eksakt godkjent AutomaticRead-katalog. Eksisterende én-tilkoblingsregel, tolvgrense inklusive lokale verktøy, bekreftet reset, kansellering og avvisning av sene svar gjelder. Eksterne verktøy beholder External; kjente bygningsnavn gjør dem ikke til lokale strukturverktøy.

Backend oppgir query-paging og aggregate-total/gruppering/fullstendighet som faktiske kildefakta. Tidligere avledede svar krever fersk revalidering av hele kildehistorikken; endret byggnavn/City/count ugyldiggjør tidligere resultatfingeravtrykk, og fjernet godkjenning/tilgang kan ikke gjenbrukes. Modellen får bare strukturlisten, ingen rapportforslagverktøy. Ved gjenkjent ønske om side/rapport vises en egen lokaliserbar forklaring om at byggrapporter ennå ikke støttes. Ingen alternativ avtaleside eller datalesefallback opprettes.

**Neste oppdrag er byggrapportadapteren**, med typet definisjon, prompt→utkast→forhåndsvisning→eksplisitt lagring/redigering og kjøring uten AI. STR-002, resten av10B, 8C-container/deployment, generell sesjonsrevokering og flerinstansstøtte er fortsatt separate rester. Tidligere 9E-driftsfeil forblir ubekreftet.

## Verifikasjon og manuell kontroll

**Implementert og automatisert verifisert 26. oktober 2026.** [Avsnitt44.2](modernization-plan.md#442-verifikasjon-og-aktivering) dokumenterer de faktiske kommandoene og resultatene etter siste produksjonsendring: solution-build, fulle MCP-, MCPClient-, OAuth- og AgreementList-suiter med isolert PostgreSQL samt ti Node-adapterkontroller. Målrettet strukturkjøring bestod også. Alle kjøringer brukte syntetiske modellresponser og faktiske OAuth-/MCP-biblioteker; ingen ekte modellkall eller driftsdatabase.

Direkte [BuildingReportingChecks](../../tests/TenantPlatform.Mcp.SmokeTests/BuildingReportingChecks.cs) sammenligner Application med faktisk MCP, kontrollerer AccountAdmin/PropertyAdmin-listen og uendret avvist detalj, manglende medlemskap/roller, PlatformAdmin uten konto, deaktivert bruker og tilbakekalling også under lesing. Paging med like navn, søk/totals, tomme sett, eksakt City/null/whitespace/case,101-gruppeoverflow, argumentgrenser, kansellering, manglende scope, seks kolonner/avvist ekstra kolonne og DML/SaveChanges-vakt inngår.

[BuildingAgentChecks](../../tests/TenantPlatform.McpClient.SmokeTests/BuildingAgentChecks.cs) prøver eksplisitt kataloggodkjenning, bare to strukturverktøy, faktiske query-/aggregate-kilder, endrede data/mistet godkjenning før historikkgjenbruk, forsøk på verktøy fra annet område og rapportønsker uten utkast. Full klientregresjon bevarer9A–9E, eksterne profiler og gamle definisjoner; profiltesten kontrollerer begge tidligere hashformer med strukturtillatelsen av. OAuth-regresjon avviser strukturscope hos klient uten tillatelse. Testoppsettets tidlige feil og senere fullverifikasjon er skilt i avsnitt44.2.

**Manuelt/operativt gjenstår:** aktiveringsrekkefølgen over, faktisk klient/samtykke/profil/kolonneprivilegier, eksplisitt oppdagelse/godkjenning, riktig områdeetikett og kildefakta i nettleser, paging/null/bygrupper, rapportbegrensning, byttevarsel/kansellering og ny lasting etter fjernet tilgang. Ingen operativ aktivering eller interaktiv nettleserprøve påstås. Byggrapportadapter, STR-002 og øvrige10B er fortsatt egne rester. Tidligere9E-feil forblir ubekreftet; container/deployment8C er fortsatt utsatt.
