---
name: state-the-assumptions-the-owner-can-falsify
description: "When debugging a visual bug, say out loud what you are ASSUMING about the symptom — the owner is looking at the thing and can correct it in one sentence."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:55:21.596Z
---

**While bug-fixing, list the assumptions you are making about the symptom, explicitly, so the owner
can knock them down.** He is looking at the program; you are not. An assumption he could falsify in
five seconds will otherwise steer hours of measurement.

The owner, 2026-08-28, after exactly that happened:

> *"lets make a method when we a bug fixing that you ask me about any assumptions you are making,
> like the hands still showing, because if i knew you were only checking the weapon and not the whole
> viewmodel as i know it, i could have told you much sooner"*

**The case that produced it.** He reported *"the sticky launcher sometimes doesnt draw"*. That was
taken literally and everything was aimed at that one model — its bones, its sequence table, its bone
merge, its posed vertex extent, its material classification, its render group. Four mechanisms were
proposed and killed. Hours later he mentioned in passing that **the arms were missing too**, which
meant the whole viewmodel PASS was blanking and every measurement had been pointed at the wrong
subject. The clean results were clean because that model was fine.

**Why:** a symptom is reported as the part that was NOTICED, never as its full extent. "The weapon is
missing" and "everything at the hands is missing" produce the same sentence and completely different
searches. The gap between them is invisible from the log and obvious from the chair.

**How to apply.** Before instrumenting, say what is being assumed and ask. Concretely, for a visual
defect:

- **What else is missing?** Name the neighbours explicitly — "are the hands still there?", "is the
  HUD still drawn?", "does the world behind it look right?"
- **What is the full extent?** One model, one pass, the whole frame.
- **What is being taken literally from the report?** Quote it back with the reading being used.
- **What is assumed constant?** Same map, same demo, same weapon, same player.

Then, at each measurement, say what a clean result would MEAN — because "instrument silent" and
"nothing wrong" are the same output for a badly aimed instrument, which is
[[instrument-bugs-outnumber-decoder-bugs]].

This is the same rule as *"anything about a UI that cannot be verified by looking is a QUESTION for
the user"*, moved one step earlier: it applies to the framing of the hunt, not only to its
conclusion. Related: [[instrument-bugs-outnumber-decoder-bugs]],
[[ask-which-input-differs-before-bisecting]], [[nothing-is-closed]].

## `the-owners-guess-is-a-hypothesis-too` — test his, and split the measurement (D166, 2026-09-12)

**The owner is not a second authority beside Valve, and he says so himself:** *"make sure im right
do not take my word as law, valve is law and god, I am making random educated guesses lol"*, then
*"you always test your and my hypothesis, more than once if you need to"*. A steer from him is
worth taking seriously — years of playing the game — and it is still settled by the engine's source
or by a number, never by who said it.

**It earned itself the moment it was given.** He suggested using a player's last known weapon for a
corpse's bodygroups; measured, that took the deployed-weapon count from 11 of 204 to 199 of 204. He
was right. And reading the engine FIRST showed the fix was more specific than his wording:
`RecalculatePlayerBodygroups` runs three passes in a fixed order (`tf_player_shared.cpp:13693`), so
an implementation built from the steer alone would have applied items in list order and been wrong
wherever two claim the same body part.

**The half worth keeping is that one count answered nothing.** "11 of 204 name the held weapon" is
equally consistent with *the value is absent at that tick* and with *my comparison is wrong*, and
those have opposite fixes — a fallback versus a decode bug. Measured apart, "readable at all" gave
15 of 204 and "matches one of his own children WHEN readable" gave 15 of 15, which settles it in
one run.

**How to apply: before reporting a ratio as evidence, ask which two explanations it cannot separate,
and measure those apart.** A denominator that mixes an availability failure with an identity failure
is not a measurement, it is two measurements added together. Related:
[[instrument-bugs-outnumber-decoder-bugs]], [[the-denominator-decides-what-can-be-lost]],
[[valve-parity-is-the-first-principle]].
