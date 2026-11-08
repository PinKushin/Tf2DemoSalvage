# IVP's per-step scratch reused: garbage and frame time in playback (B708)

## Observation

`2026-11-08-landing-allocation.md`, read again over the played window (64.7 to 65.6 s) on thread 7948 under
`CorpsePhysics.Advance` with one caller frame, after `2026-11-08-ivp-ring-without-iterators.md` removed the ring iterators.
Four more sources, MB per 20 s:

| source | types | MB/20 s |
|---|---|---|
| `IvpSimulationUnit.RebuildEntries` | `IvpRigidBody[]` 117, `List<IvpRigidBody>` 45, `IvpUnitControllerEntry` 38 | 200 |
| `IvpMinimizeLoopCheck` (one per minimize) | `ValueTuple<long,long>[]` 194, its `List` 26 of 43, the check 38 | about 258 |
| `IvpMindistMinimize.BacksideWalk`, `TriangleWeights` | `Single[]` 117, `Boolean[]` 29 | 146 |
| the heap solve's arrays (`IvpComplementaritySolver..ctor`, `SolveHeap`, `SolveNormals`) | `Double[]` 330 | 330 |

About 0.93 GB per 20 s, against 3.5 GB for the thread and about 4.9 GB for the process in that window.

The change under test:
- recycles the unit's controller entries (`RebuildEntries`);
- reuses one loop-check store per thread;
- builds the backside walk's marks and both three-weight lists on the stack;
- takes the heap solve's arrays of doubles from a per-thread arena (`IvpArena`), zeroed and exactly as long as asked.

Each has a test that it allocates nothing, or hands back what it took (`IvpSimulationUnitTests`,
`IvpMindistMinimizeConformanceTests`, `IvpArenaTests`, `IvpFrictionFilingConformanceTests`), and each was sabotaged red. All
Animation.Tests pass: 5408 passed, 1 skipped.

## Question

In steady playback, how much do the gen0 collections, the allocation a frame and the GC pause fall, and does the frame
time move?

## Prediction

The process allocates 0.93 GB less per 20 s, about 19% of the window's 4.9 GB. So:
- allocation a frame falls from about 0.85 MB to between 0.65 and 0.75 MB;
- gen0 collections per 20 s fall by 12 to 25%;
- the frame mean moves by less than run-to-run noise (about 0.2 ms here).

Falsified if gen0 does not fall by at least 12% in both pairs.

## Method

Two Release builds alternated under the desktop lock, A B A B:
- A at `8cea6d88` (before), in a temporary worktree on `D:`;
- B this change.

Each:

```bash
TF2VIEW_VIEWPORT_SIZE=1280x720 pwsh run-exclusive.ps1 <build>/tf2demoview.exe tools/corpus/demos/z1800.dem --tick 20000 --autoplay --measure 20
```

`--measure` counts playback after the opening seek's landing. Read the summary line's frame mean and its `gc` part from
stdout.

## Control

Both builds must report the same playback span (`ending at tick` within a few ticks) and a nonzero gen0 count. A's two runs
must agree (gen0 within 15%), or the pair says nothing. A's gen0 should be near the 382 and 374 B measured in
`2026-11-08-ivp-ring-without-iterators.md`, being the same code.

## Environment

The owner's desktop machine: Windows 11, 6 cores and 12 logical, viewport 1280x720. TF2 is closed and no other viewer runs.
A is a Release build of `8cea6d88` in `D:/measure-f12/b708-alloc/a`. B is a Release build of this branch: the change is
uncommitted on top of `e9626422`, the pre-run commit. Load before and after was not recorded.

## Result

The control holds:
- every run ends at tick 21340, with nonzero gen0;
- A's two runs agree within 1% (377 and 380);
- A is near the earlier B of the same code (382 and 374).

```
== A
measured 20 seconds of playback, 19 samples (19 rebuild reports), ending at tick 21340; 3038 frames, mean 6.58 ms
  gc: paused 0.36 ms a frame; collections gen0 377, gen1 71, gen2 0; allocated 0.88 MB a frame; render thread 411,227 bytes a frame
== B
measured 20 seconds of playback, 19 samples (19 rebuild reports), ending at tick 21340; 3084 frames, mean 6.49 ms
  gc: paused 0.34 ms a frame; collections gen0 341, gen1 59, gen2 0; allocated 0.78 MB a frame; render thread 408,064 bytes a frame
== A
measured 20 seconds of playback, 19 samples (19 rebuild reports), ending at tick 21340; 3082 frames, mean 6.49 ms
  gc: paused 0.35 ms a frame; collections gen0 380, gen1 64, gen2 0; allocated 0.86 MB a frame; render thread 412,499 bytes a frame
== B
measured 20 seconds of playback, 19 samples (19 rebuild reports), ending at tick 21340; 3378 frames, mean 5.92 ms
  gc: paused 0.29 ms a frame; collections gen0 350, gen1 62, gen2 0; allocated 0.73 MB a frame; render thread 401,865 bytes a frame
```

| | A1 | B1 | A2 | B2 |
|---|---|---|---|---|
| gen0 over 20 s | 377 | 341 | 380 | 350 |
| allocated a frame | 0.88 MB | 0.78 MB | 0.86 MB | 0.73 MB |
| GC paused a frame | 0.36 ms | 0.34 ms | 0.35 ms | 0.29 ms |
| frame mean | 6.58 ms | 6.49 ms | 6.49 ms | 5.92 ms |

## Verdict

**Falsified on the stated criterion.** Gen0 collections fell 9.5% and 7.9% in the two pairs, short of the 12% predicted.
Other results:
- allocation fell 11% and 15% a frame (0.88 to 0.78 MB, 0.86 to 0.73 MB);
- the GC pause fell 0.02 and 0.06 ms a frame;
- the frame mean fell 0.09 and 0.57 ms. The second pair's fall is larger than this machine's run-to-run spread, but one pair
  does not establish it.

**Why the prediction overshot:** the allocation removed is about 0.10 to 0.13 MB a frame. Over the window's 3,100 frames that
is 0.3 to 0.4 GB per 20 s, not the 0.93 GB the trace implied. `2026-11-08-ivp-ring-without-iterators.md` overshot by the same
factor: 0.83 GB predicted, about 0.34 GB measured (0.11 MB a frame).
*Interpolated, not measured:* the trace's window is the 0.9 s just after the landing, scaled to 20 s. The corpses are most
active then, and they settle over the 20 s `--measure` window, so the trace's per-20 s rates overstate the window's average
by about 2.5 times. **A prediction from that trace should divide by about 2.5.**

The corpse physics' remaining garbage is led by `IvpContactRecord` (333 MB per 20 s in the trace, one per `Build`), then
`IvpLedgeSide` (201, spread over six callers) and `IvpContactBody` (84, `IvpPairDamping.Bank` 67). `Build` replaces the
contact's record, so reusing it in place needs every member reset and every holder of the old record checked first.
