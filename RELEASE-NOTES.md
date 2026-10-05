# Tf2DemoSalvage 0.1.0-beta.24

## Changes since 0.1.0-beta.23

- **A file that is not a demo now fails fast, with the reason.** The header is checked first, as TF2 does, and
  the message reads `<file> has invalid demo header ID.` A Git LFS pointer (a demo cloned without `git lfs
  pull`) gets a hint to run `git lfs pull`. The viewer window shows the reason in its status bar and stays
  usable; `--shot` and `--measure` exit with code 1 and the reason on standard error instead of hanging; the
  command-line tool prints one line, `error: <path>: <reason>`, and exits 1, with no stack trace. A demo that
  is merely cut short still opens, as before (B499).

## Changes in 0.1.0-beta.23

- **`+cl_game_folder <path>` on the command line** sets the TF2 `tf` folder for that run only, without saving
  it. Order of precedence: `TF2_FOLDER`, then `+cl_game_folder`, then `settings.cfg`, then Steam detection (B498).
- **Menu shortcut labels follow your config after the startup folder pick:** picking the TF2 folder at startup
  reloads your TF2 config, and the menu now shows its keys rather than the defaults (B498).

## Changes in 0.1.0-beta.22

- **Pick your TF2 folder from the viewer; no environment variables.** When the viewer cannot find TF2 through
  Steam, it opens a folder picker at startup for your `tf` folder (the folder above it also works). **File > TF2
  folder...** changes it later; a demo already open keeps its game files until the next start. The choice is saved
  as `cl_game_folder` in `%LOCALAPPDATA%\Tf2DemoSalvage\settings.cfg`, in TF2's own config syntax. Cancelling the
  picker saves `cl_game_folder_ask 0`, so you are not asked again on every launch. `TF2_FOLDER` still overrides
  everything, for scripts.
- **Screenshot folder:** `cl_screenshot_folder` in `settings.cfg` sets where screenshots go. The
  `TF2VIEW_CAPTURE_FOLDER` environment variable, which nothing read, is gone from `--help`.

## Changes in 0.1.0-beta.21

- **Particles behave as in TF2:** effects start with their initial particles, long frames are simulated in the
  game's smaller steps, a burst that hits its particle limit keeps the rest for later, and random values are drawn
  in the game's order. HUD particle and model panels each get their own randomness.

## Changes in 0.1.0-beta.20

- **Demos from late 2011 to early 2013 decode:** protocols 18, 19, 21 and 22, the era of the ESEA Season 10 to 12
  LANs and Insomnia 46, now decode correctly. Their sounds and decals were misread before.

## Changes in 0.1.0-beta.19

- **Map ambience on community and event maps:** a map that ships its own ambience script (71 of the installed maps,
  koth_lazarus and pl_venice among them) now plays it. Proxied ambience follows its master as in TF2, and sound
  levels outside the game's range fall back to normal as the game does.

## Changes in 0.1.0-beta.18

- **Beams, ropes and sprite trails now draw:** spotlight shafts and other beams, the ropes and cables hung in maps
  (sagging and swaying as in TF2), and projectile sprite trails as the ribbon the game draws rather than a single
  sprite.
- **`--measure` works with the frame-rate meter off:** the frame-rate log no longer goes silent when `cl_showfps`
  is 0.

## Changes in 0.1.0-beta.17

- **No more footsteps while paused:** a paused demo, or moving the camera while paused, kept replaying the players'
  last footsteps every frame. Each footstep now plays once, when the animation reaches it, as in TF2.

## Changes in 0.1.0-beta.16

- **Map ambience, as TF2 plays it:** ambient loops play at their own pitch (Halloween maps are pitched down), sounds
  placed in the map fade with distance, each placed sound plays at the spot the map gives it, and a sound shared by
  two areas carries on as you cross between them instead of playing twice. Loops with no volume set stay silent, as
  in the game.
- **Particles:** every effect draws its own random values, so two rockets or two explosions no longer look
  identical; a rocket's smoke puffs spread along its path between ticks instead of stacking on the rocket.
- **HUD:** the round timer shows up to ten time bonuses at once, and its labels appear as soon as the timer is
  picked up in setup and overtime. The ammo count hides for Halloween ghosts, in minigames and under the match
  summary.

## Changes in 0.1.0-beta.15

- **What gets drawn, the engine's way:** models are kept in the map leaves they touch and gathered per visible leaf,
  so only what TF2 would draw is posed and drawn. Models in the 3D skybox now draw in the sky.
- **See-through models** keep the translucency their model declares, as TF2 does; a skin change no longer turns a
  model see-through.

