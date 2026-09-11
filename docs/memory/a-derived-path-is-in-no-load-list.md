---
name: a-derived-path-is-in-no-load-list
description: "A model resolved at draw time is named by no track, so a load list built from tracks can never contain it — and the miss is silent."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-10T14:59:44.920Z
---

**Two lists decide whether anything is drawn, and they are built from different things.** The asset
loader reads a list of paths at map load; the renderer asks `MapAssets.Geometry(path)` per frame, which
is a **dictionary lookup, not a loader** ([[a-lookup-is-not-a-loader]]). A path that was never in the
load list has no key, answers null, is remembered as empty, and draws nothing — with no warning, because
the loader's own `MISSING 0` is a true statement about the list it was given.

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

Related: [[a-lookup-is-not-a-loader]], [[the-client-builds-what-the-demo-omits]],
[[precache-what-the-engine-precaches]], [[the-denominator-decides-what-can-be-lost]],
[[measure-the-output-not-the-capability]].
