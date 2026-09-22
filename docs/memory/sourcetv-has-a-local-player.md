---
name: sourcetv-has-a-local-player
description: "A SourceTV demo's local player is the SourceTV client's own CTFPlayer (spectator); pLocalPlayer guards do NOT fall through on STV"
metadata: 
  node_type: memory
  type: project
  originSessionId: 168f3a6d-d771-45c7-958f-2de3d0ee4765
  modified: 2026-09-21T10:42:19.906Z
---

On a SourceTV (STV) recording, `C_BasePlayer::GetLocalPlayer()` is NOT null. It is the SourceTV client's own `CTFPlayer`
at entity `svc_ServerInfo.PlayerSlot + 1`, with `m_iTeamNum 1` (spectator) and `m_iClass 0`. Measured on
`demostf-cp_process_f12-2026-08-07` and `tf2-2013-build1729296-stv-cp_foundry`. From source: `C_BasePlayer::IsHLTV()`
is `IsLocalPlayer() && engine->IsHLTV()` (`c_baseplayer.cpp:527`).

**Why:** the project's prose said "SourceTV has no local player" in several places. On 2026-09-20 that claim was
repeated to the owner as "an STV demo plays the stock explosion sound for a Black Box", which was false. It was
corrected on 2026-09-21, when `ParticleTracerCallback`'s `if (!player) return;` made the question matter.

**How to apply:** for any engine guard of the form `pLocalPlayer &&` or `if (!player) return`, treat STV as passing it,
with team 1. What STV lacks is a recorded VIEW (`democmdinfo_t` is zeroed, `HasRecordedView` is false), not a local
player. Do not confuse the two. Related: [[a-sentinel-conflates-unknown-with-answer]], [[instrument-bugs-outnumber-decoder-bugs]].
