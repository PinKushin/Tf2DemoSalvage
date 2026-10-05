---
name: rewind-is-ours-parity-first
description: Rewind is a better-than-Valve feature (TF2 restarts and fast-forwards); B503/B504 parity made it replay from tick 0 - restore instant rewind with checkpoints after parity, identical output.
metadata:
  type: project
---

TF2 has no rewind: a skip back reloads the demo and fast-forwards. Our viewer rewound instantly because most
state is preloaded. B504 (2026-10-05) made a seek replay the engine's skip frames (HUD, wave deck), so a skip back
now replays from tick 0 - a hitch the owner accepts for now.

Owner, 2026-10-05: *"we get 100% parity, then we worry about optimizing for out better than valve choices"*; *"i
meant the reqind is better than valve, becaiuse it is"*; *"before the hud work and this latest work we could go
backwards completely fine because wwe preload most things"*.

**How to apply:** after parity, checkpoint HUD + wave-deck (+ any skip-replayed state) so a rewind resumes from
the nearest checkpoint with output identical to the full replay. Forward skip frames must cost what the engine's do
(it runs 1000+ fps), not 6 ms. See [[valve-parity-is-the-first-principle]], [[a-deck-is-dealt-at-play-time]].
