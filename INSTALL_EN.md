# Spider-Man: Edge of Time — PC Edition: installation

> **Two Eras. One Destiny.**

[English](INSTALL_EN.md) · [Русский](INSTALL_RU.md) · [Deutsch](INSTALL_DE.md) · [Français](INSTALL_FR.md) · [Italiano](INSTALL_IT.md) · [Español](INSTALL_ES.md)

## What to download

For a normal online installation, download only:

- `EOTInstaller-v1.0.0-beta.2.exe`

The installer downloads `EOT-PC-Payload-v1.0.0-beta.2.zip`, resumes an interrupted transfer and verifies its size and SHA-256 before extraction.

The other release files are:

- `EOT-PC-Payload-v1.0.0-beta.2.zip` — PC Edition components and patches; a manual download is unnecessary for a normal online installation;
- `SHA256SUMS.txt` — checksums for the installer and payload;
- `release-manifest.json` — version, sizes, hashes and supported source IDs;
- `Source code` — the installer source, not a playable copy of the game.

## Normal installation

1. Download `EOTInstaller-v1.0.0-beta.2.exe` from the [official project release](https://github.com/GenryTheFox/Spider-Man-Edge-of-Time-PC-Edition/releases/tag/v1.0.0-beta.2).
2. Run the installer.
3. Select the installer and game language.
4. Wait while the PC Edition components are downloaded and verified.
5. Select your compatible Xbox 360 source:
   - a European retail XDVDFS ISO;
   - an extracted folder containing `Default.xex` and `Data`;
   - the tested alternate Russian GOD/LIVE/XSF through its `415608B2/00007000` folder.
6. Select an empty installation directory, for example `D:\Games\Spider-Man Edge of Time PC Edition`.
7. Press INSTALL and wait for the final verification.
8. Start the installed game through `Launcher.exe`.

Do not select a drive root such as `D:\`, and do not install over a non-empty older build. A separate empty folder prevents files from different versions from being mixed together.

After installation succeeds, the source ISO, GOD or extracted console folder is no longer required. The installed PC Edition runs from its destination and is not tied to the original drive letter.

## Offline or pre-downloaded payload

1. Download both:
   - `EOTInstaller-v1.0.0-beta.2.exe`;
   - `EOT-PC-Payload-v1.0.0-beta.2.zip`.
2. Create a separate folder for the installer.
3. Extract the ZIP beside the EXE so the layout is:

```text
EOT GitHub Installer\
├── EOTInstaller-v1.0.0-beta.2.exe
└── payload\
    ├── payload-manifest.json
    ├── port\
    └── patches\
```

4. Run the installer. It detects and verifies the local `payload` folder without downloading the archive again.

Do not rename `payload` or move its internal files out of their directories.

## Supported source revisions

- `eu-retail` — European Xbox 360 retail release;
- `ru-god-alt` — tested alternate Russian GOD/LIVE/XSF release.

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
Get-FileHash .\EOTInstaller-v1.0.0-beta.2.exe -Algorithm SHA256
```

Installer:

```text
B5E66317CAD4375CF08487FC145DA6388F455C204155E110A4CCD55E088829A1
```

Payload:

```text
5651197BC6D093F7474047D37599E81E4EB6B7184EB03A245D688B80BCC314B8
```

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

## Known V1 Beta issues

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
