---
name: an-uncoverable-gap-is-usually-your-reader
description: "Exclusions that sound like properties of the format are usually properties of your parser, and they go unexamined because they are written confidently."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-16T16:53:59.525Z
---

**When a conformance suite records something as "not coverable", re-read the reason before believing
it.** Three such notes were written during the SDK-derived work on 2026-08-16 and two were wrong:

- `ddispinfo_t` was excluded because it "embeds `CDispNeighbor`, a class rather than a struct". C++
  makes `class` and `struct` identical for layout — they differ only in default access. The obstacle
  was one keyword missing from a regex. Adding it derived the whole chain, ending at 176.
- The static prop lump was excluded for having "a per-version layout". It has four versions, all
  declared, and they only append — so the property the reader actually relies on (origin, angles and
  prop type at fixed offsets in every version) was checkable all along, and is a *better* test than
  a stride.
- Only the VTX topology fields were genuinely uncoverable: added under a define the published SDK
  does not carry.

**Why it costs:** the exclusion is written in the same confident tone as the rest of the file, sits
in the file it excludes, and reads as a fact about Valve's format rather than about the tool. Nothing
ever fails to make it re-examined. It is the same shape as
[[a-test-can-outlive-its-design]] — a correct statement that stops being one silently.

**The general move: separate "the format does not permit this" from "my reader does not do this
yet".** The first is a finding; the second is a to-do wearing a finding's clothes.

**Related failures of the same kind, same session:**

- A constant extractor rejected lowercase, silently dropping
  `TCOMBINE_RGB_EQUALS_BASE_x_DETAILx2`. A constant missing from the reference makes whatever asked
  for it look *checked*.
- A control asserted that defining `REPLAY_ENABLED` changed a struct's SIZE. It does not — the extra
  byte lands in padding the struct already had. The member list was the sensitive measurement.
- Caching a file crawl to speed up a suite: measured 553 ms before, 532–648 ms after. Noise. The real
  cost was elsewhere entirely (a missing `[assembly: Parallelizable]`, worth 3x).

Related: [[conformance-test-before-implementation]], [[instrument-bugs-outnumber-decoder-bugs]],
[[measure-the-output-not-the-capability]].
