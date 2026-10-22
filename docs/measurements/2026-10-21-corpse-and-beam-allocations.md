# The corpse worker's and the beams' allocations (finding 91)

## Observation

Finding 89's allocation trace (`2026-10-21-struct-copies-and-allocations.md`, f12 tick 30000, first person) left, in MB
per 20 s: `CorpsePhysics.Advance` 1,364 on the corpse worker, of which `Nullable<IvpMatrix>[]` 241; a corpse's
`SceneProp` copy in `UpdateClientSideAnimations` 306; `PoseLayer[]` 273; `PlacementOf`'s `float[]` 172; on the render
thread the heal-beam closure 173 and `VguiRenderer.BuildVertices` 99 (B574). Six fixes were made against those sites,
each with a zero-allocation or reuse test that was red first.

## Question

1. Per site, how many MB per 20 s does each allocate before and after the six fixes?
2. Do render-thread MB a frame, every-heap MB a frame, the collector's pause, the mean frame and the frames over
   100 ms in a 60 s run move?
3. Is the picture the same?

## Method

- Before: `e1ec318a` (a detached worktree at `D:\n90-base`). After: `6d252479`, the six fixes. After2: `58d77031`,
  a seventh fix added once the after trace showed the corpse copy still made (the corpse record re-fed each corpse's
  original record every tick). After2 is the whole and is the side compared for question 2.
- Scratch script `series.ps1 <root> <label> D:\n90-traces`, one locked call per side; raw output stays on
  `D:\n90-traces` (traces are 80+ MB each, too large for the repo); the `--measure` lines are pasted below.
- Each side built with `MSBUILDDISABLENODEREUSE=1 dotnet build managed/Tf2DemoSalvage.Viewer3D` (Debug) in the same
  locked command that runs it.
- Allocation: `dotnet-trace collect --profile gc-verbose` over `tf2demoview <f12> --tick 30000 --first-person
  --measure 25 +fps_max 0`, read by `alloc-trace <trace> 4 all 200 220 any` for the type table, and with the root
  argument `CorpsePhysics.Advance` for the corpse subtree. Same probe arguments both sides.
- Timing: `--measure 60` three times per side, first person, same tick.
- Equivalence: `--shot` at first-person ticks 30000, 30540, 31000, before twice and after once, compared with
  `pair-diff`.
- A run is void when its frame count is clearly below its siblings' or it shows a heartbeat gap; void runs are rerun
  once and said so.

## Control

- `alloc-trace`: the trace's render-thread MB per 20 s / frames must be near that run's `--measure` render-thread bytes
  a frame; the corpse worker's `CorpsePhysics.Advance` root must appear on both sides (a missing root means the probe
  lost the thread, not that the allocations went).
- Unchanged sites must stay put: `IvpContactRecord` (not touched) should read about the same on both sides.
- `pair-diff`: before against before is the control; a difference outside the fps text box (y 2-10) there voids it.

## Environment

- The development machine, Debug viewer, f12 (`demostf-cp_process_f12-2026-08-08-2207`) tick 30000, the default window,
  every run under `run-exclusive.ps1`. 2026-10-21, 20:51-23:40.
- Load (CPU %, before -> after each run): before trace 76 -> 100, s60 76 -> 32, 19 -> 100, 82 -> 23; after trace
  48 -> 24, s60 31 -> 52, 29 -> 19, 19 -> 38; after2 trace 3 -> 6, s60 14 -> 23, 31 -> 60, 63 -> 62. The machine was
  busy and uneven through the session (other agents' work between the locked runs), so frame counts vary 3,268-5,199.

## Result

**Deviations, stated before the numbers.**
- The trace windows are each trace's own last 22 s of playback, not a fixed 200-220: the three processes loaded at
  different speeds (232.2, 224.0 and 193.6 s traced), and a fixed window would have mixed the seek into one side.
  Windows: before 209-229, after 201-221, after2 170-190.
- The series' shots were taken with `TF2VIEW_AUTOPLAY=1` still set, so they played during capture and the control
  failed (before against before differed over the whole frame). They are void and were retaken without it
  (`*-still*`).
- The viewer's `--measure` line is not in the trace runs' output: `dotnet-trace` did not relay the child's stdout. The
  trace-against-`--measure` part of the Control is therefore not available; the corpse root and `IvpContactRecord` are.

**Allocation, MB per 20 s** (`alloc-trace <trace> 2 all <window> any`, with the root argument for the subtrees).

| site | before | after | after2 |
|---|---:|---:|---:|
| every heap | 4,393 | 3,415 | 3,627 |
| corpse worker, under `CorpsePhysics.Advance` | 1,440 | 1,067 | 1,355 |
| `IvpContactRecord` (not touched; the control for corpse work done) | 128 | 126 | 155-156 |
| `Nullable<IvpMatrix>[]` | 265 | under 14 | under 14 |
| `UpdateClientSideAnimations` (whole) | 464 | 497 | 100 |
| its `SceneProp`, `<- CorpseRecord.Run` | 427 | 457 | not listed |
| `AutoLayersFor` (whole) | 235 | 59 | 83 |
| `PoseLayer[]` | 278 | not listed | not listed |
| `PlacementOf` (whole) | 176 | 41 | not listed |
| render thread, under `FrameSequence.Run` | 1,574 | 1,374 | 1,637 |
| heal-beam closure `<>c__DisplayClass91_1` | 183 | absent | absent |
| `VguiRenderer.BuildVertices`'s `float[]` | 103 | absent | absent |

"Not listed" is below the probe's cut for that table (the 24th type of the corpse subtree is 14 MB; the eighth of the
whole trace about 120 MB). The corpse worker is a background pass that runs as fast as the machine lets it, so its MB
per 20 s scales with how much it got done: `IvpContactRecord`, which no fix touched, rose 22% in after2. Per unit of that
control, `Advance` is 11.3 before, 8.5 after and 8.7 after2.