## Changes in 0.1.0-beta.14

- **HDR, as TF2 picks it:** maps with HDR lighting draw TF2's integer-HDR path; maps without it draw their LDR sky.
- **Skies:** HDR sky textures, half-float skies and `$color` tints draw as in TF2, and the Halloween sky faces load.
- **Water reflections** keep reflected light the way TF2's integer-HDR reflection view does.

## Changes in 0.1.0-beta.13

- **Overlay fading:** only overlays whose material reads vertex alpha fade out with distance, as in TF2; the rest
  stay solid and disappear at their maximum distance (848 overlays across the stock maps).
- **Scenes:** every compiled scene is read exactly as TF2's loader reads it, including the two engineer
  jackhammer-rodeo scenes the game itself misreads.

## Changes in 0.1.0-beta.12

- **Sprites:** every sprite render mode draws the way the game draws it — glows ignore depth, normal mode is opaque,
  additive-fractional-frame sprites draw twice, and the alpha-as-grey mode uses the texture's alpha.
- **Flinches:** a flinch a model places at its first sequence is skipped as in TF2, rather than replaced by the
  chest flinch.

## Changes in 0.1.0-beta.11

- **Water:** water now reflects and refracts the world as TF2 draws it — reflection and refraction views with the
  game's fog, cheap-water distance fade, waterline view and animated normal maps.
- **Keyboard after the File menu:** opening a file dialog from a File menu expanded by a screen reader no longer
  leaves every key going to the menu bar.
- **Movement:** the recorder's re-simulated movement only steps up over ledges when the game allows it, as TF2 does.

## Changes in 0.1.0-beta.10

- **See-through surfaces:** glass, grates and other translucent world surfaces draw in the game's order, leaf by
  leaf as the world is walked, and surfaces facing away from the camera are no longer drawn.
- **Water sort order:** surfaces above, below and across the water line draw in the game's group order, for the
  world, its overlays and its see-through surfaces.

## Changes in 0.1.0-beta.9

- **Stances:** aiming, deployed and air-dash crouch poses are drawn as the game picks them — a scoped sniper, a
  spun-up heavy, a scout crouching after a double jump — and a round's losers crouch-idle as in TF2.
- **Voice and custom gestures** now play their animations instead of being skipped.
- **A model missing an animation** shows the pose the game shows for it, rather than a substitute this viewer chose.

## Changes in 0.1.0-beta.8

- **Animations follow the weapon and item:** crouching, reloads and attacks use the activity the game picks for the
  class, the weapon's role and the item's own animation replacements, and a gesture takes the player's posture at the
  moment the game plays it.
- **Point-of-view demos:** the recorder's predicted speed now drives everything that reads it, including the spy's
  cloak fade.

## Changes in 0.1.0-beta.7

- **Point-of-view demos:** your own recordings now read the string tables the demo stores when recording starts, as
  the game does. The 3D skybox fog, step height and viewmodel visibility come through, and models, sounds and
  cosmetics loaded before you hit record are no longer missing.
- **Overlay order:** overlays are drawn in the order the game draws them each frame, from what is in view, including
  on displacements and translucent surfaces, so overlapping overlays stack the way they do in TF2.
- **Safer file lookup:** a loose file outside the game folder can no longer be reached through a folder whose name
  starts like the game's.

## Changes in 0.1.0-beta.6

- **Decals and overlays:** overlays fade out at the distance the map sets for them, and decals sit on surfaces with
  the game's own depth offset, so they no longer flicker or show through.
- **Player animation:** each class plays its own activity for a weapon, as the game's activity table maps it, and
  gestures replace the base animation where the game replaces it. Class jump behavior is part of the decode, so
  crouch and landing poses follow the class.

## Changes in 0.1.0-beta.5

- **Fog:** maps now draw their fog, using the fog controller the game picks for the recording player, with radial
  fog where the server enables it and the 3D skybox's own fog.
- **Model lighting:** a model lit by more than four lights keeps the rest as ambient light, as the game does, instead
  of dropping them; lights are ranked by brightness the way the engine ranks them.

## Changes in 0.1.0-beta.4

- **Landings:** players play the landing animation after a jump, as the game does — demoman, heavy, pyro, engineer,
  sniper and spy after any jump, and every class after a rocket or sticky jump. A player crouching through the end of
  a rocket jump is drawn standing, as TF2 draws him.

## Changes in 0.1.0-beta.3

