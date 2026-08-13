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
| `mat_hdr_level` | none, bloom, full HDR | LDR only, deliberately for now |
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
