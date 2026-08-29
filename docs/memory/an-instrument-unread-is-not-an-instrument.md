---
name: an-instrument-unread-is-not-an-instrument
description: A diagnostic that has never been read on a run is a plan, not a measurement. Reading one took a single launch and found the viewer had been silent for days.
metadata:
  type: feedback
---

**Adding a diagnostic is half the work. Read it on a real run, in the same session, or it is not an
instrument yet.**

**Why:** `SoundPresenter.ReportAudioOutput` was written days before it was read — submitted against
dropped-for-zero-gain, reported at 1, 10, 100, 1000 so a broken run says so immediately. The handoff
carried it as *"added and has never been read on a run"*. Reading it cost one launch, and **the line
was simply absent**: 23,772 sounds on the timeline, 542 precached, 110 frames drawn, nothing
submitted. The whole sound path had been dead (B228).

An unread instrument is worse than none, because it looks like coverage. The intent to measure gets
remembered as a measurement.

**How to apply:**

- **Check the instrument ran before believing what it says.** The first attempt here reported
  "no audio" from a run that drew **zero frames** — a 45-second timeout against a map that takes 40
  seconds to load. An absent line means "never reached" as readily as "reached and zero", and those
  are opposite findings. Confirm the surrounding activity — frames drawn, ticks advanced — first.
- **Absence is a reading, but only against a control.** No `sound output:` line AND no `loop '...'`
  line AND a live frame count is three facts agreeing. One of them alone is not.
- Prefer an instrument that reports on the FIRST occurrence. At 1, 10, 100 a healthy run says one
  line and goes quiet, while a dead one says nothing at all — which is exactly the signal wanted.

**And the shape it found is worth its own line: a later call silently undoing an earlier one.** Three
of these turned up in one day — autoplay switched off by `SetDemoLength`, a stale clock left by an
early return, and the demo's sounds deleted by a map read. All three are assignments, so none of them
logged anything. When a feature "does nothing", grep for everything that WRITES the field it depends
on, and check the order against the caller — do not start by reading the feature.

Related: [[measure-the-output-not-the-capability]], [[logs-are-the-debugger]],
[[output-level-assertion-or-it-is-not-done]], [[log-the-event-not-a-sample-of-it]],
[[ask-valve-before-designing-not-after]].
