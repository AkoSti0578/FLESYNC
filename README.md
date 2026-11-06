# FLEsync

**FLEsync** is een Windows-applicatie van Normec Group om twee bestandslocaties met elkaar in sync te houden:
**tweerichtings** (A ⇄ B) of **one-way** (A → B, optioneel spiegelen). De synchronisatie kan als
**Windows-service** op een instelbaar schema draaien.

![icoon](assets/flesync.png)

## Functies

- **Instelscherm na installatie**: de MSI-wizard biedt op het laatste scherm "FLEsync nu starten".
- **Twee locaties kiezen**: lokale mappen of netwerkshares (UNC-paden aanbevolen).
- **Synchronisatierichting**
  - *Tweerichtings*: nieuwe, gewijzigde en verwijderde bestanden worden in beide richtingen doorgevoerd.
    Bij een conflict (beide kanten gewijzigd) wint de nieuwste versie; de oudere wordt bewaard als
    `naam (conflict COMPUTER datum).ext`.
  - *One-way*: alleen van A naar B. Optioneel *spiegelen*: wat uit A verdwijnt, wordt ook uit B verwijderd.
- **Synchronisatietijd**: elke *N* minuten of dagelijks op een vast tijdstip.
- **Knop "Als service instellen"**: installeert en start de Windows-service `FLEsync`
  (vraagt één keer om beheerdersrechten via UAC). Daarna ook starten/stoppen/verwijderen vanuit de app.
- **Nu synchroniseren**: handmatige sync direct vanuit het scherm.
- **Uitsluitingen**: patronen zoals `*.tmp; ~$*; Thumbs.db`.
- **Veiligheid**: sync wordt afgebroken als een locatie onbereikbaar of onverwacht leeg is (voorkomt massaal
  verwijderen), bron en doel mogen niet in elkaar liggen, app en service syncen nooit tegelijk.
- **Logboek** in het scherm en in `%ProgramData%\FLEsync\logs` (30 dagen bewaard).
- **Normec Group-uiterlijk**: marineblauwe header, blauw/cyaan accenten, Segoe UI; ook in de installatiewizard.

## Installeren

1. Download `FLEsync-<versie>-x64.msi` (GitHub Actions-artifact of Release).
2. Installeer; FLEsync komt in `C:\Program Files\FLEsync` met snelkoppelingen in het Startmenu en op het bureaublad.
3. Kies locatie A en B, richting en schema, en klik **Opslaan**.
4. Klik **Als service instellen**. De service start automatisch met Windows (vertraagd automatisch) en pakt
   latere wijzigingen in de instellingen vanzelf op.

Bij verwijderen van FLEsync wordt ook de service verwijderd. Instellingen en logboeken blijven in
`%ProgramData%\FLEsync` staan.

### Netwerkshares

De service draait standaard als *Lokaal systeem*. Die ziet geen gekoppelde stationsletters (`Z:\`) en heeft
op shares de rechten van het computeraccount. Gebruik daarom UNC-paden (`\\server\share\map`) en geef het
computeraccount rechten, of stel in `services.msc` → *FLEsync Synchronisatie* → *Aanmelden* een
(service)account in met toegang tot de share.

## Bouwen

Vereist Windows met de .NET 8 SDK. WiX v5 wordt automatisch via NuGet opgehaald.

```powershell
.\build.ps1 -Version 1.0.0
```

Resultaat: `out\FLEsync-1.0.0-x64.msi`. De app en service worden *self-contained* gepubliceerd, dus op de
doelcomputer is geen losse .NET-installatie nodig.

Elke push bouwt de MSI via GitHub Actions (`.github/workflows/build.yml`); een tag `v1.2.3` maakt een
GitHub Release met de MSI.

## Structuur

| Pad | Inhoud |
| --- | --- |
| `src/FLEsync.Core` | Sync-engine, instellingen (`%ProgramData%\FLEsync\settings.json`), schema, logboek |
| `src/FLEsync.App` | WinForms-instelscherm (`FLEsync.exe`), service-beheer via UAC |
| `src/FLEsync.Service` | Windows-service (`FLEsync.Service.exe`) die volgens schema synchroniseert |
| `installer` | WiX v5 MSI-project (Nederlandstalige wizard in Normec-stijl) |
| `tests` | xUnit-tests voor de sync-engine en het schema |
| `assets` | Icoon, installer-afbeeldingen en licentietekst |

## Huisstijl aanpassen

Alle kleuren en lettertypen staan centraal in `src/FLEsync.App/Theme.cs`. De installer-afbeeldingen
(`assets/banner.bmp` 493×58, `assets/dialog.bmp` 493×312) en `assets/flesync.ico` kunnen vervangen worden
door officiële Normec-brandassets.
