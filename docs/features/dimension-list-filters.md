# Dimensjonsfiltre i Avtaler- og Leasing-lister

Dato: 2026-11-12

## Omfang

Det er lagt til felles dimensjonsfiltre i:

- `/agreements`
- `/leasing/frameworks`
- `/leasing`

Leasing filtrerer bare avtalenivådimensjonene. Linjedimensjoner og kostnadsfordeling er ikke berørt. Det er ingen schema- eller migrasjonsendringer.

## Kontrakter og presentasjon

`DimensionListFilterSelection` bruker stabil `DimensionId`, én `ValueId` og `IncludeUnspecified`. `AgreementFilter` og `LeasingListFilter` inkluderer denne listen. Side-resultatene returnerer også en konto-, modul- og tilgangsavgrenset `DimensionListFilterCatalog`.

`DimensionListFilterFields` er den felles Razor-presentasjonen. Den viser én vanlig dropdown per dimensjon med det lokaliserte dimensjonsnavnet direkte som feltetikett, uten en egen gruppeoverskrift eller innledende hjelpetekst. Dropdownen har «Alle», «Ikke angitt», hierarkiske verdistier og historikkmarkering. Røtter, mellomnivåer og blader er valgbare uavhengig av dimensjonens `AllowMultiple` og `LeafOnly`, fordi dette er søkekriterier og ikke tildelinger.

Den tidligere implementasjonen lagret ikke dimensjonsfiltre i URL, så det finnes ingen varig URL-tilstand å migrere. For kompatibilitet med en eldre klientkontrakt godtar tjenestelaget likevel `ValueIds`: flere innsendte verdier normaliseres til første verdi i serverkatalogens synlige rekkefølge. «Ikke angitt» har prioritet hvis gammel input også inneholder en verdi. Dermed kan UI og query aldri representere ulike aktive valg.

Søk nullstiller siden til 1. Nullstill fjerner både eksisterende listefiltre og dimensjonsfiltre. Leasing tømmer dimensjonsvalg ved bytte av listetype, slik at et historisk valg fra én ressurstype ikke sendes mot en annen katalog. Pågående listekall avbrytes, og sene svar ignoreres.

## Semantikk

- Ulike dimensjoner kombineres med AND.
- Eksisterende tekst-, status-, type- og eierfiltre kombineres med dimensjonsfiltrene med AND.
- En foreldrenode utvides én gang til seg selv og alle etterkommere.
- Et filter på et barn matcher ikke en eksplisitt tildeling bare til forelderen.
- «Ikke angitt» betyr at ressursen ikke har noen tildeling i dimensjonen.
- Historiske/inaktive, faktisk brukte valg kan filtreres og regnes aldri som «Ikke angitt».

## Leasing-arv

Rammeavtaler bruker `leasing_framework_dimension_assignments`. Anskaffelser bruker `leasing_acquisition_dimension_assignments`, som allerede er det effektive snapshotet for både frittstående og arvede anskaffelser. Filteret introduserer derfor ingen ny arvetolkning: endringer som eksisterende domenelogikk kopierer fra rammeavtalen, blir de samme verdiene som anskaffelseslisten filtrerer på.

## Querystruktur

Begge tjenestene starter med den eksisterende autoriserte og kontoavgrensede grunnspørringen. Historiske katalogvalg hentes bare fra tildelinger på ressurser i denne synlige mengden. Modulens aktive regler utgjør normalkatalogen.

Hvert dimensjonskriterium legges på grunnspørringen som et korrelert `Any`/`EXISTS`-vilkår. Påfølgende dimensjoner gir AND uten joins i resultatraden. Dermed kommer hver ressurs høyst én gang. Dimensjonsfiltrene anvendes før `Count`, sortering, `Skip` og `Take`. Katalog og etterkommerutvidelse bygges samlet; det finnes ingen spørring per resultat eller hierarkinivå.

Innsendte dimensjoner og verdier valideres mot den aktuelle servergenererte katalogen, inkludert dimensjonstilhørighet. Andre kontoers data inngår ikke i katalog eller tildelingssubqueries.

