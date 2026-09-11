---
name: valve-shape-beyond-parity
description: "Where a feature goes beyond TF2 and there is no engine behavior to match, still build it in Valve's shape (mechanisms, order, names) so the code keeps looking like Valve's; D163"
metadata:
  type: feedback
---

**A feature beyond parity is still built the way Valve would have built it.** D163, the owner, on
fetching old maps and models (D162): *"even those areas need to take vavles conventions into account and
probably follow them so any ai working on it stays looking like valve code"*.

**Why:** the owner wants every part of the codebase to read as Valve's shape, so whoever works on it
next, AI included, keeps following Valve's patterns rather than inventing parallel ones.

**How to apply:** when there is nothing to be parity WITH, find the nearest engine mechanism and use its
shape. Old content goes in as a search path (`IFileSystem::AddSearchPath`, `gameinfo.txt` order), not a
separate loader. Downloads follow the engine's download queue (B392). Checks are named after the
client's own checks. Read the SDK or the disassembly for the mechanism first, exactly as for parity
work. Related: [[valve-parity-is-the-first-principle]], [[parity-is-the-search-not-the-defence]].
