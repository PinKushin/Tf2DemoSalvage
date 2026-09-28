---
name: port-the-engines-bottom-layer-first
description: "Port the engine's smallest named object before anything that consumes it — a top-down port retrofits every later fact into the wrong object, and its tests cannot see the bottom layer at all."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T22:24:25.717Z
---

Owner, on why B382 became a large refactor instead of a small fix: *"these problems happened because
we started top down and not bottom up i think."*

**Why:** the engine builds interpolation bottom-up — a dumb entry list knowing nothing about meaning,
registration per member (`AddVar`, `c_baseentity.cpp:875`), then latching, only THEN pose selection.
This project started at the
top (one list of whole poses), so every engine fact learned afterward had to be retrofitted: one
history per variable became a list with two search keys, a second latch clock became a side-table, an
unconditional operation became a collapse plus a reconstruction field, a private method's call site
got silently dropped by a later refactor. Six facts about small objects, each expressed as a property
of a large one.

**It infected the tests too** — everything asserted on the drawn pose, so a conformance test needing
to detect a missing HISTORY ENTRY couldn't: while a value is held the bracketing pair is degenerate,
and the drawn pose is right regardless. Three sweeps in a row couldn't fail for the fault they named;
what worked was one exact value at the single tick where the bottom layer reaches the top.

**How to apply:**
- Port the engine's smallest NAMED object first, with its own tests, before its consumer.
- When a divergence is filed, ask which LAYER it belongs to before writing anything.
- A top-level assertion is necessary and not sufficient — see [[output-level-assertion-or-it-is-not-done]].
- Tell of a top-down port: a fact about the engine can only be stated as an extra key, a side-table,
  or a guard inside the consumer.

Related: [[an-unused-method-may-be-the-engines]],
[[the-interpolation-pair-is-found-by-changetime]]#the-prune-keeps-two-stale-entries,
[[parity-is-the-search-not-the-defence]], [[a-player-is-not-a-prop-track]],
[[filing-a-divergence-is-not-fixing-it]].

---

## `retaining-what-the-engine-deletes-breaks-its-invariants` — a deletion was holding something true

**Wherever this project keeps what the engine throws away, ask what the engine's deletion was
holding true.** The retention (a viewer never seeks backward, so keeping history is correct) is
deliberate — but a deletion is also the reason some invariant held, and that reason leaves quietly.

B386: a history stored entries flat, sliced at `index * width` — correct only while every entry has
the same width, which the engine guarantees: `SetMaxCount` calls `ClearHistory()`
(`interpolatedvar.h:1272`). The width is the model's own, set in `c_baseanimating.cpp:1124`. This
project's equivalent reset deletes nothing, so a history outliving a model change holds TWO widths at
once.

**It fails two ways, only one loud:** width grew → throws (a headless capture dies before writing its
PNG); width shrank → silently returns another entry's floats, no throw, no log, a pose built from
wrong numbers. **A bounds guard is the wrong fix — it converts the loud half into the silent half.**
Fix: carry each entry's own address/width rather than a fixed stride. See
[[address-a-struct-by-name-not-from-its-end]].

**The viewer's log couldn't tell you a capture crashed** — no unhandled-exception handlers installed,
so the runtime printed to stderr and the buffered log ended mid-frame looking like any normal run.
Fixed (B402) by registering `Application.ThreadException` (requires `SetUnhandledExceptionMode` set
FIRST or it's decoration), `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`.
**A crash inside `Dispose` reaches NONE of them** — it runs inside the window procedure, converted to
`0xC000041D` with empty stderr; needed a marker naming each member as released.

The other two retentions in the same class, both deliberate: `Add(flushNewer: true)` records a flush
tick instead of deleting (B384); `Reset` becomes a generation boundary instead of clearing
(B382/B383).

Related: [[logs-are-the-debugger]], [[ci-is-the-machine-without-tf2]].

---

## `a-flat-array-is-addressed-by-a-width` — the stride belongs to the ENTRY, not the container

**Components stored flat are addressed as `index * width`, so the width belongs to the ENTRY
written, not the container.** Treating it as the container's makes every already-held entry
unreadable the moment width changes.

**The mistake worth remembering is the FIX, not the bug.** The first fix was clearing entries,
citing the engine's own `ClearHistory()` (`interpolatedvar.h:740`) and arguing "an entry whose layout
no longer exists answers nothing" — **that sentence is a consequence of the defect dressed up as a
fact about the data.** The layout does still exist, at the width it was written; only the fixed
stride made it unreachable. A scrub back to before a width change must answer what a client at that
moment held (D131), which clearing cannot do (the engine has no scrub feature to arbitrate the
question). See [[the-base-is-not-the-behaviour]].

**The crash named its witness rather than its cause** — filed as an opening-sequence fault because
every stack trace came through the capture code that happened to sample first; a plain playback run
with no capture produced the real stack immediately. **When a crash is intermittent, vary the entry
point before believing the frame that reported it.** It was also latent until B385 put brush entities
on the interpolation list, growing the caller population enough to surface a dormant defect.

Related: [[a-lazy-cache-makes-reading-a-write]], [[struct-padding-is-on-disk]],
[[name-the-trade-before-fixing-valve]], [[a-filed-design-choice-may-not-be-one]].
