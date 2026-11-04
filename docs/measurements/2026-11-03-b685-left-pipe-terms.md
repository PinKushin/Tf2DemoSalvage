# B685: which term of the draw puts cp_process_f12's far-left pipe over TF2

## Observation

`measurements/2026-11-03-b669-pipe-traces-live.md`: far camera, Vulkan, left pipe upper 67.5 and lower 93.2 in TF2, 74.4 and 101.6 in ours, with the engine's four slots and ours' cube at or under the engine's on every face.

## Question

Which term of `2pipe_straight_64`'s draw holds the excess, and what differs from `industrial_pipe01`, which now agrees?

## Method

Both sides at the same camera and tick, box by box. In TF2, `-vulkan`, `mat_hdr_level 2`, scale 1, bloom off: `mat_phong 0`, `mat_specular 0`, `mat_fullbright 2`, `r_worldlights 0`. In ours, the same switches where they reach a `--shot`, else temporary shader edits, reversed. Each model's VMT read beside it. If that stalls, the constants the engine sets, read with Cheat Engine. Which pixels are which prop is projected from the placement first.

## Control

The concrete box stays 187.0 throughout. `industrial_pipe01`'s box agrees under each switch, or the switch is what differs. A switch that does not move TF2's box did not reach it.

## Environment

Machine and build as in the records above. Ours: branch head at the run.

## Result

Live TF2 11191711, `-vulkan`, `mat_hdr_level 2`, scale 1, bloom off, linear x1000. The far camera `-3722 -1403 726 25 99`, and a close one on the pipes, `-3770 -1300 650 30 180`.

**The filed excess is registration, not light.** The far boxes are 7 pixels wide on a thin diagonal pipe. A least-squares bilinear fit of ours onto TF2, on edges of world and models alike, finds TF2's frame is ours sampled at about (-0.5, -0.5): (-0.4, -1.0), (-0.4, -1.0), (-0.6, -0.6) close, (-0.6, -0.4), (-0.4, -0.4) far. *Interpolated:* D3D9's rule that a pixel's centre is its integer coordinate, which Source does not correct for 3D and DXVK keeps. That half pixel is B686. Boxes re-read with ours so shifted:

| box | TF2 Vulkan | ours as drawn | ours shifted | before B669, shifted |
|---|---|---|---|---|
| left pipe upper | 67.5 | 73.5 | **66.1** | 56.5 |
| left pipe lower | 93.2 | 100.7 | **93.9** | 76.3 |
| upper pipe top | 358.1 | 351.4 | 354.8 | |
| upper pipe mid | 303.8 | 297.0 | 300.7 | |
| lower pipe top | 358.4 | 352.1 | 355.8 | |
| lower pipe mid | 289.0 | 284.9 | 289.2 | |
| patch | 241.7 | 241.1 | 240.8 | |
| wall behind | 207.7 | 199.0 | 198.3 | |
| gap below | 139.5 | 139.3 | 140.0 | |
| concrete, control | 187.0 | 187.0 | 186.9 | |
| cap disc | 194.2 | 192.0 | 191.6 | |

So the fix took the left pipe from 16-18% under TF2 to within 2%, and the far table's "over" was a box on an edge. **Across the pipes at the far camera**, summing a profile perpendicular to them over 16 pixels and 10 pixels: 1152 and 752 in TF2, 1157 and 747 in ours.

**The materials differ in everything that peaks at an edge.** `2pipe`: `$phongboost 5`, `$phongexponent 8`, `$phongfresnelranges [.6 2 5]`, `$rimlight 1`, `$rimlightboost 2.5`, `$rimlightexponent 15`, `$envmap env_cubemap`, `$envmaptint [.2 .2 .2]`. `industrial_pipe01`: boost .125, exponent 15, ranges [0 15 10], no rim, no envmap. So the pipes behind the pane could not show this: nothing of theirs is steep at a silhouette, and their boxes are 30 pixels wide.

**Terms at the close camera, row sums over x 300-500, both pipes:**

| | TF2 | ours |
|---|---|---|
| all | 4257.9 | 4302.7 |
| edge rows, 6 of each pipe | 1651.2 | 1819.4 |
| body rows | 2606.7 | 2483.2 |

Ours is +1.0% in all, the edges +10% and the bodies -5%. The bodies' shortfall is the cube: `prop-shade` at the engine's point reads -x 0.1113, -y 0.2347, +z 0.3862 against the logged 0.1283, 0.2436, 0.3928, with no light on -x at all, so it is B683's bounce. Each edge moves with the half pixel: ours' upper 1110 and lower 710 against TF2's 765 and 886, because a steep term is sampled at a different phase on each.

**Two switches that do not do what they say:**

- `r_rimlight 0` in TF2 does not remove the rim here, and `mat_reloadallmaterials` after it changes no digit. *Arithmetic:* the shot equals the rim at boost 1, not 0. `vRimBoost[3]` defaults to 1 when `bHasRimLight` is false (`skin_dx9_helper.cpp:810`, `:848`) while the static combo stays on. Row by row, what remains is the removed part over 1.5: 125 against 188/1.5, 45 against 66/1.5, 21 against 32/1.5. So TF2's rim, by that arithmetic, is 1113 in these sums against ours' measured 1229: over by 10%, on the same steep rows.
- `mat_specular 0` leaves the pipes to the digit. It is `InitParamsVertexLitGeneric`'s rule, `vertexlitgeneric_dx9_helper.cpp:215`, and a `$phong` material takes the skin path, whose init has none. So it says nothing about their envmap. Ours' envmap is 267 of 4303. TF2's was not isolated. *Read:* `$basemapalphaenvmapmask` in this VMT is no parameter either; the flag is `$basealphaenvmapmask`.

**B682 fixed.** Ours' point is now the logged (-3833.5 -1216 609.0). The cube moved by up to 0.0042: it barely mattered here, and it was wrong.

## Verdict

**B685 is not a defect in the light.** The filed excess is a half-pixel difference in where every edge of the frame falls, read through a box narrower than the thing it measured. With ours shifted half a pixel, the left pipe is within 2% of TF2 and the fix moved it toward TF2. Left: B686, the half pixel itself, and B683, now showing as the 5% in these pipes' bodies. The rim's +10% was not separated from the half pixel, so it stays a question until B686 is fixed.
