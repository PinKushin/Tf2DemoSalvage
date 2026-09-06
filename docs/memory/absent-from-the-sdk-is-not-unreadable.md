---
name: absent-from-the-sdk-is-not-unreadable
description: I filed the ragdoll solver as "ours to write" because src/vphysics is not in the SDK; the owner pointed out the decompiler exists.
metadata:
  type: feedback
---

Asked to implement ragdoll physics, I checked `F:/src/source-sdk-2013/src/vphysics`, found only the
public headers, and wrote a decision saying the integrator "is this project's own". The owner:

> "remember you have the decomp so no nothing is ours"

`vphysics.dll` ships with the game at `bin/x64/`. It is 1.4 MB and it is what Ghidra is for.

**Why:** *not in the published source* is not *not readable*. `CLAUDE.md` says the four sources are
**a menu, not a ladder**, and that a decompiler is *"a normal tool — reach for it readily"* whose
only hard rule is that its output stays outside every git tree. Filing a closed component as
own-design silently converts a parity project into an approximation, and it does it in a document
that then reads as authoritative.

The same reasoning had also let me concede that `.phy` collision hulls "must be approximated" because
Havok's format is compressed. Compressed is not unknowable — the code that reads it is in the same
binary.

**How to apply:** before writing that anything is ours to design, ask which binary implements it. If
the game ships it, decompile it. Reserve "ours" for something no shipped artefact contains at all.
Prefer published source where it holds the answer — `ragdoll_shared.cpp` is published and gives
construction and read-back — but the boundary of the SDK is not the boundary of what can be read.
Related: [[nothing-is-closed]], [[where-the-game-and-clients-live]],
[[shipped-data-settles-what-closed-code-cannot]], [[a-filed-design-choice-may-not-be-one]].
