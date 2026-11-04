# B669: the light traces of placement #215's static state, logged from the running engine

## Observation

`measurements/2026-11-03-b669-pipe-lights.md`, by this project against live TF2 11191711: #215's direct lights read -y 0.201 and +z 0.391 in TF2 against 0.136 and 0.328, +y and -z zero in both. Reading the trace found no difference and no subset of the lights fits.

## Question

For the state TF2 builds at (-3459 -683 506), which lights reach `FUN_1801b9570` or `FUN_1801b8e20`, from what point, and which does each return accept? Where does that list differ from ours?

## Method

Local listen server, `-insecure -vulkan`, `cp_process_f12`, `mat_hdr_level 2`, under `run-exclusive.ps1` with a lock that times itself out. Log-only breakpoints, rebased from the module's load address, read registers and memory only and never write the game. Arguments at entry; the float return in XMM0 at the return. Filtered on the start point. Forced by a fresh map load with the breakpoints armed. The final slots and cube if reachable. In ours, `prop-shade` at the same point.

## Control

A light ours and the disassembly both keep must log accept, and one behind the floor (spot (-3480 -728 343)) must log reject. A filter with no hit for #215 says nothing until another placement's start point hits. Logged lights must number no more than the map's world lights.

## Environment

Machine and build as in the records above. Ours: branch head at the run.

## Result

Live TF2 11191711, `-insecure -vulkan`, listen server, `mat_hdr_level 2`. Cheat Engine 7 by the Windows debugger interface, debug-register execute breakpoints, reading only. `engine.dll` loaded at `0x7FFAEDE30000`.

**The Ghidra copy is older than the running binary.** `0x1801b9570` rebased read `E5 37 00`. The function's prologue found it at RVA `0x1BAEB0`, and `FUN_1801b8270`, `FUN_1801b8e20`, `FUN_1801b5db0` and `FUN_1801b5a50` at the same +0x1940, by signature. Addresses here are the Ghidra project's.

**Controls.** The spot behind the floor (-3480 -728 343) logged fraction 0.15930 and return 0. A clear one (-3425 -772 1161) logged 1.0. `FUN_1801b8e20` was never entered. 84 of the map's 477 lights were traced for this state. The first run's final cube, `r_radiosity 0`, `r_worldlights 0`:

| face | engine, G | shots | ours before |
|---|---|---|---|
| +x | 0.06327 | | |
| -x | 0.15000 | | |
| +y | 0 | 0 | 0 |
| -y | 0.20194 | 0.201 | 0.136 |
| +z | 0.39385 | 0.391 | 0.328 |
| -z | 0.00089 | about 0.001 | 0 |

So the shots were right and the state is what they read.

**The traces agree with ours.** Every light the engine accepted is one ours keeps. The difference is in the values, logged at `FUN_1801b5db0`, XMM3:

| light | engine's amount | return | ours' amount |
|---|---|---|---|
| spot (-3425 -772 1161), aim -z | 2.284283e-06 | 1/d² | that × 0.9896, its `dot2` |
| spot (-3423 -1280 1353) | 9.181228e-07 | 1/d² | that × 0.8168 |
| spot (-3400 -1128 486), aim +z | 1.142210e-04 | 0.002225, 1/d | that × 0.0445, 22 times too dim |
| spot (-3000 -1112 486) | 5.836706e-05 | 0.001591 | 31 times too dim |

**Three causes, each then read:**

1. The fold takes a spot's fringe and no cosine of its own (`FUN_1800efbf0` case 2). Ours multiplied by `dot2`, B122's reading of vrad's bake. **This is the gap.**
2. The rank is luminance times the return, with no cone (`FUN_1801b60a0`, `MULSS XMM6,XMM7`). Ours ranked with both.
3. Before the trace, `FUN_1801b99a0` tests a sphere around the point against the cone, and a radius computed at load (`FUN_1800cbdd0`). Ours tested the cone at the point and had no radius. Point (-4048 -1132 680), kept by ours at 0.0034, is not traced by the engine: its radius is 475.8 and it is 761 away.

**Slots, default cvars, third and fourth runs:** (-3000 -1112 486), (-3425 -772 1161), (-3425 -816 1161), (-3400 -1128 486). The 22 folded lights are the other 22 of those 26, equal to the first run's amounts to seven digits.

