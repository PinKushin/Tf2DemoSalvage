# Tf2DemoSalvage 0.1.0-beta.52

## Unreleased

- **Player animation variants are picked the way TF2 picks them.** Where a class model has several sequences for one
  activity, the viewer now chooses at random by their weights when the player enters the activity and holds that choice
  until the activity changes, instead of always showing the heaviest. Gestures (flinches, reloads, voice commands) pick
  their variant once when they start, the same way. Picks are reproducible: seeking to a moment shows the variant that
  playing through to it showed, with three exceptions — a player who had been in that activity for more than 2048 ticks
  (31 seconds on a 66-tick server), a change too brief for playback at your frame rate to see, and a point-of-view
  demo's recorder, whose movement is read differently while seeking.
- **The cost.** About 140 KB of memory, no per-frame allocation while playing (docs/measurements/2026-10-25-d233-activity-tuples-cost.md).
- **A seek** spends about 9 ms (up to 57 ms) recovering 10 players' variants on the f12 demo, an estimated 22 ms for 24 (docs/measurements/2026-10-25-d233-activity-tuples-cost.md).

## Changes in 0.1.0-beta.52

Released 2026-10-25.

- **The HUD's texture sheets are now loaded when the HUD starts, as in TF2,** not at the first kill drawn with each one.
  Before, the main thread read each sheet from disk in the middle of play. In one cold-disk run that read cost 361.1 ms and 223.9 ms inside two stalls (`docs/measurements/2026-10-25-b595-sync-io-in-hud-and-pose.md`).
  Warm disk, `cp_process_f12`: main-thread file reads in play went from 45 to 33 over 20 s, none of them a HUD sheet (`docs/measurements/2026-10-25-b595-sync-io-in-hud-and-pose.md`).
  **Not shown yet:** a cold-disk run after the change. The reads moved out of play; a stall disappearing was not observed, so treat the cold-disk benefit as expected, not measured.
- **The first-person view builds its draw list faster.** A set of props was compared member by member; it now compares by reference. What is drawn does not change.
  `cp_process_f12`, first person, `drawlist` per frame: 0.56 and 0.46 ms before, 0.22 and 0.21 ms after (`docs/measurements/2026-10-24-drawlist-parts.md`).
  The filter part: 0.30-0.35 ms before, 0.06 ms after (`docs/measurements/2026-10-24-drawlist-parts.md`). This was in beta.51 and was left out of its notes.

## Changes in 0.1.0-beta.51

Released 2026-10-25.

- **The viewer's log file is now written in the background (D230).** Writing it used to pause playback for up to about a
  tenth of a second now and then (`docs/measurements/2026-10-24-d230-async-file-log.md`). A log on disk lags by a fraction of a second while the viewer runs, and it is complete
  after a normal close or a crash the viewer can report. Ending a frozen viewer with Task Manager still leaves its log current up to the freeze
  (see *Known gaps*).
  Main-thread time spent writing the log, per 60 s run on `cp_process_f12`: 5.6-691.1 ms before, 0.0 ms after (`docs/measurements/2026-10-24-d230-async-file-log.md`).
  **This removes the log's share of the hitches, not all of them.** Frames over 100 ms across five 60 s runs: 7 before, 4 after, all four in one run, as `hud` and `pose` stalls that show no log time (`docs/measurements/2026-10-24-d230-async-file-log.md`).
  Their cause is not yet explained; one candidate, `hud` texture reads, is addressed in beta.52 but not shown to be the cause.
- **Two repeated log lines are quieter.** The viewmodel skin line is written once per skin change, and the muzzle-flash
  line is written only at Debug level. Each used to be flushed to disk from the frame (`docs/measurements/2026-10-24-b595-first-use-builds.md`,
  `docs/measurements/2026-10-24-muzzle-flash-log-line.md`). What is drawn does not change.

## Changes in 0.1.0-beta.50

Released 2026-10-24.

- **Detail props (grass and small sprites) cost almost nothing to place.** Like TF2, the viewer now reaches only the
  detail props in the leaves near the camera instead of every prop in the map. The drawn detail is unchanged.
  On `cp_process_f12`, first person: detail step 0.55 ms to 0.002 ms a frame (measurements/2026-10-23-b580-detail-reach.md).
  Whole frame about 13.1 ms to about 12.3 ms (measurements/2026-10-23-b580-detail-reach.md).
- **The HUD event feed no longer walks the whole match every frame.** What the HUD shows is unchanged.
  Empty view: its stage 0.13-0.20 ms to 0-0.02 ms a frame (measurements/2026-10-23-hud-event-feed-window.md).
- **The viewer now draws on a second thread by default**, as TF2 does. `mat_queue_mode` defaults to TF2's own `-1`:
  on a machine with two or more physical CPU cores each frame is drawn on a second thread, and on one core it is drawn
  on the main thread as before. On the author's `cp_process_f12` recording this was measured at about 14% faster.
  Frame time: 13.04 ms to 11.25 ms (finding 98, measurements/2026-10-23-queued-render-thread-rung-2.md).
  To get the old behaviour back, set `mat_queue_mode 0` in `settings.cfg`, or use `+mat_queue_mode 0` for one run.
  A `settings.cfg` saved by an earlier version keeps following the default, because the viewer writes defaults as
  comments.

