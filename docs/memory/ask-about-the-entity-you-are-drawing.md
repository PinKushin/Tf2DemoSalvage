---
name: ask-about-the-entity-you-are-drawing
description: A rule about "the player being watched" must resolve that player through the one accessor that knows which it is. Asking SpectatorTarget.Choose on a POV demo answers about someone else entirely.
metadata:
  type: feedback
---

**When a rule is about "the thing being shown", resolve that thing through the single accessor that
knows which it is — never through a plausible neighbour.**

**Why:** `SpectatorView.Effective` decides whether the camera may stay in first person, and it asked
`Target(tick)`. `Target` is `SpectatorTarget.Choose` — the lowest entity index on a playing team —
which is correct for a SourceTV recording and **wrong for a point-of-view one**, where the camera is
the RECORDER's own and the recorder is usually somebody else. So the recorder died, another player
was alive, the rule was told "alive", and the viewer stayed in first person drawing a dead man's
weapon (B225).

`Followed(tick)` had resolved this correctly the whole time, and says so in its own remarks: *"Asked
in one place so the two decisions cannot disagree."* The rule simply did not ask it. **The resolver
existing is not the same as it being used** — that is [[one-place-or-it-drifts]] with the drift
already prevented and the prevention bypassed.

**How to apply:**

- Grep for every call to the neighbour before assuming yours is the only wrong one. There were two —
  `Effective` and `Chase` — and fixing only the first would have dropped a POV demo out of the
  recorder's eyes and landed it behind a stranger: a NEW visible defect manufactured by half a fix
  ([[half-a-mechanism-is-not-parity]]).
- Give the resolver a name that says which question it answers. `Target` and `Viewed` differ by one
  concept and the difference was invisible at the call site.

**The other half of this, and it nearly shipped a wrong fix.** The first theory was `m_iObserverMode`
— the engine's own first-person test, genuinely missing, correctly implemented, with a conformance
suite off the SDK. Every test passed. It explains **none** of the bug: across three POV demos,
samples that are alive AND observing come to **zero**, because every observing sample is also dead
and liveness already handled those. A column printing that count is the only reason it was caught.

So: **a correct measurement can be about the wrong quantity.** Before reporting a fix, measure the
population the fix actually changes — not the population the theory is about. If that number is
zero, the theory is wrong however green the suite is. See [[run-the-control-before-arguing]].

**And read the log for the transition that did NOT happen.** A thirty-second run through a death
logged one mode line and no fall to third person. That absence was sitting in the file the whole
time; nobody had looked, because the demo had never run forward unattended until autoplay was fixed.

Related: [[lookups-must-match-exactly]], [[measure-every-hop-before-blaming-one]],
[[log-the-event-not-a-sample-of-it]], [[output-level-assertion-or-it-is-not-done]],
[[suspect-the-input-not-the-algorithm]].
