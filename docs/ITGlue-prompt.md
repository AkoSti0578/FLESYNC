# Prompt: IT Glue-documentatie voor FLEsync

Kopieer alles tussen de twee lijnen in een AI-assistent (bijvoorbeeld de AI van IT Glue, ChatGPT of Claude).
Vul eerst het blok **INSTALLATIEGEGEVENS** in. Laat leeg wat je niet weet; de AI zet daar dan `[INVULLEN]`.

---

Je bent een technisch schrijver voor een IT-afdeling. Schrijf documentatie voor **IT Glue** over de applicatie
**FLEsync** van Normec Group. Schrijf in het **Nederlands**, zakelijk en duidelijk, voor servicedesk-medewerkers
en systeembeheerders.

## Regels
- Gebruik **alleen** de feiten hieronder en de ingevulde installatiegegevens. Verzin niets.
- Ontbreekt een gegeven, schrijf dan `[INVULLEN]` en neem het op in de lijst "Open punten" aan het eind.
- Gebruik koppen, korte alinea's, tabellen en genummerde stappen. Zet paden, commando's en bestandsnamen in codeopmaak.
- Gebruik geen marketingtaal en geen emoji.
- Noem wachtwoorden nooit in de tekst; verwijs naar "IT Glue Passwords: [naam]".

## Lever drie onderdelen

### 1. Flexible Asset "Applicatie" – veldwaarden
Een tabel met veld | waarde voor deze velden:
Naam, Leverancier/eigenaar, Versie, Omschrijving (max. 2 zinnen), Type (Windows-applicatie + Windows-service),
Servers/werkplekken, Installatiepad, Gegevensmap, Servicenaam, Serviceaccount, Synchronisaties (per stuk: naam,
bron, doel, wijze, schema), Afhankelijkheden (shares, machines/applicaties die het doel lezen),
Contactpersoon/applicatie-eigenaar, Documentatie-links.

### 2. Document "FLEsync – werking en beheer"
Met deze hoofdstukken:
1. Samenvatting (wat doet het, waar draait het, voor wie)
2. Deze installatie (de ingevulde synchronisaties in een tabel: naam, bron, doel, wijze, schema, opties, doel van de koppeling)
3. Onderdelen en locaties op het systeem
4. Hoe synchroniseren werkt (alleen de wijzen en opties die in deze installatie gebruikt worden uitgebreid; de rest kort)
5. Beheer: status controleren, handmatig synchroniseren, service starten/stoppen
6. Bewaking (wat de RMM moet controleren)
7. Probleemoplossing (tabel: symptoom | oorzaak | oplossing)
8. Installeren, bijwerken en verwijderen
9. Open punten

### 3. Checklist "Nieuwe FLEsync-installatie"
Genummerde stappen van installatie tot controle na de eerste synchronisatie, met afvinkvakjes `[ ]`.

---

## INSTALLATIEGEGEVENS (invullen)

- Klant / afdeling:
- Server(s) of werkplek(ken) waarop FLEsync draait:
- Geïnstalleerde versie:
- Applicatie-eigenaar / contactpersoon:
- Serviceaccount (LocalSystem of naam van account):
- IT Glue Passwords-item van het serviceaccount (indien van toepassing):
- RMM-bewaking ingericht (ja/nee, welke tool):

**Synchronisatie 1**
- Naam:
- Actief (ja/nee):
- Locatie A (bron):
- Locatie B (doel):
- Wijze (tweerichtings / one-way / one-way spiegelen / afleveren / afleveren en A opruimen):
- Schema (elke N minuten / dagelijks om HH:mm):
- Uitsluitingen:
- Optie "A leegmaken als B leeg is" (ja/nee):
- Doel van de koppeling (bijv. "orders naar ERP-importmap"):
- Machine/applicatie die B leest (indien afleveren):

**Synchronisatie 2**
- Naam:
- Actief (ja/nee):
- Locatie A (bron):
- Locatie B (doel):
- Wijze:
- Schema:
- Uitsluitingen:
- Optie "A leegmaken als B leeg is" (ja/nee):
- Doel van de koppeling:
- Machine/applicatie die B leest (indien afleveren):

---

## FEITEN OVER FLESYNC (gebruik alleen deze)

