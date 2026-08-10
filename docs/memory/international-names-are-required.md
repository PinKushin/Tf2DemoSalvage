---
name: international-names-are-required
description: "Every string decoder must be UTF-8 — TF2 names carry Cyrillic, CJK and accents routinely, and ASCII corrupts them into plausible-looking output"
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-10T11:18:34.694Z
---

Owner requirement, stated 2026-08-10: **"international language does need to be accepted in this
parser, tf2 names can get weird."** Not an edge case to handle defensively — an ordinary input.

**How it was found, which is the reusable part.** A demo recorded that day on a 2013 client by a
player named `miałker` printed the same player twice in one dump:

```
Client             mia??ker      <- demo header, read as ASCII
     0       2  miałker          <- userinfo string table, read as UTF-8
```

`ł` is two bytes in UTF-8, so ASCII gave exactly two question marks. **Nothing failed.** Both
fields held a plausible name. The bug was visible only because two decoders disagreed in the same
output — a single-decoder parser would have shown one wrong name and looked fine.

**The fixtures could not express the bug.** `DemoHeaderTests` built its headers with
`Encoding.ASCII.GetBytes`, so no test in that file could have caught it however many were added.
When a whole class of input is absent from a test suite, look at the fixture BUILDER before
concluding the cases are missing — see [[fixtures-are-the-weak-point]].

**Flipping each decoder to ASCII found three more gaps that no test noticed.** Six UTF-8 decode
sites exist; three were unpinned, including `NetBitReading`, which every string on the wire passes
through — map names, model precache, console commands, string table entries. That sweep is the
method: `Encoding.UTF8` → `Encoding.ASCII`, one site at a time, and see whether the suite cares.

One is equivalent by construction: the JSON Lines writer, because `Utf8JsonWriter` escapes
non-ASCII to `\uXXXX` by default, so its buffer never holds a byte above 0x7F. Verified against a
real dump (`"client":"miałker"`), not assumed. Assert JSONL content by parsing the line back,
never by searching the raw text — a text search tests the escaping policy instead of the data.

**What is realistic:** Cyrillic, CJK, CJK punctuation, accented Latin. **Emoji are not valid Steam
display names** (owner, same day) — they remain in a few fixtures only to exercise four-byte
sequences and surrogate pairs, and the comments say so. Chat *text* carries no such restriction.

Related: [[numeric-decoding-traps]] is the same failure mode in another dimension — wrong output
that reads as a plausible value rather than as an error.
