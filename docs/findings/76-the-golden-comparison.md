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
   reads `WIDTHxHEIGHT` (B517, since closed by correcting the help). And the window is not the viewport: the frame adds 296 x 169 at every size measured,
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

### After B515: the viewmodel on the right side

*Measured, 2026-10-09, branch `fix/b515-flip-viewmodels`, against the SAME stored TF2 captures (live TF2 was not
driven).* Only the viewer side was re-shot, at the same ticks, args and window as above.

| case | viewmodel before → after | summary before → after | viewmodel mean RGB tf2 / ours after |
|---|---|---|---|
| 45000 (viewer 44900) | 75.5 → **25.9** | 46.0 → 38.3 | 96 88 80 / 85 79 72 |
| 60000 | 64.6 → **30.9** | 52.5 → 48.0 | 101 94 87 / 93 86 81 |
| 75000 | 72.5 → **31.7** | 38.0 → 31.5 | 51 50 45 / 74 70 64 |
| 91000 | 92.1 → **24.8** | 68.4 → 58.2 | 103 94 84 / 98 89 79 |

World and HUD moved too (by 1-6), because a weapon drawn on the wrong side covered world pixels TF2 showed and left
bare ones TF2 covered. What remains in the viewmodel region is mostly the landing-tick offset below — a weapon mid-
animation at a different moment — and B514's lighting; nothing in it is handedness.

### B514: the outdoor gap is bloom and exposure, not lighting

*Measured, 2026-10-09, branch `fix/b514-outdoor-brightness`, against the SAME stored TF2 captures; live TF2 was not
running and was not driven, so no new TF2 capture exists. Engine behaviour read from published source except where
marked.* "Before" is main at 88916034 re-shot (the free-camera shot reproduces the stored one to the digit, 52.580 —
the viewer is deterministic); "after" is this branch.

**The instrument grew one column.** `golden-compare` now prints, per region, the ratio of mean LINEAR luminance
TF2/ours over pixels clipped in neither capture, and each side's clipped share. An exposure difference is one
multiplier in linear light across world and sky; a lighting difference is world-only; a sky-texture one sky-only.

**Separating the four candidates, on the free camera (no timing in it):**

| candidate | test | result |
|---|---|---|
| (d) sun / lightmaps / ambient | the sky has no lighting at all; is it short by the world's factor? | yes — 1.56 sky vs 1.54 world (linear). Not a world-only term |
| (c) the sky texture's HDR scale | the same comparison read the other way | the world is short too, so not sky-only |
| (b) bloom | read `GetBloomAmount`, `Generate8BitBloomTexture` | missing entirely, and large: on a flat 0.5-linear frame bloom 0.5 adds 48 levels (`BloomRenderTests`). TF2 clips 69.5% of the sky; we clipped 0.1% |
| (a) auto-exposure | read `viewpostprocess.cpp` and the controller | the map pins the range to `[0.5, 0.7]`, so it can only DARKEN relative to a scale of 1 — the opposite of the gap. Left open (below) |

**What the engine does, branch by branch** (*read from published source*):

- `C_EnvTonemapController::OnDataChanged` copies all seven fields to the `g_bUseCustom*`/`g_flCustom*` globals and
  claims `g_hTonemapControllerInUse`; its destructor clears the three flags only (`c_env_tonemap_controller.cpp:70-96`).
  cp_process_f12 has one controller per round: tick 1 (and 347, 41686, 47773, 56070, 75982, 100887 in `f12.dem`) it is
  0.5 / 0.7 / bloom 0.5, and at each round restart the old one is deleted and a fresh all-zero one sends for thirteen
  ticks before the map's `logic_auto` re-applies the values (*measured*, `autoexposure f12`). **So for thirteen ticks a
  round, exposure and bloom fall back to the cvars** — a mapper's choice no viewer of the map would guess.
- `GetBloomAmount` (`:1421-1468`) walks `currentBloomAmount` toward the controller's scale (or `mat_bloomscale` 1) by
  5% of the gap per FRAME, from a static of 1 that is never reset — frame-rate dependent as written.
- `Generate8BitBloomTexture` (`:1522-1598`): a quarter-size downsample whose four taps are `pow(c, 2.2) · dot(c, (0.3,
  0.59, 0.11))` on GAMMA values (`Downsample_nohdr_ps2x.fxc`, sRGB read off on Windows), a 13-tap horizontal blur, the
  same vertically times the bloom amount. **The vertical blur steps one over the target's WIDTH**
  (`BlurFilterY.cpp:88-89`: `int height = src_texture->GetActualWidth()`), so on a 16:9 screen it reaches 56% as far
  as the horizontal one. A Valve bug, kept: it is what TF2 draws, and `BloomRenderTests` pins it with a sabotage that
  turned it red (9 vs 0 at 32 px on a 2:1 frame; 7 vs 7 when "fixed").
