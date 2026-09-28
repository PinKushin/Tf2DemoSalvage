---
name: demo-ticks-do-not-start-at-zero
description: "A demo's first tick is whatever the server was on, so a hardcoded probe tick can walk zero commands and still report data."
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-14T17:54:32.668Z
---

**A demos.tf recording starts at an arbitrary server tick, not zero.** A probe stopped at "tick
20000" walked **0 of 106226 commands** on a file whose first packet command is already past 20000,
reporting an empty world with no error. Worse, a prior probe called `PropsAt(20000)` on the same file
and got back **197 props** — a plausible answer to a tick the demo doesn't contain, driving a wrong
conclusion for a whole round of work.

**How to apply:** derive the tick from the file (`first + (last - first) / 2`) and print how many
commands were actually walked alongside any per-tick count — a count without a walked-commands number
can't distinguish "few exist" from "I looked nowhere". Same family as
[[instrument-bugs-outnumber-decoder-bugs]], [[measure-the-output-not-the-capability]].
