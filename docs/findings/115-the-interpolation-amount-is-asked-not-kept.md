# 115. The interpolation amount is asked, not kept (B848, B849, B860, B861)

How the client's interpolation delay follows the server's replicated bounds during a demo. Evidence classes: **read** is
the Source SDK 2013 client source; **disassembled** is the shipped `engine.dll`, at the address given (neither source
entry, B415 nor `ClientInterpTests`, records which build; TF2 updates weekly); **interpolated** is flagged where it
appears. Nothing here was measured in TF2.

## The wrong model this replaced

The viewer computed one delay for the whole recording, from the server's ConVars after the last `net_SetConVar`, and
every track sampled that far behind. A server that raised or lowered its interp-ratio bounds mid-recording played the
whole demo under its final ones.

## What the client does

`GetClientInterpAmount()` (`cdll_bounded_cvars.cpp:127`, the `MAX` at `:134`, **read**) is computed every time it is asked: the larger of
`cl_interp` and `cl_interp_ratio / cl_updaterate`. The clamping is the bounded ConVars' own, in that file (**read**):
`cl_interp` is raised to at least `sv_client_min_interp_ratio / cl_updaterate` (`:108-121`), and `cl_interp_ratio` is
clamped between the minimum and `sv_client_max_interp_ratio` (`:74-86`), both skipped when the minimum is −1; `cl_updaterate` is only cast to one (`:133`), and its clamp by
`sv_minupdaterate`/`sv_maxupdaterate` is the engine's (disassembled, `engine.dll` `0x180164230`, recorded in
`ClientInterpTests`). The client applies each `net_SetConVar` as its packet is read (B450, engine side, **interpolated** there), so
the amount at a tick is the one under that tick's bounds. `GetInterpolationAmount` rounds it to ticks and adds
`serverTickMultiple` (`c_baseentity.cpp:5937`, **read**): one tick, two on a server simulating on alternate ticks
(`:5919-5923`). A predictable or client-created entity takes `TICK_INTERVAL × serverTickMultiple` instead, whatever the
amount (`:5925-5928`). That `:5937` path is the one for multiplayer and for a demo that is NOT locally recorded
(`!engine->IsPlayingDemoALocallyRecordedDemo()`, `:5932-5935`); a locally recorded demo falls through to `:5940-5960`,
where an entity animated and simulated every tick (or latched for the one it is) takes `TICK_INTERVAL ×` the tick
multiple, and only the rest take the amount (**read**; the `g_nThreadModeTicks` added when threaded is a file-scope
`static int` declared 0 at `:60` whose only other use is that read, so in the SDK nothing can write it and it adds
nothing). What makes a demo "locally recorded" is the engine's
(`IsPlayingDemoALocallyRecordedDemo`, not read); findings 20 takes a POV demo to be one, B267 chose the non-local path
for every demo on purpose, and B861 holds that open as a parity question.

Each interpolated variable keeps its OWN amount (`SetInterpolationAmount` only stores it, `interpolatedvar.h:608-611`,
**read**). `C_BaseEntity::CheckCLInterpChanged` (`c_baseentity.cpp:6443-6465`, **read**), called at `FRAME_START`
(`cdll_client_int.cpp:2344`, **read**), notices `GetClientInterpAmount()` or `cl_interp_npcs` changing and sets every
entity's variables to the new amount (`Interp_UpdateInterpolationAmounts`, `:837-849`). Neither puts an entity back on
the interpolation list, so one that had settled holds its value until its next update (**read**: no re-listing in
either; that nothing else re-lists it on an amount change is **interpolated**). A variable therefore takes a new amount
at the next frame start, not when the packet is read. And the frame start comes BEFORE the frame's packets
(**disassembled**, the x64 `engine.dll`): `CEngine::Frame` calls the `ClientDLL_FrameStageNotify` wrapper with 0 —
the only caller that passes `FRAME_START`; the wrapper calls `g_ClientDLL` slot `0x118`, slot 35 of `IBaseClientDLL`,
`FrameStageNotify` — and only then runs the host frame, whose per-tick `_Host_RunFrame_Client` calls `CL_ReadPackets`.
So the frame that reads a change still draws under the old amount, and the next one draws under the new.
And `Interpolate` prunes each history by the amount it was run with (`interpolatedvar.h:1057`, **read**), so after a rise
the entries the new, longer delay would reach may already be gone.

