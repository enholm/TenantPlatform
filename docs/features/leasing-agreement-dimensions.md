# Leasing-dimensjoner på avtalenivå

Dato: 2026-11-12

## Status og avgrensning

Kildeimplementasjonen er ferdig. Den legger til generiske dimensjoner på Leasing-rammeavtaler og anskaffelser uten å endre de eksisterende linjedimensjonene eller kostnadsfordelingen.

Ingen database ble åpnet, ingen migrasjon ble anvendt, ingen app eller demoinitialisering ble startet, og ingen PostgreSQL-bundet test ble kjørt i denne implementeringsrunden.

## Valgt arvesemantikk

Arven er et snapshot på anskaffelsen:

- En ny anskaffelse under en rammeavtale kopierer rammeavtalens dimensjonsvalg, inkludert fravær av valg.
- Ved bytte til en annen rammeavtale erstattes snapshotet med valgene fra den nye rammeavtalen.
- Ved frakobling beholdes siste snapshot og blir redigerbart som en frittstående anskaffelse.
- En anskaffelse som fortsatt er koblet til en rammeavtale kan ikke sende egne dimensjonsendringer; dette håndheves i tjenestelaget.
- Når rammeavtalens dimensjoner faktisk endres, kopieres det nye snapshotet transaksjonelt til alle nåværende anskaffelser under rammeavtalen. Hver endret anskaffelse får ny revisjon og en historikkpost med handlingen `FrameworkDimensionsUpdated`.
- Urelaterte lagringer på rammeavtalen omskriver ikke identiske snapshots.

Dette gir rapporterbare verdier direkte på anskaffelsen, samtidig som nåværende tilknyttede anskaffelser følger senere endringer på rammeavtalen. Historiske Leasing-snapshots inneholder avtaledimensjonene før og etter endringen.

## Modell og persistens

Det er lagt til tre separate tabeller:

- `leasing_agreement_dimension_rules` for uavhengige regler på avtalenivå.
- `leasing_framework_dimension_assignments` for rammeavtaler.
- `leasing_acquisition_dimension_assignments` for anskaffelser.

Tildelingene bruker stabile ID-er, account-aware fremmednøkler til eier og `dimension_values`, `Restrict`-sletting og unike indekser per eier/dimensjon/verdi. Regelens databaseconstraint håndhever at obligatorisk innebærer tilgjengelig. Nye regler er deaktivert og valgfrie som standard.

Migrasjonen `20261112192241_AddLeasingAgreementDimensions` er additiv: den oppretter bare de tre nye tabellene og indeksene. Eksisterende Leasing-linjetildelinger, fordelingsdata og øvrige data berøres ikke. Design-time-fabrikkens offline-sperre er uendret.

## Tjenestekontrakter og validering

Leasing-tjenesten tilbyr lokaliserte avtaledimensjonsvalg og egne administrasjonsoperasjoner for avtalenivåreglene. Lagring skiller mellom utelatt input, eksplisitt tømming og reell endring.

Servervalideringen dekker konto og tilgang, dimensjons-/verditilhørighet, tilgjengelighet, obligatoriske valg ved nyregistrering, single/multiple og `LeafOnly`. Eksisterende historiske valg kan beholdes selv om de senere blir inaktive eller utilgjengelige. En ny obligatorisk regel invaliderer ikke en eldre avtale ved en urelatert lagring.

Eksisterende revisjonskontroll, kontolås og transaksjon brukes. Rammeavtalekoblingen slås opp innen riktig konto og eksisterende Leasing-tilgangsregler før snapshot kopieres.

## UI

Administrasjonssiden skiller nå tydelig mellom:

- «På leasingavtalen»: tilgjengelig og obligatorisk.
- «På avtalelinjene»: de eksisterende reglene for tilgjengelig, obligatorisk og kostnadsfordeling.

Rammeavtaler og frittstående anskaffelser kan redigere avtaledimensjoner. Presentasjonen følger Avtaler-visningen med overskriften «Dimensjoner», samme gruppering, typografi, hjelpe- og required-tekster, single-/multiplekontroller, hierarkiske stier og historiske valg. En anskaffelse under rammeavtale viser den samme strukturen skrivebeskyttet med forklaring om at rammeavtalen styrer valgene og en separat, lesbar liste over valgte verdier. Ved frakobling beholdes de viste verdiene og blir redigerbare. UI-state bevarer innskrevne valg ved valideringsfeil; språkbytte endrer ikke identiteter eller snapshots. Tekster er tilgjengelige for nb-NO, en-GB og sv-SE.

## Endrede filer

Kjernemodell og persistens:

- `src/TenantPlatform.Core/Leasing/LeasingDimensions.cs`
- `src/TenantPlatform.Core/Leasing/LeasingModels.cs`
- `src/TenantPlatform.Infrastructure/Persistence/TenantPlatformDbContext.cs`
- `src/TenantPlatform.Infrastructure/Persistence/Configurations/DimensionConfiguration.cs`
- `src/TenantPlatform.Infrastructure/Persistence/Migrations/20261112192241_AddLeasingAgreementDimensions.cs`
- `src/TenantPlatform.Infrastructure/Persistence/Migrations/20261112192241_AddLeasingAgreementDimensions.Designer.cs`
- `src/TenantPlatform.Infrastructure/Persistence/Migrations/TenantPlatformDbContextModelSnapshot.cs`

