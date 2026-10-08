# Tf2DemoSalvage 0.1.0-beta.39

## Changes since 0.1.0-beta.38

- **Models now light through their normal maps.** Players, weapons and cosmetics that have a bump map now use it
  for their shading, highlights, rim light and environment reflections, because the viewer reads the model file's
  tangent frame, as the game does. A cloaking spy's warp now follows the folds of his suit rather than the screen.
  No measurable change in frame time (B512, B170, B508).

## Changes in 0.1.0-beta.38

- **Camera monitors now show the camera's view.** A `point_camera` feeding a screen is rendered into `_rt_Camera`
  as the game does, so for example the stage mirrors on `koth_boardwalk` show what the camera sees, including the
  recorder's own player. `cl_drawmonitors 0` in your config turns it off, as in the game (B511).

## Changes in 0.1.0-beta.37

- **Every spy now cloaks the way the game draws it.** Cloak and Dagger (the watch whose cloak fades with how fast
  the spy moves) now applies to every spy, not only the recorder. The recorder's own weapons also stay pinned at
  0.3 visibility when his cloak meter is empty (B508).
- **Your Eternal Reward victims now cloak.** The corpse fades out over one second, as in the game. Dead Ringer
  corpses never cloak, so they are drawn normally; the earlier note saying otherwise was wrong (B508).
- **Halloween stealth.** The stealth spell's visibility cap and its screen overlay are drawn (B508, B509).
- **Animated screen overlays animate.** Jarate, bleed and gas overlays now play their animation at 30 frames a
  second instead of showing the first frame. The burning overlay (`imcookin`) draws nothing in the game either,
  so that is correct (B509).

## Changes in 0.1.0-beta.36

- **The recorder now hears his own landings.** In a point-of-view demo, a hard landing plays the ground's footstep
  sound at the game's volume (full past a fast fall, a little less past a medium one; a scout lands audibly only
  when it hurts), as the game does. The game predicts these on the recorder's own client and never records them.
  Other players' landings, and every landing in a SourceTV demo, stay silent by design: the game's server leaves
  out anyone who can see the player, so nobody else is meant to hear them either (B172).

## Changes in 0.1.0-beta.35

- **Ropes are now ported in full.** A rope's impulse (the force the two maps `ctf_helltrain_event` and
  `arena_perks` apply to theirs), the `ShakeRopes` shake, holiday lights on ropes (including Pyrovision's white
  lights), the cable material's bump term, and ropes whose ends go dormant now follow the game (B478). The
  holiday lights follow the game's Christmas rules and blink and cycle colour as it does.

## Changes in 0.1.0-beta.34

- **Cloaked spies now cloak.** A spy at any level of cloak drew as a solid player. He now warps the frame behind
  him with the game's cloak pass and fades with the cloak level; at full cloak the recorder's enemy spies vanish,
  while the recorder's teammates and every spy in a SourceTV demo keep the game's 0.95 shimmer. Team tint follows
  the game (B508).
- **The recorder's screen overlays now draw.** In first person on a point-of-view demo, uber, jarate, bleed and
  gas tint and warp the view, as the game does (B509).
- **Demos at protocols 18 and 19 are no longer held back by a list.** They already decoded; the test that checked
  them had pinned the accepted protocols, and now asks only for protocol 11 or later (D211). No change to
  decoding.

## Changes in 0.1.0-beta.33

- **Refracting materials on models now draw.** The Bazaar Bargain lens, the crystal ball and the blurred muzzle
  flash showed the missing-material checkerboard; they now warp a copy of the frame behind them, as the game
  does (B506).

## Changes in 0.1.0-beta.32

- **A map you do not have installed now loads on the first open.** The viewer starts fetching the map as soon as
  the demo header names it, in parallel with decoding, and the load waits for it. Before, a first download could
  leave a view with no map, or crash (B507). Fetched maps are cached in `%LOCALAPPDATA%\Tf2DemoSalvage\maps`.

## Changes in 0.1.0-beta.31

- **Refract trails now draw.** Trails drawn with a refracting material (the `beam001` trails attached to players,
  common in real matches) were skipped. They now warp a copy of the frame behind them and fade into fog, as the
  game does (B476).
- **No user-visible change:** offscreen test pictures now use the same sRGB format as the window.

## Changes in 0.1.0-beta.30

