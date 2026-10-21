# Struct copies and the allocations left on f12 (finding 89)

## Observation

Finding 88's CPU sample of the render thread (f12, `demostf-cp_process_f12-2026-08-08-2207`, tick 30000, first person)
put `Buffer.BulkMoveWithWriteBarrier` at 18.3% inclusive and `PollGCWorker` at 38% as a leaf; its allocation trace left
2.2 MB a frame on the render thread and 5.1 MB on every heap, and the collector paused about 1.1 ms a frame (B567, B573).

**Deviation, stated up front.** The runs below were taken on 2026-10-21 between 15:20 and 18:45, before this file existed:
the measurement gate went live while they ran. This file was written afterwards from their raw output, which is kept
unedited in `2026-10-21-struct-copies-and-allocations/`. Nothing in Method was changed to fit the result.

## Question

1. Which callers make the struct copies under `BulkMoveWithWriteBarrier`, and is that 18% copying or something else?
2. Ranked by bytes, what still allocates per frame on the render thread and on the corpse worker?
3. Do the three fixes made for the largest of them change render-thread bytes a frame, every-heap bytes, the collector's
   pause and the mean frame, and the count of frames over 100 ms in a 60 s run?

## Method

- CPU: `dotnet-trace collect --profile dotnet-sampled-thread-time` over the Debug viewer, `--tick 30000 --first-person
  --measure 25 +fps_max 0`, read by the probe `cpu-trace <trace> 230 252 <render thread>` with leaf filters
  `BulkMoveWithWriteBarrier` and `PollGC` (four frames above each leaf).
- Allocation: a separate run of the same command with `--profile gc-verbose`, read by
  `alloc-trace <trace> 4|7 all 200 220 any|<thread>`.
- Timing: `--measure 20` (first person twice per commit, empty view `TF2VIEW_CAMERA="1613 -2821 154 89 0"` once at each
  end), and `--measure 60` three times per side for the stalls. Each commit is built in the same locked script that runs
  it (scratch `series.ps1`/`run.ps1`), and the CPU load is sampled inside the lock before and after each run.
- A run is void when its frame count is clearly below its siblings', or it shows a heartbeat gap, or it overlapped the
  owner's 16:55-17:00 window (a closed process during a Drive install); void runs were rerun.
- Equivalence: `--shot` at first person ticks 30000, 30540, 31000 and a free camera (`-3700 -2300 760 20 200`) at 30540,
  for main twice and the final commit once, compared with `pair-diff`.

## Control

- `cpu-trace`: the render thread is the one whose top leaf is `Device3D.DrawFrame` and whose `EntityModelSet.Instances`
  is ~29% inclusive, as in finding 88. A `BulkMoveWithWriteBarrier` that is a real copy must show as a leaf.
- `alloc-trace`: the `ScenePlayer[]` from `SpectatorTarget.Observable` and `SunFrom`'s tuple arrays, both named in B567,
  must appear; the render thread's MB per 20 s / frames should be near the `--measure` line's render-thread bytes.
- `pair-diff`: main shot twice is the control; a difference outside the fps text box (y 2-10) in the control would make
  the comparison useless.

## Environment

- The development machine, Debug viewer, f12 tick 30000, the default window. Every run under `run-exclusive.ps1`.
- Commits: main `5f6ba673` (before); `371a0cbb` (+`Choose`), `50420d96` (+the sweep), `177ebbd0` (+the empty table).
  Those hashes are the pre-rebase ones the runs built; the rebased commits carry the same trees.
- Load before → after each run is the last line of each raw file. Other agents' viewer runs held the lock between mine,
  and a Google Drive first sync started during the session, so load was uneven: 4–46% before and 1–60% after.

## Result

