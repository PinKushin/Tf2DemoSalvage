---
name: a-changelog-dates-the-complaint
description: "A patch note dates the response to a problem, not the problem — treat it as an upper bound, never as the event"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-11T17:05:05.890Z
---

The owner caught this on 2026-08-11. TF2's 15 November 2007 patch note says *"Added backward
compatibility code to allow demos recorded with protocol 12 to continue to be playable under
protocol version 13"*, and it had been written up here as dating protocol 13 **exactly**.

It does not. The note describes a *repair*, and a repair lags the break: 13 had to ship, players
had to find their protocol-12 recordings unplayable, and someone had to report it. Owner's
wording: *"they wouldnt have known the demo system broke until someone complained and that
wouldnt even be mentioned if it was just the protocol jump"*.

**Why:** the failure mode is nasty because the result *looks* measured. A date lifted from a
changelog reads exactly like a date read off a build string, and is wrong by however long that
company's report-and-fix cycle was that year — in a direction nobody thinks to check.

**How to apply:** grade a changelog date as **bounded** (`event ≤ note`), never as measured or
exact, and say which direction the slack runs. Look for corroborating repairs in the same patch —
the same TF2 note also carries "Fix for broken .dem file playback", and two independent demo fixes
in one patch describes a system that had been broken in the field for a while.

Second, more general point from the same episode: **Valve documents effects, never mechanisms.**
This is the only protocol bump documented in nineteen years of TF2 patch notes, and it only got
written down because a user-visible thing broke. Searching a changelog for the mechanism finds
nothing; searching for the breakage finds the mechanism by accident.

Related: [[era-axis-is-measured]], [[research-before-code]].