- **Overlay layers:** overlays draw in the layers the mapper gave them, one layer at a time, as the game does, so a
  marking placed on top stays on top (136 stock maps layer their overlays, e.g. cp_badlands, cp_dustbowl, cp_granary).

## Changes in 0.1.0-beta.2

- **POV demos:** the recorder's own body now moves the way the game predicts it. His recorded inputs are
  re-simulated between packets, covering items, surfaces, doors, buildings, water, taunts, karts, stuns,
  parachutes and grappling hooks. His legs turn smoothly every frame instead of stepping each tick.
- **Steam on any drive:** the viewer finds TF2 wherever Steam records it, not only under Program Files.
  `TF2_FOLDER` overrides every lookup.
- **File > Export assembly / Compile assembly**, the same text and byte-identical rebuild as the CLI.
- **Very large demos** decompile in under 100 MB of memory.
- `demo_*` settings in your config take effect without reopening the demo.

Tf2DemoSalvage is a public beta. Tf2DemoSalvage reads Team Fortress 2 `.dem` files from any period of TF2's
history, including demos the current game client can no longer play, because it decodes each demo
against the entity schema the demo carries rather than one hardcoded for a single era.

This is a **beta**: decoding is well tested; the viewer is usable and still visibly different from
the game in places listed under *Known gaps*.

## What is in the zip

| folder | program | what it does |
|---|---|---|
| `cli/` | `tf2demosalvage.exe` | Decompiles a demo to text and compiles the text back to a demo. |
| `viewer/` | `tf2demoview.exe` | Plays a demo back in 3D with the game's own maps, models, materials and sounds. |

### The command-line tool

```
tf2demosalvage <demo> -t [-e] -o out.txt     readable trace, message by message (-e expands entity snapshots)
tf2demosalvage <demo> -s                     summary
tf2demosalvage <demo> -j -o out.jsonl        JSON Lines
tf2demosalvage <demo> -a -o out.asm          assembly text, which compiles back:
tf2demosalvage out.asm -c -o rebuilt.dem
tf2demosalvage --help                        every option
```

The decoded form goes to standard output and stays pipeable; diagnostics go to standard error. Memory
does not grow with the demo's size: 44 to 51 MB for demos of 9 MB, 1.3 GB and 2 GB (measured 2026-10-01).

### The viewer

Open a demo from the window, or pass it on the command line. `tf2demoview --help` lists every flag and
environment variable. Your own TF2 config (`.cfg`, or a mastercomfig-style `.vpk`) works as-is for
key bindings; commands the viewer does not implement are ignored rather than rejected. The viewer's own
settings (TF2 folder, screenshot folder, chosen HUD) live in `%LOCALAPPDATA%\Tf2DemoSalvage\settings.cfg`;
environment variables are for scripts and debugging only.

The File menu also has **Export assembly** (the open demo as text) and **Compile assembly** (text back
to a byte-identical demo), the same as the command-line tool's `-a` and `-c`.

## Reporting problems

The viewer writes one log per run to `%LOCALAPPDATA%\Tf2DemoSalvage` (`viewer-<date>-<time>-<id>.log`,
newest 50 kept). Send the newest one with the demo's name and what you did. The command-line tool
writes no log file; send its console output.

## What has been verified

Each claim below is a measurement, not an expectation.

- **Every protocol TF2 has shipped with that a demo could be found for: 11, 14, 15, 16, 18, 19, 21,
  22 and 24.** The test suite carries one demo per era and point of view, recorded on a client of
  that period: 2007 (build 3258, first-person and SourceTV), 2008 (build 3420, both), 2009
  (build 3862, first-person), 2011 (build 4604, both), 2013 (build 1729296, both) and a 2020 match.
  All decode and round-trip in every test run. Protocols 18, 19, 21 and 22 are verified on full
  2011-2012 ESEA and i46 recordings held in the local corpus only (builds 4735, 4743, 4833, 5126);
  committed specimens for them await short recordings on period clients. Protocols 12, 13, 17, 20
  and 23 have no known surviving demo, so they are untested.
- **Both points of view.** First-person (POV) recordings and SourceTV recordings decode alike; most
  eras above are tested with a POV and a SourceTV recording of the same session.
- **A census of 459 distinct real-world demos** (2026-09-30): the entity stage failed on none. At
  that time 214 passed every stage; every failure class it found has been fixed since (pause
  messages, demos cut off mid-command, Steam Voice audio, two 2010 SourceTV demos, the 2007 SourceTV
  schema cut, oversized files in the CLI). **The census has not been re-run since those fixes**, so
  the post-fix pass count is not yet measured.
