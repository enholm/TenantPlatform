# Steg 8D: avtalelesing for agent og rapportkonsumenter

De tre eksisterende verktøyene `accounts_list`, `agreements_list` og `agreements_attention` beholder kontraktversjon `1` og eksisterende parsing. Tre nye verktøynavn har hver sin eksplisitte kontrakt med `schemaVersion="1"`. En ny konsument skal bruke `agreements_query` for dimensjoner og filtrert rapportgrunnlag. Eldre `agreements_list` får ingen nye felter eller argumenter.

## Verktøy

| Navn | Argumenter utover påkrevd accountId | Resultat |
|---|---|---|
| `agreements_query` | Felles filtre nedenfor og `page` (1–10000, standard 1) | Filtrert total, sideinformasjon og inntil 25 avtaler med dimensjoner. |
| `agreements_aggregate` | Samme filtre og påkrevd `groupBy`: `Building` eller `Status` | Databasegruppering av alle autoriserte treff, unik total og komplette grupper. |
| `agreements_details` | Påkrevd `agreementId` | Én eksplisitt avgrenset avtalesammendrag, uten initialisering. |

Alle ID-er er ikke-tomme UUID-er. Ukjente argumenter, eksplisitt null, feil typer og ukjente enumverdier avvises. Ukjent/fremmed konto eller ukjent/utilgjengelig avtale gir samme `RESOURCE_UNAVAILABLE`. En autorisert konto uten treff gir vellykket tomt resultat. Overskridelse av grupper/resultat gir `RESULT_TOO_LARGE`; øvrig eksisterende feil- og transportmapping beholdes.

## Felt og domenesemantikk

Liste og detalj returnerer `id`, `title`, `type`, `status`, `startDate`, `endDate`, `noticeDeadline`, `renewalDate`, `isArchived` og nullable referanser `counterparty`, `building`, `unit`, `organizationElement`, `geographicArea`. Hver referanse er enten `{id,name}` eller null. Etikettene er direkte navn, ikke hierarkistier. Ingen av feltene med eier, brukeridentitet, grants, interne revisjoner, dokumentmetadata/-innhold/-adresser, fritekstvilkår eller økonomi inngår. `complete=true` for detalj betyr komplett innenfor dette sammendraget, ikke alle GUI-detaljer.

Nåværende `Agreement` har én nullable FK per bygg, enhet, organisasjonsenhet og geografisk område. Ingen flerverdirelasjon flates ut. En avtale kan ikke modelleres som knyttet til flere bygg i dette utvalget. Dette må endres eksplisitt i domene/kontrakt hvis slik støtte senere innføres.

`Active` er lagret `AgreementStatus.Active`. Den er ikke avledet fra datoer, arkiv eller frister. Utløpsutvalget bruker lagret `EndDate`, ikke `RenewalDate`, beregnede oppsigelses-/opphørsdatoer eller fristforekomster. Det er derfor ikke et generelt svar på alle typer avtalers rettslige opphør. Arkiv er uavhengig av status; velg eksplisitt `archive=Exclude` ved spørsmål om ikke-arkiverte avtaler.

Alle metadataoppslag er avgrenset til samme konto som den autoriserte avtalen, etter samme prinsipp som eksisterende detaljreader. Manglende eller fremmed referanse blir null, inklusive ID; avtaleraden beholdes. Metadata hentes bare for utvalgte rader; grupper viser bare bygg brukt av synlige avtaler. Navn på andre avtaler, medlemmer eller byggkataloger returneres ikke.

## Felles filtre

Filtrene kombineres med AND; utelatte filtre begrenser ikke utvalget. Ingen klientvalgt innringer eller forhåndsautorisert scope.

