# 115. The interpolation amount is asked, not kept (B848, B849, B860)

How the client's interpolation delay follows the server's replicated bounds during a demo. Evidence classes: **read** is
the Source SDK 2013 client source; **interpolated** is flagged where it appears. Nothing here was measured in TF2.

## The wrong model this replaced

The viewer computed one delay for the whole recording, from the server's ConVars after the last `net_SetConVar`, and
every track sampled that far behind. A server that raised or lowered its interp-ratio bounds mid-recording played the
whole demo under its final ones.

## What the client does

`GetClientInterpAmount()` (`cdll_bounded_cvars.cpp:127-134`, **read**) is computed every time it is asked: the larger of
`cl_interp` and `cl_interp_ratio / cl_updaterate`. The clamping is the bounded ConVars' own: `cl_interp` and
`cl_interp_ratio` are `ConVar_ServerBounded` wrappers in that file that read the replicated
`sv_client_min_interp_ratio`/`_max_` when asked (**read**); `cl_updaterate` is only cast to one (`:133`), and its clamp by
`sv_minupdaterate`/`sv_maxupdaterate` is the engine's (disassembled, `engine.dll` `0x180164230`, recorded in
`ClientInterpTests`). The client applies each `net_SetConVar` as its packet is read (B450, engine side, **interpolated** there), so
the amount at a tick is the one under that tick's bounds. `GetInterpolationAmount` rounds it to ticks and adds
`serverTickMultiple` (`c_baseentity.cpp:5937`, **read**): one tick, two on a server simulating on alternate ticks
(`:5919-5923`). A predictable or client-created entity takes `TICK_INTERVAL × serverTickMultiple` instead, whatever the
amount (`:5925-5928`). That `:5937` path is the one for multiplayer and for a demo that is NOT locally recorded
(`!engine->IsPlayingDemoALocallyRecordedDemo()`, `:5932-5935`); a locally recorded demo falls through to `:5940-5960`,
where an entity animated and simulated every tick (or latched for the one it is) takes `TICK_INTERVAL ×` the tick
multiple, plus `g_nThreadModeTicks` when the engine is threaded, and only the rest take the amount (**read**). What makes a
demo "locally recorded" is the engine's (`IsPlayingDemoALocallyRecordedDemo`, not read).

Each interpolated variable keeps its OWN amount (`SetInterpolationAmount` only stores it, `interpolatedvar.h:608-611`,
**read**). `C_BaseEntity::CheckCLInterpChanged` (`c_baseentity.cpp:6443-6465`, **read**), called at `FRAME_START`
(`cdll_client_int.cpp:2344`, **read**), notices `GetClientInterpAmount()` or `cl_interp_npcs` changing and sets every
entity's variables to the new amount (`Interp_UpdateInterpolationAmounts`, `:837-849`). Neither puts an entity back on
the interpolation list, so one that had settled holds its value until its next update (**read**: no re-listing in
either; that nothing else re-lists it on an amount change is **interpolated**). A variable therefore takes a new amount
at the next frame start, not when the packet is read; where that falls against the packet read is engine code, not read.
And `Interpolate` prunes each history by the amount it was run with (`interpolatedvar.h:1057`, **read**), so after a rise
the entries the new, longer delay would reach may already be gone.

Temp entities fire `GetClientInterpAmount()` after they arrive during demo playback (`CL_QueueEvent`, `engine.dll`
`0x1801f9bc0`, B415, disassembled), under the bounds of the tick they arrive on; they ask it directly, so they switch at
once, where the variables switch at the next frame start.

## What the viewer does now

`InterpolationSchedule` is the amount by tick, built from the same ConVar history `ServerConVarsAt` reads; tracks, the
demo player's rollback and the faces read it at their own tick, and the checkpoint walk keeps enough history for the
widest delay anywhere in the recording (B849). A falling delay should match the engine, to within the frame-start lag
(**interpolated** from the code above; no test predicts an engine value for it). A rising one does not yet: the sampler
re-blends a settled entity and reads history the engine pruned (B860, open).