- **A seek now replays the game's skip, silently.** HUD sounds and animation-event sounds in the skipped ticks
  count toward a script's no-repeat wave deck, as in TF2, so the waves you hear after a seek match what the game
  would play (B504).
- **Seeks are slower, and that is the cost of the above.** On the f12 demo, seeking forward to tick 90,006 takes
  about 4 s longer, and rewinding to tick 50,000 about 0.9 s longer. TF2 itself restarts the demo and
  fast-forwards on a rewind.
- **Within a frame that covers several ticks, sounds follow the game's order** (B505).

## Changes in 0.1.0-beta.29

- **A script's waves no longer repeat until every wave has played.** The game deals a sound script's waves like a
  deck, shared by every sound that names that script; the viewer now does the same (B503).
- **Within a tick, sounds play in the game's frame order:** HUD sounds, medigun patches, animation events and
  footsteps, physics impacts and friction, then temp entities in the order they arrived (B505).
- **Seeking follows the game's own skip.** Unreliable temp entities (explosions, impacts) in skipped ticks no
  longer fire or deal a wave, and the deck is kept, not rebuilt (B504).
- **Every wave of a script a demo uses is precached,** so a sound's first play no longer hitches (B503).

## Changes in 0.1.0-beta.28

- **Game sound scripts are read as the game reads them.** A script's volume, pitch and sound level are parsed and
  stored the way the game's sound system does, so a few shipped scripts now play at a slightly different volume
  (tiny differences) (B487).
- **Sounds are drawn in the game's order.** For a script sound the viewer now picks volume, pitch, wave and sound
  level in the order the game does (B502).
- **A footstep with a single wave keeps its pitch and sound level per foot,** reusing the first draw as the game
  does, instead of redrawing each step (B502).
- **The medigun loop plays at pitch 100,** not the script's pitch (B502).
- **The sound of a corpse sliding uses the script's channel and volume** (B502).

## Changes in 0.1.0-beta.27

- **Soundscapes switched by map triggers now play.** A `trigger_soundscape` volume linked to an
  `env_soundscape_triggerable` changes the ambience when you enter or leave it, as in the game (71 such
  soundscapes on the installed maps) (B483).
- **First person on the recording player plays the soundscape the demo recorded,** exactly, tick by tick. The
  free camera, other players and SourceTV views still work the soundscape out from the camera position (B483).
- **A soundscape with no `radius` key now has radius 0 and never wins,** as in the game's own code. Ambience that
  used to play on some maps because of that missing key may now be silent (B483).

## Changes in 0.1.0-beta.26

- **Sounds a map ships in its own file now play.** Ambience, water and the like that live only inside the map
  (pl_venice has 35) were silent; the viewer now looks in the map first and then in your install, as the game
  does (B485).
- **A map's own sound script overrides the stock entries.** `maps/<map>_level_sounds.txt` is read for the map
  being played, and an MvM map reads its four MvM scripts; where two scripts name the same sound, the later one
  wins, as in the game (B485).
- **Workshop maps find their sound script:** the map name is cleaned the way the game cleans it, so a workshop
  map's `_level_sounds.txt` is no longer missed (B485).

## Changes in 0.1.0-beta.25

- **The crosshair hides when TF2 hides it:** during an active minigame, under the match summary, while the
  player is frozen, and during the countdown before a competitive or casual round. The Ambassador's crosshair
  now scales as in TF2: 0.75 at rest, 2.5 times after a shot, shrinking back over half a second. Approximations
  are listed under *Known gaps* (B500).
- **Footsteps now play in 2007-2009 demos.** They were silent for every player in those demos, because the game
  changed its player flag bits between builds and the viewer read the later layout. The viewer now takes the
  bits from each demo's own schema. The same fix corrects the first-person water-jump prediction in old demos
  (B501).

## Changes in 0.1.0-beta.24

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

- **Refracting models (B506):** covered by the 0.1.0-beta.33 test suite and the merge gate. Refract trails cost no
  measurable frame rate on sanctum. No demo we hold contains a Bazaar Bargain, so this was not checked on a real
  demo; see `docs/RISKS.md` B506.
- **Early map fetch (B507):** covered by the 0.1.0-beta.32 test suite and the merge gate; see `docs/RISKS.md` B507.

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
- **Crosshair hiding and footsteps in old demos** (beta.25): the hide conditions, the frozen flag in 9-, 11- and
  32-bit flag layouts and the Ambassador scale are covered by synthetic tests with exact values; footsteps in
  the 2008 SourceTV and 2009 POV specimens went from 0 to thousands of steps, with the 2013 specimen unchanged
  as control (B500, B501).