## Changes in 0.1.0-beta.49

Released 2026-10-23.

- **New setting: `mat_queue_mode`.** Set it to 2 to draw each frame on a second thread, as TF2 does with its own setting
  of that name. It is off by default, so the viewer behaves as before. It was measured on the author's `cp_process_f12`
  recording, in first person at 1280x720, over repeated alternating runs on a quiet machine.
  Frame time: 13.04 ms to 11.25 ms, 76.7 to 88.9 fps, about 14% faster (finding 98, measurements/2026-10-23-queued-render-thread-rung-2.md).
  TF2 turns this on by itself when the machine has two or more physical CPU cores; the viewer does not yet, and a later
  release may make it the default after more testing.
- **Screenshots, `--shot` and seeking wait for the second thread** to finish its frame before they read anything.
- **Mirrors and monitors show their own camera's view.** On maps such as Boardwalk they used to draw the main view's
  geometry, so the room behind the player showed as black (finding 98).
- **Alt+Enter no longer starts DXGI's own full screen switch**, which could hang the viewer in the threaded mode. Full
  screen still works through the viewer's own control.
- **Developer note.** `--measure` prints two new parts, `camera wait` and `queue render thread`.

## Changes in 0.1.0-beta.48

Released 2026-10-22.

- **A sound no longer freezes a frame the first time it plays.** A sound that was not loaded yet used to be read from
  the game files and decoded in the middle of a frame. It now loads in the background and starts as soon as it is
  ready, which is how TF2 treats a sound whose data has not arrived. On the author's `cp_process_f12` recording, over
  60 seconds of playback, sounds read or decoded on the render thread went from 5 to 0 (finding 90).
  The one hitch the game's own log flagged, a 171 ms stall on a voice line, is gone (finding 90). The trade: such a
  sound starts 1 to 28 simulation ticks late the first time it is heard (finding 90), and a stop that arrives while it
  is still loading now cancels it. A stop for a sound that was never loaded no longer loads it.
