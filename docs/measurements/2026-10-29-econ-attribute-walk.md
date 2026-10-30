# 2026-10-29 — the per-prop attribute questions without a list, a set or a prefab walk (B599 item 1)

**Status: written and committed before any timed run.** Sections above `## Result` are fixed from here; deviations go
under Result.

## Observation

B599 (`docs/RISKS.md`) names `EconAttributes.Resolve` and `MomentScene.Build`'s attribute lambdas — "a `HashSet`, list
and dictionary per weapon per frame (~90 KB)" — from the 2026-10-25 trace. Read on `2a46490c`: the model set's `Paint`
and `Attachments` delegates (`MomentScene.Build`, `MomentScene.cs:477-499`) are called once per placed prop per frame
(`EntityModels.Instances.cs:756`, `:882`), and each calls `WeaponModels.AttributesFor`, which

- asks `ItemSchema.DefinitionAttributesFor` for an item prop, which builds a `List`, a `HashSet<string>` and walks the
  prefab chain on every call (`ItemSchema.cs:1579-1592`), and
- calls `EconAttributes.Resolve`, which builds a `List` and a `HashSet<int>` for every prop, item or not;

and `PaintFor` then fills a `Dictionary` from the result for every prop. `AttributeHooks` (`PowerupBottle.Apply`, the
motion-cloak hook) resolves the same way per carried item.

## The engine's shape (read from published source, source-sdk-2013)

- **A definition's attributes are parsed once.** `CEconItemDefinition::BInitFromKV` appends each `static_attrs` and
  `attributes` entry to `m_vecStaticAttributes` at schema init (`econ_item_schema.cpp:3330-3359`);
  `CEconItemDefinition::IterateAttributes` only walks that vector (`:3544-3561`).
- **An item view's attributes are streamed, not collected.** `CEconItemView::IterateAttributes`
  (`econ_item_view.cpp:523-600`) walks the local list into the caller's iterator, then branch 3 or 4 through
  `CEconItemAttributeIterator_EconItemViewWrapper`, which suppresses a definition the local list holds by
  `GetAttributeList()->GetAttributeByID` — a linear search. Nothing is allocated.
- **A hook's RESULT is cached per attribute manager** (`CAttributeManager::ApplyAttributeFloatWrapper`,
  `attribute_manager.cpp:331-388`), purged on a provider change (`:213-287`) or a global cache version. **Not built
  here**: this change removes allocation from the walk and keeps recomputing per call, so no cache keyed on "unchanged"
  is introduced (`docs/memory/count-the-changes-before-caching-on-unchanged.md`).

**One divergence noticed and NOT changed (a parity question for the lead):** our resolution suppresses a repeated
definition WITHIN the local list and within the fallback too; the engine's wrapper suppresses only fallback entries the
local list holds, and the local list is walked whole. The walk keeps our current semantics exactly.

## The change (`perf/alloc-sites-2`)

- `EconAttributes.Iterate` → `ResolvedAttributes`, a struct walk with a struct enumerator: local, then the chosen
  fallback, skipping a definition any earlier entry produced (a linear prefix search, as the engine's wrapper searches).
  `Resolve` keeps its signature and is now that walk into a list.
- `WeaponModels.AttributesFor` returns the walk; `IsFestivized`, `VisionGrantedBy`, `TfHudItemEffectMeter.FindAttribute`
  and `AttributeHooks` (through a new `ItemSchema.Apply(ResolvedAttributes, …)`) walk it.
- `ItemSchema.DefinitionAttributesFor` builds each definition's array once and keeps it (a `ConcurrentDictionary`,
  non-capturing factory) — the engine's `m_vecStaticAttributes`. The schema is immutable after `Read`.
- `PaintFor` returns null before building its dictionary when the item carries no primary paint attribute —
  `ItemPaint.Packed` returns null in exactly that case before reading anything else.

## Question

On f12, does the change remove the attribute sites' bytes a frame, with every drawn frame identical, and what does it do
to the frame?

## Hypothesis

The allocation falls by the census's measured bytes for those sites (`measurements/2026-10-29-allocation-census.md`);
the frame time moves by less than the run-to-run spread.

## Prediction

- **Allocation (G, gummo first person):** `allocated … MB a frame` falls by 0.04-0.15 MB. **E (empty view, posed 0):**
  falls by at most 0.02 MB (the delegates run only for placed props).
- **Frame:** inside the spread in both views; no claim unless the ranges separate.
- **Identity:** world pixels of paused shots A/B 0 different (the HUD frame-time digits excepted, as in the 10-28
  record); the mode-2 verifier on B reports 0 changed frames.

Falsified if the allocation does not fall by at least 0.03 MB in G, or any identity check fails.

## Method

- A = `2a46490c` (label `a`, `D:/measure-f12/as2-a-src`); B = this branch's code commit (label `b`,
  `D:/measure-f12/as2-b-src`), each from `git archive`, `-c Release`.
- Every run through `docs/measurements/2026-10-29-alloc-sites-2/run.ps1` (quiet gate by load: no other viewer, three samples under 25%, other agents' dotnet processes counted per run;
  `run-exclusive.ps1`, 1280x720,
  `--tick 30000 --measure 20 +fps_max 0`): first person ABBA BAAB, then empty ABBA BAAB.
- Identity: `--shot` per view per build plus an A/A control; one `TF2VIEW_QUEUE_VERIFY=1` run on B (first person).
  Compared with `2026-10-27-empty-view-breakdown/pixdiff.ps1 -WorldOnly`.
