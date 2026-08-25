# Handoff — the stall's real cause, and thinning the view

Written 2026-08-25 at the end of a very long session. Gate green across **eleven** projects,
D1..D91 each used once, plus **14 UI**:

| project | count | | project | count |
|---|---|---|---|---|
| core | 1503 | | content | 707 |
| cli | 74 | | corpus | 109 |
| logging | 17 | | viewer | 621 |
| fonts | 7 | | presentation | 139 |
| animation | 41 | | scene | 76 |
| audio | 151 | | | |

Supersedes the earlier handoff for anything about performance, audio or the viewer's structure.

---

## 0. Read this first: WHY the refactor is happening

Three days went into refactoring rather than features, and the owner named the cause exactly:

> "every time we implement a couple of new things, we have to go back and fix all the archetectural
> and parity issues, the going back over and over is the annoying part."

**That is the defect to fix, not the code.** It is recorded under D89 as a rule: before writing a new
piece, answer two questions and put the answers in the commit —

1. **What is the engine's arrangement for this job?** One grep of `F:/src/source-sdk-2013`. If Valve
   models it as a game system, a presenter or a per-frame pass, take that shape and preferably that
   NAME, so the parity is checkable by the next reader rather than rediscoverable.
2. **Which project does it belong in, and can it be tested there?** "The viewer, because that is
   where the caller lives" is the drift starting.

The project already required a conformance test with its citation before implementation, and that
works. Nothing said the same about STRUCTURE, so both questions got answered by proximity.

### The three goals the separation serves

- **SOLID**, and single-responsibility in particular. `MainForm` had five jobs in one method more
  than once.
- **MVP** (D54, D62): the boundary is a **compiler error**, not a convention. A presenter in a
  `net10.0` project cannot reference WinForms.
- **Swapping the frontend later with the least friction** — see §4.

---

## 1. What the stall actually was (B191, closed)

The owner's "every handful of seconds" freeze was **one log line taking a machine-wide lock**, then
**a disk flush per line**.

The chain, each step narrowing the last:

| split | result |
|---|---|
| frame ledger | `advance` 130 ms — but that is all of `ShowMoment` |
| `SLOW MOMENT` | `pose` 130 ms |
| lighting/viewmodel/simulate/wornlight/setup/skin | ~3 ms combined |
| `rest`, by subtraction | 126 ms |
| `reports` | 129 ms of a 133 ms pose |
| `sink` | **120.6 of 120.6 ms** |

Fixed by moving every per-frame diagnostic to `Debug` (which `developer 0` does not admit) and
guarding the WORK as well as the write — `ReportPosedExtents` was building a second full skeleton to
produce a line nobody would read.

**Measured:** slow moments ~150 over 1,470 s → 3, all in the first 8 seconds. Worst frame in a
second 120.66 ms → 13–16 ms. Log volume 431 KB → 345 KB over four minutes.

**Two lessons worth keeping, both now memories:**

- **The fat column is the subtracted one.** Every direct timer read ~1 ms while the remainder held
  126. Each new timer moved the fat column to whatever was still being subtracted; that pattern was
  the signal and was read as noise for several rounds.
- **A threshold instrument cannot see a sum.** Six frames froze on sound decode while the per-decode
  stall log fired once, because three sub-30 ms decodes in one frame never crossed the threshold.
  Time the PHASE, not the event.

**Still open: B192.** Moments still reach 60–125 ms with every measured column under 1.6 ms and
`rest` holding all of it. **Do not guess the next suspect** — in B191 five hypotheses died that way.
Time the remaining calls in the `Instances` loop individually.

---

## 2. Where the `MainForm` refactor stands

**7,409 → 6,425 lines, of which 2,981 are code.** Everything below is committed.

### Done

