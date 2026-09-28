# Leasing fase 6

## Faktisk grunnlag og avgrensning

Fase 1–5 har økonomiske registreringsstatuser, rammekontroll, bestillingsreservasjoner, dimensjons-/fordelingsrader, versjonerte betalingsplaner og allokeringer mellom leasingfakturaer og stabile terminer. Fase 2 bevarer navn/koder/stier i klassifiseringen og fører endringshistorikk; den har ikke en generell, datoavhengig rapportmotor for historiske dimensjonssaldoer. Kreditnotaer har varelinjekoblinger, men ikke en dokumentert fordeling på de opprinnelige fordelingsradene.

Eksisterende avtaleoppfølging og varslingskø er bundet til `AgreementId`. Serviceforespørslenes e-postkø krever `ServiceRequestId`. Ingen av dem kan brukes til leasingobjekter uten feil koblinger. Det finnes ingen generell oppgave- eller rapportjobbmodell. Leasing bruker derfor egne oppfølgingspunkter og leveringsrader, men gjenbruker avtalenes kalender-/tidssoneregler, samme EF-/bakgrunnsarbeidermønster, dokumentlagring, rettighetsmodell og økonomiske beregninger. Ingen ekstern tjeneste eller ny e-posttransport er innført.

## Bruk og rettigheter

Menyen **Leasing** har dashboard i eksisterende oversikt samt **Oppfølging**, **Utstyr** og **Rapporter**. Fra en anskaffelse åpnes livsløpsvisningen, eksisterende dokumenter og betalingsplanvisningen.

Ansvarlig for anskaffelsen/rammeavtalen kan registrere frister, foreslå livsløpshendelser og behandle oppfølgingspunkter innenfor sin tilgang. Kontoen kan tildele separate roller:

| Rolle | Tilgang |
| --- | --- |
| LeasingEquipmentManager | Vedlikeholde utstyr og markere tellbare varelinjer |
| LeasingLifecycleManager | Vedlikeholde livsløpsvilkår og sende hendelsesforslag |
| LeasingLifecycleApprover | Godkjenne/avvise/reversere hendelser og kontrollere avslutning |
| LeasingReportReader | Kontoens leasingrapporter og lesetilgang til grunnlaget |
| LeasingReportExporter | Kontoens rapportvisning og eksport |

Kontoadministrator har alle disse rettighetene. Vanlig ansvarlig kan lese rapportgrunnlag for egne objekter, men trenger eksportrettighet for nedlasting. Konto-ID, ressurser, dokumenter, motparter, plasseringer og mottakere kontrolleres på serveren. Plattformadministrator uten kontomedlemskap får ikke en alternativ tenantomvei.

## Livsløp og avslutning

Økonomisk `Registered` beholdes ved ordinær eller førtidig avslutning. Livsløpsstatus lagres separat: aktiv, under avslutning, forlenget eller avsluttet. Passerte datoer endrer ikke status automatisk. Anskaffelsens økonomiske sluttdato bevares; livsløpsregisteret holder opprinnelig og senere avtalt sluttdato. Vilkårsendringer, forslag og beslutninger har aktør, tidspunkt, begrunnelse og før-/ettergrunnlag.

Oppsigelse kan være uavklart, en eksplisitt dato, kalenderdager eller kalendermåneder før avtalt slutt. Månedsslutt bevares; ellers klemmes dagen til siste dag i målmåneden. 31. mars 2028 minus én måned blir 29. februar. Ingen bank-/helligdagsjustering. Automatisk forlengelse registreres som avtaleopplysning og skaper oppfølging, aldri nye avtaler eller økonomiske poster av seg selv.

Retur, utskifting, utkjøp, tap/skade, forlengelse og avslutning går gjennom forslag og separat godkjenningsrettighet. Godkjenning skjer atomisk under kontolås. Forlengelse krever gammel/ny dato, virkningsdato, videreførte/nye vilkår og dokumentasjon. Den markerer betalingsplanen for kontroll. Når betalingene er endret, kreves en annen aktiv planversjon før plankontroll kan bekreftes. Ingen terminbeløp beregnes.

Avslutningskontrollen viser åpne fysiske forhold, oppgaver, planmangler, gjenstående terminer og uavklarte fakturaforhold. Uallokerte dokumenter for samme finansieringsselskap/valuta vises som mulige tilhørende dokumenter, ikke som beviste anskaffelseskoblinger. Avslutning med åpne forhold krever uttrykkelig vurdering og dokumentasjon. Førtidig avslutning markerer betalingsplanen for en dokumentert revisjon; fremtidige terminer slettes aldri. Sluttfakturaer og kreditnotaer kan fortsatt behandles. Dokumentkontroll er separat fra avslutningsstatus og bekrefter aldri bankbetaling eller restgjeld.

