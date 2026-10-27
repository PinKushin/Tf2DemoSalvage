# Every option TF2 gives, and where ours stand

**The rule, owner-stated: if TF2 lets you change it, this should too.** Two exceptions, both
deliberate. **DirectX level** — we are DX11 and there is no reason to pretend otherwise. And
**interpolation and network timing** (`cl_interp`, `cl_interp_ratio`, `cl_updaterate`,
`cl_cmdrate`), because those shaped the recording when it was made; they are baked into the demo and
re-applying them at playback would be inventing a second, different game.

Settings live in a Source-style `.cfg` at `%LOCALAPPDATA%\Tf2DemoSalvage\settings.cfg`, one command
per line, unknown commands ignored — the same shape TF2 uses, so a person can edit it by hand and so
a frag-movie config can eventually be imported nearly as-is.

## What exists now

| our command | TF2's equivalent | notes |
|---|---|---|
| `texture_quality` | `mat_picmip` | inverted sense: ours is a maximum edge in pixels, 0 for full |
| `fullscreen_mode` | `mat_fullscreen` / borderless | 0 borderless, 1 exclusive |

**What `mat_picmip` does not change: the size a texture was authored at** (2026-09-10, B390). The
engine keeps two sizes per texture. `GetActualWidth` is the level that survived the mip skip, and
`GetMappingWidth` is the file's header, read header-only with no mips skipped. Anything sized by a
texture uses the second: a sprite's quad (`CEngineSprite::Init`) and the detail sheet's aspect
correction. So a glow stays the same size in the world at every texture detail. This project had one
size, the decoded one, which made `texture_quality` shrink sprites. It was established in
`materialsystem.dll`'s disassembly, not from the method's name. The addresses and instructions are in
`docs/RISKS.md` under "B390 FIXED 2026-09-10: the mapping size".

## What TF2 exposes that we do not, grouped as its own menu does

**Detail**

| convar | what it does | our state |
|---|---|---|
| `mat_picmip` | texture detail | have it, differently spelled |
| `r_rootlod` | model detail | no model LOD selection — we always take LOD 0 |
| `mat_reducefillrate` | shader detail, low picks cheaper shaders | not applicable until there are cheap paths |
| `r_lod` | forces a model LOD | same |

**Filtering and edges**

| convar | what it does | our state |
|---|---|---|
| `mat_forceaniso` | anisotropic filtering | sampler is linear; no aniso set |
| `mat_antialias` / `mat_aaquality` | multisampling | swap chain is single-sampled |
| `mat_trilinear` | trilinear versus bilinear mips | mips exist, filtering not exposed |

**Lighting and effects**

| convar | what it does | our state |
|---|---|---|
| `mat_bumpmap` | normal maps on or off | not implemented at all — see `12-shader-parity.md` |
| `mat_specular` | cubemap reflections | not implemented |
| `mat_hdr_level` | none, bloom, full HDR | 0, 1 and 2 to parity; 3 drawn as 2 — see *What `mat_hdr_level` 1 and 3 select* below (B537) |
| `mat_monitorgamma` | display gamma | the curve exists in `SourceGamma`, not exposed |
| `r_shadows`, `r_shadowrendertotexture` | dynamic shadows | no dynamic lights yet |
| `mat_motion_blur_enabled` | motion blur | nothing |

**Other**

| convar | what it does | our state |
|---|---|---|
| `fps_max` | frame cap | we present with no vsync and no cap |
| `mat_vsync` | vertical sync | not exposed; `SyncInterval` is hardcoded to 0 |
| `r_drawviewmodel`, `viewmodel_fov` | first-person weapon | no POV camera yet |
| `fov_desired` | field of view | orthographic overhead only so far |

## Where the frag-movie defaults sit

The default should be what TF2 looks like at its best — Chris' maxquality or Lawena, which is what
anyone recording a video runs. That means, once each exists: maximum texture detail, maximum model
detail, anisotropic filtering on, bumpmaps and specular on, no motion blur, and no LOD dropping.
`texture_quality 0` already reflects that half of it.

**The one that is already wrong to default to TF2's own value is `fps_max`.** A demo viewer scrubbing
a timeline wants frames as fast as it can make them; a game wants a stable cap. Ours has no cap and
that is right, but it should still be settable.

