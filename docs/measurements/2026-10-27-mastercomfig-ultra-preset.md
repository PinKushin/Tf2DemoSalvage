# Measurement 2026-10-27: mastercomfig's ultra preset against this viewer's defaults

**Status: audit only.** No source changed, nothing run but a throwaway VPK lister. Every value below was read from the file named beside it; nothing was executed from the download.

## Observation

Owner, 2026-10-27 (relayed by the lead): the viewer's defaults should follow mastercomfig's highest-quality preset. Standing rule, 2026-09-25 (`docs/memory/defaults-are-highest-quality.md`): *"our defaults should be as high as possible, like gfx quality wise"*. This lists every cvar that preset sets, resolved through the module aliases, and what the viewer does about each.

## Source, and what it is

| item | value |
|---|---|
| download | `mastercomfig-base.vpk`, six `custom/` files, and `cfg/overrides/modules.cfg`, extracted by the lead (all files dated 2026-08-14) |
| version | `alias version_comfig "echo mastercomfig version: 9.100.1 \| July 3, 2026"` in `cfg/comfig/echo.cfg` |
| how read | this repo's `VpkArchive` (`managed/Tf2DemoSalvage.Content/Assets/VpkArchive.cs`, v2, 35 files) from a throwaway console project; files written as bytes and read as text. Nothing was run or loaded as code |
| load order | `autoexec.cfg`: `preset` (execs `presets/<name>.cfg`), then `modules_c` (execs `overrides/modules.cfg`), then `run_modules`. A preset file is a list of `module=level` lines; each `module=level` is an alias to a `module_level` alias in `comfig/comfig.cfg` holding the cvars |

**Files in the base VPK (35):** `cfg/autoexec.cfg`; `cfg/comfig/` = `comfig.cfg` (54 KB, every module's levels), `define_presets`, `destitute_reset`, `echo`, `finalize`, `game_overrides`, `legacy_tweaks`, `legacy/opengl`, `mm_override`, `module_levels`, `modules_run`; ten class cfgs (`scout`..`spy`, `mtp`, `listenserver`); `cfg/presets/{destitute,low,medium,high,ultra}.cfg`; `dxsupport_override.cfg`, `glshaders.cfg`, `glbaseshaders.cfg`, `texture_preload_list.txt`, `scripts/client_precache.txt`, two VScript `.nut`.

**Presets (5):** destitute, low, medium, high, **ultra** (highest; the only preset above high). **Modules (56 in `comfig.cfg`), with levels:** `3dsky` off,on; `anti_aliasing` off,msaa_2x/4x/8x; `bandwidth`, `packet_rate`, `packet_size`, `snapshot_buffer`, `download` (netcode); `characters` very_low..high; `color_filter` off,grayscale,desaturated,warm,cool; `console`; `decals` off..ultra; `decals_art` off,on; `decals_models` off..high; `dynamic_background`; `effects` very_low..ultra; `flashlight` off,on; `fpscap` powersaver,30..400,1000,unlimited; `gibs` off,low,high; `sillygibs` auto,off,on; `htmlmotd`; `hud_*`, `killfeed`, `killstreaks`, `match_hud`, `messages`, `logo`, `party_mode`, `voice_chat` (HUD/UI); `jigglebones` off,on,force_on; `lighting` very_low..ultra; `lod` low..ultra; `motion_blur` off,low,high; `outlines` off..ultra; `phong` off,on,rim; `post_processing` off,low,default,calm,vivid,washed,dreamy; `props` low,high,ultra; `pyrovision` low,medium,high; `ragdolls` off,medium,high; `romevision` off,on; `ropes` off..ultra; `shading` low,medium,high; `shadows` off..ultra; `sheens_speed` off,slow,medium,fast; `sheens_tint` low..full; `sound` low..ultra; `sprays` off,on,keep; `texture_filter` blocky,trilinear,aniso2x..16x; `texture_quality` low..ultra; `tracers` low,medium,high; `vsync` off,on; `water` very_low..ultra.

## The ultra preset is not the top level of every module

`presets/ultra.cfg` is 34 `module=level` lines. Where a module has a higher level than ultra picks, the **owner's own `modules.cfg`** (527 bytes, the website's output, loaded after the preset) overrides. Both are listed so the lead can choose which is "the preset".

