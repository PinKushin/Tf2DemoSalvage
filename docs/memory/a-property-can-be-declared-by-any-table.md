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
the property's NAME, wherever it is declared — `RECVINFO_NAME( m_vecNetworkOrigin, m_vecOrigin )`
inside `BEGIN_NETWORK_TABLE( CTFBaseRocket, DT_TFBaseRocket )` (`tf_weaponbase_rocket.cpp:43`)
produces a property keyed `DT_TFBaseRocket.m_vecOrigin`, and the client stores it as the entity's
origin exactly as it would `DT_BaseEntity`'s.

**Why:** `EntityState.Origin()` searched `DT_TFLocalPlayerExclusive`,
`DT_TFNonLocalPlayerExclusive` and `DT_BaseEntity`. Every projectile in TF2 declares its own origin,
so every one answered null — `DemoTimeline` returns before creating a track when there is no origin,
and **1,608 projectile tracks in one demo existed as decoded entities and reached nothing**. The
decoder saw 3,846 rocket and 10,066 pipebomb updates in `z1800` while the viewer drew none (B372).

**How to apply:** when an accessor cannot find a property, ask whether the class declares it
ITSELF before concluding the demo does not carry it. Try the named tables first where a genuine
priority exists — a player's local/non-local pair is one — then fall back to any table declaring the
name, newest write winning. And test both halves: a fallback and a fixed list agree on every input
that supplies only one table, so the priority needs its own test with both present or it is
untested.

**The tell** is a whole CLASS of entity missing rather than a few: a fixed table list fails
uniformly, so the symptom is "we draw no projectiles at all", not "some projectiles are wrong".

See [[wire-names-are-strings]] for the naming half, and
[[a-property-name-needs-its-declaring-table]] for its converse — the pair matters when two tables
carry the same name, which is exactly why the named tables are still tried first.
