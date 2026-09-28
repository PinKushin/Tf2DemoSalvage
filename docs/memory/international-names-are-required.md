---
name: international-names-are-required
description: "Every string decoder must be UTF-8 — TF2 names carry Cyrillic, CJK and accents routinely, and ASCII corrupts them into plausible-looking output"
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-10T11:18:34.694Z
---

Owner: *"international language does need to be accepted in this parser, tf2 names can get weird."*
Not an edge case — an ordinary input.

**Found via:** a demo from player `miałker` printed the same player twice in one dump — the header
(ASCII, `ł`→`??`) disagreed with the userinfo table (UTF-8, correct). Nothing failed; both fields held
a plausible name. Visible only because two decoders disagreed in the same output.

**Fixtures couldn't express it** — `DemoHeaderTests` built headers with `Encoding.ASCII.GetBytes`, so
no test there could catch it regardless of count. Look at the fixture BUILDER, not the test count,
when a whole class of input is absent.

**Flipping each decoder to ASCII found three more unpinned gaps**, including `NetBitReading`, which
every wire string passes through. Method: `Encoding.UTF8` → `Encoding.ASCII`, one site at a time, see
if the suite cares.

One is equivalent by construction: the JSONL writer, since `Utf8JsonWriter` escapes non-ASCII to
`\uXXXX` by default. Assert JSONL content by parsing the line back, never searching raw text.

**Realistic:** Cyrillic, CJK, accented Latin. **Emoji are not valid Steam display names** — kept in a
few fixtures only for four-byte/surrogate-pair coverage, commented as such. Chat text has no such
restriction.

Related: [[numeric-decoding-traps]].
