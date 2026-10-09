# 76 — The golden comparison: real TF2 and the viewer at the same tick (B161)

*Measured, 2026-10-09, live TF2 build 11087207 against the viewer at main 53456607. Every number below came out of
`golden-compare`; none is recomputed by hand.*

## The instrument

Three pieces, and nothing in the viewer changed:

| piece | what it does |
|---|---|
| the `tf2` MCP (`console`, `screenshot`) | drives the RUNNING TF2 — never launched or quit for this |
| `tools/golden-viewer-shot.sh` | builds, takes the desktop lock, one `--shot` at a 1280x720 viewport |
| probe `golden-compare` | per-region error, each side's mean colour, a diff image, the worst tile of each region as a crop |

**The TF2 recipe that works**, after the three that did not (below):

```
playdemo <name>                        # demo must be under tf/; a path with spaces breaks the viewer's lock wrapper, so copy it
host_timescale 0.001                   # BEFORE the seek; demo_timescale is silently reset by a seek and did nothing
demo_gototick <t> 0 0                  # forward seeks only — a backward one replays the demo from the start
(wait until the console log is quiet ~25 s — the seek fast-forwards visibly, then holds)
spec_player <name>; spec_mode 4; gameui_hide      # STV in-eye, as the viewer's --spectate --first-person
screenshot (MCP)                        # = devshots_screenshot; A
screenshot (MCP)                        # A2, the noise-floor control
cl_drawhud 0 → screenshot               # HUD mask
r_drawviewmodel 0 → screenshot          # viewmodel mask
r_drawskybox 0 → screenshot             # sky mask (sv_cheats is 1 on this client)
```

**Regions come from the reference game, never from us**: each mask is "what changed when TF2 stopped drawing that
layer". A mask built from our render would hide exactly the divergence it is meant to show.

**Three wrong turns, each caught by a control rather than by reading:**

1. `demo_timescale 0.001` before the seek — two captures 3 s apart showed the round clock moving 8 s. A backward
   `demo_gototick` reloads the demo and resets it; after that `host_timescale` (a cvar, survives the seek) is the one.
2. `demo_gototick` is not instant here: it fast-forwards with frames drawn. A capture one second after it was mid-seek.
   Waiting on a quiet log, then two captures that agree, is the arrival condition.
3. The viewer's `TF2VIEW_WINDOW_SIZE="1280 720"` was silently ignored — `--help` documents `width height`, the parser
   reads `WIDTHxHEIGHT` (B517). And the window is not the viewport: the frame adds 296 x 169 at every size measured,
   so 1576x889 is what yields 1280x720.

## Pinned cvars (TF2 side), and what they were

Set before `playdemo` so `mat_hdr_level` takes on the level load. **Not restored**: TF2 stopped answering RCON at the
end of the session (below), so these are still set in the running client and will be archived on its next clean exit.

