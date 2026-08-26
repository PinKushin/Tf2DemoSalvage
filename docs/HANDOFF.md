# Handoff — thinning the view

Written 2026-08-25, twice: once at the start of a very long session and again at its end. This is the
second version and supersedes everything in the first about the viewer's structure.

**Branch `refactor/mainform-thin-view`, 28 commits ahead of `main`, nothing merged to `main` yet** —
that is deliberate and it is the owner's instruction:

> "we dont need to merge to main we need to keep sub branching and fix the rest"

> "im really not too worried about the full full gate until we are ready to merge back to main and
> start refactoring viewer 3d, which has the same fat view like issue as mainform does"

So: sub-branch off `refactor/mainform-thin-view` for each move, merge back with `--no-ff`, delete the
sub-branch. `main` waits until `MainForm` **and** the rest of `Viewer3D` are done.

Gate green across eleven projects, D1..D93 each used once, 126 numbered risks, plus **19 UI**:

| project | floor | | project | floor |
|---|---|---|---|---|
| core | 1503 | | content | 713 |
| cli | 74 | | corpus | 112 |
| logging | 17 | | viewer | 621 |
| fonts | 7 | | presentation | 159 |
| animation | 41 | | scene | 147 |
| audio | 161 | | | |

**Floors live in TWO files and both must be edited together** — `build/gate.sh` and
`.github/workflows/test.yml`. CI drifting below local is B179's defect and it has recurred twice.

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

- **SOLID**, single-responsibility in particular. `MainForm` had five jobs in one method more than
  once.
- **MVP** (D54, D62, D90): the boundary is a **compiler error**, not a convention. A presenter in a
  `net10.0` project cannot reference WinForms.
- **Swapping the frontend later with the least friction** — see §5.

### The bar, stated by the owner, and stricter than "mostly view"

> "everything that is not view gets pulled out, thin view means literally no non view code in the
> view"

> "i know the scope is large but this project needs to be a true view, no knowledge about the domain
> is allowed"

**Take that literally.** Three things follow:

- **No delegating wrappers.** `PlayerModel(player) => PlayerProps.ModelFor(...)` is the view knowing
  a domain operation exists. It is not view code because it is short. The view asks the presenter; it
  does not keep a shim.
- **`RenderFrame` splits rather than shrinks.** The message pump and the yield are view. The phase
  ORDER — sound, camera, project, advance, capture, hud, draw — is orchestration and leaves entirely.
- **The callbacks the view supplies are domain services.** Anything the form hands the scene as a
  `Func<>` is a service the form has no business owning. Four have left this way already.

**THE LINE COUNT IS NOT THE TARGET.** The owner, when an earlier version of this file named one:

> "the line count isnt a actual target, making the mainform into a true thin view is, the line count
> is just a smell that the view has domain knowledge"

So track **the member list in §2**, classified by whether a second frontend would have to reimplement
it, and work that list to zero. **The test: would a second frontend have to REIMPLEMENT this?** If
so it is not view, however short it is.

**A PARTIAL thin view is worse than no attempt** (`docs/memory/a-partial-thin-view-is-worse-than-none.md`).
A file that is 80 % view reads as permission for the other 20 %, because the next person sees a
convention already broken and matches its neighbours. Enforcement is the TFM, not the file.

---

## 1. Where the `MainForm` refactor stands

**7,409 → 5,224 lines. Code 3,039 → 2,292.** Everything below is committed on
`refactor/mainform-thin-view`.

### What has left the window

