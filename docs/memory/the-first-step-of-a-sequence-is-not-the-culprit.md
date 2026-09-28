---
name: the-first-step-of-a-sequence-is-not-the-culprit
description: A crash log naming the first item in a teardown or startup sequence names the ordering, not the cause; skip exactly one to find out.
metadata:
  type: project
---

**When a process dies partway through a sequence, the member named in the last log line is the one
that ran FIRST — not evidence it's guilty.** B402, 2026-09-12: six CI runs ended on
`shutdown: releasing viewport`, which felt like proof; they agreed because the viewport was disposed
first. Skipping only it moved the crash forward to `releasing transport` on one runner and
`releasing actions` on another.

**The experiment that settles it changes exactly ONE variable.** Skip the accused member, keep
everything else:
- Crash disappears → the accusation stands.
- Crash MOVES to different members on different runs → the fault is in the sequence itself, shared
  by every member.
- Crash stays where it was → the marker is lying about where it is.

**Skipping all of them at once can't distinguish the first case from the second**, and that's the
tempting shortcut — it's the same edit as the fix you already believe in, and would produce a green
run with a wrong write-up.

**How to apply:** before writing "member X kills the process", ask what X's position in the order is.
The actual cause in B402 was double disposal — `Form.Dispose` walks `Controls` and disposes every
child, so an override disposing the same six first tore each down twice, a mechanism belonging to ALL
of them equally, unreachable by any single-member story.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[ask-which-input-differs-before-bisecting]],
[[ci-is-the-machine-without-tf2]].