| moved | to | why that home |
|---|---|---|
| `LightAt`, `SunAt` → `LevelLighting` | Scene | **The engine answers this query.** `IVEngineClient::ComputeLighting( pt, pNormal, bClamp, color, pBoxColors )` at `cdll_int.h:392` — "an array of 6 … the light contribution at each box side" IS an ambient cube — and clients ask for it as `engine->ComputeLighting( pos, … )` from three separate files. 12 tests where there were none. |
| `ModelGeometry` → `MapAssets.Geometry` + `EntityModelSet.Geometry` | Scene | `IVModelInfo::GetStudiomodel` (`IVModelInfo.h:146`) is an interface pointer set at init, **not** a parameter threaded through every call. Passing the source per call was our invention and was the only thing keeping the wrapper alive. |
| `Sample` + 3 fields → `SoundCache` | Audio | `IEngineSound` carries `PrecacheSound`/`IsSoundPrecached`/`PrefetchSound` together (`IEngineSound.h:89-91`); game code asks rather than holding samples. 10 tests where there were none. |
| `BuildHud` → `FpsOverlay` | Presentation | **The name was wrong too.** The meter is `CFPSPanel : vgui::Panel` on `PANEL_TOOLS` (`vgui_int.cpp:209`), not a `CHudElement`. `HudQuad`/`HudRenderer` in Render name the screen-space LAYER and are correct. |
| `Decode`, `Decoded` → `DecodedDemo` | Scene | Already `static` and form-free — the only thing keeping it untested was the file it sat in. |
| `AddViewmodel` → `ViewmodelScene` | Scene | (earlier session) |
| `PlayerProps` (players → props) | Scene | Valve has **no** equivalent step — a player is already a `C_BaseAnimating` in the renderables list. Ours exists only because `DemoTimeline` splits `PlayerTracks` from `Props`. Our invention, so it needed its own tests. |
| `UpdateClientSideAnimations` | Scene (`EntityModelSet`) | **Valve's own name.** `C_BaseAnimating::UpdateClientSideAnimations()` is a static batch walk (`c_baseanimating.cpp:6368`), run BEFORE simulate and bones (`cdll_client_int.cpp:2188-2210`). Ours already was. |
| `DrawList.KeepOnly` | Scene | six duplicated lines whose intermediate copy is load-bearing |
| `PoseCounters` | Scene | 13 report parameters → 10; 24 lines of locals → 3 |
| `SoundscapeSystem` | Audio | `C_SoundscapeSystem : CBaseGameSystemPerFrame` (`c_soundscape.cpp:78`) |
| `SoundPresenter` | Presentation | `CSoundEmitterSystem : CBaseGameSystem` (`SoundEmitterSystem.cpp:134`), calling through an interface as Valve calls through `enginesound` |
| `MapLevel` | Scene | `IGameSystem::LevelInitPreEntity` (`igamesystem.h:39`) — each system initialises itself from the level |
| `FreeCameraController` | Presentation | needs `MapOutline` (Scene) and `OverheadPlacement` (Presentation); the only window input was one float |

### Remaining in `MainForm`, by role

**This list is the tracker** — work it to zero, and read the line count only as a symptom.

| member | lines | role |
|---|---|---|
| constructor (menus, layout, wiring) | 739 | **view — stays** |
| `ShowMoment` | 287 | presenter |
| `ReadMap` | 237 | presenter |
| `SetFullScreen` | 197 | **view — stays** |
| `ProjectMap` | 173 | presenter |
| `RenderFrame` | 161 | **splits** — pump is view, phase order is presenter |
| `ReportWeapons` | 146 | presenter (diagnostics over `_drawn`/`_instances`) |
| `ReadCaptureOptions` | 138 | presenter (CLI parsing) |
| `OnIdle` | 137 | **view — stays** |
| `ProcessCmdKey` | 129 | **view — stays** |
| `AddViewmodel` | 128 | presenter |
| `OnViewportHandleCreated` | 116 | **view — stays** (device creation) |
| `Apply` | 114 | presenter |
| `FlyCamera` | 97 | presenter |
| `LoadDemoAsync` | 79 | presenter |
| `OnDeactivate`, `Dispose`, mouse handlers | ~270 | **view — stays** |
| `PrecacheModels` | 74 | presenter |
| `ReportSlowMoment`/`ReportSlowSounds`/`ReportSlowFrame` | 194 | presenter (diagnostics) |
| `LeafBoxLines` | 71 | presenter (debug viz) |
| `ApplyOpeningState` | 69 | presenter |
| `ToggleFirstPerson` | 68 | presenter |
| `ShowPlayers` | 67 | presenter |
| `HeldWeaponModels`, `EnsureWeaponRoles` | 126 | domain |
| `CountFrame` | 65 | presenter (metrics) |
| `FirstPersonCamera`, `PlayerAt`, `Spectated`, `Ducking`, `FreeLookCamera`, `MapCamera`, `ViewMatrix` | ~300 | presenter — this is Valve's `CalcView` dispatch (`c_baseplayer.h:112`, `:455`, `:463`) |

