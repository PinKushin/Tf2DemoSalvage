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
lands earlier. So each `SceneSound` carries a `ScriptWaveDraw` (script, `isbeingemitted`, the stream's position)
and `SoundPresenter` deals it against one `ScriptWaveDeck` in tick order.

**The raw number was WRONG (B782, 2026-11-20).** vstdlib's `RandomInt` takes no number for a span of one and redraws a
refused one, so how many numbers the wave takes depends on the play-time deck. A sound now carries its stream's
POSITION (`RandomStreamPosition`: seed, numbers made) and `ScriptWaveDeck.Deal` makes all of `GetParametersForSound`'s
draws at play time (volume, pitch, pick, soundlevel — one call in FUN_180003370). Two sounds on one stream chain
(`ScriptWaveDraw.After`); a patch's volume is `Scaled`. Lesson: carry the generator's state, never a number you
assume it would have drawn.

**How to apply:** a seek does what `demo_gototick` does, NOT "play to T" (B504, corrected same day): deck never
rewound (flags live in soundemittersystem.dll); skip deals only RELIABLE temp entities' sounds (FUN_1801f9bc0 drops
unreliable ones while skipping) — f12: 0. Within a tick: `OnRenderStart` order + temp-entity stream place (B505,
`ClientSoundOrder`). A stop/change names its start's dealt wave, never a fresh pick. Every wave of a dealt script is
precached (`InternalPrecacheWaves`).

**Why the correction matters:** "seek == play to T" is a viewer's wish; the engine has no seek. Read the engine's skip
before designing seek semantics for engine state.
