# 39 — The engine's frame clocks, and why there are several

*Evidence class: read from published source (`source-sdk-2013`), except where marked.*

## The question, and who asked it

> *"and how does valve handle these timings?"* — the owner, 2026-08-26

Asked while an assistant was midway through designing a two-clock type for the viewer on his own
reasoning, having already written the tests for it. The answer did not change the design. It changed
the *justification* from a judgement call to a citation, and it turned "I'd rather not merge these,
B209 is open" into "merging these would be a divergence."

That difference matters more than it sounds. A design defended by taste gets undone by the next
person with different taste.

## What the engine keeps

`src/public/globalvars_base.h`, with Valve's own comments intact:

```cpp
// Absolute time (per frame still - Use Plat_FloatTime() for a high precision real time
//  perf clock, but not that it doesn't obey host_timescale/host_framerate)
float           realtime;
// Absolute frame counter
int             framecount;
// Non-paused frametime
float           absoluteframetime;
...
float           curtime;
// Time spent on last server or client frame (has nothing to do with think intervals)
float           frametime;
...
// interpolation amount ( client-only ) based on fraction of next tick which has elapsed
float           interpolation_amount;
```

Plus `Plat_FloatTime()` outside the struct — *"Returns time in seconds since the module was loaded"*
(`public/tier0/platform.h:1198`).

**Six distinct time quantities, and every one is named by what it obeys.** `realtime` follows
`host_timescale`; `Plat_FloatTime` deliberately does not. `frametime` is the last frame's duration;
`absoluteframetime` is the same thing *unpaused*. `curtime` carries **three documented meanings**
depending on whether the caller is receiving packets, rendering, or predicting — the header spells
all three out.

**There is no consolidation here to copy.** The distinctions are precisely the ones a tidy-up
erases.

## The closest analogue to this viewer's free camera

`CalcDemoViewOverride`, `src/game/client/view.cpp:141-159`. This is the engine's own free camera for
**demo playback** — the same job as ours:

```cpp
static void CalcDemoViewOverride( Vector &origin, QAngle &angles )
{
    engine->SetViewAngles( s_DemoAngle );
    input->ExtraMouseSample( gpGlobals->absoluteframetime, true );
    engine->GetViewAngles( s_DemoAngle );

    Vector forward, right, up;
    AngleVectors( s_DemoAngle, &forward, &right, &up );

    float speed = gpGlobals->absoluteframetime * cl_demoviewoverride.GetFloat() * 320;

    s_DemoView += speed * input->KeyState (&in_forward) * forward  ;
    s_DemoView -= speed * input->KeyState (&in_back) * forward ;
    ...
}
```

**Non-paused frame time, for both the mouse sample and the movement.** Note also `input->KeyState`,
which is the call whose impulse bits `docs/memory/...` records as consumed on read — the same
mechanism this viewer's `ConfigConsole.Intent()` mirrors, and the reason it may be read exactly once
per frame.

And `cl_showfps` reads the same quantity — `gpGlobals->absoluteframetime`
(`src/game/client/vgui_fpspanel.cpp:166`). Which is where **B174 arrived independently**, when the
frame meter stopped starting a clock of its own and read the camera's instead. That was reasoned out
here without the citation, and it turns out to match.

## What this settles for us

Three clocks, and each maps onto one of Valve's:

| ours | Valve's | why it cannot be another |
|---|---|---|
| the flight clock | `absoluteframetime` | the camera flies by it, exactly as `CalcDemoViewOverride` does |
| the pacing clock | a `Plat_FloatTime`-kind wall clock | a limiter cannot pace by the duration of the frame it is deciding to allow |
| the soundscape clock | `realtime` | `soundscape_fadetime` is wall seconds; tied to demo time a fade stretches when playback slows and vanishes when scrubbed |