- **Map-shipped sounds and level sound scripts** (beta.26): synthetic tests with exact values cover a map's file
  shadowing the install's, extending it, being forgotten on the next map, a later script overriding an earlier
  one, the MvM script order and workshop name cleaning; pl_venice's own wind sound is absent through the install
  and decodes once the map's file is read. Override order was settled by reading the game's sound-emitter
  binary (B485). Not yet checked by ear in a played demo.
- **Trigger soundscapes and recorded soundscape** (beta.27): synthetic tests with exact values cover trigger
  entry and exit, and the recorded parameters replacing the simulation; against a koth_lakeside POV recorded for
  this, every recorded soundscape entity matches on every tick in first person, with someone else's view as
  control. The game's missing-radius behavior was settled in its shipped engine binary (B483). Not yet checked by
  ear in a played demo.
- **Cloak and screen overlays (B508, B509):** covered by conformance and render tests and the merge gate. On the
  serveme SourceTV demo a cloaking spy rises to the 0.95 cap and reads as a red-tinted warp of the wall behind
  him; on a point-of-view demo the recorder's uber draws the red overlay at tick 95960. Compared by eye in
  `--shot` captures, not yet side by side with the game.
- **Cloak and Dagger for every spy, Your Eternal Reward corpses, Halloween stealth, animated overlays (B508,
  B509):** covered by conformance and render tests and the merge gate, each fix reverted alone and its own test
  reddened. A Cloak and Dagger spy running on an empty meter on pl_upward reads 0.5 at full speed (0.95 without
  the install's schema); the probe over eleven local matches found Cloak and Dagger on spies in eight and 39 Dead
  Ringer corpses, none cloaked. **No demo we hold has a Your Eternal Reward victim or the Halloween stealth
  spell, so those two were not checked on a real demo.** Not yet compared with the game by eye.
- **Camera monitors (B511):** covered by conformance tests and the merge gate. On the `koth_boardwalk` specimen,
  the middle stage mirror reads the soldier (R 83 G 46) with monitors on and a dark R 18 G 21 with
  `cl_drawmonitors 0`, checked by an output-level test. The extra pass cost about 0.2 ms of a 28 ms frame, inside
  that map's run-to-run noise. Not compared with the game by eye.
- **Model tangent frame (B512):** covered by conformance tests and the merge gate, each part reverted alone and
  its own test reddened. On the serveme SourceTV demo, spy 7's lit pixels move with the frame (56 of 409) and his
  cloak refraction follows it (125 of 391). Frame cost showed no difference beyond noise. Checked by eye: the owner
  looked at a first-person capture of the f12 demo at tick 3000 on 2026-10-08 and said it looks fine.
- **Recorder landing sounds (B172):** covered by conformance tests and the merge gate. On the 2009 badlands POV
  demo, 4 landings are predicted, each within one packet of a hard landing the server's own fall speed shows, with
  the same volume; the server made 7. Not yet checked by ear in a played demo.
- **Rope impulse, shake, holiday lights, cable bump, dormant ends (B478):** covered by conformance tests and the
  merge gate; each fix was sabotaged and the right test reddened. Output level, on the 2011 viaduct SourceTV rope:
  the bump term against its own control, and bulbs drawn red on grey. Impulse (4 of 239 installed maps) is in no
  demo we hold, and the lcor sweep did not finish, so it is untested on a real demo. `ShakeRopes` is
  unreachable in TF2's server code. Not yet checked by eye against the game.
- **Refract trails** (beta.31): conformance tests cover the refract shader port, the trail batching and the frame
  copy; fog was read from the game's published shader source, with the fog colour taken unscaled (an
  interpolation). Not yet checked by eye against the game (B476).
- **Skip replay and multi-tick frame order** (beta.30): HUD and animation-event sounds in skipped ticks deal the
  wave deck as the engine's skip does; timings measured on the f12 demo (forward to tick 90,006 about +4 s,
  rewind to tick 50,000 about +0.9 s). Not yet checked by ear in a played demo (B504, B505).
- **Wave deck, frame order and skip** (beta.29): synthetic tests with exact values cover the deck, the
  within-tick order and the skip; on the f12 demo's 2,629 blasts in 876 three-wave blocks, each block is a
  permutation of its waves. Each part was reverted alone and reddened its test. Settled in the game's engine
  binary and published source (B503, B504, B505). Not yet checked by ear in a played demo.
