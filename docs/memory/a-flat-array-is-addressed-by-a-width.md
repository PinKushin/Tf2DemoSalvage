---
name: a-flat-array-is-addressed-by-a-width
description: "A stride is per-entry, not per-container — and a limit the implementation imposes will pass itself off as a fact about the data."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-10T17:13:43.414Z
---

**Components stored flat are addressed as `index * width`, so the width belongs to the ENTRY that was
written, not to the container.** Treat it as the container's and every entry already held becomes
unreadable the moment it changes: widening runs off the end and throws, narrowing silently returns the
first half of a neighbour. The second has no symptom at all.

`InterpolatedHistory.SetMaxCount` did this. `m_flPoseParameter` is registered at width 1 and grown to
the model's own count on `OnNewModel` (`c_baseanimating.cpp:1124`), so every animated entity does it
once; it crashed the viewer about one run in three.

## The mistake worth remembering is the FIX, not the bug

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

## And the crash named its witness rather than its cause

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
