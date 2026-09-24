# Spider-Man: Edge of Time — PC Edition: installation

> **Two Eras. One Destiny.**

[English](INSTALL_EN.md) · [Русский](INSTALL_RU.md) · [Deutsch](INSTALL_DE.md) · [Français](INSTALL_FR.md) · [Italiano](INSTALL_IT.md) · [Español](INSTALL_ES.md)

## What to download

For a normal online installation, download only:

- `EOTInstaller-v2.0.0-beta.4.1.exe`

The installer downloads `EOT-PC-Payload-v2.0.0-beta.4.1.zip`, resumes an interrupted transfer and verifies its size and SHA-256 before extraction.

The other release files are:

- `EOT-PC-Payload-v2.0.0-beta.4.1.zip` — PC Edition components and patches; a manual download is unnecessary for a normal online installation;
- `SHA256SUMS.txt` — checksums for the installer and payload;
- `release-manifest.json` — version, sizes, hashes and supported source IDs;
- `Source code` — the installer source, not a playable copy of the game.

## Normal installation

1. Download `EOTInstaller-v2.0.0-beta.4.1.exe` from the [official project release](https://github.com/GenryTheFox0/Project-2099/releases/tag/v2.0.0-beta.4.1).
2. Run the installer.
3. Select the installer and game language.
4. Wait while the PC Edition components are downloaded and verified.
5. Select your compatible Xbox 360 source:
   - a USA/Europe retail XDVDFS ISO;
   - a ZIP containing the game, even when it has an outer `Spider-Man - Edge of Time (USA Europe)` folder;
   - an extracted folder containing `Default.xex` and `Data`, or its outer parent folder;
   - the tested alternate Russian GOD/LIVE/XSF through its `415608B2/00007000` folder.
6. Select an empty installation directory, for example `D:\Games\Project 2099`.
7. Press INSTALL and wait for the final verification.
8. Start the installed game through `Launcher.exe`.

Do not select a drive root such as `D:\`, and do not install over a non-empty older build. A separate empty folder prevents files from different versions from being mixed together.

After installation succeeds, the source ISO, GOD or extracted console folder is no longer required. The installed PC Edition runs from its destination and is not tied to the original drive letter.

## Offline or pre-downloaded payload

1. Download both:
   - `EOTInstaller-v2.0.0-beta.4.1.exe`;
   - `EOT-PC-Payload-v2.0.0-beta.4.1.zip`.
2. Create a separate folder for the installer.
3. Leave the ZIP beside the EXE; extraction is optional. If you extract it, the layout is:

```text
EOT GitHub Installer\
├── EOTInstaller-v2.0.0-beta.4.1.exe
└── payload\
    ├── payload-manifest.json
    ├── port\
    └── patches\
```

4. Run the installer. It detects and verifies either the ZIP or the local `payload` folder without downloading the archive again.

Do not rename `payload` or move its internal files out of their directories.

The source button accepts ISO and ZIP directly. When a folder is selected, the installer searches up to two levels below it and also opens a single ISO or ZIP found inside it.

## Supported source revisions

- `usa-europe-retail` — common USA/Europe Xbox 360 retail ISO/ZIP release;
- `usa-europe-retail-r2` — second recorded USA/Europe retail revision;
- `eu-retail` — older PC Edition original-language baseline retained for compatibility;
- `ru-god-alt` — tested alternate Russian GOD/LIVE/XSF release;
- `sazanoff-rus-god` — Region Free RUS GOD (SazanOFF v1.0b).

A file extension or a `LIVE`, `PIRS` or `CON` header is not enough. The installer checks actual file sizes and SHA-256 hashes. Unknown, mixed or modified revisions are rejected instead of producing a broken installation.

## Installed files and user data

The completed folder contains `Launcher.exe`, `SpiderManEOT.exe`, `rexruntime.dll`, `spider_man_edge_of_time.toml`, both language-data trees and the Support folder. Use `Launcher.exe` as the normal entry point; no pile of BAT files is required.

Settings and saves:

```text
%USERPROFILE%\Documents\spider_man_edge_of_time
```

Installer download cache:

```text
%LOCALAPPDATA%\GenryTheFox\EOTInstaller
```

The game does not need the installer cache after a successful installation. Keep it for faster reinstallations or remove it to reclaim disk space.

## Controls

- keyboard and mouse support;
- WASD movement;
- adjustable mouse sensitivity;
- XInput controller support;
- prompts follow the active input device;
- QTE prompts: **B ↔ E** and **Y ↔ MMB**;
- **MMB means clicking the mouse wheel**, not scrolling it.

## SmartScreen and SHA-256

The installer is not currently signed with a commercial certificate, so Windows SmartScreen may warn about a newly downloaded EXE. Download it only from this GitHub release and verify its SHA-256 before running it:

```powershell
Get-FileHash .\EOTInstaller-v2.0.0-beta.4.1.exe -Algorithm SHA256
```

Compare the result with `SHA256SUMS.txt` attached to this exact Beta 4 release. Checksums from older releases do not apply.

## If something fails

- Run the installer again: an interrupted `.part` download resumes through HTTP Range.
- A corrupted payload is rejected when its size or SHA-256 differs.
- If the same download error repeats, close the installer, remove `%LOCALAPPDATA%\GenryTheFox\EOTInstaller\downloads`, and retry.
- If the source is rejected, check that it is a supported revision rather than another region, a modified image or a mixture of files.
- To verify an installed folder, run:

```powershell
.\Launcher.exe --validate
.\SpiderManEOT.exe --verify-install
```

## Known limitations

- microstutter during some transitions;
- occasional severe freezes;
- white textures or white rendering on some Intel integrated GPUs;
- white elements in parts of the settings UI;
- possible audio trouble on weaker systems;
- hardware-specific behavior from the experimental DLSS/ReShade package.

If the experimental graphics option causes problems, disable it and use the regular D3D12 mode.

## Short version

Download the installer, select your own compatible Xbox 360 copy, choose an empty folder, wait for verification, and start `Launcher.exe`.

That is it. No hundred BAT files and no dependency on somebody else's drive.
