---
name: author-the-specimen-the-corpus-lacks
description: "The writer is a test instrument, not just a product feature — a case no demo contains can be authored rather than hunted for; covers what makes a field truly untestable on ordinary play (a default that is the operation's identity), why a feature scoring 159 of 159 on one demo needs a second era before it counts as done, the standing rule to check backwards compatibility the moment an old demo is in play, and why a POV recording's missing props are the PVS rather than a decode gap."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:53:07.420Z
---

**When the corpus lacks a case, write a demo that does have it.** This project can emit `.dem` files
the engine accepts ([[engine-accepts-authored-demos]]), making the writer a *testing* capability, not
just a product one.

2026-08-19: a test was skipped for lacking a time-driven proxy material, and the response was to hunt
for a map with one rather than author the input — owner pointed it out, said they won't always
remember to. Written down so it's not re-derived.

Where it applies: era gaps (protocols 12–13, 17–23 have no specimen, D5); decoder branches real demos
never take ([[most-of-a-decoder-is-untested]]); edge/malformed values with a known intended meaning;
anything that would otherwise be an eternal skip.

**Two distinctions, not the same:** *cutting up* an existing demo was called "a little cheaty" and its
code deleted same day. *Authoring* a specimen was endorsed outright — trimming vs. constructing an
input you chose and can predict.

**Check whether a demo is needed at all first** — the 2026-08-19 case didn't (`MapAssets.Load` takes
model list as a parameter directly). Reach for the writer only when the thing under test is the demo
stream itself.

Related: [[round-trip-needs-the-encoding-shape]], [[fixtures-are-the-weak-point]].

---

## `a-default-valued-field-is-untestable-on-the-corpus`

**A field whose default makes it a no-op cannot be tested on ordinary play, ever** — not "no demo
yet" but impossible in principle, since correct and missing implementations produce identical output
at that value. `m_flHeadScale`/`m_flTorsoScale`/`m_flHandScale` (B312) all default to 1 and every
corpus recording reports 1 (440/440 on `z1800`) — every comparison agreed while nothing read the
fields. Same shape as `m_flPlaybackRate` (every animation played at rate 1).

**Tell: a default that is the IDENTITY of the operation it feeds** (1 for a multiplier, 0 for an
offset, empty for a concatenated list). If the engine's own initialiser sets that value, no
measurement of ordinary content can find the gap — author the specimen instead
(`SyntheticPlayer.Demo`, a property dictionary through the real container/schema). Use values distinct
from each other AND the default; test the default's own claim separately with a null-input control.

## `measure-a-new-feature-on-a-second-demo`

**Before calling a decode feature done, run its probe on a demo from another era.** B319: a corpse's
orientation reached through the player scored 159/159 on a 2026 SourceTV demo, 0/407 on `z1800` — the
two demos name the field differently (`m_hPlayer`, packed ehandles, needing Resolve, vs.
`m_iPlayerIndex`, entity indices, used as-is). **`m_iPlayerIndex` is not in the published SDK at
all** — only a demo carries it ([[the-demo-dates-its-own-fields]], [[wire-names-are-strings]]).

**Tell of under-measurement: a perfect score on one file.** Prefer a committed era specimen over
another modern demo — a second 2026 match would score 159/159 again and teach nothing.

Related: [[era-axis-is-measured]], [[instrument-bugs-outnumber-decoder-bugs]], [[record-both-points-of-view]].

## `check-backwards-compat-on-old-demos`

Owner, after the doubled-viewmodel bug: *"you know the demos have to be backwards compat to 07... we
should probably check the 07 demo after this... we dont ui test every demo we have, and i dont look
at every one before we commit."* The bug was a modern assumption (viewmodel = hands + separate gun)
applied to a 2011 recording where it was one combined `v_` model.

**How to apply:** when a change touches drawing or resolution, open the oldest supported demo. Era
axis: protocols 11, 14, 15, 16, 24 with matched POV/STV pairs. Note: period clients have **no
internet**, so a modern item can't be loaded in them for comparison — answers must come from shipped
data and the SDK instead. Related: [[a-player-has-two-viewmodels]], [[era-axis-is-measured]],
[[record-both-points-of-view]], [[the-demo-dates-its-own-fields]].

## `pov-demos-are-pvs-limited`

A POV `.dem` is one client's **received** packet stream — the server transmits an entity only when it
passes the PVS check, so a POV recording physically cannot contain entities the recorder couldn't
see. Measured 2026-08-16, badlands POV vs. process STV, same viewer build: studio props peaked at 16
vs. 94; `cap_point_base` never above 2 vs. 5 per frame — though the badlands timeline holds 5 cap
points, 20 ammopacks, 14 medkits over the WHOLE recording, just never at once.

Valve's side: `FL_EDICT_PVSCHECK` is the default transmit state (`baseentity.cpp:4025,4096`) —
entities opt OUT of PVS, not in.

**Why worth a memory:** imitates a regression perfectly ("all the props went away") and consumed a
session bisecting skin retention, track identity, draw-loop counters — all healthy. The question that
would have ended it: *which demo* — every earlier screenshot was SourceTV.

**So verify rendering on an STV demo**; use POV only when the point is the recorder's own view.
[[record-both-points-of-view]] is the same distinction from the writer's side. Related:
[[instrument-bugs-outnumber-decoder-bugs]].
