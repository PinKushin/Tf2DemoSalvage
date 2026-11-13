# B724: the frame cost of following every sound channel (2026-11-13)

**Question.** Following every channel each frame (`ActiveLoops.Follow`: a track lookup and a sample per channel, then
a gain and a pan) instead of loops only: what does the `sound` phase cost?

**Command**, both builds Debug, the machine-wide lock held, same demo and tick:

```
TF2VIEW_AUTOPLAY=1 pwsh run-exclusive.ps1 tf2demoview.exe demostf-cp_process_f12-2026-08-08-2207.dem --tick 30000 --first-person --measure 20 +fps_max 0
```

**Environment.** main `a6bb75801` (loops followed only) against `fix/b724-every-channel` at `2fb5e893c` (every channel
followed; the review fixes after it touch no per-frame path but the dormancy and parent checks, which only add early
outs). The desktop was otherwise idle; no other measurement ran.

**Raw output, the `sound` phase of each `--measure` line:**

| build | frame | sound |
|---|---|---|
| main | 117 fps, 7.6 ms | 0.4 ms |
| main | 112 fps, 8.3 ms | 0.4 ms |
| main | 109 fps, 10.8 ms | 0.5 ms |
| branch | 119 fps, 8.9 ms | 0.5 ms |
| branch | 114 fps, 8.1 ms | 0.5 ms |

**Verdict.** About +0.05 to 0.1 ms of a ~8 ms frame, at the phase's 0.1 ms printed resolution, so the difference is at
the edge of what this line can show. It is the engine's own per-frame work (`S_Update` spatialises every channel), so
it stays; a per-pass cache of each entity's answer is the next step if a finer instrument shows it.
