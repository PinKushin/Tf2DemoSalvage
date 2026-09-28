---
name: key-a-lookup-on-the-question
description: "Deriving every case from case zero makes case zero load-bearing; key on the input the engine keys on, not on one case's answer."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:42:10.861Z
---

Storing every case as a diff from resolved case zero is tempting (halves storage) and makes case zero
load-bearing for every case, failing two ways:

**B229:** a mesh's skin family zero resolved to −1 (unresolvable) while family 1 was fine
(`cp_fulgur` places a model at skins 1 and 12 of 15, packing only those textures) — 19,274 triangles
drew in the missing-material chequer on a map the game renders perfectly. **The derived key need not
be unique either** — two meshes sharing texture X at family zero but differing above it collide
silently.

**Why it survives testing:** the degenerate case (one skin family) is overwhelmingly common, and the
table is the identity there.

**How to apply:** key the lookup on the same input the engine keys on; build the table for EVERY
case including zero. Ask "is case zero privileged?" — if only because it's always present, it isn't
privileged, it's assumed. Related: [[conformance-test-before-implementation]],
[[a-constant-carries-no-scope]], [[valve-parity-is-the-first-principle]].

---

## `a-key-format-is-two-facts` — one wrong kills it, and `??` hides that it did

**A string-built lookup key encodes several independent facts, each of which can be wrong alone.**
`m_iTeam.003` is: the array name, indexed by ENTITY INDEX (not player slot), zero-padded to three
digits. B313: a recorder line got two of three wrong, building `"m_iTeam.0"`, matching nothing for
the life of the code — masked by `resource?.Integer(key) ?? OwnProperty()`, a dead lookup composed
with a working fallback.

**Confirm a key format against the DATA**: `grep -o "m_iTeam\.[0-9]*" dump.txt | sort -u`. Then
measure whether fixing it changes anything — here it didn't (the fallback happened to agree), so
report it as a latent defect, not a fixed bug.

---

## `lookups-must-match-exactly` — a `Contains` match laid every player down

**Look up an asset by exact name — `Contains` returns the first LONGER name embedding the one asked
for, and looks like a working lookup.** `PropModels.SkinnedModel.Find` used `Contains`, so
`Stand_PRIMARY` matched `AttackStand_PRIMARY` (an upper-body layer meant to ADD to a base pose)
instead of the real `stand_PRIMARY`. Played alone, it left every player near their reference pose —
lying on its back. Four wrong diagnoses were filed first (up-axis, transposition, bone composition,
blend grid) because a real animation WAS being applied, just the wrong one.

**How to apply:** match exactly, like Valve's `Studio_LookupSequence` (`stricmp`). Print what a
suspected lookup RETURNED next to what was asked — settled this after hours downstream. Related:
[[logs-are-the-debugger]], [[instrument-bugs-outnumber-decoder-bugs]].

---

## `a-vector-keys-its-halves-differently` — elements key FLAT, the length keys by PATH

`EntityStateTable` keys a decoded property by path only when element-scoped (a datatable member
named `lengthproxy` or all digits); a plain property never sets it. So one vector is keyed two ways:
sub-table elements as a path (`…m_AnimOverlay.000.m_nSequence`), plain-EHANDLE elements flat
(`_ST_m_hActorList_16.000`), length via `lengthproxy`.

**How to apply:** don't copy a leading-dot match from one vector to another — it won't match the flat
spelling. Match the member name without a leading dot; treat any key containing `lengthprop` as the
count; take the index from the tail after the last `.`. Before believing an empty vector, decompile
the demo and grep for the property name — the trace prints the flat spelling the reader must accept.
1,824 scene playbacks reporting 0 actors was this, not a TF2 fact.

Related: [[instrument-bugs-outnumber-decoder-bugs]]#an-empty-search-needs-a-control, [[wire-names-are-strings]].