| Argument | Betydning |
|---|---|
| `search` | Inntil 200 Unicode-tegn, uten kontrolltegn. Trimmet, søk uten hensyn til store/små bokstaver i tittel og kontobegrenset motpartsnavn. |
| `status` | Eksakt `Draft`, `Active`, `Terminated` eller `Expired`. |
| `type` | Eksakt `Lease`, `SoftwareLicense`, `ServiceMaintenance`, `Supplier` eller `Other`. |
| `archive` | `Include` (standard), `Exclude` eller `Only`. |
| `buildingId`, `unitId`, `organizationElementId`, `geographicAreaId` | Eksakt referanse i aktuell konto. Ingen implisitt inkludering av etterkommere. Fremmed/ukjent ID gir null treff, ikke fremmed metadata. |
| `missingBuilding` | Standard false. True inkluderer både null og ugyldig/fremmed byggtilknytning. Kan ikke kombineres med `buildingId`. |
| `endDateFrom`, `endDateTo` | ISO `yyyy-MM-dd`, begge inklusive. Null EndDate ekskluderes når en datogrense finnes. Fra må være før eller lik til. |
| `endsWithinMonths` | 1–12 kalendermåneder. Fra kontodato til og med kontodato.AddMonths(n). Kan ikke kombineres med eksplisitte datogrenser. |

Application tar ett UTC-tidspunkt før første await og bruker lagret tidssone fra reminderinnstillingene, ellers Europe/Oslo. `selection.today` og normaliserte `selection.filter` følger svaret; relative filtre har konkrete fra-/til-datoer. Ingen settings opprettes. Enumverdier i filterresultatet bruker de samme strengene som inngangsskjemaet. Etter normalisering er endsWithinMonths null; bruk de konkrete datogrensene ved senere gruppering eller deterministisk rapportering, uten å sende nullfelter som argumenter.

For «utløper de neste tre månedene» brukes `endsWithinMonths=3`, og ønsket status/arkiv velges i tillegg. «Hvor mange aktive avtaler per bygg?» bruker `status=Active`, `groupBy=Building` og et eksplisitt arkivvalg. «Grupper dem etter bygg» skal gjenbruke forrige filters konkrete normaliserte datogrenser og andre filtre; ikke gruppere bare den viste siden.

## Aggregater og fullstendighet

Eksisterende AgreementInsightsService har ikke det nødvendige felles dato-/arkiv-/dimensjonsfilteret. 8D bruker derfor en målrettet rapportport fremfor å fremstille ufiltrert innsikt som et filtrert resultat; felles AgreementReadQueries.Visible og eksisterende evaluator gjenbrukes.

Application innhenter ferskt avtaleomfang gjennom felles evaluator. Infrastructure bruker samme autoriserte SQL-utvalg og samme filterfunksjon for liste og aggregat. Gruppering og telling skjer i PostgreSQL før materialisering, uten å hente listesider til agenten.

Begge grupperinger teller unike avtaler. Med dagens enkeltverdifelt tilhører hver avtale én gruppe; `countsUniqueAgreements=true` og `groupsOverlap=false`. Null eller utenlandsk bygg havner i `key="missing", name=null`. Bygggrupper bruker lokal bygg-ID som key og lokalt navn. Statusgrupper bruker enumstrengen som key/name. Grupper uten treff returneres ikke.

Maksimum er 100 grupper. SQL henter høyst 101 grupper for å oppdage overskridelse; hele kallet feiler ved overskridelse. Totalen beregnes fra den komplette, ikke-overlappende SQL-grupperingen. Ingen avkortet gruppeliste presenteres som komplett. En senere flerverdimodell vil kreve ny overlapps-/totalstrategi.

Lister sorteres etter Title/Id, har sider på 25 og synlig sideklamping. `totalCount`, `returnedCount`, `requestedPage`, `page`, `hasMore`, `nextPage`, `complete`, `limitReached` og `snapshotConsistent=false` følger etablert listepraksis. Ved sidegrensen 10000 og flere treff er nextPage null mens hasMore/limitReached er true; snevr inn filtrene. Alle resultater er underlagt eksisterende 60 KiB verktøy-/64 KiB envelopegrenser. Ingen konsistent snapshot loves mellom telling, liste og senere kall.

## Detalj, klient og samtalehistorikk

`IAgreementDetailsReader.ReadSummaryAsync` er en smal utvidelse av eksisterende detaljreader. Den bruker `GetAgreementReadScopeAsync` og en målrettet SQL-projeksjon. GUI-metoden `ReadAsync` og dens eksisterende kontrakt beholdes. MCP kaller aldri GUI-fasader, initialisering, dokumentlager eller writes.