| module | `ultra.cfg` | owner's `modules.cfg` | higher level that exists |
|---|---|---|---|
| flashlight | off | **on** | on |
| sprays | off | **on** | on |
| sheens_tint | high (`tf_sheen_alpha_firstperson .5`) | **full** (`1`) | full |
| sheens_speed | slow (`tf_sheen_framerate 15`) | **medium** (`25`) | fast |
| jigglebones | force_on (`cl_jiggle_bone_framerate_cutoff 1`) | **on** (`67`) | force_on is the more jiggle |
| fpscap | not set | **unlimited** (`fps_max 0;mat_powersavingsmode 0`) | unlimited |
| phong, lighting, lod, texture_*, ropes, outlines, sound, water, effects, shadows, decals, props | same | same | already the top |
| not set by ultra, set by `modules.cfg` | | bandwidth 6.0Mbps (`rate 786432`...), download all, killstreaks high, hud_contracts active, hud_panels high, hud_avatars everyone, htmlmotd on | |
| no single "highest" | post_processing `calm`, motion_blur off | same | post_processing has 7 styles, motion_blur's `high` is a look, not quality |

`anti_aliasing msaa_8x`, `texture_quality ultra`, `texture_filter aniso16x`, `water ultra`, `shadows ultra`, `lighting ultra`, `effects ultra`, `props ultra`, `decals ultra`, `ropes ultra` are each the module's top level.

## Result

**Legend, status column.** `S` = the viewer reads this cvar (or its mastercomfig module name) as a setting. `B` = the feature is drawn/simulated but has **no knob**, so it behaves one fixed way; "drawn" is from a file existing for it, not from checking the value matches. `?` = no knob and the behaviour was **not checked**. `A` = absent: no code. `N` = irrelevant to a demo viewer (audio, HUD text, downloads, spray upload, server-side). "Ours" is the default, with its source.

Cheat flags: `managed/Tf2DemoSalvage.Core/Net/EngineConVars.cs` carries `Cheat` for only the ConVars it declares; every one of those listed here is `cheat=false`. **For every other cvar the flag was not read**, and no value below is claimed cheat-locked or not.

