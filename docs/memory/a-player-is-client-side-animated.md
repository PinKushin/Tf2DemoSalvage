---
name: a-player-is-client-side-animated
description: Every TF player sets m_bClientSideAnimation, so their m_flCycle is always zero and the client advances it; a viewmodel does the same by a different route.
metadata:
  type: project
---

**`CTFPlayer::CTFPlayer` calls `UseClientSideAnimation()` unconditionally** (`tf_player.cpp:953`),
so every TF player sends `m_bClientSideAnimation` — one unsigned bit in `DT_BaseAnimating`
(`baseanimating.cpp:250`). `C_BaseAnimating::UpdateClientSideAnimation`
(`c_baseanimating.cpp:5134`) then latches and calls `FrameAdvance( 0.0f )` for every member of
`g_ClientSideAnimationList` each frame.

**So a player's `m_flCycle` is never a driving value.** It decodes to zero and stays there — it is
excluded from the send table anyway. Everything that moves the model is the client's own advance:

```
float addcycle = flInterval * cyclerate * m_flPlaybackRate;    // c_baseanimating.cpp:5493
```

All three factors matter. The playback rate was missing from this project's skinned path for a long
time and only the baked vertex path multiplied by it (B281).

**A VIEWMODEL advances too, and it is a different mechanism.** `C_BaseViewModel`
(`c_baseviewmodel.cpp:197`) computes `elapsed_time * GetSequenceCycleRate(…) * GetPlaybackRate()`
unconditionally; it never joins `g_ClientSideAnimationList` and has no `m_bClientSideAnimation`. It
also clamps a finished one-shot to **0.999f** rather than to 1, which is the only place in the
engine that does.

**Both reach one gate in this project** — `SceneProp.ClientSideAnimated`, read by
`EntityModelSet.Simulate` — so anything that builds a prop must set it. **Every place that builds a
prop from scratch is a place it can be dropped**, and it was dropped twice:

- `PlayerProps.Add` built every player's prop and had no parameter for it (B280). Every player slid
  through the map in one pose, twice reported by the owner, for weeks.
- `ViewmodelScene.Build` built its three props without it (B283). No draw, reload or fire ever
  played in first person.

**Neither was visible to any test**, because every test either called the advance directly or built
its own prop with the flag already set. The assertion that catches it reads the frame the SKELETON
was handed — `EntityModelSet.FrameOf`, carried rather than recomputed — across two times.

Related: [[a-moves-regressions-are-wiring]], [[the-player-send-table-excludes-the-animation]].
