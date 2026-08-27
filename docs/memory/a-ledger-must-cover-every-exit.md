---
name: a-ledger-must-cover-every-exit
description: A drop-counter wired into two of three skips reported "every dropped face is a tool material" while blind to the one rule that discards geometry by position; a count of the input cannot see a cull.
metadata:
  type: project
---

**A ledger that misses one exit reports a clean bill of health, and that is worse than no ledger.**
Hunting missing geometry, a counter was added to the world build to name every dropped face by
reason and material. It was wired into the visibility skip and the tool-material skip and **not**
into the play-area skip — the only rule that discards geometry by POSITION, which is what was being
looked for. It reported "1,556 faces dropped, every one a tool material", which reads as proof that
nothing structural is being culled, and the search moved elsewhere for several hours.

**A second instrument in the same hunt measured its input instead of its output.** The world log
reported `props.Count / 3` prop triangles — the number handed to `AppendProps`, not the number
`AppendProps` appended. Removing a cull therefore could not move that figure, and did not, and the
figure was never wrong; it simply was not measuring the thing it was being read for. The brush-face
count beside it moved by exactly the 133 the ledger predicted, which made the pair look like
corroboration.

**Three instruments were wrong in one session** — those two plus a category view whose white was
read first as "uncoloured surface" and then as "the sign", when it meant overlays.

**How to apply:** when adding a counter to a loop with several `continue` paths, enumerate the exits
first and cover all of them, or state in the log which are counted. Count on the way OUT, never on
the way in: a total taken before the filters cannot observe a filter. And when a ledger reports that
a whole category is empty, check whether it can see that category at all before believing it — an
absence produced by not looking is identical to an absence produced by nothing being there.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[an-empty-search-needs-a-control]],
[[measure-the-output-not-the-capability]], [[logs-are-the-debugger]],
[[instrument-bugs-outnumber-decoder-bugs]].
