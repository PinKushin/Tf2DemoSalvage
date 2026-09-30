---
name: a-protocol-can-hide-two-builds
description: Protocol 15 is two wire dialects (B440) - one specimen per protocol cannot locate a change inside one; a protocol number promises client-server compatibility, not a dialect.
metadata:
  type: project
---

**Protocol 15 was written by two builds that disagree on the wire** (B440, 2026-09-30). Build 3862 (June
2009): five-bit message types, `SendPropType` without `DPT_VectorXY`. Later builds (by November 2010):
six bits and VectorXY — still announcing 15. Two SourceTV demos in lcor were noise from their first bit
for a month because both rules were keyed on the protocol.

**Why it hid:** each boundary was "measured on both sides" with ONE specimen per protocol (a 2009 POV at
15, a 2011 pair at 16), which can only say a change lies somewhere between them — and a deduction ("a
protocol only moves when the wire does") closed the gap on paper. The hl2sdk branch already diffed for
B18 had the date in its own history (`c789d33e`, 14 August 2009). Valve bumps the protocol when clients
and servers can no longer talk; TF2's always ran one build, so the wire changed and the number did not.

**The misdiagnosis on the way:** "SourceTV at that era writes differently", from one POV that decoded
and two SourceTV demos that did not. The 2007/2008 SourceTV specimens already contradicted it, and a hex
quote starting two bytes early (the length field's `00 00`) looked like the extra field it predicted.
**When a mode is blamed, ask which OTHER variable the two samples differ in** — here the build.

**How to apply:**
- A rule keyed on a protocol number is a claim that nothing changed inside it. Write down which builds
  it was measured on; treat a new build at an old protocol as unmeasured.
- When a demo is noise from bit 0, try the neighbouring era's rules before a new layout: B440's ServerInfo
  read as protocol 30 was 15 shifted one bit — the type field one bit too narrow.
- Settle "which dialect" from the demo's own bits, with a check that can see the wrong premise: the
  width at which ServerInfo restates the header's protocol; the numbering that reads the schema WHOLE.
- Related: [[era-axis-is-measured]], [[length-arithmetic-identifies-a-layout]],
  [[a-corpus-selection-outlives-its-corpus]], [[record-both-points-of-view]].