Sporbar reversering gjenoppretter siste godkjente livsløpshendelse når ingen nyere tilstandsendring hindrer dette. Hendelsen og disposisjonsradene beholdes. Ved nyere endringer må disse avklares først; systemet overskriver ikke ny historikk automatisk.

Rammeavtalens `Closed` betyr stengt for nye anskaffelser. `Finished` krever avsluttede underliggende leasingforhold med ferdig dokumentkontroll, ingen restreservasjon og ingen åpne rammeoppgaver. Anskaffelsesperiodens slutt frigir ingen reservasjoner og avslutter ingen underliggende leasinger. Historisk brukt ramme beholdes ved normal livsløpsavslutning.

## Utstyr

Enhetsregistrering er valgfri. Før registrering markeres varelinjen som tellbart fysisk utstyr; tjenester og beløpslinjer trenger ikke dette. Enheten har stabil ID, varelinje/anskaffelse, valgfritt serienummer og intern-ID, beskrivelse, nåværende bygg, ansvarlig, datoer, dokumentreferanse og notater.

Intern-ID normaliseres til store bokstaver og er unik innenfor konto. Mulige serienummerduplikater krever uttrykkelig kontroll, men er tillatt etter bekreftelse. Masseregistrering bruker én linje per enhet med `serienummer;intern-ID`, eller antall uten serienummer. Ingen serienumre konstrueres.

Registrerte enheter og behandling av uregistrert antall deler samme antallsgrense. Enheter som finnes i registeret må velges eksplisitt ved hendelser. Ny registrering kan ikke bruke antall som allerede er behandlet uten enhetskobling. Varelinjen kan ikke reduseres under dokumentert antall. Statusene returnert/erstattet/kjøpt ut/tapt eller skadet følger godkjente hendelser; vanlig vedlikehold kan bare velge i bruk eller planlagt retur.

Utskifting knytter til en eksisterende enhet på en økonomisk registrert anskaffelse. Et nytt kjøp må først gjennom vanlig anskaffelses- og rammekontroll. Faktisk utkjøpsbeløp registreres separat med dokumentasjon; restverdi brukes ikke som pris. Nåværende bygg/ansvarlig endrer aldri varelinjens økonomiske klassifisering.

## Oppfølging og varsling

Arbeidslisten har stabile koblinger for utløp, oppsigelse, retur, ubekreftet forlengelse, åpne reservasjoner, utstyrsbehandling, manglende ansvarlig, uavklarte vilkår, plankontroll og sluttkontroll. Samme jobbkjøring oppretter ikke nye kopier. Fullføring har kommentar, aktør og tidspunkt. Eksplisitt valgt oppgaveansvarlig bevares ved ny kjøring. Endrede frister gjør gamle forekomster foreldet og oppretter en ny datert forekomst.

Kontoadministrator aktiverer varsling under **Oppfølging → Varslingsoppsett**. Standard er **av**. Ved aktivering lagres tidspunktet, og varselstidspunkt før dette hoppes over. Eksisterende etterslep blir stående i arbeidslisten uten massevarsling. Deaktivering og ny aktivering oppretter en ny startgrense.

Varselterskler er konfigurerbare dager før anskaffelsesperiodens slutt, oppsigelse, leasingutløp og retur. Første tidssonevalg arves fra eksisterende avtaleinnstillinger eller `Europe/Oslo`. Leveringstid er kl. 09 lokalt. Eksisterende regler håndterer ugyldige/tvetydige DST-tidspunkt. Bakgrunnsarbeideren kontrollerer aktive kontoer hvert femte minutt, og arbeidslisten oppdaterer også grunnlaget ved åpning.

Ansvarlig mottar varsel i applikasjonen. Ekstra mottakere må velges eksplisitt og må ha aktivt medlemskap og objekttilgang ved levering. Mistet tilgang, endrede frister, avslutning, fullførte/foreldede oppgaver og deaktiverte terskler stopper aktuelle varsler. Planlagt tidspunkt, leverings-/lesetidspunkt, status og feilgrunn beholdes. Kontoadministrator ser de siste 200 leveringsradene. Ingen e-post eller kommunikasjon med leverandør/finansieringsselskap sendes.

## Rapportdefinisjoner

