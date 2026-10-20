---
name: show-a-comparison-dont-ask-him-to-look
description: When a change needs the owner to see it, the lead provides a golden-image comparison test or a screenshot with a comparison shot; never "please look in the game" as a gate.
metadata:
  type: feedback
---

Owner, 2026-10-20: *"if something needs me to see it, that is a golden image comparison test, or at least you giving me a SSS and comparison shot to look at"*.

**Why:** a request to go look in f12 puts the work on him and gates a release on his free time; a comparison shot or a golden test is the same evidence, produced by the instrument, and he can answer from the picture.

**How to apply:**
- A visible change (particles, HUD, a render fix) ships with (a) a golden/regression test that fails on the old pixels where the picture is assertable, or (b) a before/after pair of the SAME view (same demo, tick, camera; use the sidecar `--like` so both match) sent to him with `SendUserFile` and a one-line "what to look at".
- Find WHERE a change differs by a differential, not by guessing: shoot both builds at many ticks, diff the pixels, show the ticks that differ. A pair that is byte-identical proves nothing unless the shot reaches the changed code (a `--shot` reaches its tick by seek, which skips play-time paths).
- **The comparison is against the REFERENCE (real TF2), not old-ours vs new-ours** (owner, 2026-10-20, after the lead sent main-vs-branch particle pairs: *"none of those were tf2 images, tf2 was never booted so those are all our own app, that's not the comparison I asked for and I don't give a fuck about that one really, because I can't tell which one is more right"*). A before/after of our own build shows that something changed, never which side is right. Make it THREE-way: TF2 | ours before | ours after, same demo and tick (TF2 plays `tf/f12.dem` only, a different match from the demostf f12; follow the tick `demoui` reports), sidecar on every image.
- Do not list "owner to look" as a blocker when a comparison can be produced; keep it only for things no picture can show (sound by ear, feel of playback).
- Related: [[take-your-own-screenshot]], [[a-picture-is-assertable]].
