---
name: the-fat-column-is-the-subtracted-one
description: Every direct timer read ~1 ms while the subtracted remainder held 126 ms; the pattern was the signal, and the cost was one log line taking a machine-wide mutex.
metadata:
  type: project
---

**When every directly-measured column is small and the fat one is computed by SUBTRACTION, the
pattern is the finding — not noise to be narrowed one more time.** Measured 2026-08-25 hunting a
~130 ms stall (B191):

```
pose 128.7 (lighting 0.7, viewmodel 0, simulate 0.3, wornlight 0.8,
            setup 1.1, skin 0.2, rest 125.6)
```

Six timers, ~3 ms between them, and `rest` holding 125.6. Each new timer added moved the fat column
to whatever was still being subtracted. That went round several times before the shape was read.

**The answer was `Debug.WriteLine` in the log sink** — `OutputDebugString`, which serialises every
caller on the machine through the global `DBWinMutex`. One line cost ~120 ms. Fixed by gating on
`Debugger.IsAttached`; worst frame per second went 120.66 ms → 13-16 ms.

**Why it resisted so long:**

- **It appeared to MOVE between phases** — `pose`, `players`, `weapons`, `models` across runs —
  because every one of them logs. That reads as external CPU contention, and was argued as such.
  The mechanism was right and the victim wrong: not starvation, but our own line taking a global
  lock.
- **`Debug.WriteLine` is `[Conditional("DEBUG")]`.** Live in the build you develop and profile in,
  absent from Release. A cost that exists only where it can be observed, and only for the person who
  could fix it.
- **The owner identified it before the instrument did**, with *"the spikes are deterministic theyy
  are ours"*. Determinism was the decisive fact: a tight cluster at 128-138 ms across five runs, and
  the startup pair landing at +13.6/+14.5 s every time. Random contention gives a broad spread.

**How to apply:**

- **Suspect the instrument's own I/O.** A logger, a console write, a file flush — anything on the
  measurement path can cost more than the thing measured. Time the WRITE separately from the work.
- **A stall that lands in a different phase each run is one shared mechanism, not several bugs.**
  Look for what all those phases have in common; here it was that they all log.
- **Check the distribution before blaming the machine.** Tight cluster → deterministic → ours.
  Broad spread → contention. This distinction was available in the logs hours before it was used.
- **Read `[Conditional]` and build-configuration-gated code as a hiding place**, because its cost is
  invisible in the configuration users run and only ever paid by developers.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[nothing-is-closed]],
[[logs-are-the-debugger]].
