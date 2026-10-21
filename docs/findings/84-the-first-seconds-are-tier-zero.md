# 84. The first seconds ran tier-0 code, and the stalls were old rocket jumps and unprecached sounds (2026-10-20)

Two stories from one investigation of f12's frame. The first is the ramp over the first seconds of playback. The
second, which the owner raised partway through (*"there's still major jitters every few seconds"*), is a single frame
of 100–400 ms every few seconds all through playback. They turned out to have different causes.

**The symptom.** In every `--measure` run of f12 (`demostf-cp_process_f12-2026-08-08-2207.dem`, tick 30000, first
person, Debug, `+fps_max 0`), the frame rate climbed over the first seconds of playback, from 30–45 fps to 60–85 fps.
The `camera` phase fell from 15–21 ms to 8–10 ms. Valve's client has no such ramp. Every number below comes from one
machine (12 logical processors), and the machine was shared with other agents' builds, so each is a per-second window
quoted from one run, not a mean over runs.

**The cause (measured).** The tiered JIT. Until the `.NET Tiered Compilation Worker` thread promoted it, the render
thread ran unoptimized tier-0 code. The worker was the one thread that was busy early and idle late, and the frame rate
stepped up in the second it went quiet. **The fix tried, `TieredCompilationQuickJit=false`, was measured and
reverted.** In the published package it loaded 46 s slower and played about 17% slower at steady state, and it was
not faster early either (see *Before and after, the ramp*). The ramp's cause stands; its fix is open.

## How it was found, wrong turns included

The method was instruments first, each with a control, then a hypothesis. Everything was a throwaway hack in a
separate build folder, never committed: per-thread CPU seconds per second, per-second GC counters, the corpse record's
progress, and a switch to turn the record off.

1. **The ramp is real and ends early (run 1, `--measure 50`).** Seconds 1–3: 34, 29, 35 fps. By second 8: 61. From
   there it was flat at 55–75 through second 50, apart from a light stretch of the demo at seconds 22–26 (102–115 fps).
2. **Candidate (a), the D181 corpse record: killed as the cause.** It started when the map finished loading and ran
   until **52.8 s after that**, so it was busy through the whole flat stretch. With the record switched off (run 2),
   the ramp was unchanged in shape: 40, 33, 48, 50, 54, 58, 60, then 66 by second 8. What the record does cost is
   steady state: everything ran 5–15 fps higher without it, and the GC pause fell from 1.60 to 0.57 ms a frame. Those
   two runs measured different window lengths (50 s against 30 s). That cost belongs to finding 83, not here.
   **Caveat:** "record off" is not "no corpse work". D179's replay moves the work onto the render thread instead.
   Run 3's first rebuild report shows it: `advance 7.7 (corpses 7.6 over 306.6 steps)`.
3. **The control that made it startup work, not content (run 3).** Starting at tick 30533 means starting at second 8
   of run 2, where that content ran 60–66 fps. The ramp **reappeared**: 38, 42, 45, 54, 73. So it follows the start
   of playback, not the demo.
