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