Hardware dependent (value is a request the GPU may refuse): `mat_antialias`, `mat_alphacoverage`, `mat_forceaniso` (D3D11 ceiling 16), `mat_hdr_level`, `r_shader_srgb`, `mat_picmip` (capped by the VTF's own mip 0; `WorldRenderer.cs:8500`).

### lod, lighting, shading, phong, shadows, flashlight

| cvar | preset (ultra) | st | ours (source) |
|---|---|---|---|
| r_rootlod | 0 | B | always LOD 0 (prior doc, `docs/findings/13`); equals preset |
| r_lod | 0 | B | always LOD 0; equals preset |
| r_staticprop_lod | 0 | B | always LOD 0; equals preset |
| r_ambientboost | 1 | ? | no knob |
| r_lightaverage | 1 | ? | no knob |
| r_dynamic | 1 | B | dynamic lights drawn (`DynamicLights.cs`, `WorldDynamicLights.cs`); no knob |
| r_maxdlights | 32 | B | lights drawn; cap not checked |
| r_worldlightmin | .000001 | ? | no knob |
| r_worldlights | 4 | ? | no knob |
| mat_disable_lightwarp | 0 | ? | no knob |
| r_pixelvisibility_partial | 1 | ? | no knob (`r_dopixelvisibility` is a different, implemented setting, default on) |
| r_lightcache_zbuffercache | 0 | N | engine cache hint |
| r_lightmap_bicubic | 0 | ? | no knob (`bicubic_on` and `bicubic_off` both set 0 in this version) |
| mat_bumpmap | 1 | ? | no knob |
| mat_specular | 1 | S | on (`ViewerSettings.Specular`, `:837`); equals preset |
| mat_reducefillrate | 0 | ? | no knob |
| mat_disable_fancy_blending | 0 | ? | no knob |
| mat_phong | 1 | S | on (`ViewerSettings.Phong`, `:740`); equals preset |
| r_rimlight | 1 | ? | no knob |
| r_shadows | 1 | A | no shadow code (a case-insensitive search of `managed/` for r_shadows, shadow map, blobby and flashlight found none); **below preset** |
| r_shadowrendertotexture | 1 | A | same |
| cl_blobbyshadows | 0 | A | nothing drawn; equals preset (off) |
| r_shadowmaxrendered | 160 | A | same |
| nb_shadow_dist | 8192 | A | same |
| r_shadowlod | 0 | A | same |
| mat_supportflashlight | 0 | A | nothing drawn; equals preset (`modules.cfg` turns it on) |
| r_flashlightculldepth | 0 | A | same |
| r_flashlightdepthtexture | 0 | A | same |
| r_flashlightmodels | 0 | A | same |
| r_flashlightrender | 0 | A | same |

### effects, tracers, water, post-processing, anti-aliasing

| cvar | preset | st | ours |
|---|---|---|---|
| cl_show_splashes | 1 | B | `BulletWater.cs` drawn; no knob |
| tf_impactwatertimeenable | 0 | ? | no knob |
| fx_drawimpactdebris | 1 | B | `ImpactEffects.cs` family; no knob |
| fx_drawimpactdust | 1 | B | same |
| fx_drawmetalspark | 1 | B | same |
| r_drawflecks | 1 | ? | no knob |
| cl_ejectbrass | 1 | ? | no knob |
| cl_muzzleflash_dlight_1st | 1 | ? | no knob |
| muzzleflash_light | 1 | ? | no knob |
| cl_fasttempentcollision | 0 | N | collision speed hint |
| cl_particle_batch_mode | 0 | ? | no knob |
| tf_particles_disable_weather | 0 | ? | no knob |
| mat_reduceparticles | 0 | ? | no knob |
| cl_drawmonitors | 1 | S | on (`ViewerSettings.DrawMonitors`, `:743`); equals preset |
| tf_monitor_resolution | 1024 | ? | no knob |
| tracer_extra | 1 | B | `HitscanTracers.cs`; no knob |
| r_drawtracers_firstperson | 1 | B | same |
| r_WaterDrawReflection | 1 | B | water reflection drawn (`WorldRenderer.Water.cs`, `WaterViews.cs`, `MapWater.cs`); no knob; which path matches `*ultra` not checked |
| r_WaterDrawRefraction | 1 | B | same |
| r_waterforceexpensive | 1 | B | same |
| r_waterforcereflectentities | 1 | B | same |
| tf_water_resolution | 2048 | ? | no knob; render-target size not checked |
| r_cheapwaterstart | 8192 | ? | no knob |
| r_cheapwaterend | 8193 | ? | no knob |
| mat_hdr_level | 2 | S | 2 (`BspHdr.DefaultMatHdrLevel`, `ViewerSettings.HdrLevel`); equals preset |
| mat_colorcorrection | 1 | ? | no knob |
| mat_colcorrection_disableentities | 0 | ? | no knob |
| r_bloomtintexponent | 2.2 | ? | no knob (bloom drawn; `mat_disable_bloom` is a setting, default off) |
| mat_bloom_scalefactor_scalar | .25 | ? | no knob |
| r_shader_srgb | 0 | ? | no knob |
| mat_color_projection | 0 | A | nothing drawn; equals preset |
| pyro_dof | 1 | ? | no knob |
| pyro_vignette | 2 | ? | no knob |
| pyro_vignette_distortion | 1 | ? | no knob |
| tf_romevision_opt_in | 0 | N | per-player cosmetic opt-in |
| mat_motion_blur_enabled | 0 | A | nothing drawn; equals preset |
| mat_alphacoverage | 1 | A | no MSAA at all |
| mat_antialias | 8 | A | every swap chain and target is `SampleDesc(1, 0)` (`Device3D.cs:369`, `WorldRenderer.cs:8076`, more); **below preset**, hardware dependent |

### characters, decals, gibs, props, ragdolls, ropes, outlines

| cvar | preset | st | ours |
|---|---|---|---|
| r_flex | 1 | B | face flex drawn (`FaceFlex.cs`); no knob |
| flex_rules | 1 | B | same |
| flex_smooth | 1 | B | same |
| anim_3wayblend | 1 | ? | no knob |
| r_teeth | 1 | ? | no knob |
| r_eyes | 1 | ? | no knob |
| r_eyemove | 1 | ? | no knob |
| tf_clientsideeye_lookats | 1 | ? | no knob |
| blink_duration | .2 | ? | no knob |
| phonemefilter | .007 | B | constant `0.08` (`FaceFlex.cs:285`, Valve's stock); **differs from preset** |
| phonemedelay | 0 | ? | no knob |
| phonemesnap | 4 | ? | no knob |
| r_decals | 2048 | S | 2048 (`DefaultDecals`, `:665`); equals preset |
| mp_decals | 2048 | N | server-side decal cap |
| r_decal_cover_count | 4 | ? | no knob (`ImpactDecals.cs` exists) |
| r_decal_overlap_area | .4 | ? | no knob |
| r_decal_overlap_count | 3 | ? | no knob |
| r_drawbatchdecals | 1 | ? | no knob |
| r_drawmodeldecals | 1 | B | model decals drawn (`ModelDecals.cs`); no knob |
| r_maxmodeldecal | 50 | S | 50 (`ModelDecals.MaximumPerModel`, `:671`); equals preset |
| r_renderoverlayfragment | 1 | ? | no knob |
| cl_allowdownload | 0 | N | spray transfer; preset value is "off" |
| cl_allowupload | 0 | N | same |
| r_spray_lifetime | 0 | N | sprays are not drawn in the viewer |
| cl_spraydisable | 1 | N | same |
| tf_delete_temp_files | 1 | N | file housekeeping |
| tf_hide_custom_decals | 1 | N | custom decals not drawn |
| cl_burninggibs | 1 | B | `PlayerGibs.cs`; no knob; value not checked |
| tf_playergib | 1 | B | same |
| props_break_max_pieces | -1 | ? | no knob |
| violence_hgibs / violence_agibs | 1 / 1 | ? | no knob (set by `sillygibs_off`, which `gibs_high` runs) |
| violence_hblood / violence_ablood | 1 / 1 | ? | no knob |
| r_decalstaticprops | 1 | ? | no knob |
| cl_phys_props_enable | 1 | ? | no knob |
| cl_phys_props_max | 100 | ? | no knob; **lower than stock 300**, so not a quality maximum |
| cl_phys_props_respawndist | 901 | ? | no knob |
| cl_phys_props_respawnrate | 60 | ? | no knob |
| r_propsmaxdist | 8192 | ? | no knob |
| lod_TransitionDist | 3592 | ? | no knob |
| r_drawdetailprops | 1 | B | detail sprites drawn (`DetailSprites.cs`); no on/off knob |
| cl_detaildist | 8592 | S | **1200** (`DefaultDetailDistance`, `:677`); **below preset** |
| cl_detailfade | 400 | S | 400 (`:680`); equals preset |
| cl_ragdoll_fade_time | 15 | B | constant 15 (`RagdollFade.cs:53`); equals preset |
| cl_ragdoll_forcefade | 0 | ? | no knob |
| cl_ragdoll_physics_enable | 1 | B | ragdolls simulated (`IvpRagdollWorld.cs`); no knob |
| cl_ragdoll_collide | 1 | ? | no knob |
| ragdoll_sleepaftertime | 5 | B | default 5 (`IvpRagdollWorld.cs:1034`); equals preset |
| r_3dsky | 1 | S | 1 (`SkyboxView.DrawsByDefault`, `ViewerSettings.ThreeDimensionalSky`, `:619`); equals preset |
| cl_jiggle_bone_framerate_cutoff | 1 | ? | no knob |
| tf_sheen_framerate | 15 | ? | no knob |
| tf_sheen_alpha_firstperson | .5 | ? | no knob |
| r_drawropes | 1 | B | `EntityRopes.cs`, `RopePhysics.cs`; no knob |
| rope_rendersolid | 1 | ? | no knob |
| r_ropetranslucent | 1 | ? | no knob |
| rope_smooth | 1 | ? | no knob |
| rope_subdiv | 7 | ? | no knob |
| rope_collide | 1 | ? | no knob |
| rope_wind_dist | 8192 | ? | no knob |
| glow_outline_effect_enable | 1 | B | `GlowSight.cs`, `MapOutline.cs`; no knob |
| replay_rendersetting_renderglow | 1 | ? | no knob |
| tf_enable_glows_after_respawn | 1 | ? | no knob |
| tf_spec_xray_disable | 0 | ? | no knob |

### textures, vsync, HUD, sound

| cvar | preset | st | ours |
|---|---|---|---|
| mat_picmip | -10 | S | `texture_quality` command (mastercomfig's own name, not `mat_picmip`), default `Full` = no cap (`ViewerSettings.cs:526`); equals preset. Past mip 0 there is nothing to ask for (`WorldRenderer.cs:8500`) |
| mat_filtertextures | 1 | ? | no knob; filtering is on |
| mat_filterlightmaps | 1 | ? | no knob |
| mat_forceaniso | 16 | B | hardcoded 16 (`WorldRenderer.cs:8483`); equals preset; hardware ceiling |
| mat_vsync | 0 | S | off (`ViewerSettings.VerticalSync`, `:899`); equals preset |
| cl_hud_playerclass_use_playermodel | 1 | S | read from the ConVar table, default 1 (`EngineConVars.cs:273`, `TfHudPlayerClass.cs`); equals preset |
| snd_disable_mixer_duck | 0 | N | audio |
| snd_pitchquality | 1 | N | audio |
| snd_spatialize_roundrobin | 0 | N | audio |
| snd_defer_trace | 0 | N | audio |
| dsp_slow_cpu | 0 | N | audio |
| dsp_room | 1 | N | audio |
| dsp_speaker | 50 | N | audio |
| dsp_water | 14 | N | audio |
| dsp_spatial | 40 | N | audio |
| dsp_enhance_stereo | 1 | N | audio |

### Outside the ultra preset, in the owner's `modules.cfg`

| cvar | value | st | ours |
|---|---|---|---|
| fps_max | 0 (+ `mat_powersavingsmode 0`) | S | **300** (`DefaultFrameRateLimit`, `:458`); **below** |
| rate, net_splitpacket_maxrate, sdr SendRateMin/Max | 786432 | N | netcode |
| cl_downloadfilter | all | N | download |

## Tally

The cvar count is by row: the 137 distinct names found in the 34 resolved module aliases, plus 11 that `detail_props_full`, `cheap_water_never`, `shadowlod_high`, `sillygibs_off` and `bicubic_on` add (`cl_detaildist`, `cl_detailfade`, `r_drawdetailprops`, `r_cheapwaterstart`, `r_cheapwaterend`, `r_shadowlod`, `r_lightmap_bicubic`, four `violence_*`). `violence_*` share a row pair here but count as four.

**148 cvars** in the ultra preset (counted from the status column: S, B, A, N and ? rows, `violence_*` rows counted twice each).

| status | cvars | meaning |
|---|---|---|
| S | **12** | implemented as a setting: `mat_picmip` (as `texture_quality`), `mat_hdr_level`, `mat_phong`, `mat_specular`, `mat_vsync`, `r_3dsky`, `cl_drawmonitors`, `r_decals`, `r_maxmodeldecal`, `cl_detaildist`, `cl_detailfade`, `cl_hud_playerclass_use_playermodel` |
| B | 29 | drawn or simulated with no knob (value match not checked) |
| A | 15 | absent: shadows 6, flashlight 5, MSAA 2, `mat_color_projection`, `mat_motion_blur_enabled`. Eight have an "off" preset value (`cl_blobbyshadows`, the 5 flashlight, the last two), so nothing drawn matches |
| N | 20 | irrelevant to a demo viewer: 10 audio, 6 spray/custom decal, 4 other |
| ? | 72 | no knob, behaviour not checked |

**Of the 12 settings, 1 is below the preset: `cl_detaildist` (1200 against 8592).** The other 11 equal it. Outside the 12: `fps_max` (not in ultra; `modules.cfg` says 0, ours 300) is below, `phonemefilter` is a differing constant, and **MSAA 8x and shadows are real absences** (7 of the 15 `A` rows are below the preset: 5 shadow cvars and 2 MSAA cvars; the other 8 are "off" in the preset and match by drawing nothing).

## Reconciliation with `2026-10-27-defaults-vs-max-quality.md` (commit `7e7673ff`)

That audit used the owner's own `ultra.cfg`, the SDK and the wiki, and marked mastercomfig **unverified**. Now verified, against it:

- **Confirmed, and now sourced to mastercomfig:** `mat_picmip -10` (the audit called the wiki "ambiguous"; the claim at `ViewerSettings.cs:506` is true), `mat_forceaniso 16`, `mat_antialias 8`, `cl_detaildist 8592` with `cl_detailfade 400` and `r_drawdetailprops 1`, `r_lod`/`r_rootlod`/`r_staticprop_lod` all 0, `r_dynamic 1`, `r_maxdlights 32`, `r_worldlights 4`, `r_rimlight 1`, `tf_water_resolution 2048` with the four water cvars at 1 and `r_cheapwaterstart 8192`, `r_shadowmaxrendered 160`, `mat_reducefillrate 0`, `r_decals 2048`, `r_maxmodeldecal 50`, `cl_burninggibs 1`, `props_break_max_pieces -1`, `cl_phys_props_max 100` (lower than stock, as the audit said), `r_propsmaxdist 8192`, `lod_TransitionDist 3592`, `rope_subdiv 7`. The owner's `ultra.cfg` is, for these, mastercomfig's ultra.
- **Contradicted by mastercomfig:** the audit's owner `ultra.cfg` deliberately sets `mat_hdr_level 0`, `mat_antialias 1`, `mat_colorcorrection 0` and `mat_alphacoverage 0` for capture parity. mastercomfig's ultra sets **2, 8, 1 and 1**. So "implement mastercomfig ultra" and "match the owner's capture config" disagree on exactly those four; for HDR the viewer already follows mastercomfig (2).
- **Filled in (audit said unverified):** `tf_playergib` is **1** (the audit left 1 or 2 open); `cl_particle_batch_mode 0`; `mat_alphacoverage 1`; `r_flashlight*` are all **0** in ultra (the audit listed 1/1024/1/1 from the owner's file, which is the `flashlight=on` module); `fx_*` and `r_drawflecks` 1; `cl_ragdoll_*` 15/0/1/1 and `ragdoll_sleepaftertime 5` (matches the audit's 0/1/5 for forcefade/collide/sleep).
- **Corrected:** the audit said `cl_detaildist` is "the only" implemented setting below its maximum. Here it is still the only **preset-listed** one, but `fps_max` (not in ultra, in `modules.cfg`) is also below, and `phonemefilter` is a constant that differs (0.08 against .007). The audit's `-no_texture_stream` row has no mastercomfig counterpart.
- **New, not in the audit:** `tf_sheen_*`, `cl_jiggle_bone_framerate_cutoff`, `pyro_*`, `mat_motion_blur_enabled`, `tracer_extra`, `r_drawtracers_firstperson`, `mat_color_projection`, `r_shadowlod`, `nb_shadow_dist`, `cl_blobbyshadows`, `anim_3wayblend`, `blink_duration`, `phoneme*`, `tf_clientsideeye_lookats`, `r_renderoverlayfragment`, `r_decal_*`, `r_ropetranslucent`, `rope_wind_dist`, `rope_collide`, `tf_enable_glows_after_respawn`, `tf_spec_xray_disable`.
- **Unchanged conclusion:** the real gaps are absences. Two are in the preset's headline: **MSAA 8x** and **shadows** (six cvars). Raising a default needs a setting to exist first; the one default that can be raised today is `cl_detaildist` 1200 to 8592.

## Not verified

- Whether any `B` row draws at the preset's value; "drawn" is from a file existing.
- Cheat flags for everything outside `EngineConVars.cs`.
- Whether `r_cheapwater*` or `tf_water_resolution` match in the water path.
- That the `?` rows are absent; only that no cvar of that name appears in `managed/`.
- Fps cost of any row (unmeasured; `docs/measurements/` has only the HDR/LDR pair, `2026-10-22-empty-view-budget-ldr-f12.md:134`).

## Verdict

An audit with no Prediction (status line at the top), so nothing is confirmed or falsified; this is what the reading
answers. Of the 148 cvars the ultra preset sets, 12 are implemented as settings, 29 are drawn with no knob, 15 are absent,
20 are irrelevant to a demo viewer and 72 have no knob and were not checked. Of the 12 settings, 11 equal the preset and 1
is below it, `cl_detaildist` (1200 against 8592). Outside the 12, `fps_max` (300 against 0 in the owner's `modules.cfg`) is
below and `phonemefilter` is a differing constant. The real gaps are absences, led by MSAA 8x and shadows (7 of the 15
absent rows are below the preset); the one default that can be raised today is `cl_detaildist`. Unchecked: whether any
drawn row matches the preset's value, cheat flags outside `EngineConVars.cs`, and the fps cost of any row.

Verdict written 2026-10-29 after the fact from the Result above; the run itself was not repeated.