- **Interpolated: the add.** `engine_post`'s source is not published; this adds in gamma, as the 2007 `bloomadd` did.

**Before → after, all five stored cases** (per-region mean error /255; world mean RGB, TF2 in brackets):

| case | summary | world | sky | world mean ours | linear world TF2/ours |
|---|---|---|---|---|---|
| 45000 (viewer 44900) | 38.2 → 41.8 | 38.3 → 42.6 | — | 111 106 96 → 123 116 105 (102 96 85) | 0.90 → 0.80 |
| 60000 | 48.0 → 45.3 | 49.0 → 45.6 | 51.9 → 51.4 | 77 73 77 → 84 80 83 (109 104 104) | 2.06 → 1.92 |
| 75000 | 31.5 → 31.9 | 26.0 → 26.3 | 107.3 → 102.7 | 83 83 76 → 91 90 82 (88 89 82) | 1.15 → 0.99 |
| 91000 | 57.6 → 57.2 | 57.6 → 58.1 | 82.9 → 69.4 | 110 103 104 → 127 119 118 (142 132 123) | 1.29 → 1.04 |
| free 91000 | 52.6 → 46.1 | 50.9 → 44.5 | 52.8 → 24.7 | 105 98 99 → 120 111 111 (142 132 126) | 1.54 → 1.18 |

Free-camera sky mean: 143 156 180 → 171 191 212 against TF2's 188 210 233. The in-eye rows still carry the landing-tick
offset below; 60000 most of all.

**What is left is exposure, and the one number that would settle it is closed.** With the range pinned at
`[0.5, 0.7]`, TF2 at its brightest draws 0.7 of its unscaled light — yet outdoors it is still 1.18× ours after bloom,
and indoors at 45000 now 0.80×. Both fit TF2's unscaled light being roughly 1.5-2× the viewer's, with the auto-exposure
then putting indoor scenes near the floor and outdoor ones nearer the ceiling. That factor would live in what the
shader API loads into `cLightScale.x` (`LINEAR_LIGHT_SCALE`, `common_ps_fxc.h:50`) under `HDR_TYPE_INTEGER`, which is
`shaderapidx9.dll` and unpublished. **The auto-exposure itself is ported as an instrument**, not into the renderer:
`AutoExposure` (the histogram's sixteen `(i/16)^1.5` bins over the centre 90 × 85%, the 2%-at-60% target with its
sticky bin, the 3% average floor, the ten-frame history weighted `|i - 5| / 5`, `:615-713`, `:1130-1182`) and the
`autoexposure` probe, which replays it on a stored frame. At a factor of 1 the viewer's frames settle at 0.646 (45000)
and 0.700 (75000, 60000, free) in the linear read. *Interpolated*: which space `dev/lumcompare` samples, and how the
current scale walks to the goal — both in closed code.

**Wrong turn, kept**: the first reading of the free camera's equal sky and world ratios was "exposure, so port the
exposure". The controller's 0.7 ceiling killed it: a scale capped below 1 cannot brighten. The equal ratios were bloom
— an add that, over a mostly bright frame, looks like a multiplier.

### B514 part 2: what the closed code says, and what it does not fix

*Read in disassembly, 2026-10-09 (Ghidra MCP; projects on `D:`), and from published source; measured against the same
stored captures, viewer re-shot at `59c9f1a0` (before) and `fix/b514-exposure` (after).*

**The wrong inference of part 1, killed.** Part 1 concluded TF2's unscaled light "must be 1.5-2× ours" and hoped the
closed shader API hid the factor in `cLightScale.x`. It does not:

| what | where | value under `HDR_TYPE_INTEGER` |
|---|---|---|
| `cLightScale.x`, `LINEAR_LIGHT_SCALE` | `shaderapidx9.dll 0x180023be0` (`SetToneMappingScaleLinear`) | the tone-map scale, unchanged (1 under none) |
| `cLightScale.y`, `LIGHT_MAP_SCALE` | `0x18001be90` | 16 (1 under float) |
| `cLightScale.z`, `ENV_MAP_SCALE` | `0x180023be0` | 16 |
| integer-HDR lightmap texel | `materialsystem.dll 0x180036450`, from page writer `0x180028550` | `min(light · 4096, 65535)` in 16 bits |
| the walk, once a frame | `materialsystem.dll 0x180035fa0` | rate `mat_hdr_manual_tonemap_rate`·2; down: `min(6, (cur−goal)·4·⅔ + 2)`; ×frame time; ≤ 1/64; `cur = lerp(cur, goal, r)`; nothing when frame time ≤ 0 |

