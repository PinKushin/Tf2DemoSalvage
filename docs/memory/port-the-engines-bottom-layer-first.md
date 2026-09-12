---
name: port-the-engines-bottom-layer-first
description: "Port the engine's smallest named object before anything that consumes it — a top-down port retrofits every later fact into the wrong object, and its tests cannot see the bottom layer at all."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T22:24:25.717Z
---

**The owner, on why B382 became a large refactor instead of a small fix:**

> *"these problems happened because we started top down and not bottom up i think"*

**Why:** the engine builds interpolation bottom-up. `CInterpolatedVarArrayBase` is a dumb list of entries —
a changetime and `m_nMaxCount` floats — that knows nothing about what the floats mean. `AddVar` registers
one per networked member (`c_baseentity.cpp:875`); `OnLatchInterpolatedVariables` appends to each whose
latch group fired (`:2814`); only above all of that does anything ask what pose to draw.

This project started at the top: `ScenePropTrack.At` answered *"what pose should be drawn"* over one list
of whole poses. Every engine fact learned afterwards had to be retrofitted into that one object — one
history per variable became one list with two search KEYS, the second latch clock became a side-table,
`AddToHead` being unconditional became a collapse plus `_heldUntil` to reconstruct the hold, the
arrived-only history became a guard inside `At`, and `TimeFixup_Hermite` became a private method whose call
site a later refactor dropped without a single warning. Six facts about small objects, each expressed as a
property of a large one. The result was neither ours nor Valve's.

**And it infected the tests, which is the part that is easy to miss.** Everything written for this area
asserted on the drawn pose. So when the fix's own conformance test needed to detect a missing HISTORY
ENTRY it could not: while a value is held the bracketing pair is degenerate, and the drawn pose is right
whatever the history contains. **Three sweeps in a row were written and none could fail for the fault it
named.** What worked was one exact value at the single tick where the bottom layer reaches the top.

**How to apply:**

- Port the engine's smallest NAMED object first, with its own tests, before the thing that consumes it.
- When a divergence is filed, ask which LAYER it belongs to before writing anything. A reset belongs on
  the history; "assigned on receipt" belongs to the member, not to the sampler.
- A top-level assertion is necessary and not sufficient — see
  [[output-level-assertion-or-it-is-not-done]], which points the other way and is equally true.
- The tell that a port went top-down: a fact about the engine can only be stated here as an extra KEY, an
  extra side-table, or a guard inside the consumer.

Related: [[an-unused-method-may-be-the-engines]],
[[the-interpolation-pair-is-found-by-changetime#the-prune-keeps-two-stale-entries]],
[[parity-is-the-search-not-the-defence]], [[a-player-is-not-a-prop-track]],
[[filing-a-divergence-is-not-fixing-it]].

---

## `retaining-what-the-engine-deletes-breaks-its-invariants` — a deletion was holding something true

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

### It fails two ways and only one is loud

- **Width grew** — the offset runs past the end, `Slice` throws `ArgumentOutOfRangeException`, and a
  headless `--shot` capture dies before writing its PNG.
- **Width shrank** — the offset lands inside a neighbour's components. The read succeeds and returns
  another entry's floats. No throw, no log, a pose built from the wrong numbers.

**So a bounds guard is the wrong fix**: it converts the loud half into the silent half. The fix was to
carry each entry's address in an `_offsets` list, its own width being the distance to the next one.
See [[address-a-struct-by-name-not-from-its-end]] — the same shape in a mapped buffer.

### The viewer's log cannot tell you a capture crashed

`Program.Main` installs no `AppDomain.UnhandledException` or `Application.ThreadException` handler, so
the runtime prints to stderr and the buffered log ends mid-frame looking like any other run. Forty-nine
logs contained no trace of two reported crashes. **Read the process's stderr, not the log**, and treat
a log that just stops as evidence of nothing. Related: [[logs-are-the-debugger]].

### Where else the same question is open

The other two retentions in the same class, both deliberate and both documented:

- `Add(flushNewer: true)` records a flush tick instead of deleting (B384).
- `Reset` becomes a generation boundary instead of clearing (B382/B383).

Each was checked here and neither carries a stride assumption, but the question is the one to ask of
any future one: **what did the engine's delete make true, and what still assumes it?**

---

## `a-flat-array-is-addressed-by-a-width` — the stride belongs to the ENTRY, not the container

**Components stored flat are addressed as `index * width`, so the width belongs to the ENTRY that was
written, not to the container.** Treat it as the container's and every entry already held becomes
unreadable the moment it changes: widening runs off the end and throws, narrowing silently returns the
first half of a neighbour. The second has no symptom at all.

`InterpolatedHistory.SetMaxCount` did this. `m_flPoseParameter` is registered at width 1 and grown to
the model's own count on `OnNewModel` (`c_baseanimating.cpp:1124`), so every animated entity does it
once; it crashed the viewer about one run in three.

### The mistake worth remembering is the FIX, not the bug

The first fix was to clear the entries, citing the engine — `Reset()` opens with `ClearHistory()`
(`interpolatedvar.h:740`) — and argued: *"an entry whose layout no longer exists answers nothing."*

**That sentence is a consequence of the defect dressed up as a fact about the data.** The layout does
still exist. It is the width the entry was written at, and only the FIXED stride made it unreachable.
Carrying a per-entry offset keeps every old entry readable at its own width, and a peer session
measured what the clearing version costs: a scrub back to before the width change stops finding the
entries a client at that moment held.

**So the reasoning inverted the dependency.** It read a limitation of the storage as a property of the
subject, then quoted the engine to justify it. The engine could not arbitrate: it has no scrub, so
`ClearHistory()` is the absence of the question rather than an answer to it —
[[the-base-is-not-the-behaviour]] applied to a whole missing feature.

**The test that settles it is about the REQUIREMENT, not the mechanism**: a client playing forward at
tick 15 held those entries and blended them, so a scrub back to tick 15 must answer what that client
answered. Written as `Bracket_ScrubbedBackToBeforeAWidening_...`, it fails against clearing and passes
against per-entry offsets. Whenever a retention rule is invoked, ask which side of D131's line the
change falls on: what is RETAINED is licensed, what is ANSWERED is not.

### And the crash named its witness rather than its cause

It killed the viewer about one run in three and the only stack anyone had came through
`TakeAutomaticShot` — the frame that happened to sample first — so it was filed as an opening-sequence
fault and a whole session was started against the wrong file. A plain playback run with no `--shot`
produced the real stack immediately. **When a crash is intermittent, vary the entry point before
believing the frame that reported it**: the first caller to touch shared state is the earliest witness,
not the cause.

**It was also latent until something else changed.** B385 put brush entities on the interpolation list,
so `Bracket` began being called for a great many tracks it never had been. A dormant defect in shared
state surfaces when a caller population grows, which makes the new caller look guilty.

Related: [[a-lazy-cache-makes-reading-a-write]], [[struct-padding-is-on-disk]],
[[address-a-struct-by-name-not-from-its-end]], [[the-base-is-not-the-behaviour]],
[[name-the-trade-before-fixing-valve]], [[a-filed-design-choice-may-not-be-one]].