| moved | to | why that home |
|---|---|---|
| `LightAt`, `SunAt` → `LevelLighting` | Scene | `IVEngineClient::ComputeLighting( pt, pNormal, bClamp, color, pBoxColors )` at `cdll_int.h:392` — "an array of 6 … the light contribution at each box side" IS an ambient cube. Clients ask the engine; they do not carry the lightmap. |
| `ModelGeometry` → `MapAssets.Geometry` + `EntityModelSet.Geometry` | Scene | `IVModelInfo::GetStudiomodel` (`IVModelInfo.h:146`) is an interface pointer set at init, **not** a parameter threaded through every call. Passing the source per call was our invention. |
| `Sample` + 3 fields → `SoundCache` | Audio | `IEngineSound` carries `PrecacheSound`/`IsSoundPrecached`/`PrefetchSound` together (`IEngineSound.h:89-91`); game code asks rather than holding samples. |
| `BuildHud` → `FpsOverlay` | Presentation | **The name was wrong too.** The meter is `CFPSPanel : vgui::Panel` on `PANEL_TOOLS` (`vgui_int.cpp:209`), not a `CHudElement`. |
| `Decode`, `Decoded` → `DecodedDemo` | Scene | already `static` and form-free; the only thing keeping it untested was the file it sat in |
| `AddViewmodel` → `ViewmodelScene` | Scene | |
| `PlayerProps` (players → props) | Scene | Valve has **no** equivalent step — a player is already a `C_BaseAnimating` in the renderables list. Ours exists only because `DemoTimeline` splits `PlayerTracks` from `Props`, so it is our invention and needed its own tests. |
| `UpdateClientSideAnimations` | Scene | **Valve's own name.** Static batch walk (`c_baseanimating.cpp:6368`), run BEFORE simulate and bones (`cdll_client_int.cpp:2188-2210`). Ours already was. |
| `DrawList.KeepOnly`, `PoseCounters` | Scene | |
| `SoundscapeSystem` | Audio | `C_SoundscapeSystem : CBaseGameSystemPerFrame` (`c_soundscape.cpp:78`) |
| `SoundPresenter` | Presentation | `CSoundEmitterSystem : CBaseGameSystem` (`SoundEmitterSystem.cpp:134`) |
| `MapLevel`, then `LoadedMap` | Scene | `IGameSystem::LevelInitPreEntity` (`igamesystem.h:39`) — each system initialises itself from the level, and `LevelInitPreEntityAllSystems( pMapName )` takes the map NAME (`:77`) |
| `FreeCameraController` | Presentation | |
| `MomentScene` + `MomentInfo` | Scene | `SetupRenderInfo_t` carries the render origin and forward (`clientleafsystem.h:75`); `BuildRenderablesList` is TOLD where the camera is (`:169`) |
| `SpectatorView` (`IEyeSource`, `TimelineEyes`) | Scene | `C_BasePlayer::CalcView` → `CalcObserverView` → `CalcInEyeCamView`/`CalcChaseCamView`/`CalcRoamingView` (`c_baseplayer.h:112`, `:455`, `:463`) |
| `WeaponModels` | Scene | |
| `GameContent` (was `GameInstall` — collided with `SdkReference.GameInstall`) | Scene | |
| `DemoModels.Needed`/`Worn`/`ToPack` | Scene | |
| `LaunchOptions` + `LaunchOptionsReader` (was `ReadCaptureOptions`, writing into 8 fields) | Presentation | |
| `SoundCache.Precache` | Audio | |
| `EntityModelSet.Precache` | Scene | |

### Field collapses that made those moves possible

Not the goal — preparation. Each replaced a cluster of loose fields with one owner, so the member
that read them had somewhere to go.

- 8 map-lump fields → `_level` → 11 → `_loaded` (`LoadedMap`)
- `_archives` / `_classModels` / `_entityClasses` → `_game` (`GameContent`)
- six `_shot*` → `_launch` (`_shotPath` stays separate — it is *consumed*, not configuration)
- `_assetLog` deleted outright

### Remaining in `MainForm`, by role — THIS LIST IS THE TRACKER

| member | lines | role |
|---|---|---|
| constructor (menus, layout, wiring) | 753 | **view — stays** |
| `SetFullScreen` | 197 | **view — stays** |
| `RenderFrame` | 161 | **splits** — pump is view, phase order is presenter |
| `OnIdle` | 137 | **view — stays** |
| `ProcessCmdKey` | 129 | **view — stays** |
| `OnViewportHandleCreated` | 123 | **view — stays** (device creation) |
| `Apply` | 123 | **presenter — the wiring hub, see §2** |
| `ReadMap` | 116 | presenter |
| `ProjectMap` | 107 | presenter (splits) |
| `EnsureWeaponRoles` | 103 | domain |
| `FlyCamera` | 97 | presenter |
| `ToggleFirstPerson` | 92 | presenter |
| `ShowMoment` | 80 | presenter |
| `LoadDemoAsync` | 80 | presenter |
| `Dispose`, `OnDeactivate`, mouse handlers | ~250 | **view — stays** |
| `LeafBoxLines` | 71 | presenter (debug viz) |
| `CaptureViewport`, `CaptureViewportToFile` | 138 | **view — stays** (backbuffer readback) |
| `ApplyOpeningState` | 69 | presenter |
| `ShowPlayers` | 67 | presenter |
| `SetFullbright` | 66 | **view — stays** (menu state + a device flag) |
| `PrecacheSounds`, `CountFrame`, `MapCamera`, `ViewMatrix`, `PlayerModel`, slow-frame reporters | ~250 | presenter |

