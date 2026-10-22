# 91. Seven allocations behind the collector, and a fix its caller undid (2026-10-21)

**The question.** Finding 89 ranked what still allocates on f12 (tick 30000, first person) and left B574's open list.
Which of those can go without changing what the engine does, and what do they buy? Every number here is from
`docs/measurements/2026-10-21-corpse-and-beam-allocations.md`, which holds the windows, the void runs and the load.

## (1) The fixes, and why each keeps the engine's behaviour

Each has a test that was red first. Allocation shape is ours to change; results are not.
- **The time-of-impact motion caches.** The engine keeps its 21 slots in the searcher's own storage (`FUN_1800a0800`,
  finding 51). Ours made a new cache and a new slot array per search. The simulation now holds two caches and rebuilds
  them per search, emptying every slot past 0. *Measured by sabotage*: without the emptying, five tests redden,
  among them a corpse drop's pose hashes. So the clear is what keeps the result, not a tidy-up.
  The sides are built inside the search delegate, which the scheduler calls synchronously, so no other pair's
  `Examine` can rebuild them in the middle of a search.
- **The layer lists.** A skeleton's layers are now appended to its own list, which is emptied first. Before, a new list
  was built per prop per frame, plus one per autolayer pass. The order is unchanged. Every reader takes `Layers` within
  the frame.
- **The placement.** `PlacementOf` writes `matrix3x4_t` into the entity's own array through a stack matrix. Before, it
  built a 16-float array and a 12-float array. `PropTransform.ToMatrix` now uses the same `WriteMatrix`, so the
  formula exists once.
- **The heal-beam closure.** The lambda in the loop body captured the loop's locals. So the compiler allocated its
  closure at the top of every iteration over all 1,211 of f12's beams, live or not. This is the same trap as
  `docs/memory/a-capturing-cache-factory-allocates-on-a-hit.md`. The closure is now built in its own method, for a
  live beam only.
- **The HUD vertices** are written into the mapped vertex buffer, as the engine's mesh builder writes into its locked
  one.
- **The corpse copy**, which needed two fixes (2).

## (2) A fix that its only caller undid

`UpdateClientSideAnimations` copied a corpse's whole prop each tick to write its sequence. The first fix skipped the copy
when the sequence was already the one it would write, with a test that passed the same list in twice. The after trace
showed the copy unchanged: 427 MB per 20 s before the fix, 457 after it. The test was right about the method and wrong
about the caller. `CorpseRecord.Run` builds its `alive` list from each corpse's original record every tick, so the
sequence was never already resolved. The second fix carries each corpse's resolved prop on to its next tick. The whole
pass then reads 100.

This is `docs/memory/output-level-assertion-or-it-is-not-done.md` again. A unit test proves what the component does
with the values the test chose. Only the trace of the real path showed that production never hands it those values.

## (3) What it bought

*Measured*, 60 s runs that the heartbeat rule leaves clean:
- **Render thread:** 1.21-1.26 → 0.94-1.01 MB a frame.
- **Every heap:** 2.50-2.98 → 1.54-1.83 MB a frame.
- **Gen0 collections:** 1,631-1,903 → 945-1,124.

The pause a frame overlaps between the two sides and moves with the frame rate. **Frames over 100 ms are inconclusive.**
The machine was loaded and uneven all session: frame counts ran 3,268-5,199 for the same 60 s, and three runs had
heartbeat gaps. Nothing here shows the stalls went away, and nothing shows they came back.

**A corpse-worker figure needs its control.** The corpse worker is a background pass that runs as fast as the machine
allows, so MB per 20 s scales with the work it got done. `IvpContactRecord`, which no fix touched, rose 22% on the
quiet after2 run. Against that control, `CorpsePhysics.Advance` went from 11.3 to 8.7.

## (4) What is left

The corpse tail is the IVP port's own small objects: contact records, solver arrays, ledge sides, topology iterators.
None is over 160 MB per 20 s. Each wants the engine's ownership read first (B574). On the render thread, the largest
is `Device3D.File`'s closure (150).
