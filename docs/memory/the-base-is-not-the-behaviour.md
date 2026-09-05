---
name: the-base-is-not-the-behaviour
description: Reading an engine function to its closing brace tells you nothing about the overrides that run after it — and some overrides are dead while others are the whole feature.
metadata:
  type: project
---

**Read the override list before concluding what a virtual does.** `StandardBlendingRules` has seven
live overrides; everything the minigun does happens in one of them, *after* `BaseClass::` returns.
Reading the base to its closing brace and stopping would have missed B347 entirely — a barrel bone
that TF2 spins procedurally and this project left still.

```
grep -rn "::TheVirtual" src            # every definition, base and overrides
grep -rn "BaseClass::TheVirtual" src   # which of them chain
```

**Some overrides are DEAD, and that is equally worth measuring**, because they look like features:

- `C_AI_BaseHumanoid::StandardBlendingRules` — the whole file is `#if 0`. Not in a built game.
- `C_BaseFlex`'s body is entirely inside `#ifdef HL2_CLIENT_DLL`. Nothing for TF2.
- `ChildLayerBlend` is CALLED unconditionally and its body opens with a bare `return;`
  (`c_baseanimating.cpp:1909`). Quoting the call site and not the body would produce a whole
  child-merge pass the engine never runs.

**Why:** a base implementation, an `#if 0`, an `#ifdef` for another game, and an early `return` all
read as "this is what happens" from the wrong vantage point. Implementing a dead override is worse
than missing a live one — it adds behaviour the engine does not have, and nothing will ever
contradict it.

**How to apply:** for any virtual you are reproducing, list the overrides, check each for `#if 0`,
`#ifdef <OTHERGAME>`, and a leading `return;`, and say in the finding which ones are live. A row
saying "dead, implementing it would be implementing nothing" is a real answer and stops the question
being asked again.

Related: [[unreachable-can-be-proved-not-just-observed]], [[a-guard-you-remove-may-be-the-mechanism]],
[[parity-is-the-search-not-the-defence]], [[nothing-is-closed]].
