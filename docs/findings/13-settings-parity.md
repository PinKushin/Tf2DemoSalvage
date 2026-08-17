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
| `mat_hdr_level` | none, bloom, full HDR | LDR only. Reference captures must be set to match — see `24-reference-capture.md` |
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

**What changed is the hardware**, per the owner, who has the history first-hand: the 250 fps years
were mid-range i5s and weak AMD cards, an RX 480 among them. The ceiling was the machine.

Not the architecture — a draft of this note offered the 64-bit client as a candidate and that was
wrong twice over. The owner has run a 64-bit OS since Windows 7, and TF2's own 64-bit build is far
more recent than the observations. Two different meanings of "64-bit", neither of them the cause.

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
