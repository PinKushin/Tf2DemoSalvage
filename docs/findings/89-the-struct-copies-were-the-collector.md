# 89. The struct copies were the collector, and three allocations behind it (2026-10-21)

**The question.** Finding 88 left `Buffer.BulkMoveWithWriteBarrier` at 18% of the render thread. That is the runtime's
copy of a struct that holds references (B573). It also left 2.2 MB a frame allocated on the render thread, and as much
again on the corpse worker (B567). Which callers make those copies? What allocates now, ranked fresh rather than taken
from B567's list? Can the largest be removed? Every number here is from
`docs/measurements/2026-10-21-struct-copies-and-allocations.md`, which holds the raw output and the void runs.

## (1) `BulkMoveWithWriteBarrier` is where the thread waits, not what it does

*Measured* (CPU sample, f12 tick 30000, first person, the render thread's last 22 s): 17.2% inclusive, **0.0% as a
leaf**. Beneath it is `PollGCWorker`. The runtime polls for a pending collection inside a long reference-holding copy,
so a thread that is asked to stop for the collector stops there, and the sampler charges the wait to the copy.
- **The wrong reading, and how it was killed.** B573 read the 18% as copying, and proposed passing `StudioBone` and the
  scene records by reference. Had the copies cost that much, the helper would carry the time as its own leaf samples.
  It carries none.
- **Which copies are poll sites** (shares of the thread). `SceneProp.GetHashCode` under `MomentScene.Build` 1.4%: the
  record is hashed whole as a dictionary key. `List<ParticleBurst>`'s enumerator 1.3%. `Instances` 1.4%, and a long tail
  of under 1% each. Removing a copy only moves the wait to the next safe point.
- **So the cost is the allocation that makes the collector run.** The engine allocates nothing per frame, so it has no
  collector to wait for. Fixes 1-3 go at the bytes.

## (2) What allocates now (MB per 20 s, fresh trace)

Render thread:
- `DisplacementSweep.EdgePlanes`, 374 MB. It builds three `(float, float, float)[]` for every terrain triangle that a
  light cache sun trace tests. The stack shows `SunFrom`, because `StrikesSky` and `TraceBrushOnly` are expression
  bodies the JIT tail-calls, so their frames vanish. *Read in our code*: the arrays are in `EdgePlanes`.
- `SpectatorTarget.Observable`'s `ScenePlayer[]`, 212 MB, from six callers of `Choose` a frame.
- The heal-beam closure, 173 MB.
- `VguiRenderer.BuildVertices`, 99 MB.

Corpse worker:
- `CorpsePhysics.Advance`, 1,364 MB.
- `UpdateClientSideAnimations`, 560 MB. Of it, `SceneProp` copies are 306 and the string dictionary is 185.
- `PoseLayer[]`, 273 MB.
- `Nullable<IvpMatrix>[]`, 241 MB.

**The string dictionary was not the corpse's.** `PlayerActivityTable.For` answered `PlayerActivityOverride.None`, the
table of every ordinary player, with a new empty `Dictionary<string, string>` on every translation. *Read in our code.*

## Fixes

1. **`SpectatorTarget.Choose` scans in place.** The engine's spectator target is a pointer into its entity list, and it
   copies no roster. Ours sorted a filtered copy six times a frame. Strict `<` keeps the first of equal indices, which is
   what the stable `OrderBy` gave. Test: `SpectatorTargetTests.Choose_CalledEveryFrame_AllocatesNothing`. It is red
   when the old body is put back, and the nine existing tests are unchanged.
2. **The triangle sweep's vectors are `ReadOnlySpan` collection expressions.** `CDispCollTree::SweepAABBTriIntersect`
   holds them in registers. Test: `DisplacementSweepTests.Against_ASecondSweep_AllocatesNothing`, red before the change.
   The ten `DisplacementSweepConformanceTests` give the same fractions.
3. **One shared empty table.** Test: `PlayerActivityTableTests.Translate_WithNoPlayerTable_AllocatesNothing`. It was
   red at 80 bytes, one empty dictionary.

| f12 tick 30000, first person (measurements/2026-10-21-struct-copies-and-allocations.md) | before | after |
|---|---:|---:|
| render thread a frame, 20 s | 2.21–2.24 MB | 1.46 MB |
| render thread a frame, 60 s | 1.85–1.89 MB | 1.20 MB |
| every heap a frame, 60 s | 3.09–3.20 MB | 2.40 MB |
| collector's pause a frame, 60 s | 0.62–0.66 ms | 0.52 ms |
| mean frame, 60 s | 11.13–11.71 ms | 10.95–11.02 ms |
| mean frame, 20 s | 15.00–15.59 ms | 14.57–15.04 ms (inside the noise) |
| empty view, 20 s: render thread / mean frame | 0.86 MB / 5.78 ms | 0.73 MB / 5.97 ms (one run each) |

Fix by fix, at 20 s, the render thread went 2.21–2.24 → 1.83–1.86 (`Choose`) → 1.45–1.48 MB (the sweep). The third fix
is the corpse worker's, about 0.14 MB a frame. That is below the spread of every-heap bytes between 20 s runs, so the
runs do not resolve it, and only its test does.

**Stalls, 60 s, frames over 100 ms:** before 7, 2, 2. After 0, 0, in the two clean runs. Three more after-runs were void:
each had a heartbeat gap (a process-wide stop, which no before run had), ~1,000 fewer frames than its siblings, and in
two of them high load. The machine had other agents' viewer runs and a Google Drive first sync. Two clean runs against
three is short of the three-per-side the plan asked for.

**Equivalence.** Shots at first-person ticks 30000, 30540 and 31000, and a free camera at 30540, compared with
`pair-diff`. Main against itself is the control: 28–40 px. Main against the final: 12–53 px. Both are confined to the fps
text box (x 805–823, y 2–10).

## The empty-view floor (B572): not started

The work stopped at three fixes, as the plan said. B572's buckets were not read against the engine this session. The
empty view's render thread fell 0.86 → 0.73 MB, and its frame did not move, at 5.78 against 5.97 ms in one run each.

## Divergences found

- `SceneProp` is hashed as a whole record wherever it keys a dictionary (`MomentScene.Build`). The engine keys an
  entity by its index or pointer.
- A corpse's `UpdateClientSideAnimations` makes a new `SceneProp` and `ScenePose` every tick, even when the sequence it
  sets is the one it already holds.
- `PlayerAnimation.For` allocates a closure (`declared`) on every call.

All three are filed in B574.

## Wrong turns

- **The series script ran one step of six.** A `[string[]]` parameter passed through `pwsh -File` binds only its first
  value. The run reported success, and the missing files showed it. It is a comma-separated string now.
- **The measurement gate refused an untouched Control.** It went live mid-session. The Control held a `÷`. The gate
  reads the committed copy through `git cat-file` and the working copy through `Get-Content`, which decode a non-ASCII
  character differently. It was replaced by `/` in a pre-run commit. Reported to the lead as a hook defect.
