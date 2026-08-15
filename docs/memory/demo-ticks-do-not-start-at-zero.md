---
name: demo-ticks-do-not-start-at-zero
description: "A demo's first tick is whatever the server was on, so a hardcoded probe tick can walk zero commands and still report data."
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-14T17:54:32.668Z
---

**A demos.tf recording starts at an arbitrary server tick, not at zero or near it.** On
`demostf-cp_process_f12-2026-08-07.dem` the first packet command is already past 20000.

**Why:** a probe written to stop at "tick 20000" walked **0 of 106226 commands** and reported an
empty world with no error. Worse, the probe before it called `PropsAt(20000)` on the same file and
got back **197 props**, one of them a weapon — a completely plausible answer to a tick the demo does
not contain. That number drove a wrong conclusion for a whole round of work.

**How to apply:** derive the tick from the file — `first + (last - first) / 2` over the packet
commands — and print how many commands were actually walked alongside any per-tick count. A count
without a walked-commands number cannot distinguish "few of these exist" from "I looked nowhere".

Same family as [[instrument-bugs-outnumber-decoder-bugs]] and
[[measure-the-output-not-the-capability]]: the tool answered confidently for a question it was never
pointed at.
