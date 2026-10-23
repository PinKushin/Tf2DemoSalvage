# 102 — The empty view's floor, stage by stage, against what the engine does with nothing in view

**Question.** With the camera pointed at the sky on f12 (`1613 -2821 154 89 0`, tick 30000), the viewer still pays about
5 ms a frame. Which stages pay it, what does each one walk, and which of them does the engine skip when nothing is in
view?

## The floor, measured by the viewer's own timers

Debug build, `--measure 20 +fps_max 0`. Source: the fastest run of `measurements/2026-10-23-empty-view-floor-quiet.md`
(4.99 ms) and the base runs of `measurements/2026-10-23-blend-at-bone-setup-f12.md` (4.93-5.02 ms). **The same build's
empty view read 4.99-9.23 ms on the same day**, and every phase grew together in the slow runs. So the shape below is
quoted and no single floor figure is. *Measured.*

| stage (per-second means, ms) | empty | what it walks with nothing in view | does the engine? |
|---|---:|---|---|
| `camera particles` | 1.2-1.8 | | |
| — `bursts` (offers + step) | 0.6-0.8 | the timeline windows of explosions, tracers, dispatches, muzzle flashes; ~42 live effects stepped | yes, see below |
| — `build` | 0.26-0.32 | every live effect and the effect entities (sprites, beams, ropes, trails) of the draw list | partly; not read further |
| — `decals` | 0.12-0.51 | this frame's bullet landings, traced against hitboxes | yes, see below |
| `camera pose` | 0.7 → **0.5** after fix 1 | | |
| — `simulate` | 0.5-0.6 → **0.3-0.4** | every entity: cycle, events, placement; **before fix 1 also every entity's layers, autoplay and IK** | cycle and events yes; the blend **no** |
| — `collate` | 0.2 | the ~230 entities' leaf handles (static props are filed once and not visited) | yes, on change |
| `hud` | 0.8 | every panel's think and paint; `HudChat` 0.34, `specgui` 0.14, events 0.13 | yes (VGUI ~0.8 ms in TF2's vprof) |
| `advance` (rebuild) | 0.7 | `sample` 0.3 every track; `drawlist` 0.1-0.2 every prop through the visibility rules; `models` 0.1 | see B259 |
| `between`, `draw`, `sound` | 0.3, 0.2, 0.2 | | |

`posed 0 of ~1,882 selected, ~1,653 hidden by PVS`: no prop reaches bone setup. The per-second phases sum to the frame
within 0.1 ms, so nothing large is untimed.

## What the engine does, stage by stage

- **The blend is built inside `SetupBones`, not for every entity.** `C_BaseAnimating::StandardBlendingRules`
  (`c_baseanimating.cpp:1953-2018`): `AccumulatePose` for the main sequence, `MaintainSequenceTransitions`,
  `AccumulateLayers`, `CalcAutoplaySequences`, `CalcBoneAdj`. It is called from `SetupBones` (`:2796`). Every frame, for
  every client-animating entity, the engine runs only `UpdateClientSideAnimations` (`:6368`): the cycle and the events.
  Ours built every skinned entity's layers, autoplay and IK rules in `Simulate`, whether or not anything set its bones
  up. *Read from published source.* **This was the floor's one clear whole-set walk the engine does not make. Fixed (fix 1
  below).**
- **An attachment's query is a bone setup in the engine too.** `CalcAttachments` is
  `SetupBones( NULL, -1, BONE_USED_BY_ATTACHMENT, gpGlobals->curtime )` (`c_baseanimating.cpp:2094`), and
  `CParticleMgr::UpdateNewEffects` updates every effect's control points every frame, *"done on all particles"*
  (`particlemgr.cpp:1841-1853`). So the ~8 skeletons a frame that the burst offers build for muzzles and followed
  effects nobody sees are parity. This answers B572's third question. After fix 1 they also build those entities'
  blends, which is the engine's order. *Read from published source.*
- **An undrawn effect's simulation sleeps, and ours already does.** `BuildParticleSimList` skips an effect at or past
  `m_flNextSleepTime` (`particlemgr.cpp:1775`). This was ported in finding 79. The f12 window showed "0 asleep" because its
  ~42 effects are short-lived bursts, younger than their no-draw sleep time. *Measured; read from published source.*
- **Model decals are already windowed** to this frame's landings (`measurements/2026-10-22-model-decal-window.md`). What
  remains is client bullets traced against hitboxes, which `FireBullet` does on every client. Taken as parity.
- **Dispatches walk every entity-owned effect fired so far, every frame** (`BurstEffects.AddParticleDispatches`, the
  `_owned` loop, skipping finished ones). The engine sees each dispatch once, when it arrives. This is a whole-timeline
  walk the engine does not make, but the whole `dispatches` part is 0.10-0.17 ms. *Not fixed; filed in B572.*
- **`HudChat` paints 0.34 ms with no text on screen.** Its `RichText.Paint` port walks the whole faded history. *Not read
  against Valve's `RichText::Paint` yet. Filed in B572.*

## Fix 1 — the blend inputs at bone setup (`f61c7602`)

`AnimatingEntity.BeforeBuild` builds the local and sequence autolayers, the transitions (queued in `Simulate`), the
entity's layers and gestures, the autoplay sequences, and the IK rules. It runs on the entity's first bone request of the
frame. `Simulate` keeps the cycle, the events, the transitions queue, the duck jump, the IK chains and locks, and the
placement. A culled entity builds no layers (`BlendingRulesConformanceTests`, red before the change). One that is asked
for through a query does build them.

**Kept divergence:** `TransitionsFor` still runs for every entity in `Simulate`. In the engine
`MaintainSequenceTransitions` runs only inside `StandardBlendingRules`, so an entity out of view misses a sequence change
there. Moving it would change what an entity re-entering the view blends. It is cheap, so equivalence was chosen (B572).

**The corpse worker is unaffected.** It poses its own `EntityModelSet` (`CorpseRecord.Run`), so no entity is set up from
two threads.

**Measured** (`measurements/2026-10-23-blend-at-bone-setup-f12.md`, interleaved, 43-56% mean in-run load):

| view | `simulate` base → fix 1 (ms) | frame base → fix 1 (ms) |
|---|---|---|
| empty | 0.5-0.6 → 0.3-0.4 | 4.93, 4.97, 5.02 → 4.74, 4.86, 4.95, 5.02 |
| gummo first person | 0.5-0.7 → 0.3-0.5 (in-view players' blends move into `setup`) | 13.03, 12.95 → 12.67, 12.76 |

The stage timer drops about 0.2 ms in both views. The empty-view frame means overlap, so no frame gain is claimed there.
The gummo pairs are 0.2-0.4 ms faster each time, under load.

**Equivalence: not provable by hash on this build.** Base's own `--shot` does not reproduce. Two base shots at tick 30400
differ on 63 of 921,600 pixels, and at 30000 on 41,710. Fix 1 against base differs on 70 and 73 pixels. So fix 1's
picture is inside base's own run-to-run variation, but it is not shown byte-identical. Why base's shot varies run to run
is open: an unfixed timing input to the shot, *interpolated*.

## What the floor is, then

Most of the ~5 ms is work the engine also does: particle control points and their bone setups, bullet traces, VGUI,
sound, the cycle advance. It is done here more slowly per item (finding 101: the gap is a long tail, not one walk). The
remaining whole-set walks are small: `sample` 0.3, `drawlist` 0.1-0.2, `collate` 0.2 (B259), and the dispatch walk. The
largest one the engine does not make was the blend, now fixed.