**Measured on the owner's machine, 2026-08-16: TF2 itself runs uncapped without trouble.** Their
config sets no `fps_max` at all, and the game returns roughly **600 fps in most places and close to
1000 in some**.

**The "Source can't go above 300" belief had real evidence behind it, and that is the interesting
part.** The owner held it because they used to sit around **250 fps and never cross 300** — so it was
not folklore for them, it was an observation. On this build it is simply untrue, and by a wide
margin.

**Mostly the hardware**, per the owner, who has the history first-hand: the 250 fps years were
mid-range i5s and weak AMD cards, an RX 480 among them. Their own read is that **CPU speed mattered
most, then the GPUs**, and that the 32-bit client may have contributed without being the main thing.
Nothing here separates those, and it is recorded as their attribution rather than as a measurement.

That reading is at least consistent with how the engine behaves: Source's frame rate is bound by a
single main thread, so a faster core moves it more than a faster card does — general knowledge, not
something measured in this project.

**What it is NOT is the 64-bit OS.** A draft of this note offered "the 64-bit client" as the
mechanism and that was wrong twice over: the owner has run a 64-bit OS since Windows 7, and TF2's own
64-bit build postdates the observations by years. Two different meanings of "64-bit", and I had
merged them.

**The transferable shape:** a limit observed consistently on one machine over years is evidence about
that machine, and it survives long after the machine does. The same failure as an empty grep read as
a fact about the format — see `an-empty-search-needs-a-control`.

**And the correction is its own instance of the same thing.** Told the ceiling had lifted, I reached
for the 64-bit client as the mechanism — a plausible story, adopted without measurement, which is
precisely how the original belief hardened. The owner knew the actual history. Where a cause is
someone's own machine, ask rather than infer.

For this project's purposes the consequence is small and useful: an uncapped game and an uncapped
viewer are the same condition, so frame rate is not a variable between a reference capture and our
output.

## What `mat_hdr_level` 1 and 3 select (B537, 2026-10-14)

**Level 1 is the LDR picture with LDR bloom added, at three tenths.** It was filed as *interpolated*; it is now
read. Three pieces, each with its evidence class:

