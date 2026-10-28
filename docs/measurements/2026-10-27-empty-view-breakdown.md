# Measurement 2026-10-27: where a blank view's 3.44 ms goes, pass by pass, against the engine (f12)

**Status: the untimed half (Observation to Control, and the engine audit) was written and committed before any run.**
Sections above `## Environment` are fixed from here; deviations go under Result.

## Observation

The lead, 2026-10-27, on f12 at `TF2VIEW_CAMERA="1613 -2821 154 89 0"`, `+fps_max 0`: frame 3.44-3.45 ms (~290 fps) =
camera 1.5-2.0 (particle decals 0.1-0.3), advance 0.5, hud 0.6, sound 0.1, draw 0; moment cost 0.9 (sample 0.3, drawlist 0.1,
pose 0.5 incl. simulate 0.3). The owner: *"that really really should be basically infinite"*. TF2 facing a wall on
`cp_badlands` is 0.81-0.88 ms (B259's reference; a different map, so an order of magnitude, not a pair).

**Configuration caveat.** The brief says "first person" together with the camera override. `tf2-measure` (finding 82):
`--first-person` follows the player and ignores `TF2VIEW_CAMERA`, so a first-person run is not an empty view. The timed
runs below drop `--first-person` and prove the view empty (`posed 0 of N`, a `--shot`). Which configuration the lead's
figures came from is not established here.

**How the lines nest** (so no row below is added to its parent): the top level is `capture + advance + camera + sound +
project + hud + draw + between` (`FrameSequence.Run`). The moment's `sample`, `drawlist` and `models` run inside `advance`;
its `pose` runs inside `camera` (`camera pose`). `camera particles` holds lights, decals, sounds, impacts, trails, bursts
(itself explosions … overheals and `step`), build and hand. `hud frame` holds scheme … paint; `hud paint` holds the
viewport's children by `.res` name.

## The engine audit, per pass, with nothing on screen (untimed)

Evidence class on each line: **read** (source or disassembly read today), **record** (an earlier record in this repo, cited),
**interpolated**.

| pass | what the engine does with nothing on screen | what we do | verdict |
|---|---|---|---|
| `hud paint` → `HudChat` | `RichText::Paint` (vgui_controls/RichText.cpp:714-) walks the visible lines from `GetStartDrawIndex` every frame, faded or not; `DrawString` (:610-665) measures every character with `GetKernedCharWidth` and skips only `DrawPrintText` at alpha 0 (:657). `CBaseHudChat::OnTick` (hud_basechat.cpp:1064-1099) re-sets the history's bounds and `FadeChatHistory` (:1291) every tick. **read** | The same walk, the same measurement, the same alpha-0 skip (`VguiRichText.Paint`, `DrawString`). | **Shape is parity.** Record. A layout cache or a trim is above-Valve and already queued (handoff 2026-10-27: "HudChat above-Valve layout cache", owner decision "trim HudChat") — not taken here. |
| every HUD text call (chat, cards, labels) | A font handle is an INDEX. `CFontManager::GetCharABCwide` (vguimatsurface.dll 0x180016a70): `TEST EDX,EDX; JS` / `CMP EDX,[RCX+0x10]; JGE` — a bounds check — then `handle * 72` into the amalgam array. **O(1). read (disassembly)** | `VguiFontManager` keeps the handles in a `List` and asks `_handles.Contains(font)` — a linear scan — in `GetCharAbcWide`, `GetCharacterWidth` (twice: itself and through `GetCharAbcWide`) and `GetFontTall`. `VguiRichText.DrawString` and `VguiDrawList.DrawPrintText` call these once or twice per CHARACTER. Each scheme load makes two handles per font (`VguiSchemeFonts.cs:57-60`). **read** | **Divergence (mechanism): O(handles) per character where the engine is O(1).** Fixed here: a reference-keyed set, same answers. How many handles, and what it costs, is the timed half's question (interpolated: several hundred, so a few hundred comparisons per character). |
| `hud events` | Events delivered as their packets are read. **read (B579)** | Window search since B579. **record** (`2026-10-23-hud-event-feed-window.md`: 0.13-0.20 → 0-0.02 ms) | Fixed earlier. |
| `hud precache` | `PrecacheModel` on a change. | `string.Join` of the panel models every frame, compared. **read** | ~0.01 ms; an allocation a frame. Below the 0.01 ms bar on the record; filed in the report, not fixed. |
| `specgui`, cards | `CTFSpectatorGUI` paints its player panels every frame while shown; `RecalculatePlayerPanels` throttled to `curtime + 0.1f` (client/tf/vgui/tf_spectatorgui.cpp:333-336). **read** | Same. | Parity. Record. |
| `camera particles/bursts/step` | `CParticleMgr::UpdateNewEffects` (particlemgr.cpp:1824-) calls `Update` on EVERY live effect each frame (:1844-1848); `BuildParticleSimList` (:1766-1799) skips the simulation of one past `m_flNextSleepTime` (set from `m_flNoDrawTimeToGoToSleep`, particles_new.cpp:536) unless it has moved. **read** | `ParticleEffects` steps live effects and counts `Slept` (finding 84). **record** | Parity in shape: an unseen effect still updates until it sleeps, in the engine too. Record. Its size is the timed half's question. |
| `camera particles/bursts/*` offers | Each effect is created ONCE when its TE arrives: `C_TEEffectDispatch::PostDataUpdate` (client/c_te_effect_dispatch.cpp:146-155) calls `DispatchEffectToCallback` once; a held effect's control points are all re-read by `CParticleProperty::UpdateParticleEffect` (shared/particle_property.cpp:506-519) each update. **read** | A window search per source per frame, then a fresh offer per live effect, with an attachment query for a following one (muzzles, dispatches). **read** | The per-frame attachment query is the engine's for a following effect. The per-frame search and re-offer is ours; the dispatch split is B577's, queued for another agent. Not taken here. |
| `camera particles/decals` | A decal is placed when its effect arrives (`ImpactCallback` → `AddStudioDecal`); nothing per frame. **record (DecalEffects comments)** | `StepModelDecals` searches the arrival window (`LandedWindow`), and `ClearOnChange` visits every player, per frame. **read** | Arrival-window search is ours (cheap, measured earlier); the per-player clear check is the engine's `C_TFPlayer` clears on state change. The 0.1-0.3 ms is the timed half's question; no fix decided. |
| `camera particles/build` | `CNewParticleEffect` is a leaf-system renderable; `DetectChanges` (client/particles_new.cpp:370-404) runs per live effect and re-links only when its box leaves the padded last box; only collated effects render. **read** | `ParticleEffects.Build` files every live effect by its box (`CollateEffects`, `FileEffect`), re-linking only on a change, collates the main and sky leaves, and gathers quads only for collated effects (finding 84). **read** | **Parity in shape** — a per-live-effect box check in both, quads only for the collated. Record. What the box (`effect.Bounds()`) costs per effect is the timed half's question. |
| `camera pose` → `simulate` | `UpdateClientSideAnimations` for every entity on the client-side animation list. **record (B259 2026-10-23)** | Same since `f61c7602`. | Parity. Record. |
| `camera pose` → `collate` | `BuildRenderablesList` starts from the visible leaves. **record (B259)** | Walks every entity (~230 on f12). | **B259, open.** PVS node prune agent is in this area; not touched here. |
| `advance` → `sample` | `ProcessInterpolatedList` walks a maintained list. **record (B259)** | Walks every track since stage C only for changed ones; ~0.3 ms left. | **B259, open.** Not touched here. |
| `advance` → `drawlist`, `models` | Leaf-list-driven; model resolved on data update. **record (B259)** | ~0.1 ms each. | B259, open. Not touched here. |
| `sound` | `S_Update` mixes active channels. Closed engine; not read today. | 0.1 ms. | Not audited (below the HUD and particles by an order). |

## Question

1. On f12, camera override, no `--first-person`, nothing posed: what does each top-level phase and each part cost, in ms and
   as % of the run's frame mean, with the first-use (miss) frames apart from the steady (hit) ones?
2. Does replacing `VguiFontManager`'s linear handle check with a constant-time one change `hud paint` (and `HudChat` inside
   it), by more than the run-to-run spread, with the frame pixel-identical?

