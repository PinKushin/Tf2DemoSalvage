---
name: a-running-client-caches-its-config
description: TF2 serves a stale .cfg after an overwrite, so a fix can look like it failed; type the single cvar instead.
metadata:
  type: project
---

**Overwriting a `.cfg` while TF2 is running changes nothing until it is `exec`'d, and the first read
after an overwrite can still serve the old copy.** Measured 2026-08-16 while building `Pin-Config`'s
two profiles.

The damage is not the delay, it is the shape of the failure: a fix that was applied correctly appears
not to work. Two further theories were built on that false negative before the log falsified both.
**A false negative from a caching layer invalidates the experiment without invalidating anyone's
confidence in it**, which is why this is worth remembering rather than rediscovering.

**How to test a render setting instead: type the single cvar in the console.** It is read
immediately, and it is one variable rather than a file of them — a measurement rather than a change
of state. Restart the client only when a whole profile genuinely needs exercising.

Same family as [[real-data-hides-bugs-small-inputs-expose]] and the `-1` versus `-10` wrong turn in
`docs/findings/24-reference-capture.md`: a procedure chosen for convenience, insensitive to the thing
it was meant to detect.

Two more facts from the same session:

- **`mat_reducefillrate 1` crashes the modern client.** It selects the ps20b shader path, which has
  decayed to the point of requesting combos that no longer load. TF2's render settings are not a
  clean cheap-to-expensive spectrum: the bottom end is fatal and the expensive path is the one that
  works.
- **`Pin-Config` has two profiles and only `ultra` is a reference.** A capture taken under `low` is
  not ground truth for this project. See [[read-the-spec-before-measuring-our-data]] for why a
  capture with an unstated configuration is not evidence at all.
