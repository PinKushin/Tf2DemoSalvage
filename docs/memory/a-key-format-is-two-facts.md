---
name: a-key-format-is-two-facts
description: "A string-keyed lookup encodes several facts; one wrong kills it, and a fallback hides it."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-04T08:32:24.089Z
---

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
