---
name: tf2-game-code-is-in-the-sdk
description: source-sdk-2013 ships TF2's own game code — 1,318 files; the belief that it is closed was wrong and deferred real work.
metadata:
  type: project
---

**`source-sdk-2013` carries TF2's game code.** 1,318 files across `src/game/shared/tf`,
`src/game/client/tf` and `src/game/server/tf`. Verified 2026-08-16.

That includes, concretely:

- **All 125 HUD sources**, `tf_hud_deathnotice.cpp` (the kill feed) among them.
- **`tf_shareddefs.h`** — the full `TF_COND_*` player-condition enumeration with values
  (`TF_COND_INVULNERABLE = 5`, `TF_COND_BURNING = 22`, …), and `TF_CLASS_UNDEFINED` /
  `TF_FIRST_NORMAL_CLASS` at lines 205 and 198.
- **`c_tf_player.cpp:395,398`** — the übercharge material names, `models/effects/invulnfx_blue.vmt`
  and `invulnfx_red.vmt`.
- **`game/shared/econ/`**, 55 files: the item schema, the attribute system, `CEconStyleInfo`,
  paint-kit definitions, per-team attached models.

**This project had recorded the opposite in three separate places** — `docs/CONFORMANCE.md`, the
client-system conformance batch and the entity batch — and none of them had checked. One of them
named a decompiler as the next step for a constant that is an ordinary `#define`.

**The mechanism of the error is the part worth carrying forward.** The search looked in
`client/replay/`, found a reference with no definition, and concluded the definition existed nowhere.
**An absence found by a search is a fact about the search.** Third instance of that shape in this
project — see also the level-name filter in `docs/findings/24-reference-capture.md` and the "Econ"
substring count that briefly reported 405 matches inside words like "second".

**The cost is invisible, which is why it lasted.** Nothing was blocked; work was just deferred as
expensive when it was cheap. Anything else previously waved off with "TF2 is closed" is worth
reopening — the item system, the HUD and the material overrides all were, and are now specified.

Related: [[closed-source-check-the-public-api]], [[an-uncoverable-gap-is-usually-your-reader]],
[[read-the-spec-before-measuring-our-data]].
