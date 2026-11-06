# FLEsync – Bestandssynchronisatie (Normec Group)

> **IT Glue-document** · Categorie: Applicaties / Interne tools
> Eigenaar: _[naam / team invullen]_ · Laatst bijgewerkt: _[datum]_ · Versie: _[bijv. 1.0.x]_

---

## 1. Samenvatting

FLEsync is een interne Windows-applicatie van Normec Group die **twee onafhankelijke synchronisaties** tussen
bestandslocaties uitvoert. Elke synchronisatie heeft een eigen bron (A), doel (B), richting en schema.

- **Tweerichtings (A ⇄ B):** nieuwe, gewijzigde en verwijderde bestanden worden in beide richtingen doorgevoerd.
- **One-way (A → B):** alleen van A naar B. Optioneel **spiegelen**: wat uit A verdwijnt, wordt ook uit B verwijderd.
- **Afleverstand (one-way):** voor een doelmap waar een machine bestanden oppakt en verwijdert. Elk bestand
  wordt maar één keer afgeleverd; verwerkte bestanden worden niet opnieuw gekopieerd.
- **Optioneel: bron leegmaken als het doel leeg is.** Wordt B leeggemaakt, dan wordt A ook leeggemaakt.
- **Schema per synchronisatie:** elke *N* minuten (1 – 10080), of dagelijks op een vast tijdstip.
- De synchronisaties worden uitgevoerd door de Windows-service **FLEsync**. Daarnaast kan vanuit de app
  handmatig worden gesynchroniseerd.

---

## 2. Componenten

| Onderdeel | Bestand / naam | Functie |
| --- | --- | --- |
| Instelscherm | `C:\Program Files\FLEsync\FLEsync.exe` | Instellingen beheren, handmatig syncen, service installeren/beheren |
| Windows-service | `C:\Program Files\FLEsync\FLEsync.Service.exe` | Voert de synchronisaties uit volgens schema |
| Service-naam | `FLEsync` (weergavenaam **FLEsync Synchronisatie**) | |
| Installer | `FLEsync-<versie>-x64.msi` | Installatie per computer (perMachine), 64-bit |

- Gebouwd op .NET 8, **self-contained**: er hoeft geen aparte .NET-runtime geïnstalleerd te zijn.
- Ondersteund: Windows 10/11 en Windows Server (x64).

---

## 3. Locaties op het systeem

