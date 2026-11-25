# 115. The interpolation amount is asked, not kept (B848, B849, B860)

How the client's interpolation delay follows the server's replicated bounds during a demo. Evidence classes: **read** is
the Source SDK 2013 client source; **interpolated** is flagged where it appears. Nothing here was measured in TF2.

## The wrong model this replaced

The viewer computed one delay for the whole recording, from the server's ConVars after the last `net_SetConVar`, and
every track sampled that far behind. A server that raised or lowered its interp-ratio bounds mid-recording played the
whole demo under its final ones.

## What the client does

`GetClientInterpAmount()` (`cdll_bounded_cvars.cpp:126`, **read**) is computed every time it is asked: the larger of
`cl_interp` and `cl_interp_ratio / cl_updaterate`, the ratio clamped by the replicated `sv_client_min_interp_ratio` and
`sv_client_max_interp_ratio` and the rate by `sv_minupdaterate`/`sv_maxupdaterate`, as the client has them then. The
client applies each `net_SetConVar` as its packet is read (B450), so the amount at a tick is the one under that tick's
bounds. `GetInterpolationAmount` rounds it to ticks and adds one (`c_baseentity.cpp:5920`, B267).

Each interpolated variable keeps its OWN amount. `C_BaseEntity::CheckCLInterpChanged` (`c_baseentity.cpp:6443-6465`,
**read**) notices the amount change and sets every entity's variables to the new one
(`Interp_UpdateInterpolationAmounts`, `:837-849`). It does not put an entity back on the interpolation list: one that
had settled holds its value until its next update. And `Interpolate` prunes each history by the amount it was run with
(`interpolatedvar.h:1057`), so after a rise the entries the new, longer delay would reach may already be gone.

Temp entities fire `GetClientInterpAmount()` after they arrive during demo playback (`CL_QueueEvent`, B415), under the
bounds of the tick they arrive on.

## What the viewer does now

`InterpolationSchedule` is the amount by tick, built from the same ConVar history `ServerConVarsAt` reads; tracks, the
demo player's rollback and the faces read it at their own tick, and the checkpoint walk keeps enough history for the
widest delay anywhere in the recording (B849). A falling delay matches the engine. A rising one does not yet: the
sampler re-blends a settled entity and reads history the engine pruned (B860, open).
