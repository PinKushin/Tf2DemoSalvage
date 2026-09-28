---
name: a-property-can-be-declared-by-any-table
description: "A class may declare m_vecOrigin in its own table instead of inheriting DT_BaseEntity's; a reader keyed on a fixed list of tables silently drops every class that does."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T04:24:18.135Z
---

**Never key an entity-state accessor on a fixed list of TABLES.** The engine binds a recv proxy by
the property's NAME wherever declared — `RECVINFO_NAME( m_vecNetworkOrigin, m_vecOrigin )` inside
`BEGIN_NETWORK_TABLE( CTFBaseRocket, DT_TFBaseRocket )` (`tf_weaponbase_rocket.cpp:43`) produces a
property keyed `DT_TFBaseRocket.m_vecOrigin`, stored as origin just like `DT_BaseEntity`'s.

**Why:** `EntityState.Origin()` only searched `DT_TFLocalPlayerExclusive`,
`DT_TFNonLocalPlayerExclusive`, `DT_BaseEntity`. Every TF2 projectile declares its own origin, so all
answered null — `DemoTimeline` returns before creating a track with no origin, and **1,608 projectile
tracks in one demo existed as decoded entities and reached nothing.** The decoder saw 3,846 rocket +
10,066 pipebomb updates in `z1800` while the viewer drew none (B372).

**How to apply:** when an accessor finds nothing, ask whether the class declares the property itself
before concluding the demo lacks it. Try named tables first where a real priority exists (a player's
local/non-local pair), then fall back to any table declaring the name, newest write wins. Test both
halves — fallback and fixed list agree when only one table is present, so the priority itself needs a
test with both present.

**The tell:** a whole CLASS of entity missing rather than a few — a fixed table list fails uniformly.

See [[wire-names-are-strings]] for the naming half, and [[a-property-name-needs-its-declaring-table]]
for its converse.
