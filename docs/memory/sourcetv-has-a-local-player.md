---
name: sourcetv-has-a-local-player
description: "A SourceTV demo's local player is the SourceTV client's own CTFPlayer (spectator); pLocalPlayer guards do NOT fall through on STV"
metadata: 
  node_type: memory
  type: project
  originSessionId: 168f3a6d-d771-45c7-958f-2de3d0ee4765
  modified: 2026-09-21T10:42:19.906Z
---

On a SourceTV recording, `GetLocalPlayer()` is NOT null — it's the SourceTV client's own `CTFPlayer`
(team 1/spectator, class 0). Measured on two demos; confirmed from source (`C_BasePlayer::IsHLTV()`,
`c_baseplayer.cpp:527`, requires
`IsLocalPlayer()`).

**Why:** the project's prose said "SourceTV has no local player" in several places, repeated to the
owner as a false claim about a stock sound, corrected when a `if (!player) return;` guard made the
question matter.

**How to apply:** for any engine guard of the form `pLocalPlayer &&`, treat STV as PASSING it, team 1.
What STV lacks is a recorded VIEW (`democmdinfo_t` zeroed), not a local player — don't confuse the
two. Related: [[sentinels-conflate-unknown-with-answer]], [[instrument-bugs-outnumber-decoder-bugs]].