- **Less memory churn per frame.** Seven more per-frame allocations are gone (corpse physics, corpse animation
  copies, layer lists, placement matrices, the healing beam and the HUD's vertex array). On the same recording the
  render thread went from 1.21-1.26 MB to 0.94-1.01 MB allocated per frame (finding 91), and all threads together
  from 2.50-2.98 MB to 1.54-1.83 MB per frame (finding 91), with fewer garbage-collection passes in 60 seconds
  (1,631-1,903 to 945-1,124, finding 91). Frame times did not change measurably, so this release makes no frame-rate
  claim for it.
- **Developer notes.** Findings 90 and 91 and risks B568, B570 and B574 in the repository's `docs` folder record how
  these were measured.

## Changes in 0.1.0-beta.47

Released 2026-10-21.

- **The viewer draws faster.** In first person on the author's `cp_process_f12` recording, a frame went from about
  16.5-17.1 ms to about 14.5-15.3 ms, and with nothing in view from about 7.3 ms to about 5.6 ms. The largest single
  part was a one-millisecond wait after every frame that served only accessibility and test tools. It now runs only
  when such a tool is attached to the window.
- **Less memory churn per frame.** Memory allocated per frame fell from about 7.7 MB to about 5.1 MB over all threads
  (the render thread from about 4.0 MB to 2.2 MB), because skeleton poses and inverse-kinematics lists are reused.
  This also shortens the collector's pauses.
- **Fewer playback hitches from sounds.** Weapon draw and reload sounds are loaded while the demo loads instead of
  the first time they play. On that recording, misses during play fell from 12 a minute to 4.
- **A saved screenshot appears only when the whole image is on disk**, so a tool or a file watcher can no longer pick
  up a half-written PNG.
- **A particle effect whose owner is out of view for a while and then returns is no longer lost.** Before, it stayed
  gone until you scrubbed.
- **Developer options.** `--measure` also prints the memory allocated per frame by the render thread, and lists
  stalls beside whether the whole process paused.

## Changes in 0.1.0-beta.46

Released 2026-10-21.

- **Large demos open faster.** Building the timeline for a 58 MB match (the `cp_process_f12` recording) went from about
  170 seconds to about 100 seconds on the author's machine, with the same memory use.
- **Fewer hitches during playback.** Surface sounds (footsteps, impacts, scrapes) are decoded while the demo loads
  instead of the first time they play. Rocket-jump smoke effects that had already finished are no longer rebuilt all at
  once after the camera moves.
- **Particle effects follow the game's rules for what is drawn and what sleeps.** An effect that is out of view is not
  drawn and goes to sleep after a few seconds, as in TF2, and one frame of particle simulation is limited to 0.1
  seconds, as in the game. On the author's f12 test the picture is unchanged: the comparison images differ only by the
  frame-rate counter and nine pixels that are off by two shades out of 255.
- **Developer option.** `--measure` prints new lines for JIT compilation and for particle work per frame.

## Changes in 0.1.0-beta.45

Released 2026-10-20. Betas 0.1.0-beta.16 to 0.1.0-beta.44 were never published, so this is the first public build since beta.15. The
sections below cover every change since, one per version.

- **`--shot` writes a companion file beside the picture.** `--shot out.png` also writes `out.png.json`: the demo's
  file name and its sha256, the map, the tick, the camera, the view size, the settings that differ from their
  defaults, the build and the command line (file names only). You can rebuild a capture from it later (D226).
  Deleting a screenshot from the viewer's tidy-up deletes its companion too. The F12 key writes one only in a
  Debug build.
- **`--like <shot.png.json>` reproduces a TF2 capture.** Give it the companion file of a TF2-side capture and the
  viewer takes its tick, camera, view size and console variables, and its demo too, from the file's folder, when
  you name none. A `--tick` or `+cvar` you pass still wins; the camera and view-size environment variables do not.
  If the demo you open is not the one the file names (different sha256), the viewer warns and still shoots. A
  file with a bad camera, or one whose demo is a path rather than a file name, is refused with the reason.
- **Static props are no longer walked every frame.** A map's static props (1,353 on `cp_process_f12`) are filed
  once and only the ones in view are posed; the picture is identical pixel for pixel in the two golden pairs
  taken (finding 83, item 1). Measured on one machine, on f12 at tick 30000 in a Debug viewer, looking at nothing
  (`TF2VIEW_CAMERA="1613 -2821 154 89 0"`): the simulate phase fell from 1.2-1.4 ms to 0.5-0.7 ms a frame and
  the draw-list phase from 0.4-0.6 ms to 0.1-0.2 ms. The whole frame ranged 7.7-9.0 ms before and 6.0-9.4 ms
  after, with other work on the machine, so quote the phases, not the frame. A static prop you look at is still
  animated when it has an animation, which the game does not do (B560).
- **A demo whose map is missing still opens**, and the status bar says why (D224). The viewer fetches a map it
  lacks, so you should rarely see this. TF2 itself refuses such a demo. **Unverified:** that the message reaches
  the status bar on every path (see *Known gaps*).

- **`TF2VIEW_VIEWPORT_SIZE`** (`"1280x720"`, WIDTHxHEIGHT) sizes the 3D view itself; the window grows around it,
  so the view is that size at any display scaling. A value that does not parse is logged and ignored. For scripted
  captures; `--help` lists it.
- **Demo voice plays.** Voice chat recorded in a demo is heard through five voice channels, as TF2 plays it; a sixth
  speaker at once is dropped, as in the game. `voice_scale` and `voice_enable` are read. Not yet ported: the
  lowering of other sounds while someone speaks and the speaking indicator (B549, see *Known gaps*).
  Not checked by ear in a played demo.
- **`snd_musicvolume` lowers music**, as TF2 does: the sounds with a `#` in their name and no distance falloff,
  including map soundscapes, and a change reaches music already playing (B545).
- **Animated props draw their real pose.** A door or a resupply locker that animates is now posed by its bones
  instead of from one stored copy per animation step. Before, the last step of a one-shot animation blended toward
  the closed pose, so on f12 a door at tick 6520 drew shut where TF2 draws it open (finding 80). The change is
  not measurable in frame time: eight 20-second runs on f12 at tick 30000 averaged 40.1 ms (before) and 40.3 ms
  (after). A few props still differ slightly; see *Known gaps*.
- **A building or prop shows its state at the moment you are watching**, not the demo's last one. Its model, item,
  class, team, parent, owner and attachments are read for the current tick. On f12 a sentry (entity 457) drew its
  end-of-demo level 3 at tick 66600, where it was level 1 (B548).
- **Parts that move with a door are placed and lit where the door puts them.** Every child of a `func_door` on
  badwater, upward and vigil was lit from its own offset, and a grandchild was drawn only one level deep (B551).
- **Effects end where TF2 ends them.** An effect whose owner went dormant now stops emitting and fades, and one
  nothing is drawing sleeps, as TF2's particle code does, where they used to hang in the air and pile up. On f12,
  60 seconds from tick 30000, live effects stayed between 8 and 41 where they had climbed from 65 to 199
  (merge `3315abde`; finding 79). A seek replays the effects as playing does (D221).
- **Particles step once per frame on that frame's own points and time**, as TF2 does (D221, B539).
- **Smoother playback on a busy scene.** A dead player's physics was rebuilt every frame: 48.7 ms a rebuild, about 1 ms
  now. Below that, TF2's HUD texture sizes are read once, and a first-person arm nobody can see is not posed.
- **Less memory on a map.** Measured on f12, a Debug viewer, `--measure 20` (finding 80); run-to-run spread is
  about 400 MB, and the figures are one change each, not a sum:
  - A map's screen overlays decode their animated normal map once, not once per frame: the load's transient peak
    fell from 2,245 to 940 MB, the viewer's peak working set from 9.3 to 7.6 GB and peak private bytes from 11.1 to
    9.5 GB.
  - The allocation fixes behind finding 80's "after the four fixes" re-census (particle lookups, face flex
    arrays, decals, sound conversion; the finding lists each) took the large allocations of 20 seconds of playback from 1,867-2,241 MB to 67-87 MB; private bytes at the end of
    playback went from 10,689 to 8,494-8,733 MB and the working set from 8,170 to 6,015-6,249 MB.
  - Sounds are held as 16-bit, and the sound cache is capped at 512 MB, least recently used out first (the cap is
    our figure; TF2's is not in the SDK). WAV memory 387 to 194 MB; private bytes at the end 8,493-8,514 to
    8,298-8,305 MB.
  - Animated props from bones (above): private bytes at the end 7,378 and 7,370 MB before, 6,045 and 6,087 after.
  - A map load reads each texture file once, particle definitions are one compact copy per TF2 install
    (managed heap after a load, 1,026.0 to 934.2 MB), and a block texture's upload no longer copies its mip levels.
  - **The load's peak did not move**: the upload still peaks at 9.3-11.1 GB private bytes in those runs.
    The six-versus-six figures under *Requirements* have not been re-measured.
- **A map's own particle file is read** (`maps/<name>_particles.txt`, or `particles.txt` in the map), so a community
  map's custom effects appear, as in TF2 (B554, finding 81). **A particle declared twice keeps the later
  definition**, which TF2 does and the viewer did not, so 30 stock effects now come from a different file than
  before: `ExplosionCore_MidAir` and four `Explosions_MA_*` (now `bigboom.pcf`), the eleven `_unusual_parent_*`
  (now `weapon_unusual_energyorb.pcf`), `blood_decap`, `taunt_gasblast_steam`, `mini_firework*`, the three
  `speech_medic*` and others (B555). Not compared on screen in a demo.
- **The stock HUD is closer to TF2's** (finding 78; it applies when you do not pass `--hud`): the ammo and
  class backgrounds take their shape, soft edge and outline from the game's masks instead of flat blocks; the
  class background follows your team (it was red for a BLU player); the class model stands in its activity pose;
  HUD colors are gamma-corrected, which TF2's tan text needs; the ammo number paints over its shadow (B552); and
  a medic gets the ÜberCharge meter. The class portrait is drawn at tone-map scale 1, which accounts for its being
  about 20 luma darker than TF2's. The ammo golden is live (B552); the class and ÜberCharge goldens stay
  Explicit (B559): whether the portrait now matches TF2's is for those goldens to show.
- **The program has its own icon**, a film reel with a play hub in brown and gold, replacing the Windows default.
- **Developer options.** `--measure` also prints the GC's own memory and the large allocations by type.
  `--windows` (in `--help`) walks the demo into 2-second checkpoints and logs, per seek, what a window cost and
  whether it matched; nothing drawn changes (D217).

## Changes in 0.1.0-beta.44

- **Volume control.** View > Volume sets the sound volume in tenths, and `volume` works as in TF2: in a config,
  as `+volume 0.5` at launch, and in binds such as `bind m "toggle volume 0 1"` or `incrementvar volume 0 1 0.1`.
  The menu remembers its choice. **The viewer now goes silent while another window has focus**, as TF2 does by
  default; untick View > Volume > Mute when not focused, or set `snd_mute_losefocus 0`, to keep the sound
  (D218). `snd_musicvolume` and `voice_scale` are accepted but change nothing yet (B545).
- **A key that sets a console variable now takes effect immediately.** Binds like `bind x "cl_showpos 1"`, and alias
  toggles that set a variable, used to change nothing on screen. They now apply live, as a launch option does.
- **`toggle`, `incrementvar` and `multvar` work in binds**, the same way TF2 runs them: `bind x "toggle cl_showpos"`
  turns the position readout on and off, and `bind y "incrementvar cl_showpos 0 2 1"` cycles it.
- **The Settings menu and your key binds now agree.** A bind starts from whatever the menu last set, so turning the
  position readout on in the menu and then pressing a `toggle cl_showpos` key turns it off on the first press, and a
  value a key sets shows as checked in the menu.
- **A console variable reads back the way TF2 stores it.** The value is kept as you typed it. The exception is a
  variable with limits that your value went past: it then reads back as the limit. Three variables have limits:
  `viewmodel_fov` (0.1 to 179.9), `cl_interp` (0 to 0.5) and `mat_vsync` (0 to 1). This matters to `toggle` with a
  list of values, which moves on from the value the variable actually holds. A variable the viewer does not
  implement is also remembered once, not twice (B543).
- **Keyboard binds from your config now work.** Before, only mouse buttons and the free camera's movement keys reached
  your binds, so a keyboard key bound to `toggle`, a ConVar or an alias (such as a scoreboard toggle) did nothing, or ran
  the viewer's own shortcut for that key instead. Every key now goes to its bind, as in TF2; where your config binds a
  key the viewer also uses, your bind wins. Escape always leaves full screen (B547).
- **The viewer sleeps when another application is active, and the menu no longer writes launch options.**
  Along with the muting above, the viewer now sleeps when it is not the active window (`engine_no_focus_sleep`),
  as TF2 does, and wakes when you return. Choices made in the menus are remembered as settings only; they are
  never saved as launch options (D218).
- **A long match needs much less memory for its timeline.** The timeline now shares what does not change from
  one moment to the next. On the f12 demo it fell from 2,948 MB to 1,235 MB, with the picture and every answer
  unchanged (D217).
- **TF2's spectator panel on SourceTV and spectator-view demos.** With `cl_use_tournament_specgui 1` in your
  config you get the tournament player cards (name, health, respawn time, charge, portrait, team border);
  otherwise you get TF2's stock bars, as the game does (B516).
- **`mat_hdr_level 1` draws as TF2 does:** low dynamic range with bloom, no longer the same as level 0 (B537).
- **The sun's glow is drawn.** Sprites and particles also take the right brightness scale under the tone map,
  so tinted sprites are no longer too pale (B378).
- **A medic's heal beam, seen from the medic's own eyes, starts at the weapon's muzzle** instead of at the
  camera (B539).

## Changes in 0.1.0-beta.43

- **A paused demo no longer plays footsteps or other animation-event sounds.** Before, a demo that opened paused
  sounded the opening tick's footsteps (or not, depending on when the window changed during the load), and a seek
  made while paused sounded the steps at the new tick. TF2 fires no animation event on a paused frame, and now the
  viewer does not either. On the first frame after you press play, each player may sound one catch-up step. That
  is what TF2 does (B541).
- **`--spectate` with a name nobody has warns once.** The log line "is not playing at tick ... following the
  default" was written on every check, over a hundred times before a capture's first seek. It is now written once
  for each absence (B540).
- **Keys work again after a menu item is picked by a screen reader.** Picking a menu item through UI Automation
  left the window in menu mode: Home and End on the speed slider did nothing, and SPACE opened the title bar menu
  instead of switching the camera, until you clicked somewhere. The viewer now leaves that state by itself (B542).

## Changes in 0.1.0-beta.42

This release closes the gaps found by laying the viewer's picture beside TF2's for one moment of one demo (chase
and in-eye views of a demoman), and by reading more of your config the way the game reads it. Players, heal beams,
glows and the chase camera change the most.

- **Soldier and medic jumps play again, and medics walk in the air.** A line in each class's script that the game
  treats as a comment had been read as a setting, so every soldier and medic jump drew the standing reference
  pose and medics never took the air-walk pose (B523).
- **Canteens draw their empty skin**, not the team's skin, outside Mann vs. Machine (B523).
- **Medigun heal beams are brighter and the right colour.** The glow on the patient was too pale and too thin.
  A beam you land on by seeking now follows where its two ends were, a beam that has just been released keeps
  moving as the game's does, and a beam ends on the patient where he is drawn (B523, B527, B538).
- **Overhealed players show team-coloured pluses**, and a running patient sheds a trail of them (B538).
- **Tinted sprite particles are more saturated everywhere.** The game converts a particle's tint from gamma and
  the viewer had not, so every tinted sprite particle, not only the heal beam, was drawn too pale. This was seen
  on screen on the heal beam only (B538).
- **Particle cards honour their material's size and fade limits**, so a card very near the camera shrinks and
  fades out as in the game (B529).
- **A paused frame keeps heal beams and pluses behind a moving patient**, as the game does, where it had put
  them on him (B538).
- **Chase camera.** It pulls in at walls and eases back out when paused or after a seek instead of snapping to
  full distance, and it stops at another player standing behind the target (B528, B533).
  `spec_autodirector 0` in your config gives the fixed-direction chase the game shows on SourceTV demos, which
  looks along the map and does not turn with its target (B535).
- **Glows follow the game's depth rule.** The covered share of a glow is measured over the game's depth range,
  and `r_dopixelvisibility 0` in your config gives line-of-sight glows, as in the game (B378).
- **More of your config is honoured:** `mat_hdr_level`, `spec_autodirector`, `tf_use_min_viewmodels`,
  `mat_specular`, `mat_fullbright` and `r_dopixelvisibility`. With `mat_hdr_level` 0 or 1 the map is drawn from
  its low-dynamic-range lighting with no tone mapping, as the game does (a change takes a map reload). While a
  demo plays the game uses `viewmodel_fov_demo`, not `viewmodel_fov`, and so does the viewer. The demo's own
  `sv_restrict_aspect_ratio_fov` is read for the widescreen field of view (B537, B525, B535, B378, B518).
- **Viewmodels bob while the demo plays**, and `tf_use_min_viewmodels` draws the minimized viewmodels (B525).
- **Reflections.** Cosmetics take `$envmapfresnel`, and a shiny model's reflection loses the contrast and
  saturation shaping the game does not apply to it (B521).
- **Blended terrain with a second normal map is lit through both**, and blended terrain under water uses the plain
  blend, as the game does (B514).
- **Material and script text is read the way the game reads it.** Seven materials regain settings that an empty
  value had cut off (a gold Tomislav, two zombie skins and others), and 83 settings take their PC value instead
  of the Xbox one. A `.cfg` is read as the game reads it, so `bind k ""` and alias bodies come out right
  (B524, B532).
- **The floating health icon and target id follow the player where he is drawn**, and medigun sounds start from
  the drawn position, where both had led him by the interpolation delay (B526, B531).
- **Auto-exposure adapts at the game's rate during playback.** It had been starved of its measurement, so the
  scene's brightness lagged. A `--shot` no longer waits long for it (B520).
- **Headless runs fail loudly.** A path split by spaces, or `--spectate` with a name nobody has, exits with a
  non-zero code and a message instead of capturing the wrong thing (B522). A dead player named by `--spectate`
  is shown from behind, as SourceTV shows one.
- **The lighting-only debug view (`mat_fullbright 2`) matches the game's brightness.** It had been about 2.3 times
  too bright (B539).
- The `--help` text for `--spectate` now says a dead player is shown from behind and that a name nobody has
  exits 1 with `--shot` or `--measure` (B522).

## Changes in 0.1.0-beta.41

This release is about making the picture match TF2's. Brightness, glow and light now follow the game's own rules
in many places, so most scenes look different from beta.40 (usually brighter or closer to the game's).

- **Widescreen field of view matches TF2.** On a wide window the world's field of view is widened by the aspect
  ratio as the game does it, instead of staying at the 4:3 value (B518).
- **Left-handed viewmodels flip.** A weapon the game draws mirrored, or a spectated player who has
  `m_bFlipViewModels` set, is mirrored in first person. Your own `cl_flipviewmodels` setting is read for the
  recorder (B515).
- **Bloom.** The glow around bright areas such as the sun and the sky is drawn, driven by the map's tone-map
  settings, as in the game (B514).
- **Auto-exposure.** The scene's brightness adapts to what you are looking at and settles over a moment, as in
  the game, instead of staying at one fixed exposure. The sky takes the same brightness scale as the world, and a
  screenshot of a still waits for the exposure to settle before it is taken (B514, B519, B520).
- **Lightmaps keep the game's full range.** Very bright map light (overbright) is kept instead of being
  clipped, so lit areas that were flat now show their gradation (B514).
- **Surfaces with bump maps are brighter, as in the game.** Their baked light is normalized the way the game
  does it, which lifts bump-lit walls and floors (B514).
- **Blended terrain edges follow the blend texture.** Where two ground materials meet, the edge follows the
  material's blend-modulate texture rather than a straight fade (B514).
- **Props with bump or shiny materials are lit like TF2.** They take the map's light at their position instead
  of baked vertex colours, and props under the open sky pick up the sun (B514).
- **Tinted model materials are darker, and unlit ones take no light.** A model's colour tint now acts in the
  right colour space, which darkens tinted and painted models, and cards the game draws unlit (such as
  3D skybox trees) no longer pick up baked shading (B514).
- **Player and model skin shading is always half-Lambert**, as the game's skin shader draws it, so the shadowed
  side of a player or weapon is softer (B514).
- **Reflections on models are weaker where the game masks them.** A shiny model's reflection is masked by its
  texture the way the game masks it, and a plain model's reflection tint acts in the right colour space. Pipes,
  lamp fixtures, weapons and players lose a reflection that was too strong (B521).
- **Glows fade by how much of them is covered.** A lamp halo or sprite glow partly hidden by a wall or a model
  is dimmed by the covered share and fades in and out, as in the game, instead of switching on and off (B378).
- **Glowing sprites, beams, halos and trails match TF2's brightness.** They are now added the way the game adds
  them, which is brighter over dark surfaces. Lamp spotlight shafts also take the right brightness and fade out
  along their length, where they had been near-solid white cones (B521).
- **Map lighting shifts by up to one lightmap texel on every brush face.** Shadow edges and light now sit where
  TF2 puts them, instead of being squeezed toward one corner of each face. This changes the look of nearly
  every map slightly (B521).
- **The viewer reads more of your config.** `cl_flipviewmodels`, `mat_disable_bloom`,
  `mat_force_tonemap_scale` and `cl_drawhud` now take effect, read the way the game reads them (B515, B514).
- The `--help` text for `TF2VIEW_WINDOW_POS` and `TF2VIEW_WINDOW_SIZE` now shows the formats the viewer reads
  (`85,78` and `1576x889`) (B517).

## Changes in 0.1.0-beta.40

- **TF2 faces now move.** Taunt and voice-line scene expressions and flex animations drive the face as the game
  does, and a voice line's sentence data lip-syncs the mouth. Overlapping scenes blend, faces keep running while
  off-screen so they are right when they come into view, and a respawned player's corpse shows its own face. Flexed
  vertices also move their normals and tangents, so the lighting follows (B513).
- **Cached sound lengths were doubled and are now correct.** The sample rate of a cached sound was read at the
  wrong bit, which halved the rate and doubled every length. Sounds that end by length now end on time.
- Measurement runs moved to GitHub Actions (D212). Nothing changes for users.

## Changes in 0.1.0-beta.39

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

- **Memory and speed (beta.45):** every figure is in this section's memory and playback bullets
  with its run (f12, Debug viewer, `--measure 20`); sources are finding 80 and the merge commits named there.
  Not measured: a Release viewer's memory, p50 and p99 frame time (`--measure` prints means), and the six-versus-six
  match of *Requirements*.
- **Merge gate (beta.45):** all twelve assemblies passed (core 2661, cli 75, logging 17, fonts 5, animation 5339,
  scene 3173, audio 366, presentation 1227, content 1814, corpus 230, rendering 1178, viewer 152), with the UI suite at
  82 of 82 passed and six `[Explicit]` goldens not run (the two class-and-health portraits, the ÜberCharge meter, the
  lakeside sun, granary at level 0, and the golden-measure diagnostic). Presentation then gained 3 test cases and passed
  1230 of 1230. CI on the pushed commit was green.
- **Static props filed once (beta.45):** conformance tests cover the gather (facing away visits none, facing two
  of three visits those two, filed once over several frames, an entity as control). Golden shots before and after
  on f12 at tick 30000 (first person, and a free camera on the map's trees) are identical pixel for pixel outside
  the frame-time text (finding 83). Measured on one machine, shared at the time.
- **Capture companion file and `--like` (beta.45):** covered by tests of the written fields and of `--like`
  refusing a bad camera and a path-valued demo; the five committed TF2 goldens carry backfilled companions
  (D226). Not run against a live TF2 capture yet.
- **Merge gate (beta.43):** all twelve assemblies passed (core 2481, cli 75, logging 17, fonts 5, animation 5338,
  scene 2995, audio 295, presentation 988, content 1768, corpus 224, rendering 1128, viewer 135), and the UI suite
  passed 54 of 54. The paused-frame fix is checked by a conformance test written from the game's published source
  and by a UI test. **Not checked by ear in a played demo** (B541).
- **Picture parity (beta.42, B514 to B539):** the viewer was compared against the live game with a golden
  comparison on the f12 demo, and each fix above was made from the game's published source or its engine binary
  and covered by tests and the merge gate. Re-measured after these changes at tick 60000, as linear brightness
  over a 16 by 9 grid of tiles against the game's own screenshot from the same camera: the indoor comparison
  camera reads 96 to 102% of TF2 in every tile, and the outdoor camera reads 92 to 105% in every tile but one,
  whose difference is a control point's state. The merge gate and the UI suite (52 of 52) passed, and CI's Test
  workflow is green again. Players and weapons were compared with the game at one moment of that demo (chase and
  in-eye views of a demoman, near tick 63600), and the differences found were fixed except the heal glow cloud
  (see *Known gaps*). **Not seen on screen:** the viewmodel bob, which only moves while a demo plays, and most
  of the particle tint changes, which were seen on the heal beam only.

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
- **Face flex (B513):** covered by conformance tests and the merge gate, each part reverted alone and its test
  reddened. Medic 687 flex animation on `koth_product` (medic 12: 3,785 vertices differ from the no-animation
  control); scout voice-line lip sync on granary 2013 (2,548 vertices differ from the no-sentence control); soldier
  laugh taunt (7,965 to 8,105 vertices moving, furthest 1.79 units, none before the scene); spy corpse 24 wears its
  own face. Frame cost showed no difference beyond noise. Not compared with the game by eye.
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

- **Occasional stalls of 50-190 ms remain** in play on a busy scene, with an unexplained cause (B595). On `cp_process_f12`, first person, five 60 s runs after the log writer change: 4 frames over 100 ms, all in one run (`docs/measurements/2026-10-24-d230-async-file-log.md`).
  Known so far: not JIT, not first-use builds, no longer the log; most are pose or particle time. The 8-run precache comparison was run on a loaded machine, so it does not settle them (`docs/measurements/2026-10-25-b595-part6-main-thread-reads-in-play.md`).

- **Ending the viewer with Task Manager can lose the last moment of its log**, usually about a millisecond of lines and
  at worst about 55 ms (`docs/measurements/2026-10-24-d230-async-file-log.md`). A normal close, or a crash the viewer can report, loses nothing; a crash that takes the process down without warning
  is the same as Task Manager.

- **The first twenty seconds or so of playback run slower than steady state** while the runtime optimizes the viewer's
  code. Turning that off made loading much slower and playback slower too, so it was reverted. Still open.
- **Frame rate is not yet where we want it.** On the author's f12 first-person test it is roughly 65 to 69 frames per
  second. The game itself, playing the same recording from the same player's view at the same size on the same machine, ran
  at about 190 frames per second, so the viewer is about 2.5 to 3 times slower (the author's measurement, one recording and
  one player; treat the ratio as an estimate; see `docs/measurements/2026-10-21-tf2-vs-viewer-f12.md`). This is the current
  priority.
- **A sound's first play can still stall for a few hundred milliseconds on a busy machine.** The cause of that wait
  is not yet understood; the game itself logged a blocked first sound read of about 170 ms on the same machine, so it may
  be the disk or the system and not the viewer.

- **The "no map" message is unverified** (B556, D224). When a demo's map cannot be fetched or found, the viewer
  is meant to say so in the status bar. No test has opened a demo with its map absent by each route (a failed
  download, a map the install lacks, the library, the command line), so on some route the bar may say nothing.
- **A static prop in view animates when it has an animation**, which the game's static-prop draw never does
  (B560). This was so before beta.45.
- **A sun glow seen through a water reflection or a monitor** is not tested separately for being covered (B378).
- **The spectator HUD is not complete** (B546): the control-point lock row, the target ID's health number and the
  overheal color of a player card's health still differ from the game's.
- **The heal glow beside a running patient is partly fixed** (B538, B539): the beam's arc toward the aim still
  differs from the game's, and the haze is fainter, with fewer pluses along the beam.
- **`mat_hdr_level 3` is drawn as 2** (B537).
- **Demo voice is partly ported** (B549): TF2 lowers every other sound while someone speaks and shows a speaking
  indicator, and the viewer does neither. A voice's position is fixed when it starts, the output device resamples
  where the game does it itself, `cl_voice_filter` is not offered, and after a seek voice resumes at the next packet.
- **A seek silences every sound** and brings back only the loops (B550). TF2's demo skip keeps what is playing, so
  a song heard before a seek is gone after it.
- **Some HUD materials' texture transforms, addressing modes and `$vertexcolor` are not read** (B553).
- **Two animated props still differ from the game's pose** (finding 80): `w_breadmonster` (cause not traced) and
  a model whose first blended animation is a single frame, which still draws one pose. No such model was found on
  f12. A pakfile that ships a particle file under a stock path is also not modeled (finding 81).
- **`snd_musicvolume` lowers music but never ducks it** (B545). It applies to the sounds TF2 treats as music,
  which are `#` sounds with no distance falloff, including map soundscapes. A change takes effect on music that
  is already playing. TF2 also turns music down briefly while someone speaks, and the viewer does not.
- **Some outdoor details still differ in brightness from the game** (B514 is open): a band of ground changes
  brightness at some moments of the f12 demo. Players and weapons were compared with the game at one moment of
  one demo (a demoman, near tick 63600), and every difference found there was fixed except the heal glow cloud
  above. Other classes, weapons and moments were not compared.
- **Downloaded maps carry no checksum.** A fetched map is cached by name, not by checksum, so the
  checksum-named cache of D162 is not used for it (B507).

- **Very large demos cannot be opened in the viewer.** A demo left recording on an idle server for
  hours (the 1.3 GB and 2 GB specimens found so far) works in the command-line tool, but the
  viewer's timeline still needs too much memory to open them (B449, B439). A typical match now needs
  much less than before (D217: the f12 timeline is 21 times the file, down from 34).
- **The viewer needs a lot of memory.** Measured 2026-10-15 on a 58 MB six-versus-six match: about 9 GB at the
  peak while it loads (10.4 GB in 0.1.0-beta.43) and about 6.4 GB once playing. The "about 4 GB" these notes gave
  before was wrong. See *Requirements* (B433, B407, B449).
- **A truncated 2007 SourceTV schema is only completed for the one build known to truncate it**
  (build 3258). Another launch-era SourceTV demo with a cut schema would be refused with a message
  saying so (B24).
- **Some model refract materials still show the checkerboard** (B506): the 13 HL2 and test materials that use
  `$envmap` or `_rt_Camera`. `$nowritez` and `$bumptransform` are not applied on models. None of the 242 maps we
  hold places a model that uses them, so stock TF2 maps never show it; a community map might.
- **Camera monitor leftovers (B511):** the room in a mirror looks darker than in the game (not measured). The
  monitor's view has no water views of its own and shows the main view's. The camera's `m_Resolution` is not read.
  `pd_circus` and `vsh_skirmish`, which also place a camera, were not checked.
- **Model lighting leftovers (B512):** `$selfillumfresnel` is not ported.
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
- **Gesture and overlay layer animation events are not sounded** (B541).
- **Cosmetics are not drawn in first person** (B186).
- **Switching demos without restarting the viewer gets slower** (B148).
- **Demos recorded in late June 2011 were malformed by the game itself** and may not decode (B144).
- **The first-person camera's movement differs slightly from the game's** in velocity and feet yaw
  (B450).

## Requirements

- **Windows 10 or 11, x64**, with a Direct3D 11 GPU.
- **16 GB of RAM.** A full match peaks at about 9 GB while it loads and holds about 6.4 GB while playing (measured
  2026-10-15, B433). With 8 GB the system pages heavily or the viewer runs out of memory.
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