4. **It was uniform, which pointed away from any one phase.** Between run 3's first second and its sixth, `camera
   particles` fell 8.06 → 2–3 ms and `camera pose` 9.64 → 4. But `hud frame` also fell 1.51 → 0.57, `HudChat`
   0.62 → 0.29, and `card.HealthIcon` 0.24 → 0.08. The HUD has no startup work tied to corpses, particles or models.
   A 2–3× slowdown of every phase means the code itself was slow, not one lazy cache filling.
5. **One unnamed thread (runs 4–6).** Per thread, per second: the record thread was at 0.9–1.0 CPU throughout, and so
   was the render thread. A third thread ran **0.60–0.84 CPU for seconds 1–7, then 0.03**, and it did so in all three
   runs. In run 6 it dropped from 0.45 to 0.09 at second 7, and the frame rate went from 45 to 59 in that same second.
6. **Two instruments that failed their controls, kept here because both gave confident wrong answers.**
   - `dotnet-trace` (sampled thread time, 50 s from attach) listed only four managed threads, and the busy one was
     not among them. That read as "it is a native thread". The control killed that reading: in the trace, the thread
     that later became the corpse record sat in `DemoTimeline.Build` for all 50 s. **The trace had covered the load,
     not playback.**
   - `ProcessThread.StartAddress` returned an address in no loaded module for every thread, including the known
     managed record thread. **The input was bad**, so its "?" meant nothing.
7. **The name (run 7).** `GetThreadDescription`, with `NtQueryInformationThread` class 9 mapped to a module, passed its
   control: the record came back as `.NET TP Worker`. The busy thread was the **`.NET Tiered Compilation Worker`**,
   at 0.74, 0.53, 0.78, 0.66 and 0.35 CPU in seconds 1–5, then 0.05 from second 6. Fps over the same seconds: 32, 34,
   45, 37, 45, then **56**.
8. **Why the lead's "TC=0 still ramps" was a misreading (run 8).** With `DOTNET_TieredCompilation=0` the worker thread
   is gone, so the variable did take effect. The HUD no longer ramps: `hud frame` read 0.99, 0.89, 0.87, 0.91, ...
   But the whole run sits lower, at 35–50 fps through second 10, where the tiered runs reached 56–61 by second 7. With
   tiering off, the framework's precompiled code is never recompiled and nothing gets profile-guided optimization. So
   TC=0 removes the warm-up and pays for it in steady state. The later rise in the lead's tc0 run (seconds 13–18)
   coincides with the lighter demo content seen in run 3, not with warm-up. This is interpolated: no run here measured
   that stretch under TC=0.

## Why tier 0 shows here at all

Debug is compiled optimized (D176), so the JIT treats our code like Release code. With quick JIT on, a method's first
compile is tier 0 (unoptimized; PGO-instrumented where dynamic PGO applies), and it is recompiled at tier 1 only after
30 calls plus a delay. That delay restarts whenever new tier-0 methods appear (runtime documentation, read and not
measured here). Playback is when most of the viewer's code first runs. Run 9 counts it with the new instrument: **7,648
methods compiled during the first 20 s of playback** with quick JIT on.

**Valve's equivalent is nothing.** The engine is native code, optimized at build time. "Valve's way" is code that is
optimized from its first call. `TieredCompilationQuickJit=false` gives that to everything the framework did not
precompile. The framework's ReadyToRun code is already optimized and still tiers up in the background.

**The trade, named, then measured.** Methods compiled optimized on first call cost more JIT time, and our own
methods no longer collect the dynamic-PGO profile that tier 0 would have gathered. The measurement below says that
loss dominates: the decode and the frame both got slower. That PGO is the mechanism is interpolated, not shown.

## The instrument that stays

`--measure` prints `jit: N methods compiled, T ms compiling` beside `gc:`, read from `System.Runtime.JitInfo` over the
counted window (B243: carried from the runtime, not estimated). `JitReadingTests` includes a control: a method
compiled between two readings must show.

## Before and after, the ramp: the fix lost, and was reverted

**A wrong turn, kept.** The quick-JIT fix was committed on runs 1–8 alone: the worker named, the ramp following it,
and TC=0 removing the warm-up. The first before-run (run 9) overlapped another agent's compiler and the Epic Games
launcher at 97% load and was void. The review asked for the load cost, and measuring it reversed the decision.

**Published package, as `build/package.ps1` makes it** (`dotnet publish -c Release -r win-x64 --self-contained`; it
does not publish ReadyToRun), f12 tick 30000 first person, `--measure 20`. Three runs per variant, alternating, every
run quiet before and after (no build CPU, load at most 15%), medians quoted. One machine.

| variant | timeline decode | start → first frame | peak working set / private | fps, playback s 2–7 | fps, s 9–19 |
|---|---:|---:|---|---:|---:|
| P0, quick JIT on (`main`) | **169 s** (169.0, 169.1, 191.8) | **198 s** | 4.89 / 6.56 GB | ~47 | **~64** |
| P1, quick JIT off | 216 s (212.8, 215.6, 220.2) | 242 s | 4.93 / 6.64 GB | ~43 | ~53 |
| P2, quick JIT on + `PublishReadyToRun` | 172 s (171.6, 172.5, 176.1) | 195 s | 4.89 / 6.56 GB | ~29 | ~66 |

- **Quick JIT off costs 46 s of load (+22%) and about 17% of steady frame rate**, and it is not faster in the first
  seconds either (43 against 47). Memory does not move. **Reverted**; `RuntimeConfigConformanceTests` now pins the
  default so the trade is not re-made without new numbers.
- **ReadyToRun is not the ramp fix.** It loads like the default and ends a little faster (66 against 64), but its
  first seconds are much worse (about 29 fps). Why ReadyToRun code starts slower here was not looked into.
- **Debug builds agree** on the load direction (two valid quick-on runs against three quick-off, one voided for build
  CPU): decode 170.5 and 190.2 s against 206.3, 218.3 and 226.6 s.
- **Not this setting:** f12's decode took 170–250 s in every one of these runs, against 54–55 s in other agents' runs
  earlier the same day. What differs there was not looked into.
- **The fps columns are read from the log's per-second lines**, after `first frame after the opening state`. The
  stdout capture was empty under a background job, which a control caught: the out files were 0 bytes.

The `jit:` instrument stays. The ramp is open.

## The periodic stalls

**The instrument was already in the log.** Every frame over 25 ms writes `SLOW FRAME` with its phase split and the
collections inside it (`gc g0/g1/g2 paused N ms`). A slow camera frame adds `SLOW CAMERA` (fly, view, device,
particles), and a sound decode inside a frame adds `STALL decoding '<name>'`. The per-second `--measure` line hides
these: its second number is one frame, and its mean spreads a 300 ms frame over fifty.

**Run A, the before (13:56, quiet-gated, checked after; build `f1652da6` plus a throwaway print).** Twelve frames over
100 ms in the 60 s after the opening seek:

| kind | frames | what the log names |
|---|---|---|
| particles | 1: 276 ms | `SLOW CAMERA 267`: `particles 256` |
| sound | 4: 299, 106, 224, 224 ms | `STALL decoding` `weapons/draw_secondary.wav` 268 ms, `physics/surfaces/tile_impact_bullet2.wav` 170 ms, `physics/glass/glass_sheet_step3.wav` 204 ms |
| HUD | 5: 210, 144, 460, 114, 122 ms | each the frame right after the viewport changed size, 984×551 ↔ 1640×879 |
| other | 2: 174, 105 ms | the first frames after the seek |

**No stall was a gen2 collection** (measured): every one reads `gen2 0`, and the GC pause inside each is 3–33 ms. The
first hypothesis (a blocking gen2 GC of a ~3.5 GB heap) is dead.

### The particle stall: every rocket jump a player ever made, replayed in one frame

- **The counter.** The census's `replays so far` jumped once in every run, near tick 30540: +172, +285 and +440 ms in
  three runs, +23,566 steps each time, and +88 bursts in `finished`. The other agent's 440 ms `camera` frame in a run
  earlier the same day landed on the same tick.
- **The instrument (throwaway, one run).** A line for each rebuild over 200 steps showed it: at played tick 30537,
  **90 `rocketjump_smoke` bursts** born from tick 893 onward, each replayed about 260 steps, 23,559 in all, 248 ms.
  Each was `absent` from the running set while 470 were offered. Tick 30013 had a second, smaller one: 16 bursts and
  50 ms. The two are 524 ticks apart, 7.9 s, which matches the lead's "one stall about every 7–8 seconds".
- **Why they were offered at all.** `rocketjump_smoke` is a `ParticleEffect` dispatch from an entity, a player. An
  entity's dispatch with no `ParticleEffectStop`, no RESET and no deletion of its owner is offered for the rest of the
  demo (`BurstEffects.AddParticleDispatches`, by design: Valve's stop is what ends it).
- **Why they were rebuilt.** At the opening seek their owner was not posed, so the seek built none of them. The first
  frame he was posed, every one was absent, so each was replayed from its own tick through its whole life only to
  finish again.
- **A wrong fix, kept here.** The first fix (`23827fd8`) remembered bursts that `Retire` let go after finishing in
  play. Its unit test was valid, but the measured run still jumped +23,577 steps at the same tick. These bursts were
  never retired; they had never been built.
- **A second wrong fix, caught in review.** The next fix ("playing builds a burst only if it fired since the last
  seek") skipped any burst older than the landing that the seek had not built. That dropped an in-flight burst whose
  owner was merely out of our camera's view at the seek, for its whole remaining life. **Out of view is not dormant**:
  dormancy follows the recording's PVS, so the engine made that effect at its dispatch (`c_particle_system.cpp:194`).
  Seek while a rocket-jumping Soldier is behind the camera, turn to him a second later, and there was no smoke.
  The 248 ms stall had been 90 bursts that were *all long finished*, so the rule was wider than the evidence.
- **The fix, by arithmetic** ("skip a burst by its age past its whole life"). On a played frame, a burst that is not
  held is skipped, and marked spent, only when its age exceeds `ParticleEffect.MaximumLife`: the last emission, plus
  the longest particle lifetime, plus the no-draw time. Those are the quantities `Finished` and `SleepAfter` read. An
  unbounded part means never skipped: emitting for ever, `Lifetime From Sequence`, or a child not supplied. One inside
  its life is built, replayed from its own tick. **A held burst is never skipped**, since asleep its clock stands
  (`particlemgr.cpp:1775`), so it can be alive past the bound. The first fix's set stays, for a burst that finishes
  and drops out of the offer after the seek.
- **A third turn: the bound was infinite, and the console said 8.** On f12 the age rule at first did nothing: the
  replay counter still jumped +23,689 steps near tick 30543. A throwaway print gave `rocketjump_smoke` a life of "8",
  and the skip's three terms all printed true, yet the burst was rebuilt. Printing the comparison itself, and the
  bound's raw bits, settled it: `7ff0000000000000`, +infinity. A code-page console renders `∞` as `8`. The cause was
  `rocketjump_flame`, a child, using `Lifetime From Sequence`, which the bound had treated as unknown. Now it is
  bounded by the sheet, as `C_INIT_SequenceLifeTime` computes it: the longest span over "Frames Per Second", or 1. A
  measured run afterwards (19:07) moved the counter +20 steps over ticks 30528–30595, against +23,500 before. That run's
  timings are void, since another agent's build overlapped it; the count is not a timing. **The instrument rule this
  adds: print a double you are comparing as its bits, or with an invariant culture, never as `{value}` to a console.**
- **What the bound reads, after the second review.** Only the lifetime writers the viewer runs: `Lifetime Random`,
  with its keys defaulting as the initializer's do; `Lifetime From Sequence`, by the sheet; and `move particles
  between 2 control points` and a `remap initial scalar` onto the life, both unbounded. A child is bounded from its
  parent's start, because this viewer does not apply a child's `delay` (`ParticleChildrenInfo_t::m_flDelay`,
  `particles.h:2125`); that gap is older than this finding (B564). A replay treats undrawn ticks as drawn where the
  engine would sleep the effect (B565); the sleep rule for the bound waits on that audit.
- **Row 6 (`fix/particle-caller-parity`) and this rule, on one burst.** A burst fired after the landing but
  unplaceable on its creation frame is built later by a replay from its own tick. So it takes one step of simulation
  more than a burst created on its own frame, which row 6 makes simulate nothing. Chosen, since the replay is what a
  seek into that moment shows, and pinned by
  `Bursts_FiredAfterTheLandingAndUnplacedOnTheirFirstFrame_AreReplayedFromTheirTickOncePlaced`. Otherwise the two
  rules are disjoint.
- **Not changed.** The opening seek itself replays every one of those ancient bursts it can place: 321 rebuilds,
  about 3 ms each, plus two `xms_ornament_glitter` replays of about 27,000 steps (70 ms each). That is seek cost, not a
  playback stall, and it is left for a separate change.

### The sound stalls: the precache missed what `PrecachePhysicsSounds` names

- **Read from published source.** `PrecachePhysicsSounds` (`physics_shared.cpp:1034-1053`, with a caller on the
  client) passes every surface's `stepleft`, `stepright`, `impactsoft`, `impacthard`, `scrapesmooth`, `scraperough`,
  `bulletimpact`, `rolling`, `breaksound` and `strainsound` to `PrecacheScriptSound`, which precaches every wave.
- **What ours did.** `DemoSounds.Precache` named the demo's sounds, the client's emitted ones and the soundscapes, but
  no surface's. Two of run A's three decode stalls are exactly those: a bullet on tile, and a step on glass.
- **The fix** (`58abb759`) adds every surface sound this viewer reads. `rolling`, `breaksound` and `strainsound` are
  not parsed yet and nothing plays them.
- **Measured.** `decodes in play` (the cache's own miss counter) fell from 33–38 to 13.
- **And its cost.** The second precache pass, which carries the client's own sounds, reads `precached 563 of 564
  sounds in 19 ms` without the surface set and `precached 948 of 949 sounds in 85 ms` with it (113 ms in another run,
  763 ms once with a cold disk). Held after playback: 593 sounds and 260 MB without the set, 926 sounds and 277 MB
  with it (WAV 211 MB, MP3 66 MB), far under the 512 MB cap. The surface sounds are precached first, as the engine
  does at level init. The order matters only once the cap is reached, and the engine has no cap: `SoundCache` lets the
  least recently used go from the back, so the first precached would be the first evicted, a step wave before a demo
  sound. They are also precached on the load worker now, behind the B146 barrier; before, the worker had no scripts
  and the 385 surface names decoded in `Apply` on the UI thread.
- **What is left, and it is now most of the jitter.** In the 19:07 run (timings void, see above) seven decodes on first
  play took 170–402 ms: `player/invulnerable_on.wav`, `grenade_launcher_drum_start` and `_close`,
  `stickybomblauncher_boltback`, a near-miss whiz, `vo/null.mp3` and `draw_primary`. Earlier the same:
  viewmodel animation-event sounds: `weapons/draw_primary.wav`,
  `draw_secondary.wav`, `grenade_launcher_drum_close.wav` and `stickybomblauncher_boltback.wav`, plus a near-miss whiz.
  The engine precaches a model's animation-event sounds when the model is precached (its model-sounds cache; this is
  interpolated, not read here). Not done.
- **An oddity, not explained.** Five decodes of different files and sizes (13,856 to 45,824 frames) each took
  396–398 ms. A time that does not vary with the work is not decode time. Nothing in `SoundFile.Open` waits, so the
  time is inside the content reader or the decode, and which one was not shown.

### The HUD stalls: the window, not the demo (interpolated)

All five HUD stalls in run A, and both in run B, are the frame right after a `camera set for a …` line where the
viewport flipped between 984×551 and 1640×879. That ratio is 1.67, which looks like a window moving between monitors
with different scaling. It happened during agent runs on a desktop the owner was using, and no run without the flips
showed a HUD stall. That a HUD relayout costs 85–404 ms per resize is ours, and it is worth knowing; a user does not
resize every few seconds, so it is not the jitter the owner reported. Not fixed.

### Counts

Frames over 100 ms per minute, f12 tick 30000, first person, Debug, `--measure 60`, one machine:

| run | build | gate | frames over 100 ms | of them: particles / sound / HUD | `decodes in play` |
|---|---|---|---:|---|---:|
| A | `f1652da6` + print | quiet; checked after | 12 | 1 / 4 / 5 | 33–38 (earlier runs) |
| B | + sound fix + print | quiet; checked after | 5 | 1 / 1 / 2 | — |
| C | + both burst fixes (the landing rule, since replaced) | quiet before; **100% load after (other agents' builds)**, void | 1 (the frame after the seek) | 0 / 0 / 0 | 13 |
| D | same build as C | quiet before (16%) and after (10%, no build CPU) | **1** (112 ms, the frame after the seek) | 0 / 0 / 0 | 13 |

**So, 12 → 1 frames over 100 ms per minute.** The one left in C and D is the frame that ends the opening seek, which
`--measure` counts from. Run A's HUD stalls came from the window and do not appear in C or D, so the honest count for
what the owner reported is 7 → 0 (particles and sound). That rests on one quiet run each way plus B, so it is a count,
not a rate. Seconds without a stall also read cleaner: D's per-second means were 54–81 fps.

## The sleep and wake audit (2026-10-20, in progress)

**The engine, read from published source** (with the coordinator's Ghidra read of `particles.obj` for `Render`):

- **The skip.** `BuildParticleSimList` skips an effect when `flNow >= m_flNextSleepTime && m_nActiveParticles > 0`
  (`particlemgr.cpp:1775`), the root collection's own count; children simulate with it. `flNow` is this frame's
  `curtime`, set at the top of `UpdateNewEffects` (:1833). A skipped effect is not simulated unless `HasMoved()`, which
  sends it to a bounds-only simulate (:1787-1791). Nothing adds a skipped frame back: the collection's clock moves only
  inside `Simulate`.
- **The frame's step.** `UpdateAllEffects` clamps the frame time to 0.1 s (:1947) and passes it to `UpdateNewEffects`
  (:1995); `ProcessPSystem` simulates every listed collection with it (:1579), and a new one's first frame with 0 (:1574).
- **What pushes the sleep time.** Only drawing: `CParticleCollection::Render` (Ghidra) and the too-far early-out of
  `CNewParticleEffect::DrawModel`, which still pushes it "or they won't follow their attachment points"
  (`particles_new.cpp:528-538`). `DrawModel` is reached only for a renderable the leaf system collected for a view:
  its world box in a leaf of the view's world list, inside the frustum (`CullBox`, or the area frustum under
  `r_PortalTestEnts`), with nonzero `FxBlend` (`clientleafsystem.cpp:1574-1716`). Occlusion is tested only for studio
  models. `cl_maxrenderable_dist` does not apply in TF2: its test is inside `#ifdef INVASION_CLIENT_DLL`
  (:1659-1676).

