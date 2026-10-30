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

(to be filled at the run)

## Result

(pending)

## Verdict

(pending)
