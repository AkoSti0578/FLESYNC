# Prompt: FLEsync herbouwen in een andere omgeving

Deze prompt is voor een AI-programmeerassistent (bijvoorbeeld Claude Code, GitHub Copilot of ChatGPT) of een
ontwikkelaar die FLEsync opnieuw moet bouwen: in een andere repository, organisatie of tenant, of zonder toegang
tot de originele broncode. De specificatie beschrijft het gedrag van FLEsync zoals het nu is.

Gebruik:
1. Vul het blok **DOELOMGEVING** in.
2. Kopieer alles tussen de twee lijnen in de assistent.
3. Laat de assistent eerst het plan geven (stap 0) en keur dat goed voordat er code komt.

---

Je bent een senior .NET-ontwikkelaar. Bouw de Windows-applicatie **FLEsync** opnieuw volgens de specificatie
hieronder. Het resultaat is een werkende broncode-repository met tests, een MSI-installer en een build-pipeline.

## Werkwijze
0. Geef eerst een kort plan: projectstructuur, gekozen versies en welke punten uit DOELOMGEVING je aanpast. Wacht op akkoord.
1. Bouw daarna in deze volgorde: Core (sync-engine + tests), Service, App (UI), Installer, Build/CI, Documentatie.
2. Laat na elke stap de build en de tests draaien en meld de uitkomst eerlijk. Meld ook wat je niet kon testen.
3. Wijk alleen af van de specificatie als DOELOMGEVING dat vraagt of als het technisch niet anders kan; noem elke afwijking expliciet.
4. Alle teksten in de app, logboeken, installer en documentatie zijn in het **Nederlands**.

## DOELOMGEVING (invullen)
- Repository / locatie van de code:
- Bedrijfsnaam en productnaam (standaard: Normec Group / FLEsync):
- Huisstijl (standaard: kleuren hieronder; of eigen kleuren/logo):
- .NET-versie (standaard .NET 8, of nieuwer LTS):
- Build-pipeline (GitHub Actions / Azure DevOps / anders / geen):
- Code signing van exe en MSI (ja + certificaat-/pipelinegegevens / nee):
- Nieuwe MSI UpgradeCode genereren (ja, standaard bij een nieuw product) of de bestaande `{6C2B8E1A-3F47-4D3B-9B0E-5A1F7C2D9E41}` behouden (alleen als dit dezelfde FLEsync vervangt):
- Aantal synchronisaties (standaard 2):
- Overige wensen:

---

## 1. Doel van de applicatie
FLEsync synchroniseert bestanden tussen twee locaties (lokale mappen of netwerkshares). Er zijn **twee
onafhankelijke synchronisaties** ("jobs"), elk met een eigen bron (A), doel (B), wijze, uitsluitingen en schema.
Een **Windows-service** voert ze automatisch uit; een **WinForms-app** dient om in te stellen, handmatig te
synchroniseren en de service te beheren. Installatie gaat met een **MSI**.

## 2. Oplossing en projecten
- `FLEsync.sln`, `Directory.Build.props` (Version, Company, Product, Copyright, Nullable, ImplicitUsings, `EnableWindowsTargeting=true`).
- `src/FLEsync.Core` (net8.0, class library): instellingen, sync-engine, logboek, status. Geen Windows-specifieke API's, zodat de tests ook op Linux draaien. `InternalsVisibleTo` voor het testproject.
- `src/FLEsync.Service` (Worker SDK, net8.0-windows, win-x64, `Microsoft.Extensions.Hosting.WindowsServices`), exe `FLEsync.Service.exe`.
- `src/FLEsync.App` (WinForms, net8.0-windows, win-x64, PerMonitorV2 DPI, manifest `asInvoker`), exe `FLEsync.exe`, referentie `System.ServiceProcess.ServiceController`.
- `tests/FLEsync.Core.Tests` (xUnit, parallelisatie uit).
- `installer/` (WiX Toolset v5 SDK-project, `Package.wxs`), `assets/` (icoon `.ico`, installer-banner 493×58 en dialoogafbeelding 493×312 als `.bmp`, `license.rtf`), `build.ps1`, pipeline, `README.md`.
- App en service worden **self-contained** gepubliceerd naar dezelfde map (geen aparte .NET-installatie nodig).

## 3. Gegevens en bestanden
Gegevensmap: `%ProgramData%\FLEsync` (te overschrijven met omgevingsvariabele `FLESYNC_DATA_DIR`, voor tests).
Alle JSON met enums als tekst, ingesprongen, en atomisch weggeschreven (eerst `.tmp`, dan `File.Move(overwrite)`).

