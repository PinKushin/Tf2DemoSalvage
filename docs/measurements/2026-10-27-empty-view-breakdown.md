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

The high after-reads are another agent's work starting as a run ended (the B607 agent's `-Shared` builds and tests).
**Each loaded run judged against the OTHER runs of its build** (steady figures, below):

- **02-B** (after 91/94/100) against 03-B, 05-B, 08-B: frame 3.44 against 2.90-3.72; `hud` 0.250 against 0.253-0.300;
  `camera` 2.27 against 1.83-2.49. Inside on frame and camera, 1% under on `hud`. Two slow frames (56 and 55 ms, at 11.7
  and 12.6 s). Kept.
- **04-A** (after 55/74/100) against 01-A, 06-A, 07-A: frame 3.57 against 3.29-4.88 (2% above 06-A and 07-A's 3.29-3.49);
  `hud` 0.692 against 0.608-0.805. No slow frames. Kept.
- **fp-B** (after 4/93/79): **14 slow frames of 51-79 ms, all "mostly camera"**, 9.587-11.992 s (`fp-B.txt`), none in
  fp-A, fitting the load that overlapped it. **Void for frame, camera and particles**; its `hud` figures are kept as
  supporting only (one run, no spread): the slow frames are camera-bound, and `hud` reads 0.5-1.2 ms in them, so the load
  may have touched it too.
- **01-A** (quiet: 3/13/6 before, 14/7/8 after) is the outlier with no load to blame: frame 4.88 against 3.29-3.57,
  `camera` 2.89 against 1.83-1.96, `bursts/step` 0.69 against 0.19-0.21. Kept, and every claim below is also stated with
  it excluded. **`bursts/step` has two modes regardless of build**: 0.59-0.69 ms in 01-A, 02-B, 08-B and 0.19-0.23 in the
  other five. What decides the mode is not established.

**Deviations from the plan.** (1) The gate stalled 20:55-21:47: it counted another agent's idle MSBuild worker nodes
(`/nodemode:1`, `nodeReuse:true`) as busy while load was under 25%. Worker nodes are now excluded (the driving `dotnet`
process and the load samples still gate); the plan resumed at 03. (2) The first two `--shot`s ran under autoplay and
landed at ticks 33355 and 33178; they were retaken PAUSED at tick 30000 (`shot-A2`, `shot-B2`, control `shot-A3`).
(3) **The 15% control failed for build A:** `hud paint` 0.62 in 01-A against 0.46-0.51 in the others (a 35% spread
across all four). The plan said a difference is then not claimed. It is claimed anyway, on separation the control did not
anticipate: B's largest `hud paint` (0.15) is under a third of A's smallest (0.46), and with 01-A excluded A's three runs
agree within 11% (0.46-0.51). (4) The % column divides by the steady window's own frame mean, not the whole window's
`measured … mean` — see the table's note.

## Result

**Controls.** Every empty run ended at tick 31347-31348 (~1,347 ticks over 20 s). Every moment line reads `posed 0 of
1,852-1,888`, and the paused shot is a flat surface with the HUD only — the view is empty. `HudChat` is non-zero in every
A run. The steady per-second phases sum to the frame within rounding (`unaccounted 0`). The 15% control on `hud paint`
failed for build A (deviation 3).

**The table: means of the four runs of each build** (range in brackets), steady per-second lines after the first five,
frame-weighted; **%** of that run's STEADY frame mean (1000 ms × steady lines ÷ steady frames — the whole window's
`measured … mean` also counts the first-use seconds, so dividing a steady part by it understated every share);
**first use** = the first five lines (the miss path: archives, JIT, first builds), frame-weighted. Nesting as in the
Observation; nested rows are never added to their parent.

