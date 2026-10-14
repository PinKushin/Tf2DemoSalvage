---
name: a-paused-frame-walks-no-animation-event
description: "The engine walks animation events only when gpGlobals->frametime is non-zero: never on a paused frame, always on a skip's frame. Two runs that disagree about events at a paused tick differ in where the skip's frames fell (B541)."
metadata:
  type: project
---

**`C_BaseAnimating::Simulate` calls `DoAnimationEvents` only `if ( gpGlobals->frametime != 0.0f )`**
(`c_baseanimating.cpp:5162`). The client's frametime is zero while the demo is paused and NOT while it skips:
`CDemoPlayer::IsPlaybackPaused` answers false for the length of a skip whatever the pause flag says (engine.dll
FUN_1800729f0). So a seek made while paused walks events on its skip frames and none on the frame it lands on, and the
first playing frame walks from where the last skip frame left `m_flPrevEventCycle`. `EntityModelSet.Paused` is that
gate; `FramePresenter.ShowMomentOver` sets it from the frame's demo seconds and `ReplaySkip` clears it for its frames.

**Why:** B541 was filed as "full screen during a load LOSES the opening tick's footsteps". The run with footsteps was
the divergence. The viewer walked on every pose, so the paused opening frame fired whatever lay between the skip's
last frame and tick 20000 — 96 ticks in one run (skip read from tick 6, last frame 19904), 2 ticks in the other (an
early frame had shown tick 0, so the skip read from tick 1, last frame 19998). Both numbers are in the two logs'
`event walk` lines; nothing was lost.

**How to apply:**

- A sound at a PAUSED tick that comes from an animation event is a bug, whichever run has it.
- When two runs differ in what a seek's landing frame fired, compare `skip: to N from …` first: "from the start" and
  "from 0" put the 99-packet batches in different places.
- A UI control of the form "X sounded at the paused opening tick" is true only by this bug. Play, then pause.
- **Still open:** the decal replay advances the client's cycles (`_clientCycles`) without walking, so the cycle the
  first playing frame starts from is the replay's integration, not the skip's closed form; and
  `NotifyShouldTransmit( START )` setting `m_nEventSequence` is not ported (finding 39).
