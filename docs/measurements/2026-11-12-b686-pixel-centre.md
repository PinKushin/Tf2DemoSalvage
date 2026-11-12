# B686: where every edge of ours falls against TF2, before and after the D3D9 pixel centre

## Observation

`measurements/2026-11-03-b685-left-pipe-terms.md`: a least-squares bilinear fit of ours onto TF2 on `-vulkan`, cp_process_f12,
close and far cameras, found TF2's frame is ours sampled at about (-0.5, -0.5). B686.

## Question

After the 3D views take D3D9's pixel centre (`PixelCentre`, B686), at what sub-pixel offset does ours register onto the same TF2
captures, on the same boxes the "before" is re-measured on?

## Method

Ours with `D:/measure-f12/b669/rem/shot.ps1`'s settings (f12.dem tick 45091, 1280x720, `+cl_drawhud 0
+mat_force_tonemap_scale 1 +mat_disable_bloom 1`), from this branch under `run-exclusive.ps1`, at the far camera
`-3722 -1403 726 25 99` and the close one `-3770 -1300 650 30 180`. Fitted with B685's own instrument,
`D:/measure-f12/b669/rem/subpix.py` (linear luminance, bilinear shifts of ours in fifths of a pixel, -1.4 to 1.4, least squares),
against TF2 Vulkan's `tf2/vk-ref.png` (far) and `tf2/lp-base.png` (close).

Boxes, x0-x1 y0-y1. Far: the B logo 865-980 95-210, the hazard stripes 780-880 420-600, the railing 810-1000 345-420, the pipes
behind the pane 800-1000 260-330, the left pipe 300-520 360-560, the stairs 560-760 100-230. Close: the clamps 520-600 340-420 and
930-1020 340-420, the tile seams 300-900 250-300, the elbow 70-200 340-460.

## Control

The same boxes on ours before the change (`ours/lp2-far.png`, `ours/lp2-base.png`, B685's shots) must reproduce B685's
(-0.5, -0.5), or the boxes or the instrument do not see the registration. A box whose edges all run one way cannot fix the shift
along them; such a box is reported, not dropped.

## Environment

The owner's machine, hardware Direct3D 11. TF2 build 11191711, `-vulkan`, captured 2026-11-03 (B685). Ours: branch
`fix/b686-half-pixel` from `84f76505` with the change in the working tree; before: B685's shots.

## Result

`ours sampled at +(dx, dy) matches TF2`:

| box | before (lp2) | after |
|---|---|---|
| far, B logo | (-0.6, -0.4) | (0.0, 0.0) |
| far, stripes | (-1.2, 0.0) | (0.0, 0.0) |
| far, railing | (-0.6, -0.4) | (0.0, 0.0) |
| far, pipes behind the pane | (-0.4, -0.4) | (0.0, 0.0) |
| far, left pipe | (0.0, -1.0) | (0.0, 0.0) |
| far, stairs | (-0.6, -0.4) | (0.0, 0.0) |
| close, clamp | (-0.6, -0.6) | (0.0, 0.0) |
| close, clamp right | (-0.6, -0.6) | (0.0, 0.0) |
| close, tile seams | (-0.6, -1.0) | (-0.2, 0.0) |
| close, elbow | (-0.6, -0.4) | (0.0, 0.0) |

The control holds: eight of ten boxes reproduce B685's half pixel before. The stripes and the left pipe, both one diagonal
direction, gave (-1.2, 0) and (0, -1) before, the aperture problem; after, they too read (0, 0).

## Verdict

Ours now registers on TF2 to within a fifth of a pixel on every box, where it was half a pixel off on every box that can see it:
B686's offset is D3D9's pixel centre, and offsetting the 3D views' viewports by it removes it.
