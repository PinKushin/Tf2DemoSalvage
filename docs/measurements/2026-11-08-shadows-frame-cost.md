# 2026-11-08 — render-to-texture shadows: frame cost (B702)

**Question:** what the shadows (finding 107) cost a frame, on and off, at f12's fight.

**Command** (each run, `+r_shadowrendertotexture 1` then `0`; the window off screen so per-pass GPU is comparable, the
`tf2-measure` skill's rule):

```
TF2VIEW_WINDOW_POS=-3000,100 TF2VIEW_AUTOPLAY=1 pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 \
  managed/Tf2DemoSalvage.Viewer3D/bin/Debug/net10.0-windows/tf2demoview.exe D:/measure-f12/f12.dem \
  --tick 5970 --first-person --measure 20 +fps_max 0 +r_shadowrendertotexture <v>
```

**Environment:** branch `feat/b709-control-points` at `0d3c3f4b` plus the uncommitted renderer and presenter (the commit
after it), Debug build, 984×551, `mat_queue_mode` default (2). Both runs classify as "nothing visible" by Clear GPU (1.045,
0.985 ms).

**Not drawn, D129:** shadows on brush entities and static props, the blob fallback, shadows in reflection views on surfaces
the main view does not draw (finding 107's open list).

## Raw output

```
== r_shadowrendertotexture 1
  phases, mean ms: sound 0.12, camera 4.94, project 0.00, advance 0.59, capture 0.00, hud 0.31, draw 0.01, between 0.18; render thread draw 1.96 (mode 2 only, frame-weighted like the phases); particles now: 0 trails, 19 bursts, 563 quads last frame
  render passes, mean per frame at 984x551: 3249 frames drawn, 3248 read on the GPU, 0 skipped, 0 disjoint, 0 abandoned
    Clear: cpu 0.004 ms, gpu 1.045 ms, draws 0.0, maps 0.0, primitives 0, pixels 0
    Shadows: cpu 0.238 ms, gpu 0.283 ms, draws 41.6, maps 81.6, primitives 57796, pixels 83970
    World: cpu 0.256 ms, gpu 0.169 ms, draws 492.9, maps 493.9, primitives 10553, pixels 1090562
    Present: cpu 0.069 ms, gpu 3.804 ms, draws 0.0, maps 0.0, primitives 0, pixels 0
== r_shadowrendertotexture 0
  phases, mean ms: sound 0.11, camera 4.27, project 0.00, advance 0.55, capture 0.00, hud 0.30, draw 0.01, between 0.17; render thread draw 1.68 (mode 2 only, frame-weighted like the phases); particles now: 0 trails, 19 bursts, 562 quads last frame
  render passes, mean per frame at 984x551: 3693 frames drawn, 3692 read on the GPU, 0 skipped, 0 disjoint, 0 abandoned
    Clear: cpu 0.004 ms, gpu 0.985 ms, draws 0.0, maps 0.0, primitives 0, pixels 0
    Shadows: cpu 0.003 ms, gpu 0.007 ms, draws 0.0, maps 1.0, primitives 0, pixels 0
    World: cpu 0.250 ms, gpu 0.165 ms, draws 500.0, maps 501.0, primitives 10626, pixels 1079789
    Present: cpu 0.067 ms, gpu 3.512 ms, draws 0.0, maps 0.0, primitives 0, pixels 0
```

The game thread's own split of the shadow step, per-second means from an earlier on-screen run of the same code
(`camera particles/shadows/…` parts):

```
gather 0.21  casters 0.18  receivers 0.41  decals 0.38  shadows 1.19
gather 0.12  casters 0.08  receivers 0.35  decals 0.27  shadows 0.85
gather 0.08  casters 0.06  receivers 0.34  decals 0.23  shadows 0.73
```

## Verdict

**0.74 ms a frame**: 3249 frames in 20 s on (162 fps, 6.16 ms) against 3693 off (185 fps, 5.42 ms). Of it the game
thread's `camera` phase +0.67 ms (the shadow step: `ProjectShadow`'s leaf walk and the decal cut), the render thread
+0.28 ms (the atlas pass's 42 model draws, 0.24 ms CPU), the atlas GPU 0.28 ms; the world's shadow decals add 0.004 ms
GPU. The largest share is the link walk, run for every shadow every frame where the engine re-projects only a moved one —
the first cost to take (finding 107, open list).

**Earlier, before the clip's early-out and the drawn-surface build** (same tick, on screen): the step was 0.6–2.6 ms and
the frame 148 fps on against 170 off.
