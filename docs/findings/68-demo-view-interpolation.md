# 68 — A POV demo's view is interpolated by the demo player

**B56 and B442.** A point-of-view demo records one `democmdinfo_t` per packet (`demoformat.h:112-144`): the view
origin, the view angles and the local view angles, each with a second copy selected by `FDEMO_USE_ORIGIN2` /
`FDEMO_USE_ANGLES2`. The viewer drew the last packet's view, so the camera stepped once per tick. The game does
not. Everything below is read from `engine.dll` x64 (Ghidra project `tf2enginex64`) unless marked otherwise.

## Where it runs

`SCR_UpdateScreen` calls `CDemoPlayer::InterpolateViewpoint` (`0x180072180`) every rendered frame, through the
`demoplayer` global, before `FRAME_RENDER_START`. Its output goes to the client's prediction:
`SetViewOrigin` is the local player's origin (`prediction.cpp:1837-1847`), `SetViewAngles` the camera, and
`SetLocalViewAngles` is `pl.v_angle`, which `C_TFPlayer::UpdateClientSideAnimation` animates the local player from
(`c_tf_player.cpp:4279-4284`, `7280-7290`; `baseplayer_shared.cpp:303-321`). *Published source.*

So one call serves three consumers: the camera, the recorder's drawn origin, and the recorder's eye angles. The port
keeps one `DemoPlayer` per open demo, shared by `TimelineEyes` and `TimelineMoments`.

## The parse-ahead window

`ReadPacket` (`0x180072ee0`) holds back `dem_packet`, `dem_consolecmd` and `dem_usercmd` until their tick; the
other commands run immediately (mask `0x1ca`). Every signon or packet command reads its `democmdinfo_t` and sets
`m_bInterpolateView` from `ParseAheadForInterval(tick, 8)` (`0x180072af0`).

`ParseAheadForInterval` drops entries older than `tick - 32`, then reads forward. `dem_synctick` (3) or `dem_stop`
(7, also returned at end of file by `ReadCmdHeader` `0x1800be370`) ends the scan with "do not interpolate".
Commands 4, 5, 6 and 8 are skipped. Every other command adds a packet; a backward tick clears the list. The scan
stops with "interpolate" after the first packet more than 8 ticks ahead.

**Departure from the brief, from the bytes:** signon packets also parse ahead, not only `dem_packet`.

## The pair and the fraction

The target is the playback tick, rolled back when `maxclients == 1`: by 1, or with `demo_legacy_rollback` by
`1 + (int)(interp / TICK_INTERVAL + 0.5)`. `FUN_180071fd0` picks `prev.tick <= target < next.tick` from the list
plus a copy of the current info at tick -1. If an `FDEMO_NOINTERP` packet lies in `(lastTarget, target]`, it
takes the pair ending at that packet.

`dem_synctick` and `StartPlayback` (`0x180073bd0`) set `lastTarget` to `host_tickcount`. The port uses
`int.MaxValue`, which makes the cut window empty in the same way. **Departure from the brief.**

The fraction is `((target - prev) * TI + remainder) / ((next - prev) * TI)`, clamped by `MAXSS(0)` then
`MINSS(1)`: NaN goes to 0, +infinity to 1. *Disassembly.*

## When it snaps

The current info is used as-is when `demo_interpolateview` is 0, the window said no, or the reset flag
(`+0x63d`) is set. With `dt > 0` it also snaps when the origin speed exceeds `demo_interplimit` (4000), or the
largest local-angle speed, `AngleNormalize(AngleNormalizePositive(next) - AngleNormalizePositive(prev)) / dt`,
exceeds `demo_avellimit` (2000). Both comparisons are strict. Otherwise the origin lerps and both angle sets go
through `AngleQuaternion`, `QuaternionSlerp` and `QuaternionAngles` (`mathlib_base.cpp:1605-1658`).

The ConVars' defaults are from their registrations (`0x180003bea`, `0x180003baa`, `0x180003a2a`, `0x180003c2a`),
all flags 0, and agree with `cvarlist.log`.

## A seek is not a reset

`ResetDemoInterpolation` (`0x180073990`) sets the flag. Nothing in `engine.dll` calls it, and the published client
does not either: a text search over source-sdk-2013 finds only its `IVEngineClient` declaration (`cdll_int.h:522`),
the control being that the search found that. The live TF2 client binary is unread. `SkipToTick` (`0x180073b10`) does not set it:
a backward skip reloads the demo through `StartPlayback`, which does not clear the list or the flag.
**Departure from the brief**, which asked seeks to reset: the port treats a backward move as a restart and leaves
the flag alone. What remains is B450.

## The recorder's feet turn per frame

`UpdateClientSideAnimation` feeds the local anim state `EyeAngles()` (`c_tf_player.cpp:4279-4284`) every frame,
and `ComputePoseParam_AimYaw` converges the feet by `gpGlobals->frametime` (`multiplayer_animstate.cpp:1759`)
before drawing the body at them (`:1765`) and twisting against them (`:1768-1772`). The port had the twist on the
interpolated local yaw but the feet on the timeline's per-tick advance from the server's `m_angEyeAngles`, so the
twist lagged up to a tick. `RecorderFeet` now converges them per frame from the viewpoint's local yaw. *Published
source.*

## Prediction runs during playback — a wrong belief

B450 first filed "whether demo playback runs prediction at all for the recorder" as unread, and the easy reading
was that it does not: a demo has no server to predict against. The bytes say otherwise. `CL_RunPrediction`
(`0x180092710`) calls `IPrediction::Update` at full signon whenever the delta tick is valid, skipping only while the
demo player is skipping or seeking (its vtable +0x48, B56). Playback hands the client each `dem_usercmd`, and
`CPrediction::_Update` stops only for `cl_predict 0` (`prediction.cpp:1742-1799`) before `PerformPrediction`
re-runs those commands (`:1570-1698`). So the recorder's velocity in the game is prediction's, re-simulated by
`CGameMovement` — which the port does not have, so it keeps the networked `m_vecVelocity` and files the gap (B450).
*Disassembly plus published source.*