| pass | A (main) ms | A % | B (fix) ms | B % | first use A / B |
|---|---|---|---|---|---|
| **frame, steady window** (the % denominator) | **3.81** (3.29-4.88) | | **3.35** (2.90-3.72) | | 5.76 / 4.65 |
| frame, whole window (`measured … mean`) | 4.28 (3.88-5.16) | | 3.71 (3.32-4.04) | | |
| `capture` | 0.00 | 0 | 0.00 | 0 | |
| `advance` | 0.62 (0.55-0.72) | 16.5 | 0.59 (0.55-0.65) | 17.7 | 0.82 / 0.71 |
| `camera` | 2.15 (1.83-2.89) | 56.3 | 2.16 (1.83-2.49) | 64.3 | 3.42 / 3.11 |
| ↳ `camera pose` (the moment's pose) | 0.59 | 15.8 | 0.58 | 17.4 | 0.71 / 0.67 |
| ↳ `camera particles` | 1.52 (1.28-2.12) | 39.5 | 1.55 (1.27-1.84) | 46.0 | 2.69 / 2.42 |
| ↳↳ `decals` | 0.17 | 4.4 | 0.14 | 4.3 | 0.35 / 0.28 |
| ↳↳ `bursts` | 0.82 (0.65-1.25) | 21.0 | 0.89 (0.65-1.13) | 26.3 | 1.07 / 1.02 |
| ↳↳↳ `bursts/step` (two modes, see above) | 0.32 (0.19-0.69) | 7.8 | 0.41 (0.20-0.63) | 12.0 | 0.30 / 0.28 |
| ↳↳↳ `bursts/muzzles` | 0.22 | 5.9 | 0.22 | 6.5 | 0.27 / 0.26 |
| ↳↳↳ `bursts/dispatches` | 0.16 | 4.2 | 0.15 | 4.5 | 0.32 / 0.30 |
| ↳↳ `build` | 0.33 | 8.8 | 0.31 | 9.4 | 0.83 / 0.73 |
| ↳ `camera wait` | 0.02 | 0.4 | 0.02 | 0.6 | 0 / 0 |
| `sound` | 0.11 | 2.9 | 0.11 | 3.3 | 0.15 / 0.12 |
| `project` | 0.00 | 0 | 0.00 | 0 | |
| `hud` | **0.68** (0.61-0.81) | 18.0 | **0.27** (0.25-0.30) | 8.1 | 1.04 / 0.38 |
| ↳ `hud frame` | 0.60 | 15.9 | 0.22 | 6.5 | 0.93 / 0.32 |
| ↳↳ `hud paint` | 0.52 (0.46-0.62) | 13.7 | 0.14 (0.13-0.15) | 4.1 | 0.82 / 0.22 |
| ↳↳↳ `HudChat` | **0.33** (0.29-0.39) | 8.7 | **0.011** (0.011-0.012) | 0.3 | 0.54 / 0.05 |
| ↳↳↳ `specgui` | 0.15 | 3.9 | 0.09 | 2.7 | 0.20 / 0.11 |
| ↳↳ `hud layout` | 0.06 | 1.7 | 0.06 | 1.7 | 0.08 / 0.07 |
| ↳ `hud tools` | 0.04 | 1.0 | 0.01 | 0.3 | 0.05 / 0.02 |
| `draw` (the hand-off; mode 2) | 0.00 | 0 | 0.00 | 0 | |
| `between` (whole window only; not in the per-second lines) | 0.24-0.35 | | 0.23-0.29 | | |

**3.44 ms was not reproduced as a whole-window mean** (A: 3.88-5.16). *Inferred:* it matches the STEADY window (A 3.29-3.57
without 01-A, B 2.90-3.72), so the likeliest cause is a figure read after warm-up, or a different configuration (the brief
named first person); neither is established.

Moment lines (mean of 15-17 rebuilds a run, both builds alike): sample 0.31-0.40, drawlist 0.18-0.22, models 0.01-0.05,
pose 0.56-0.72 = simulate 0.32-0.42 + collate 0.20-0.26. gc 0.10-0.14 ms a frame, 0.53-0.61 MB allocated a frame.

**First person, one run per build** (`--spectate gummo --first-person`; posed 14-50 of ~1,870, so NOT an empty view):

| | steady frame | camera | camera pose | camera particles | hud | hud paint | HudChat |
|---|---|---|---|---|---|---|---|
| fp-A | 7.12 | 5.42 | 2.91 | 1.46 | 0.64 (9.0%) | 0.47 | 0.29 |
| fp-B (void for frame, camera, particles) | — | — | — | — | 0.33 | 0.15 | 0.011 |

**fp-A stands alone for the first-person frame; there is no comparison.** fp-B's slow frames (above) void its frame,
camera and particle figures; its 0.85 ms `bursts/step` is not attributed to anything. The `hud` drop, 0.31 ms, is one run
per build with no spread: supporting only.

**Identity — what the shots show, and what they cannot.** Paused at tick 30000, A2 against B2 differed in 49 pixels, all
inside (1101,2)-(1119,10): the digits of the frame-time overlay (`cl_showfps 2`), which reads each build's own frame time.
The control, A2 against A3 of the SAME build, differed in 53,167 pixels over (0,0)-(1279,538): A3 showed six chat lines
(looked at); A2 showed none (A2 itself was not looked at — inferred from its 49-pixel difference from B2, which was). *Inferred:* the chat fades on the wall clock (`hud_saytext_time`,
`CHAT_HISTORY_IDLE_FADE_TIME`), and A3's load was faster, so a HUD capture is not deterministic between runs.

- **The chat was faded in both A2 and B2**: B2 shows none (looked at), and A2 differs from B2 only in the overlay digits. So
  the chat's own text never reached a pixel in the compared pair, and the pair cannot speak for `HudChat`'s output. What it
  does cover is the rest of the HUD's text — card names, health numbers, the round timer, the overlay's words — which goes
  through the same changed check in `DrawPrintText` and matched to the pixel.
- **The world is NOT shown identical.** No world-only comparison was made, and the three captures were removed in the
  session's cleanup before their hashes were written down (`all.ps1` said they would be kept; they were not). The counts
  above are as printed by the comparison script during the session; `pixdiff.ps1` beside the runs is that script re-created,
  with a `-WorldOnly` mask, for whoever retakes the pair. Retaking needs the desktop (an exclusive run).

## Verdict

**The font-handle check was most of the HUD's empty-view cost.** `HudChat` falls from 0.29-0.39 ms to 0.011 ms in every
run, `hud` from 0.61-0.81 ms (18.0% of the steady frame) to 0.25-0.30 ms (8.1%) — 0.41 ms a frame, with B's largest under
half of A's smallest. First person: a 0.31 ms `hud` drop in one run per build; no spread, supporting only.

**The frame did NOT fall by the HUD's saving.** Whole window 4.28 → 3.71 (0.57), but 01-A carries most of that; without it
A is 3.99, a 0.28 drop. Steady window 3.81 → 3.35, and without 01-A 3.45 → 3.35, a 0.10 drop. The ranges overlap either way.
Other phases rose in B — `bursts/step` 0.32 → 0.41 on average, from its two modes (two of B's four runs in the high one, one
of A's) — so the frame is not claimed; only the HUD is.

**The predictions, scored.**
- (1) *Camera largest, then hud, then advance.* Holds for A (2.15, 0.68, 0.62). **Fails for B**: `hud` 0.27 is under
  `advance` 0.59, because the fix took the HUD below it.
- (2) *`hud paint` falls 0.1-0.3 ms.* **Outside the range**: it fell 0.38 (0.52 → 0.14). The band rested on guessed handle
  and call counts, never measured; and its falsifier ("inside the before build's spread") covered only the low side, so a
  drop larger than predicted could not fail it. The direction held; the size was not predicted.

**What the empty view costs now, and whose it is** (B, steady window, 3.35 ms): camera 2.16 ms (64%), of which particles
1.55 (bursts 0.89, build 0.31, decals 0.14) and the pose 0.58; advance 0.59 (sample, drawlist, models); hud 0.27; sound 0.11;
between ~0.26 over the whole window. The
particle step and build are the engine's shape (an effect updates until it sleeps; every live effect's box is checked
each frame) and are recorded, not changed; the muzzles and dispatches offers are B577's; sample, drawlist and collate are
B259's. **Nothing in this record makes 3.7 ms "basically infinite"**: the remaining floor is the particles' per-effect
work and B259's per-entity walks, each audited above.

In the Prediction's own terms. (1) Camera largest, then hud, then advance: confirmed for A, falsified for B (`hud` 0.27 is
under `advance` 0.59, because the fix took the HUD below it). (2) `hud paint` falls 0.1-0.3 ms: the direction is confirmed
and the stated range is falsified on the high side, since it fell 0.38 ms (0.52 to 0.14); the falsifier as written (inside
the before build's spread) covered only the low side, so a larger drop could not fail it, as the scoring above says.

Verdict extended 2026-10-29 after the fact from the Result above; the run itself was not repeated.
