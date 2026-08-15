---
name: lookups-must-match-exactly
description: A substring lookup on an asset name silently returns a longer name that embeds it — Find("Stand_PRIMARY") returned "AttackStand_PRIMARY" and laid every player down.
metadata:
  type: project
---

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
[[a-log-must-name-what-it-measured]] and [[instrument-bugs-outnumber-decoder-bugs]].

Posed Z spans for a scout, useful as a reference: reference pose 14, `AttackStand_PRIMARY` 23,
`stand_PRIMARY` 59, `run_PRIMARY` 68. Standing is ~60 and nothing else is.