**Timing, 60 s, first person.**

| run | frames | mean | render thread | every heap | pause | gen0 / gen1 | over 100 ms | gaps |
|---|---:|---|---|---|---|---|---:|---:|
| before 1 | 3,706 | 16.19 ms | 1.26 MB | 2.98 MB | 0.95 ms | 1,631 / 210 | 5 | 0 |
| before 2 | 4,217 | 14.23 | 1.25 | 2.78 | 0.87 | 1,687 / 269 | 14 | 1 |
| before 3 | 5,199 | 11.54 | 1.21 | 2.50 | 0.55 | 1,903 / 252 | 0 | 0 |
| after 1 | 3,268 | 18.36 | 1.01 | 2.22 | 0.86 | 1,049 / 169 | 7 | 2 |
| after 2 | 3,662 | 16.39 | 1.03 | 2.24 | 0.68 | 1,195 / 181 | 1 | 0 |
| after 3 | 4,726 | 12.70 | 0.95 | 1.89 | 0.47 | 1,307 / 192 | 5 | 1 |
| after2 1 | 5,022 | 11.95 | 0.94 | 1.54 | 0.36 | 1,124 / 168 | 2 | 0 |
| after2 2 | 3,563 | 16.84 | 1.01 | 1.83 | 0.69 | 945 / 148 | 3 | 0 |
| after2 3 | 4,165 | 14.41 | 0.98 | 1.72 | 0.50 | 1,031 / 171 | 6 | 0 |

Void by the rule in Method: before 2 (a heartbeat gap), after 1 and after 3 (gaps). None was rerun; the machine stayed
loaded. The frame counts of the rest still differ by up to 46%, so the mean frame is not compared.

**Equivalence** (`pair-diff`, retaken stills on `D:\n90-traces\*-still*`). Before against before (the control): 22 and
37 px in the fps box (805-823, 2-10) at 30540 and 31000, and 21,493 px in a band y 0-36 at 30000. Before against after2:
49-60 px, all in y 2-10, at all three ticks (one box starts at x 697, still in the text row). Second pair: 37 px in the
box at 31000 and the same y 0-36 band at 30000 and 30540, as in the control.

## Verdict

- (1) Every fixed site but one fell out of the tables: the slot arrays (265), the heal-beam closure (183), the HUD's
  vertex array (103), `PoseLayer[]` (278) and `PlacementOf` (176 -> 41 after, unlisted after2). The corpse copy did not
  fall with its first fix (464 -> 497), because the corpse record re-fed each corpse's original record every tick; with
  the seventh fix it is 100 for the whole pass.
- (2) Render thread a frame, every clean run: 1.21-1.26 MB before, 0.94-1.01 after2. Every heap a frame: 2.50-2.98 before,
  1.54-1.83 after2. Collections in 60 s: gen0 1,631-1,903 before, 945-1,124 after2. The pause a frame overlaps
  (0.55-0.95 against 0.36-0.69) and moves with the frame rate. Frames over 100 ms: 0-5 before, 2-6 after2 —
  **inconclusive** on this machine's load; nothing here shows the stalls went away or came back.
- (3) The picture is the same: every difference outside the control's own bands is in the fps text.
