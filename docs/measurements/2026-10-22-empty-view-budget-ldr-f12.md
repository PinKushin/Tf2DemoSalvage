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
