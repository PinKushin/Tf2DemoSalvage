---
name: write-can-destroy-what-you-did-not-read
description: "Writing a \"new\" file over an existing one deleted a complete implementation and ten tests; the tell is \"updated\" rather than \"created\", and the count floor is what caught it."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 71bbc8e9-0f7a-489c-a987-3e0867aae1fa
  modified: 2026-09-10T22:52:49.278Z
---

**Check whether a file exists before writing it, especially when sure it does not.** Writing a
"new" implementation and its tests, believing the feature didn't exist, silently replaced a finished,
better-researched version and ten tests with a thinner, partly WRONG one. The original resolved
`dcubemapsample_t.size` as a CODE — 0 means the default 32, and passing 0 through `1 << (size - 1)`
gives `1 << 31` in C# because the shift count is masked — cited vbsp's actual format string from
`cubemap.cpp:511`. The replacement derived the path from filenames and got it wrong.

**The tell was in the tool's own reply, read past:** "has been updated successfully" rather than
"created". A create and an overwrite don't say the same thing.

**What caught it: the test-count FLOOR**, while ADDING tests — the dangerous moment is reasoning "I
added four, so the count moved for my reasons." A floor that drops while you're adding is never
explained by your additions.

**How to apply:**
- Before `Write`, establish whether the path exists — `Read` it or list the directory.
- Treat "updated" in a write result as a stop signal unless overwriting was intended.
- Search for the feature before building it — grep first, costs seconds. `BspCubemaps` was already
  complete, so the whole premise (that B55 was blocked on reading the lump) was false; the missing
  half was the renderer.
- A count moving the wrong way is a finding, never an accounting nuisance.

**It happened again twice** (once colliding with `ViewmodelAttachmentTests` for B252's display-flag
mask, an unrelated subject), and the second time the count went UP (more tests added than deleted),
so every floor passed comfortably — the gate would NOT have caught it. **What caught it was DELTA
arithmetic**: predicted count (prior + N added) didn't match actual, revealing deletions hiding under
an addition. Then `git status` showed `M`, not `??`, on a file believed newly created.

**Added rule:** predict the count before running, subtract — an increase smaller than N added is a
deletion hiding under an addition, invisible to any floor. Check `git status` on the test directory
before committing — `M` on a file believed created is the whole finding in one command. **Being
mid-flow (fixing something else) is when it happens** — the path felt "already established" earlier
in the session.

See [[read-the-trx-total-not-the-console]].
