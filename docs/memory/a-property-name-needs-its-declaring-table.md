---
name: a-property-name-needs-its-declaring-table
description: "A real property name in the wrong send table matches nothing; check the Table.Property pair against the SDK block, not the name alone."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:52:57.754Z
---

Entity properties are keyed `Table.Property` — a name real in the wrong table finds nothing, and
finding nothing is indistinguishable from an entity never sending it. Two properties were wrong this
way for the project's whole life, neither erroring:

- **`m_fFlags`** was looked for in `DT_LocalPlayerExclusive`; `player.cpp:8183` declares it in
  `DT_BasePlayer` (no exclusivity, `SPROP_CHANGES_OFTEN`). `Flags` was null for every player in every
  demo — nobody ever crouched or jumped in the viewer.
- **`m_flCycle`** was looked for in `DT_BaseAnimating`; `baseanimating.cpp:223` puts it in
  `DT_ServerAnimationData` ("fields we don't want to send to clientside animating entities"). Doors
  send a cycle; players never do (`CTFPlayer` calls `UseClientSideAnimation()`).

**A citation next to a guess looks identical to one next to a measurement** — the comment cited the
right line while stating the wrong table ([[measure-the-output-not-the-capability]]).

**The old conformance test couldn't catch it by construction** — checked each name against the union
of every SDK `SENDINFO`, using the table only in the error message. `SendTableConformanceTests` now
parses each `BEGIN_SEND_TABLE`...`END_SEND_TABLE()` block and checks the pair; found the `m_flCycle`
mismatch on its first run.

**The scan failed its own control first**: `SourceSdk.Files` is non-recursive by default and returns
absolute paths while `SourceSdk.Text` wants relative — both gave an empty sweep reading as "everything
conforms". Any SDK-crawling test needs a positive control ([[instrument-bugs-outnumber-decoder-bugs]]).

**Third instance (2026-08-20) denied the property existed at all.** `EntityState` claimed "a
viewmodel inherits no `DT_BaseEntity` — no origin, no angles, no `m_fEffects`". First two right;
`DT_BaseViewModel` declares its own `m_fEffects` (`baseviewmodel_shared.cpp:565`).
**`BEGIN_NETWORK_TABLE_NOBASE` stops a table INHERITING a property, not declaring one.** `IsDrawn`
looked in `DT_BaseEntity`, got null → "no flags" → draw it: the spy's `EF_NODRAW`-hidden watch would
have been drawn in every player's hand for a whole match. The reader now resolves effects from
whichever table declares them, in one place.

Related: [[wire-names-are-strings]], [[a-player-has-two-viewmodels]].
