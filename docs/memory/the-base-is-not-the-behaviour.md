---
name: the-base-is-not-the-behaviour
description: Reading an engine function to its closing brace tells you nothing about the overrides that run after it — and some overrides are dead while others are the whole feature.
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:55:05.684Z
---

**Read the override list before concluding what a virtual does.** A virtual method with seven live
overrides did all of its actual work AFTER the base returned — reading only the base to its closing
brace missed a procedurally-spun barrel bone entirely.

```
grep -rn "::TheVirtual" src            # every definition, base and overrides
grep -rn "BaseClass::TheVirtual" src   # which chain
```

**Some overrides are DEAD, equally worth measuring, and look like features:** a whole file behind
`#if 0`; a body entirely inside `#ifdef` for another game; a function called unconditionally whose
body opens with a bare `return;`. Implementing a dead override is worse than missing a live one — it
adds behaviour the engine doesn't have, and nothing will ever contradict it.

**How to apply:** for any virtual being reproduced, list overrides, check each for `#if 0`, `#ifdef
<OTHERGAME>`, a leading `return;`, and say which are live. "Dead, implementing it is implementing
nothing" is a real answer that stops the question being re-asked.

Related: [[most-of-a-decoder-is-untested]], [[a-guard-you-remove-may-be-the-mechanism]],
[[parity-is-the-search-not-the-defence]], [[nothing-is-closed]].