| face, G | engine | ours before | ours now |
|---|---|---|---|
| +x | 0.13899 | | 0.13899 |
| -x | 0.23487 | | 0.23552 |
| +y | 0.06567 | 0.06567 | 0.06567 |
| -y | 0.27004 | 0.204 | 0.27335 |
| +z | 0.40297 | 0.272 | 0.40617 |
| -z | 0.03904 | 0.03904 | 0.03904 |

**Admission, diffed with no game.** `prop-shade` at the head, its `reaches True, in pvs True` set for #215 against the engine's traced list, the sky light left out: 83 and 83, none on one side only. So the sphere, the cell and the computed radius hold at this point, whatever `DAT_18075b318` is called. Its kept subset sums to -y 0.2017, +z 0.3939, -z 0.0009, from four-decimal shares, against the engine's 0.20194, 0.39385, 0.00089. So what stands over on -x, -y and +z is in the bounce: B683.

**The left pipe, a second placement, a fourth run** (`2pipe_straight_64` #226, the far camera's left pipe by its ray and `map-near`; *interpolated:* `TF2VIEW_PICK`'s line was not recovered from the shot's log). Engine's point (-3833.5 -1216 609.0), ours (-3834 -1216 609.5): B682. **Slots equal:** (-3831 -1280 1062), (-3798 -905 1161), (-2848 -2080 582), (-3767 -936 1161). G:

| | +x | -x | +y | -y | +z | -z |
|---|---|---|---|---|---|---|
| engine | 0.39234 | 0.12832 | 0.20646 | 0.24364 | 0.39281 | 0.15434 |
| ours | 0.39065 | 0.11029 | 0.20073 | 0.23326 | 0.38496 | 0.15333 |

So ours' state is at or under the engine's on every face, and the shot reads over. That was the half pixel, not the state.

**Far camera, `-3722 -1403 726 25 99`, tick 45091, scale 1, bloom off, linear luminance x1000.** Ours before is the shot at the parent (`final.png`):

| | TF2 D3D9 | TF2 Vulkan | ours before | ours now |
|---|---|---|---|---|
| patch | 228.1 | 241.7 | 229.1 | **241.1** |
| upper pipe top | 357.1 | 358.1 | 326.5 | **351.4** |
| upper pipe mid | 303.8 | 303.8 | 268.7 | **297.0** |
| lower pipe top | 358.5 | 358.4 | 322.3 | 352.1 |
| lower pipe mid | 289.2 | 289.0 | 260.1 | 284.9 |
| wall behind | 187.2 | 207.7 | 199.0 | 199.0 |
| gap below | 125.1 | 139.5 | 139.3 | 139.3 |
| concrete, the control | 187.0 | 187.0 | 187.0 | 187.0 |
| cap disc | 190.8 | 194.2 | 177.3 | 192.0 |
| left pipe upper | 54.3 | 67.5 | 63.0 | 74.4 |
| left pipe lower | 83.0 | 93.2 | 82.0 | 101.6 |
| whole frame | 155.5 | 160.0 | 154.0 | 155.8 |

The D3D9 column keeps its driver halo (`measurements/2026-11-03-b669-behind-the-pane.md`), so Vulkan is the column to read. **The left pipe's "over" is a half pixel on a narrow box, not light:** `measurements/2026-11-03-b685-left-pipe-terms.md`.

**B665's vat**, `-208 1360 80 60 90`, linear RGB x1000, with the old arithmetic put back by temporary edits for the before (reversed):

| box | TF2 | before | now |
|---|---|---|---|
| outer wall, x 20-60 y 400-600 | 5.6 60.4 45.7 | 9.5 71.6 55.9 | 6.1 68.3 53.0 |
| upper wall, x 1000-1100 y 90-130 | 4.8 53.9 27.5 | 6.8 51.7 28.7 | 5.4 50.3 27.5 |
| upper left, x 60-110 y 120-170 | 9.8 42.4 19.4 | 7.3 38.8 17.5 | 7.3 38.8 17.5 |

The vat's slots are ours: **a white spot in a slot there is what the rule gives, not a log of that map**, whose debugging the approval did not cover.

## Verdict

**The pipes behind the pane are explained and fixed.** The cause was the angle of a folded spotlight, with the rank and the admission beside it. At #215 the logged state and ours agree light for light and slot for slot, and admission to the digit. Left: B683, in the bounce. B685, a second placement that read over, is closed by the next record.
