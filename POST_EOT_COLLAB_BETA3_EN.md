**🕷 GENRY WAREHOUSE | EDGE OF TIME PC EDITION — BETA 3 AND THE REEOT SITUATION**

**The last 24 hours went much further than I expected, so I want to close the confusion without turning this into another twenty-page technical wall.**

**Spider-Man: Edge of Time PC Edition and EdgeOfTime-Recompiled (`reeot`) were developed independently. My build was not taken from reeot, and reeot was not taken from mine. Nothing leaked. Two groups simply approached the same Xbox 360 game from very different directions.**

**The reeot team has spent far longer reverse-engineering the game itself: its renderer, PKZ format, shaders, internal systems, debug functions and future modding support. My work grew around the PC-facing side: a public playable build, launcher, installer, keyboard and mouse, rebinding, dynamic QTE prompts, localization, Cyrillic fonts, achievements and release packaging.**

**After they found my release, I spoke directly with Graine25 and SerJar. The initial surprise could have turned into a stupid “AI port versus real port” shitshow, but it did not. We exchanged builds, started comparing our work and are now discussing where the useful parts of both projects can fit together.**

**Please do not harass either team and do not invent a leak or theft story. There is no fucking war here. There are two independent projects and a real attempt to cooperate.**

**My public repository and V1 Beta are staying online for now. People are already using the build, sending reports and following links from articles. Deleting the repository overnight would erase the public history and break the existing release.**

**That does not mean I am refusing a unified future. I am joining the development conversation first. If the collaboration works well, the code and features combine properly, and everyone’s contributions remain credited, then having one stronger project may be the best result. We will decide that through actual work, not through panic on the first night.**

**At the same time, I am releasing installer hotfix `v1.0.0-beta.3`.**

**Beta 3 fixes the main source-selection problem:**

— the common Spider-Man: Edge of Time **USA/Europe Xbox 360 ISO** is accepted;

— a complete game **ZIP can be selected directly** without manual extraction;

— the ZIP may contain an outer `Spider-Man - Edge of Time (USA Europe)` folder;

— selecting the outer folder is enough; the installer finds `Default.xex` automatically;

— a folder containing one ISO or ZIP is detected automatically;

— the existing Russian GOD/`00007000` source still works;

— USA/Europe now has its own reconstruction path to the same final Original and Russian PC Edition data.

**This is an installer compatibility patch. It does not magically remove the remaining V1 runtime problems: microstutter, occasional heavy freezes, frame pacing and some Intel rendering issues are still being investigated.**

**Project:**
[https://github.com/GenryTheFox/Spider-Man-Edge-of-Time-PC-Edition](https://github.com/GenryTheFox/Spider-Man-Edge-of-Time-PC-Edition)

**reeot:**
[https://github.com/goliathret/reeot](https://github.com/goliathret/reeot)

**So that is the situation. My project is not being erased. Their work is not being ignored. We are looking at each other’s code and taking the good shit instead of sitting in separate corners guessing. If the collaboration goes well, that will be fucking great for everyone who wants a real Edge of Time PC release.**

**Two eras. One destiny. Apparently two recompilation projects too.**