**Measured first** (f12 tick 30000, first person, `--measure 60`, the new `particle work` line; one run; its
after-check sampled a job queued behind the lock, not the playback window, so its milliseconds stand): 368.4 effects
held a frame (most of them finished markers), **22.2 steps a frame, 0 asleep, 21.9 effects with particles handed to the
draw**; particle cost 1.28 ms a frame searching the timeline for offers and 0.41 ms starting and stepping bursts; the
`camera` phase 9.0 ms. So stepping is at most 0.41 ms a frame here, and sleep can save at most part of that.

**Divergences, and what was done:**

| | engine | ours | status |
|---|---|---|---|
| D1 | the sleep time is pushed only when the effect is drawn: collated for a view by leaf, frustum and `FxBlend` | pushed at simulate time whenever the burst can be **placed** (its owner posed); every placed effect counts as drawn | **open, needs a decision** (below) |
| D2 | every effect sleeps | a projectile trail (`Update`) has no sleep at all | open; follows D1 |
| D3 | a frame simulates at most 0.1 s | a played frame stepped however long | **fixed** (`fix: a particle frame simulates at most 0.1 s`) |
| D4 | an effect undrawn since creation sleeps at creation plus the no-draw time | a replay treats every replayed tick as drawn (B565) | open, the owner's choice (below) |
| D5 | a skipped effect that moved updates its bounds only | not modelled; it changes bounds and nothing drawn | not done, by choice |