**What it did find: the lightmap atlas clipped sunlight.** An integer-HDR lightmap holds light to 16 (65535/4096); the
viewer's held `light / 2` in a byte, so it clipped at 2. `lightmap-range` on cp_process_f12: 8.91% of the HDR lump's
luxels have a channel above 2, 0.01% above 4 — the sunlit outdoors, no interiors. The atlas is now `R11G11B10_FLOAT`
(the same four bytes a luxel; six-bit mantissa), and the shader reads the light without the doubling.

**The histogram's colour space, settled from published source**: `dev/lumcompare` is `screenspace_general` (shipped
VMT), which reads sampler 0 through sRGB unless `$linearread_basetexture` or a 16-bit target
(`screenspace_general.cpp:124-132`); neither applies, so the histogram is of LINEAR light.

**Resets** (`ResetToneMapping(1.0)`): `LevelInitPreEntity`, `C_TFPlayer::ClientPlayerRespawn` (`c_tf_player.cpp:7946`)
and the local player's `SetObserverTarget` (`c_baseplayer.cpp:611-614`). The viewer resets on map load and on the
followed player changing; not on the recorder's respawn.

**Why the golden numbers barely move, and why that is right**: the capture recipe runs `spec_player` — an observer
target change, so a reset to 1 — under `host_timescale 0.001`, and the walk is multiplied by the frame time the engine
hands the material system. So every stored TF2 capture is at a scale of (nearly) 1, and the viewer's `--shot`, a paused
still after the same reset, is at exactly 1 (logged: `tone-map scale 1, bloom 0.5001852` for all five). *Interpolated*:
that the frame time passed to the material system is the timescaled one (not read in `engine.dll`).

| case | summary | world | sky | world mean ours (TF2) | world linear TF2/ours |
|---|---|---|---|---|---|
| 45000 | 41.8 → 41.9 | 42.6 → 42.7 | — | 123 116 105 → 124 117 106 (102 96 85) | 0.80 → 0.80 |
| 60000 | 45.3 → 45.2 | 45.6 → 45.4 | 51.4 → 51.3 | 84 80 83 → 84 80 83 (109 104 104) | 1.92 → 1.92 |
| 75000 | 31.9 → 31.7 | 26.3 → 26.0 | 102.7 → 102.6 | 91 90 82 → 91 90 83 (88 89 82) | 0.99 → 0.98 |
| 91000 | 57.2 → 57.9 | 58.1 → 58.8 | 69.4 → 69.6 | 127 119 118 → 131 122 119 (142 132 123) | 1.04 → 1.25 |
| free 91000 | 46.2 → 46.9 | 44.7 → 45.5 | 24.7 → 24.7 | 120 112 111 → 123 114 112 (142 132 126) | 1.17 → 1.31 |

Sunlit surfaces brighten (world mean +3 to +4 outdoors; our clipped share at the free camera 9.0% → 16.4%) and the
error barely moves, because the brightened pixels were already near white in ours. **The free-camera "no timing in it"
claim above is too strong**: its diff is dominated by gummo, who stands in front of our camera and not TF2's — the
landing-tick fault below reaches the free camera too, through what is IN the frame. **Still open**: outdoor world short
by ~19 levels, indoor 45000 over by ~22. The likeliest remaining term is bloom's add space, the one interpolation left
in it.

### The instrument's open fault: TF2 lands earlier than the tick asked for

*Measured, interpolation flagged.* At the free camera, the camera IS gummo's eye as TF2 reported it (`spec_pos` the
moment in-eye was switched to free roam), yet in our capture at the same nominal tick gummo stands about a body length
ahead of that camera. At 45000 the in-eye error falls from 51.3 (45000) to 41.9 (44900) and rises again at 44850 and
44800 — a minimum, but against a world error still dominated by B514, so it bounds the offset loosely (on the order
of 100 ticks) rather than measuring it. **So the in-eye numbers above include a camera misalignment of unknown size,
and only the free-camera case's static world and sky are clean of it.** The fix is a tick readout from TF2 at the
moment of capture — none of `demo_info`, `demo_debug 1`, `cl_showdemooverlay 1` printed one — and is the next step for
B161, not a reason to distrust the controls, which do not depend on it.

