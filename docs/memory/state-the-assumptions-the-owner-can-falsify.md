---
name: state-the-assumptions-the-owner-can-falsify
description: "When debugging a visual bug, say out loud what you are ASSUMING about the symptom — the owner is looking at the thing and can correct it in one sentence."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:55:21.596Z
---

**While bug-fixing, list the assumptions being made about the symptom, explicitly, so the owner can
knock them down** — he's looking at the program, you're not.

Owner: *"lets make a method when we a bug fixing that you ask me about any assumptions you are
making, like the hands still showing, because if i knew you were only checking the weapon and not the
whole viewmodel as i know it, i could have told you much sooner."* A weapon reported "sometimes
doesn't draw" got four killed mechanisms aimed at that one model before he mentioned in passing that
the ARMS were missing too — the whole viewmodel pass was blanking, and every clean result had been
clean because the wrong model was tested.

**Why:** a symptom is reported as the part NOTICED, never its full extent. "The weapon is missing"
and "everything at the hands is missing" produce the same sentence and completely different
searches — invisible from the log, obvious from the chair.

**How to apply, for a visual defect:**
- What else is missing? Name the neighbours explicitly.
- What is the full extent — one model, one pass, the whole frame?
- What is being taken literally from the report? Quote it back.
- What is assumed constant — same map, demo, weapon, player?

Then at each measurement, say what a clean result would MEAN — "instrument silent" and "nothing
wrong" are the same output for a badly aimed instrument ([[instrument-bugs-outnumber-decoder-bugs]]).

Related: [[ask-which-input-differs-before-bisecting]], [[nothing-is-closed]].

## `the-owners-guess-is-a-hypothesis-too` — test his, and split the measurement (D166)

**The owner is not a second authority beside Valve, and says so:** *"make sure im right do not take
my word as law, valve is law and god, I am making random educated guesses lol"*, then *"you always
test your and my hypothesis, more than once if you need to."* His steer is worth taking seriously and
still settled by the engine's source or a number.

His guess about a corpse's bodygroups was right, measured — but reading the engine FIRST showed the
fix was more specific than his wording (three passes in a fixed order, not list order).

**The half worth keeping: one count answered nothing.** "11 of 204 match" is equally consistent with
"the value is absent" and "my comparison is wrong" — opposite fixes. Measured apart ("readable at
all" vs. "matches when readable"), it settled in one run.

**How to apply:** before reporting a ratio as evidence, ask which two explanations it cannot separate,
and measure those apart. Related: [[instrument-bugs-outnumber-decoder-bugs]],
[[the-denominator-decides-what-can-be-lost]], [[valve-parity-is-the-first-principle]].