### Programma
- Installatiemap: `C:\Program Files\FLEsync\`
- Snelkoppelingen: Startmenu (alle gebruikers) en bureaublad (alle gebruikers), naam **FLEsync**

### Gegevens (gedeeld door app en service): `C:\ProgramData\FLEsync\`

| Bestand | Inhoud |
| --- | --- |
| `settings.json` | Instellingen van beide synchronisaties |
| `state-1.json`, `state-2.json` | Staat van de vorige run per synchronisatie: bestanden die aan beide kanten gelijk waren. Nodig om verwijderingen te herkennen en voor "bron leegmaken als doel leeg is" |
| `status-1.json`, `status-2.json` | Laatste resultaat per synchronisatie (tijd, gelukt/mislukt, samenvatting, uitgevoerd door) |
| `delivered-1.json`, `delivered-2.json` | Afleverregister (alleen bij afleverstand): welke bestanden al naar B zijn afgeleverd, met grootte en wijzigingstijd |
| `sync-1.lock`, `sync-2.lock` | Lock-bestand; voorkomt dat app en service dezelfde synchronisatie tegelijk uitvoeren |
| `logs\flesync-JJJJMMDD.log` | Dagelijks logbestand. Logs ouder dan 30 dagen worden opgeruimd bij het starten van de service |

**Rechten op `C:\ProgramData\FLEsync`** (gezet door de MSI): SYSTEM = volledig beheer,
Administrators = volledig beheer, **Users = wijzigen**. Daardoor kan een gewone gebruiker de instellingen
opslaan en kan de service (als SYSTEM) ze lezen.

### Register: `HKLM\SOFTWARE\Normec Group\FLEsync`

| Waarde | Inhoud |
| --- | --- |
| `InstallDir` | Installatiemap |
| `DataDir` | Gegevensmap |
| `StartMenuShortcut`, `DesktopShortcut` | Interne markeringen van de installer |

### MSI
- **UpgradeCode:** `{6C2B8E1A-3F47-4D3B-9B0E-5A1F7C2D9E41}`
- Fabrikant: Normec Group · Productnaam: FLEsync

---

## 4. Installatie

### Handmatig
1. Start `FLEsync-<versie>-x64.msi` en doorloop de (Nederlandstalige) wizard.
2. Laat op het laatste scherm **"FLEsync nu starten"** aangevinkt; het instelscherm opent.
3. Stel de synchronisatie(s) in (zie §5) en klik **Opslaan**.
4. Klik **Als service instellen** en bevestig de UAC-melding (beheerdersrechten nodig).

### Stil / via RMM of Intune
```
msiexec /i "FLEsync-<versie>-x64.msi" /qn /l*v "%TEMP%\FLEsync-install.log"
```
**Let op:** de MSI installeert de service **niet** zelf. Doe dit met de app (knop) of via de opdrachtregel
(als administrator / SYSTEM):
```
"C:\Program Files\FLEsync\FLEsync.exe" --install-service
```
Werkwijze voor uitrol zonder gebruikersinteractie:
1. MSI stil installeren.
2. Een vooraf gemaakt `settings.json` (zie §5.3) kopiëren naar `C:\ProgramData\FLEsync\`.
3. `FLEsync.exe --install-service` uitvoeren.

### Opdrachtregelopties van `FLEsync.exe` (vereisen beheerdersrechten)

| Optie | Actie |
| --- | --- |
| `--install-service` | Service aanmaken (indien nodig) en starten |
| `--uninstall-service` | Service stoppen en verwijderen |
| `--start-service` | Service starten |
| `--stop-service` | Service stoppen |
| `--restart-service` | Service stoppen en weer starten |

Exitcodes: `0` = gelukt, `1` = mislukt (details in het logboek), `2` = onbekende optie.

### Service-eigenschappen na installatie
- Opstarttype: **Automatisch (vertraagd starten)**
- Account: **Lokaal systeem (LocalSystem)**
- Herstel bij een fout: na 1 min herstarten, na 1 min herstarten, daarna na 5 min herstarten; de teller wordt na 1 dag gereset

---

## 5. Configuratie

### 5.1 In de app
Het scherm heeft twee tabbladen: **Synchronisatie 1** en **Synchronisatie 2**. Per tabblad:

| Instelling | Toelichting |
| --- | --- |
| *Deze synchronisatie is actief* | Uitgeschakeld = wordt niet uitgevoerd |
| Naam | Vrije naam, bijv. "Archief" (verschijnt in tabblad en logboek) |
| Locatie A / Locatie B | Map of netwerkpad. Bij one-way is A de bron en B het doel |
| Richting | Tweerichtings of One-way (+ optioneel spiegelen) |
| *Alleen nieuwe bestanden afleveren* (alleen bij one-way) | Afleverstand, zie §6. Spiegelen en "bron leegmaken" zijn dan uitgeschakeld |
| *Verwerkte bestanden ook uit A verwijderen* | Bij afleverstand: bestanden die uit B zijn verdwenen (verwerkt) worden ook uit A verwijderd |
| *Huidige bestanden in A markeren als afgeleverd* (link) | Alles wat nu in A staat geldt als afgeleverd; alleen wat hierna binnenkomt gaat naar B |
| *Afleverregister wissen* (link) | Bij de volgende run wordt alles in A opnieuw afgeleverd |
| *Als B (doel) leeg is, ook A (bron) leegmaken* | Standaard uit. Bij inschakelen volgt een bevestigingsvraag. Zie §6 |
| Uitsluiten | Patronen gescheiden door `;`. Standaard: `*.tmp; ~$*; Thumbs.db; desktop.ini` |
| Synchronisatietijd | Elke *N* minuten, of dagelijks om *HH:mm* |

Knoppen: **Opslaan**, **Deze nu synchroniseren** (per tabblad), **Alles nu synchroniseren**,
**Als service instellen** / **Service bijwerken & herstarten**, **Service starten/stoppen**, **Service verwijderen**.
Rechtsboven staat de servicestatus (actief / gestopt / niet ingesteld); onderaan staat het logboek.

Wijzigingen worden binnen **±10 seconden** door een draaiende service overgenomen; een herstart is niet nodig.

### 5.2 Netwerklocaties – belangrijk
- De service draait als **LocalSystem** en ziet **geen gekoppelde stationsletters** (bijv. `Z:\`).
  Gebruik altijd **UNC-paden**: `\\server\share\map`. De app waarschuwt als een gekoppelde schijf wordt gebruikt.
- Als LocalSystem benadert de service shares met het **computeraccount** (`DOMEIN\COMPUTERNAAM$`).
  Geef dat account rechten op de share/NTFS-map, **of** stel een serviceaccount in via
  `services.msc` → *FLEsync Synchronisatie* → tabblad *Aanmelden*. Dat account heeft lees-/schrijfrechten
  op beide locaties nodig, plus wijzigrechten op `C:\ProgramData\FLEsync`.
- Handmatig syncen vanuit de app gebeurt met de rechten van de **ingelogde gebruiker**, niet met die van de service.

### 5.3 Voorbeeld `settings.json`
```json
{
  "Jobs": [
    {
      "Id": 1,
      "Name": "Projecten",
      "Enabled": true,
      "SourcePath": "D:\\Data\\Projecten",
      "TargetPath": "\\\\fileserver01\\projecten$",
      "Mode": "TwoWay",
      "MirrorDeletes": false,
      "EmptySourceWhenTargetEmpty": false,
      "Schedule": "Interval",
      "IntervalMinutes": 15,
      "DailyTime": "02:00",
      "ExcludePatterns": [ "*.tmp", "~$*", "Thumbs.db", "desktop.ini" ]
    },
    {
      "Id": 2,
      "Name": "Archief",
      "Enabled": true,
      "SourcePath": "\\\\fileserver01\\archief",
      "TargetPath": "\\\\backup01\\archief-kopie",
      "Mode": "OneWay",
      "MirrorDeletes": true,
      "EmptySourceWhenTargetEmpty": false,
      "DeliverNewOnly": true,
      "DeleteProcessedFromSource": false,
      "Schedule": "Daily",
      "IntervalMinutes": 15,
      "DailyTime": "23:30",
      "ExcludePatterns": [ "*.tmp" ]
    }
  ]
}
```
Waarden: `Mode` = `TwoWay` | `OneWay`; `Schedule` = `Interval` | `Daily`; `DailyTime` in `HH:mm`.
In JSON moeten backslashes dubbel worden geschreven (`\\`).

---

## 6. Hoe de synchronisatie werkt

### Vergelijken
- Bestanden worden vergeleken op **grootte + laatst-gewijzigd-tijd** (2 seconden tolerantie voor FAT/SMB),
  niet op inhoud of hash.
- Bij het kopiëren blijft de wijzigingstijd van het bronbestand behouden. Er wordt eerst naar
  `<bestand>.flesync.tmp` gekopieerd en daarna vervangen, zodat er geen half gekopieerde bestanden achterblijven.
- Verborgen bestanden worden meegenomen. **Symbolische koppelingen en junctions worden overgeslagen.**
- Lege mappen worden ook gesynchroniseerd.

### Tweerichtings
| Situatie | Actie |
| --- | --- |
| Alleen aan één kant gewijzigd | Kopiëren naar de andere kant |
| Nieuw aan één kant | Kopiëren naar de andere kant |
| Aan één kant verwijderd (en aan de andere kant ongewijzigd sinds de vorige sync) | Ook aan de andere kant verwijderen |
| Aan één kant verwijderd, aan de andere kant gewijzigd | Wijziging wint: bestand wordt teruggezet |
| **Conflict:** aan beide kanten gewijzigd | Nieuwste versie wint; de oudere wordt bewaard als `naam (conflict COMPUTERNAAM JJJJMMDD-UUMMSS).ext` |

**Eerste run:** omdat er nog geen staat is, worden beide kanten samengevoegd. Er wordt dan niets verwijderd.
Bestanden die aan beide kanten bestaan maar verschillen, worden als conflict behandeld (met conflict-kopie).

### One-way
- Nieuwe en gewijzigde bestanden gaan van A naar B. Wijzigingen in B worden bij de volgende run overschreven
  door de versie uit A als die verschilt.
- **Zonder spiegelen:** in B wordt nooit iets verwijderd.
- **Met spiegelen:** bestanden en mappen in B die niet in A staan, worden verwijderd (uitgesloten patronen niet).

### Afleverstand (one-way, "alleen nieuwe bestanden")
Bedoeld voor een doelmap B waar een **machine of applicatie bestanden oppakt en daarna verwijdert**. In de gewone
one-way stand zou FLEsync zo'n verwijderd bestand bij de volgende run opnieuw kopiëren, waardoor de machine het
nog eens verwerkt. De afleverstand voorkomt dat.

| Situatie | Actie |
| --- | --- |
| Nieuw bestand in A | Afleveren naar B en vastleggen in het afleverregister (`delivered-N.json`) |
| Afgeleverd, staat nog in B | Niets (wacht op verwerking) |
| Afgeleverd, uit B verdwenen (verwerkt) | **Niet opnieuw kopiëren.** Met *Verwerkte bestanden ook uit A verwijderen*: ook uit A verwijderen (lege submappen in A worden opgeruimd) |
| Bestand in A gewijzigd (andere grootte of wijzigingstijd) | Geldt als nieuw: wordt opnieuw afgeleverd |
| Bestand uit A verwijderd | Wordt uit het register gehaald. Verschijnt later een bestand met dezelfde naam, dan wordt het afgeleverd |
| Bestand in A minder dan 5 seconden geleden gewijzigd | Wacht tot de volgende run (wordt mogelijk nog geschreven) |

- Afleveren is atomair: eerst `<bestand>.flesync.tmp`, daarna hernoemen naar de echte naam. De machine ziet het
  bestand dus pas als het compleet is. **Let op:** de machine moet `*.flesync.tmp`-bestanden negeren als die
  op alle bestanden in de map reageert.
- **Overstappen van gewone one-way naar afleverstand:** bestanden die bij de vorige one-way sync al in B stonden,
  gelden automatisch als afgeleverd. Zijn er toch bestanden in A die al verwerkt zijn, gebruik dan
  *Huidige bestanden in A markeren als afgeleverd* voordat de service de volgende run doet.
- **Veiligheid:** is B niet bereikbaar terwijl er al eerder is afgeleverd, dan stopt de sync. Er wordt dan niets
  uit A verwijderd en niets dubbel afgeleverd. Is het afleverregister onleesbaar, dan stopt de sync ook.
- Het register hoort bij de combinatie bron + doel. Wordt een van de paden gewijzigd, dan begint een nieuw register
  en wordt alles in A opnieuw afgeleverd. Markeer in dat geval eerst de huidige bestanden als afgeleverd.

### Optie: bron leegmaken als het doel leeg is
Werkt bij tweerichtings en bij one-way, **niet** in de afleverstand. Staat standaard **uit**.
Voor een map die door een machine wordt leeggehaald is de afleverstand met *Verwerkte bestanden ook uit A
verwijderen* de veiligere keuze: die verwijdert alleen bestanden die echt zijn afgeleverd en verwerkt, niet
bestanden die net in A zijn binnengekomen.

Als B (doel) **geen bestanden** meer bevat, worden in A (bron) alle bestanden en mappen verwijderd, behalve
uitgesloten patronen. De bestanden gaan **niet** naar de prullenbak. Dit gebeurt alleen als **alle** voorwaarden gelden:
1. De optie staat aan voor deze synchronisatie.
2. Map B bestaat en is bereikbaar. Is B (of de share) niet bereikbaar, dan wordt de sync afgebroken en blijft A intact.
3. Bij de vorige sync waren er wél gesynchroniseerde bestanden (`state-N.json` niet leeg). **Bij een eerste sync
   wordt A dus nooit leeggemaakt.**

Logregel: `[WARN] [Naam] Doel … is leeg; bron … wordt leeggemaakt …`. Resultaat in de app:
"Doel was leeg: bron leeggemaakt (… bestand(en) verwijderd)". Daarna werkt de synchronisatie weer normaal:
nieuwe bestanden in A gaan weer naar B.

Staat de optie **uit**, dan geldt: bij tweerichtings wordt de sync afgebroken (zie hieronder), bij one-way
worden de bestanden opnieuw van A naar B gekopieerd.

### Veiligheidsmaatregelen
- **Afbreken bij een onverwacht lege kant (tweerichtings, tenzij de optie hierboven aan staat en B de lege kant is):**
  als een locatie ineens geen bestanden meer heeft
  terwijl er bij de vorige sync wel bestanden waren, wordt de sync afgebroken. Zo wordt niet alles gewist als
  bijvoorbeeld een share niet bereikbaar is.
  *Gevolg:* wie bewust een map volledig leegmaakt, ziet die verwijdering niet doorgezet worden.
- **Spiegelen afgebroken** als bron A leeg is terwijl doel B bestanden bevat.
- Onbereikbare locatie → sync wordt afgebroken. Uitzondering: een map die nog niet bestaat, wordt aangemaakt
  als er nog niet eerder gesynchroniseerd is (en bij one-way altijd voor het doel).
- Bron en doel mogen niet gelijk zijn en niet in elkaar liggen.
- App en service voeren dezelfde synchronisatie nooit tegelijk uit (lock-bestand). Synchronisatie 1 en 2
  kunnen wel tegelijk lopen.
- Bij het opslaan waarschuwt de app als beide synchronisaties dezelfde of in elkaar liggende mappen gebruiken.

### Fouten per bestand
Een bestand dat in gebruik of geblokkeerd is, wordt overgeslagen en gelogd als `Mislukt: …`. Bij de volgende run
wordt het opnieuw geprobeerd. De rest van de sync gaat door; het resultaat telt dan als "met problemen".

---

## 7. Beheer en controle

### Status controleren
- **App:** servicestatus rechtsboven; per tabblad de laatste sync (✓ of ⚠ met samenvatting) en de volgende geplande sync.
- **PowerShell:**
  ```powershell
  Get-Service FLEsync
  Get-Content "C:\ProgramData\FLEsync\status-1.json"
  Get-Content "C:\ProgramData\FLEsync\logs\flesync-$(Get-Date -f yyyyMMdd).log" -Tail 50
  ```

### Logboek lezen
Formaat: `JJJJ-MM-DD UU:MM:SS [NIVEAU] [Naam synchronisatie] bericht`. Niveaus: `INFO`, `WARN`, `FOUT`.
Voorbeeld van een geslaagde regel:
```
2026-11-06 14:15:02 [INFO] [Projecten] Synchronisatie klaar: 3 naar doel, 1 naar bron, 0 verwijderd, 0 conflict(en), 0 fout(en) in 1,2s
```

### Bewaking (RMM-suggesties)
- Service `FLEsync` moet de status *Running* hebben.
- `status-N.json` → `"Success": false`, of `LastRun` ouder dan verwacht (bijv. 2× het interval) → melding.
- Logbestand bevat `[FOUT]` → melding.

---

## 8. Probleemoplossing

| Symptoom | Oorzaak / oplossing |
| --- | --- |
| "Bronlocatie niet bereikbaar" / "Doellocatie niet bereikbaar" | Pad bestaat niet of geen rechten. Controleer het UNC-pad en de rechten van het service-account (computeraccount bij LocalSystem) |
| Werkt handmatig wel, via de service niet | Gekoppelde stationsletter gebruikt, of het computeraccount heeft geen rechten → UNC-pad en rechten aanpassen of serviceaccount instellen (§5.2) |
| "Locatie … is onverwacht leeg; afgebroken…" | Een kant is leeg terwijl er eerder bestanden waren. Controleer de bereikbaarheid. Was het leegmaken van B bewust en moet A ook leeg? Zet de optie "Als B (doel) leeg is, ook A (bron) leegmaken" aan. Anders: zie hieronder "Staat resetten" |
| Machine verwerkt bestanden dubbel | Afleverstand staat niet aan, of de paden zijn gewijzigd (nieuw register). Zet *Alleen nieuwe bestanden afleveren* aan en markeer de huidige bestanden als afgeleverd |
| Bestand wordt niet afgeleverd | Staat het al in het afleverregister (eerder afgeleverd)? Wijzig het bestand, verwijder de regel uit `delivered-N.json`, of wis het register. Recent gewijzigde bestanden wachten tot de volgende run |
| Bron is onverwacht leeggemaakt | De optie "bron leegmaken als doel leeg is" staat aan en B was leeg. Controleer het logboek (`WARN … wordt leeggemaakt`) en zet bestanden terug vanuit de back-up |
| "Deze synchronisatie loopt al." | Er loopt al een run (app of service). Wachten. Blijft het hangen: service herstarten |
| Veel `Mislukt: kopiëren …` | Bestanden in gebruik (bijv. Outlook-PST, databases) of te weinig rechten. Uitsluiten of buiten werktijd synchroniseren |
| Conflict-bestanden verschijnen | Bestand is aan beide kanten gewijzigd tussen twee runs. Gebruiker kiest de juiste versie en verwijdert de conflict-kopie |
| Knop "Als service instellen" doet niets / fout | UAC geweigerd of geen beheerdersrechten. Als admin uitvoeren: `FLEsync.exe --install-service`. Controleer of `FLEsync.Service.exe` aanwezig is |
| Instellingen opslaan mislukt | Geen schrijfrechten op `C:\ProgramData\FLEsync` → MSI repareren of rechten herstellen (§3) |
| Service start niet | Logboek en Windows Logboeken → *Toepassing* bekijken; `sc qc FLEsync` controleren; MSI repareren |

### Staat resetten (bewust opnieuw beginnen)
De volgende run wordt dan als eerste run uitgevoerd: samenvoegen, niets verwijderen, en de bron wordt niet
leeggemaakt.
1. Service stoppen.
2. `C:\ProgramData\FLEsync\state-N.json` verwijderen (N = 1 of 2).
3. Service starten.

---

## 9. Bijwerken en verwijderen

- **Bijwerken:** nieuwe MSI-versie installeren (major upgrade). De service wordt tijdens de installatie gestopt en
  daarna automatisch weer gestart. Instellingen blijven behouden. Downgraden wordt geweigerd.
- **Verwijderen:** via *Apps en onderdelen* of `msiexec /x "FLEsync-<versie>-x64.msi" /qn`. De service wordt
  daarbij gestopt en verwijderd. **`C:\ProgramData\FLEsync` (instellingen, staat en logs) blijft staan** en kan
  handmatig worden verwijderd.
- Alleen de service verwijderen: knop *Service verwijderen* of `FLEsync.exe --uninstall-service`.

---

## 10. Broncode en build

- Repository: `akosti0578/flesync` (GitHub)
- Build: `.\build.ps1 -Version x.y.z` op Windows met de .NET 8 SDK → `out\FLEsync-x.y.z-x64.msi`
- CI: GitHub Actions bouwt bij elke push een MSI; een tag `vX.Y.Z` maakt een GitHub Release met de MSI.

---

## 11. Gerelateerde IT Glue-items (invullen)

| Item | Koppeling |
| --- | --- |
| Configuraties (servers/werkplekken met FLEsync) | _[koppelen]_ |
| Gebruikte shares / locaties | _[koppelen]_ |
| Serviceaccount (indien gebruikt) – wachtwoord in IT Glue Passwords | _[koppelen]_ |
| Contactpersoon / applicatie-eigenaar | _[koppelen]_ |