- **The HDR type is none.** `Map_CheckForHDR` enables HDR only from level 2 (*read in disassembly*, B537's table),
  so level 1 loads the LDR lumps, LDR `.vhv`, LDR cubemaps, no tone map and no exposure walk — every reader that
  already followed the type.
- **Bloom is still on.** `GetBloomAmount` (`viewpostprocess.cpp:1429-1432`, *read from published source*) enables
  it on `mat_hdr_level >= 1`, then turns it off only when `MapHasHDRLighting()` is false — a question about the
  map's lumps, not the type. TF2's shipped `engine.dll` carries the cvar's help verbatim, *"Set to 0 for no HDR, 1
  for LDR+bloom on HDR maps, and 2 for full HDR on HDR maps."* (*the game's own shipped data*), which is the
  engine's own statement that `MapHasHDRLighting` answers yes on an HDR map at level 1. Not confirmed in
  disassembly: the Ghidra MCP's analysis tools were not reachable from the session that wrote this.
- **At 0.3 of the amount.** Under `HDR_TYPE_NONE` the amount is multiplied by `mat_non_hdr_bloom_scalefactor`,
  ".3" (`:66`, `:1460-1463`), after the walk, so the walking static is unscaled. The passes are the same ones HDR
  uses: `HDR_TYPE_NONE` and `HDR_TYPE_INTEGER` share one `case` in `DoEnginePostProcessing` (`:2268-2357`), which
  calls `Generate8BitBloomTexture` either way, and only the tone-map calls on either side are skipped under none.
  The client ships both the cvar and the `blurfiltery_nohdr` material name (*shipped data*).

**Measured, `cp_granary` STV 2008 at tick 1000:** level 1 over level 0 raises the channel means from
140.0/103.2/72.1 to 143.1/105.3/73.1. Before the fix the two captures were the same picture.

**Level 3 asks for float HDR, and is not built.** The caps computation sets `HDR_TYPE_FLOAT` at level 3 when the
device reports float support (*read in disassembly*, `HdrTypeConformanceTests`). The client's `HDR_TYPE_FLOAT` case
(`:2559-2629`) is a different pipeline outright: no 8-bit bloom, the `dev/floattoscreen_combine` material through
`HDRFinal_Float`/`HDRFinal_Float_NoBloom` pass lists, the histogram's own goal (`0.18 / avg_lum`, then `0.023 /
avg_lum`), and no colour correction (`:2332`). TF2 ships the float-to-screen shaders in `hl2_misc` (*shipped data*).
**Open:** what `shaderapidx9` asks before it reports float support on a current device, and whether TF2's
`materialsystem` then allocates float frame-buffer targets; both need the disassembly this session could not reach.
Until then level 3 draws as 2, which is filed rather than claimed.

## A keyboard key's bind never ran (B547, 2026-10-15)

**Found by the owner while testing D218:** `bind MOUSE3 "toggle cl_showpos"` worked and the same bind on a keyboard
key did nothing, which took his scoreboard toggle alias with it. The console (`ConfigConsole`, D69/D215) ran either
bind correctly when pressed; the fault was upstream, in who pressed. `PointerDown` handed every mouse button to the
console, while `ViewerSession.KeyPressed` handed a keyboard key over only when the free camera was on AND the key
drove a held flight action. Everything else went to the viewer's own key comparisons or the menu's shortcuts.

**The first suspects were wrong.** Menu `ShortcutKeys`, `WidgetKeys.Keeps` and `ProcessCmdKey` consuming the key
were all named in the filing; none was the cause. `ProcessCmdKey` calls the session first, and the session simply
never pressed the key.

**The engine gives every key to its binding** (`Key_Event`, then the bind through the command buffer; the closed
engine's keys.cpp, *interpolated* from its Quake lineage - the binary was not read for this). Matched with three
refinements: an auto-repeat runs nothing (Source binds the press; `WM_KEYDOWN` bit 30 marks a repeat); CTRL or ALT
with a key is not that key, since `bind` has no modifier syntax and the viewer's own defaults live on CTRL chords;
and a bind that did something of the console's own (set a ConVar, ran an alias, moved a button) takes the key, so a
viewer shortcut on the same key does not also fire (D101). A viewer one-shot (`resetspeed`, `+jump`) does not take
it, and goes on to its own route.

**A second, hidden half.** `CommandFor` falls back to the viewer's default when a key's bind expands to no
`ViewerAction` - a rule written for binds that mean nothing here (B214). `toggle cl_showpos` names no action, so
`bind HOME "toggle cl_showpos"` ran `resetspeed`. A bind that sets a declared ConVar or defines an alias now counts as
meaning something.

## Correction, 2026-10-27: the "our state" column above was stale for six rows

The audit `docs/measurements/2026-10-27-defaults-vs-max-quality.md` (branch `docs/defaults-vs-max-quality`, `7e7673ff`)
read `ViewerSettings.cs` and `WorldRenderer.cs` against the tables in *What TF2 exposes that we do not*. The old
statements are kept as written; what showed each wrong is the source line named. *Evidence class: read from our source.*

| row | old statement | now |
|---|---|---|
| `mat_forceaniso` | "sampler is linear; no aniso set" | built, but hardcoded rather than a setting: anisotropic 16, `Filter.Anisotropic` (`WorldRenderer.cs:8483`, `:8506`) |
| `mat_specular` | "not implemented" | a setting, default on (`ViewerSettings.cs:837`) |
| `mat_phong` (not listed above; the 12-shader-parity doc's claim) | absent | a setting, default on (`ViewerSettings.cs:740`) |
| `mat_hdr_level` | "none, bloom, full HDR" at 0, 1, 2 | a setting, default 2 (`BspHdr.cs:23`, `ViewerSettings.cs:828`); 3 still draws as 2 (B537), unchanged |
| `mat_vsync` | "not exposed; `SyncInterval` is hardcoded to 0" | a setting, default off (`ViewerSettings.cs:899`) |
| `fps_max` | "we present with no vsync and no cap" | a setting, default 300 (`ViewerSettings.cs:458`, `:552`) |

Still true: `mat_antialias`/`mat_aaquality` (single-sampled) and `r_shadows` (no shadow renderer), now filed as B701 and
B702 in `docs/RISKS.md` and deferred until the fps target. Not re-checked, so left as written: `mat_bumpmap` (the audit
did not re-read `12-shader-parity.md`), and the `fov_desired` and `r_drawviewmodel` rows, which read as stale
(the viewer has a first-person camera) but were not in the audit's list.
