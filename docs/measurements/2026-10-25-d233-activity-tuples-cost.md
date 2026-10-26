# Measurement 2026-10-25: what the engine's weighted pick costs (D233)

**Written after the run** (no prediction was fixed beforehand; the owner asked for the cost to be reported, not a
hypothesis tested). No viewer was timed: the render-thread work was being timed by another agent.

## Question

What do per-activity candidate lists cost in retained memory over a loaded model set, what does a pick cost against the
heaviest-only lookup it sits beside, and does the per-frame held path allocate?

## Method

`dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- activity-tuples D:/measure-f12/f12.dem 30000`, twice.
The probe takes the skinned models of the props at the tick plus the nine class models, through the production loader.

- **Retained bytes**: `GC.GetTotalMemory(true)` around building every model's map on a fresh merged table, minus the same
  around the fresh tables alone (the control), against a heaviest-only `Dictionary<string, int>` of the same names (what the
  B584 map held).
- **Per call**: Stopwatch over 2,000,000 calls each of `ForActivity` and `SelectWeightedSequence` for
  `ACT_MP_STAND_PRIMARY`, round-robin over the nine class models, Release.
- **Held path allocation**: `SelectWeightedSequenceConformanceTests.UpdateClientSideAnimations_HeldPath_AllocatesNothing`
  (`GC.GetAllocatedBytesForCurrentThread` around one whole `UpdateClientSideAnimations` pass on a held player).

## Result

Both runs identical in counts and bytes; timings within 0.3 ns.

| | value |
|---|---:|
| skinned models | 64 |
| sequences | 3,869 |
| sequences with an activity (candidates) | 1,369 |
| activities | 1,339 |
| activities with more than one candidate | 19 |
| retained, tuple maps | 195,928 B |
| retained, heaviest-only map of the same names | 55,784 B |
| `ForActivity` | 48.3-48.4 ns/call |
| `SelectWeightedSequence` | 50.1-50.4 ns/call |
| allocated over 4,000,000 calls | 40 B (the harness, not per call) |
| held path, one pass, one player | 0 B (was 88 B: the `declared` closure, now one delegate per model) |

**Reading.** The tuple lists cost about 140 KB more than the heaviest-only map across 64 models. A pick costs the same as the
lookup it replaces, and runs only when an activity changes; a held frame does the same lookups as before plus one dictionary
store. Only 19 activities on these models have more than one candidate, so the visible change is confined to those.
