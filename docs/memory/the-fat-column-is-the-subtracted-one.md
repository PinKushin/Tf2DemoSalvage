---
name: the-fat-column-is-the-subtracted-one
description: Every direct timer read ~1 ms while the subtracted remainder held 126 ms; the pattern was the signal, and the cost was one log line taking a machine-wide mutex.
metadata:
  type: project
---

**When every directly-measured column is small and the fat one is computed by SUBTRACTION, the
PATTERN is the finding, not noise to narrow further.** Hunting a ~130ms stall: six timers at a few ms
each, and a `rest` remainder holding 125.6ms. Each new timer added just moved the fat column to
whatever was still subtracted — happened several times before the shape was read.

**The answer was `Debug.WriteLine`** — `OutputDebugString` serialises every caller on the machine
through a global mutex; one line cost ~120ms. Fixed by gating on `Debugger.IsAttached`.

**Why it resisted:** it appeared to MOVE between phases across runs (every phase logs), reading as
external contention when it was our own line taking a global lock; `Debug.WriteLine` is
`[Conditional("DEBUG")]`, invisible in Release, present only where it can be profiled. **The owner
identified it first**: *"the spikes are deterministic theyy are ours"* — a tight cluster is
deterministic (ours); a broad spread is contention.

**How to apply:** suspect the instrument's own I/O — time the WRITE separately from the work. A stall
landing in a different phase each run is one shared mechanism, not several bugs — look for what those
phases have in common. Check the distribution before blaming the machine. Read `[Conditional]` code
as a hiding place — its cost is invisible in the configuration users run.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[nothing-is-closed]], [[logs-are-the-debugger]].
