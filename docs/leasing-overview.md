# Leasingmodulen – arbeidsflyter og registrering

Denne veiledningen beskriver arbeidsflytene som er implementert i TenantPlatform, og hvordan brukeren registrerer hvert steg. Beskrivelsene tar utgangspunkt i norsk språkdrakt og kodebasen per 30. september 2026. Hvilke knapper du ser og kan bruke, avhenger av rollen din og valgt konto.

Modulen følger kjøpet fra eventuell rammeavtale og bestilling til registrert leasinganskaffelse, dokumentasjon, betalingsplan, fakturakontroll, utstyr og avslutning. Den registrerer avtaler og dokumenterte hendelser; den utfører ikke bankbetalinger, bokføring eller bestillinger hos leverandøren.

## Innhold

1. [Begreper, navigasjon og roller](#1-begreper-navigasjon-og-roller)
2. [Opprette en rammeavtale](#2-opprette-en-rammeavtale)
3. [Endre ramme og avtaleopplysninger](#3-endre-ramme-og-avtaleopplysninger)
4. [Opprette og godkjenne en bestilling](#4-opprette-og-godkjenne-en-bestilling)
5. [Realisere en bestilling som faktisk leasing](#5-realisere-en-bestilling-som-faktisk-leasing)
6. [Endre eller avslutte en bestilling](#6-endre-eller-avslutte-en-bestilling)
7. [Registrere en anskaffelse direkte eller uten rammeavtale](#7-registrere-en-anskaffelse-direkte-eller-uten-rammeavtale)
8. [Klassifisere og fordele kjøpsverdier](#8-klassifisere-og-fordele-kjøpsverdier)
9. [Behandle leverandørfaktura for utstyrskjøpet](#9-behandle-leverandørfaktura-for-utstyrskjøpet)
10. [Kreditnota og korrigering av kjøpsdokumentasjon](#10-kreditnota-og-korrigering-av-kjøpsdokumentasjon)
11. [Registrere og endre finansieringsvilkår](#11-registrere-og-endre-finansieringsvilkår)
12. [Opprette, importere og aktivere betalingsplan](#12-opprette-importere-og-aktivere-betalingsplan)
13. [Registrere og allokere leasingfaktura](#13-registrere-og-allokere-leasingfaktura)
14. [Fullføre fakturakontroll og håndtere avvik](#14-fullføre-fakturakontroll-og-håndtere-avvik)
15. [Registrere serienumre og utstyrsenheter](#15-registrere-serienumre-og-utstyrsenheter)
16. [Registrere frister og følge opp varsler](#16-registrere-frister-og-følge-opp-varsler)
17. [Retur, utskifting, utkjøp og tap/skade](#17-retur-utskifting-utkjøp-og-tapskade)
18. [Forlenge leasingforholdet](#18-forlenge-leasingforholdet)
19. [Avslutte leasingforhold og rammeavtale](#19-avslutte-leasingforhold-og-rammeavtale)
20. [Dokumenter, historikk, dashboard og rapporter](#20-dokumenter-historikk-dashboard-og-rapporter)
21. [Et sammenhengende eksempel](#21-et-sammenhengende-eksempel)
22. [Vanlige stoppunkter og kontrolliste](#22-vanlige-stoppunkter-og-kontrolliste)

## 1. Begreper, navigasjon og roller

### 1.1 Objektene som inngår

| Begrep | Hva du registrerer | Hva det brukes til |
| --- | --- | --- |
| Rammeavtale | Finansieringsselskap, maksbeløp, valuta, anskaffelsesperiode og standardvilkår | Setter rammen for flere kjøp og bestillinger |
| Bestilling | Planlagte varelinjer under en rammeavtale | Reserverer ramme når bestillingen godkjennes |
| Leasinganskaffelse | Faktisk kjøp med kjøpsdato, varelinjer, finansiert beløp og leasingvilkår | Registrerer kjøpsverdi og det enkelte leasingforholdet |
| Bestillingsleasing | En leasinganskaffelse uten rammeavtale | Enkeltstående leasing uten felles anskaffelsesramme |
| Varelinje | Beskrivelse, antall, pris, mva og klassifisering | Grunnlag for kjøpsverdi, fordeling og eventuelt utstyr |
| Utstyrsenhet | Ett fysisk objekt med valgfritt serienummer og intern-ID | Individuell oppfølging av plassering, ansvarlig og fysisk utfall |
| Betalingsplan | Avtalte terminer med perioder, forfall og beløp | Forventningsgrunnlag for leasingfakturaer |
| Leverandørfaktura | Faktura/kreditnota for selve kjøpet | Dokumenterer eksisterende kjøp eller oppretter kjøpslinjer ved godkjenning |
| Leasingfaktura | Faktura/kreditnota fra finansieringsselskapet | Fordeles mot avtalte betalingsterminer |
| Livsløpshendelse | Retur, utskifting, utkjøp, forlengelse eller avslutning | Registrerer et godkjent fysisk eller avtalemessig utfall |

**Bestillinger og Bestillingsleasing er forskjellige arbeidsflyter.** En bestilling ligger under en rammeavtale. Menyvalget **Bestillingsleasing** viser enkeltstående leasinganskaffelser uten rammeavtale; det viser ikke bestillinger som venter på levering.

### 1.2 Hvor du starter

| Menyvalg | Adresse | Bruk |
| --- | --- | --- |
| Oversikt | `/leasing` | Dashboard, nøkkeltall og søk i avtaler/anskaffelser |
| Rammeavtaler | `/leasing/frameworks` | Finne og åpne rammeavtaler |
| Bestillinger | `/leasing/purchase-orders` | Planlagte kjøp, godkjenning og realisering |
| Bestillingsleasing | `/leasing/orders` | Finne enkeltstående leasinganskaffelser |
| Fakturaimport | `/leasing/invoices` | Leverandørfakturaer for kjøpet |
| Betalingsplaner | `/leasing/payment-plans` | Åpne finansiering, terminer og fakturakontroll per anskaffelse |
| Leasingfakturaer | `/leasing/rental-invoices` | Registrere og kontrollere løpende leiefakturering |
| Utstyr | `/leasing/equipment` | Søke etter registrerte enheter |
| Oppfølging | `/leasing/followup` | Arbeidsliste, egne varsler og varslingsoppsett |
| Rapporter | `/leasing/reports` | Filtrerte uttrekk og eksport |

Fra en anskaffelse åpner **Finansieringsvilkår, betalingsplan og fakturakontroll**, **Utstyr** og **Livsløpsoppfølging** egne sider for den aktuelle anskaffelsen. **Avtaleopplysninger**, **Dokumentvedlegg** og **Endringshistorikk** hopper til seksjoner på detaljsiden. Historikken er sammenleggbar; åpne den for å se registreringene.

### 1.3 Forutsetninger og ansvar

Velg riktig konto før du registrerer noe. Leverandør, finansieringsselskap, ansvarlige brukere, bygg, dokumenter og dimensjoner må være tilgjengelige innenfor denne kontoen. Dersom en organisasjon eller bruker mangler i en valgliste, må grunnregisteret eller kontotilgangen avklares først.

Kontoadministrator har de sentrale leasingrettighetene. For andre brukere er objektenes ansvarlige og særskilte roller viktige:

| Arbeid | Normal tilgang |
| --- | --- |
| Opprette rammeavtale og frittstående anskaffelse | Kontoadministrator etter eksisterende opprettelsesrettighet |
| Vedlikeholde en eksisterende avtale/anskaffelse | Kontoadministrator eller relevant avtale-/anskaffelsesansvarlig |
| Registrere bestilling | Kontoadministrator eller rammeansvarlig; tildelt bestillingsansvarlig kan følge opp sin bestilling |
| Godkjenne bestilling | Kontoadministrator eller `LeasingOrderApprover` |
| Foreslå/godkjenne rammeendring | Kontoadministrator eller `LeasingLimitApprover` |
| Aktivere og kontrollere betalingsplan | Kontoadministrator eller `LeasingPlanApprover` |
| Godkjenne, korrigere og reversere leasingfaktura | Kontoadministrator eller `LeasingInvoiceApprover` |
| Fullføre fakturakontroll og akseptere avvik | Kontoadministrator eller `LeasingVarianceApprover` |
| Vedlikeholde fysiske enheter | Kontoadministrator eller `LeasingEquipmentManager` |
| Vedlikeholde livsløp og foreslå hendelser | Kontoadministrator, relevant ansvarlig eller `LeasingLifecycleManager` |
| Godkjenne livsløpshendelser/dokumentkontroll | Kontoadministrator eller `LeasingLifecycleApprover` |
| Lese kontoens rapportgrunnlag | Kontoadministrator eller rapportrolle; ordinær ansvarlig har et avgrenset utvalg |
| Eksportere rapporter | Kontoadministrator eller `LeasingReportExporter` |

Rapportleserrollen heter `LeasingReportReader`. Lesetilgang gir ikke automatisk redigerings- eller godkjenningsrettighet. Alle handlinger kontrolleres også på serveren. En bruker med nødvendig godkjenningsrettighet kan normalt godkjenne egne forslag; det er ikke innført et generelt krav om to forskjellige personer.

## 2. Opprette en rammeavtale

**Start:** Leasing → Rammeavtaler → **Ny rammeavtale**.

1. Registrer **Navn** og **Avtalenummer / referanse**. Bruk et navn som gjør avtalen lett å kjenne igjen i bestillinger og anskaffelser.
2. Velg **Finansieringsselskap** og **Ansvarlig**.
3. Velg **Valuta**. Denne følger kjøp og bestillinger som knyttes til rammen; modulen foretar ingen valutakonvertering.
4. Registrer anskaffelsesperiodens fra- og til-dato. Perioden sier når kjøp kan inngå i rammen, ikke når alle underliggende leasinger slutter.
5. Registrer **Maksbeløp** og om rammen er **inklusive mva**. Beløpsgrunnlaget avgjør om kjøpenes netto- eller bruttobeløp belaster rammen.
6. Velg om kreditnotaer skal frigjøre ramme. Valget kan være uavklart ved opprettelse, men må være avklart før en kjøpskreditnota kan godkjennes.
7. Velg status. Bruk **Åpen for anskaffelser** når avtalen skal tillate nye anskaffelser.
8. Registrer standardvilkår: leasingperiode i hele måneder, fast/flytende rente og betalingshyppighet. Ved flytende rente registreres referanserentens navn og margin. Opplysningene er manuelle avtaleopplysninger.
9. Legg inn notater og velg **Lagre**.
10. Åpne **Dokumentvedlegg** på detaljsiden og last opp signert rammeavtale og relevante vedlegg.

**Resultat:** Rammeavtalen finnes i oversikten. Rammeutnyttelsen viser maksbeløp, benyttet, reservert og tilgjengelig. Opprettelsen alene registrerer ingen kjøp eller betalingsterminer.

Standardvilkårene brukes som utgangspunkt for nye anskaffelser. Hver anskaffelse får egne datoer og vilkår. Senere redigering av standardvilkårene omskriver ikke automatisk eksisterende leasinger.

## 3. Endre ramme og avtaleopplysninger

### 3.1 Vanlig avtaleredigering

1. Åpne rammeavtalen og velg **Rediger**.
2. Endre de relevante opplysningene, eksempelvis ansvarlig, notater eller standardvilkår.
3. Oppgi **Begrunnelse for endringen** og velg **Lagre**.
4. Kontroller opplysningene og **Endringshistorikk** på detaljsiden.

Eksisterende kjøp kan begrense hvilke periode-, valuta- og beløpsgrunnlagsendringer som er tillatt. Maksbeløpet på en eksisterende ramme endres gjennom egen forslag/godkjenningsflyt. Valget for om kreditnotaer frigjør ramme kan ikke endres mens det finnes godkjente kjøpskreditnotaer på rammen.

### 3.2 Endre maksbeløpet

1. Finn seksjonen for rammeendringer på rammeavtalens detaljside.
2. Registrer **Nytt maksbeløp**, begrunnelse og eventuell dokumentreferanse.
3. Kontroller førverdien og tilgjengelig ramme etter endringen.
4. Velg **Lagre forslag**.
5. Godkjenner kontrollerer beløp, dokumentasjon og gjeldende benyttet/reservert ramme, og velger **Godkjenn** på forslaget.
6. Kontroller at maksbeløp og tilgjengelig ramme er oppdatert.

**Resultat:** Først godkjenningen endrer rammen. Den kan ikke settes lavere enn det gjeldende kjøp og reservasjoner krever. Forslag, begrunnelse og godkjenning bevares. Flyten registrerer ikke en fremtidig eller tilbakedatert rammeendring.

## 4. Opprette og godkjenne en bestilling

**Start:** Åpne rammeavtalen og velg **Ny bestilling**, eller Leasing → Bestillinger → **Ny bestilling**.

### 4.1 Registrere utkastet

1. Velg **Rammeavtale**. En bestilling må ha en rammeavtale.
2. Velg leverandør og ansvarlig.
3. Registrer bestillingsdato og eventuell forventet kjøps-/leveringsdato.
4. Registrer hver varelinje med beskrivelse, varenummer hvis aktuelt, enhet, antall, enhetspris eksklusive mva og mva-sats.
5. Velg **Realiseringsmetode** for hver linje: antall eller beløpsandel. Antall passer f.eks. ti datamaskiner; beløpsandel passer et avtalt omfang som leveres i deler.
6. Registrer kjent klassifisering og kostnadsfordeling. Et planlagt kjøp kan ha ufullstendig klassifisering, men den faktiske anskaffelsen må oppfylle kravene ved registrering.
7. Legg til flere linjer etter behov, noter bakgrunnen og oppgi begrunnelse der skjemaet krever det.
8. Velg **Lagre**. Systemet tildeler bestillingsnummer, og du kommer til bestillingens detaljside.
9. Last eventuelt opp bestillingsgrunnlag og tilbud som dokumentvedlegg.

Bestillings- og forventet leveringsdato starter ikke en leasingperiode. Det gjør den faktiske kjøpsdatoen på anskaffelsen.

### 4.2 Sende til godkjenning og reservere ramme

1. Kontroller bestillingslinjene og rammeutnyttelsen.
2. Skriv begrunnelse og velg **Send til godkjenning**.
3. Godkjenner åpner bestillingen, kontrollerer linjer og tilgjengelig ramme etter handlingen.
4. Velg **Godkjenn**, eller **Avvis** dersom grunnlaget må endres.
5. Ved avvisning korrigerer ansvarlig bestillingen og sender den inn på nytt.

**Resultat ved godkjenning:** Det gjenstående bestillingsomfanget reserverer ramme. Utkast og innsending alene gjør ikke dette. Godkjenning kontrollerer at anskaffelsesperioden er åpen på godkjenningstidspunktet, og at kapasiteten er tilstrekkelig.

Registreringen er intern. Systemet sender ikke bestillingen til leverandør eller finansieringsselskap.

## 5. Realisere en bestilling som faktisk leasing

En godkjent bestilling kan realiseres i én levering eller flere delleveranser. Hver registrert anskaffelse får faktisk kjøpsdato og egen leasingperiode.

### 5.1 Opprette anskaffelsen fra bestillingen

1. Åpne den godkjente bestillingen.
2. Velg **Opprett anskaffelse**.
3. Kontroller foreslåtte avtaleopplysninger og varelinjer. Registrer et gjenkjennelig navn og referanse for den faktiske leveransen.
4. Registrer **Kjøpsdato / leasingstart**, finansiert beløp, leverandør og ansvarlig. Kontroller leasingvilkårene som er foreslått fra rammen.
5. Tilpass varelinjenes antall og pris til det som faktisk er kjøpt nå. Ikke la hele bestillingsantallet stå dersom bare en del er levert.
6. I realiseringsseksjonen kobler du hver anskaffelseslinje til riktig **Bestillingslinje** og oppgir **Antall / beløpsandel**.
7. Ved antallsmetode må realisert antall samsvare med den faktiske anskaffelseslinjens antall. Ved beløpsandel angis en andel mellom 0 og 1; f.eks. `0,25` for en fjerdedel.
8. Kontroller **Reservasjon som frigis**, **Faktisk kjøpsverdi** og **Tilgjengelig ramme etter handling**.
9. Dersom faktisk pris avviker fra godkjent pris for delen, kontroller avviket og bekreft det med avkryssingen i realiseringsskjemaet.
10. Fullfør obligatoriske dimensjoner og fordeling på de faktiske varelinjene.
11. Velg økonomisk status **Registrert** når kjøpet er endelig og klart for registrering, og velg **Lagre**. Et utkast er ikke en ferdig realisering.
12. Gå tilbake til bestillingen og kontroller realisert/gjenstående omfang, restreservasjon og koblingen til den nye anskaffelsen.

**Resultat:** Godkjent reservasjon for den leverte delen reduseres, og faktisk kjøpsverdi belaster rammen. Gjenstående bestillingsomfang beholder sin reservasjon. Operasjonen avvises samlet hvis rammen ikke tåler den faktiske verdien.

Overlevering løses ved først å endre og få godkjent bestillingen. Avkryssingen for prisavvik gir ikke tillatelse til å realisere mer enn godkjent omfang.

### 5.2 Delleveranser

Gjenta arbeidsflyten for hver levering med dens egen kjøpsdato og faktiske varelinjer. Samme bestillingslinje kan realiseres gjennom flere anskaffelser. Én anskaffelse kan bare realisere én bestilling, og én anskaffelseslinje kobles til én bestillingslinje. Del derfor en varelinje dersom separate koblinger er nødvendige.

### 5.3 Koble en allerede registrert anskaffelse

1. Åpne bestillingen og finn **Koble eksisterende anskaffelse**.
2. Velg anskaffelsen blant tilgjengelige kandidater.
3. Koble varelinjer til bestillingslinjer og oppgi realisert antall/andel.
4. Kontroller frigitt reservasjon og eventuelt prisavvik. Bekreft avvik når det kreves.
5. Velg **Koble eksisterende anskaffelse**.

Kjøpsverdien er allerede registrert og skal ikke belastes en gang til. Koblingen dokumenterer oppfyllelsen og håndterer den tilhørende reservasjonen.

### 5.4 Realisere via leverandørfaktura

Fakturaimport kan opprette anskaffelse eller nye varelinjer og samtidig realisere en bestilling. Velg bestillingen og linjekoblingene i fakturakontrollen, som beskrevet i kapittel 9. Fakturalinjer som bare matches til allerede eksisterende kjøpslinjer skal ikke realisere bestillingen på nytt.

## 6. Endre eller avslutte en bestilling

### 6.1 Endre et godkjent kjøpsomfang

1. Åpne bestillingen og velg **Rediger**.
2. Endre planlagt gjenstående omfang, pris eller andre tillatte opplysninger.
3. Registrer begrunnelse og lagre endringsforslaget.
4. Godkjenner vurderer forslaget og tilgjengelig ramme etter endringen.
5. Velg **Godkjenn** eller **Avvis**.

Mens forslaget venter, gjelder den tidligere godkjente bestillingen og reservasjonen. Realiserte deler omskrives ikke. Systemet kontrollerer leveransene på nytt når endringen godkjennes.

### 6.2 Avslutte det som ikke skal leveres

1. Kontroller realiserte anskaffelser og gjenstående reservasjon.
2. Oppgi begrunnelse på bestillingens detaljside.
3. Velg **Kanseller gjenstående del** eller **Avslutt gjenstående del**, avhengig av utfallet.
4. Kontroller at restreservasjonen er frigitt.

Tidligere anskaffelser og fakturaer beholdes. Handlingen kansellerer ikke leasingforholdene som allerede er opprettet.

### 6.3 Gjenåpne reservasjon etter reversering

En reversert realisering kan etterlate omfang **Uten aktiv reservasjon**. Dette er ikke automatisk reservert på nytt.

1. Åpne bestillingen og kontroller det ureserverte omfanget.
2. Registrer begrunnelse og vurder ny rammeeffekt.
3. Bruk **Godkjenn gjenåpning av reservasjon** med nødvendig godkjenningsrettighet.
4. Kontroller at rammen er tilstrekkelig og anskaffelsesperioden fortsatt er åpen.

Kreditnotaer gjenåpner ikke bestillingsreservasjoner automatisk.

## 7. Registrere en anskaffelse direkte eller uten rammeavtale

Du trenger ikke registrere en bestilling før hvert faktisk kjøp.

**Under ramme:** Åpne rammeavtalen og velg handlingen for ny anskaffelse. **Uten ramme:** Åpne opprettelse fra leasingoversikten og velg **Uten rammeavtale (bestillingsleasing)**.

1. Registrer navn, avtalenummer/referanse, leverandør og ansvarlig.
2. Velg rammeavtale eller ingen rammeavtale. Ved rammetilknytning følger finansieringsselskap og valuta rammen; uten ramme angis de separat.
3. Registrer faktisk kjøpsdato, finansiert beløp og leasingperiode i hele måneder.
4. Kontroller rentevilkår og betalingshyppighet.
5. Legg inn varelinjer med beskrivelse, antall, enhetspris og mva-sats.
6. Fullfør klassifisering og kostnadsfordeling.
7. Bruk **Utkast** så lenge registreringen er uferdig. Velg **Registrert** og lagre når kjøpet skal inngå i registrert grunnlag.
8. Last opp dokumentasjon og registrer leverandørfaktura senere dersom denne ikke foreligger ennå.

**Resultat:** Registrert anskaffelse får kjøpsverdier og sluttdato beregnet fra kjøpsdato og leasingperiode. Ved rammetilknytning kontrolleres kjøpsdato mot anskaffelsesperioden og kjøpsverdien mot tilgjengelig ramme. Et enkeltstående kjøp har ingen felles ramme å belaste.

Finansiert beløp er en separat opplysning fra kjøpsverdi. Det er ikke en beregnet restgjeld. Rentevilkårene genererer heller ikke automatisk betalingsterminer.

**Om eldre fakturafelter:** Anskaffelsesredigeringen har fakturanummer/-dato som eldre referansefelter. Etikettene omtaler dem som påkrevd ved registrering, men dagens tjeneste tillater kjøpsregistrering før faktura foreligger. Faktisk fakturabehandling gjøres gjennom Fakturaimport; en referanse i disse feltene erstatter ikke fakturakontrollen.

### 7.1 Korrigere en anskaffelse

1. Åpne anskaffelsen og velg **Rediger**.
2. Korriger de aktuelle grunnopplysningene eller kjøpslinjene. Kontroller pris, antall og klassifisering samlet hvis linjene endres.
3. Oppgi begrunnelse og lagre. Registrerte kjøp gjennomgår ny validering og eventuell rammekontroll.
4. Kontroller før-/ettergrunnlaget i endringshistorikken.

En registrert anskaffelse kan ikke settes tilbake til utkast. Linjer med utstyr eller behandlet antall kan ikke reduseres under dokumentert omfang. Endringer i løpende finansieringsvilkår og forlengelser føres gjennom egne revisjoner/hendelser, slik at opprinnelig avtalehistorikk bevares.

### 7.2 Kansellere en økonomisk registrering

Bruk dette når selve anskaffelsesregistreringen skal kanselleres, ikke ved ordinært utløp eller retur av utstyr.

1. Åpne seksjonen **Kanseller anskaffelsen** på detaljsiden.
2. Skriv begrunnelse og velg kanselleringsknappen.
3. Kontroller status **Kansellert** og historikken. Anskaffelsen inngår ikke lenger som et registrert kjøp i benyttet ramme.
4. Hvis kjøpet realiserte en bestilling, kontroller bestillingens reverserte omfang og eventuell del uten aktiv reservasjon. Reservasjon gjenåpnes ikke automatisk.
5. Avklar dokumenter og andre tilknyttede forhold separat. Kanselleringen er ikke en kreditnota, betaling eller ekstern oppsigelse.

## 8. Klassifisere og fordele kjøpsverdier

### 8.1 Klargjøre dimensjoner

En administrator vedlikeholder dimensjoner og verdier under **Dimensjoner**. Eksempler kan være avdeling, kostnadssted, prosjekt eller utstyrstype. Kontroller hvilke dimensjoner som er tilgjengelige/påkrevde for leasing, og hvilke som tillater økonomisk fordeling. Navnene i din konto kan avvike fra eksemplene her.

### 8.2 Registrere på en varelinje

1. Åpne **Klassifisering og kostnadsfordeling** for linjen i anskaffelses-, bestillings- eller fakturaskjemaet.
2. Velg **Felles klassifisering for hele linjen**. Bruk søk og hierarkiet til å finne riktige verdier.
3. Hvis hele linjen har samme økonomiske tilhørighet, behold samlet fordeling og fullfør nødvendige valg.
4. Skal verdien fordeles, velg fordelingsmåte: prosent, nettobeløp eller antall.
5. Velg hvilke dimensjoner som varierer mellom fordelingsradene.
6. Legg til fordelingsrader. Angi andel/beløp/antall og dimensjonsverdier på hver rad.
7. Kontroller **Fordelt**, **Gjenstår** og meldinger om manglende verdier. Summen må være henholdsvis 100 %, linjens netto eller linjens antall.
8. Lagre hovedregistreringen. På anskaffelsen kan felles valg kopieres til øvrige linjer med den egne kopieringshandlingen; kontroller hver linje etterpå.

Eksempel: Fire PC-er til samlet netto 40 000 fordeles med 75 % på avdeling A og 25 % på avdeling B. Fordelingen blir 30 000 og 10 000. To avdelingsmerker i et flervalgsfelt er derimot ikke en 50/50-fordeling.

Klassifisering og fordelingsgrunnlag bevares på kjøpet. Senere flytting av en utstyrsenhet til et annet bygg endrer ikke disse økonomiske opplysningene. Modulen fordeler heller ikke renter, leie og gebyrer automatisk etter kjøpets dimensjoner.

## 9. Behandle leverandørfaktura for utstyrskjøpet

Dette gjelder fakturaen for selve kjøpet. Bruk **Leasingfakturaer** for de løpende terminene fra finansieringsselskapet.

### 9.1 Laste opp og kontrollere dokumentet

1. Åpne **Fakturaimport**, eventuelt fra anskaffelsen eller bestillingen slik at sammenhengen følger med.
2. Last opp XML/EHF, PDF, PNG eller JPEG.
3. Åpne dokumentets kontrollside. Bruk **Oppdater** hvis bakgrunnstolkingen ikke er ferdig.
4. Sammenhold originaldokumentet med **Kontrollerte data**: dokumenttype, nummer, leverandøridentitet, faktura-/forfallsdato, valuta og referanser.
5. Korriger avleste data og varelinjer. Ved manglende tolking kan du fylle inn kontrollen manuelt. PDF/bildetolking krever at den eksisterende tolketjenesten er konfigurert og aktivert.
6. Kontroller antall, enhet, prisgrunnlag, enhetspris, rabatter/tillegg, mva-kategori og sats. Dokumentrabatter og -tillegg må også være med.

Ny tolking er et nytt kildeforslag. **Tolk på nytt (behold korrigeringer)** erstatter ikke automatisk dine kontrollerte data. Bruk **Erstatt med dette tolkeresultatet** bare når du faktisk vil erstatte utkastet og linjekoblingene.

### 9.2 Velge hva fakturaen skal gjøre

Under **Anskaffelsen dokumentet gjelder** velger du ett av følgende løp:

| Valg | Registrering | Effekt ved godkjenning |
| --- | --- | --- |
| Dokumentere et eksisterende kjøp | Velg anskaffelse og match hver fakturalinje til eksisterende varelinje | Dokumentert antall/beløp øker; kjøpsverdien blir ikke lagt til på nytt |
| Legge til en faktisk ny kjøpslinje | Velg anskaffelse og «Opprett ny varelinje» for relevant fakturalinje | Nye linjer øker kjøpsverdien og kan belaste rammen |
| Opprette ny anskaffelse | Velg «Opprett ny anskaffelse ved godkjenning» og fyll inn anskaffelsesopplysninger | Anskaffelsen opprettes ved godkjenning etter vanlig kontroll |

Ved ny anskaffelse fyller du inn navn, referanse, eventuell ramme, leverandør, finansieringsselskap, ansvarlig, kjøpsdato, finansiert beløp og vilkår. Bekreft uttrykkelig kjøpsdatoen; fakturadato er ikke i seg selv bekreftet leasingstart.

For nye kjøpslinjer fullfører du klassifisering og fordeling. Matching til eksisterende linjer bevarer deres pris, antall og dimensjoner. Hvis selve kjøpet er feil, må det korrigeres eksplisitt i anskaffelsen.

Hvis dokumentet realiserer en bestilling, velger du bestilling, bestillingslinjer og antall/andel for de aktuelle nye kjøpslinjene. Bekreft eventuelt prisavvik og kontroller reservasjonen som skal frigis. Ett dokument gjelder én anskaffelse; en samlefaktura for flere bestillinger kan ikke matches automatisk som én anskaffelse.

### 9.3 Avstemme, forhåndsvise og godkjenne

1. Kontroller linjesummer, mva-grupper, netto, mva, brutto, forskudd og eventuell eksplisitt betalingsavrunding mot originalen.
2. Bruk **Lagre uferdig kontroll** hvis arbeidet skal fortsette senere.
3. Avklar mulige duplikater. Et mulig treff krever begrunnelse; sikre godkjente duplikater kan ikke bare overstyres.
4. Bekreft at dokument, korrigeringer, linjekoblinger, dimensjoner og fordeling er kontrollert.
5. Oppgi begrunnelse og velg **Lagre og vis effekt før godkjenning**.
6. Les **Effekt ved godkjenning**: gammel/endret/ny kjøpsverdi, rammeeffekt og eventuell bestillingsreservasjon.
7. Velg **Godkjenn i Leasing** med nødvendig tilgang.
8. Åpne anskaffelsen og kontroller dokumentet, dokumentert kjøpsverdi og eventuelle nye linjer. Ved bestillingskobling kontrolleres også realiseringen på bestillingen.

Godkjenning låser det kontrollerte dokumentgrunnlaget. Opplasting og lagring av utkast påvirker ikke kjøpsverdien. Godkjenningen er intern registrering, ikke betaling eller regnskapsføring.

## 10. Kreditnota og korrigering av kjøpsdokumentasjon

### 10.1 Registrere kjøpskreditnota

1. Last opp kreditnota gjennom **Fakturaimport**.
2. Kontroller at dokumenttype er **Kreditnota**, og velg samme anskaffelse som originalkjøpet.
3. Koble hver kreditlinje til **Opprinnelig godkjent fakturalinje**.
4. Registrer krediterte antall og beløp som positive kildeverdier. Dokumenttypen gir negativ virkning; ikke legg inn minus en gang til.
5. Kontroller at krediteringen ikke overstiger opprinnelig dokumentert antall/netto/mva etter tidligere krediteringer.
6. Hvis kjøpet ligger under ramme, avklar rammeavtalens valg for om kreditnotaer frigjør ramme.
7. Fullfør innholdsbekreftelse, begrunnelse og forhåndsvisning, og velg **Godkjenn i Leasing**.

Krediteringen dokumenteres separat. Den endrer ikke automatisk finansiert beløp, rentevilkår, leasingperiode eller betalingsplan. Vurder slike endringer gjennom de respektive arbeidsflytene.

### 10.2 Korrigere et godkjent dokument

Et godkjent dokument redigeres ikke som et vanlig utkast. Bruk dokumentets reverseringshandling med begrunnelse når en godkjenning må oppheves, og behandle korrigert grunnlag på nytt. Avhengige kreditkoblinger må håndteres først. Reversering kan påvirke kjøpsverdi, ramme og bestillingsrealisering og gjennomgår derfor ny kontroll.

Originaldokument, godkjenningsgrunnlag og historikk beholdes. Reversering er ikke permanent sletting.

### 10.3 Avvise et dokument før godkjenning

Hvis et kjøpsdokument under kontroll ikke skal registreres, åpner du dokumentet, skriver begrunnelse i handlingsseksjonen og bruker avvisningshandlingen. Dokumentet beholdes som avvist uten kjøpseffekt. Hvis fakturaen allerede er godkjent, er det reverseringsflyten som gjelder.

## 11. Registrere og endre finansieringsvilkår

**Start:** Anskaffelsen → **Finansieringsvilkår, betalingsplan og fakturakontroll**, eller via Betalingsplaner.

1. Se gjeldende finansieringsopplysninger for valgt dato. Eldre opprinnelige opplysninger kan ha ukjent virkningsdato.
2. Åpne handlingen for ny finansieringsrevisjon.
3. Registrer datoen vilkårene gjelder fra, finansieringsselskap og finansiert beløp.
4. Registrer de avtalte vilkårene: rente, referanserente/margin, betalingshyppighet og relevante tilleggsopplysninger.
5. Tilleggsfeltene omfatter blant annet finansieringsselskapets avtalereferanse, startleie, restverdi, etableringsgebyr, andre gebyrer, første forfall, betalingstidspunkt og dokumentreferanse.
6. For flytende rente kan observert sats, observasjonsdato, reguleringshyppighet og gulv/tak registreres. Satsene må hentes og bekreftes utenfor denne registreringen.
7. Oppgi begrunnelse og velg **Lagre**.
8. Kontroller revisjonen i vilkårshistorikken og vurder berørte betalingsterminer.

**Resultat:** Daterte vilkår bevares som revisjoner. Berørte aktive terminer merkes for vurdering; beløpene endres ikke automatisk. To revisjoner kan ikke starte samme dato. Fremtidige revisjoner støttes.

En restverdiopplysning er ikke automatisk en betalingsforpliktelse eller utkjøpspris. Dokumenterte betalingsposter må registreres i betalingsplanen, og faktisk utkjøp registreres som egen livsløpshendelse.

## 12. Opprette, importere og aktivere betalingsplan

### 12.1 Registrere manuelt eller som serie

1. Åpne finansieringssiden og velg **Ny betalingsplan**.
2. Bruk den avtalte planen fra finansieringsselskapet som grunnlag.
3. For en serie registrerer du første periodes fra-/til-dato, første forfall, antall terminer, betalingshyppighet, netto og mva. Velg månedsslutt dersom dette er avtalt.
4. Velg forhåndsvisning av serien. Serien gjentar oppgitte beløp og datomønster; den beregner ikke leasingbeløp fra rente og finansiert kapital.
5. Alternativt legger du til terminer én om gangen. Kontroller hver termin ved å åpne den.
6. Angi terminreferanse, periode, forfallsdato, posttype og avtalte netto-/mva-/bruttobeløp. Kontroller kobling til relevant vilkårsrevisjon og eventuelle kontrollbegrunnelser.
7. Registrer særskilte poster, som startleie eller gebyr, uttrykkelig. Kontroller første og siste termin separat.
8. Velg kildedokument blant anskaffelsens vedlegg og skriv notater.
9. Kontroller sammenligningen med eventuell aktiv plan, og velg **Lagre som utkast**.

Datoene forankres i første dato og håndterer månedsslutt/skuddår. Det gjøres ikke automatisk justering for helligdager. Manglende beløp betyr ukjent; tallet 0 betyr et uttrykkelig nullbeløp.

### 12.2 Importere CSV eller XLSX

1. Åpne ny plan eller planredigering, og utvid importseksjonen.
2. Last opp CSV eller XLSX. Velg regneark dersom filen har flere ark.
3. Koble kildekolonner til referanse, periode fra/til, forfall, netto, mva, brutto og posttype. Valuta kan også kobles.
4. Velg forhåndsvisning av importen.
5. Rett radfeil for datoer, beløp, valuta og duplikate referanser. XLSX-formler støttes ikke; bruk en fil med verdier.
6. Kontroller alle importerte terminer og suppler eventuelle dokumentreferanser og begrunnelser.
7. Lagre utkastet.

Importen støtter inntil 600 terminer og normalt maksimalt 10 MB, innenfor dokumentlagringens grense. En PDF-plan kan legges ved og registreres manuelt; PDF-tabeller importeres ikke automatisk som betalingsplan.

### 12.3 Aktivere planen

1. Åpne det lagrede planutkastet i planlisten.
2. Kontroller avtalebeløp, perioder, forfall, kilde og sammenligning med aktiv plan.
3. Oppgi begrunnelse. Bekreft endringer i allerede fakturerte terminer når dette kreves.
4. En bruker med planrettighet velger **Aktiver kontrollert plan**.
5. Kontroller den aktive planen og fakturakontrollen.

Bare aktiv planversjon inngår i forventede betalinger. Utkast og erstattede versjoner beholdes for sporbarhet, men summeres ikke som ekstra forpliktelser.

### 12.4 Revidere en eksisterende plan

1. Åpne en planversjon og velg revisjonshandlingen.
2. Endre avtalte datoer/beløp eller legg til dokumenterte poster.
3. Behold terminreferansen når det er samme forpliktelse som videreføres. En ny referanse skal ikke brukes bare for å skjule tidligere fakturering.
4. Lagre nytt utkast og gjennomfør aktiveringen som over.
5. Behandle terminer som er merket **Må vurderes**, og kontroller tidligere fakturaallokeringer.

Historisk allokerte terminer kan ikke bare fjernes fra en ny aktiv plan. Planrevisjon endrer heller ikke fakturaallokeringene automatisk.

## 13. Registrere og allokere leasingfaktura

**Start:** Leasing → Leasingfakturaer. Dette er en annen dokumentkategori enn Fakturaimport for kjøpet.

### 13.1 Registrere dokumentgrunnlaget

1. Velg **Registrer leasingfaktura manuelt** eller **Last opp leasingfaktura/kreditnota**.
2. Velg finansieringsselskap og valuta og åpne dokumentet/utkastet.
3. Kontroller eller registrer dokumenttype, nummer, fakturadato, forfall, referanser, fakturalinjer og summer.
4. Sammenhold opplysningene med originalen. Tolking gir forslag; manuell kontroll fungerer også uten ekstern tolking.
5. Bekreft at innholdet er kontrollert, registrer begrunnelse og lagre kontrollen.

### 13.2 Fordele fakturaen til terminer

1. Åpne seksjonen for matching på leasingfakturaen.
2. Legg til en allokeringsrad.
3. Velg riktig anskaffelse/termin i terminlisten. Forslag må kontrolleres; de er ikke automatisk godkjente koblinger.
4. Angi hvor mye netto og mva fra dokumentet som gjelder denne terminen.
5. Legg til flere allokeringsrader hvis fakturaen dekker flere terminer eller anskaffelser.
6. Kontroller **Uallokert rest**. Fordelingen kan ikke overstige dokumentets netto og mva.
7. Lagre matching/kontroll.
8. Godkjenner velger handlingen for godkjenning av leasingfakturaen. Godkjenningen gjelder den sist lagrede kontrollen.
9. Åpne fakturakontrollen på anskaffelsen og kontroller at allokeringen vises på riktig termin.

Flere anskaffelser på samme leasingfaktura må ha kompatibelt finansieringsselskap, valuta og kontotilknytning. Dokumentets faktiske forfall og terminens forventede forfall vises separat.

**Resultat:** Godkjente allokeringer inngår i fakturerte beløp. De endrer ikke kjøpsverdi, finansiert beløp, dimensjoner, rammeutnyttelse eller bestillingsreservasjoner.

### 13.3 Kreditnota, korrigering og reversering

For en leasingkreditnota velges godkjent originalfaktura. Hver kredittallokering kobles også til den opprinnelige allokeringen for samme termin. Registrer positive krediterte kildebeløp; kreditnotatypen bestemmer fortegnet. Systemet hindrer overkreditering.

For feilfordeling på en godkjent faktura endrer du allokeringsradene, oppgir begrunnelse og bruker korrigeringshandlingen. Gamle allokeringer reverseres og nye opprettes med historikk. Skal hele dokumentets effekt oppheves, brukes reversering med begrunnelse. Avhengige kredittkoblinger må håndteres først.

## 14. Fullføre fakturakontroll og håndtere avvik

1. Åpne anskaffelsens finansierings-/betalingsside og finn **Terminer og fakturakontroll**.
2. Åpne aktuell termin. Sammenlign forventet netto/mva/brutto, netto fakturert etter kreditnotaer og differansen.
3. Kontroller lenkene til fakturaer og allokeringer. Avklar eventuell uallokert rest på dokumentene.
4. Når all fakturering for terminen er mottatt, bruk handlingen for å fullføre fakturakontrollen og oppgi begrunnelse.
5. Stemmer beløpene innen toleransen, blir terminen avstemt. Toleransen er 0,01 i valutaen på hver av netto, mva og brutto.
6. Ved avvik korrigerer du enten dokument/allokering/plan etter faktisk grunnlag, eller bruker **Aksepter dokumentert avvik** med begrunnelse og nødvendig rettighet.
7. Bruk gjenåpning hvis kontrollen må tas om igjen. Nye relevante allokeringer, krediteringer og planendringer kan også åpne kontrollen igjen.

Et avvik aksepteres uten å omskrive forventet beløp. Avstemt fakturering betyr heller ikke at banken har betalt fakturaen eller at leasingforholdet er økonomisk oppgjort.

## 15. Registrere serienumre og utstyrsenheter

**Start:** Åpne anskaffelsen → **Utstyr**. Denne lenken åpner utstyrssiden med riktig anskaffelse valgt. Den generelle utstyrsoversikten brukes først og fremst til søk og videre navigasjon.

### 15.1 Klargjøre en varelinje

1. Anskaffelsen må være økonomisk registrert, og livsløpet må tillate utstyrsregistrering.
2. Finn varelinjen over utstyrsskjemaet og aktiver enhetsregistrering med linjens knapp.
3. Bruk dette bare for tellbart fysisk utstyr med helt antall. En tjeneste eller ren beløpslinje trenger ikke enheter.
4. Kontroller at linjen nå er tilgjengelig i feltet **Varelinje**.

Enhetsregistrering kan ikke slås av når det finnes enheter eller behandlingshistorikk som bruker linjen.

### 15.2 Registrere én enhet

1. Velg **Ny enhet / masseregistrering**.
2. Velg **Varelinje**. Avkryssing for duplikatkontroll alene er ikke en registrering av utstyr.
3. Skriv beskrivelse/modell.
4. Registrer eventuelt intern utstyrs-ID og serienummer. Begge er valgfrie; intern-ID må være unik innenfor kontoen.
5. Registrer registreringsdato og eventuell hendelses-/statusdato.
6. Velg status **I bruk** eller **Planlagt returnert**, samt eventuelt nåværende plassering og ansvarlig.
7. Velg dokumentasjon blant anskaffelsens vedlegg og registrer notater hvis aktuelt.
8. La masseregistreringsfeltet være tomt og antallet være 1.
9. Dersom systemet varsler mulig serienummerduplikat, undersøk om enheten allerede finnes. Samme serienummer kan være legitimt hos forskjellige produsenter. Kryss av for at mulige duplikater er kontrollert bare etter vurderingen.
10. Skriv begrunnelse og velg **Lagre**.
11. Kontroller at enheten finnes i oversikten med riktig varelinje og kjennetegn.

Manglende varelinje gir en valideringsmelding og skal ikke sende kontoadministrator til AccessDenied. Bekreftelsen av serienummerduplikater gjelder lagringen; den er ikke en generell innstilling som alene skal lagres.

### 15.3 Masseregistrering

1. Start en ny registrering og fyll ut felles varelinje, beskrivelse, datoer, plassering og øvrige felles opplysninger.
2. Skriv én enhet per linje i masseregistreringsfeltet med formatet `serienummer;intern-ID`, for eksempel:

   ```text
   SN-1001;PC-001
   SN-1002;PC-002
   SN-1003;PC-003
   ```

3. Intern-ID kan utelates dersom den ikke skal brukes. Det opprettes ikke fiktive serienumre.
4. Alternativt lar du tekstfeltet være tomt og angir ønsket antall enheter uten serienumre. Ikke oppgi samme intern-ID som fellesverdi for flere enheter.
5. Kontroller mulige duplikater, skriv begrunnelse og lagre.
6. Kontroller alle opprettede enheter.

Tekstlinjene styrer antallet når masseregistreringsteksten brukes. Det separate antallsfeltet brukes når tekstfeltet er tomt. Systemet kontrollerer antall mot varelinjen, inkludert antall som allerede er behandlet gjennom livsløpshendelser uten enhetsregistrering.

### 15.4 Redigere eksisterende utstyr

Åpne anskaffelsens utstyrsvisning og klikk den aktuelle enhetsknappen for å hente den inn i skjemaet. Endre opplysninger, oppgi begrunnelse og lagre. Bruk ikke **Ny enhet** når hensikten er å endre en eksisterende.

Nåværende plassering/ansvarlig endrer ikke historiske økonomiske dimensjoner. Endelige utfall som returnert, erstattet, kjøpt ut eller tapt/skadet registreres gjennom godkjent livsløpshendelse, ikke vanlig redigering av statusfeltet.

## 16. Registrere frister og følge opp varsler

### 16.1 Registrere avtalevilkår for oppfølging

1. Åpne anskaffelsen → **Livsløpsoppfølging**.
2. Se opprinnelig og avtalt sluttdato og eventuell tidligere avslutning.
3. Under **Frister og forlengelsesvilkår** velger du fristgrunnlag: uavklart, eksplisitt dato, dager eller kalendermåneder før sluttdato.
4. Fyll ut eksplisitt oppsigelsesdato eller antall dager/måneder, avhengig av valgt grunnlag.
5. Registrer om automatisk forlengelse er avtalt og eventuell forlengelsesperiode.
6. Registrer avtalt returfrist, ansvarlig, kildedokument og notater.
7. Oppgi begrunnelse og lagre.
8. Kontroller den viste oppsigelsesfristen og oppfølgingslisten.

Kalendermåneder følger månedssluttregler: 31. mars 2028 minus én måned blir 29. februar. Ingen helligdagsjustering foretas. Ukjente vilkår skal stå som uavklart. Ny avtalt sluttdato registreres gjennom forlengelsesflyten, ikke ved å overskrive tidligere historikk i fristskjemaet.

### 16.2 Behandle oppfølgingspunkter

1. Gå til **Oppfølging**.
2. Filtrer på type, status og frist til og med. Standardvisningen viser åpne oppgaver innen valgt frist; fjern eller utvid filtrene ved behov.
3. Åpne et punkt og bruk **Åpne tilknyttet objekt** for å undersøke grunnlaget.
4. Registrer ansvarlig, status og kommentar/begrunnelse.
5. Velg **Lagre**. Når punktet fullføres, lagres også hvem som fullførte og tidspunktet.
6. Gjennomfør nødvendige registreringer på selve avtalen, fakturaen eller utstyret. Å fullføre en oppgave utfører ikke automatisk den underliggende forretningshandlingen.

Listen omfatter blant annet utløp, oppsigelse, retur, ubekreftet forlengelse, åpne reservasjoner, utstyrsbehandling, manglende ansvarlig og uavklart plan-/dokumentkontroll. Endret frist kan gi en ny oppgave og gjøre den gamle foreldet.

### 16.3 Aktivere varsling

1. Kontoadministrator åpner **Oppfølging → Varslingsoppsett**.
2. Angi antall dager før frist, kommaseparert, eksempelvis `90,30,7`.
3. Velg tidssone og eventuelle ekstra mottakere.
4. Kryss av **Aktiver varsler fra nå**.
5. Oppgi begrunnelse og lagre.
6. Se egne leverte varsler under **Mine leasingvarsler**, og marker dem lest etter behandling. Administrator kan kontrollere leveringsstatus og feil i oppsettet.

Varsling er av til den aktiveres. Varseltidspunkter før aktivering sendes ikke ut som historisk massevarsling, men etterslepet finnes i oppfølgingslisten. Varsler er i applikasjonen, ikke e-post. Planlagt tidspunkt er kl. 09 i valgt tidssone, og bakgrunnsjobben kontrollerer normalt hvert femte minutt. Mottakernes tilgang kontrolleres ved levering.

## 17. Retur, utskifting, utkjøp og tap/skade

**Start:** Livsløpsoppfølging → **Registrer hendelse til godkjenning**.

### 17.1 Felles forslag og godkjenning

1. Velg hendelsestype og faktisk hendelsesdato.
2. Velg varelinje og antall som hendelsen gjelder.
3. Hvis enhetene er registrert, marker de konkrete enhetene. Antallet må stemme med valgte enheter.
4. Hvis enhetene ikke er registrert individuelt, oppgir du antall på varelinjen. Du kan ikke bruke uregistrert antall til å omgå allerede registrerte enheter.
5. Registrer motpart, referanse, dokumentasjon og begrunnelse etter behov. Referansefeltet har i dagens skjermbilde etiketten **Terminreferanse**, også i hendelsesskjemaet.
6. Velg **Send til godkjenning**.
7. Godkjenner åpner forslaget under **Hendelser og godkjenninger**, kontrollerer grunnlaget, skriver begrunnelse og velger **Godkjenn hendelse** eller **Avvis forslag**.
8. Kontroller enhetsstatus og registrert behandlet antall etter godkjenning.

Forslaget alene endrer ikke utfallet. Samlet godkjent behandlet antall kan ikke overstige varelinjens tilgjengelige antall.

### 17.2 Full eller delvis retur

Velg retur og registrer enhetene/antallet som faktisk er returnert. Gjenta ved senere delleveranser. Registrerte enheter får returstatus etter godkjenning.

Retur oppretter ikke kreditnota, frigjør ikke kjøpsramme og avslutter ikke automatisk fremtidige betalinger. Registrer eventuell kreditnota, planrevisjon og avslutning separat.

### 17.3 Utskifting

1. Registrer nytt kjøp gjennom ordinær anskaffelsesflyt hvis utskiftingen medfører nytt kjøp.
2. Registrer den nye enheten på en økonomisk registrert anskaffelse.
3. Opprett utskiftingshendelse for én opprinnelig enhet/ett antall.
4. Søk etter og velg **Ny registrert utstyrsenhet**.
5. Registrer dokumentasjon/begrunnelse, send inn og få hendelsen godkjent.

Utskiftingen knytter gammelt og nytt utstyr sammen. Den nye enheten må være tilgjengelig for koblingen; samme erstatningsenhet kan ikke gjenbrukes i flere godkjente utskiftinger. Hendelsen er ingen snarvei rundt kjøps- og rammekontrollen.

### 17.4 Utkjøp

Velg utkjøp, enheter/antall, **Faktisk avtalt utkjøpsbeløp** og kildedokument. Send forslaget til godkjenning. Dokumentasjon og faktisk beløp må være angitt; restverdien brukes ikke automatisk som pris. Faktura og eventuell endret betalingsplan behandles separat.

### 17.5 Tap eller skade og korrigering

Registrer tap/skade med faktisk dato, berørt utstyr/antall og begrunnelse, og send til godkjenning. Hendelsen endrer ikke økonomiske forpliktelser automatisk.

En feil godkjent hendelse kan reverseres med begrunnelse når den fortsatt er den siste hendelsen/tilstanden som trygt kan gjenopprettes. Nyere tilstandsendringer kan blokkere reverseringen. Historikken beholdes; bruk ikke sletting for å skjule feilregistreringen.

## 18. Forlenge leasingforholdet

1. Avklar forlengelsen med motparten utenfor systemet, og last opp dokumentasjonen på anskaffelsen.
2. Åpne livsløpsoppfølging og opprett hendelse av typen forlengelse.
3. Angi virkningsdato, tidligere sluttdato og ny avtalt sluttdato.
4. Registrer nye eller videreførte vilkår og velg kildedokument.
5. Kryss av dersom betalingsforpliktelsene er endret, og oppgi begrunnelse.
6. Send til godkjenning; godkjenner kontrollerer og godkjenner hendelsen.
7. Kontroller ny avtalt sluttdato og status **Forlenget**. Opprinnelig sluttdato bevares.
8. Gå til betalingsplanen. Registrer/importer og aktiver ny avtalt plan dersom betalingene er endret.
9. Gå tilbake til livsløpsoppfølging og bruk **Bekreft kontrollert betalingsplan** med begrunnelse når kontrollen er utført.

Forlengelsen markerer betalingsplanen for vurdering. Når betalingene er endret, kreves en annen aktiv planversjon før kontrollen kan bekreftes. Systemet beregner ikke nye leiebeløp.

Avtalt automatisk forlengelse utløser oppfølging ved relevante frister. Det opprettes ikke en godkjent forlengelse eller nye betalingsforpliktelser bare fordi datoen passerer.

## 19. Avslutte leasingforhold og rammeavtale

### 19.1 Kontrollere anskaffelsen før avslutning

1. Åpne livsløpsoppfølging og les **Avslutningskontroll**.
2. Undersøk utstyr som fortsatt er i bruk, gjenstående antall og åpne oppfølgingspunkter.
3. Kontroller betalingsplan, gjenstående terminer, fakturaavvik og uallokerte dokumenter.
4. Uallokerte fakturaer for samme finansieringsselskap og valuta kan vises som mulige relevante dokumenter. Dette er ikke i seg selv en bekreftet kobling til akkurat denne anskaffelsen.
5. Registrer retur, utkjøp eller annet fysisk utfall der det er aktuelt, og legg ved avslutningsdokumentasjon.
6. Ved behov kan en godkjent hendelse sette forholdet **Under avslutning** mens arbeidet pågår.

### 19.2 Ordinær eller førtidig avslutning

1. Opprett avslutningshendelse og velg ordinær eller førtidig avslutning.
2. Registrer faktisk dato, dokumentasjon og begrunnelse. Ordinær avslutning kan ikke dateres før avtalt slutt; fremtidig faktisk avslutning godtas ikke.
3. Hvis kontrollen viser åpne forhold, avklar hvordan de skal følges opp. Bekreft uttrykkelig dette i skjemaet dersom forholdet likevel skal avsluttes.
4. Send forslaget til godkjenning og få beslutningen registrert.
5. Kontroller status **Avsluttet**, avslutningsmåte og faktisk dato.
6. Ved førtidig avslutning må betalingsplanen revideres og kontrolleres med dokumentert grunnlag. Fremtidige terminer slettes ikke automatisk.

Anskaffelsen beholder økonomisk status **Registrert**. Ordinær livsløpsavslutning er ikke det samme som **Kanseller anskaffelsen** på detaljsiden. Kansellering gjelder den økonomiske kjøpsregistreringen og har egne kontroller; den skal ikke brukes som erstatning for ordinær avslutning.

### 19.3 Fullføre dokumentkontrollen

1. Registrer og behandle sluttfakturaer, kreditnotaer og planrevisjoner også etter at livsløpet er avsluttet.
2. Kontroller at dokument- og planforholdene er tilstrekkelig avklart.
3. Velg **Bekreft dokumentkontroll** med begrunnelse og nødvendig godkjenningsrettighet.
4. Gjenåpne kontrollen dersom den må tas om igjen. Nye relevante fakturaendringer kan også gjøre tidligere ferdig kontroll utdatert.

**Avsluttet leasingforhold** og **Dokumentkontroll ferdig** er separate opplysninger. Ingen av dem er bekreftelse på bankbetaling eller beregnet restgjeld.

### 19.4 Stenge eller avslutte en rammeavtale

1. For å stoppe nye anskaffelser, rediger rammeavtalen til **Stengt for nye anskaffelser** og oppgi begrunnelse.
2. Fortsett oppfølgingen av eksisterende leasinger. Utløpt anskaffelsesperiode avslutter dem ikke, og restreservasjoner frigis ikke automatisk.
3. Avklar gjenstående bestillinger/reservasjoner og åpne oppfølgingspunkter.
4. Sørg for at underliggende registrerte leasingforhold er avsluttet og dokumentkontrollen ferdig, med plankontroll avklart og uten uavklarte hendelsesforslag.
5. Velg status **Avsluttet** for rammeavtalen i redigeringen og lagre med begrunnelse. Systemet kontrollerer de tilknyttede forholdene før dette tillates.

Historisk benyttet ramme forsvinner ikke fordi leasingforholdene avsluttes.

## 20. Dokumenter, historikk, dashboard og rapporter

### 20.1 Dokumentvedlegg og historikk

Last opp kontrakter, leveringsbekreftelser, endringsavtaler og avslutningsdokumentasjon under **Dokumentvedlegg** på det aktuelle objektet. Last opp dokumentasjon på anskaffelsen før du velger den som kilde i utstyrs-, plan- eller livsløpsskjemaer.

Et vanlig vedlegg starter ikke fakturaimport, aktiverer ingen betalingsplan og godkjenner ingen hendelse. Bruk de egne arbeidsflytene for disse handlingene.

**Endringshistorikk**, **Kontrollhistorikk** og **Hendelser og godkjenninger** viser ulike deler av historikken. Bruk historikken som hører til registreringen du undersøker. Ved korreksjon oppgis begrunnelse; opprinnelig og senere grunnlag bevares.

### 20.2 Dashboard

1. Åpne **Oversikt** og velg periode fra/til. Trykk **Søk** for å oppdatere tallene.
2. Klikk nøkkeltall for aktive forhold, utløp, frister, oppgaver, manglende planer eller avvik for å se underliggende utvalg.
3. Åpne rammeutnyttelse, planlagte betalinger eller fakturakontroll fra sammendragskortene.
4. Kontroller valuta, netto-/bruttogrunnlag og antall rader med ufullstendig grunnlag før du bruker summene.

Periodevalget gjelder frister/utløp og planlagt forfall. Rammeutnyttelse og samlet fakturakontroll viser registrert grunnlag på lesetidspunktet. Null i et varselkort har nøytral farge. «Ingen rader i valgt utvalg» betyr ingen treff i utvalget, ikke nødvendigvis at kontoen mangler slike registreringer.

### 20.3 Rapporter og eksport

1. Åpne **Rapporter** og velg rapporttype.
2. Velg hvilken dato periodefilteret gjelder, og registrer fra-/til-dato ved behov.
3. Åpne **Flere filtre** for finansieringsselskap, ansvarlig, valuta, søk, status og relevante ekstra valg. Dimensjonsrapporten har dimensjonsfilter.
4. Velg **Søk**.
5. Kontroller rapportens genereringstidspunkt, filtre, summer og merking av ufullstendig grunnlag.
6. Bruk **Vis alle kolonner** hvis standardkolonnene ikke viser detaljen du trenger.
7. Velg **CSV** eller **XLSX** med eksportrettighet. Eksporten følger sist utførte rapportutvalg; endrede filterfelt må først brukes med **Søk**. Alle eksportkolonnene beholdes selv om skjermen viser et kortere standardutvalg.

| Rapport | Bruk den til |
| --- | --- |
| Avtaleoversikt | Se rammeavtaler/anskaffelser, ansvarlig, finansieringsselskap, datoer, frister og status |
| Rammeutnyttelse | Kontrollere maks, benyttet, reservert og tilgjengelig per valuta og mva-grunnlag |
| Anskaffelser og dimensjoner | Undersøke kjøpsverdier og bevarte klassifiseringer/fordelingsrader |
| Planlagte betalinger | Se forventede terminer fra aktiv plan, forfall, planversjon og vurderingsbehov |
| Fakturakontroll | Sammenholde forventet og netto fakturert, avvik og uallokert rest |
| Utstyr og avslutning | Se enheter/varelinjer, serienumre, plassering, hendelser og avslutningsopplysninger |

Velg en datotype rapporten faktisk har. Rader uten valgt dato faller utenfor et avgrenset datointervall. Historiske kreditbeløp uten dokumentert dimensjonsfordeling vises som ufordelt; ingen fordeling konstrueres.

Listene er paginerte. Rapportgrunnlaget har grenser på 10 000 anskaffelser/rammer og 100 000 rader; avgrens uttrekket ved for stort utvalg. Valutaer summeres separat. Rapportene viser ikke historiske rammesaldoer rekonstruert fra dagens tall, faktisk betalte beløp eller automatisk beregnet restgjeld.

## 21. Et sammenhengende eksempel

Anta en ramme på **500 000 NOK eksklusive mva**, og en bestilling på **10 PC-er à 10 000 NOK eksklusive mva**. Mva er 25 %. Ingen andre kjøp eller reservasjoner finnes i eksemplet.

| Steg | Registrering/handling | Benyttet ramme | Reservert | Tilgjengelig |
| --- | --- | ---: | ---: | ---: |
| 1 | Opprett åpen rammeavtale | 0 | 0 | 500 000 |
| 2 | Lagre bestillingen som utkast | 0 | 0 | 500 000 |
| 3 | Send inn og godkjenn bestillingen | 0 | 100 000 | 400 000 |
| 4 | Registrer første anskaffelse: 4 PC-er til avtalt pris | 40 000 | 60 000 | 400 000 |
| 5 | Match leverandørfaktura på disse 4 mot eksisterende kjøpslinje | 40 000 | 60 000 | 400 000 |
| 6 | Registrer fire enheter med serienumre | 40 000 | 60 000 | 400 000 |
| 7 | Registrer andre anskaffelse: siste 6 PC-er til avtalt pris | 100 000 | 0 | 400 000 |

Den første anskaffelsen har kjøpsverdi 40 000 netto / 50 000 brutto og sin egen kjøpsdato. Den andre får sin egen dato og leasingperiode. Eventuelt finansiert beløp registreres separat.

Etter første levering:

1. Åpne første anskaffelse, kontroller klassifisering og last opp avtaledokumentasjon.
2. Behandle kjøpsfakturaen gjennom Fakturaimport og match de fire PC-ene mot den eksisterende linjen. Ikke opprett samme kjøpslinje en gang til.
3. Under Utstyr aktiverer du enhetsregistrering på linjen og registrerer de fire serienumrene med intern-ID-er.
4. Registrer avtalte finansieringsvilkår med kjent virkningsdato og betalingsplanen fra finansieringsselskapet. Lagre utkast og få planen aktivert.
5. Når første leasingfaktura kommer, registrerer du den under Leasingfakturaer og fordeler netto/mva til riktig termin.
6. Fullfør fakturakontrollen når all fakturering for terminen er mottatt. Dette påvirker ikke tabellens rammetall.
7. Registrer oppsigelses- og returfrist og ansvarlig, og aktiver ønsket varsling.
8. Ved slutten av perioden registrerer du faktisk retur, utkjøp eller forlengelse. Behandle planendringer og sluttfakturaer separat før dokumentkontrollen fullføres.

Hvis første levering i stedet koster 42 000 netto, frigis fortsatt godkjent reservasjon på 40 000 for de fire enhetene, mens 42 000 blir benyttet. Med 60 000 i restreservasjon blir tilgjengelig ramme 398 000. Dette er grunnen til at prisavvik og rammeeffekt må kontrolleres ved realisering.

## 22. Vanlige stoppunkter og kontrolliste

| Situasjon | Hva du kontrollerer |
| --- | --- |
| Bestilling kan ikke lagres | Er rammeavtale valgt, og er leverandør, ansvarlig og linjer gyldige? |
| Bestilling kan ikke godkjennes | Er perioden åpen nå, finnes tilstrekkelig ramme og har du godkjenningsrettighet? |
| Levering avvises | Er antall/andel innen godkjent rest, er linjekobling riktig og er prisavvik bekreftet? |
| Registrering stoppes av dimensjoner | Har hver fordelingsrad nødvendige verdier og stemmer sum prosent/beløp/antall? |
| Utstyr kan ikke lagres | Er tellbar varelinje valgt, beskrivelse/dato gyldig, antallet ledig og intern-ID unik? |
| Mulig serienummerduplikat | Undersøk eksisterende enheter; bekreft bare et legitimt duplikat |
| Faktura gir dobbel kjøpsverdi | Kontroller om linjen skulle vært matchet til eksisterende kjøp, ikke opprettet som ny |
| Kreditnota stopper | Finn godkjent original og kontroller beløpsgrenser og eventuell regel om frigjøring av ramme |
| Betalingsplan kan ikke aktiveres | Kontroller beløp, datoer, referanser, kilde, vurderingsbehov og godkjenningsrettighet |
| En termin viser fortsatt delvis fakturert | Kontroller allokeringene og om fakturakontrollen er uttrykkelig fullført |
| Varsler kommer ikke | Kontroller aktiveringstidspunkt, frist/terskel, tidssone, ansvarlig og mottakertilgang |
| Avslutning blokkeres | Les avslutningskontrollen, kontroller dokumentasjon, dato og håndtering av åpne forhold |
| Rapporten mangler rader | Kontroller konto, tilgang, datoens betydning og alle aktive filtre |
| Data er endret av en annen bruker | Last inn gjeldende grunnlag og vurder endringen på nytt før lagring/godkjenning |

For en vanlig leveranse er følgende kontrolliste et nyttig sluttpunkt:

- Riktig ramme eller bevisst valg av bestillingsleasing.
- Eventuell bestilling godkjent og realisert i riktig omfang.
- Faktisk kjøpsdato, kjøpsverdi, finansiert beløp og vilkår kontrollert.
- Dimensjoner og kostnadsfordeling fullført.
- Leverandørfaktura dokumentert uten dobbelttelling av kjøpet.
- Enheter/serienumre registrert hvis individuell oppfølging er nødvendig.
- Avtalt betalingsplan aktivert og finansieringsgrunnlag dokumentert.
- Leasingfakturaer allokert og fakturakontroll fulgt opp.
- Frister, ansvarlig og eventuelle varsler registrert.
- Senere retur, forlengelse og avslutning behandlet med separate godkjenninger.

## Videre dokumentasjon og kildegrunnlag

Denne veiledningen er kontrollert mot skjermbildene under [Components/Pages/Leasing](../src/TenantPlatform.Web/Components/Pages/Leasing) og tjenestene under [Services/Leasing](../src/TenantPlatform.Web/Services/Leasing). Den beskriver nåværende funksjonalitet, ikke en plan for fremtidige funksjoner.

Mer detaljerte regler og tekniske avgrensninger finnes i:

- [Bestillinger og reservasjoner](leasing-orders.md)
- [Fakturaimport for kjøpet](leasing-invoice-import.md)
- [Betalingsplaner og leasingfakturaer](leasing-payments.md)
- [Livsløp, utstyr, varsling og rapporter](leasing-lifecycle.md)
