---
name: key-a-lookup-on-the-question
description: "Deriving every case from case zero makes case zero load-bearing; key on the input the engine keys on, not on one case's answer."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:42:10.861Z
---

When a value varies by some selector — a skin family, a team, a language, a quality level — there is
a tempting shortcut: resolve **case zero**, then express every other case as a *diff from* that
resolved answer. It halves the storage and it reads as an optimisation.

It makes case zero load-bearing for every case, and it fails in two ways that look nothing alike.

**Measured, 2026-08-29 (B229).** A `.mdl` mesh's `material` field is a **skinref**, and
`g_skinref[skin][skinref]` returns the texture index — Valve's own comment, at
`utils/motionmapper/motionmapper.h:134`. This project resolved family zero for each mesh and stored
every other family as a swap FROM that resolved material index.

1. **Case zero can be unresolvable while the case you want is fine.** `cp_fulgur` places
   `props_aquatic/pipe_256.mdl` at skins 1 and 12 of 15 and packs exactly those two textures — not
   family zero's. Family zero resolved to −1, a swap keyed on −1 was refused, and 19,274 triangles
   drew in the missing-material chequer on a map the game renders perfectly.
2. **The derived key need not be unique.** "What does texture X become at skin 1" has *two* answers
   the moment two meshes share texture X at family zero and differ above it. That fault was in the
   code the whole time with no symptom, and its symptom would have been a mesh wearing a
   neighbour's texture — plausible, and far harder to spot than magenta.

**Why it survives testing:** the degenerate case is overwhelmingly common. Almost every model has
one skin family, where the table is the identity and every reading agrees with every other. The
control map had zero failures before and after the fix. See [[most-of-a-decoder-is-untested]].

**How to apply:** key the lookup on the same input the engine keys on, and build the table for
**every** case including zero, so nothing has a special case. Ask "is case zero privileged here?" —
if the answer is only "it is the one that is always present", it is not privileged, it is assumed.
Related: [[conformance-test-before-implementation]], [[a-constant-carries-no-scope]],
[[valve-parity-is-the-first-principle]].

---

## `a-key-format-is-two-facts` — one wrong kills it, and `??` hides that it did

**A string-built lookup key encodes several independent facts, and every one of them can be wrong on
its own.** `m_iTeam.003` is three: the array's name, that it is indexed by ENTITY INDEX rather than
by player slot, and that the number is zero-padded to three digits.

B313, 2026-09-04: the player loop got all three right. The recorder's line, sixty lines away in the
same file, got two of them wrong — it passed the slot and did not pad — so it built `"m_iTeam.0"`,
which matches nothing, **every time, for the life of the code**.

**A `??` fallback is what made it survive.** The line read `resource?.Integer(key) ?? OwnProperty()`,
so a dead lookup and a working fallback compose into something that always answers. The code
described a preference it never expressed
([[a-fallback-that-makes-sound-hides-itself]]).

**Confirm a key format against the DATA, not the code that writes it.** One grep settles it:

```bash
grep -o "m_iTeam\.[0-9]*" dump.txt | sort -u | head
```

**Then ask whether fixing it changes anything, and measure that too.** Here it did not — the
fallback happened to give the same answer on this corpus — so it is a latent defect, and saying
"latent" rather than "fixed a bug" is the honest report.

**Two call sites building one key is the smell.** Extract the key, and the two cannot disagree; leave
them apart and only one of them is ever exercised by the case that would notice.

---

## `lookups-must-match-exactly` — a `Contains` match laid every player down

**Look an asset up by exact name. A `Contains` match returns the first LONGER name that embeds the
one you asked for, and it looks like a working lookup.**

**Why:** `PropModels.SkinnedModel.Find` used `Contains`, so asking a scout for `Stand_PRIMARY`
returned sequence 9, `AttackStand_PRIMARY`, while the real `stand_PRIMARY` sat at 175 and was never
reached. A TF2 attack sequence is an upper-body layer meant to be ADDED to a base pose; played alone
as an absolute pose it leaves the skeleton near its reference — and a TF2 player's reference pose is
authored lying on its back.

Every player in the viewer lay down. Worn items sat at ankle height because `bip_head` was down
there with them. The legs looked broken. Four confident diagnoses were filed and retracted first: an
up-axis conversion, an axis transposition in the readers, a bone composition worth rewriting
wholesale, and the blend grid. All four were wrong.

The evidence looked self-contradictory precisely BECAUSE a real animation was being applied — the
posed shape differed from the rest shape, which reads as "the pipeline works", while the model never
stood up.

**How to apply:** Valve's own `Studio_LookupSequence` compares with `stricmp`. Match exactly. And
when a lookup is suspected, print what it RETURNED next to what was asked for — that one line
settled this after hours of measuring things downstream of it. Related:
[[logs-are-the-debugger]] and [[instrument-bugs-outnumber-decoder-bugs]].

Posed Z spans for a scout, useful as a reference: reference pose 14, `AttackStand_PRIMARY` 23,
`stand_PRIMARY` 59, `run_PRIMARY` 68. Standing is ~60 and nothing else is.