## Endrede filer

- `src/TenantPlatform.Web/Services/Dimensions/DimensionListFilters.cs`
- `src/TenantPlatform.Web/Components/Shared/DimensionListFilterFields.razor`
- `src/TenantPlatform.Web/Services/Agreements/AgreementDtos.cs`
- `src/TenantPlatform.Web/Services/Agreements/AgreementService.cs`
- `src/TenantPlatform.Web/Services/Agreements/AgreementService.Dimensions.cs`
- `src/TenantPlatform.Web/Components/Pages/Agreements/Agreements.razor`
- `src/TenantPlatform.Web/Services/Leasing/LeasingService.cs`
- `src/TenantPlatform.Web/Services/Leasing/LeasingService.AgreementDimensions.cs`
- `src/TenantPlatform.Web/Components/Pages/Leasing/LeasingList.razor`
- `src/TenantPlatform.Web/Components/Pages/Leasing/_Imports.razor`
- `src/TenantPlatform.Web/Resources/TenantPlatformResources.nb-NO.resx`
- `src/TenantPlatform.Web/Resources/TenantPlatformResources.en-GB.resx`
- `src/TenantPlatform.Web/Resources/TenantPlatformResources.sv-SE.resx`
- `tests/TenantPlatform.Dimensions.SmokeTests/DimensionListFilterChecks.cs`
- `tests/TenantPlatform.Dimensions.SmokeTests/ComponentChecks.cs`
- `tests/TenantPlatform.Dimensions.SmokeTests/Program.cs`

## Verifikasjon

Kommandoer:

```text
dotnet build tests/TenantPlatform.Dimensions.SmokeTests/TenantPlatform.Dimensions.SmokeTests.csproj --no-restore
dotnet run --project tests/TenantPlatform.Dimensions.SmokeTests/TenantPlatform.Dimensions.SmokeTests.csproj --no-build
dotnet build tests/TenantPlatform.Leasing.SmokeTests/TenantPlatform.Leasing.SmokeTests.csproj --no-restore
dotnet build TenantPlatform.sln --no-restore
```

Resultater:

- Dimensions smoke checks: PASS, 228 kontroller, uten databaseforbindelse, SQL, migrasjonsanvendelse eller initialisering.
- Målrettede kontroller dekker alle tre ressurstypene, én dropdown per dimensjon, Alle/konkret/Ikke angitt, AND, eksisterende filtre, røtter/mellomnivåer/blader ved `LeafOnly`, foreldre/etterkommere, foreldretildeling mot barnefilter, historiske valg, arv-snapshot, kontrollert normalisering av gammel flerverdi-input, duplikater, total/paginering, ugyldige ID-er, nullstilling og lokalisert rendering i nb-NO, en-GB og sv-SE.
- Faktiske tjenestequeries er kjørt med EF Core InMemory for kontroll av queryplassering, total og paginering. Dette er ikke PostgreSQL-verifikasjon.
- Leasing smoke-testprosjektet bygger. Det PostgreSQL-bundne programmet ble ikke kjørt.
- Løsningsbygg: PASS.

## Ikke utført

- PostgreSQL-eksekvering og kontroll av generert queryplan/ytelse.
- Visuell browserkontroll, tastaturkontroll og responsiv kontroll av filterområdet.
- Belastningsmåling med store dimensjonshierarkier eller tildelingsmengder.

## Manuell sjekkliste

1. På `/agreements`: kombiner tekst/status/eier med to dimensjoner og kontroller total og sidebytte.
2. På `/leasing/frameworks`: kontroller egne rammeavtalevalg, historiske valg og «Ikke angitt».
3. På `/leasing`: kontroller én frittstående og én arvet anskaffelse mot samme filter.
4. Velg forelder og kontroller treff på barn; velg deretter barn og kontroller at en ren foreldretildeling ikke treffes.
5. Bytt mellom «Alle», «Ikke angitt» og én konkret verdi, nullstill, og kontroller at alle øvrige filtre også fjernes.
6. Bytt språk og kontroller at navn/tekster endres uten at valgte identiteter endres.

