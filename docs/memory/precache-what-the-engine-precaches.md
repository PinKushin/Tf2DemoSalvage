---
name: precache-what-the-engine-precaches
description: "A model reached only by a rare event is in no track and no schema, so it loads only if something precaches it — and the engine's precache list names exactly those; ours must match it."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T03:55:12.715Z
---

**When a feature draws nothing and every test is green, check what precaches its models before
checking the feature.** The loader here is a dictionary built up front, not an on-demand read: a
path missing from `DemoModels.Needed` (what the asset loader decodes) or `DemoModels.ToPack` (what
goes into the vertex buffer) packs to nothing for ever, silently. That is B195, and gibs hit it
again on 2026-09-08 — `RagdollProps` emitted the pieces correctly and the screen stayed empty.

**Why:** a model reached only by a rare event is in **no `ScenePropTrack`, no string table and no
item schema**, because it is not an entity until the event happens. Nothing in the ordinary
path-collection can discover it. The engine has the same problem and solves it explicitly:
`CTFPlayer::PrecachePlayerModels` (`tf_player.cpp:2848`) precaches each class model **and**
`PrecacheGibsForModel( iModel )`, which is `PrecachePropsForModel( iModel, "break" )`
(`props_shared.cpp:1239`) — it walks the collide data's key values and precaches every
`breakModel.modelName` the `.phy` declares. **So the engine's precache list is the answer to "what
else must be loaded", already written down.** Read it rather than deriving one.

**How to apply:** when adding anything drawn by an event rather than by an entity — gibs, a
breakable's pieces, a spawned effect's model — find the engine's `Precache` for the thing that
spawns it and add every model it names to BOTH sets. Then verify by LOOKING, with the same camera
and tick before and after: this class of bug leaves the suite green, so a screenshot is the only
instrument that fails.

**The companion mistake, same session:** the supplier that answers "what pieces does this model
declare" was reading a cache of models already DRAWN, and a gibbed corpse draws no body — so it
reported "none" for a model declaring nine. See the `a-lookup-is-not-a-loader` section below. The
engine reads the `.phy` at precache time; so do we now. And the probe that should have caught it
called `Fill` without the new optional parameter, compiled, and agreed with itself —
[[instrument-bugs-outnumber-decoder-bugs]].

---

## `a-lookup-is-not-a-loader` — `MapAssets.Geometry` answers from a dictionary built once

`MapAssets.Geometry(path)` looks like an on-demand model loader and is a `TryGetValue` over a
dictionary filled once, during `MapAssets.Load`, from the demo's model list plus the map's brush
entities. Anything else asks and gets null for ever (B363: the map's detail models).

**Why it matters:** the failure is silent and one layer away from where it looks. The model packs to
an empty entry, the placement code is correct, every count reads right, and the only symptom is the
renderer's *"was posed before its geometry was uploaded"* — which names the model but not the cause.

**How to apply:** a new population of models must be added to the list `MapAssets.Load` reads, next
to the brush entities, not merely requested later. Two more traps sit behind it, both now fixed and
both general: `EntityModelSet.Add` remembers a failed load as an empty entry permanently (right per
frame, wrong for a deliberate `Precache`, which now retries empty entries), and `MomentScene` used
to upload only when its OWN `Add` returned true, so a set grown anywhere else never reached the
device — `EntityModelSet.Grown` now says so however it grew.

See [[instrument-bugs-outnumber-decoder-bugs]] and
[[output-level-assertion-or-it-is-not-done]].

---

## `a-derived-path-is-in-no-load-list` — a model named by an item is named by no track

**Two lists decide whether anything is drawn, and they are built from different things.** The asset
loader reads a list of paths at map load; the renderer asks `MapAssets.Geometry(path)` per frame, which
is a **dictionary lookup, not a loader** (the section above). A path that was never in the load list
has no key, answers null, is remembered as empty, and draws nothing — with no warning, because the
loader's own `MISSING 0` is a true statement about the list it was given.

**The trap is a path the client DERIVES.** `WeaponPropModels.Resolve` replaces a prop's model with
`GetPlayerDisplayModel( iClass, team )` for every prop carrying an item index, because
`CEconEntity::UpdateModelToClass` (`econ_entity.cpp:411`) lets the item win over the networked model. So
the path that reaches the renderer is named by the ITEM and appears on **no track** — while
`DemoModels.Needed` builds the load list by walking tracks. The two can never meet.

Measured on `20130518_0313_cp_granary_blu_blu`: the load reported `ASKED FOR 166; HAVE 166; MISSING 0`
while thirteen cosmetics a frame packed zero batches. 34 of the 66 no-geometry models were absent from
the list and **named by no track** — that last clause is the whole diagnosis, and it took one line in a
census to get.

**How to apply.** Whenever a model path can be produced by something other than the wire — an item
schema, a class script, a gib list, an `attached_models` entry, a fallback — ask whether the walk that
builds the load list can reach the same producer. If the answer comes from a resolver, the load list has
to call that resolver too, not a narrower one. And build the list as a **superset**: the wearer's class
and team are per-tick facts, so ask across every class and both teams rather than for who happens to
wear what now.

**The diagnostic that names it in one run**: for every model that produced no geometry, report whether
the load list contained it AND whether any track names it. Absent-from-list plus named-by-a-track means
the walk is wrong; absent plus named-by-nothing means the path is derived and the fix belongs where it
is derived.

Related: [[the-client-builds-what-the-demo-omits]],
[[the-denominator-decides-what-can-be-lost]], [[measure-the-output-not-the-capability]].