Temp entities fire `GetClientInterpAmount()` after they arrive during demo playback (`CL_QueueEvent`, `engine.dll`
`0x1801f9bc0`, B415, disassembled), under the bounds of the tick they arrive on; they ask it directly, so they switch at
once, where the variables switch at the next frame start.

## What the viewer does now

`InterpolationSchedule` is the amount by tick, built from the same ConVar history `ServerConVarsAt` reads; tracks, the
demo player's rollback and the faces read it at their own tick, and the checkpoint walk keeps enough history for the
widest delay anywhere in the recording (B849). Since B860 a rise and the frame start are the engine's too, short of
what its reviews filed open: B862 (prediction's own history and time), B863 (followers), B864 (a latch that changed
nothing), B865 (the first window, re-parenting, the face's jump), B866 (extrapolation), B867 (readers of where an
entity is), B868 (the cycle across a sequence change) and B869 (a jump landing just past a rise):

- **A frame samples under the schedule its frame start knew** (`InterpolationSchedule.AsOf`): the presenter tells the
  timeline the tick the previous frame showed (`FrameStart`), and a step read since is not used until the next frame.
  The face lags its own `m_iv_flexWeight` amount the same way.
- **A rise holds what had settled**, including an entity that settles on the very frame that reads the rise, and
  **floors what is still moving** at the oldest entry the earlier frames' pruning kept. Only a frame that had the
  variable listed pruned it (`Interpolate`, `interpolatedvar.h:1057`, skipped once settled; `NoteChanged` at a latch,
  `:667`, **read**), so a variable that settled before the rise was last cut on its settle frame. The sampler is
  stateless and takes frames a tick apart, each reading a step a frame late. At real frame rates that is a divergence,
  not an approximation: the engine cuts at a frame's fractional time (`interpolatedvar.h:1057`, **arithmetic** on read
  source), and the frame that reads a rise can sit fractions of a tick or several ticks past it — B869; the face, which
  keeps its history frame by frame, prunes at the real times.
- **A held variable keeps the entry that last changed it**, sampled at the first such entry still held (a later
  same-valued latch can flush the changing one), a fraction of zero; and **a pair starting on the pruned floor has no
  third sample** — `Truncate( i + 3 )` leaves nothing older (`interpolatedvar.h:782-795`, `:851`, **read**) — so the origin
  and the cycle run linear there. The floor is chosen from the history as the cutting frame held it — after every
  earlier frame's cut, which a late update arriving between them exposes by flushing the entries after it — and then
  follows the later updates: one stamped below the kept three flushes them and is itself the oldest held (`AddToHead`,
  `interpolatedvar.h:649`, **read**). The cut that sets it is the furthest any frame since reached, found over every
  delay step in the window, not only the last rise.
- **Each variable holds or blends on its own.** Origin, angles, cycle and pose parameters are separate
  `CInterpolatedVar`s, and a latch re-arms only those whose value it changed (`NoteChanged`, `interpolatedvar.h:633-639`;
  `c_baseentity.cpp:2828-2829`, **read**). So after a rise a crate turned in place by a late update holds its position
  while its angles blend, and a pose parameter still moving when the cycle settled is floored on its own history.
- **A jump forward is read in batches**: while skipping, `CDemoPlayer::ReadPacket` stops after its hundredth call
  (**disassembled**, `engine.dll` 0x1800741e0), and each frame of the skip has a frame start, so the frame that reaches the
  target had read to within a hundred packets of it; the viewer takes the far end of that batch (where the batch began is
  **interpolated**). A sample by any other reader, at any other tick, is no frame of the presenter's: it is the frame after one that read the
  last whole tick below it (`ceil(tick) − 1`), the model the history walks use (**interpolated**, reviews 12 and 13).
  Anything asking where an entity is takes it at the frame start and prediction the frame draws it under — not at
  the frame's fractional tick for two readers, nor at the recorded view a point-of-view frame places the recorder at
  (B867).
