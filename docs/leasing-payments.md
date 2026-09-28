# Leasing fase 5: betalingsplaner og leasingfakturaer

## Bruk

Åpne **Leasing → Betalingsplaner**, velg anskaffelsen, og registrer en datert vilkårsrevisjon. Opprinnelige opplysninger vises med uavklart virkningsdato. Datovelgeren viser siste revisjon som gjelder fra og med valgt dato. Neste revisjon avslutter forrige gyldighetsperiode; to revisjoner kan ikke starte samme dag. Fremtidige revisjoner er tillatt.

Opprett terminer manuelt, som serie eller gjennom CSV/XLSX-import. Forhåndsvisningen kan redigeres, inkludert første/siste termin og engangsposter. Lagre utkast, sammenlign med aktiv plan og aktiver etter kontroll. Bare aktiv versjon inngår i forventede beløp. Ref./terminnummer normaliseres til store bokstaver og identifiserer samme forpliktelse på tvers av versjoner. Referansen til en videreført termin skal beholdes. Historisk allokerte terminer kan ikke fjernes fra en ny aktiv plan. Endrede fakturerte terminer krever uttrykkelig bekreftelse og ny fakturakontroll.

Beløp kommer fra avtalen eller finansieringsselskapet. Det finnes ingen renteberegning, amortiseringsmotor eller automatisk opprettelse av gebyrer, startleie eller restverdiposter. Restverdi er ikke utkjøpspris eller betalingsplikt. En eksplisitt restverdipost krever dokumentreferanse. Komponenter er spesifikasjon av netto; når fullstendig angitt skal kapital + renter + gebyr = netto, og netto + mva. = brutto. Tomt beløp betyr ukjent og stopper aktivering; eksplisitt null er gyldig.

En vilkårsendring markerer aktive terminer fra virkningsdatoen som **Må vurderes**. Den endrer ingen terminbeløp. Planansvarlig kan dokumentere gjennomført kontroll eller registrere en revidert plan. Hver termin bevarer koblingen til vilkårsrevisjonen som beløpet bygger på. Finansierte beløp og opprinnelige rentevilkår på anskaffelsen bevares; endringer gjøres gjennom daterte revisjoner.

## Import

CSV: UTF-8, gjerne med BOM; semikolon, tabulator eller komma som separator, sitattegn rundt felter ved behov. XLSX: velg regneark, deretter kolonner. Første ikke-tomme rad er overskrifter. Inntil 600 terminer, 100 kolonner og 10 MB, begrenset ytterligere av eksisterende dokumentlagringsgrense.

Obligatorisk mapping: referanse, periode fra, periode til, forfall, netto, mva., brutto, posttype. Valuta kan mappes i tillegg. Kolonnerekkefølgen er valgfri.

```csv
Referanse;Fra;Til;Forfall;Netto;Mva;Brutto;Type;Valuta
1;01.01.2028;31.01.2028;05.02.2028;1 234,56;308,64;1.543,20;leie;NOK
2;01.02.2028;29.02.2028;05.03.2028;1 234,56;308,64;1.543,20;leie;NOK
```

Datoer: `dd.MM.yyyy`, `d.M.yyyy`, `yyyy-MM-dd`, `dd/MM/yyyy`, samt numeriske XLSX-datoer med 1900/1904-datosystem. Beløp: norske desimaler og mellomrom/punktum som tusenskille ved desimalkomma, eller punktum som desimaltegn. Maksimalt to desimaler. Posttyper: `leie`, `forskuddsleie`, `gebyr`, `restverdi`, `annet` (og tilsvarende engelske/svenske verdier i parseren). Restverdiposter må suppleres med dokumentasjon før lagring. Tomme beløp beholdes som ukjente.

Ugyldige datoer/tall, duplikate referanser, feil summer og avvikende valuta vises med radnummer. XLSX-formler avvises, også når de har bufret verdi. Makroer kjøres aldri; filer med VBA avvises. XML leses uten DTD eller eksterne resolvere. PDF kan vedlegges anskaffelsen og velges som dokumentasjon for manuell plan; PDF-tabeller tolkes ikke automatisk.

Serier forankres i første dato, ikke forrige genererte dato. Periodegrenser som starter på månedsslutt fortsetter på månedsslutt. Forfallsdato bevarer opprinnelig dag, begrenset av månedslengden, eller månedsslutt når valgt. 30. januar → 29. februar 2028 → 30. mars. Ingen justering for bankdager/helligdager. Periode utenfor anskaffelsens leasingperiode krever kontrollbegrunnelse; forfall kan lovlig være utenfor.

Identisk planinnhold mot samme aktive grunnlag gjenbruker eksisterende utkast. Reimport mot ny aktiv versjon kan gi nytt utkast, men bevarer terminidentitet og endrer ikke aktiv forventning før aktivering.

## Leasingfakturaer og kontroll