**The 753-line constructor stays.** Menus, layout, event wiring and control construction are exactly
what a WinForms view is for. It is the single largest member and it is not a smell.

---

## 2. What to do next, and why it is one move rather than five

**`Apply` (123 lines) is the wiring hub, and it is the last big move.** Nearly all of it is telling
collaborators about a newly-opened demo — and that is precisely the thing B193 kept breaking. It
belongs with `LoadDemoAsync`, `ShowMoment`, `ApplyOpeningState` and `RenderFrame`'s phase order in
**one presenter that owns the collaborators**:

```
_moment  _spectator  _sound  _soundscape  _sounds  _models  _playback  _clock  _game  _loaded  _launch
```

That is a substantially bigger change than any single extraction so far, and it is the one that
finishes the job — after it, what is left in the file is the constructor, the window overrides, the
device callbacks and the capture path.

**Clear the independent pieces first** so the big move is as small as it can be. In rough order of
how self-contained they are:

1. `LeafBoxLines` (71) — debug visualisation, reads only `_loaded`
2. `ShowPlayers` (67) — already nearly pass-through to `MomentScene`
3. the three slow-frame reporters (~190 together) — diagnostics over `_drawn`/`_instances`
4. `ToggleFirstPerson` (92) and `FlyCamera` (97) — camera mode, wants `SpectatorView`
5. `EnsureWeaponRoles` (103) — domain, and it is the member that already caused one regression
6. `ProjectMap` (107) — splits: the projection is Scene, the control invalidation is view
7. `ReadMap` (116) — mostly `LoadedMap.Read` already; what is left is error presentation

Then `Apply` + `LoadDemoAsync` + `ShowMoment` + `ApplyOpeningState` + `RenderFrame`'s order as one
commit, with the §3 audit run on it twice.

**After `MainForm`: the rest of `Viewer3D` has the same fault** and its tests move with it. Then the
full gate, the UI suite, and only then `main`.

---

## 3. Audit every move for WIRING, because that is what breaks

**Three regressions in one day of extracting, two of which shipped, and not one was a logic error.**
The moved body is covered by the tests written with it; what breaks is the assignment that used to be
implicit. `new TimelineViewmodels(timeline)` written INLINE becomes a `Viewmodels` property, and a
property nobody sets is null — a legal state the guard already handles, written for "no demo open
yet" and unable to tell that from "nobody wired this".

| what moved | what broke | shipped? |
|---|---|---|
| `EnsureWeaponRoles` | the call was dropped; every weapon suffix answered null | no — an analyzer saw the method go unreachable |
| `AddViewmodel` | `MomentScene.Viewmodels` never assigned; **the weapon never drew** | **yes** |
| `ShowMoment`'s upload | `MomentScene.Upload` assigned NOWHERE; **no geometry reached the GPU** | **yes** |

The viewer suite reported **620/620 green** through all three. Proved afterwards by sabotage:
`new GameAppearance(_classModels, null)` — still 620/620.

**Four passes, at the END of every move, not only when something looks wrong.** The worst of the
three was completely invisible. Each of the four found something real:

```bash
# 1. every collaborator property that is set rather than constructed. Zero is a regression.
for p in "_moment.Upload" "_moment.Viewmodels" "_moment.Appearance" "_spectator.Eyes"; do
  printf "%-24s %s\n" "$p" "$(grep -c "$p *=" managed/Tf2DemoSalvage.Viewer3D/MainForm.cs)"
done
```

2. **Diff the log STRINGS** before and after. A dropped line means a dropped code path — this is how
   the null-object no-op was found (193 sites converted, suite green, log lost 202 lines).
3. **Diff the moved BODY against the original**, line by line. This is how the ±89 pitch clamp was
   found missing from `FreeCameraController.Parse` — pitch 90 makes the camera basis degenerate
   (D65) and is an ordinary thing to paste out of the game's `ang` readout.
4. **Check that a counter which kept its NAME kept its MEANING.**

