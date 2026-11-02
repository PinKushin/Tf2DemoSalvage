# Measurement 2026-11-02: what the light cache's visibility trace costs (B666)

Branch `worktree-agent-ae30875a0076f24a3`, fix at `c852758d`. One question: the port now traces every admitted world light
from each static prop's lighting origin and each light cache entry's point (`LocalLights.Visible`). Does that cost a
frame, or a load?

## Environment

- Debug build of `tf2demoview` (`managed/Tf2DemoSalvage.Viewer3D/bin/Debug/net10.0-windows`), the build the owner runs.
- Demo `D:/measure-f12/b659/b659_toxic.dem` (koth_toxic, sha256 `277a94b9…cb5e5d`), `--tick 600 --first-person
  --measure 20 +fps_max 0`, `TF2VIEW_AUTOPLAY=1`, under `run-exclusive.ps1`.
- A/B on one base: B is `c852758d`; A is the same tree with `LevelLighting.From` passing `null` for `lightTrace` (an
  inverse edit, rebuilt, measured, restored and rebuilt). Nothing else differs.

## Raw

Load line (`first uses in play … before play`):

| | static-prop lighting at load | static states 2225 | baked built 1692 |
|---|---|---|---|
| A, no trace | 3,445.9 ms | 3,326.0 ms | 101.3 ms |
| B, traced | 9,369.1 ms | 9,236.8 ms | 141.0 ms |

A first B run (before the A run) read 9,279.3 ms / 9,153.0 ms, so B reproduces.

Per-interval frame rates (fps / mean ms / frames), in order:

- A: 440/2.0/323 471/2.3/429 774/1.2/639 861/1.4/836 1134/0.7/860 1007/1.6/965 922/2.1/940 1062/1.3/961 1324/0.8/994
  1471/0.6/1329 1503/0.6/1353 1491/0.7/1378 1414/0.6/1389 1386/0.8/1279 1219/1.2/1222 1363/1.0/1275 1463/0.7/1343
  1489/0.6/1327
- B: 400/2.3/330 493/2.2/465 940/1.1/736 923/1.9/870 880/1.8/876 990/1.2/884 893/1.4/909 1161/1.3/960 1423/0.6/1067
  1434/0.6/1274 1301/1.2/1381 1263/0.8/1293 1490/0.7/1290 1445/0.7/1398 1137/1.0/1278 1264/1.0/1258 1270/1.0/1274
  1435/0.9/1315

`moment cost … lighting 0` in both: no entry is rebuilt in steady play here.

## f12, where entries are relit in play

Same A/B, `F:/…/tf/f12.dem --tick 5000 --first-person --measure 20 +fps_max 0` (cp_process_f12).

| | static-prop lighting at load | static states 1353 |
|---|---|---|
| A, no trace | 1,130.8 ms | 1,052.0 ms |
| B, traced | 1,608.8 ms | 1,519.7 ms |

Pose `lighting` column per interval (ms), the control that entries are being lit in this window:
- A: 0.6 1.6 1.2 0.6 0.8 0.1 0 0.1 0.1 0.2 0 0.1 0.4 0.5 0.4 0.3, mean 0.44
- B: 0.9 2 1.8 1.4 0.9 0.3 0.1 0.1 0.2 0.5 0 0.1 0.9 1.1 1 0.9, mean 0.76

Frame rates (fps/ms): A 46/20.9 55/18.5 59/15.6 73/14.6 84/12.6 95/10.0 129/7.4 178/5.0 187/5.3 228/4.7 157/6.3
250/3.4 209/5.3 127/10.6 112/8.5 124/7.5 135/8.3 133/7.2; B 50/17.5 65/21.7 58/15.6 64/14.7 58/15.8 84/11.2 106/10.7
143/6.3 172/6.7 190/5.1 156/6.0 208/5.0 270/3.4 110/9.2 112/9.9 99/9.6 98/10.4 116/8.9. They are too noisy to separate.

## Verdict

- **Load: +5.9 s** (static states 3.3 s → 9.2 s, measured, Debug). That is 2,225 static states each tracing every
  style-0 light that clears `r_worldlightmin` at its origin: on this map nearly every lamp, since a 70,000-intensity
  quadratic lamp clears it out to about 18,700 units (*arithmetic*: √(70000 / 0.0002)).
- **Frame: about +0.3 ms on f12**, in the pose's `lighting` column (0.44 → 0.76 ms mean). On koth_toxic no entry was
  lit in the window (`lighting 0` both ways), so that run cannot see it. The f12 frame rates are too noisy to show it.
- **The fix the engine's own order allows** (*interpolated, not built*): `Strongest` folds nothing, so ranking first and
  tracing candidates strongest-down until four are seen gives the same four lights with far fewer traces; `Split` must
  still trace every admitted light, since the visible losers are folded. A light cache entry relit on a style change can
  keep its point's visibility, because the world does not move. Filed under B666 as a follow-up.