- **Sound script values and draw order** (beta.28): synthetic tests with exact values cover the narrowed volume,
  pitch and sound level, the draw order, the per-foot footstep cache, the medigun pitch and the friction sound's
  channel and volume; a shipped script's volume is checked against the game's stored value. Each part was reverted
  alone and reddened exactly its test. Settled in the game's sound-emitter binary and published source (B487,
  B502). Not yet checked by ear in a played demo.
- **Voice from every era:** Speex (2007 to 2011), Steam Voice / SILK (2011 to 2016), CELT
  (2016 to about 2018) and Opus (since).

## Known gaps

Things a user is likely to notice. Each has an entry in `docs/RISKS.md` in the repository.

- **Downloaded maps carry no checksum.** A fetched map is cached by name, not by checksum, so the
  checksum-named cache of D162 is not used for it (B507).

- **Very large demos cannot be opened in the viewer.** A demo left recording on an idle server for
  hours (the 1.3 GB and 2 GB specimens found so far) works in the command-line tool, but the
  viewer's timeline needs 40 to 84 times the file's size in memory (B449, B439).
- **The viewer needs a lot of memory.** A typical modern match uses about 4 GB once loaded (B433,
  B407); see *Requirements*.
- **A truncated 2007 SourceTV schema is only completed for the one build known to truncate it**
  (build 3258). Another launch-era SourceTV demo with a cut schema would be refused with a message
  saying so (B24).
- **Some model refract materials still show the checkerboard** (B506): the 13 HL2 and test materials that use
  `$envmap` or `_rt_Camera`. `$nowritez` and `$bumptransform` are not applied on models. None of the 242 maps we
  hold places a model that uses them, so stock TF2 maps never show it; a community map might.
- **Camera monitor leftovers (B511):** the room in a mirror looks darker than in the game (not measured). The
  monitor's view has no water views of its own and shows the main view's. The camera's `m_Resolution` is not read.
  `pd_circus` and `vsh_skirmish`, which also place a camera, were not checked.
- **Model lighting leftovers (B512):** the viewer applies no vertex flex at all, so flexed faces keep their
  unflexed normals. Animated props that are baked into the map keep their bind-pose normals and tangents.
  `$selfillumfresnel` and wrinkle maps are not ported.
- **Cloak leftovers (B508):** A taunt that sets a spy's invisibility is not
  applied over the cloak. `vm_invis` is read uncapped for other players. A cloak tint can carry over to the next
  spy that shares the material. The cosmetics on a cloaked Your Eternal Reward corpse are not cloaked.
- **Rope leftovers (B478):** a holiday bulb's roll uses this project's own random stream, and the pool of 500
  light temp entities is the lights' alone here, where the game shares it with every other temp entity. A bulb
  draws its sprite's first frame only. A rope impulse or `ShakeRopes` fires at its packet's tick, and a skip
  forward fires every event it passes. `m_skybox3d.origin` is taken as the map's `sky_camera` origin, or zero
  without one.
- **Crosshair approximations (B500):** the match-summary and minigame hides are verified only on synthetic data,
  because no demo we hold contains either. The Ambassador scale follows the server's shot time, so a shot the
  game predicts ahead of the last packet appears slightly late, and in 2009 demos, whose data lacks the shot
  time, it stays at 0.75.
- **Trigger soundscape limits (B483):** the camera has no body, so a view can enter a trigger up to 24 units
  later than a player would; the Enable and Disable inputs are not recorded in a demo, so a trigger the map turns
  off mid-match is not followed.
- **Sound wave draws are approximated (B503):** the deck is dealt as the game does, but the game's random stream
  cannot be reproduced, so each sound's random draw is the viewer's own.
- **Seeks are slower than before (B504):** forward seeks replay the skipped ticks, and a rewind restarts from
  the beginning, as the game does. Faster rewind through checkpoints is planned.
- **Landing sounds are the recorder's only, as in the game (B172).** Three of the seven hard landings in the
  demo we measured are not predicted: the landing's command is read on the same tick as the packet that
  acknowledges it (the game's order within a tick is inferred, not confirmed). Also not ported: the landing view
  punch, landing on moving or descending ground, the grappling hook's safe-fall reset, `sv_footsteps 0` and
  Mann vs. Machine's volume rule.
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