### Algemeen
- Interne Windows-applicatie van Normec Group die bestanden synchroniseert tussen twee locaties.
- Er zijn **twee onafhankelijke synchronisaties** (tabblad 1 en 2), elk met eigen naam, aan/uit, bron (A), doel (B), wijze, uitsluitingen en schema. Ze kunnen tegelijk lopen.
- Een Windows-service voert ze automatisch uit volgens schema; de app kan ook handmatig synchroniseren.
- Gebouwd op .NET 8, self-contained (geen aparte .NET-installatie nodig). Windows 10/11 en Windows Server, x64.
- Bestanden worden vergeleken op grootte en wijzigingstijd (2 seconden tolerantie), niet op inhoud.
- Verborgen bestanden gaan mee; symbolische koppelingen en junctions worden overgeslagen; lege mappen worden meegenomen (behalve bij afleveren).

### Onderdelen en locaties
- App: `C:\Program Files\FLEsync\FLEsync.exe`
- Service: `C:\Program Files\FLEsync\FLEsync.Service.exe`, servicenaam `FLEsync`, weergavenaam "FLEsync Synchronisatie".
- Service: opstarttype Automatisch (vertraagd), account LocalSystem, bij fout herstart na 1 min, 1 min, 5 min (teller reset na 1 dag).
- Snelkoppelingen in Startmenu en op bureaublad (alle gebruikers).
- Gegevensmap `C:\ProgramData\FLEsync\` (rechten: SYSTEM en Administrators volledig, Users wijzigen):
  - `settings.json` – instellingen van beide synchronisaties
  - `state-1.json`, `state-2.json` – bestanden die bij de vorige run aan beide kanten gelijk waren
  - `status-1.json`, `status-2.json` – laatste resultaat (LastRun, Success, Summary, RunBy)
  - `delivered-1.json`, `delivered-2.json` – afleverregister (alleen bij afleveren)
  - `sync-1.lock`, `sync-2.lock` – voorkomt dat app en service dezelfde synchronisatie tegelijk draaien
  - `logs\flesync-JJJJMMDD.log` – dagelijks logboek, ouder dan 30 dagen wordt opgeruimd bij start van de service
- Register: `HKLM\SOFTWARE\Normec Group\FLEsync` (InstallDir, DataDir).
- MSI: `FLEsync-<versie>-x64.msi`, perMachine, UpgradeCode `{6C2B8E1A-3F47-4D3B-9B0E-5A1F7C2D9E41}`.
- Broncode: GitHub-repository `akosti0578/flesync`; build met `build.ps1`; GitHub Actions bouwt de MSI.

### Wijzen van synchroniseren
- **Tweerichtings (A ⇄ B):** nieuw, gewijzigd en verwijderd gaat beide kanten op. Beide kanten gewijzigd: nieuwste wint, oudere blijft als `naam (conflict COMPUTERNAAM JJJJMMDD-UUMMSS).ext`. Verwijderd aan één kant terwijl de andere kant het wijzigde: wijziging wint. Eerste run voegt samen en verwijdert niets.
- **One-way (A → B):** nieuw en gewijzigd van A naar B. Uit A verwijderd blijft in B staan. Wijzigingen in B worden overschreven door A. Uit B verwijderd wordt opnieuw gekopieerd.
- **One-way spiegelen:** zoals one-way, en wat niet in A staat wordt uit B verwijderd (behalve uitgesloten patronen). Is A leeg terwijl B bestanden heeft, dan stopt de sync.
- **Afleveren (one-way, "alleen nieuwe bestanden"):** voor een doelmap waar een machine of applicatie bestanden ophaalt en verwijdert. Elk bestand gaat één keer naar B en wordt vastgelegd in het afleverregister. Uit B verdwenen = verwerkt = niet opnieuw kopiëren. Gewijzigd in A = geldt als nieuw en wordt opnieuw afgeleverd. Bestanden die minder dan 5 seconden geleden zijn gewijzigd wachten tot de volgende run. Bij overstap vanuit gewone one-way gelden bestanden die bij de vorige sync al in B stonden als afgeleverd. Wijzigen van bron- of doelpad start een nieuw register (alles wordt dan opnieuw afgeleverd).
- **Afleveren en A opruimen:** zoals afleveren, en bestanden die uit B zijn verdwenen worden ook uit A verwijderd (alleen echt afgeleverde bestanden; lege submappen in A worden opgeruimd).
- Knoppen bij afleveren: "Huidige bestanden in A markeren als afgeleverd" (alleen wat hierna binnenkomt gaat naar B) en "Afleverregister wissen" (alles gaat opnieuw).

### Opties
- **Schema:** elke N minuten (1–10.080) of dagelijks om HH:mm. Wijzigingen worden binnen ca. 10 seconden door de service overgenomen.
- **Uitsluiten:** patronen gescheiden door `;`, standaard `*.tmp; ~$*; Thumbs.db; desktop.ini`.
- **A leegmaken als B leeg is:** bij tweerichtings en one-way, niet bij afleveren. Alleen als B bereikbaar is en bij de vorige sync bestanden had; nooit bij de eerste sync. Verwijderde bestanden gaan niet naar de prullenbak. Standaard uit.

### Beveiliging
- Tweerichtings: is een kant onverwacht leeg terwijl er eerder bestanden waren, dan stopt de sync (tenzij "A leegmaken als B leeg is" aan staat en B de lege kant is).
- B niet bereikbaar na eerdere synchronisatie: sync stopt bij tweerichtings, bij afleveren en bij one-way met "A leegmaken"-optie. Gewone one-way maakt map B aan.
- Kopiëren gaat via `<bestand>.flesync.tmp` en daarna hernoemen; een machine die B leest moet `*.flesync.tmp` negeren.
- Bron en doel mogen niet gelijk zijn of in elkaar liggen; de app waarschuwt als de twee synchronisaties overlappen.
- Een bestand dat in gebruik is wordt gelogd als `Mislukt:` en de volgende run opnieuw geprobeerd.

### Netwerklocaties
- De service (LocalSystem) ziet geen gekoppelde stationsletters: gebruik UNC-paden `\\server\share\map`. De app waarschuwt hiervoor.
- LocalSystem benadert shares als het computeraccount `DOMEIN\COMPUTERNAAM$`; geef dat rechten, of stel in `services.msc` → FLEsync Synchronisatie → Aanmelden een serviceaccount in (rechten op beide locaties en wijzigrechten op `C:\ProgramData\FLEsync`).
- Handmatig synchroniseren vanuit de app gebeurt met de rechten van de ingelogde gebruiker.

### Bediening van de app
- Kopbalk met servicestatus (actief / gestopt / niet ingesteld).
- Tabbladen Synchronisatie 1 en 2 met de instellingen, "Deze nu synchroniseren" en de laatste uitkomst.
- Vaste knoppenbalk onderin: Opslaan, Alles nu synchroniseren, Als service instellen / Service bijwerken & herstarten, Service starten/stoppen, Service verwijderen.
- Logboek onderaan; het scherm scrolt bij lage resolutie.

### Installeren en beheren
- Handmatig: MSI installeren, op het laatste scherm "FLEsync nu starten", instellen, Opslaan, "Als service instellen" (UAC).
- Stil: `msiexec /i "FLEsync-<versie>-x64.msi" /qn /l*v "%TEMP%\FLEsync-install.log"`. De MSI installeert de service niet zelf.
- Opdrachtregel (als administrator): `FLEsync.exe --install-service | --uninstall-service | --start-service | --stop-service | --restart-service`. Exitcodes: 0 gelukt, 1 mislukt, 2 onbekende optie.
- Uitrol zonder gebruiker: MSI stil installeren, `settings.json` naar `C:\ProgramData\FLEsync\` kopiëren, `FLEsync.exe --install-service` uitvoeren.
- Bijwerken: nieuwe MSI installeren; service wordt gestopt en weer gestart, instellingen blijven. Downgraden wordt geweigerd.
- Verwijderen: Apps en onderdelen of `msiexec /x`; service wordt verwijderd, `C:\ProgramData\FLEsync` blijft staan.
- Staat resetten (volgende run als eerste run): service stoppen, `state-N.json` verwijderen, service starten.

### Controle en bewaking
- PowerShell: `Get-Service FLEsync`, `Get-Content C:\ProgramData\FLEsync\status-1.json`, `Get-Content "C:\ProgramData\FLEsync\logs\flesync-$(Get-Date -f yyyyMMdd).log" -Tail 50`.
- Logformaat: `JJJJ-MM-DD UU:MM:SS [INFO|WARN|FOUT] [Naam synchronisatie] bericht`.
- RMM: service moet Running zijn; `status-N.json` met `"Success": false` of een `LastRun` ouder dan 2× het interval → melding; `[FOUT]` in het logboek → melding.

### Bekende meldingen
- "Bronlocatie niet bereikbaar" / "Doellocatie niet bereikbaar" – pad of rechten.
- "Locatie … is onverwacht leeg; afgebroken…" – beveiliging tegen massaal wissen.
- "Deze synchronisatie loopt al." – er loopt al een run.
- "Doel … is leeg; bron … wordt leeggemaakt" – optie "A leegmaken als B leeg is".
- "Afleverregister onleesbaar; afgebroken…" – `delivered-N.json` beschadigd.
- "Afleverstand ingeschakeld: N eerder gesynchroniseerde bestand(en) gelden als afgeleverd." – overstap naar afleveren.

---
