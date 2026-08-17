---
name: a-property-name-needs-its-declaring-table
description: A real property name in the wrong send table matches nothing; check the Table.Property pair against the SDK block, not the name alone.
metadata:
  type: project
---

Entity properties are keyed `Table.Property`, so **a name that is real in the wrong table finds
nothing** — and finding nothing is indistinguishable from an entity that never sent it. Two
properties were wrong this way for the whole life of the project and neither produced an error:

- **`m_fFlags`** was looked for in `DT_LocalPlayerExclusive`; `player.cpp:8183` declares it in
  `DT_BasePlayer` with no exclusivity and `SPROP_CHANGES_OFTEN`. `Flags` was null for every player
  in every demo, so **nobody ever crouched or jumped** in the viewer.
- **`m_flCycle`** was looked for in `DT_BaseAnimating`; `baseanimating.cpp:223` puts it in
  `DT_ServerAnimationData`, under the comment *"Sendtable for fields we don't want to send to
  clientside animating entities"*. Doors send a cycle; players never do, because `CTFPlayer` calls
  `UseClientSideAnimation()`.

**The comment cited the right line while stating the wrong table**, and added a consequence nobody
measured ("for the recorder alone in a POV one"). A citation next to a guess is typographically
identical to a citation next to a measurement — see [[measure-the-output-not-the-capability]].

**The old conformance test could not catch it by construction.** It checks each name against the
union of every `SENDINFO` in the SDK — correct for this project, which decodes generically — but it
used the table only in the error message. `SendTableConformanceTests` now parses each
`IMPLEMENT_SERVERCLASS_ST` / `BEGIN_SEND_TABLE` block to `END_SEND_TABLE()` and checks the **pair**.
It found the `m_flCycle` mismatch on its first run, which nobody suspected.

**The scan failed its own control first**, and for two independent reasons at once:
`SourceSdk.Files` is non-recursive by default (`src/game` has no `.cpp` at its top level) **and**
returns absolute paths while `SourceSdk.Text` takes a path relative to the checkout. Both give an
empty sweep that reads as "everything conforms". Any SDK-crawling test needs a positive control
asserting it found a known pair — [[an-empty-search-needs-a-control]].

Related: [[wire-names-are-strings]] for the other half of this, where `SENDINFO_NAME` sends under its
second argument so the member name never appears on the wire.