| Rapport | Grunnlag og beløp |
| --- | --- |
| Avtaleoversikt | Rammeavtaler og tilgjengelige anskaffelser; ansvarlig, finansieringsselskap, opprinnelig/avtalt slutt, frister og livsløp. Kjøpsverdier er eksplisitt merket. |
| Rammeutnyttelse | Samme kapasitet som rammekontroll: maks, benyttet, reservert og tilgjengelig. Summer holdes adskilt både på valuta og netto-/bruttogrunnlag. Ingen historiske saldoøyeblikksbilder rekonstrueres. |
| Anskaffelser og dimensjoner | `LeasingAllocationCalculator` brukes én gang per fordelingsrad. Felles/flervalgsklassifisering er merking og multipliserer ikke beløpet. Bevarte navn/stier brukes, ikke utstyrets nåværende plassering. Mangelfull klassifisering og krediteringer/reverseringer uten fordelingsgrunnlag vises separat. |
| Planlagte betalinger | Bare aktiv planversjon. Terminperiode, forfall, kildebeløp, finansieringsgrunnlag, versjon og vurderingsstatus. Manglende planer er egne rader; datofilter kan utelate disse udaterte radene, så dashboardet viser også en egen teller for manglende planer. |
| Fakturakontroll | Forventning fra aktiv plan og netto fakturert etter godkjente kreditnotaer/reverseringer. Samme statusberegning og toleranse som fase 5. Uallokert dokumentrest vises én gang per dokument uten konstruert anskaffelseskobling. |
| Utstyr og avslutning | Enheter, varelinjenes antall, nåværende plassering, serienummer, hendelser, berørte enheter, dokumentert utkjøpsbeløp og avslutning. Hendelsesrader er historikk og ikke ekstra kjøpsverdier. |

Periodefilteret angir uttrykkelig kjøpsdato, avtalt sluttdato, planlagt forfall, fakturadato, hendelsesdato, oppsigelsesfrist eller returfrist. Velg en relevant dato for rapporten; rader uten valgt dato kan ikke falle innenfor et datointervall. Ved fakturadatofilter summeres bare allokeringer fra dokumenter innenfor perioden; forventningen er fortsatt terminens fulle avtalegrunnlag. Finansieringsselskap på planrapporter følger terminens vilkårsgrunnlag; avtaleoversikten viser gjeldende datert finansieringsselskap når kjent. Utstyr bruker nåværende ansvarlig, økonomiske dimensjoner bruker bevart klassifisering.

Dashboardets tall åpner rapporter med samme filtre. Planperiodefilter gjelder forfall, mens rammeutnyttelse og samlet fakturakontroll viser registrert grunnlag på lesetidspunktet. Ingen valutakonvertering eller betalings-/gjeldsberegning foretas.

## Eksport, ytelse og begrensninger

CSV bruker UTF-8 med BOM, semikolon og siterte felter. Farlige tekstprefikser nøytraliseres med apostrof. XLSX bruker numeriske beløp og datoer med datoformat; tekst skrives som `inlineStr`, aldri som formel. Begge formater inkluderer genereringstidspunkt, aktive filtre, rapporttype, valuta og beløpsgrunnlag. XLSX har eget metadataark.

Listene er paginerte med 50 rader. Rapporter er beregnet direkte uten mellomlager og viser genereringstidspunkt. Rapportgrunnlaget er begrenset til 10 000 anskaffelser/rammer og 100 000 rader per uttrekk; for store uttrekk avvises med beskjed om å avgrense filtrene, uten stille trunkering. Det finnes ingen generell eksportjobbmekanisme i prosjektet, og denne fasen innfører ikke en ny slik plattform. XLSX-celletekst følger formatets grense på 32 767 tegn.

Begrensninger: ingen automatisk juridisk vurdering, avtaleinngåelse, oppsigelse, e-postutsending, leasingberegning, bankavstemming, regnskapsføring eller historisk saldorekonstruksjon. Ufordelte kjøpskrediteringer inngår ikke i en filtrert dimensjonsverdi uten dokumentert kobling. Det finnes ingen automatisk fordeling av leie/renter/gebyrer på dimensjoner. Livsløpshendelser er registrering og kontroll av dokumentert utfall; eksterne parter kontaktes ikke.

## Migrering og verifisering

`AddLeasingLifecycleAndReporting` oppretter separate tabeller for livsløp, utstyr, forslag/beslutninger, antallsdisposisjoner, oppfølging og varsler, samt et eksplisitt tellbar-felt på varelinjer. Ingen eksisterende økonomiske statuser, datoer, beløp eller dokumentkoblinger omskrives. Ingen enheter, serienumre eller avslutningshendelser genereres. Livsløpsgrunnlag leses fra kjente anskaffelsesdatoer ved behov. Migreringen aktiverer ingen varsler.

Migreringen testes i isolert PostgreSQL-skjema sammen med hele migreringskjeden og tidligere faser. `LifecycleChecks` dekker datoregler/tidssone, idempotente jobber, aktivering, fristendring, mottakertilgang, full/delvis retur, utstyrskoblinger, reversering, forlengelse, avslutning med sluttfaktura, uendrede økonomiske summer, rapport-/dashboardsummer, flervalg og fordelingsrader, separate valutaer samt CSV/XLSX. `LifecycleComponentChecks` rendrer alle nye visninger med norsk lokalisering. Ingen migrering er manuelt anvendt på applikasjonsdatabasen; vanlig oppstart anvender ventende migreringer.
