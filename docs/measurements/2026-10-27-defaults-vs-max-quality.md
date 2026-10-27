# Measurement 2026-10-27: viewer defaults against maximum quality

**Status: audit only.** No source changed, nothing built or run. Every figure below was read from the file named beside it.

## Observation

Owner, 2026-09-25 (`docs/memory/defaults-are-highest-quality.md`): *"our defaults should be as high as possible, like gfx quality wise"*. TF2 itself defaults to a medium config. This lists every quality-affecting setting the viewer has and what its default is against the highest value TF2 accepts.

## Sources, and how far each is trusted

| tag | source | caveat |
|---|---|---|
| **ours** | `managed/Tf2DemoSalvage.Scene/ViewerSettings.cs` (line numbers below) and `managed/Tf2DemoSalvage.Render/WorldRenderer.cs` | read |
| **SDK** | `F:/src/source-sdk-2013`, `ConVar` declarations | read; covers client-side cvars only, not engine/materialsystem ones |
| **cvarlist** | `F:/SteamLibrary/steamapps/common/Team Fortress 2/tf/cvarlist.log` (dated 2026-08-16) | `docs/findings/40` calls its column a default, **but `mat_picmip -1`, `mat_antialias 8`, `mat_forceaniso 16` in it are exactly the owner's `ultra.cfg` values, so those three are most likely the dump-time value, not Valve's stock. Treated as unverified for stock** |
| **owner ultra.cfg** | `.../Team Fortress 2/tf/cfg/ultra.cfg` (188 lines, the owner's own capture config) | read; the owner's choice, not Valve's. It deliberately sets `mat_hdr_level 0` and `mat_antialias 1` for capture parity, so it is **not** a maximum there |
| **wiki** | https://wiki.teamfortress.com/wiki/Ultra_quality, fetched 2026-10-27 | web-sourced; the fetched page gave `r_lod 0`, `r_rootlod 0`, `cl_new_impact_effects 1`, `cl_burninggibs 1`, `mat_antialias 8`, `mat_aaquality 2 or 4` (old NVIDIA only), `-no_texture_stream`; for `mat_picmip` it gave a range "2 to -10" and also said 0 is highest and -1 "very high" without reconciling. The `-10` in the task brief is the page's best-quality figure |
| **mastercomfig / Lawena / Chris' maxquality** | not found in repo docs; the mastercomfig docs host redirected and was not followed | **unverified**; nothing below is attributed to them. (`ViewerSettings.cs:506` and `docs/findings/13` claim they use `mat_picmip -10`; that claim was not re-verified here) |

Fps cost: only one pair exists in `docs/measurements/`, HDR 18.09 ms against LDR 16.96 ms (LDR 6% cheaper; its drift control failed) in `2026-10-22-empty-view-budget-ldr-f12.md:134`. Everywhere else the cost is **unmeasured**.

## Result

### (a) Implemented, below max, and a one-line default change

Only two rows, both owner-pasteable cvars. The honest finding is that almost every implemented default is already at or above the maximum.

| name | our default | Valve stock default | max-quality value (source) | already max? | fps cost |
|---|---|---|---|---|---|
| `cl_detaildist` | 1200 (`ViewerSettings.cs:677`, `DetailDistance` `:647`) | 1200 (SDK `detailobjectsystem.cpp:52`, per the `ViewerSettings.cs:218` comment; cvarlist 1200) | 8592 (owner ultra.cfg; the same value is quoted for `ultra.cfg` at `Device3D.cs:3090`). No Valve-published maximum found: unverified | no | unmeasured; draws detail props to 7x the range |
| `cl_detailfade` | 400 (`:680`, `:651`) | 400 (same source) | 400 (owner ultra.cfg leaves it) | yes | none |

### Already at max (kept for completeness, not changes)

| name | our default | Valve stock default | max-quality value (source) | already max? | fps cost |
|---|---|---|---|---|---|
| texture detail, ours `texture_quality` (~`mat_picmip`) | `Full` = no cap (`ViewerSettings.cs:526`, enum `:44`) | `mat_picmip`: unverified (cvarlist shows -1, likely the owner's) | `mat_picmip -10` (wiki, ambiguous; owner ultra.cfg -1). `WorldRenderer.cs:8500` records that nothing exists past mip 0, so Full is the ceiling | yes | measured cheap: full vs 1024 cap +15 MB and 0.03 s load (`ViewerSettings.cs:512`) |
| anisotropic filtering (not a setting, hardcoded) | 16 (`WorldRenderer.cs:8483`, `Filter.Anisotropic` `:8506`) | `mat_forceaniso`: unverified (cvarlist 16, likely the owner's) | 16 (owner ultra.cfg; D3D11 ceiling per `WorldRenderer.cs:8480`) | yes | unmeasured |
| `mat_hdr_level` | 2 (`BspHdr.cs:23`, `ViewerSettings.cs:828`) | 2 (cvarlist; `EngineConVars.cs:205`) | 2 implemented; 3 asks for float HDR and draws as 2 (`docs/findings/13` B537) | yes, in the implemented range | HDR 18.09 ms vs LDR 16.96 ms (1 run, drift control failed) |
| `mat_specular` | on (`:837`) | 1 (cvarlist) | 1 (owner ultra.cfg) | yes | unmeasured |
| `mat_phong` | on (`:740`) | 1 (cvarlist) | 1 (owner ultra.cfg) | yes | unmeasured |
| `r_3dsky` | 1 (`SkyboxView.cs:98`, `:619`) | 1 (SDK `viewrender.cpp:113`) | 1 | yes | unmeasured |
| `cl_drawmonitors` | on (`:743`) | 1 (SDK `viewrender.cpp:168`) | 1 (owner ultra.cfg) | yes | unmeasured |
| `r_decals` | 2048 (`:665`, `:662`) | 2048 (SDK `EffectsClient.cpp:77`) | 2048 (owner ultra.cfg). Higher accepted by the engine: unverified | yes as far as a source says | unmeasured |
| `r_maxmodeldecal` | 50 (`ModelDecals.cs:25`, `:671`) | 50 (SDK `mdlpanel.cpp:276`) | 50 (owner ultra.cfg) | yes | unmeasured |
| `r_drawviewmodel` | on (`:726`) | 1 | 1 | yes | not a quality knob |
| `mat_disable_bloom` | off (`:809`) | unverified | off | yes | one of the two bloom passes |
| `mat_queue_mode` | -1 (`:625`) | -1 (cvarlist; `low.cfg:153`, `ultra.cfg:177`) | -1 (owner ultra.cfg); threading, not picture quality | yes | threading |
| `r_threaded_particles` | on (`:632`) | 1 (cvarlist) | 1 | yes | threading |
| `mat_vsync` | off (`:899`) | 0 (cvarlist) | 0 for speed; not a quality knob | n/a | n/a |
| `fps_max` | 300 (`:458`, `:552`) | 400 (cvarlist, `ViewerSettings.cs:441`) | 0 (uncapped; `docs/findings/13` records TF2 at 600-1000 fps uncapped) | no, but a cap, not picture quality | a cap, nothing to render |
| `cl_ragdoll_fade_time` | 15 baked in (`RagdollFade.cs:53`); not a setting | 15 (SDK `c_tf_player.cpp:514`) | 15 (owner ultra.cfg) | yes | n/a |
| `cl_ragdoll_physics_enable` | not a setting; ragdolls are simulated | 1 (SDK `c_tf_player.cpp:513`) | 1 | yes | unmeasured |

### (b) Not implemented as a setting (default cannot be changed; feature may or may not exist)

"Feature" says whether the drawing code exists. Source: grep of `managed/` for the cvar name found no declaration except the ones in tables above; `docs/findings/13` lists the Detail, Filtering and Lighting items as not built, and **that doc is stale for aniso (now built, hardcoded) and `mat_specular`/`mat_phong`/`mat_hdr_level`/`mat_vsync`/`fps_max` (now settings)**.

| name | our default | Valve stock default | max-quality value (source) | already max? | fps cost |
|---|---|---|---|---|---|
| `mat_antialias` (MSAA) | none: every swap chain and target is `SampleDesc(1, 0)` (`Device3D.cs:369`, `WorldRenderer.cs:8076` and more) | unverified (cvarlist 8, likely the owner's; owner ultra.cfg sets 1) | 8 (wiki) | no (single sampled) | unmeasured; wiki says moderate GPU |
| `mat_aaquality` (CSAA) | none | unverified | 2 or 4, old NVIDIA only (wiki) | n/a (hardware dependent) | unmeasured |
| `mat_alphacoverage` | none | unverified | owner ultra.cfg sets 0; max unverified | unverified | unverified |
| `mat_trilinear` | not exposed; anisotropic implies it | 0 (cvarlist) | 1 (owner ultra.cfg) | effectively yes via aniso 16 | unmeasured |
| `mat_filtertextures`, `mat_filterlightmaps` | not exposed | 1 for lightmaps (cvarlist) | 1 (owner ultra.cfg) | filtering is on | unmeasured |
| `r_lod`, `r_rootlod`, `r_staticprop_lod` | always LOD 0 (`docs/findings/13`, "we always take LOD 0") | `r_lod` -1 and `r_staticprop_lod` -1 (cvarlist); `r_rootlod` 0 (SDK `mdlpanel.cpp:280`) | 0 / 0 / 0 (wiki for the first two; owner ultra.cfg for all three) | yes, by construction | none |
| `lod_transitiondist` | not exposed | unverified | 3592 (owner ultra.cfg) | n/a | unverified |
| `mat_bumpmap` | `docs/findings/13` says not implemented; unverified against current code (`docs/findings/12-shader-parity.md` not re-read) | 1 (cvarlist) | 1 | unverified | unverified |
| `mat_colorcorrection` | not exposed | unverified (cvarlist 1) | 1 for max fidelity; owner ultra.cfg sets 0 for capture parity | unverified | unverified |
| `mat_motion_blur_enabled` | nothing drawn | 0 (cvarlist) | 0 (owner ultra.cfg; frag-movie baseline per `docs/findings/13`) | yes (off) | n/a |
| `mat_reducefillrate`, `mat_disable_lightwarp`, `mat_disable_fancy_blending` | not exposed | 0 (cvarlist for the first) | 0 (owner ultra.cfg) | n/a | n/a |
| `r_shadows`, `r_shadowrendertotexture`, `r_shadowmaxrendered` | no shadow renderer found in `managed/Tf2DemoSalvage.Render` or `.Scene` by file name; unverified | 1 / 0 (SDK `clientshadowmgr.cpp:684`, `:91`) / unverified | 1 / 1 / 160 (owner ultra.cfg) | no | unmeasured |
| `r_flashlightdepthtexture`, `r_flashlightdepthres`, `r_flashlightmodels`, `r_flashlightrender` | not exposed | 1 for depthtexture (SDK `clientshadowmgr.cpp:94`) | 1 / 1024 / 1 / 1 (owner ultra.cfg) | unverified | unverified |
| `r_dynamic`, `r_maxdlights` | dynamic lights exist (`DynamicLights.cs`, `WorldDynamicLights.cs`, `docs/RISKS.md` B425); no setting | `r_dynamic` 1, `r_maxdlights` 32 (cvarlist) | 1 / 32 (owner ultra.cfg) | yes by behaviour, unverified for the cap | measured elsewhere (`2026-10-22-dynamic-light-merge.md`), not as a quality delta |
| `r_worldlights` | not exposed | 4 (cvarlist) | 4 (owner ultra.cfg) | unverified | unverified |
| `r_rimlight` | not exposed | 1 (cvarlist) | 1 | unverified | unverified |
| water: `r_waterforceexpensive`, `r_waterforcereflectentities`, `r_waterdrawreflection`, `r_waterdrawrefraction`, `r_cheapwaterstart`/`end`, `tf_water_resolution` | water reflection/refraction is drawn (`WorldRenderer.Water.cs`, `MapWater.cs`); none is a setting | `r_waterforceexpensive` 0 and `r_waterforcereflectentities` 0 in the SDK (`viewrender.cpp:153`, `:155`); cvarlist shows 1/1 (likely the owner's); `tf_water_resolution` 1024 (cvarlist) | 1 / 1 / 1 / 1 / 8192 / 2048 (owner ultra.cfg) | unverified which path the viewer takes | unmeasured |
| `cl_show_splashes` | `BulletWater.cs` exists; no setting | 1 (SDK `fx_water.cpp:34`) | 1 | yes by behaviour | unmeasured |
| `cl_new_impact_effects` | no setting; `ImpactEffects.cs` exists | 0 (SDK `fx_impact.cpp:211`) | 1 (wiki) | unverified which path the viewer draws | unmeasured |
| `r_drawflecks` | no setting | 1 (SDK `fx_impact.cpp:24`) | 1 (owner ultra.cfg) | unverified | unverified |
| `cl_ejectbrass` | no setting | 1 (SDK `c_te_legacytempents.cpp:69`) | 1 | unverified | unverified |
| `cl_burninggibs` | no setting; `PlayerGibs.cs` exists | 0 (SDK `props_shared.cpp:30`) | 1 (wiki; owner ultra.cfg) | unverified | unverified |
| `tf_playergib` | no setting | 1 (SDK `tf_player.cpp:188`) | 1 or 2 ("2: always" per cvarlist help); which is richer is unverified | unverified | unverified |
| `props_break_max_pieces` | no setting | -1 (SDK `props_shared.cpp:25`) | -1 (owner ultra.cfg) | unverified | unverified |
| `cl_phys_props_max`, `cl_phys_props_enable` | no setting | 300 (SDK `physpropclientside.cpp:23`) | 100 in owner ultra.cfg (lower than stock: so not a quality maximum); higher: unverified | unverified | unverified |
| `r_propsmaxdist` | no setting | 1200 (SDK `physpropclientside.cpp:24`; that declaration is for clientside physics props) | 8192 (owner ultra.cfg) | unverified | unverified |
| `r_drawdetailprops` | detail props drawn; no on/off setting | unverified | 1 (owner ultra.cfg) | yes by behaviour | n/a |
| `r_drawmodeldecals` | model decals drawn (`ModelDecals.cs`) | 1 (SDK `c_baseentity.cpp:83`) | 1 | yes by behaviour | n/a |
| `cl_ragdoll_forcefade`, `cl_ragdoll_collide`, `ragdoll_sleepaftertime` | no settings | unverified | 0 / 1 / 5 (owner ultra.cfg) | n/a | n/a |
| `r_eyes`, `r_flex`, `r_teeth`, `r_eyemove`, `flex_rules`, `flex_smooth`, `anim_3wayblend` | no settings; animation exists | unverified | 1 each (owner ultra.cfg) | unverified | n/a |
| `mat_reduceparticles`, `cl_particle_batch_mode`, `tf_particles_disable_weather` | no settings | `mat_reduceparticles` 0, weather 0 (cvarlist) | 0 / 0 / 0 (owner ultra.cfg) | unverified | n/a |
| `muzzleflash_light`, `cl_muzzleflash_dlight_1st`, `tracer_extra`, `r_drawtracers_firstperson` | no settings (`HitscanTracers.cs` exists) | unverified | 1 each (owner ultra.cfg) | unverified | n/a |
| rope: `rope_subdiv`, `rope_smooth`, `rope_rendersolid`, `r_ropetranslucent`, `rope_collide` | no settings | unverified | 7 / 1 / 1 / 1 / 1 (owner ultra.cfg) | unverified | n/a |
| `glow_outline_effect_enable`, `pyro_dof`, `pyro_vignette` | no settings | unverified | 1 / 1 / 2 (owner ultra.cfg) | unverified | n/a |
| `r_decalstaticprops`, `r_renderoverlayfragment`, `r_drawbatchdecals`, `r_decal_cover_count`, `r_decal_overlap_count` | no settings | unverified | 1 / 1 / 1 / 4 / 3 (owner ultra.cfg) | unverified | n/a |
| `tf_monitor_resolution` | no setting | unverified | 1024 (owner ultra.cfg) | unverified | n/a |
| `snd_pitchquality`, `dsp_*` | audio, no quality setting | unverified | 1 / 0 / 1 (owner ultra.cfg) | n/a | n/a |
| `-no_texture_stream` (launch option) | not applicable: the viewer decodes whole textures at load (`ViewerSettings.cs:512` table) | n/a | passed to TF2 only (wiki) | yes, nothing streams | n/a |

### (c) Max value is cheat-locked or hardware dependent

| name | our default | Valve stock default | max-quality value (source) | already max? | fps cost |
|---|---|---|---|---|---|
| `mat_hdr_level 3` (float HDR) | 2 | 2 | 3 when the device reports float support (`docs/findings/13` B537, read in disassembly); not built, draws as 2 | no | unmeasured |
| `mat_aaquality` | none | unverified | 2 or 4, only on very old NVIDIA GPUs (wiki) | n/a | unmeasured |
| `mat_antialias 8` | none | unverified | 8 if the GPU and driver offer 8x; the cap is hardware dependent. wiki gives 8 | no | unmeasured |
| `mat_forceaniso 16` | 16 | unverified | 16 is the D3D11 ceiling (`WorldRenderer.cs:8480`) | yes | unmeasured |
| texture size | full | n/a | the VTF's own mip 0 (`WorldRenderer.cs:8500`) | yes | n/a |
| `fps_max` | 300 | 400 | TF2 clamps 1..29 up to 30 unless `sv_cheats` (`ViewerSettings.cs:542`, engine string); ours does not | n/a | n/a |
| cheat flag (`FCVAR_CHEAT`) on any max value | `r_3dsky` is not cheat (`ViewerSettings.cs:203`) | cheat flags for the rest were not read here | unverified for every other row | | |

## Conclusions

- The viewer implements about fifteen quality-adjacent settings and **all but `cl_detaildist` already default to the maximum any source gives**. Texture detail and anisotropy (16, hardcoded) are at the ceiling.
- Real gaps are **absences**, not defaults: MSAA, shadows (`r_shadows`), the water cost settings, `cl_new_impact_effects`, `cl_burninggibs` and the rest of group (b). Raising a default needs a setting to exist first.
- `docs/findings/13-settings-parity.md` is stale on aniso, specular, phong, HDR level, vsync and `fps_max` (all now exist).
- Not verified here: cheat flags, stock defaults for engine-side cvars (`mat_picmip`, `mat_forceaniso`, `mat_antialias`, `r_lod`), mastercomfig/Lawena/Chris' values, and whether `mat_bumpmap` is now built.
- Next step if the owner wants it: a `[Explicit]` probe or Strings read of `engine.dll`/`materialsystem.dll` registrations (finding 37's method) for the engine-side stock defaults.
