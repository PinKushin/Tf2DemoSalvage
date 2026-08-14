---
name: sentinels-conflate-unknown-with-answer
description: A sentinel like -1 that means both "not known yet" and a real answer produces plausible wrong output twice over.
metadata:
  type: feedback
---

A sentinel value that carries more than one meaning will be read as the wrong one. Twice in one
day, both times as `-1` for a sequence number:

- **Health packs drew static.** `m_nSequence` was absent from the wire, decoded as `-1`, and `-1`
  was treated as "this entity has no animation". Absent actually means the property never changed
  from its default, which is **sequence 0** — so every pickup in the corpus sat on frame zero while
  animating perfectly well in game.
- **Players drew in the reference pose.** The sequence chooser returned `-1` because the model had
  not been loaded when it was asked. `-1` already meant "this model has no such sequence", so the
  ordering bug was indistinguishable from a lookup that ran and found nothing.

**Why:** the owner named the second as "the same error you had before with the health packs", which
is the right generalisation — not two bugs about animation, one bug about a value with two jobs.

**How to apply:** when a value can be *absent*, *not yet computed*, or *a real negative answer*,
those are three states and want three representations — `null` for unknown, a real value for known,
and an explicit default where the format defines one. In particular: **on the wire, absent means
the default, not unknown.** A delta-compressed format only sends what changed, so a property
missing from every packet is a property that was never anything but its default. Never read absence
as "the feature is off".

And when a lookup can be asked too early, make that a different answer from "found nothing" —
or make it impossible by ordering, which is what fixed the player case.

Related: [[ask-whether-the-data-arrived]] is the same question one layer down, and
[[fallbacks-do-not-make-guesses-safe]] is what happens when the ambiguity is papered over instead.
