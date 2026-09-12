---
name: the-first-step-of-a-sequence-is-not-the-culprit
description: A crash log naming the first item in a teardown or startup sequence names the ordering, not the cause; skip exactly one to find out.
metadata:
  type: project
---

**When a process dies partway through a sequence, the member named in the last log line is the one
that ran first — which is not evidence that it is the guilty one.** B402, 2026-09-12: six CI runs
ended on `shutdown: releasing viewport`, and six agreeing runs felt like proof. They agreed because
the viewport was disposed first. Skipping only the viewport moved the crash forward to
`releasing transport` on one runner and `releasing actions` on another.

**The experiment that settles it changes exactly one variable.** Skip the accused member, keep
everything else. Three outcomes, all informative:

- the crash disappears — the accusation stands;
- the crash moves to a different member, and **moves to different members on different runs** — the
  fault is in the sequence itself, shared by every member;
- the crash stays where it was — the marker is lying about where it is.

**Skipping all of them at once cannot distinguish the first case from the second**, and that is the
tempting shortcut, because it is the same edit as the fix you already believe in. It would have
produced a green run and a wrong write-up.

**How to apply:** before writing a mechanism for "member X kills the process", ask what X's position
in the order is. If X is first, the log has told you the order and nothing else. The actual cause in
B402 was double disposal — `Form.Dispose` walks `Controls` and disposes every child, so our override
disposing the same six first tore each down twice — a mechanism that belongs to all six equally and
that no single-member story could have reached.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[ask-which-input-differs-before-bisecting]],
[[ci-is-the-machine-without-tf2]].
