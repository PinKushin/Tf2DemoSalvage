---
name: fallbacks-do-not-make-guesses-safe
description: "Handling unknown values protects against unknown values, not against a wrong assumption about what a field means — an allowlist beats inference plus a fallback"
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:54:17.950Z
---

Building event-to-player name resolution: resolve every numeric event field to a player name, and if
unrecognized, print the raw number — "falls back rather than guessing", believed safe.

**What real demos produced:** `damageamount=Ardaddy Ultrasex(14)` — 14 was a real user id that
happened to be the damage amount; `inflictor_entindex=sidewayssteven(7)` — entity 7 was a real player,
not the actual inflictor. The fallback only guards *unknown* values; both were perfectly well-known —
the mistake was the premise that those fields referred to players at all. A fallback can't detect a
wrong premise, since nothing about the data looks wrong.

**The rule:** a guess about what a field MEANS can't be made safe by handling unexpected values —
different failures. Enumerate what qualifies (six field names genuinely carrying a user id) rather
than infer and catch misses; the worst an allowlist does is leave a number unresolved.

**Tell:** if the safety argument is "and if I'm wrong it degrades gracefully", check whether the wrong
case actually looks wrong to the code — here it didn't.

**The corpus caught it, fixtures couldn't** — needs a real match with dozens of numeric fields and
user ids sharing a small integer range. Same family as [[fixtures-are-the-weak-point]]: a fixture
tests the mechanism, real data tests the premise. See also [[ask-whether-the-data-arrived]].