Tjenester og UI:

- `src/TenantPlatform.Web/Services/Leasing/LeasingService.cs`
- `src/TenantPlatform.Web/Services/Leasing/LeasingService.Classification.cs`
- `src/TenantPlatform.Web/Services/Leasing/LeasingService.AgreementDimensions.cs`
- `src/TenantPlatform.Web/Services/Accounts/AccountService.cs`
- `src/TenantPlatform.Web/Components/Pages/Dimensions/DimensionRulesSection.razor`
- `src/TenantPlatform.Web/Components/Pages/Dimensions/DimensionsPage.razor`
- `src/TenantPlatform.Web/Components/Pages/Leasing/LeasingAgreementDimensionEditState.cs`
- `src/TenantPlatform.Web/Components/Pages/Leasing/LeasingAgreementDimensionEditor.razor`
- `src/TenantPlatform.Web/Components/Pages/Leasing/LeasingAgreementDimensionsView.razor`
- `src/TenantPlatform.Web/Components/Pages/Leasing/LeasingEditor.razor`
- `src/TenantPlatform.Web/Components/Pages/Leasing/LeasingDetailsPage.razor`
- `src/TenantPlatform.Web/Resources/TenantPlatformResources.nb-NO.resx`
- `src/TenantPlatform.Web/Resources/TenantPlatformResources.en-GB.resx`
- `src/TenantPlatform.Web/Resources/TenantPlatformResources.sv-SE.resx`

Databasefrie kontroller:

- `tests/TenantPlatform.Dimensions.SmokeTests/LeasingAgreementDimensionChecks.cs`
- `tests/TenantPlatform.Dimensions.SmokeTests/AccountDeletionChecks.cs`
- `tests/TenantPlatform.Dimensions.SmokeTests/ComponentChecks.cs`
- `tests/TenantPlatform.Dimensions.SmokeTests/FinalModelChecks.cs`
- `tests/TenantPlatform.Dimensions.SmokeTests/Program.cs`

## Utført verifikasjon

Kommandoer:

```text
dotnet build tests/TenantPlatform.Dimensions.SmokeTests/TenantPlatform.Dimensions.SmokeTests.csproj --no-restore
dotnet run --project tests/TenantPlatform.Dimensions.SmokeTests/TenantPlatform.Dimensions.SmokeTests.csproj --no-build
dotnet build tests/TenantPlatform.Leasing.SmokeTests/TenantPlatform.Leasing.SmokeTests.csproj --no-restore
dotnet build TenantPlatform.sln --no-restore
```

Resultater:

- Dimensions smoke checks: PASS, 205 kontroller, uten databaseforbindelse, SQL, migrasjonsanvendelse eller initialisering. Dette inkluderer målrettet rendering-sammenligning mellom Avtaler og Leasing i nb-NO, en-GB og sv-SE; det er ikke en visuell browsertest.
- Leasing smoke-prosjektet: bygg PASS. Programmet ble ikke kjørt fordi det er PostgreSQL-bundet.
- Løsningsbygg: PASS.
- Eksisterende pakkevarsler NU1902 og NU1903 for `System.Security.Cryptography.Pkcs` 10.0.0 gjenstår; ingen kompileringsfeil.

De databasefrie kontrollene dekker uavhengige regler, required/available-constraint, single/multiple, `LeafOnly`, historiske valg, ugyldige/utenfor-konto-identiteter, snapshot ved koblingsendring, serveravvisning av overstyring, automatisk propagasjon med revisjon/historikk, kontosikre FK-er, additiv migrasjon, lokalisert adminpresentasjon og uendrede linje-/fordelingsberegninger.

## Ikke utført

- Anvendelse og inspeksjon av den nye migrasjonen i PostgreSQL.
- PostgreSQL-bundne Leasing-regresjonstester.
- Browserkontroll av administrasjon, editorer, detaljvisning, responsivitet og tastatur/fokus.
- Integrert flerbruker-/concurrency- og rollback-kjøring mot PostgreSQL.
- Kontroll av faktisk rapport-/eksportforbruk utenfor de eksisterende Leasing-snapshotene.

## Manuell sjekkliste

1. Administrasjon: kontroller de to Leasing-seksjonene, koblingen obligatorisk/tilgjengelig og at linjenes fordelingsregel er uendret.
2. Rammeavtale: opprett/rediger single-, multiple- og leaf-only-valg; kontroller obligatorisk validering, lokalisering og detaljvisning.
3. Frittstående anskaffelse: lagre, gjenåpne, tøm eksplisitt og kontroller at en urelatert lagring bevarer historiske valg.
4. Anskaffelse under rammeavtale: kontroller skrivebeskyttet forklaring og identiske snapshots; endre rammeavtalen og kontroller automatisk oppdatering.
5. Bytt rammeavtale og kontroller nytt snapshot. Koble deretter fra og kontroller at siste snapshot beholdes og kan redigeres.
6. Fremprovoser valideringsfeil og revisjonskonflikt og kontroller at brukerinput bevares.
7. Kontroller nb-NO, en-GB og sv-SE samt små vinduer og tastaturnavigasjon.