**D1 needs a decision, not a guess.** Our particle gather culls nothing: every effect is gathered for every frame and
view (`ParticleEffects.Build`), so "drawn" in the engine's sense does not exist here yet. Modelling it needs each effect's
world box tested against the view's world-list leaves and frustum. This viewer has that collation twice, in `Device3D`
and in the `EntityModels` collation partial (`ClientLeafSystem`), and the particle gather is in `FrameParticles`, which
has the camera but neither. Two routes: register particle effects in a leaf system beside the entities (the engine's
shape, but it touches the collation partial another job is splitting), or answer the box question inside the particle
path from the map's leaves and the view's frustum. Which one is the coordinator's call.

**D4 is the owner's, with the trade named.** A seek's replay is ours (D221), and nobody knows what the camera saw before
the seek. Replayed as drawn (as now), a long effect is fully simulated: about 70 ms for each `xms_ornament_glitter`.
Replayed as undrawn, it sleeps 8 s after creation and the replay is cheap, but an effect born more than 8 s before the
seek is then shown frozen mid-life. Neither is the engine.

**The age bound waits on D1.** Once sleep is modelled, the age skip can apply only to an effect whose emission and
lifetime fit within its root's sleep, since only those finish before they could sleep; for the rest the bound is
unbounded (review item 1, held).