## Prediction

- (2) `hud paint` falls by 0.1-0.3 ms, most of it in `HudChat`; `hud` total by the same. Falsified if the after build's
  `hud paint` is inside the before build's own spread. **Interpolated:** the handle count and the per-character call count.
- (1) camera largest (particles above pose), then hud, then advance — the 2026-10-23 shape.

## Method

Two Release builds copied out of the tree to `D:/measure-f12/empty-before` (this branch's parent) and `empty-after` (this
branch). Runs only after the lead's go; one at a time, each behind the quiet gate (three
`Win32_Processor.LoadPercentage` samples under 25%, no other `tf2demoview`/`dotnet`/`MSBuild`/`testhost`), each under
`run-exclusive.ps1`:

`TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 TF2VIEW_CAMERA="1613 -2821 154 89 0" <exe> D:/measure-f12/f12.dem
--tick 30000 --measure 20 +fps_max 0`

Order ABBA then BAAB (A = before, B = after): eight runs. **Plus one continuity run per build** (lead, 2026-10-27: the
form behind the 3.44 ms is not known): the same command with `--spectate gummo --first-person` in place of the camera,
reported beside the empty view and never pooled with it. Identity: one `--shot` per build at the same tick and camera,
compared by hash and by pixel. Reading: per-second lines after the first five (steady, the hit path) frame-weighted; the
first five reported apart (first-use builds, the miss path, `a-split-carries-its-first-use-builds.md`). % = part / that run's
`measured … mean`.

