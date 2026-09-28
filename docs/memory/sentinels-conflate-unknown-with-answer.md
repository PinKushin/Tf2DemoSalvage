---
name: sentinels-conflate-unknown-with-answer
description: A sentinel like -1 that means both "not known yet" and a real answer produces plausible wrong output twice over.
metadata:
  type: feedback
---

A sentinel carrying more than one meaning gets read as the wrong one. Twice in one day, both `-1` for
a sequence number: health packs drew static because absent-from-wire decoded as `-1`, read as "no
animation" — absent actually means the property never changed from default, sequence 0; players drew
in reference pose because a lookup asked too early returned `-1`, already meaning "no such sequence",
indistinguishable from a genuine miss.

**Why:** owner named the second "the same error as the health packs" — one bug about a value with two
jobs, not two animation bugs.

**How to apply:** when a value can be absent, not-yet-computed, or a real negative answer, those are
THREE states wanting three representations — `null` for unknown, a real value for known, an explicit
default where the format defines one. **On the wire, absent means the default, not unknown** — a
delta-compressed format only sends what changed. When a lookup can be asked too early, make that a
different answer from "found nothing", or make it impossible by ordering.

Related: [[ask-whether-the-data-arrived]], [[fallbacks-do-not-make-guesses-safe]].
