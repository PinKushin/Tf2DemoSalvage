---
name: nothing-is-closed
description: Never write that something is closed, unpublished or unknowable; check the SDK, the shipped data and a decompiler first, because the claim has been wrong every time it was made here.
metadata:
  type: feedback
---

**Owner's rule, stated 2026-08-17: nothing should ever be described as closed and unavailable. It
is never unavailable.**

**Why:** every time this project has written that something is unknowable, the claim was false and it
cost a real defect. It is never a neutral note — it is a instruction to future readers to stop
looking, and they do.

Three, all found by going and checking:

- `BspAmbientLight.Nearest` carried *"The engine blends a leaf's samples in `LightcacheGetDynamic`,
  which is in the closed engine — the weighting is not in `source-sdk-2013` and cannot be
  transcribed."* The function is `Mod_LeafAmbientColorAtPos`, published in
  `utils/vrad/leaf_ambient_lighting.cpp`, and it is an inverse-squared-distance weighted average.
  Nobody had looked. It made one capture point on cp_process draw at 0.10 while its mirror image on
  a symmetric map drew at 0.39.
- *"TF2 is closed"* was written in three places and checked in none. 1,318 files of TF2 game code,
  including the HUD and the econ schema, are in the SDK — see [[tf2-game-code-is-in-the-sdk]].
- `$modblend` was filed as needing a decompiler. It is declared in three shipped VMTs and read by a
  commented-out proxy — see [[shipped-data-is-a-source]].
- **The sound mixer is genuinely closed and its cvar's MEANING still was not** (2026-08-22). The
  claim in hand was that `snd_mixahead` is how far ahead the mixer renders. Half right, and the
  useful half was missing: `game/server/sceneentity.cpp` reads it through a function called
  `GetSoundSystemLatency()`, with `SOUND_SYSTEM_LATENCY_DEFAULT (0.1f)` as the fallback, to align
  lipsync with speech that will not be heard for 100 ms. It is a fixed pipeline DELAY the engine
  schedules around, not a target and not a clamp.

**That last one adds a case the rule did not cover: a closed component's behaviour can be documented
by published code that merely CONSUMES it.** `snd_dma.cpp` is not in the SDK and never will be, so
"the mixer is closed" was true — and answering the actual question needed no decompiler at all,
because a game-side caller had to reason about the engine's latency and wrote down what it is. When
the implementation is closed, grep for its CALLERS.

**How to apply:** before writing that anything is unavailable, check in this order and say which one
you checked — `source-sdk-2013` (including `utils/`, which holds the compilers and is where the
lighting answers live), the game's own shipped data, then a decompiler, which the owner considers a
normal tool to reach for readily. If after that it is genuinely not in hand, write down *what was
searched* rather than *that it cannot be known*.

**The compounding danger is a defensible-sounding substitute.** The nearest-sample comment did not
merely admit ignorance; it argued that nearest was "a decision this project can defend" and that a
blend would be "a guess wearing parity's clothes". That reasoning is what kept it in place. Worse,
nearest is not a coarse approximation of the real answer: vrad's `CompressAmbientSampleList` deletes
every sample the blend can already predict, so the stored set only reconstructs the original lighting
when interpolated, and taking the nearest reads back an arbitrary survivor of that thinning.

Related: [[an-empty-search-needs-a-control]] — an absence claim needs a positive control in the same
sweep, and "it is closed" is an absence claim about a search nobody ran.