## Control

- Ticks advanced ~1,333 per 20 s window.
- `posed 0 of N` in every moment line, and the `--shot` shows no model — else the view is not empty and the run is void.
- `HudChat` present and non-zero in the before runs (the instrument sees the subject).
- The per-second phase sums equal the frame to within 0.1 ms (no untimed residual).
- Runs of one build agree within 15% on `hud paint`, else the spread is stated and no difference claimed.

## Environment

RTX 3060, i5-10600K, 2026-10-27 20:21-23:55. Release builds: A = `a706d1e4` (main's code) built from a `git archive` at
`D:/measure-f12/evb-before-src`, copied to `D:/measure-f12/evb-before`; B = `373b8e7b`'s code (this branch) copied to
`D:/measure-f12/evb-after`. `mat_queue_mode` default. Scripts and raw output beside this file:
`2026-10-27-empty-view-breakdown/` — `run.ps1` (one gated run), `all.ps1` (the order), `read.ps1` (the parse), `NN-X.txt`.

Load before / after each run (3 × 1 s, %), every run passed the gate before starting:

| run | before | after | | run | before | after |
|---|---|---|---|---|---|---|
| 01-A | 3 13 6 | 14 7 8 | | 05-B | 15 17 13 | 48 32 34 |
| 02-B | 19 6 3 | 91 94 100 | | 06-A | 7 14 6 | 14 4 7 |
| 03-B | 12 12 12 | 30 18 22 | | 07-A | 4 6 6 | 11 15 17 |
| 04-A | 2 4 11 | 55 74 100 | | 08-B | 21 17 22 | 41 42 28 |
| fp-A | 6 9 2 | 24 19 19 | | fp-B | 7 8 3 | 4 93 79 |

The high after-reads are another agent's work starting as a run ended (the B607 agent's `-Shared` builds and tests); a
run's phases do not move with them (02-B and 04-A sit inside their build's range on every phase), so none is voided.

**Deviations from the plan.** (1) The gate stalled 20:55-21:47: it counted another agent's idle MSBuild worker nodes
(`/nodemode:1`, `nodeReuse:true`) as busy while load was under 25%. Worker nodes are now excluded (the driving `dotnet`
process and the load samples still gate); the plan resumed at 03. (2) The first two `--shot`s ran under autoplay and
landed at ticks 33355 and 33178; they were retaken PAUSED at tick 30000 (`shot-A2`, `shot-B2`, control `shot-A3`).

## Result

**Controls.** Every empty run ended at tick 31347-31348 (~1,347 ticks over 20 s). Every moment line reads `posed 0 of
1,852-1,888`, and the paused shot is a flat surface with the HUD only — the view is empty. `HudChat` is non-zero in every
A run. The steady per-second phases sum to the frame within rounding (`unaccounted 0`). Runs of one build agree on
`hud paint` within 15% except 01-A (0.62 against 0.46-0.51), the first run of the session; it is kept and the range shown.

**The table: means of the four runs of each build** (range in brackets), steady per-second lines after the first five,
frame-weighted; **%** of that run's `measured … mean`; **first use** = the first five lines (the miss path: archives,
JIT, first builds), frame-weighted. Nesting as in the Observation; nested rows are never added to their parent.