**CPU sample** (`cpu-fp.txt` is the run's `--measure` line; probe output pasted). Render thread 34488, 9,980 samples in
22 s. `BulkMoveWithWriteBarrier` is 17.2% inclusive and 0.0% as a leaf. Its leaf beneath is `PollGCWorker`, which is
37.3% of all leaves. The copies reaching `PollGC`, by share of the thread:

| caller of `BulkMoveWithWriteBarrier` | share |
|---|---:|
| `SceneProp.GetHashCode` ← `MomentScene.Build` (a record hashed whole as a key) | 1.4% |
| `List<ParticleBurst>.Enumerator.MoveNext` ← `ParticleEffects.Bursts` | 1.3% |
| `EntityModelSet.Instances` (inline) | 1.1% + 0.3% |
| `SimulateEach` 0.8, `ParticleEffects.Gather` 0.7 + 0.2, `EffectEntities.BuildEffectEntities` 0.7, `ScenePropTrack.At` 0.7 | 3.1% |
| `PlacementOf` 0.5, `PlayerProps.Add` 0.4, `SceneProp..ctor` 0.4, `FlexFor`'s dictionary 0.4, `AdvanceSample` 0.4, `SceneProp.get_Pose` 0.3 + 0.3 | 2.7% |

**Allocation, MB per 20 s** (`alloc-trace … 200 220`; `alloc-fp.txt` is the run's line). Render thread 2,302 in all.

| type | site | thread | MB / 20 s |
|---|---|---|---:|
| `(float, float, float)[]` | `SunFrom` → (tail-called) `DisplacementSweep.EdgePlanes` | render | 374 |
| `SceneProp` | `UpdateClientSideAnimations`, a corpse's `with` | corpse | 306 |
| `ScenePlayer[]` | `SpectatorTarget.Observable` ← `Choose`, six callers | render | 212 |
| `PoseLayer[]` | `AutoLayersFor` 164, `SimulateEach` 101 | corpse | 273 |
| `Nullable<IvpMatrix>[]` | `IvpVertexFaceSearch.Search` 145, `IvpRootFinder.March` 47 | corpse | 241 |
| `Dictionary<string, string>` | `UpdateClientSideAnimations` → `PlayerActivityTable.For` | corpse | 185 |
| heal-beam closure | `BurstEffects.AddHealBeams` | render | 173 |
| `float[]` | `PlacementOf` 137, `ToBoneMatrix` 35, `VguiRenderer.BuildVertices` 99, `OcclusionQueries.Count` 29 | both | 440 |

The corpse worker as a whole: `CorpsePhysics.Advance` 1,364, `UpdateClientSideAnimations` 560, `AutoLayersFor` 259,
`PlacementOf` 172.

**Timing, 20 s, first person** (raw files `s20-*`, `r20-*`):

| commit | runs | mean frame | render thread a frame | every heap | collector's pause |
|---|---:|---|---|---|---|
| main | 3 | 15.00–15.59 ms | 2.21–2.24 MB | 5.00–5.12 MB | 1.11–1.17 ms |
| + `Choose` | 2 | 14.59–15.94 | 1.83–1.86 | 4.50–4.58 | 1.02–1.10 |
| + the sweep | 2 | 13.98–14.62 | 1.45–1.48 | 3.99–4.33 | 0.91–0.99 |
| + the empty table | 2 | 14.57–15.04 | 1.46 | 4.09–4.20 | 0.97 |
| empty view, main → final | 1 + 1 | 5.78 → 5.97 | 0.86 → 0.73 | 2.02 → 1.83 | 0.39 → 0.38 |

Void: `s20-5f6ba673-fp1` (load 100% before), `s20-5f6ba673-empty1` (the 16:55–17:00 window), `s20-371a0cbb-fp2` (1,022
frames against 1,255–1,371; load 57% after).

**Stalls, 60 s, first person** (raw files `s60-*`, `s60b-*`):

| run | frames | mean | render thread | every heap | pause | frames over 100 ms | heartbeat gaps |
|---|---:|---|---|---|---|---:|---:|
| before 1 | 5,325 | 11.27 ms | 1.87 MB | 3.13 MB | 0.62 ms | 7 | 0 |
| before 2 | 5,122 | 11.71 | 1.89 | 3.20 | 0.66 | 2 | 0 |
| before 3 | 5,390 | 11.13 | 1.85 | 3.09 | 0.62 | 2 | 0 |
| after 1 | 5,443 | 11.02 | 1.20 | 2.40 | 0.52 | 0 | 0 |
| after 4 | 5,478 | 10.95 | 1.20 | 2.40 | 0.52 | 0 | 0 |

Void, after: run 2 (4,323 frames, 11 over 100 ms, a gap, load 60% after), run 3 (4,235 frames, a gap) and run 3's rerun
(4,486 frames, a gap, load 45% before). Each had a heartbeat gap, which is a process-wide stop, and none of the before
runs had one. So only two clean after runs stand against three before.

**Equivalence** (`pair-diff`, shots on `D:\n89-traces\shots`). Main against main (the control): 28–40 px, all in the box
(805–823, 2–10). Main against the final: 12–53 px, all in the same box. That box is the fps text.

## Verdict

- (1) The 17% under `BulkMoveWithWriteBarrier` is not copying. A large struct copy is a GC poll site, and the samples
  there are the thread waiting for the collector. The copies are cheap, and the allocations behind the collector are the
  cost.
- (2) The ranking is the table above.
- (3) The three fixes take the render thread from 2.21–2.24 to 1.46 MB a frame (20 s), and from 1.85–1.89 to 1.20 (60 s).
  Every heap goes from 3.09–3.20 to 2.40, the pause from 0.62–0.66 to 0.52 ms, and the 60 s mean frame from
  11.13–11.71 to 10.95–11.02 ms. The clean after runs had 0 frames over 100 ms, against 2–7 before.
- The 20 s mean frame moved inside its noise. The empty-table fix's 0.14 MB a frame on the corpse worker is below the 20 s
  runs' spread, so its effect is not resolved there. It is resolved only by its test.