**WRONG, and what killed it (2026-10-09, `fix/b161-instrument`).** `demoui` is the readout: its panel prints
`Tick: 45002 / 106313` after `demo_gototick 45000 0 0`, 91004 after 91000, 100005 after 100000 — TF2 lands within a
few ticks, never a hundred. The "offset" was two other things read as one: **`spec_mode 6` is not free roam in TF2**
(6 is `OBS_MODE_POI`, roaming is 7 — `shareddefs.h:499-500`), so that "free camera" was a chase view that kept following gummo; and **our
world view is a third narrower than TF2's** (B518), which is what the 44900 "minimum" was fitting. See "The instrument,
second pass" below.

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

## The instrument, second pass: a tick readout, exact cameras, and each term isolated (2026-10-09)

*Measured on live TF2 build 11087207 against the viewer at main b0c8e408 plus this branch's three switches; read from
published source where cited. Every number is `golden-compare`'s own; a ratio written `a/b` is two of its printed means
divided.*

### What changed in the recipe

| need | how |
|---|---|
| TF2's actual tick | `demoui` — the panel prints `Tick: n / total`, the same numbering the viewer's `--tick` takes. Leave it open in the capture and pass `--exclude 197,378,499,603` so the capture carries its own tick (owner's suggestion). **Follow TF2: shoot the viewer at the tick TF2 landed on**, never force TF2 onto one |
| an exact, empty camera | not the demo: `map cp_process_f12`, `jointeam spectator`, `spec_mode 7`, `spec_goto x y z pitch yaw` — a listen server has no players and `spec_goto` is a SERVER command (`player.cpp:6692`), which is why it did nothing during demo playback. `spec_pos` reads the pose back |
| TF2's exposure | `mat_hdr_tonemapscale` is written by `SetToneMapScale` every frame (`viewpostprocess.cpp:1150,1180`), so reading the cvar reads the goal scale |
| exposure pinned | `mat_force_tonemap_scale 1` — `SetToneMapScale` resets to exactly it (`:1143-1148`) |
| bloom off | `mat_disable_bloom 1` |
| the same switches in the viewer | `+mat_force_tonemap_scale`, `+mat_disable_bloom`, `+cl_drawhud 0` — Valve's names, read from the config (D210), added here |
| a surface, not a frame | `--rect x0,y0,x1,y1`; an image compared with itself under it prints that surface's mean colour and mean linear luminance |

**Per camera, four TF2 captures and five of ours**: TF2 auto+bloom, auto, forced 1, forced 1+bloom; ours forced 1, forced
1+bloom, forced to TF2's measured scale with and without bloom, and our own auto. **Control on the method:** the linear
ratio auto/forced-1 over a lightmapped surface reproduces the cvar — 307.0/438.6 = 0.700 against a read 0.700 outdoors,
246.0/448.4 = 0.549 against a read 0.5496 indoors.

**Things learned about the client on the way**: `host_timescale` slows RCON itself (at 0.0001 a console call timed out
at 30 s; 0.002 costs ~15 s a call, ~2 ticks of drift across four calls — which is the argument for the tick being IN the
capture); `spec_mode 7` during an STV demo keeps the position where in-eye left it but takes its ANGLES from the
recording's own view angles, so it cannot be aimed; `+lookup` does not turn it.

**Every TF2 cvar touched was read first and read back after**: `mat_hdr_level` 0, `mat_picmip` -1, `cl_detaildist` 8592,
`con_drawnotify` 1, `hud_saytext_time` 10, `sv_cheats` 0, `cl_drawhud` 1, `host_timescale` 1, `mat_disable_bloom` 0,
`mat_force_tonemap_scale` 0, `mat_hdr_tonemapscale` 1.0 — all at those values in the final readback.

### The empty-world table (cp_process_f12, no player or particle in any patch)

Cameras: **outdoor** `-526 -422 575 0 15`, **sky** `-526 -422 575 -70 300`, **indoor** `146 -1831.6 728 -28.7 -36.5`. Ours
at tick 45091. Linear luminance ×1000, mean over the patch. Because of B518 the same surface is not at the same pixels,
so each patch is one rectangle in TF2's frame and that rectangle scaled 4/3 about the centre in ours.

| patch | TF2 exposure (read / measured) | ours in a `--shot` | residual, both off: TF2 / ours | bloom added at TF2's exposure: TF2 / ours |
|---|---|---|---|---|
| outdoor, sunlit brick wall | 0.700 / 0.700 | 1.0 | 438.6 / 533.1 (**×1.22**) | +117.2 / +180.9 |
| outdoor, shaded container face | 0.700 / 0.700 | 1.0 | 41.4 / 43.7 (×1.06) | +2.0 / +2.3 |
| outdoor, sunlit ground | 0.700 / 0.704 | 1.0 | 621.1 / 546.6 (×0.88) | +195.0 / +128.9 (TF2 21% clipped) |
| outdoor, sky near the horizon | 0.700 / 0.701 | 1.0 | 449.7 / 446.1 (×0.99) | +137.4 / clipped (79%) |
| sky camera, sky | 0.700 / **0.760** | 1.0 | 483.4 / 483.3 (**×1.000**) | +164.8 / saturated (201 230 255) |
| indoor, wall | 0.5496 / 0.549 | 1.0 | 448.4 / 456.7 (×1.02) | +75.3 / +104.7 |
| indoor, control-panel face | — / 0.549 | 1.0 | 319.3 / 383.5 (**×1.20**) | +35.2 / +74.6 |
| indoor, ceiling by the lamps | — / 0.542 | 1.0 | 79.5 / 53.8 (**×0.68**) | +1.7 / +0.6 |

**What our surfaces do when forced to TF2's scale** (ours forced-s / ours forced-1): outdoor wall 373.3/533.1 = 0.700 and
container 30.6/43.7 = 0.700 — exact. **Sky 483.3/483.3 = 1.000 and 438.2/446.1 = 0.98: our sky ignores the scale**
(B519). Indoors at a forced 0.55: wall 0.610, panel 0.644, ceiling 0.617 where TF2's are 0.549 — something in those
patches is not scaled either, and the lamp cones (B521) are in every one of them.

**So B514's remainder is four separate things, in order of size:**

1. **Exposure state, not exposure maths.** TF2 sits at 0.70 outdoors and 0.55 indoors at these cameras; a viewer `--shot`
   is at 1.0, because the walk does nothing at a frame time of zero and a shot is one paused frame (B520). Whether live
   playback reaches TF2's values is the next measurement, with `--measure`-style playback and the scale logged.
2. **Bloom adds 1.4-2.1× TF2's linear amount** on bright surfaces at the same exposure (+180.9 vs +117.2, +104.7 vs
   +75.3, +74.6 vs +35.2) and less on dark ones — partly because what goes in is brighter (the wall's ×1.22, the unscaled
   sky), partly not: the container goes in equal and comes out +2.3 against +2.0.
