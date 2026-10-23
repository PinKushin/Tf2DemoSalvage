# Measurement 2026-10-22: the empty-view floor on both sides, TF2's own budget, and an equal-workload pair (f12)

**Status: written and committed before the run.** Sections above `## Result` are fixed from here; deviations go under Result.

## Observation

`2026-10-22-tf2-vs-viewer-f12.md` paired TF2 at ~207 fps (4.8 ms) against the viewer at ~75 fps (13.4 ms) on `tf/f12.dem`,
gummo first person from tick 30000. Finding 101 read the viewer's main thread as a long tail. An independent review pointed out
three gaps: the empty-view control was planned on 10-21 and never measured (deviation 4 there), so "long tail" and "floor" were
never separated; TF2's own per-category budget was never read; and the pair was not of equal workloads (TF2 ran `ultra.cfg`,
`mat_hdr_level 0`, LDR; the viewer ran HDR). The `tf2-measure` skill and B259/B572 say the viewer's sample/drawlist/models/placement
walk every prop regardless of visibility.

## Question

1. **Floor:** with (almost) nothing in view at f12 tick ~30000, how many ms per frame does each side pay? If the viewer's empty
   view costs 6-8 ms, most of the 13.4 ms is a floor paid regardless of visibility, not a tail of visible work.
2. **TF2's budget:** what does TF2's own `showbudget` / `vprof` report per category on the gummo first-person scene?
3. **Equal workload:** with both sides LDR, what is the ratio?

## Prediction

1. Viewer empty view 5-8 ms (finding 88 read 5.9 ms on an empty view); TF2 empty view under 2 ms (>500 fps).
2. TF2 world + static props + models together under 3 ms; swap/present a visible share.
3. Viewer LDR 0-15% faster than its HDR run; ratio still above 2x.

## Method

