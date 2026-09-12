# Third-Party Licenses and Credits

This file describes the scope of the notices collected for Spider-Man: Edge of Time — PC Edition. It distinguishes the installer source published here from the separately built game runtime, graphics add-ons and resource patches.

## Project source

The project's own installer code is offered under [GNU GPL version 3.0](LICENSE), to the extent the authors have the right to license it. Original third-party notices are retained.

Earlier copies of the installer were distributed under BSD-3-Clause. Those grants are not revoked; the previous notice is preserved in [licenses/Genry-Installer-Legacy-BSD-3-Clause.txt](licenses/Genry-Installer-Legacy-BSD-3-Clause.txt).

## ReXGlue

- Project: <https://github.com/rexglue/rexglue-sdk>
- Role: recompilation/runtime foundation of the separately built PC Edition.
- License of the inspected local SDK: BSD-3-Clause.
- Copyright notice: Tom Clay and the ReXGlue contributors, with Xenia-derived portions credited in the original text.
- Full collected notice: [licenses/ReXGlue.txt](licenses/ReXGlue.txt).

This credit does not imply upstream endorsement of the PC Edition.

## Xenia

- Project: <https://github.com/xenia-project/xenia>
- Role: Xbox 360 research, runtime ancestry and disc/container filesystem reference work.
- License: BSD-3-Clause.
- Full upstream notice: [licenses/Xenia-BSD-3-Clause.txt](licenses/Xenia-BSD-3-Clause.txt).

The XDVDFS and SVOD reader files retain their existing Xenia attribution. Their copyright notices and license conditions must be preserved when redistributed.

## XenosRecomp

- Project: <https://github.com/hedge-dev/XenosRecomp>
- Role here: acknowledgement of related Xbox 360 shader-recompilation work.

A direct dependency of the standalone C# installer has not been established. This document therefore does not assign a guessed license to an unverified version or claim to include its source. Any distributed fork or binary must carry the notices belonging to that exact component.

## Unleashed Recompiled

- Project: <https://github.com/hedge-dev/UnleashedRecomp>
- Role: reference for the installation flow and source-container handling.
- The inspected repository's top-level `COPYING` contains GPLv3; individual files may carry other notices.

The EOT installer is a separate implementation. This acknowledgement does not replace license obligations for any code adapted from upstream files.

## reeot / EdgeOfTime-Recompiled

- Project: <https://github.com/goliathret/reeot>
- Role: a separate Edge of Time recompilation effort, credited in the README.
- Its public repository identifies BSD-3-Clause licensing.

It is not represented here as an EOTInstaller dependency or as the source of this release.

## Other collected runtime notices

The following notices were collected from the local PC Edition's license directory. Inclusion documents those components; it does not mean each is directly linked into the standalone installer.

| Component | Notice |
| --- | --- |
| Dear ImGui | [licenses/DearImGui.txt](licenses/DearImGui.txt) |
| fmt | [licenses/fmt.txt](licenses/fmt.txt) |
| SDL3 | [licenses/SDL3.txt](licenses/SDL3.txt) |
| spdlog | [licenses/spdlog.txt](licenses/spdlog.txt) |

## Graphics add-ons and platform libraries

Release packaging also involves components such as ReShade, Feeder, RenoDX, FidelityFX, NVIDIA libraries and Microsoft runtime libraries. They retain their respective terms. These are not all covered by this repository's GPL license.

This collection is **not a complete notice inventory for every DLL or add-on in the separate payload**. The exact shipped versions, redistribution terms and notices still need to be reconciled for that package. A link to an upstream repository alone does not complete that work.

## Artwork, fonts and resource patches

The supplied logo, character PNGs, game-derived fonts and original game content are outside the license grant for installer code. Their rights remain with the respective owners.

The current EOTP builder has two modes. Equal-size files use changed blocks; size-changing targets use compressed replacement blocks covering the complete target. Consequently, some `.eotp` files contain complete replacement resource data. A filename-extension audit is not evidence that a payload contains no game assets.

The source repository excludes game dumps. The payload is a separate distribution artifact and must be assessed on its contents; this document does not certify it as asset-free.

## Original game

Spider-Man: Edge of Time was developed by Beenox and originally published by Activision. Spider-Man and associated characters, artwork and trademarks belong to their respective rights holders.

No ownership of the original game is claimed. The fan project is not affiliated with or endorsed by the original developers, publisher or platform holders.
