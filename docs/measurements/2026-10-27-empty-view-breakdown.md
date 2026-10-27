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
| `specgui`, cards | `CTFSpectatorGUI` paints its player panels every frame while shown; `Update` throttled to `curtime + 0.1` (tf_spectatorgui.cpp:332). **record (VguiHud.cs comments)** | Same. | Parity. Record. |
| `camera particles/bursts/step` | `CParticleMgr::UpdateNewEffects` (particlemgr.cpp:1824-) calls `Update` on EVERY live effect each frame (:1844-1848); `BuildParticleSimList` (:1766-1799) skips the simulation of one past `m_flNextSleepTime` (set from `m_flNoDrawTimeToGoToSleep`, particles_new.cpp:536) unless it has moved. **read** | `ParticleEffects` steps live effects and counts `Slept` (finding 84). **record** | Parity in shape: an unseen effect still updates until it sleeps, in the engine too. Record. Its size is the timed half's question. |
| `camera particles/bursts/*` offers | Each effect is created ONCE when its TE / `DispatchEffect` / parity change arrives (`C_TEEffectDispatch::PostDataUpdate`, c_te_effect_dispatch.cpp:146-154); a `PATTACH_POINT_FOLLOW` effect's control point is re-read every frame while alive (`CParticleProperty::UpdateParticleEffect`, particle_property.cpp:507-519). **record (BurstEffects comments, B577)** | A window search per source per frame, then a fresh offer per live effect, with an attachment query for a following one (muzzles, dispatches). **read** | The per-frame attachment query is the engine's for a following effect. The per-frame search and re-offer is ours; the dispatch split is B577's, queued for another agent. Not taken here. |
| `camera particles/decals` | A decal is placed when its effect arrives (`ImpactCallback` → `AddStudioDecal`); nothing per frame. **record (DecalEffects comments)** | `StepModelDecals` searches the arrival window (`LandedWindow`), and `ClearOnChange` visits every player, per frame. **read** | Arrival-window search is ours (cheap, measured earlier); the per-player clear check is the engine's `C_TFPlayer` clears on state change. The 0.1-0.3 ms is the timed half's question; no fix decided. |
| `camera particles/build` | Only renderables in the view's leaf list are drawn (`BuildRenderablesList`). **record (B259)** | `ParticleEffects.Build` over every live effect, then the entity sprites and strips behind the glow gate. **read** | Possible divergence (we build quads for what is off screen); not established until `build` is timed with 0 quads. |
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

Order ABBA then BAAB (A = before, B = after): eight runs. Identity: one `--shot` per build at the same tick and camera,
compared by hash and by pixel. Reading: per-second lines after the first five (steady, the hit path) frame-weighted; the
first five reported apart (first-use builds, the miss path, `a-split-carries-its-first-use-builds.md`). % = part / that run's
`measured … mean`.

## Control

- Ticks advanced ~1,333 per 20 s window.
- `posed 0 of N` in every moment line, and the `--shot` shows no model — else the view is not empty and the run is void.
- `HudChat` present and non-zero in the before runs (the instrument sees the subject).
- The per-second phase sums equal the frame to within 0.1 ms (no untimed residual).
- Runs of one build agree within 15% on `hud paint`, else the spread is stated and no difference claimed.