- Reading: `2026-10-27-empty-view-breakdown/read.ps1` per run (steady window, seconds 6-20, frame-weighted), and each
  run's `gc:` line (`allocated … MB a frame`, `render thread … bytes a frame`).
- Attribution: one traced run of B per view as the census (`-Seconds 120 -Trace …`), same analyser, to show the sites
  gone.

## Control

- Every run ends at tick ~31347; empty-view moment lines read `posed 0`.
- The A/A shot pair bounds what "identical" can mean.
- The census's A trace is the before attribution; the B trace must lose the econ sites and keep the untouched ones
  (`EffectEntities.BuildEffectEntities`, the particle sites) at their A size within ~15%.

## Environment

RTX 3060, i5-10600K, 2026-10-30 00:26-02:12. A = `D:/measure-f12/as2-a-src` (`2a46490c`), B = `D:/measure-f12/as2-b-src`
(`bece7a0d`), both Release from `git archive` (117 warnings on each archive's first build, 0 on an incremental one; the
in-tree build of `bece7a0d` is 0). Every timed run: load before 2-13% on all three samples, no other dotnet process,
exit 0; one after-sample of 100% (run 10, as the viewer exited). Raw stdout, shots and traces in
`D:/measure-f12/alloc2/runs/item1/`, not committed.

## Result

**Deviations.** (1) `all.ps1`'s loop variable `$b` is the `-B` parameter in PowerShell (names ignore case), so the B
runs were launched as label `B` — the folder `as2-B-src`, which on Windows IS `as2-b-src`: the right build, by luck
(fixed in `e648792f` before any later pair ran). (2) The B trace of the empty view did not run: the queue's job hit its
two-hour limit during the first-person trace. (3) Two extra empty-view shots, B2 and A3, were added after the first
pair differed in the top bar (below).

**Controls.** Every timed run ended at tick 31,323-31,347; empty-view moment lines `posed 0`.

**Allocation (the decision).** `gc:` line per run, and the main thread's bytes (`render thread … bytes a frame`, which
is the main thread by the 10-25 record's reading):

| view | A (4 runs) | B (4 runs) | B − A |
|---|---|---|---|
| first person, MB a frame | 0.97-1.00 | 0.88-0.89 | **−0.10 MB** |
| first person, main thread bytes | 747,451-752,872 (750,856) | 641,731-648,412 (646,136) | **−104,720 (−14%)**, ranges apart |
| first person, gc pause | 0.22-0.23 ms | 0.20-0.21 ms | −0.02 ms |
| empty, MB a frame | 0.49-0.53 | 0.49 | −0.01 |
| empty, main thread bytes | 389,160-398,843 (391,899) | 380,352-382,061 (381,120) | **−10,779 (−2.8%)**, ranges apart |

Attribution (B trace, first person, same analyser as the census): 641.1 KiB a frame against the census's 744.2; the
econ sites are gone but for `PaintFor`'s dictionary on the painted items (1.7 KiB), and untouched sites hold
(`EffectEntities` boxed enumerator 16.2 vs 16.5, `WorldDecals.Placed` 18.8 vs 19.9, `ImpactParticles` 54.5 vs 54.1).

**Frame, steady window (seconds 6-20), per-second lines frame-weighted, ms (% of A's mean steady frame):**

| view | part | A | B | B − A | |
|---|---|---|---|---|---|
| first person | frame | 6.035-6.100 (6.076) | 5.920-6.030 (5.980) | **−0.095 (1.6%)** | ranges apart by 0.005 ms |
| first person | `camera pose` | 2.893-2.943 (2.922) | 2.794-2.836 (2.821) | **−0.101 (1.7%)** | ranges apart |
| first person | `advance` | 0.570-0.587 (0.580) | 0.556-0.580 (0.567) | −0.013 | overlap |
| first person | `camera particles` (untouched) | 1.460-1.489 (1.471) | 1.481-1.527 (1.498) | +0.027 | overlap |
| empty | frame | 2.515-3.245 (2.714) | 2.529-2.584 (2.551) | — | overlap (run 14-A 3.245) |
| empty | `camera pose` | 0.465-0.501 (0.478) | 0.467-0.475 (0.472) | −0.006 | overlap |

The pose falls because the Paint and Attachments delegates run inside placement, which is in `camera pose`; each placed
item prop rebuilt its definition's attributes through the prefab chain twice a frame.

**Identity.** Paused shots (tick 30000, `pixdiff`, a C# port of `2026-10-27-empty-view-breakdown/pixdiff.ps1`'s rule):
first person A/B 0 pixels whole and world, A/A2 0. Empty view: world 0 in every pair; the whole frame differs by 46,816
pixels in the top bar (box (0,0)-(1279,48): the bar's dark background present or absent) in A/B **and** in A/A3 and
B/B2, while A/A2 and A/B2 are identical — a two-state top bar both builds show from run to run, not the change.
Mode-2 verifier on B: `queue verify: 821500 frames checked, 0 changed`, `QueuedThreaded on 6 physical cores`, exit 0.

## Verdict

**Confirmed.** The attribute sites' allocation is gone: −0.10 MB a frame in first person (main thread −105 KB, −14%,
ranges apart), −0.01 MB in the empty view, with the trace showing the sites absent and the untouched ones unchanged.
Identity held (world pixels, the verifier, and a top-bar difference both builds show). The frame is **claimed narrowly**
in first person: −0.095 ms (1.6%) with the ranges apart by only 0.005 ms, carried by `camera pose` −0.101 ms (ranges
clearly apart); not claimed in the empty view. Above the 0.01 ms bar, from removing allocation and a per-call prefab
walk, with the engine's own shape (definition attributes parsed once, item attributes streamed).
