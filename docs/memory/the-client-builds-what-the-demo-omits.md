---
name: the-client-builds-what-the-demo-omits
description: The first-person weapon model is a client-side entity, so no demo contains it; the item definition index plus items_game.txt is the bridge.
metadata:
  type: project
---

**A demo does not contain everything on screen.** The first-person weapon model is a client-created
entity, no edict, no entity index, nothing networked — bone-merged onto the arms by the client at draw
time. Searching a demo for it correctly finds nothing.

**What the demo carries is enough to rebuild it:** an item definition index resolves through
`items_game.txt`'s prefab chain to a model. Twenty-two of fifty-six held weapons on one demo send no
index at all — the fallback is the stock item for the weapon's class. The two rules together resolved
56 of 56.

**Why it matters beyond weapons:** the same shape covers the HUD, tracers, muzzle flashes, any
client-only effect. When something visibly in-game is absent from the demo, the question is "does the
client make this itself", not "which field did we miss" — the demo carries the INPUT to that
construction, not its result.

**How to apply:** before hunting a field, check whether the thing is created client-side (grep for the
class in `client/` with no `server/` match, or for the client-entity constructor). Find what the
client reads to build it, read the same thing — shipped data files usually hold it
([[nothing-is-closed]]); an absent networked value normally means the default, not "unknown"
([[sentinels-conflate-unknown-with-answer]]).