| cvar | pinned | owner's value before |
|---|---|---|
| `mat_hdr_level` | 2 (TF2 default; the viewer renders HDR) | 0 |
| `r_drawviewmodel` | 1 | 0 |
| `viewmodel_fov_demo` | 54 | 54 |
| `demo_fov_override` | 0 | 0 |
| `mat_phong`, `r_3dsky` | 1, 1 | same |
| `cl_detaildist` / `cl_detailfade` | 1200 / 400 | same |
| `cl_interp` / `cl_interp_ratio` / `cl_updaterate` | 0 / 1 / 66 (the viewer's settings.cfg) | 0.015 shown / 1 / 66 |
| `mat_motion_blur_enabled` | 0 | 0 |
| `mat_picmip` | 0 | -1 |
| `mat_colorcorrection` | 0 | 0 |
| `cl_showfps`, `cl_showpos`, `net_graph` | 0 | same |
| `cl_drawhud` | 1 | 1 |
| `con_drawnotify` 0, `hud_saytext_time` 0 | console/chat text off | (not read) |
| `host_timescale` | 0.001 during capture, 1 between demos | 1 |

Also left as found and recorded: `mat_antialias 1`, `mat_forceaniso 16`, `fov_desired 90` (does not apply: an in-eye
spectator uses the target's `m_iDefaultFOV`, 90 for every f12 player per the `fov` probe), `cl_flipviewmodels 0`.
Viewer side: its own `settings.cfg` plus `+cl_showfps 0`, 1280x720.

**Asymmetries the numbers include**: our chat lines are drawn (TF2's were suppressed); an external performance
overlay appeared in one TF2 sky-mask capture (91000, top-left 370x14) and is counted as "sky" there.

## The controls — the instrument works

| control | summary mean error /255 |
|---|---|
| TF2 vs TF2, same tick, 2-3 s apart (45000) | **0.000** (max pixel 1.0) |
| same, 60000 / 75000 / 91000 | 0.385 / 0.889 / 0.380 |
| same, STV free camera 91000 | 0.421 |
| ours vs ours, camera yawed 5° (free 91000) | **35.458** |

**The noise floor is under one level in 255**, and it is not zero only where something animates in real time while
game time is crawling: tracers and the HUD (the 60000 HUD region is 1.9, world 0.24). Two TF2 captures of one tick
are, for world and viewmodel, the same picture — so timing, AA and post-processing variance between captures is not
what limits this instrument. A 5° camera error is forty times the floor.

## Parity numbers, f12 (`f12.dem`, `cp_process_f12`, STV, spectating gummo)

| case | summary | world | viewmodel | HUD | sky | world mean RGB tf2 → ours |
|---|---|---|---|---|---|---|
| 45000 in-eye (best viewer tick, 44900) | 46.0 | 41.9 | 75.5 | 56.4 | — | 102 96 85 → 106 102 93 |
| 60000 in-eye | 52.5 | 50.6 | 64.6 | 58.2 | 51.5 | 109 104 104 → 73 69 73 |
| 75000 in-eye | 38.0 | 29.0 | 72.5 | 57.0 | 107.3 | 88 89 82 → 85 84 78 |
| 91000 in-eye | 68.4 | 64.0 | 92.1 | 63.4 | 82.9 | 142 132 123 → 103 97 98 |
| 91000 free camera `-526.0 -422.4 574.9 -0.4 40.5` | 52.6 | 50.9 | — | 66.9 | 52.8 | 142 132 126 → 105 98 99 |

Sky mean RGB where present: 60000 106 108 129 → 80 73 83; 75000 177 189 206 → 100 99 92; 91000 187 210 237 →
122 127 148; free 188 210 233 → 143 156 180.

**Every case is far above the floor, and for three separate reasons that the region split pulls apart:**

1. **Outdoors we are darker; indoors we are not.** Indoor ticks (45000, 75000) agree on the world's mean colour to
   within 4 levels. Every outdoor one is 25-35% darker in world and sky, and the worst sky tile at the free camera is
   the sunlit tower beside the sun, washed out in TF2 and plain in ours. Filed **B514**.
2. **The viewmodel is on the wrong side.** gummo plays left-handed; TF2 draws his viewmodel on the left for an in-eye
   spectator, we draw it on the right. That is why the viewmodel region is the worst in every in-eye case. Filed
   **B515**.
3. **The STV spectator HUD is a different HUD.** TF2 draws the tournament spectator HUD (both teams' player panels with
   health and respawn timers, the control-point row, the target ID with health cross); we draw a timer bar and a name
   plate. Filed **B516**.

### The instrument's open fault: TF2 lands earlier than the tick asked for

*Measured, interpolation flagged.* At the free camera, the camera IS gummo's eye as TF2 reported it (`spec_pos` the
moment in-eye was switched to free roam), yet in our capture at the same nominal tick gummo stands about a body length
ahead of that camera. At 45000 the in-eye error falls from 51.3 (45000) to 41.9 (44900) and rises again at 44850 and
44800 — a minimum, but against a world error still dominated by B514, so it bounds the offset loosely (on the order
of 100 ticks) rather than measuring it. **So the in-eye numbers above include a camera misalignment of unknown size,
and only the free-camera case's static world and sky are clean of it.** The fix is a tick readout from TF2 at the
moment of capture — none of `demo_info`, `demo_debug 1`, `cl_showdemooverlay 1` printed one — and is the next step for
B161, not a reason to distrust the controls, which do not depend on it.

## Non-determinism: what the numbers include

| source | in the numbers? |
|---|---|
| particles, tracers | yes — they move in real time while `host_timescale` crawls (the 0.4-0.9 floor) |
| interpolation | TF2 interpolates (not paused); our `--shot` is a paused frame (`take-your-own-screenshot.md`), a pose up to one interval apart |
| HDR exposure (D192) | yes and uncontrolled: TF2's exposure is whatever the fast-forward seek left, ours is the viewer's settled value. It may be part of B514 |
| time of capture | TF2's landing tick, above — the largest |
| AA / post-processing | not measurable above the floor: two TF2 captures agree to 0.000 at 45000 |

## Cases that did not run

- **Modern POV demo** (`tf2-2026-pub-pov-clean.dem`, then `rgl-pug-2026-08-10-pov.dem`): a "TF2 ADVANCED OPTIONS"
  dialog opened over the frame during the first and stayed through `gameui_hide`, `gameui_activate`, `cancelselect`
  and a second `playdemo`. Closing it takes a click, and the owner was using the desktop, so it was not clicked. The
  viewer's shot exists (scratch); the TF2 half does not.
- **2009-era demo** (`tf2-2009-build3862-pov-cp_badlands.dem`): live TF2 printed `Netchannel: unknown net message (52)`
  then `Out of memory or address space. Texture quality setting may be too high.` and stopped answering RCON; the
  process stayed up. Never quit or relaunched. **The live client cannot be the reference for a 2009 demo** — the same
  wall as B201 — so era cases need a period client.