Full reasoning in `docs/memory/a-moves-regressions-are-wiring.md`.

**All three now report themselves** — `no player appearance`, `no viewmodel source`, `no model
upload` — once rather than per frame, each guarded on there actually being work to do, each with a
control test proving the legitimate case stays silent. **That is D83's requirement**: a null object
must report itself when there was work it would have done.

**Audits are worth running until one comes back clean** — the owner's rule, and the third one did.

---

## 4. Valve parity auditing — keep doing this

The owner asked repeatedly, and it paid every time. **A refactor is when the check is cheapest**
(D89): the code is being moved anyway, and a divergence written into a NEW type reads as deliberate,
which is harder to spot later than one left in an old method.

Found purely by checking while moving:

- **Soundscape choose interval was 0.25 with no citation.** Valve's is **0.2** —
  `SetNextThink( gpGlobals->curtime + 0.2 )`, `soundscape.cpp:534` and `:549`.
- **`C_SoundscapeSystem::Update` does not choose at all.** It fades loops and picks random sounds; a
  live client is TOLD its soundscape via `audioparams_t`. Choosing is `CEnvSoundscape`'s job on the
  SERVER — which is exactly why our class must exist, since a SourceTV recording carries no player's
  audio params (B173).
- **FOV applies to the free camera**, not just POV: `CalcRoamingView` ends `fov = GetFOV();`
  (`c_baseplayer.cpp:1646`).
- **`demo_fov_override` exists for exactly this program** — *"If nonzero, this value will be used to
  override FOV during demo playback"* (`c_baseplayer.cpp:120`), clamped **10..90** (`:2444`).
- **`SetupRenderInfo_t` carries the render origin and forward** (`clientleafsystem.h:75`) — Valve's
  renderables-list builder is TOLD where the camera is. That is what `MomentInfo` now is.

**Never drop something because we do not use it yet.** The owner, on my closing B194 as "nothing to
fix":

> "the reason i dont like dropping anything valve does is because i dont want to need it later and
> require a uge change"

That reversed a decision I had already recorded. The evidence that settled it: `vbsp` writes plane
normals into `LUMP_VERTNORMALS` *"because the vrad does it"* — and **vrad replaces them** with
smoothed normals, so deriving normals from plane data is only equivalent on flat unsmoothed
brushwork. The reader is now written; `SdkCoverageTests` went **27 → 29 of 66**.

And the owner's follow-up, which is the general rule:

> "yep thats why i say conformance tests first too"

`docs/SDK-COVERAGE.md` **already said "27 of 66" and already named `LUMP_VERTNORMALS`**. I
re-derived the same gap by grepping `Mod_Load*` out of `engine.dll`. The conformance instrument had
the answer before the measurement did — read it first.

---

## 5. The frontend-swap goal (D90), and the ImGui question

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

**Two open questions the owner raised, neither decided.**

> "Heck im partially tempted to get rid of the winforms form completely and just go with ImGUI right
> now so we have nothing to switch there for linux, but thats a massive change, and winforms is just
> soo easy to design and layout compared to ImGUI."

Both halves are true. **Nothing in the current work forecloses either choice** — thinning the view
helps a WinForms-forever world and an ImGui world identically. Not urgent; do not rush it for
tidiness.

> "should maps maybe become their own project… i know models dont [talk to each other]. are
> presenters suppose to talk to each other? or no?"