All under one hold of the machine-wide lock (`run-exclusive.ps1` running a sleep, waiter on `ACQUIRED`), so viewer runs go straight
to the exe (they would otherwise wait on my own lock). Build: `origin/main` `436f571a`, Debug,
`MSBUILDDISABLENODEREUSE=1 dotnet build managed/Tf2DemoSalvage.Viewer3D`. Demo: `D:/measure-f12/f12.dem` (the space-free copy of
TF2's `tf/f12.dem`). Load per run: `(Get-CimInstance Win32_Processor).LoadPercentage` before and after.

**Viewer:**
- (1) one `--shot` with `TF2VIEW_CAMERA="1613 -2821 154 89 0"` (pitch 89, straight up; the skill's empty-view camera) at tick 30000,
  no `--first-person`, to prove the view is empty; then
  `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 TF2VIEW_CAMERA="…" tf2demoview.exe D:/measure-f12/f12.dem --tick 30000 --measure 20 +fps_max 0`.
  If the shot is not empty, pick another pitch/position and re-shoot before measuring. Record frames, mean, and the parts lines.
- (3) `… --tick 30000 --spectate gummo --first-person --measure 20 +fps_max 0` twice: once as before (HDR) and once with
  `+mat_hdr_level 0` (LDR, B537), back to back.

**TF2** (`mcp__tf2__*`; Steam up; no sign-in): read first and record `fps_max`, `mat_vsync`, `cl_showfps`, `engine_no_focus_sleep`,
`mat_hdr_level`, `sv_cheats`, `sv_allow_wait_command`, `spec_autodirector`; set `fps_max 0`, `mat_vsync 0`, `engine_no_focus_sleep 0`,
`cl_showfps 2`. `playdemo f12`, `demo_gototick 30000 0 0`.
- (1) a free camera (`spec_mode 6`/`7` or whatever works in playback) turned to look straight up (`+lookup` or the mouse), ONE
  screenshot before the window to prove the sky, then several screenshot readings ~5 s apart after the fps has settled.
- (2) `spec_player gummo; spec_mode 4`; `+showbudget` (or `vprof_on`/`vprof_reset`, ~5 s, `vprof_generate_report`), screenshot or
  console text. If it needs `sv_cheats 1`: read, set, restore, read back; if refused in playback, say so.
- (3) gummo first person is the LDR reading (`mat_hdr_level` read back as 0): several readings, the pair for the viewer's LDR run.
- After: restore every ConVar changed, read back, quit TF2.

Focus check (`GetForegroundWindow` process, must be `tf_win64`) at every reading; no screenshot inside a reading window except the
reading itself, and readings only after the fps has recovered from the previous one. TF2's counter is a smoothed display; the
viewer's figure is frames counted over playback seconds.

## Control

- **Empty is empty:** one picture per side before its window; a non-empty picture voids that reading.
- **Viewer instrument:** ticks over the window ~1,333; the HDR run must land within 15% of the 10-22 pair's 13.4 ms (else drift is
  stated and the LDR delta reported against today's HDR run only).
- **TF2 instrument:** the 10-22 record's `fps_max 60` control read 59; repeated here only if a reading looks implausible.
- **Budget reading:** the panel's total must be within ~30% of the frame time `cl_showfps` shows at the same moment, else the
  budget is listed as unreconciled.

## Result

Raw outputs beside this file in `2026-10-22-empty-view-budget-ldr-f12/`: `viewer-empty.txt`, `viewer-gummo-hdr.txt`,
`viewer-gummo-ldr.txt` (whole `--measure` reports), `viewer-empty-shot.png` (the empty-view proof). Lock held 23:25:53 to ~00:00.
Build `436f571a` + this record, 0 warnings, 0 errors.

**The machine was not quiet, and that governs everything below.** CPU load read 80-100% around the viewer runs (47/62 empty,
87/40 HDR, 26/59 LDR) and 88-90% during TF2. At 23:57 the per-process counter showed `vbcscompiler` at 371% (another agent's
build — the lock covers desktop work, not builds), plus a long-running Floorp. The 10-22 pair ran at 13-19% load.

### (1) Empty-view floor

**Viewer** (`TF2VIEW_CAMERA="1613 -2821 154 89 0"`, no `--first-person`; the shot shows only sky and the HUD): **2,743 frames in
20 playback s, mean 7.29 ms (137 fps)**, ending tick 31347. Phases (ms): camera 3.55, hud 1.15, advance 1.06, draw 0.62,
between 0.51, sound 0.40. The `moment cost` (rebuild) lines: 1.4-3.9 ms = sample 0.4-1.1, drawlist 0.2-1.0, models 0.1-0.4,
pose 0.7-2.2 (`posed 0 of ~1,870 selected, ~1,640 hidden by pvs`). The remaining camera time is particles: decals 0.5-0.7,
bursts/dispatches 0.17-0.45, beams 0.17 (per-second `parts:` lines). JIT 4.6 s during the run.

Against the gummo first-person run on the same machine at the same time (HDR, 18.09 ms): **the empty view costs about 40% of the
full frame**. camera 3.55 vs 11.19, draw 0.62 vs 2.97, hud 1.15 vs 1.23, advance 1.06 vs 1.48. The pose stage is where the
difference sits (0.7-2.2 empty, 3.2-9.5 gummo); sample/drawlist/models stay at about 0.5/0.2-0.7/0.1-0.2 either way.

**TF2: not measured.** In demo playback the free camera could not be pitched from the console: `spec_mode 6` then `setang -89 0 0`
(with `sv_cheats 1` too), `+lookup`/`-lookup` in separate calls with `cl_pitchspeed 1000`, and `+lookup; wait 600; -lookup` all
left the view unchanged (screenshots). The first `spec_mode 6` before choosing a player put the camera outside the map, a mostly
black void with part of the map in view: 140 fps, 7.2 ms (one reading, under load, **not an empty view**, not counted). Turning
it with the mouse through computer-use was started and **the owner interrupted it**, so it was not retried.

### (2) TF2's own budget

`+showbudget` draws in demo playback (screenshot taken, the panel's labels overlap at 1280x720 and are not legible enough to
transcribe). **`vprof_on; vprof_reset`, about 15 s on gummo first person, then `vprof_generate_report` worked** with
`sv_cheats 1` (read 0 before, set 1, restored 0, read back 0); it prints to `tf/console.log`, not to the RCON reply
(`vprof_generate_report_budget` printed nothing). The log file was read in place; it is not committed (13 MB, the owner's
file). Over 2,308 engine frames (15.3 s, so about 151 fps with vprof on, under the load above), per engine frame
(Avg/Call of once-per-frame scopes, or Avg/Frame x 2 — vprof counts two "frames" per `CEngine::Frame`):

| scope | ms / engine frame |
|---|---:|
| `CEngine::Frame` (all) | 6.63 |
| `CViewRender::Render` (all 3D + its children) | 3.83 |
| `C_BaseAnimating::SetupBones` (466,149 calls, self 0.69) | 0.96 incl. |
| `C_BaseAnimating::DrawModel` (220,163 calls) | 0.84 |
| `CViewRender::DrawOpaqueRenderables` (x2 views) | 1.03 |
| `CViewRender::DrawTranslucentRenderables` (x2) | 0.63 |
| `ParticleMgr()->Simulate` | 0.57 |
| `CParticleMSG::UpdateNewEffects` | 0.52 |
| `Draw3dSkyboxworld` | 0.51 |
| `CEngineVGui::Simulate` + `Paint` | 0.39 + 0.39 |
| `DrawWorld` (x2) | 0.34 |
| `_Host_RunFrame_Sound` | 0.30 |
| `ComputeShadowTextures` (x2) | 0.29 |
| `UpdateClientSideAnimations` | 0.29 |
| `Client SimulateEntities` | 0.24 |
| `CNewParticleEffect::DrawModel` | 0.24 |
| `BuildWorldRenderLists` (x2) | 0.21 |
| `CStaticPropMgr::DrawStaticProps` | 0.19 |
| `CClientLeafSystem::PreRender` (x2) | 0.17 |
| `Mat_ThreadedEndframe` | 0.10 |
| `CMaterialSystem::SwapBuffers` | 0.001 |

Read: TF2's main thread spends ~1.8 ms on animation (SetupBones + DrawModel), ~1.1 ms on particles, ~0.8 on VGUI, ~0.5 on world
(`DrawWorld` + render lists), 0.19 on static props, 0.3 on sound; Swap is nothing on the main thread (the queued material
system, finding 99). **Budget control:** vprof's 6.63 ms frame vs `cl_showfps` 6.0-8.3 ms read on the same scene minutes earlier
(roaming and chase cameras, 120-167 fps) — inside 30%.

### (3) Equal workload (LDR both sides)

Chosen direction: the viewer down to LDR (`+mat_hdr_level 0`, B537), because TF2 already ran `mat_hdr_level 0` (read back 0
at start and end; the owner's `ultra.cfg`). **Viewer, back to back:** HDR **1,106 frames, 18.09 ms (55.3 fps)**; LDR **1,180
frames, 16.96 ms (59.0 fps)** — LDR 6% cheaper (camera 10.68 vs 11.19, draw 2.75 vs 2.97). **Drift control failed:** the HDR
run is 35% slower than the 10-22 pair's 13.4 ms, outside the 15% limit; the machine load explains it.

**TF2 LDR, gummo first person** (`cl_showfps 2`): 200 fps / 4.1 ms (23:57:1x, foreground `tf_win64` checked at 23:57:30, load 88%),
68 fps / 15.9 ms (23:57:3x), 52 fps / 38.0 ms (23:57:5x, focus check failed to run — the type was lost between calls — load 90%,
`vbcscompiler` 371%). Three readings spanning 4x; the last two coincide with the other agent's compile. The round clock in the
pictures jumped backwards (3:33 → 3:43 → 3:49) — the demo was replaying earlier parts (a seek or the `playdemo` looping), so the
tick region is not pinned either. No ratio is computed from these.

### Deviations

1. **The machine was loaded** (above) — every number is a contended one; the 10-22 pair is not comparable to these.
2. TF2's empty view not measured (camera could not be pitched; the owner interrupted the mouse attempt).
3. `cl_pitchspeed` was set to 1000 **without being read first**; restored to 225 (the default, as its readback listed no
   different default) and read back 225.
4. Viewer runs went straight to the exe under my own lock hold (as instructed), via `Start-Process -RedirectStandardOutput`
   (the shell redirect is hook-blocked and the GUI exe writes nothing to a pipe otherwise).
5. TF2 readings were taken within ~20 s of a `screenshot`; with the load swinging, the screenshot-dip question is moot here.
6. ConVars restored and read back after `disconnect`: `sv_cheats 0`, `engine_no_focus_sleep 50`, `cl_showfps 0`,
   `spec_autodirector 1`, `cl_pitchspeed 225`, `sv_allow_wait_command 1`; `fps_max 0`, `mat_vsync 0`, `mat_hdr_level 0`
   unchanged. `vprof_off`. TF2 quit.

## Verdict

1. **Viewer floor: confirmed in shape, inconclusive in value.** With nothing in view the viewer pays **7.29 ms** (prediction 5-8),
   about 40% of the same-time full frame (18.09). The floor is spread: camera 3.55 (of which rebuild ~1.5-2 with pose still ~1 ms
   for posing *zero* models, and particles/decals ~1.5), hud 1.15, advance 1.06, draw 0.62, between 0.51, sound 0.40. So the gap
   is **both**: ~7 ms of floor and ~11 ms more that scales with what is visible — mostly `pose` (3-9.5 ms with gummo's view vs
   ~1 empty). Under a loaded CPU; repeat quiet before quoting. **TF2's floor: not measured.**
2. **TF2's budget: obtained** (vprof, table above). Its biggest main-thread items are SetupBones+DrawModel ~1.8 ms, particles
   ~1.1, VGUI ~0.8, world ~0.5, static props 0.19, sound 0.3. Against ours: TF2's whole animation is ~1.8 ms where our `pose`
   is 3-9.5; TF2's HUD (VGUI) is ~0.8 where ours is 1.2; TF2's sound 0.3 against ours 0.4-0.5.
3. **Equal workload: inconclusive.** Viewer LDR is only 6% cheaper than HDR (so HDR is not the gap); TF2's LDR readings were
   taken under a compile and span 52-200 fps.

**For the plan (candidates only, no code):** the pose stage (`pose` in the rebuild line) is the largest visible-scaling item
and TF2 does its equivalent in ~1.8 ms; the empty-view floor's named items are already B572's list (decals, burst offers,
exposure readback, log flush, transport bar) plus `hud` 1.15 ms and `advance` 1.06 ms with nothing on screen. HDR/bloom is not
worth chasing for fps. Repeat (1)-(3) on a quiet machine, and find a way to pitch TF2's demo free camera (a bound key held, or
`demoui`'s camera) for its floor.
