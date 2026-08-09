---
name: fallbacks-do-not-make-guesses-safe
description: Handling unknown values protects against unknown values, not against a wrong assumption about what a field means — an allowlist beats inference plus a fallback
metadata:
  type: project
---

Recorded 2026-08-09, after building event-to-player name resolution in the text dump.

**The design that felt safe.** Resolve every numeric event field to a player name, and if the id
is not in the roster, print the raw number instead. I wrote in the commit message that this
"falls back rather than guessing", and believed it.

**What real demos produced:**

```
damageamount=Ardaddy Ultrasex(14)      # 14 damage, colliding with user id 14
inflictor_entindex=sidewayssteven(7)   # an inflictor is usually a weapon entity
```

**Why the fallback never fired.** It guards the case where a value is *unknown*. Both of these
values were perfectly well known — 14 was a real user id, entity 7 was a real player. The
mistake was not in the lookup, it was in the premise that those fields referred to players at
all. A fallback cannot detect a wrong premise, because nothing about the data looks wrong.

## The rule

**A guess about what a field *means* cannot be made safe by handling unexpected values.** Those
are different failures. Unknown-value handling catches "I don't recognise this"; it is silent on
"I should never have looked this up".

So: enumerate what qualifies, rather than inferring it and catching the misses. The list of six
field names that genuinely carry a user id is boring, checkable, and cannot produce a wrong
name — the worst it does is leave a number unresolved, which is honest.

**The tell:** if the safety argument is "and if I'm wrong, it degrades gracefully", check
whether the wrong case actually *looks* wrong to the code. Here it did not, and the graceful
degradation path was never reached.

## The corpus caught it, and the fixtures could not

The fixture had one field with one value. A collision between a damage number and a user id
needs a real match, where dozens of numeric fields and a dozen user ids share a small integer
range. This is [[differential-beats-fixtures]] again in a different costume: a fixture tests the
mechanism, real data tests the premise.

See also [[ask-whether-the-data-arrived]] — same family of error, where a measurement of the
tool was mistaken for a fact about the world.
