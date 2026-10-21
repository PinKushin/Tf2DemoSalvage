# 85. The 55 seconds of load time was one enumerator (2026-10-20, B562)

`DemoTimeline.Build` on f12 (`demostf-cp_process_f12-2026-08-08-2207.dem`) took about 105 s before the D217 step 3
merge (`cecd3963`) and about 160 s from it on. The decision recorded the memory win and never a load-time cost. The cost
was not the checkpoints and not the trim. It was the enumerator of the new `PropertyBag`, which is the storage behind
every entity's properties.

## First question: does step 3 cost anything per frame?

No. The owner's priority is frame rate, so that was measured first. I ran `--measure 20` on f12 at tick 30000, in first
person and with an empty view (camera `0 0 6000 -89 0`), Release, under the machine lock, two repetitions. The other
agent's work kept the load at 3–90 %, so these numbers have noise. The gap between commits is far larger than the noise.

| commit | first person, mean frame | empty view, mean frame |
|---|---:|---:|
| `0277578c` (before step 3) | 212 / 179 ms | 128 / 95 ms |
| `cecd3963` (step 3) | 152 / 143 ms | 96 / 96 ms |
| `2fb6598c` (main) | 20.4 / 23.8 ms | 8.2 / 9.1 ms |

Step 3 is no slower per frame than its parent, and on first person it is faster. The much larger gain on main comes
from later work and is not part of this finding. Measured.

## Where the 55 s went: a sampled trace

`dotnet-trace collect --profile dotnet-sampled-thread-time` over the `timeline-time` probe (traces kept on `D:/b562-trace`).
Each figure is the inclusive share × the wall time of an untraced run beside it: 96 s at `0277578c`, 170 s at main, and
114 s for the fix (a run while the machine was loaded; a quiet run gave 99.5 s). That makes these arithmetic over a
measurement, good to a few seconds.

| phase (inclusive) | `0277578c` | main | fix |
|---|---:|---:|---:|
| whole build (`Builder.Run`) | 96 s | 170 s | 114 s |
| GC (`PollGCWorker`) | 52.4 s | 59.6 s | 51 s |
| `RecordProp`, all | 44.3 s | 107 s | 50 s |
| — `BoneControllers` + `PoseParameters` + `AnimationLayers` (entity scans) | ~5.5 s | 54.5 s | ~15 s |
| — of which `PropertyBag` enumerator `MoveNext` | — | 21.0 s | inlined |
| `CarriedItems` | 17.7 s | 20.4 s | 20.6 s |
| checkpoint building, copying, bag packing, `Backfill` | 0 | 0 | 0 |

The last row is zero by construction. `Build` is the straight build: it never checkpoints, never freezes a bag, and
never trims. So every second of the regression is in the decode walk. Of the 74 s added, 49 s is in the three pattern
scans, which walk every property of an entity looking for the keys of a pattern, several times per entity per packet.
GC accounts for 7 s more, from the allocations those walks made.

## The cause

Before step 3, `EntityState` stored its properties in a `Dictionary<string, PropertyValue>`. `foreach` over it uses the
dictionary's struct enumerator: no allocation and no interface call. Step 3 replaced that dictionary with `PropertyBag`
(chunked, so a checkpoint can share it). The bag's `GetEnumerator` was an iterator method that returned
`IEnumerator<KeyValuePair<…>>`. So each scan allocated an iterator object and made two interface calls per key, where
the dictionary had made none. An earlier review had already made the iterator resolve each chunk once, but it was still
an iterator.

## The fix

`PropertyBag.GetEnumerator()` now returns a public struct `Enumerator`. It walks the slot map's own struct enumerator
and resolves each chunk once. The interface versions box it, and only LINQ callers use them. The order and the values
are the same as before: the struct walks the same map in the same order and stops at the same visible count.

- **Test:** `PropertyBagTests.Enumerate_ThawedOrFrozen_AllocatesNothing` walks a 45-key bag, thawed and then frozen, and
  checks that the walk allocates 0 bytes and that the summed values are exact. On the iterator it failed (red). On the
  struct it passes.
- **Equivalence:** `timeline-fingerprint` gives the same four hashes before and after on both demos. On f12:
  frames `73C836A1…`, soundscapes `881844B2…`, props `3C6967B2…`, collections `F97EF29D…`. `timeline-windows` gives
  IDENTICAL, with 120 seeks and 0 ticks differing, both before and after.

| demo | build before → after | retained timeline heap (windows probe) | window decode mean |
|---|---:|---:|---:|
| f12 | 162.6 / 170.5 s → 99.5 / 112–116 s | 736.4 MB → 736.4 MB | 105 → 70 ms |
| rgl-pug-2026-08-10-pov | 232.9 s → 166.2 s | 1636.2 MB → 1636.2 MB | 94 → 62 ms |

rgl's fingerprints match as well: frames `02E7FEBF…`, props `5B4F4E8F…`. Its windows give IDENTICAL, 0 of 120 seeks
differing. The load during these runs was 16–46 %, so treat each wall time as good to about ten percent.

The memory win is untouched, because the storage did not change. A seek also gets faster, because a window's
decode-forward walks the same scans.

## What is left, and the lesson

The fix is still 3–20 s above `0277578c`, depending on load. In the fix trace, the scans take about 15 s against
about 5.5 s at `0277578c`, and `Builder.Run`'s own exclusive time is about 14 s against 2.6 s. That is small, it costs
load time only, and the owner has accepted load time as it stands. So I stopped here and did not build the remaining
options in B562 (later checkpoints off the critical path, a parallel walk).

**When a type replaces a `Dictionary` or `List` on a hot path, its `foreach` must be a struct pattern, not an iterator
method.** An `IReadOnlyDictionary` implementation compiles and passes every equality test. It also costs 2 × the
calls on every walk, and no test of the answer can show that. Only an allocation assertion or a trace can.
