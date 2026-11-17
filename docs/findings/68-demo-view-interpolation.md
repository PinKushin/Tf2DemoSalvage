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

## Added above it: `demo_retimeview` (D238)

Everything above is the engine's, and it is what `demo_retimeview 0` plays. The default, 1, is this project's. It pairs
the recorded ORIGIN on each packet's `net_Tick` rather than on the stamp, because a POV packet's stamp is the client's
arrival time and jitters (finding 106). The angles keep the engine's pair. So does the origin whenever any queued
`FDEMO_NOINTERP` packet lies between the outermost ends of its pair and the engine's, which covers a cut placed early by
any amount, lifted, or held past a gap. Where the target lies before every queued placement (the first frames on a
packet placed after its stamp, at the start or on a seek landing there) the origin is the engine's blend too. At both of
these hand-overs, and at a cut's hull edges, the origin can step by up to one tick of motion; D238 accepts that. The
snap's origin speed is measured from the current packet's own placement on that
axis. The reading, the parse-ahead and the rewind above are untouched. How each packet is placed, and the measured
result: finding 106 and `docs/measurements/2026-10-27-retime-view-by-server-tick.md`.

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

## A dead recorder's camera is computed during playback, not recorded (B745)

The belief was that a point-of-view demo's recorded viewpoint *is* the camera in every observer mode, death cam and
freeze cam included (D188's implementation). **It is not.** `democmdinfo_t` carries the prediction origin, the feet,
and during playback `CViewRender::SetUpViews` runs the local player's `CalcView`, which for an observer dispatches to
`CalcObserverView` (`baseplayer_shared.cpp:1533`, `:1689`). The death cam backs off the corpse and swings toward the
killer over one second (`C_TFPlayer::CalcDeathCamView`, `c_tf_player.cpp:7177`). The freeze cam zooms over 0.4 s to a
point a random 96..200 units in front of the killer (`C_BasePlayer::CalcFreezeCamView`, `c_baseplayer.cpp:1652`), at a
random height drawn when the data update enters the mode (`:885-891`). None of that is in the recording. *Published
source.*

**Another player's eyes are his feet.** `m_vecViewOffset` is received only in `DT_LocalPlayerExclusive`
(`c_baseplayer.cpp:219-273`), and `C_BaseEntity::Clear` zeroes it, so for anyone but the recorder `EyePosition()` is
`GetAbsOrigin()`. The death cam therefore looks at the killer's origin, and the freeze cam stands at the killer's origin
height plus -30..20 while aiming at his chest (`VEC_VIEW` 72 less half the 82-unit hull). It reads as a camera looking
slightly up at the killer, which is what the etf2l POV specimen shows at tick 2070. *Published source; looked at in
ours, not compared with TF2.*

**The death time is the server's.** `m_flDeathTime` is stamped on the server clock (`player.cpp:1746`), and a demo's
ticks do not start at zero: the bots specimen's recorder died at demo tick 1376 with a death time of server tick 1758.
Measured on the demo's own clock, the swing would never have started.

## A decapitated recorder looks out of his own head, and a gibbed target is orbited at his first piece (B746)

**Two gib handles steer the observer views, and neither is networked.** `CreatePlayerGibs` keeps the first piece it
makes in the player's `m_hFirstGib` (`c_tf_player.cpp:7506`). A decapitation's head, made by `CreateTFHeadGib` after the
headless body (`:1240-1245`, `:1066-1083`), is both `m_hFirstGib` and `m_hHeadGib`. A dead player's observer origin is
his first gib's mass centre when he has one, before his death view (`GetObserverCamOrigin`, `:6817-6838`), so a chase or
freeze cam on a gibbed killer centres on one flying piece. A decapitated recorder's death cam is six units over his
head's mass centre, swinging from the head's own angles to face his body over a second, and it sets `EF_NODRAW` on the
head, never cleared (`CalcDeathCamView`, `:7199-7223`). *Published source; synthetic tests, not looked at.*

**The handle is to one piece, and a faded one is null.** When the first piece fades, the camera does not move to the
second: `m_hFirstGib` reads null and the death view takes over. The headshot decapitation keeps `m_hFirstGib` and clears
`m_hHeadGib` (`:1077-1081`), so it orbits the head without looking out of it, and the recorder sees his own head drawn.
*Published source.*

**The handle also gates the throwing, and a respawn is what clears it.** `CreateTFGibs` and `CreateTFHeadGib` throw only
while `m_hFirstGib` is null or the corpse is a feign death (`:1070`, `:1102`), and `ClientPlayerRespawn` nulls both
handles (`:7992-7994`). Two real deaths always have a respawn between them, so the guard can bite only a spy's real
death while his feign-death pieces still last: that death throws nothing, and his camera stays on the feign death's
pieces. **Wrong turn, kept:** the first port tied a thrown piece's life to its body's window and fade. A piece is its
own `C_PhysPropClientside`, released by its own `fadetime`; a body leaving view went 5 s after, taking the head (and the
death cam's eye) 6 s early. *Published source; synthetic tests.*

**The head comes off the body in the bone build, after the three scales, and not for the seed.** `C_TFRagdoll::
BuildTransformations` zeroes `bip_head`, `prp_helmet` and `prp_hat` in place (`BuildDecapitatedTransformations`,
`:8522-8549`) only under `!m_bBaseTransform` (`:8836`). The seed reads the player's own bones, so a ragdoll seeded from
the decapitated build would hand its head element a zero basis. Tested: a body whose one solid is its head falls
identically with and without the decapitation, and differs when the seed's guard is removed. The head piece's
rotational damping is multiplied by six "so it doesn't roll so much" (`:7492-7499`). *Interpolated:* vphysics'
`GetDamping`/`SetDamping` pair is read as a round trip through the one field IVP keeps.

## A camera on another player sits in the body as drawn, and paused that is the held one (B747)

**The SourceTV cameras read their target's interpolated origin.** `C_HLTVCamera::CalcChaseCamView` starts from
`target1->GetRenderOrigin()` (`hltvcamera.cpp:125`) and `CalcInEyeCamView` from `pPlayer->GetAbsOrigin()` (`:315`). Both
are the values `InterpolateServerEntities` left for this frame, so the camera moves at the frame rate. Ours read the
whole tick's roster and stepped at the tick rate. The players the chase camera's hull trace meets are drawn ones too.
*Published source.*

**Wrong turn, kept.** The first fix sampled the drawn players with interpolation always on. The review caught that a
paused frame does not interpolate: one global flag (`c_baseentity.cpp:3226`, B399) holds every entity at its last
update, with no `cl_interp` delay. The bodies stood there, and the camera sat a tenth of a second of motion behind them.
Every `--shot` is a paused frame, so every capture of a moving target would have carried it. The camera now samples with
the moment's own flag. The seek replay of `m_flLastDistance` samples each earlier tick as drawn while playing, the time
base the following frames continue on. *Published source; a real-demo assertion
(`CorpusSourceTvCameraTests`), not looked at.*

**Another player's eye angles interpolate too, and ours do not (B749).** `C_TFPlayer`'s constructor registers
`AddVar( &m_angEyeAngles, &m_iv_angEyeAngles, LATCH_SIMULATION_VAR )` (`c_tf_player.cpp:3874`), the one interpolated
variable TF2 adds to a player. The track's rebuild held `EyePitch` and `EyeYaw` from the latest arrived keyframe, so
they stepped at the snapshot rate and ran `cl_interp` ahead of the body they sat on. The chase camera's yaw
(`hltvcamera.cpp:189`), the in-eye camera's angles (`:314`) and every player's aim pose read them. *Published source.*

**The interpolation already existed; its result was dropped** (B749). A player's `m_angRotation` is not networked, so
the pose's angles have always carried `m_angEyeAngles`, and the simulation history slerped them on the right pair, as
`Lerp_Hermite<QAngle>`'s refusal to spline requires. The rebuild then took the eye fields from the stated pose instead.
**Wrong turn, kept:** the first fix added a second eye history beside the simulation one. Both reviews found it held the
same numbers twice. The fix is two lines in the rebuild.

## Everyone's feet turn per frame, and between ticks the body had been facing the eyes (B750)

`UpdateClientSideAnimation` hands a non-local player's anim state `m_angEyeAngles` each frame
(`c_tf_player.cpp:4287`), so his feet converge by the frame time as the recorder's do. **What reading the code first
found:** between ticks the drawn player's `Yaw` was the track pose's, and a player's pose angles are his eyes. Every
other player's body faced his eyes while playing, with the torso twisted on top against the per-tick feet. It showed
only in a turn on the spot, the one case where feet and eyes part. Now the feet are carried per player per frame
(`PlayerFeet`), the body is drawn at them, and the twist is measured from them. *Published source; synthetic tests,
not looked at.*
