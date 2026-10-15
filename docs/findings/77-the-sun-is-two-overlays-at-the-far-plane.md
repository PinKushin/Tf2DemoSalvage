# 77 — The sun is two overlays asked at the far plane

B378's last residual was *"`CGlowOverlay` — the sun's glow asks the same system and is not drawn through it"*, and
beside it *"a query per VIEW (the monitor and water views ask nothing)"*. Both were filed as waiting on a caller that
did not exist. This is what the engine does once the caller is read, and what it turned out the second question was.

## Where a sun exists at all

*Measured* (2026-10-14, `grep -a '"classname" "env_sun"'` over the installed `tf/maps`): eight of 242 shipped maps carry
one — `arena_offblast_final`, `arena_ravine`, `cp_egypt_final`, `koth_harvest_final`, `koth_king`,
`koth_lakeside_final`, `mvm_bigrock`, `mvm_coaltown`. None of the committed era specimens is on one (`entity-keys` on
granary, badlands, viaduct and foundry: 0, with `light_environment` on viaduct as the control: 1). The one recording
in lcor on such a map is `tf2-2026-pov-koth_lakeside-triggers.dem`.

## The wire

*Read from published source*, `sun.cpp:54-64` and `c_sun.cpp:21-33`. `DT_Sun` is **NOBASE**: no origin, no model
index — a direction, `m_bOn`, two sizes, two colours, two material indices and `HDRColorScale`. It is
`FL_EDICT_ALWAYS`. So a sun can never be a prop track: the timeline's admission gate wants a model. It is a feed, shaped
like the tonemap controller's (`SunFeed`).

The server resolves the direction once, in `CSun::Activate`: with `use_angles`, `SetupLightNormalFromProps` negated;
otherwise toward its target. `overlaysize -1` becomes the core's size, and a missing material becomes
`sprites/light_glow02_add_noz.vmt`, precached **as a model** — so the client names it through the model string table.

**The colours put red in the HIGH byte.** `SendProxy_Color32ToInt` is `r << 24 | g << 16 | b << 8 | a`
(`sendproxy.cpp:21`) and `RecvProxy_IntToColor32` reads it back the same way (`recvproxy.cpp:20-23`). *Measured*:
lakeside's sun sends `m_clrRender = 3838491391 = 0xE4CABAFF` for the map's `rendercolor "228 202 186"`.
**A wrong conclusion, kept.** `EntityState.RenderRgb()` reads red from the LOW byte, and the first reading here was
that it must be byte-reversed too. It is not: `DT_BaseEntity.m_clrRender` names no proxy (`baseentity.cpp:279`), so it
gets `SendProxy_UInt32ToInt32`, a raw copy of the `color32` struct (`dt_send.cpp:157-160`) — red low on x86 — and the
client copies it back (`c_baseentity.cpp:457`). Only tables that name `SendProxy_Color32ToInt`, like the sun's, put red
high. *Measured*, lakeside's `env_sprite`s: `0x7D248CFF` on the wire for map `rendercolor "255 140 36"`,
`renderamt 125`. One field name, two packings, chosen per send table.

## The client

*Read from published source*, `c_sun.cpp:50-138`, `c_sun.h:166-195`, `glow_overlay.cpp`.

- **Two overlays.** `m_Overlay` is the core: its own size, the render colour renormalized so its largest component is
  one ("make sure only hue is altered"). `m_GlowOverlay` takes the overlay colour, or the core's when that is black,
  and `SetModulateByDot`: six times the size, alpha `RemapVal( dot, 1, 0.9, 0.75, 0 )` clamped to [0, 0.75] — gone
  once the view is more than about 26° off the sun.
- **Drawn a hundred units in front of the eye**, not at the sky: `CalcBasis` puts the quad at
  `CurrentViewOrigin() + vToGlow * 100`, right = `vToGlow × (0,0,1)`, up = `right × vToGlow`. The material is
  `$ignorez`, so no depth test cuts it; only the query hides it.