The middle row is arithmetic rather than authority: at a 60 Hz cap the budget is 16.67 ms, so a frame
that cost 4 ms leaves 12.67 ms still to wait. Feed the limiter the frame's own duration and it calls
every frame due early, by however long that frame happened to be cheap.

## `frametime` is zero while a demo is paused, and NOT while it skips (B541)

*Disassembly of engine.dll, 2026-10-13; the client half is published source.*

`CClientState::GetFrameTime` (FUN_1800a2d40) answers `IsPaused() ? 0 : m_flFrameTime` outside a simulation tick, and
`_Host_RunFrame` (FUN_1801a4570) copies it into the client's `gpGlobals->frametime` before the frame renders.
`CClientState::IsPaused` (FUN_1800a34c0) is true when `demoplayer->IsPlaybackPaused()` is. And
`CDemoPlayer::IsPlaybackPaused` (FUN_1800729f0) is

    IsPlayingBack() && !IsSkipping() ? m_bPlaybackPaused : false

so the pause flag does not count for the length of a skip. `SkipToTick` (FUN_180073b10) sets the flag at once when
`demo_gototick` is given its pause argument, and the skip's frames still have a frametime; the frame the skip lands
on is the first with none.

**What that decides on the client** is everything gated on `frametime`, and animation events are one:
`C_BaseAnimating::Simulate` calls `DoAnimationEvents` only `if ( gpGlobals->frametime != 0.0f )`
(`c_baseanimating.cpp:5162`), once per entity per frame from `SimulateEntities` (`cdll_client_int.cpp:2000-2017`,
called at `:2207` in `OnRenderStart`). So:

| frame | events walked |
|---|---|
| playing | yes |
| a skip's frame, paused or not | yes — every 99 packets or so (finding 31) |
| paused, including the frame a paused skip lands on | no |

**A paused frame also moves nothing.** `m_flPrevEventCycle` is written only inside `DoAnimationEvents`, so the first
frame after a pause walks from where the last frame with a frametime left it — the skip's last rendered frame, after
a seek. Whether that span crosses a footstep depends on where the engine's skip frames happened to fall, which
depends on how long its host frames took. The engine has that accident too; it just never sounds while paused.

**The wrong turn:** the viewer walked on every pose, gate-free, and the paused opening frame fired footsteps or not
by where the skip's 99-packet batches had started — tick 6 in one run and tick 1 in another, because an early frame
had already shown tick 0. Two runs disagreeing was read as a lost event for a day; the run WITH footsteps was the bug.

**Not read:** `NotifyShouldTransmit( SHOULDTRANSMIT_START )` sets `m_nEventSequence = GetSequence()`
(`c_baseanimating.cpp:4670-4678`), so whether an entity's FIRST walk restarts at cycle zero depends on whether that
call comes before or after its first sequence is known — which is the engine's packet-entities code, not yet
disassembled. `CL_CopyNewEntity` (FUN_180087c40) does not make the call itself.

## What is NOT here, stated so nobody looks for it

**`fps_max` and the host frame loop are engine code, and `source-sdk-2013` ships no
`engine/host.cpp`** — that folder contains only `audio`. So the limiter's own reference point cannot
be read from the SDK.

What the published headers *do* establish is that it cannot be `frametime` or `absoluteframetime`,
both of which are outputs of the frame being paced. It has to be a wall clock of the `Plat_FloatTime`
kind. That is an inference, and it is flagged as one.

B209 still holds two open frame-pacing parity questions for the owner; nothing here answers them.

## The wrong turn worth recording

The assistant had written `FrameClockTests` before asking any of this — the tests were correct and
the reasoning behind them was "these are stamped at different moments, so merging them changes
pacing." True, but it is an argument from the code we already have, which cannot tell you whether the
code we already have is right.

`CLAUDE.md` says the order is **conformance test, then unit tests, then implementation**, and the
conformance test is where "what does the engine actually do" gets written down *before* any code
exists to bias the answer. Written second, it becomes a description of what was built. The only thing
that saved it here was the owner asking the question.
