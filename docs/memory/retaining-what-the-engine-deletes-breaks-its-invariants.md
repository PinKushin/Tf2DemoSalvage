---
name: retaining-what-the-engine-deletes-breaks-its-invariants
description: The engine's deletions are load-bearing; keeping those entries for scrubbing quietly turned a per-history constant into a per-entry one, and a flat list read at a fixed stride crashed a headless capture.
metadata:
  type: project
---

**Every place this project keeps what the engine throws away, ask what the engine's deletion was
holding true.** The retention is deliberate and correct — a client never seeks backwards, a viewer
does — but a deletion is not only a deletion. It is also the reason some invariant held, and that
reason leaves with the licensed difference while nothing announces it.

B386, 2026-09-10. `InterpolatedHistory` stores entries in one flat `List<float>` and sliced it at
`index * _width`. Correct while every entry has the same width — which the engine guarantees, because
`SetMaxCount` calls `ClearHistory()` (`interpolatedvar.h:1272`) and the old-width entries are simply
gone. Our `Reset` deletes nothing, so a history that outlives a model change holds **two widths at
once**, and a single stride is wrong for every entry after the change.

The width is the model's: `m_iv_flPoseParameter.SetMaxCount( hdr->GetNumPoseParameters() )`
(`c_baseanimating.cpp:1124`). A class change moves it mid-match.

## It fails two ways and only one is loud

- **Width grew** — the offset runs past the end, `Slice` throws `ArgumentOutOfRangeException`, and a
  headless `--shot` capture dies before writing its PNG.
- **Width shrank** — the offset lands inside a neighbour's components. The read succeeds and returns
  another entry's floats. No throw, no log, a pose built from the wrong numbers.

**So a bounds guard is the wrong fix**: it converts the loud half into the silent half. The fix was to
carry each entry's address in an `_offsets` list, its own width being the distance to the next one.
See [[address-a-struct-by-name-not-from-its-end]] — the same shape in a mapped buffer.

## The viewer's log cannot tell you a capture crashed

`Program.Main` installs no `AppDomain.UnhandledException` or `Application.ThreadException` handler, so
the runtime prints to stderr and the buffered log ends mid-frame looking like any other run. Forty-nine
logs contained no trace of two reported crashes. **Read the process's stderr, not the log**, and treat
a log that just stops as evidence of nothing. Related: [[logs-are-the-debugger]].

## Where else the same question is open

The other two retentions in the same class, both deliberate and both documented:

- `Add(flushNewer: true)` records a flush tick instead of deleting (B384).
- `Reset` becomes a generation boundary instead of clearing (B382/B383).

Each was checked here and neither carries a stride assumption, but the question is the one to ask of
any future one: **what did the engine's delete make true, and what still assumes it?**