- **Each overlay owns a query handle**, and both ask at `CurrentViewOrigin() + m_vDirection * zFar * 0.999` with
  `m_flProxyRadius = 0.05` and `bSizeInScreenspace`. That flag is set by `CGlowOverlay` and nothing else. In
  `PixelVisibility_DrawProxy` it replaces the five-pixel floor with `scale = PixelDiameter( eye + forward, scale / 2 )
  / PixelDiameter( origin, 1 )` — the proxy covers what a 0.05 sphere one unit from the eye would, wherever it is.
- **`m_skyObstructionScale` is a second query, in the 3D skybox's own view** (`UpdateSkyOverlays`,
  `viewrender.cpp:4937`), multiplied in. It defaults to 1.

## Which views draw it — the "query per view" question, answered

*Read from published source*, `viewrender.cpp:1388-1450`. `CGlowOverlay::DrawOverlays` runs in `ViewDrawScene`, and
`ViewDrawScene` is called by `RenderView` and `DrawOneMonitor` only. **A water reflection or refraction draws no glow
overlay at all.** A monitor does call `DrawOverlays`, so its overlay asks for a query keyed to `VIEW_MONITOR` — but
`PixelVisibility_EndCurrentView` runs only `if ( IsMainView( CurrentViewID() ) )`, so that query is created and never
issued, and a never-issued query answers `m_brightnessTarget * m_clipFraction`, which starts and stays at zero. **No
sun is ever seen in a monitor.** So the main-view-only system this project has is the engine's answer for every view;
the missing half was never a per-view query, it was the sun.

## What is not ported

- `m_skyObstructionScale` stays 1: scenery in the 3D skybox does not hide the sun. That needs a query issued in the
  sky view's own depth, before the main view clears it.
- `$hdrcolorscale` is carried and not applied, as for every entity sprite.
- `R_CullSphere` is not run; it only saves a draw the rasteriser clips.
- The depth state is `$ignorez` for every sun because every shipped sun uses `light_glow02_add_noz`.

## Against TF2: the glow was right and the light scale was missing

The first golden comparison had TF2 at 6,747 pixels of white about the sun and ours at 30,512, with our wall drawn
larger. Taken apart one variable at a time, at one camera, counting white pixels and reading a radial profile:

| capture | white px |
|---|---|
| TF2, live client, `fov_desired 75` (the first golden) | 6,747 |
| TF2, `fov_desired 90` — the recorder's fov — retaken here | 4,984 |
| ours, recorder fov 90, before | 30,512 |
| ours, bloom off, before | 14,750 |
| ours, after the tone-map fix | 14,854 |
| ours, after, bloom off | **4,999** |

**The framing was the reference's, not ours**: the demo's free camera takes the recorder's `GetFOV()` (90), the first
golden shot was a live client at 75. **The brightness was ours**: UnlitGeneric (the sun's material), SpriteCard and
Cable end in `TONEMAP_SCALE_LINEAR` — `result.rgb * LINEAR_LIGHT_SCALE` — and this project's linear sprite path never
multiplied by it, so every such sprite drew at a tone-map scale of one, ~1.4x too bright at TF2's outdoor 0.70. A glow
whose texture falls off gently turns a gain error into a radius error: the white disc was twice as wide. With the scale
applied and bloom off, the sun matches TF2 to the pixel count. The rest is bloom, filed in finding 76.

## An instrument that lied

The first two screenshots of lakeside were byte-for-byte the recorder's view, from two different `TF2VIEW_CAMERA`
positions, while the log said *"free camera placed from TF2VIEW_CAMERA"*. `CameraPresenter.DemoOpening` set first
person for every POV demo (D153) without the headless exemption every other lock site honours — so a POV capture
logged the placed camera and photographed the recorder. The control that caught it: a third, wildly different
placement giving the same picture.