**The next move is the `ShowMoment` cluster, and it is one move rather than several**, because
`ShowMoment`, `AddViewmodel`, `ReportWeapons`, `ShowPlayers` and `ReportSlowMoment` all operate on
the same state: `_players`, `_props`, `_drawn`, `_instances`, `_models`, `_lighting`, the viewmodel
scene and the pose counters. That state IS the scene for the current moment, and the only thing in
the whole cluster that needs a window is `device.UploadModels(_models)` — one call, which wants an
interface the way `LevelLighting` and `SoundCache` took one.

**The owner's instruction: finish `MainForm.cs` before touching the rest of `Viewer3D`** — *"we will
forget about mainforms if you switched I guarantee it lol"*. Other files in the project have the same
fault; they wait.

### The bar, stated by the owner and stricter than "mostly view"

> "everything that is not view gets pulled out, thin view means literally no non view code in the
> view"

**Take that literally.** Three things follow that an earlier estimate in this file got wrong:

- **No delegating wrappers.** `PlayerModel(player) => PlayerProps.ModelFor(...)` is the view knowing
  a domain operation exists. It is not view code because it is short. The view asks the presenter;
  it does not keep a shim.
- **`RenderFrame` splits rather than shrinks.** The message pump and the yield are view. The phase
  ORDER — sound, camera, project, advance, capture, hud, draw — is orchestration and leaves entirely.
- **The callbacks the view supplies are domain services.** `LightAt`, `SunAt`, `Sample`,
  `ModelGeometry` are handed to the scene by the form today. The form has no business owning them.

**THE LINE COUNT IS NOT THE TARGET, and reading it as one sends the work the wrong way.** The
owner, 2026-08-25:

> "the line count isnt a actual target, making the mainform into a true thin view is, the line
> count is just a smell that the view has domain knowledge"

So track the LIST — every member, classified by whether a second frontend would have to
reimplement it — and work that list to zero. An earlier version of this file named
"~1,200–1,600 total lines" as the end state, which was wrong twice over: wrong as a target at all,
and wrong as an estimate, because at this comment density a member is roughly one line of code to
two of commentary. Measured on 2026-08-25 the file was 6,513 lines: **883 blank, 1,517 doc, 1,074
inline, 3,039 code.** A thin view lands somewhere near a thousand lines of code — but that is a
consequence, not the goal.

**The test:** would a second frontend have to REIMPLEMENT this? If so it is not view, however
short it is.

---

## 3. Valve parity auditing — keep doing this

The owner asked repeatedly, and it paid every time. **A refactor is when the check is cheapest**
(D89): the code is being moved anyway, and a divergence written into a NEW type reads as deliberate,
which is harder to spot later than one left in an old method.

Found this session, purely by checking while moving:

- **Soundscape choose interval was 0.25 with no citation.** Valve's is **0.2** —
  `SetNextThink( gpGlobals->curtime + 0.2 )`, `soundscape.cpp:534` and `:549`.
- **`C_SoundscapeSystem::Update` does not choose at all.** It fades loops and picks random sounds; a
  live client is TOLD its soundscape via `audioparams_t` in private player data. Choosing is
  `CEnvSoundscape`'s job on the SERVER — which is exactly why our class must exist, since a SourceTV
  recording carries no player's audio params (B173).
- **FOV applies to the free camera**, not just POV: `CalcRoamingView` ends `fov = GetFOV();`
  (`c_baseplayer.cpp:1646`).
