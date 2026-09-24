# Spider-Man: Edge of Time — PC Edition: Installation

> **Zwei Epochen. Ein Schicksal.**

[English](INSTALL_EN.md) · [Русский](INSTALL_RU.md) · [Deutsch](INSTALL_DE.md) · [Français](INSTALL_FR.md) · [Italiano](INSTALL_IT.md) · [Español](INSTALL_ES.md)

## Was muss heruntergeladen werden?

Für die normale Online-Installation wird nur diese Datei benötigt:

- `EOTInstaller-v2.0.0-beta.4.1.exe`

Der Installer lädt `EOT-PC-Payload-v2.0.0-beta.4.1.zip` automatisch herunter, setzt eine unterbrochene Übertragung fort und prüft Größe sowie SHA-256 vor dem Entpacken.

Die übrigen Dateien im Release:

- `EOT-PC-Payload-v2.0.0-beta.4.1.zip` — Komponenten und Patches der PC Edition; für die normale Installation ist kein manueller Download nötig;
- `SHA256SUMS.txt` — Prüfsummen für Installer und Payload;
- `release-manifest.json` — Version, Größen, Hashes und unterstützte Ausgangsversionen;
- `Source code` — Quellcode des Installers, keine spielbare Kopie.

## Normale Installation

1. `EOTInstaller-v2.0.0-beta.4.1.exe` vom [offiziellen Projekt-Release](https://github.com/GenryTheFox0/Project-2099/releases/tag/v2.0.0-beta.4.1) herunterladen.
2. Den Installer starten.
3. Sprache für Installer und Spiel auswählen.
4. Download und Prüfung der PC-Edition-Komponenten abwarten.
5. Eine kompatible eigene Xbox-360-Quelle auswählen:
   - USA/Europa-Retail-XDVDFS-ISO;
   - ein ZIP mit dem Spiel, auch mit einem äußeren Ordner `Spider-Man - Edge of Time (USA Europe)`;
   - entpackter Spielordner mit `Default.xex` und `Data` oder dessen äußerer übergeordneter Ordner;
   - getesteter alternativer russischer GOD/LIVE/XSF über den Ordner `415608B2/00007000`.
6. Einen leeren Zielordner auswählen, zum Beispiel `D:\Games\Project 2099`.
7. INSTALLIEREN drücken und die abschließende Prüfung abwarten.
8. Das installierte Spiel über `Launcher.exe` starten.

Nicht direkt in ein Laufwerksstammverzeichnis wie `D:\` und nicht über eine ältere, bereits gefüllte Installation installieren. Ein eigener leerer Ordner verhindert vermischte Dateien verschiedener Versionen.

Nach erfolgreicher Installation werden ISO, GOD oder der entpackte Konsolenordner nicht mehr benötigt. Die PC Edition läuft vollständig aus dem Zielordner und hängt nicht vom ursprünglichen Laufwerksbuchstaben ab.

## Offline-Installation

1. Beide Dateien herunterladen:
   - `EOTInstaller-v2.0.0-beta.4.1.exe`;
   - `EOT-PC-Payload-v2.0.0-beta.4.1.zip`.
2. Einen eigenen Ordner für den Installer anlegen.
3. Das ZIP kann neben der EXE liegen bleiben; Entpacken ist optional. Danach sieht die Struktur so aus:

```text
EOT GitHub Installer\
├── EOTInstaller-v2.0.0-beta.4.1.exe
└── payload\
    ├── payload-manifest.json
    ├── port\
    └── patches\
```

4. Die EXE starten. Der Installer erkennt und prüft das ZIP oder den lokalen Ordner `payload`, ohne das Archiv erneut herunterzuladen.

`payload` nicht umbenennen und die enthaltenen Dateien nicht aus ihrer Ordnerstruktur herausziehen.

ISO und ZIP können direkt ausgewählt werden. Bei einem Ordner sucht der Installer bis zu zwei Ebenen tiefer nach dem Spielstamm und öffnet auch ein einzelnes ISO/ZIP im gewählten Ordner automatisch.

## Unterstützte Ausgangsversionen

- `usa-europe-retail` — verbreitete USA/Europa-Xbox-360-Retail-Version als ISO/ZIP;
- `usa-europe-retail-r2` — zweite bekannte USA/Europa-Retail-Version;
- `eu-retail` — ältere Originalsprachen-Basis der PC Edition für Kompatibilität;
- `ru-god-alt` — getestete alternative russische GOD/LIVE/XSF-Version;
- `sazanoff-rus-god` — Region Free RUS GOD (SazanOFF v1.0b).

Eine Dateiendung oder ein `LIVE`-, `PIRS`- beziehungsweise `CON`-Header reicht nicht aus. Der Installer prüft echte Dateigrößen und SHA-256-Hashes. Unbekannte, gemischte oder veränderte Versionen werden abgelehnt, statt eine kaputte Installation zu erzeugen.

## Dateien, Einstellungen und Speicherstände

Der fertige Ordner enthält unter anderem `Launcher.exe`, `SpiderManEOT.exe`, `rexruntime.dll`, `spider_man_edge_of_time.toml`, beide Sprachdatenbäume und den Ordner `Support`. Der normale Einstieg ist `Launcher.exe`; zusätzliche BAT-Dateien sind nicht nötig.

Einstellungen und Speicherstände:

```text
%USERPROFILE%\Documents\spider_man_edge_of_time
```

Download-Cache des Installers:

```text
%LOCALAPPDATA%\GenryTheFox\EOTInstaller
```

Nach erfolgreicher Installation benötigt das Spiel diesen Cache nicht. Er kann für eine schnellere Neuinstallation behalten oder zum Freigeben von Speicherplatz gelöscht werden.

## Steuerung

- Tastatur und Maus;
- Bewegung mit WASD;
- einstellbare Mausempfindlichkeit;
- XInput-Controller;
- automatische Anzeige für das aktive Eingabegerät;
- QTE-Anzeigen: **B ↔ E** und **Y ↔ MMB**;
- **MMB bedeutet einen Klick auf das Mausrad**, nicht das Scrollen.

## SmartScreen und Prüfsummen

Der Installer besitzt derzeit kein kommerzielles Codesignatur-Zertifikat. Windows SmartScreen kann deshalb bei einer neuen EXE warnen. Nur vom offiziellen GitHub-Release herunterladen und bei Bedarf prüfen:

```powershell
Get-FileHash .\EOTInstaller-v2.0.0-beta.4.1.exe -Algorithm SHA256
```

Die Prüfsumme mit `SHA256SUMS.txt` aus genau diesem Beta-4-Release vergleichen. Prüfsummen älterer Versionen gelten hier nicht.

## Bei Problemen

- Installer erneut starten; eine unterbrochene `.part`-Datei wird über HTTP Range fortgesetzt.
- Ein beschädigter Payload wird wegen falscher Größe oder SHA-256 abgelehnt.
- Bei wiederholten Downloadfehlern den Installer schließen, `%LOCALAPPDATA%\GenryTheFox\EOTInstaller\downloads` löschen und erneut versuchen.
- Bei einer abgelehnten Quelle Region und Revision kontrollieren.
- Prüfung einer installierten Version:

```powershell
.\Launcher.exe --validate
.\SpiderManEOT.exe --verify-install
```

## Bekannte Einschränkungen

- Mikroruckler bei einigen Übergängen;
- gelegentliche starke Hänger;
- weiße Texturen oder weißes Rendering auf manchen integrierten Intel-GPUs;
- weiße Elemente in Teilen des Einstellungsmenüs;
- mögliche Audioprobleme auf schwächeren Systemen;
- hardwareabhängiges Verhalten des experimentellen DLSS/ReShade-Pakets.

Wenn die experimentelle Grafikoption Probleme verursacht, deaktivieren und den normalen D3D12-Modus verwenden.

Kurz gesagt: Installer starten, eigene kompatible Xbox-360-Kopie auswählen, leeren Ordner angeben, Prüfung abwarten und `Launcher.exe` starten. Keine hundert BAT-Dateien und keine Bindung an fremde Laufwerke.
