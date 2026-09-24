# Spider-Man: Edge of Time — PC Edition: installazione

> **Due epoche. Un solo destino.**

[English](INSTALL_EN.md) · [Русский](INSTALL_RU.md) · [Deutsch](INSTALL_DE.md) · [Français](INSTALL_FR.md) · [Italiano](INSTALL_IT.md) · [Español](INSTALL_ES.md)

## Cosa scaricare

Per la normale installazione online serve soltanto:

- `EOTInstaller-v2.0.0-beta.4.1.exe`

L'installer scarica automaticamente `EOT-PC-Payload-v2.0.0-beta.4.1.zip`, riprende un trasferimento interrotto e controlla dimensione e SHA-256 prima dell'estrazione.

Gli altri file della release sono:

- `EOT-PC-Payload-v2.0.0-beta.4.1.zip` — componenti e patch della PC Edition; non serve scaricarlo manualmente per l'installazione normale;
- `SHA256SUMS.txt` — checksum dell'installer e del payload;
- `release-manifest.json` — versione, dimensioni, hash e sorgenti supportate;
- `Source code` — codice sorgente dell'installer, non una copia giocabile del gioco.

## Installazione normale

1. Scarica `EOTInstaller-v2.0.0-beta.4.1.exe` dalla [release ufficiale del progetto](https://github.com/GenryTheFox0/Project-2099/releases/tag/v2.0.0-beta.4.1).
2. Avvia l'installer.
3. Seleziona la lingua dell'installer e del gioco.
4. Attendi il download e la verifica dei componenti della PC Edition.
5. Seleziona una tua sorgente Xbox 360 compatibile:
   - ISO XDVDFS retail USA/Europa;
   - ZIP contenente il gioco, anche con una cartella esterna `Spider-Man - Edge of Time (USA Europe)`;
   - cartella estratta contenente `Default.xex` e `Data`, oppure la sua cartella principale esterna;
   - GOD/LIVE/XSF russo alternativo verificato tramite la cartella `415608B2/00007000`.
6. Scegli una cartella di destinazione vuota, per esempio `D:\Games\Project 2099`.
7. Premi INSTALLA e attendi la verifica finale.
8. Avvia il gioco installato tramite `Launcher.exe`.

Non scegliere la radice di un disco come `D:\` e non installare sopra una vecchia build non vuota. Una cartella separata evita di mischiare file di versioni differenti.

Dopo un'installazione riuscita, l'ISO, il GOD o la cartella console estratta non servono più. La PC Edition funziona dalla destinazione scelta e non dipende dalla lettera dell'unità sorgente.

## Installazione offline

1. Scarica entrambi i file:
   - `EOTInstaller-v2.0.0-beta.4.1.exe`;
   - `EOT-PC-Payload-v2.0.0-beta.4.1.zip`.
2. Crea una cartella separata per l'installer.
3. Puoi lasciare lo ZIP accanto all'EXE senza estrarlo. Se lo estrai, la struttura è:

```text
EOT GitHub Installer\
├── EOTInstaller-v2.0.0-beta.4.1.exe
└── payload\
    ├── payload-manifest.json
    ├── port\
    └── patches\
```

4. Avvia l'EXE. L'installer rileva e verifica lo ZIP o la cartella locale `payload` senza scaricare di nuovo l'archivio.

Non rinominare `payload` e non spostare i file interni fuori dalla loro struttura.

ISO e ZIP possono essere selezionati direttamente. Se scegli una cartella, l’installer cerca la radice del gioco fino a due livelli più in basso e apre automaticamente l’unico ISO/ZIP presente nella cartella selezionata.

## Sorgenti supportate

- `usa-europe-retail` — comune versione retail USA/Europa Xbox 360 in formato ISO/ZIP;
- `usa-europe-retail-r2` — seconda revisione retail USA/Europa conosciuta;
- `eu-retail` — vecchia base multilingue di PC Edition mantenuta per compatibilità;
- `ru-god-alt` — versione russa alternativa GOD/LIVE/XSF verificata;
- `sazanoff-rus-god` — GOD RUS Region Free (SazanOFF v1.0b).

Un'estensione o un'intestazione `LIVE`, `PIRS` o `CON` non basta. L'installer controlla dimensioni reali e hash SHA-256. Le revisioni sconosciute, miste o modificate vengono rifiutate invece di produrre un'installazione danneggiata.

## File installati e dati utente

La cartella finale contiene `Launcher.exe`, `SpiderManEOT.exe`, `rexruntime.dll`, `spider_man_edge_of_time.toml`, entrambi gli alberi dei dati linguistici e la cartella `Support`. Usa `Launcher.exe` come avvio normale; non serve una montagna di file BAT.

Impostazioni e salvataggi:

```text
%USERPROFILE%\Documents\spider_man_edge_of_time
```

Cache dei download:

```text
%LOCALAPPDATA%\GenryTheFox\EOTInstaller
```

Il gioco non usa questa cache dopo un'installazione riuscita. Puoi conservarla per reinstallare più rapidamente oppure eliminarla per recuperare spazio.

## Comandi

- tastiera e mouse;
- movimento con WASD;
- sensibilità del mouse regolabile;
- controller XInput;
- icone che seguono il dispositivo attivo;
- QTE: **B ↔ E** e **Y ↔ MMB**;
- **MMB significa premere la rotellina**, non farla scorrere.

## SmartScreen e SHA-256

L'installer non è ancora firmato con un certificato commerciale, quindi Windows SmartScreen può mostrare un avviso. Scaricalo solo dalla release GitHub ufficiale e, se necessario, verificalo:

```powershell
Get-FileHash .\EOTInstaller-v2.0.0-beta.4.1.exe -Algorithm SHA256
```

Confronta il risultato con `SHA256SUMS.txt` allegato a questa stessa release Beta 4. Le somme delle versioni precedenti non valgono.

## In caso di problemi

- Riavvia l'installer: un download `.part` interrotto riprende tramite HTTP Range.
- Un payload danneggiato viene rifiutato se dimensione o SHA-256 non coincidono.
- Se l'errore di download continua, chiudi l'installer, elimina `%LOCALAPPDATA%\GenryTheFox\EOTInstaller\downloads` e riprova.
- Se la sorgente viene rifiutata, controlla regione e revisione.
- Per verificare una cartella già installata:

```powershell
.\Launcher.exe --validate
.\SpiderManEOT.exe --verify-install
```

## Limitazioni note

- microstuttering in alcune transizioni;
- blocchi pesanti occasionali;
- texture o rendering bianchi su alcune GPU Intel integrate;
- elementi bianchi in alcune parti del menu delle impostazioni;
- possibili problemi audio sui sistemi meno potenti;
- comportamento dipendente dall'hardware del pacchetto sperimentale DLSS/ReShade.

Se l'opzione grafica sperimentale causa problemi, disattivala e usa la modalità D3D12 normale.

In breve: avvia l'installer, scegli la tua copia Xbox 360 compatibile, indica una cartella vuota, attendi la verifica e avvia `Launcher.exe`. Senza cento file BAT e senza dipendere dal disco di qualcun altro.