- **`demo_fov_override` exists for exactly this program** — *"If nonzero, this value will be used to
  override FOV during demo playback"* (`c_baseplayer.cpp:120`), clamped **10..90** (`:2444`). So our
  default of 90 is the widest the game itself will watch a demo at, not a departure.
- **`SetupRenderInfo_t` carries the render origin and forward** (`clientleafsystem.h:75`) — Valve's
  renderables-list builder is TOLD where the camera is. `ShowMoment(tick)` reads it off the form,
  which is the coupling that makes it need a window. **That is the direction the rest should move.**

**Also caught by reading rather than by tests:** my extracted `FreeCameraController.Parse` had
dropped the ±89 pitch clamp that `MainForm.ParseCamera` had. Pitch 90 is an ordinary thing to paste
out of the game's `ang` readout and makes the camera basis degenerate (D65). Restored. **A move is
not automatically faithful — diff the behaviour, not just the shape.**

---

## 4. The frontend-swap goal (D90), and the ImGui question

The acceptance test for all of this, in the owner's words:

> "we should be easily able to replace the view with something that runs on linux… and not have to
> touch anything outside the winforms view and d3d"

**Only two projects carry a `-windows` TFM:**

| Windows-pinned | everything else |
|---|---|
| `Tf2DemoSalvage.Viewer3D` (WinForms) | Core, Content, Scene, Animation, Audio, Presentation, **Render**, Logging, Cli |
| `Tf2DemoSalvage.Fonts` (GDI rasteriser) | |

`Render` is `net10.0` — D3D11 arrives through Silk.NET, a runtime dependency rather than a
compile-time one. A port therefore needs a frontend, a different backend through the same Silk.NET
family, and FreeType instead of GDI. **What it must not need is a reimplementation of the viewer**,
and every line of presenter logic still in `Viewer3D` is exactly that.

**Open question the owner raised, not decided:**

> "Heck im partially tempted to get rid of the winforms form completely and just go with ImGUI right
> now so we have nothing to switch there for linux, but thats a massive change, and winforms is just
> soo easy to design and layout compared to ImGUI."

Both halves are true and it is genuinely balanced. **Nothing in the current work forecloses either
choice** — thinning the view helps a WinForms-forever world and an ImGui world identically, because
in both the presenter logic has to leave the form. Deciding it is not urgent and should not be
rushed for tidiness.

---

## 5. Context the owner has given that must not be lost

- **`custom/` and choosable huds (D91) — AFTER parity, but it constrains design now.** A `custom/`
  folder laid out as modern TF2 lays it out; several huds in it at once with one **chosen at
  runtime**, which TF2 cannot do; and an importer so nobody has to find the folder. **The hud choice
  is a deliberate step BEYOND the game and must not be "corrected" toward parity under D89.**
- **Every setting a player can change in the game is settable here** — *"it makes changing them and
  changing defaults free"*. A compiled-in value has to be argued about; a config value gets tried.
  The world FOV was compiled into three places until today.
- **A real config must work wholesale** (D69): Valve's own cvar names, and ignoring unknown commands
  is the primary feature, not an afterthought.
- **Parity is the FIRST principle (D89)** — performance never buys a departure, and every measured
  win on this viewer has been a move TOWARD the engine.

---

## 6. Housekeeping notes worth knowing

- **Kill the viewer before building.** Three builds this session failed on a file lock, and one was
  followed by a launch that measured a **stale binary**. Numbers from it were discarded. Same silent
  failure as `--no-build`.
- **The UI suite takes the desktop.** A failure while the owner is typing is not a regression — it
  happened once here and a clean run was 14/14. Do not retry-until-green; run once cleanly.
- **CI floors had drifted below the local gate again** (B179's defect) and were corrected: core
  1497→1503, content 686→707, audio 116→151, corpus 106→109. **Watch the next CI run** — it builds
  Release, and if a count legitimately differs there, the floors are now strict enough to say so.
- **`+developer 1`** is needed to see per-frame lines now that they are `Debug`; the UI suite passes
  it automatically because it reads the log as its instrument.
