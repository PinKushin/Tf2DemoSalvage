---
name: research-before-code
description: Guess, then verify against the SDK or a reference implementation, then code — the hypothesis is required because it produces the question; skipping the verify step is what costs
metadata:
  type: feedback
---

Owner's correction, 2026-08-08, after I hit the same class of bug three times in one session:
**do more research first.** When something turns out wrong like this, it means more of the SDK
and the open-source implementations should have been read before any code was written. Their
framing: *"we are reverse engineering, a lot of that is not even touching code and just
research, SDKs, changelogs, tests."*

**Why: the session's own evidence splits cleanly.**

Every row below started from a guess. That is not what separates them — what separates them is
whether the guess was **checked against a source before code was written on it**.

| Hypothesis | Checked first? | Outcome |
|---|---|---|
| "Coordinates are `COORD_MP`, per `dt_common.h`" | Yes | Right, no rework |
| "Coord flags are independent modifiers" | Yes | Wrong, found in minutes, cost nothing |
| "Changes-often is a stable partition" | No | Wrong; days of green fixtures; answer was in `flatten_props` |
| "Class id width is `ceil(log2)+1`" | No | Wrong; every fixture agreed, since 2 classes is where both formulas match |
| "The corpus uses LZSS" | No | Wrong; every compressed table is Snappy |

**The distinction that matters, in the owner's words:** *"research that doesn't pan out after a
question is not bad. Coding with that research and finding out later it's wrong, and the answer
was right there, is bad."* And: *"guessing for research is expected."*

**The hypothesis is mandatory, not merely tolerated.** *"You have to make an educated guess or
a hypothesis to look at — can't research a question you haven't asked, right?"* Guessing is the
step that produces the question; without it there is nothing to grep for. Reading source with no
hypothesis is browsing.

So the sequence is: **guess, then verify against a source, then code.** All three steps, in that
order. Forming a hypothesis and finding it empty is a normal result costing minutes. The failure
is only ever **skipping the middle step** — writing code on a guess when the answer was sitting
in a source nobody opened. The bill for that is not the guess; it is the
write-test-fail-research-fix cycle plus everything built on the wrong assumption before it
surfaced.

Worked example from the same session, and it is the *good* outcome: reading the SDK's flag
precedence turned up a real bug — coordinate flags are first-match, not independent modifiers —
which fixed nothing measurable, because no property in the corpus carries two coord flags. That
research did not pan out and was still correct to do. Cost: minutes. Compare the flattening
order, where the answer sat in `flatten_props` the whole time and the guess cost days.

**The reading is not preparation for the work; it is the work.**

**How to apply:** before implementing any wire format, read — in this order — the SDK headers
(`dt_common.h`, `bf_read`), `demostf/parser`'s implementation of that specific piece, then its
tests. Note the exact constants and the *order of conditionals*. Only then write the fixture,
and write it from the encoder side of what was read. A fixture written from the same guess as
the decoder cannot falsify the guess — see [[differential-beats-fixtures]].

**The tell that research was skipped:** a bug whose fix is one line found by reading, but which
took a build-and-test cycle to notice. `floor` versus `ceil`. Swap versus stable partition.
Flag precedence versus independent modifiers. None were discoverable by thinking harder; all
were one grep away.

**Corollary — the limit is on unverified *edits*, never on hypotheses.** Generate as many
hypotheses as the problem needs; they are the raw material. But two consecutive *code* changes that do not move the measurement mean
the model of the format is wrong, and a third edit will not find it. Go read. `RISKS.md` B13 is
where this got applied, and it lists what has been ruled out so the next attempt starts from
evidence rather than from another guess.
