🕷 GENRY WAREHOUSE | EDGE OF TIME PC EDITION DEVELOPMENT DIARY — BETA 3

More happened around Spider-Man: Edge of Time PC Edition during the last day than I expected from its first public release, so I want to record the current situation properly — without a team war, without arguing about which port is “real,” and without pretending that a beta is already a perfect final build.

First, the situation with EdgeOfTime-Recompiled, also known as `reeot`.

My PC Edition and `reeot` were developed independently. I did not use their private code, and their project is not based on mine. We simply approached the same game from different directions.

The `reeot` team has spent far longer researching the game itself: reverse engineering, the renderer, PKZ files, shaders, debug features and future modding tools. My work grew around the user-facing PC side: a playable build, launcher, installer, keyboard and mouse, rebinding, dynamic QTE prompts, localization, Cyrillic fonts, achievements and full campaign testing.

When the `reeot` developers found my release, the first reaction was tense, which is understandable. They have invested a huge amount of time in this game, and another public PC Edition suddenly appeared beside their work. I spoke directly with Graine25 and SerJar, we exchanged builds and started comparing our approaches. Instead of another fucking argument, there is now a real opportunity to help each other.

Please do not harass either side and do not invent a leak or theft story. There was none.

My GitHub repository and V1 Beta are staying online for now. People are already using the build, articles point to it, and reports from different hardware are exposing problems I could not reproduce alone. Deleting the repository overnight would erase that history and break every existing link.

I am still interested in cooperation. We will first see how actual joint development goes. If our systems fit together, everyone’s contributions remain credited, and one combined result becomes stronger than two separate branches, that would be fucking great. We can then decide calmly what a unified future should look like. I am not making dramatic merger promises on the first night.

Now for beta.3 itself.

After release, users reported that the installer accepted the alternate Russian GOD source but rejected the common `Spider-Man - Edge of Time (USA Europe)` archive. Others had to extract the game manually and click through duplicate wrapper folders before reaching `Default.xex`.

The cause was specific. The old manifest labeled `eu-retail` had been generated from an already prepared original-language PC Edition tree rather than the common clean USA/Europe file set. The Xbox 360 `Default.xex` was correct, but localized files such as `Main.pkz` and `Act01.pkz` differed, so the installer rejected the source.

Beta 3 adds a dedicated USA/Europe donor and its own reconstruction path.

The installer can now:

— open a USA/Europe Xbox 360 ISO directly;

— open a complete game ZIP without manual extraction;

— detect a game stored inside an outer `Spider-Man - Edge of Time (USA Europe)` folder in the ZIP;

— search one or two levels below a selected outer directory;

— open the only ISO or ZIP found inside a selected folder;

— continue accepting the Russian GOD/`415608B2/00007000` source.

The installer finds `Default.xex`, identifies the compatible structure and reconstructs the same final `Data/Original` and `Data/Russian` trees. Users no longer need to guess which duplicate folder level is the real game root.

This was tested with the actual 5.075 GB USA/Europe ZIP, not only a tiny mock archive. The complete PC Edition was built from it successfully.

Final verification:

— `Launcher.exe --validate` returned 0;

— `SpiderManEOT.exe --verify-install` returned 0;

— all 293 Original files matched;

— all 293 Russian files matched;

— the Russian GOD source still passed after the change;

— normal XISO, the missing XGD offset and fallback partition scanning were tested separately;

— the source page was rendered and checked in English, Russian, German, French, Italian and Spanish.

Beta 3 is an installer compatibility hotfix. It does not replace the game runtime, remove saves or roll back graphics and controls. Its job is to stop making users fight ISO layouts, ZIP extraction and duplicate wrapper folders.

The remaining V1 issues are still real:

— microstutter during some transitions;

— occasional severe freezes;

— frame-pacing problems;

— white materials or frames on some Intel integrated GPUs;

— scene-dependent 75/120 FPS behavior;

— remaining visual differences from the Xbox 360 original.

Those are targets for continued development. At the same time, I am beginning to study the `reeot` material while sharing my PC controls, QTE, localization, font, launcher and other user-facing work with their team.

My project:
https://github.com/GenryTheFox0/Project-2099

EdgeOfTime-Recompiled / reeot:
https://github.com/goliathret/reeot

Voluntary support options are available inside `Launcher.exe`, on its home screen and Project page.

In short: V1 stays online, beta.3 fixes USA/Europe installation, the conflict has become a development conversation, and the next step depends on the actual results of cooperation. If two different approaches eventually produce one stronger port, then this unexpected mess was worth something after all.

Two eras. One destiny.