Agentens egenprofil tillater nå de fem avtaleverktøyene, men fortsatt ikke accounts_list. Hvert verktøy krever tilgjengelig profil, personlig OAuth, eksplisitt tilgang, aktuell katalog, godkjent metadataversjon og AutomaticRead. Ny oppdagelse godkjenner ingen nye verktøy automatisk. Eksisterende kontobinding, skjemavalidering og én forbindelse per samtale beholdes. Nye inputskjemaer bruker kun validatorens eksisterende støttede vokabular.

Listekilder har de eksisterende sidefaktaene. Aggregatkilder har egne fakta om gruppering, unik total, antall grupper, fullstendighet, overlapp og snapshot; disse vises lokalisert. Detaljkilder har vanlig kildeidentitet og versjon. Begge nye kildetyper inngår i 8Bs transitive avhengigheter, fingeravtrykk og ferske gjenhenting. Endret resultat eller fjernet ressurstilgang ugyldiggjør avledet historikk. Ingen direkte datalesefallback i agenten.

## SELECT-rolle og aktivering

MCP trenger nå SELECT på nøyaktig tolv tabeller:

`accounts`, `users`, `user_accounts`, `user_account_roles`, `agreements`, `agreement_access`, `organizations`, `agreement_reminder_settings`, **`buildings`, `units`, `organization_elements`, `geographic_areas`**.

De fire siste er nye. Ingen tilgang til OAuth-lager, login_accounts, dokumenter, audit, finans eller fristtabeller er nødvendig. SELECT-rollen, oppstartskontrollen og SaveChanges-vernet beholdes; ingen nye schemaer, tabeller eller migreringer innføres. Denne implementeringen endrer ingen lokale/driftsmessige databaseprivilegier.

Ved senere kontrollert aktivering må operatør gi de fire målrettede SELECT-rettighetene før de nye verktøyene brukes. Eksempel for den lokale leserollen (dokumentasjon, ikke utført her):

```sql
GRANT SELECT ON TABLE
  public.buildings, public.units,
  public.organization_elements, public.geographic_areas
TO tenantplatform_mcp_reader;
```

Kjør med riktig operatøridentitet i riktig database; bruk faktisk rollenavn i andre miljøer. Oppdater katalogen med «Oppdag verktøy». Administrator velger tilgang/AutomaticRead og godkjenner hver ny versjon eksplisitt. Eksisterende legacyklient fortsetter med sine tre tidligere verktøy og DTO-er.

Containerbygg/deployment i 8C er fortsatt utsatt. Brukeren opplyser at reell modellprøve, interaktiv GUI og proxy/TLS ble prøvd før 8D; dette dokumenterer ikke de nye verktøyene eller en full GUI-/driftsmatrise. Ingen reelle modellkall, utviklings-/produksjonsdatabase eller deploy brukes i 8D-verifikasjonen.

## Før deterministisk rapportbygging

Dette grunnlaget støtter datofiltrerte avtalespørsmål, gruppering etter bygg/status, unike autoriserte tellinger og avgrensede detaljer. En senere rapportkonsument kan kalle MCP med faste enumverdier, eksplisitte datoer og samme filter, uten modellgenererte SQL-uttrykk. Rapportdefinisjoner, lagring/kjøring, eksport, flere grupperingsdimensjoner, økonomiske nøkkeltall og konsistente snapshots er ikke implementert i 8D. Flere bygg per avtale eller rekursive hierarkiutvalg krever en egen domene-/kontraktendring og nye overlappsregler.

## Faktisk verifikasjon 19. oktober 2026

Etter siste produksjonsendring bestod solution-build og de sju berørte suitene McpClient, Mcp, OAuth, AgreementList, Agreements, AccountRegisters og Dashboard. De seks første brukte isolert PostgreSQL; Dashboard brukte eksisterende InMemory-/SQL-/komponentkontroller. Direkte Application-prøver brukte SELECT-rollen og SaveChanges-vakten. Faktisk OAuth-/MCP-transport, metadata/filter-/grupperingsregler, nye detaljer, tilbakekalling, skrivevern og agentens oppfølgingshistorikk er prøvd med syntetisk modelladapter. Cookie-/antiforgery-beskyttet agent-HTTP returnerte lokaliserte aggregatfakta. Node-syntaks/DOM-kontroll og diff-/ressurskontroll bestod. Se moderniseringsplanens avsnitt 30 for kommandoer, avgrensning og manuelle rester.
