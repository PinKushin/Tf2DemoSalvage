---
name: ask-about-the-entity-you-are-drawing
description: "A rule about \"the player being watched\" must resolve that player through the one accessor that knows which it is. Asking SpectatorTarget.Choose on a POV demo answers about someone else entirely."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:55:25.791Z
---

**When a rule is about "the thing being shown", resolve it through the single accessor that knows
which it is — never a plausible neighbour.**

`SpectatorView.Effective` asked `Target(tick)` = `SpectatorTarget.Choose` (lowest entity index on a
playing team) — correct for SourceTV, **wrong for POV**, where the camera is the RECORDER's own and
he's usually someone else. Recorder died, another player was alive, the rule was told "alive", viewer
stayed first-person drawing a dead man's weapon (B225).

`Followed(tick)` had resolved this correctly all along, with its own comment: *"Asked in one place so
the two decisions cannot disagree."* The rule simply didn't call it — the resolver existing isn't the
same as being used ([[one-place-or-it-drifts]] with the drift already prevented and bypassed).

**How to apply:**
- Grep every call to the neighbour before assuming only yours is wrong — two callers here
  (`Effective`, `Chase`); fixing only one would have manufactured a NEW visible defect
  ([[parity-is-the-search-not-the-defence]]).
- Name the resolver for the question it answers — `Target` vs `Viewed` differ by one concept,
  invisible at the call site.

**A correct measurement can be about the wrong quantity.** The first theory (`m_iObserverMode`,
genuinely missing, correctly implemented, fully tested) explained NOTHING — across three POV demos,
samples "alive AND observing" = zero, since every observing sample is dead and liveness already
handled those. A column printing that count is what caught it — measure the population the fix
actually changes, not the population the theory is about. See [[instrument-bugs-outnumber-decoder-bugs]].

Related: [[key-a-lookup-on-the-question]], [[nothing-is-closed]],
[[output-level-assertion-or-it-is-not-done]].