| Bestand | Inhoud |
| --- | --- |
| `settings.json` | `{ "Jobs": [ SyncJob, SyncJob ] }` |
| `state-{id}.json` | Staat: `SourcePath`, `TargetPath`, `Files` (relatief pad → `{Size, LastWriteUtc}`), `Dirs` (relatieve paden) |
| `status-{id}.json` | `LastRun`, `Success`, `Summary`, `RunBy` |
| `delivered-{id}.json` | Afleverregister, zelfde formaat als staat |
| `sync-{id}.lock` | Lock-bestand, geopend met `FileShare.None` tijdens een run |
| `logs\flesync-yyyyMMdd.log` | Logboek |

**SyncJob**: `Id` (1..N), `Name`, `Enabled`, `SourcePath`, `TargetPath`, `Mode` (`TwoWay`|`OneWay`),
`MirrorDeletes`, `EmptySourceWhenTargetEmpty`, `DeliverNewOnly`, `DeleteProcessedFromSource`,
`Schedule` (`Interval`|`Daily`), `IntervalMinutes` (standaard 15, 1–10080), `DailyTime` (`"HH:mm"`, standaard `"02:00"`),
`ExcludePatterns` (standaard `*.tmp`, `~$*`, `Thumbs.db`, `desktop.ini`).
Afgeleid: `DisplayName` (Name of "Synchronisatie {Id}"), `IsConfigured` (bron en doel ingevuld), `IsActive` (Enabled en IsConfigured),
`IsDelivery` (OneWay en DeliverNewOnly), `Describe()` (bijv. "tweerichtings, elke 15 min").

**Instellingen laden**: altijd precies N jobs met Id 1..N (ontbrekende aanvullen; job 1 standaard aan, de rest uit).
Corrupt bestand → standaardwaarden. Een oud formaat zonder `Jobs` (velden van één job op het hoogste niveau) wordt job 1.

**Schema** `GetNextRun(lastRun, now)`:
- Interval: zonder lastRun → nu; anders lastRun + interval (minimaal 1 min).
- Dagelijks: zonder lastRun → vandaag op het tijdstip, of morgen als dat al voorbij is; anders de eerste keer na lastRun op het tijdstip.

**Logboek**: regels `yyyy-MM-dd HH:mm:ss [INFO|WARN|FOUT] [Naam job] bericht`, thread-safe, fouten bij het loggen worden genegeerd.
Opruimen van logs ouder dan 30 dagen bij het starten van de service. De app leest de laatste 300 regels (met `FileShare.ReadWrite`).

