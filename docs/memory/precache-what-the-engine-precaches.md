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
reported "none" for a model declaring nine. See [[a-lookup-is-not-a-loader]]. The engine reads the
`.phy` at precache time; so do we now. And the probe that should have caught it called `Fill`
without the new optional parameter, compiled, and agreed with itself — [[instrument-bugs-outnumber-decoder-bugs]].
