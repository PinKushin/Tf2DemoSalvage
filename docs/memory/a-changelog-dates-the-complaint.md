---
name: a-changelog-dates-the-complaint
description: "Valve's notes date their build exactly; only a note describing a REPAIR lags the thing it describes"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-11T17:19:18.596Z
---

Two owner corrections on 2026-08-11, in sequence, and the second narrowed the first.

**First correction.** TF2's 15 November 2007 note says *"Added backward compatibility code to
allow demos recorded with protocol 12 to continue to be playable under protocol version 13"*, and
it had been written up as dating protocol 13 **exactly**. It does not: the note describes a
*repair*, and 13 had to ship, players had to find protocol-12 recordings unplayable, and someone
had to report it. Owner: *"they wouldnt have known the demo system broke until someone complained"*.

**Second correction, which is the one to remember.** The rule was then over-generalised to "a
changelog dates the complaint". Owner: *"most of the changelogs are done on the day of update with
valve and tf2"*. Correct — Valve publishes TF2 notes the day the build ships, so **the note's date
is exact for the build it describes**. Nothing lags there.

| the note says | the date is | why |
|---|---|---|
| "Added *X*" | **exact** for when *X* went live | the build shipping *is* the event |
| "Fixed *X*", "Added compatibility for *X*" | **upper bound** on *X* | break, report and fix are three different days |

**Why it matters:** the trap is not that changelogs are unreliable. It is reading a **repair** as
if it were an **announcement**. That produces a date that looks measured, reads as measured, and
is wrong by weeks in a direction nobody checks.

**How to apply:** before grading a changelog date, ask whether the note announces something or
responds to something. Announcements are strong evidence — strong enough that TF2's user message
table can be dated from feature updates (`RDTeamPointsChanged` → Robot Destruction, 8 July 2014),
which is how the protocol-24 name-table ambiguity in RISKS B29 gets a discriminator. Repairs bound
from above only.

Residual caveat, independent of changelog timing: code can ship dark before the feature that uses
it is announced, so a feature date bounds a code change from above too. Prefer evidence that needs
no date at all — the *presence* of a late id in a demo proves the late table regardless.

Related: [[era-axis-is-measured]], [[binaries-answer-what-the-sdk-cannot]], [[research-before-code]].
