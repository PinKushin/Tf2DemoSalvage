---
name: a-deck-is-dealt-at-play-time
description: "Engine state shared across load-time and live producers (B503's rndwave deck) is resolved where both pass — the presenter — at play time; a seek redeals from the demo"
metadata:
  node_type: memory
  type: project
---
**When engine state is shared by sounds we build at load AND sounds we emit live, resolve it where both pass, at
play time.** B503's per-script wave deck: explosions/server impacts/medigun are scheduled at load; client impacts,
whiz, HUD, animation events are live. A wave baked at load goes stale the moment a live emission of the same script
lands earlier. So each `SceneSound` carries a `ScriptWaveDraw` (script, the generator's RAW number, `isbeingemitted`)
and `SoundPresenter` deals it against one `ScriptWaveDeck` in tick order.

**Why the raw number works:** `UniformRandomStream.RandomInt` is a plain modulo that always consumes one number, so
`RandomInt( 0, int.MaxValue - 1 )` returns the raw value and `raw % count` is the engine's pick over whatever count
is available later — the soundlevel draw after it is unaffected.

**How to apply:** a seek does what `demo_gototick` does, NOT "play to T" (B504, corrected same day): deck never
rewound (flags live in soundemittersystem.dll); skip deals only RELIABLE temp entities' sounds (FUN_1801f9bc0 drops
unreliable ones while skipping) — f12: 0. Within a tick: `OnRenderStart` order + temp-entity stream place (B505,
`ClientSoundOrder`). A stop/change names its start's dealt wave, never a fresh pick. Every wave of a dealt script is
precached (`InternalPrecacheWaves`).

**Why the correction matters:** "seek == play to T" is a viewer's wish; the engine has no seek. Read the engine's skip
before designing seek semantics for engine state.