3. **The sky does not take the tone-map scale** (B519) — at TF2's 0.70 our sky is 1/0.70-1/0.76 too bright before
   bloom, and then saturates it.
4. **Pure lighting, both off**: flat walls agree to 2% indoors and 6% in shade, the sky texture to 0.0%. What does not:
   the sunlit brick wall +22%, the sunlit ground −12%, the control panel +20%, the ceiling by the lamps −32%.

### First person, indicative only

TF2 landed on 91004 and 100005; the viewer was shot at those ticks, `demoui` excluded, HUD off both sides. Per-pixel
error is 57.3 and 40.5 /255 and **means nothing while B518 stands** — the two frames are different projections. Mean
world colour: 146 140 131 → 137 129 126 (91004, mid, outdoors) and 147 131 118 → 146 133 121 (100005, last point). With
both forced to 1 and bloom off (TF2 at ~100009, ours at 100009): 133 120 109 → 130 119 109, a linear ratio of 1.02.
Sub-tick interpolation, particles and tracers are in all of these.

### Found on the way

- **B518 — closed 2026-10-10**; at the outdoor camera the frame error fell 42.7 → 21.5 /255 and the patches now sit at
  the same pixels (`docs/RISKS.md` B518). As found: TF2 widens the world field of view by the aspect ratio (`view.cpp:1075-1083`); we do not. At 16:9 TF2's 90
  is 106.26° across and ours is 90°. Measured at the fixed camera: the same wall is 4/3 as far from the centre in ours.
- **B519 and B520 — closed 2026-10-10** (`docs/RISKS.md`). The sky shader was right and the main view never handed it
  the scale; a still now lets the exposure walk before it is taken. Unforced shots settle at 0.701 outdoors and at the
  sky camera (TF2 0.700) and 0.501 indoors (TF2 0.550, ours pinned to the floor by a brighter indoor frame).
- **B521**: the cones under the indoor lamps are large near-opaque white beams in ours and a faint glow in TF2.