- **What a point-of-view demo predicts takes one tick whatever the amount.** `CheckInitPredictable`
  (`c_baseentity.cpp:2674-2712`, **read**) needs `cl_predict`, a local player, eligibility and `ShouldPredict()`.
  `cl_predict` reads `sv_client_predict` unless that is −1 (`cdll_bounded_cvars.cpp:36-48`, **read**), whose registered
  default is `"-1"` with `FCVAR_REPLICATED` (**disassembled**, `engine.dll` `0x18000a290`), and the client forces its own
  to 1 on a level of more than one client (`cdll_client_int.cpp:1645-1652`, **read**). `CL_RunPrediction` skips only while
  the demo skips (**disassembled**, the `0x48` slot of the demo player, which `_Host_RunFrame` uses beside the timedemo
  one to pull packets on a frame of no ticks). So in a point-of-view demo the recorder (`C_BasePlayer::ShouldPredict`,
  `IsLocalPlayer()`) and every TF weapon he owns (`CTFWeaponBase::ShouldPredict`, `tf_weaponbase.cpp:3433-3441`) are
  predicted; the recorder is drawn where the demo's view put him (B56) whenever there is a view, and his weapons, and he
  when there is none, take `InterpolationSchedule.Predicted`. A SourceTV recording's local player is the SourceTV client,
  never drawn. The predicted branch also moves the target to the predicted time and fills the history from prediction
  rather than the network (`BaseInterpolatePart1`, `:2853-2862`, **read**): that is B862.
- **Alternate ticks never apply to TF2**: `IsSimulatingOnAlternateTicks` needs `maxClients == 1`
  (`baseentity_shared.cpp:2544-2552`, **read**), TF2's `GetPlayerLimits` sets the minimum to 2 ("Force multiplayer",
  `tf_gameinterface.cpp:22`, **read**) and the engine clamps `maxplayers` between the limits (**disassembled**, the
  setter that prints `maxplayers set to %i`; the only way under the minimum is the retail HL2 and Portal demo builds).
- **`cl_interp_npcs` moves nothing in TF2**: `AdjustInterpolationAmount` raises only an entity that `IsNPC()` or rides
  one (`c_baseentity.cpp:5893-5913`, **read**), and no TF2 client class answers true: only `C_AI_BaseNPC` overrides it,
no file under `client/tf` or `shared/tf` names that class, and TF2's bosses and robots are `C_NextBotCombatCharacter`
(`C_NextBot.h:33`), a `C_BaseCombatCharacter` that does not override it (**read**).

- **What "changed" means is two kinds of compare, not one** (B860 reviews 28-30). The rise logic asks which latch
  last re-armed a variable, and the engine re-arms on `NoteChanged`'s return (`c_baseentity.cpp:2828-2829`, **read**):
  a `memcmp` against the head (`interpolatedvar.h:636`, **read**), bitwise, so −0 after +0 re-arms. But whether a
  latch happens at all is `PostDataUpdate`'s `originChanged || anglesChanged || simTimeChanged`
  (`c_baseentity.cpp:2571-2576`, **read**), whose first two are float `!=`s (`Vector`, `vector.h:663-668`; `QAngle`,
  `:2042-2047`; **read**) — so at one simulation time −0 after +0 is no latch, and at a new one it latches and then
  re-arms. This project had the two the other way round: the history compare was float-equal until review 28 and the
  append gate bitwise until review 29. The angle lerp's shortcut is a third float compare; findings 20 owns it. A
  commit message in review 27 also said an entry equal to the head is not added — wrong: `AddToHead` at `:649` is
  unconditional, which is B382's whole point.

The locally recorded every-tick path is still the open question of B861.
