---
name: precache-what-the-engine-precaches
description: "A model reached only by a rare event is in no track and no schema, so it loads only if something precaches it — and the engine's precache list names exactly those; ours must match it."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T03:55:12.715Z
---

**When a feature draws nothing and every test is green, check what precaches its models first.** The
loader is a dictionary built up front, not on-demand — a path missing from the needed/pack sets packs
to nothing forever, silently (B195, and gibs hit it again).

**Why:** a model reached only by a rare event is in no prop track, string table, or item schema,
because it isn't an entity until the event happens. The engine solves this explicitly —
`CTFPlayer::PrecachePlayerModels` (`tf_player.cpp:2848`) precaches each class model and
`PrecacheGibsForModel`, which is `PrecachePropsForModel(iModel, "break")` (`props_shared.cpp:1239`),
walking the collision data's KeyValues for every `breakModel.modelName`. **The engine's precache list
is the answer to "what else must be loaded", already written down** — read it rather than deriving
one.

**How to apply:** for anything drawn by an EVENT rather than an entity, find the engine's Precache for
the thing that spawns it and add every named model to both load sets. Verify by LOOKING (same camera,
before/after) — this class of bug leaves the suite green.

**Companion mistake:** a supplier answering "what pieces does this model declare" read a cache of
models already DRAWN — a gibbed corpse draws no body, so it reported "none" for a model declaring
nine. The engine reads the `.phy` at PRECACHE time; so must we. A probe that should have caught it
compiled and agreed with itself against a stale signature ([[instrument-bugs-outnumber-decoder-bugs]]).

---

## `a-lookup-is-not-a-loader` — a geometry accessor answers from a dictionary built once

`MapAssets.Geometry(path)` looks like an on-demand loader; it's a `TryGetValue` over a dictionary
filled ONCE during load, from the demo's model list plus brush entities. Anything else asks and gets
null forever (B363: the map's detail models).

**Why it's silent:** the model packs to an empty entry, placement code is correct, every count reads
right — the only symptom is a downstream renderer message that names the model but not the cause.

**How to apply:** a new population of models must be added to the SAME list the loader reads, not
requested later. Two more traps behind it, both general: a failed load remembered permanently even
when a deliberate re-precache should retry it; an upload gated on the wrong collaborator's own `Add`
returning true, so a set grown elsewhere never reached the device.

See [[instrument-bugs-outnumber-decoder-bugs]], [[output-level-assertion-or-it-is-not-done]].

---

## `a-derived-path-is-in-no-load-list` — a model named by an item is named by no track

**Two lists decide whether anything draws, built from different things.** The asset loader reads
paths from tracks at map load; the renderer looks up geometry per frame from that dictionary. A path
never in the load list has no key, answers null, is remembered as empty — no warning, since the
loader's own "0 missing" is true about the list it was given.

**The trap: a path the client DERIVES** — an item can override a prop's model
(`CEconEntity::UpdateModelToClass`, `econ_entity.cpp:411`, lets the item win), so the resolved path is
named by the ITEM and appears on NO track, while the load list is built by walking tracks. The two
never meet — thirteen cosmetics a frame packed zero batches while the loader reported zero missing.

**How to apply:** whenever a model path can be produced by something other than the wire (item
schema, class script, gib list, fallback), ask whether the load-list walk can reach the same
producer. Build the list as a SUPERSET (every class/team combination, not just who wears what now).

**Diagnostic that names it in one run:** for every model with no geometry, report whether the load
list contained it AND whether any track names it — absent-plus-tracked means the walk is wrong;
absent-plus-untracked means the path is derived and the fix belongs where it's derived.

Related: [[the-client-builds-what-the-demo-omits]], [[the-denominator-decides-what-can-be-lost]],
[[measure-the-output-not-the-capability]].