| pass | A (main) ms | A % | B (fix) ms | B % | first use A / B |
|---|---|---|---|---|---|
| **frame mean** (whole window) | **4.28** (3.88-5.16) | | **3.71** (3.32-4.04) | | |
| `camera` | 2.15 (1.83-2.89) | 49.9 | 2.16 (1.83-2.49) | 58.0 | 3.42 / 3.11 |
| ↳ `camera pose` (the moment's pose) | 0.59 | 14.0 | 0.58 | 15.6 | 0.71 / 0.67 |
| ↳ `camera particles` | 1.52 (1.28-2.12) | 35.1 | 1.55 (1.27-1.84) | 41.5 | 2.69 / 2.42 |
| ↳↳ `decals` | 0.17 | 3.9 | 0.14 | 3.9 | 0.35 / 0.28 |
| ↳↳ `bursts` | 0.82 (0.65-1.25) | 18.7 | 0.89 (0.65-1.13) | 23.8 | 1.07 / 1.02 |
| ↳↳↳ `bursts/step` | 0.32 (0.19-0.69) | 7.1 | 0.41 (0.20-0.63) | 10.9 | 0.30 / 0.28 |
| ↳↳↳ `bursts/muzzles` | 0.22 | 5.2 | 0.22 | 5.8 | 0.27 / 0.26 |
| ↳↳↳ `bursts/dispatches` | 0.16 | 3.7 | 0.15 | 4.1 | 0.32 / 0.30 |
| ↳↳ `build` | 0.33 | 7.7 | 0.31 | 8.5 | 0.83 / 0.73 |
| ↳ `camera wait` | 0.02 | 0.3 | 0.02 | 0.5 | 0 / 0 |
| `hud` | **0.68** (0.61-0.81) | 15.9 | **0.27** (0.25-0.30) | 7.3 | 1.04 / 0.38 |
| ↳ `hud frame` | 0.60 | 14.0 | 0.22 | 5.8 | 0.93 / 0.32 |
| ↳↳ `hud paint` | 0.52 (0.46-0.62) | 12.1 | 0.14 (0.13-0.15) | 3.8 | 0.82 / 0.22 |
| ↳↳↳ `HudChat` | **0.33** (0.29-0.39) | 7.7 | **0.011** (0.011-0.012) | 0.3 | 0.54 / 0.05 |
| ↳↳↳ `specgui` | 0.15 | 3.4 | 0.09 | 2.5 | 0.20 / 0.11 |
| ↳↳ `hud layout` | 0.06 | 1.5 | 0.06 | 1.6 | 0.08 / 0.07 |
| ↳ `hud tools` | 0.04 | 0.9 | 0.01 | 0.3 | 0.05 / 0.02 |
| `advance` | 0.62 (0.55-0.72) | 14.6 | 0.59 (0.55-0.65) | 15.9 | 0.82 / 0.71 |
| `sound` | 0.11 | 2.6 | 0.11 | 3.0 | 0.15 / 0.12 |
| `draw` (the hand-off; mode 2) | 0.00 | 0 | 0.00 | 0 | |
| `between` (whole window) | 0.24-0.35 | | 0.23-0.29 | | |

Moment lines (mean of 15-17 rebuilds a run, both builds alike): sample 0.31-0.40, drawlist 0.18-0.22, models 0.01-0.05,
pose 0.56-0.72 = simulate 0.32-0.42 + collate 0.20-0.26. gc 0.10-0.14 ms a frame, 0.53-0.61 MB allocated a frame.

**First person, one run per build** (`--spectate gummo --first-person`; posed 14-50 of ~1,870, so NOT an empty view):

| | frame | camera | camera pose | camera particles | hud | hud paint | HudChat |
|---|---|---|---|---|---|---|---|
| fp-A | 8.04 | 5.42 | 2.91 | 1.46 | 0.64 (8.0%) | 0.47 | 0.29 |
| fp-B | 8.86 | 6.77 | 3.21 | 2.31 | 0.33 (3.7%) | 0.15 | 0.011 |

One run each, so no frame or camera difference is claimed: fp-B's `bursts/step` is 0.85 against 0.21, the same
burst-step spread the empty runs show (0.19-0.69 within build A alone).

**Identity.** Paused at tick 30000: A2 against B2 differ in 49 pixels, all inside (1101,2)-(1119,10), the digits of the
frame-time overlay (`cl_showfps 2`), which reads each build's own frame time. The control, A2 against A3 of the SAME build,
differs in 53,167 pixels: A3 loaded fast enough that the chat history had not faded yet. **The chat fades on the wall clock**
(`hud_saytext_time`, `CHAT_HISTORY_IDLE_FADE_TIME`), so a shot of the HUD is not deterministic between runs; A2 and B2
happened to land in the same fade state. The world is identical in all three. That is as far as pixel identity goes here.

## Verdict

**The font-handle check was the HUD's empty-view cost.** `HudChat` falls from 0.29-0.39 ms to 0.011 ms in every run, `hud`
from 0.61-0.81 ms (15.9% of the frame) to 0.25-0.30 ms (7.3%) — 0.41 ms a frame, outside both builds' spreads — and the
first-person view shows the same 0.31 ms. The frame mean falls 4.28 → 3.71 ms on average, but the ranges overlap
(3.88-5.16 against 3.32-4.04), so only the HUD's part of it is claimed.

**What the empty view costs now, and whose it is:** camera 2.16 ms (58%), of which particles 1.55 (bursts 0.89, build
0.31, decals 0.14) and the pose 0.58; advance 0.59 (sample, drawlist, models); hud 0.27; sound 0.11; between ~0.26. The
particle step and build are the engine's shape (an effect updates until it sleeps; every live effect's box is checked
each frame) and are recorded, not changed; the muzzles and dispatches offers are B577's; sample, drawlist and collate are
B259's. **Nothing in this record makes 3.7 ms "basically infinite"**: the remaining floor is the particles' per-effect
work and B259's per-entity walks, each audited above.