**Leasing → Leasingfakturaer** er en egen dokumentkategori. Registrer manuelt eller last opp XML (UBL/EHF), PDF eller bilde gjennom eksisterende dokumenttolking. Velg finansieringsselskap og valuta, lagre, og kontroller matchforslag. Registrer fakturalinjer og kildens summer. Kreditnotaer bruker positive kildebeløp; dokumenttypen bestemmer fortegn. Manuell behandling fungerer uten ekstern tolking.

Fordel netto og mva. eksplisitt til terminer, eventuelt fra flere anskaffelser med samme finansieringsselskap, tenant og valuta. Dokumentets netto og mva. kan ikke over-allokeres. Uallokert rest er synlig. Matchforslag er ikke økonomiske koblinger før godkjenning. Godkjenning gjelder sist lagrede kontroll. Fakturaens eget forfall og terminens forventede forfall vises separat.

Fakturering er delvis frem til autorisert bruker bekrefter at all fakturering er mottatt. Deretter vises avstemt eller avvik. Toleranse er **0,01 i dokumentets valuta på hver av netto, mva. og brutto**. Avviksaksept krever begrunnelse og endrer ikke forventningen. Nye allokeringer, krediteringer eller planendringer som berører fakturerte terminer åpner kontrollen igjen.

Kreditnota må knyttes til godkjent originalfaktura. Allokert kreditbeløp må knyttes til originalallokeringen for samme termin. Både dokumentnivå og allokeringsnivå beskyttes mot overkreditering. Korrigering reverserer gamle allokeringer og oppretter nye atomisk. Aktive kreditkoblinger må reverseres før originaldokumentet/allokeringene kan reverseres. Dokumenter, opprinnelig godkjenningsgrunnlag og allokeringshistorikk slettes ikke.

Dette er fakturakontroll, ikke bankavstemming. Ingen termin merkes betalt, og ingen gjeldssaldo beregnes. Valutaer summeres separat. Ingen av handlingene endrer varelinjer, dimensjoner, kjøpsverdi, benyttet ramme eller bestillingsreservasjoner.

## Tilgang og drift

- Registrering: kontoadministrator eller ansvarlig for anskaffelsen/rammeavtalen.
- Aktivering og vurdering av plan: `LeasingPlanApprover` eller kontoadministrator.
- Fakturagodkjenning, allokeringskorrigering og reversering: `LeasingInvoiceApprover` eller kontoadministrator.
- Fullfør fakturakontroll og aksepter avvik: `LeasingVarianceApprover` eller kontoadministrator.

Rollene gjelder valgt konto. Tjenestene kontrollerer medlemskap, tenantgrenser og ressurser på serveren. Godkjenninger låser kontoraden i PostgreSQL; revisjonstoken og hendelses-ID beskytter samtidighet og nye forsøk. Opplasting av samme leasingfakturafil gjenbruker synlig eksisterende dokument som ikke er reversert/avvist. Godkjente duplikater kontrolleres også på filhash og finansieringsselskap/dokumenttype/fakturanummer.

Ingen ny ekstern tjeneste eller konfigurasjonsseksjon. Eksisterende `AgreementDocuments` og `LeasingInvoiceInterpretation` (samt eksisterende `AgreementAnalysis`-nøkkel/modell ved ekstern tolking) brukes. Eksisterende bakgrunnsarbeider tolker begge fakturakategorier; leiedokumenter sendes aldri til godkjenning av utstyrskjøp.

Migreringen `AddLeasingPaymentPlans` utvider vilkårsfelter og oppretter revisjoner, planversjoner, stabile terminer, allokeringer og kontrollhistorikk. Gamle anskaffelser får en kopi av opprinnelige vilkår med **NULL virkningsdato, aktør og registreringstidspunkt**; ingen historiske datoer konstrueres. Ingen planer genereres. Tidligere dokumenter og økonomiske verdier bevares. Migreringen er ikke manuelt kjørt mot applikasjonsdatabasen; eksisterende oppstartsrutine anvender ventende migreringer.

## Verifisering og grenser

`dotnet build TenantPlatform.sln` og PostgreSQL-kontrollene i `tests/TenantPlatform.Leasing.SmokeTests` dekker eksisterende faser og fase 5: datoforankring, skuddår, import, historikk, planbytte, godkjenning, delvis matching, kreditnota, reversering, samtidighet, tilgang og uendret ramme/reservasjon. Komponentene rendres med norsk lokalisering i testene. Migrering testes over eksisterende anskaffelser i et isolert, midlertidig databaseskjema.

Store importfiler må deles. XLSX med formler må eksporteres som verdier. Ukjent historisk virkningsdato må avklares manuelt. Automatisk betalingsberegning, rentesatsinnhenting, PDF-tabelltolking, bankbetaling, regnskapsføring og automatisk dimensjonsfordeling er utenfor fasen.