- **Text round-trips to the identical file.** A demo decompiled to assembly and compiled back
  reproduces the original bytes, held by the test suite for every era above.
- **The TF2 folder picker and File > TF2 folder... menu item** are covered by unit tests of the folder
  precedence and validation and by the UI suite, which now runs on its own settings file and never touches
  yours (B497).
- **`+cl_game_folder` and menu shortcut relabeling** (beta.23) are covered by a command-line UI test and unit
  tests (B498).
- **Non-demo input** (beta.24): an empty file, a Git LFS pointer and an unrelated file are each refused with the
  header-ID message, and a truncated real header still opens; covered by unit tests and by the headless `--shot`
  exit code (B499).
- **Voice from every era:** Speex (2007 to 2011), Steam Voice / SILK (2011 to 2016), CELT
  (2016 to about 2018) and Opus (since).

## Known gaps

Things a user is likely to notice. Each has an entry in `docs/RISKS.md` in the repository.

- **Very large demos cannot be opened in the viewer.** A demo left recording on an idle server for
  hours (the 1.3 GB and 2 GB specimens found so far) works in the command-line tool, but the
  viewer's timeline needs 40 to 84 times the file's size in memory (B449, B439).
- **The viewer needs a lot of memory.** A typical modern match uses about 4 GB once loaded (B433,
  B407); see *Requirements*.
- **A truncated 2007 SourceTV schema is only completed for the one build known to truncate it**
  (build 3258). Another launch-era SourceTV demo with a cut schema would be refused with a message
  saying so (B24).
- **No landing sounds.** The game predicts them on the client and never records them in the demo;
  footsteps are rebuilt from the player animations, landings are not yet (B172).
- **Sounds a map ships in its own files do not play** (pl_venice has 35) (B485). A map's own ambience *script*
  does play (beta.19).
- **Refractive trails are not drawn**, the see-through trail several projectiles leave (B476).
- **Part of the rope model is not ported:** impulses, rope shaking, holiday lights (B478).
- **Cosmetics are not drawn in first person** (B186).
- **Switching demos without restarting the viewer gets slower** (B148).
- **Demos recorded in late June 2011 were malformed by the game itself** and may not decode (B144).
- **The first-person camera's movement differs slightly from the game's** in velocity and feet yaw
  (B450).

## Requirements

- **Windows 10 or 11, x64**, with a Direct3D 11 GPU.
- **8 GB of RAM minimum, 16 GB recommended.** The viewer holds about 4 GB with a full match loaded (B433).
- **Nothing else to install.** Both programs carry their own copy of .NET, so no runtime download is
  needed; unzip and run.
- **Your own Team Fortress 2 install**, for the viewer. Nothing from the game is included — no maps,
  models, materials or sounds — and no demos. The viewer reads them from your install (found through the Steam folder Steam records in the registry, then its library list, so Steam on another drive works; a `tf` folder you pick in the viewer, saved as `cl_game_folder`, takes over from that, `+cl_game_folder <path>` on the command line beats the saved one for a single run, and `TF2_FOLDER` beats all of them, for scripts) and, if it
  cannot find one, asks you to pick the folder, or if you cancel, plays the demo without the game's maps and models. The command-line tool needs only the demo.

## Licences

- **Tf2DemoSalvage** — MIT, `LICENSE.txt`.
- **Speex 1.2.1** (`viewer/speex.dll`) — Xiph.Org BSD-style, `licenses/SPEEX-COPYING.txt`.
- **CELT 0.11.3** (`viewer/celt.dll`) — Xiph.Org BSD-style, `licenses/CELT-COPYING.txt`.
- **SILK SDK 1.0.9** (`viewer/silk.dll`) — Skype Limited BSD-style, `licenses/SILK-LICENSE.txt`.
  Tf2DemoSalvage did not write it and does not claim to; it is included under that licence, which
  grants no patent rights.
- **libopus 1.6.1** (`viewer/runtimes/win-x64/native/opus.dll`) — Xiph.Org BSD-style,
  `licenses/OPUS-COPYING.txt`.
- **OpenAL Soft 1.23.1** (`viewer/soft_oal.dll`, from the `Silk.NET.OpenAL.Soft.Native` package) —
  LGPL 2.0 or later, `licenses/OPENAL-SOFT-COPYING.txt`; dynamically loaded and replaceable.
- **.NET runtime** (bundled in `viewer/` and `cli/`) — MIT, Microsoft.
- Other third-party packages in `viewer/` and `cli/` (Silk.NET, NLayer,
  Microsoft.Extensions.Logging) are under their own licences, published with each package on NuGet.
