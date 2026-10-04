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
the control being that the search found that. **The live TF2 client does not call it either** (read 2026-10-03,
x64). `CEngineClient::ResetDemoInterpolation` is `engine.dll` `0x180070cb0` — through the demo player global
`0x180522e30`, it asks vtable +0x20 and +0x30 and tail-jumps to +0xa0, the flag setter — and it sits at slot 124
(+0x3e0) of the `IVEngineClient` vtable at `0x180367058`, which is `cdll_int.h`'s 125th virtual: binary and header
agree. `client.dll` holds 13 call sites through any +0x3e0 slot, and every one passes an argument in `edx`/`r8`, so
none is a `void` call; the no-argument shape `mov rcx,[rip+g]; mov rax,[rcx]; call [rax+N]` occurs 57 times at
`IsPlayingDemo`'s +0x260 (the control) and never at +0x3e0. Nothing resets demo interpolation in the shipped game;
the port leaving the method uncalled is Valve's behavior. *Disassembly and a byte search of the shipped binaries.*
`SkipToTick` (`0x180073b10`) does not set it:
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
`CGameMovement` — which the port did not have, so it kept the networked `m_vecVelocity` and filed the gap (B450).
*Disassembly plus published source.* D205 ported the movement; the last piece (2026-10-03) was that prediction's
velocity fed the anim state only. `GetAbsVelocity()` is one value, and `InvisibilityThink`'s motion cloak reads it
too (`tf_player_shared.cpp:8020`), so the recorder's drawn `Velocity` is now prediction's for every reader.

## What the predicted player meets, and two things the client cannot know

Porting `CTFGameMovement`'s trace (D205, B450) turned up engine behaviour with little prior art. **A
`COLLISION_GROUP_PASSABLE_DOOR` still stops a player's movement**: `CGameRules::ShouldCollide` refuses it to
`COLLISION_GROUP_PLAYER` only (`gamerules.cpp:710`), and movement traces as `COLLISION_GROUP_PLAYER_MOVEMENT`. **A
respawn wall stops nobody whose mask lacks its team's contents** (`c_func_respawnroom.cpp:78-93`), and the mask drops
the enemy's contents while `m_isPassingThroughEnemies` is set (`tf_gamemovement.cpp:269`) — so a player stuck in an
enemy also walks through the enemy's spawn wall until he is clear. **The client's ground velocity is zero for every
brush entity**: no client table receives `m_vecVelocity` for a door or a train (`c_baseentity.cpp:438-485`,
`c_basedoor.cpp:17-19`, `c_func_tracktrain.cpp:41-42`), so `SetGroundEntity`'s add and subtract do nothing on the
client, and `CTFGameMovement::CheckStuck`'s `func_tracktrain` rescue (`:1417`) can never fire there. And two pieces of
movement state are the movement object's own and never networked — `m_isPassingThroughEnemies` and
`m_flStuckCheckTime` — so a demo cannot say what they were; the port re-derives the first from the restored origin.
*Published source; the last is interpolated.*

## The step-up is gated by a flag that reads as a ducking flag

`m_bAllowAutoMovement` looks like a ducking flag — `OnUnDuck` uses it to stop an on-ground unduck (`:3306`) — and the port
first treated it only there. **TF's `StepMove` gates the whole step-up on it too** (`tf_gamemovement.cpp:2845`): without
it a player walking into a step stops at the face, the low road alone (B459). The wrong belief was "it is always true for
a TF player", written as a comment in `WaterMove`; `WaterMove` does read it (`:1693`), and a test shows it changing
nothing under a low ceiling — but one geometry is not a proof, so the gate is ported there too. The gap was found by mutation testing, not by
playback: StepMove had no test at all, and reading the engine to write one is what surfaced the branch.
*Published source; the WaterMove equivalence is a test against a synthetic world.*