## 4. Sync-engine
### Algemeen
- `RunExclusive(job, runBy)`: neemt het lock-bestand van de job (lukt dat niet: resultaat "Deze synchronisatie loopt al."), logt start en einde, voert de run uit, vangt fouten af en schrijft `status-{id}.json`.
- Resultaat: aantallen naar doel, naar bron, verwijderd, conflicten, fouten, wachtend; `Aborted` + reden; `Success` = niet afgebroken en 0 fouten. Samenvattingstekst in het Nederlands.
- Paden normaliseren (volledig pad, zonder afsluitende `\`), vergelijken zonder hoofdlettergevoeligheid.
- **Scannen**: recursief, ontoegankelijke mappen overslaan (loggen), reparse points (symlinks/junctions) overslaan, verborgen bestanden meenemen. Bestanden die eindigen op `.flesync.tmp` en uitgesloten patronen overslaan. Patroon zonder `\` of `/` geldt voor de bestandsnaam, met `\` voor het relatieve pad (`FileSystemName.MatchesSimpleExpression`, hoofdletterongevoelig).
- **Gelijk** = zelfde grootte en wijzigingstijd binnen **2 seconden**.
- **Kopiëren**: map aanmaken, kopiëren naar `<doel>.flesync.tmp`, wijzigingstijd van de bron overnemen, alleen-lezen van het bestaande doel weghalen, hernoemen naar de echte naam (overschrijven), attributen van de bron overnemen.
- **Verwijderen**: alleen-lezen weghalen en verwijderen (geen prullenbak). Mappen alleen verwijderen als ze leeg zijn.
- Fout per bestand (`IOException`, `UnauthorizedAccessException`): loggen als `Mislukt: <actie> <pad> - <melding>`, fouten tellen, doorgaan.

### Validatie vóór elke run
- Bron en doel ingevuld; niet gelijk; niet in elkaar genest → anders afbreken.
- Bron bestaat niet: bij tweerichtings zonder staat aanmaken, anders afbreken ("Bronlocatie niet bereikbaar").
- Doel bestaat niet: afbreken ("Doellocatie niet bereikbaar") als het al eerder gebruikt is, namelijk bij afleveren met een niet-leeg register, of bij tweerichtings / "A leegmaken"-optie met een niet-lege staat. Anders aanmaken.

### Tweerichtings
- Staat laden (alleen geldig als `SourcePath`/`TargetPath` gelijk zijn; job 1 mag een oud `state.json` overnemen). A en B scannen.
- Optie "A leegmaken als B leeg is" (zie hieronder) eerst controleren.
- **Beveiliging**: staat niet leeg en A of B heeft 0 bestanden → afbreken ("Locatie … is onverwacht leeg; afgebroken om massaal verwijderen te voorkomen.").
- Per bestand in A ∪ B:
  - In beide en gelijk → niets. In beide en verschillend: alleen A gewijzigd t.o.v. staat → A naar B; alleen B → B naar A; beide (of geen staat) → **conflict**: nieuwste wint, de verliezende versie eerst kopiëren naar `naam (conflict COMPUTERNAAM yyyyMMdd-HHmmss).ext` in dezelfde map, dan overschrijven, waarschuwing loggen.
  - Alleen aan één kant: staat bevat het en het is gelijk aan de staat → aan de andere kant verwijderd → hier ook verwijderen. Anders (nieuw, of gewijzigd terwijl de andere kant het verwijderde) → kopiëren.
- Mappen: alleen aan één kant en in de staat → daar verwijderen als leeg (diepste eerst); niet in de staat → aan de andere kant aanmaken.
- Nieuwe staat = alle bestanden en mappen die na de run aan beide kanten gelijk zijn; voor bestanden waarvan de actie mislukte blijft de oude staatregel staan.

### One-way
- Staat laden, scannen. Optie "A leegmaken als B leeg is" eerst controleren.
- Spiegelen aan en A heeft geen bestanden en mappen terwijl B bestanden heeft → afbreken.
- Mappen van A in B aanmaken; bestanden van A die in B ontbreken of verschillen kopiëren.
- Spiegelen: bestanden in B die niet in A staan verwijderen; mappen idem (als leeg, diepste eerst).
- Daarna staat opslaan (zelfde regel als bij tweerichtings).

### Optie "A leegmaken als B leeg is" (niet bij afleveren)
Voorwaarden: optie aan, staat niet leeg, B heeft 0 bestanden, map B bestaat, A heeft bestanden of mappen.
Dan: waarschuwing loggen, alle (niet-uitgesloten) bestanden in A verwijderen, daarna mappen als ze leeg zijn
(diepste eerst), staat opslaan. Samenvatting: "Doel was leeg: bron leeggemaakt (N bestand(en) verwijderd, …)".

### Afleveren (one-way met DeliverNewOnly)
Voor een doelmap waar een machine bestanden oppakt en verwijdert.
- Register laden (paden moeten overeenkomen). Bestaat het registerbestand nog niet: de staat van de vorige run overnemen als register (loggen). Onleesbaar register → afbreken ("Afleverregister onleesbaar; afgebroken om dubbel afleveren te voorkomen.").
- Per bestand in A:
  - In register en gelijk aan registerregel: staat het niet meer in B en staat DeleteProcessedFromSource aan → uit A verwijderen, uit register halen, lege bovenliggende mappen in A opruimen. Anders niets.
  - Staat al gelijk in B → alleen registreren.
  - Gewijzigd minder dan **5 seconden** geleden → overslaan, tellen als wachtend.
  - Anders kopiëren naar B en registreren (register elke 50 bestanden tussentijds opslaan).
- Registerregels van bestanden die niet meer in A staan verwijderen. Register altijd opslaan (ook bij een fout).
- Geen mappen aanmaken of verwijderen in B.
- Publieke functies: `MarkAllDelivered(job)` (alle huidige bestanden in A registreren, met lock, geeft het aantal terug) en `ResetDelivered(job)` (leeg register opslaan, zodat alles opnieuw gaat).

## 5. Windows-service
- Servicenaam `FLEsync`. Elke 10 seconden: wijzigingstijd van `settings.json` controleren; bij wijziging jobs opnieuw laden en per job het volgende moment berekenen (laatste run uit `status-{id}.json`, zodat een herstart geen extra sync forceert). Loggen per job: ingesteld + volgende sync, niet ingesteld, of uitgeschakeld.
- Per actieve job die aan de beurt is en niet al loopt: in een eigen taak `RunExclusive(job, "service")` uitvoeren; daarna laatste/volgende run bijwerken en loggen. Jobs lopen onafhankelijk en parallel.
- Bij stoppen maximaal 20 seconden wachten op lopende runs.

## 6. App (WinForms)
### Uiterlijk (huisstijl)
Kleuren: Navy `#0A2240`, NavyLight `#163A63`, Blue `#0062AE` (hover `#00508F`), Accent `#00A9CE` (hover `#0090B0`),
achtergrond `#F3F5F8`, kaart wit, rand `#DDE3EA`, tekst `#1B2A3A`, gedempt `#5E6E80`, succes `#2E9E5B`,
waarschuwing `#E39B1B`, fout `#C8372D`. Lettertype Segoe UI (9,75 pt; kop "FLEsync" 20 pt vet; kaarttitels Segoe UI Semibold 11 pt; logboek Consolas 9 pt).
Platte knoppen: primair (blauw), accent (cyaan), secundair (wit met rand), gevaar (wit met rode tekst en rand).
Kaarten: witte afgeronde panelen met titel en een korte cyaan accentstreep links van de titel.

### Indeling
- **Kopbalk** (navy, cyaan lijn onderaan): "FLEsync", ondertitel "Bestandssynchronisatie · Normec Group", rechts een statuspil met gekleurde stip: "Service: actief / gestopt / niet ingesteld".
- **Scrollbaar middendeel** (belangrijk voor lage resoluties):
  - Tabbladen per job (eigen getekende tabs: naam + statusregel met stip, bijv. "tweerichtings, elke 15 min", "Uitgeschakeld", "Nog niet ingesteld").
  - Per job een editor: vinkje "Deze synchronisatie is actief", Naam; kaart **Locaties** (Locatie A/B met tekstvak en "Bladeren…", hint over UNC-paden, knop "Deze nu synchroniseren" en laatste uitkomst); kaart **Richting** (Tweerichtings / One-way, spiegelen, "Alleen nieuwe bestanden afleveren", "Verwerkte bestanden ook uit A verwijderen", links "Huidige bestanden in A markeren als afgeleverd" en "Afleverregister wissen", rode optie "Als B (doel) leeg is, ook A (bron) leegmaken", tekstvak Uitsluiten); kaart **Synchronisatietijd** (Elke [N] minuten / Dagelijks om [HH:mm], volgende synchronisatie).
  - Kaart **Logboek** (vaste hoogte, laatste regels, scrollt naar het eind).
- **Vaste knoppenbalk onderin** (altijd zichtbaar): Opslaan, Alles nu synchroniseren, Als service instellen / Service bijwerken & herstarten, Service starten/stoppen, Service verwijderen, plus een regel met uitleg over de servicestatus.
- Voettekst met versie en logmap. Minimale venstergrootte 700×480; bij openen passend maken op het scherm. Lange optieteksten breken af.

### Gedrag
- Opties alleen beschikbaar als ze zin hebben: spiegelen en afleveren alleen bij one-way; spiegelen en "A leegmaken" niet bij afleveren; "verwerkte verwijderen" en de twee links alleen bij afleveren; alles uit als de job uit staat.
- "A leegmaken" en "Afleverregister wissen" vragen bevestiging (Nee als standaardknop); "markeren als afgeleverd" vraagt bevestiging en meldt het aantal.
- Opslaan valideert elke job (uitgeschakeld en leeg is geldig; anders bron en doel verplicht en verschillend; waarschuwing als de bron niet bestaat) en springt naar het tabblad met de fout. Waarschuwing (Ja/Nee) als twee actieve jobs dezelfde of geneste mappen gebruiken. Waarschuwing als een pad op een gekoppelde netwerkschijf staat.
- Handmatig synchroniseren: eerst opslaan, dan de gekozen jobs parallel uitvoeren in de achtergrond met `runBy = "handmatig door <gebruiker>"`, resultaat per job in een melding.
- Elke 3 seconden: servicestatus, status per job en logboek verversen. Vraag bij sluiten met niet-opgeslagen wijzigingen.
- Eén exemplaar tegelijk (`Local\FLEsync.App`-mutex).

### Servicebeheer via UAC
`FLEsync.exe` accepteert één argument en sluit dan af met exitcode 0 (gelukt), 1 (mislukt, gelogd) of 2 (onbekend):
`--install-service` (aanmaken als hij niet bestaat, dan starten), `--uninstall-service`, `--start-service`, `--stop-service`, `--restart-service`.
De UI start zichzelf met `Verb = "runas"` voor deze acties (UAC); geannuleerde UAC (Win32-fout 1223) netjes melden.
Aanmaken met `sc.exe`:
- `create FLEsync binPath= "\"<pad>\FLEsync.Service.exe\"" start= delayed-auto DisplayName= "FLEsync Synchronisatie"`
- `description FLEsync "Normec Group FLEsync - synchroniseert bestandslocaties volgens schema."`
- `failure FLEsync reset= 86400 actions= restart/60000/restart/60000/restart/300000`
Starten en stoppen met `ServiceController`, wachten tot de status bereikt is (30 s / 60 s).

## 7. Installer (WiX v5)
- `Package`: naam FLEsync, fabrikant Normec Group, perMachine, taal 1043 met cultuur `nl-NL`, x64, `MajorUpgrade` (downgrade geweigerd), cab ingebed. UpgradeCode volgens DOELOMGEVING.
- Bestanden: alles uit de publicatiemap (`<Files Include="…\**" />`) naar `ProgramFiles64Folder\FLEsync`.
- `CommonAppDataFolder\FLEsync` aanmaken met SDDL `D:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1301bf;;;BU)` (SYSTEM en Administrators volledig, Users wijzigen).
- Register `HKLM\SOFTWARE\Normec Group\FLEsync`: `InstallDir`, `DataDir`.
- `ServiceControl` voor `FLEsync`: stoppen bij installeren en verwijderen, verwijderen bij de-installatie. De MSI maakt de service niet aan (dat doet de app).
- Na installatie (niet bij verwijderen) `sc.exe start FLEsync` als uitgestelde actie met `Return="ignore"`, zodat de service na een upgrade weer draait.
- Snelkoppelingen in Startmenu en op bureaublad. `WixUI_InstallDir` met eigen banner, dialoogafbeelding en licentie; op het laatste scherm vinkje "FLEsync nu starten" (WixShellExec, doel via een eigen actie op `[INSTALLFOLDER]FLEsync.exe` zetten). ICE38, ICE43 en ICE57 onderdrukken.

## 8. Build en pipeline
- `build.ps1 [-Version x.y.z] [-SkipTests]`: tests, `dotnet publish` van App en Service (Release, win-x64, self-contained) naar `publish\FLEsync`, `.pdb` verwijderen, installer bouwen naar `out\FLEsync-<versie>-x64.msi`.
- Pipeline op een Windows-runner: .NET SDK installeren, versie bepalen (tag `vX.Y.Z` of `1.0.<runnummer>`), `build.ps1`, MSI als artefact; bij een tag een release met de MSI.

## 9. Tests (minimaal, allemaal op een tijdelijke map met `FLESYNC_DATA_DIR`)
1. Tweerichtings kopieert nieuwe bestanden en lege mappen beide kanten op.
2. Tweerichtings voert verwijderingen en wijzigingen door (incl. verwijderde map).
3. Conflict: nieuwste wint, oudere als conflict-kopie.
4. Tweerichtings breekt af als een kant ineens leeg is.
5. One-way laat extra bestanden in B staan; spiegelen verwijdert ze (incl. lege map).
6. Uitgesloten bestanden worden overgeslagen; geneste locaties worden geweigerd.
7. Schema: interval en dagelijks (voor en na het tijdstip, na een run).
8. Twee jobs werken onafhankelijk met eigen staat en status.
9. Instellingen opslaan/laden met twee jobs; oud formaat wordt job 1.
10. "A leegmaken" bij tweerichtings en one-way (uitgesloten bestanden blijven, daarna normaal verder); zonder optie: tweerichtings breekt af, one-way kopieert opnieuw; nooit bij eerste sync; ontbrekend doel → afbreken.
11. Afleveren: opgehaalde bestanden niet opnieuw; gewijzigde wel; verwerkte uit A verwijderen (incl. lege submap); markeren en register wissen; overname van eerdere one-way staat; jonge bestanden wachten; onbereikbaar doel verwijdert niets uit A.

## 10. Documentatie
- `README.md`: functies, installeren, netwerkshares (UNC, computeraccount of serviceaccount), bouwen, structuur, huisstijl aanpassen.
- Beheerdocument (voor IT Glue): onderdelen en locaties, installatie (ook stil en via opdrachtregel), configuratie met voorbeeld `settings.json`, werking per wijze, beveiliging, bewaking, probleemoplossing, bijwerken en verwijderen.

## Klaar wanneer
- De oplossing bouwt zonder fouten en alle tests slagen.
- De pipeline levert een installeerbare MSI op.
- Je hebt een lijst gegeven van: wat is getest, wat niet (bijv. UI en MSI alleen op Windows te controleren), en elke afwijking van deze specificatie.

---
