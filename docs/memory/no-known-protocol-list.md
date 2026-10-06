---
name: no-known-protocol-list
description: Never gate a test or decode on a closed set of "known" protocols; the floor is 11, coverage is a TIMELINE fact (D211).
metadata:
  type: feedback
---

**A closed list of accepted protocol values is banned; a protocol RANGE gating a decode branch is fine.**
`CorpusContainerTests` pinned the header protocol to `[11, 14, 15, 16, 21, 22, 24]` and failed the first
18 and 19 specimens, which decoded cleanly. Owner: *"we know tf2 demos are protocol 11+ ... not having it
listed somewhere should never keep it from being tested"*.

So: assert `>= 11`. Which protocols have specimens is coverage, recorded in `docs/TIMELINE.md`, never a
gate. A new protocol showing up is a dating/specimen finding to write up, not a test failure. D211.