Answered as: **maps do not need their own project yet** — `Scene` is where a map's *meaning* lives
and `Content` already owns its *bytes*, so a third project would split one concept across three.
Revisit if `Scene` grows a second unrelated cluster. **Presenters may talk to each other**, unlike
models: a presenter's whole job is orchestration, and Valve's own game systems call each other
freely (`C_SoundscapeSystem` reads the player's audio params). What must NOT happen is a presenter
reaching *up* into the view. Recorded in `docs/DECISIONS.md`.

---

## 6. Context the owner has given that must not be lost

- **Production logging is cut to almost nothing.** *"logs are causing hiccups, so production is going
  to have to cut logging down to basically nothing"* — *"thats why things need to be set in debug for
  the most part"*. **Audit the logging in every file you touch.** Anything per-frame, per-entity or
  per-sound is `Debug`. `Information` is for once-per-load facts. B191 was ONE `Information` line
  taking a machine-wide mutex and costing 120 ms per frame.
- **`custom/` and choosable huds (D91) — AFTER parity, but it constrains design now.** Several huds
  in the folder at once with one **chosen at runtime**, which TF2 cannot do. **That is a deliberate
  step BEYOND the game and must not be "corrected" toward parity under D89.**
- **Every setting a player can change in the game is settable here** — *"it makes changing them and
  changing defaults free"*. A compiled-in value has to be argued about; a config value gets tried.
- **A real config must work wholesale** (D69): Valve's own cvar names, and ignoring unknown commands
  is the primary feature, not an afterthought.
- **Parity is the FIRST principle (D89)** — performance never buys a departure, and every measured
  win on this viewer has been a move TOWARD the engine.
- **Never revert what was asked for and works** without asking.

---

## 7. Open risks, and what each is waiting on

| risk | state |
|---|---|
| **B188** — MainForm is 87 % of the viewer | **this work**; open until the tracker in §1 is zero |
| **B192** — moments still spike to ~120 ms, fat column still subtracted | open. **Do not guess the next suspect** — five hypotheses died that way in B191. Time the remaining calls in the `Instances` loop individually. |
| **B193** — nothing catches the view failing to hand the scene a source | partly closed: all three self-report, five UI tests added. A **viewer-level** test that fails when a property goes unassigned is still wanted. |
| **B194** — the engine loads 31 world lumps, we read ~29 | vertex normals now read. `Marksurfaces` and `AreaPortals` are features; `LeafWaterData` and `BrushSides` deferred. |
| **B195** — two disagreeing answers to "what models does this demo need" | **open and NOT to be fixed inside a refactor commit.** `DemoModels.Needed` (decoded) vs `ToPack` (packed) differ on three axes: brush/sprite filter, item-schema weapons, roster vs track paths. Neither fails loudly. Unifying them changes what is drawn and wants its own measurement first. |
| **B189** — animation allocates per call where Valve writes into caller arrays | open |
| **B190** — viewmodels intermittently do not draw while the pass reports two instances | open |

---

## 8. Housekeeping — every one of these has bitten

- **The gate is TWO PHASES and one `dotnet test` on the solution is not a valid way to run it.**

  ```bash
  bash build/gate.sh
  ```

  ```bash
  pwsh -File "C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1" dotnet test tests/Tf2DemoSalvage.Viewer3D.UiTests
  ```

- **`pwsh run-exclusive.ps1 …` FAILS GREEN.** The script lives at the PinKushin root, not in this
  repo, so a bare filename is not found — and `pwsh` responds by printing its usage banner and
  **exiting 0**. Indistinguishable from a passing run except in output nobody reads. Use `-File` with
  the full path, as above.
- **`dotnet test | tail` reports the PIPELINE's exit code.** A broken build came back exit 0 with an
  empty grep. Redirect to a file and read `$?`.
- **Never filter the gate's output to summary lines while iterating** — you lose which test failed,
  and that costs a re-run.
- **`TF2DEMOSALVAGE_GCOR_ONLY=1`** is 28 s against 30 minutes. Use it for "did I break something";
  run the full superset when the change touches decoding.
- **Build servers outlive the build.** Eight MSBuild nodes plus `VBCSCompiler` held **1.4 GB** after
  one gate run — the likely cause of the owner's periodic need to restart. `build/gate.sh` now has a
  `trap 'dotnet build-server shutdown' EXIT`. **Never `pkill -f`** — over SSH it matches the shell
  running it.
- **Kill the viewer before building.** Three builds failed on a file lock, and one was followed by a
  launch that measured a **stale binary**. Same silent failure as `--no-build`.
- **The UI suite takes the desktop.** A failure while the owner is typing is not a regression. Do not
  retry-until-green; run once cleanly. 19/19 in 17 s.
- **`+developer 1`** is needed to see per-frame lines now that they are `Debug`; the UI suite passes
  it automatically because it reads the log as its instrument.
- **`git mv` fails on untracked files**, and a follow-up scripted edit then silently edits nothing.
- **A third `RecordingLogger` copy exists** (Scene, Corpus, Presentation) as a stated trade — a test
  project referencing another pulls its fixtures into discovery and breaks the exact count floors.
  **The clean fix is a `TestSupport` project** with no `[Test]` methods. Do it when a fourth copy is
  wanted.
- **Do not rerun a green gate.** Floors and docs cannot change a test result; take the counts from
  the run in hand.
