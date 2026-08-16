# Configuring TF2 so a screenshot is a valid reference

A screenshot from the real game is the only ground truth this project has for "what should it look
like". That makes the game's draw state part of the measuring instrument, and an instrument that
drifts is worse than none — it produces a difference, and the difference is attributed to our
renderer.

Everything below is about pinning that state. Determined 2026-08-16 against mastercomfig 9.x
(`mastercomfig-base.vpk`, ultra preset) on a 1920×1080 / RTX 3060 / 119 Hz machine, capturing at
desktop resolution and downscaling.

---

## 1. The game must run LDR, and it is not a preference — **CONFIRMED**

*Evidence class: read from this project's own source, plus the map measurement in
`RENDERING_NOTES.md` §6.*

`BspLightmaps.cs` chooses the LDR lump and says why:

> LDR is the lump that matches the renderer, and HDR is the fallback for a map that has only that
> one. Rendering HDR properly needs a tone map rather than a multiply.

`StudioVertexLighting.cs` reaches the same preference independently for static props — `sp_<n>.vhv`
in LDR, `sp_hdr_<n>.vhv` in HDR, and it records that **the two hold different values, HDR being
authored brighter**.

TF2's `mat_hdr_level` selects which set the *game* reads out of the same BSP. `koth_harvest_final`
carries both at 6,095,136 bytes each. So at `mat_hdr_level 2` the game renders one baked lighting
solution and we render a different one, from the same file. No shader work closes that; it is not a
shading difference, it is a different input.

**Reference captures therefore require `mat_hdr_level 0`.**

## 2. Auto-exposure is a second reason, and it outlives implementing a tonemap — **CONFIRMED**

*Evidence class: Source engine behaviour, `mat_hdr_manual_tonemap_rate` and per-map
`env_tonemap_controller`.*

Source's HDR path tonemaps to 8-bit output through an **adaptive** exposure stage — the walk-out-of-
a-tunnel effect. Exposure is a function of what has recently been on screen, so two captures of an
identical camera position can differ depending on where the camera was pointed a second earlier.

This matters beyond item 1. When Phase 3.x eventually implements a tonemap and the lightmap-set
objection goes away, **reference capture should still be LDR**, because the thing that breaks
reproducibility is the adaptation, not the range.

## 3. MSAA must be off, because our capture path already antialiases — **CONFIRMED**

*Evidence class: arithmetic on the capture pipeline.*

Capture is at desktop resolution and downscaled afterwards. Downscaling **is** supersampling
antialiasing, and it is applied to the reference and to our output equally. Multisampling in TF2 on
top of it antialiases the game's edges twice and ours once, so edges cannot agree.

`mat_antialias 1` (mastercomfig `anti_aliasing=off`) puts both sides on the same method. This
matches `13-settings-parity.md`, which records the swap chain as single-sampled.

## 4. Two settings are non-deterministic per frame — **CONFIRMED**

*Evidence class: read from mastercomfig's module bodies in `mastercomfig-base.vpk`.*

- **`cl_jiggle_bone_framerate_cutoff`** disables jigglebones below the given framerate. mastercomfig's
  `jigglebones=on` sets it to **67**; `force_on` sets it to **1**. At `on`, with an uncapped
  framerate, the same scene draws differently depending on the instantaneous frame rate. Use
  `force_on`.
- **`tf_sheen_framerate`** animates killstreak sheens over time, so any sheened weapon in frame makes
  the capture time-dependent. `sheens_speed=off` sets it to 0.

With the jigglebone cutoff at 1, framerate no longer changes what is drawn, so an uncapped
`fps_max 0` is fine and needs no separate pinning.

## 5. Sprays put third-party content in the frame — **MEASURED**

*Evidence class: measured on the live install, 2026-08-16.*

The installation carried `tf/materials/vgui/logos/spray.vmt` referencing
`vgui\logos\engineer-tf2_00246784` — another player's spray, cached from a server, in a folder the
owner did not knowingly populate. Any capture on a map where that spray is placed contains an image
we do not have and cannot draw.

`sprays=off` also sets `cl_allowdownload 0`, which prevents further server content arriving. That
supersedes `download=all`; for a reference machine, that is the desired direction.

## 6. Launch options override the config, and no reinstall clears them — **MEASURED**

*Evidence class: read from `userdata/<id>/config/localconfig.vdf`, 2026-08-16.*

The observed options were:

```
-novid -nojoy -nosteamcontroller -nohltv -particles 1 -noborder
```

**`-particles 1`** starves the particle system regardless of what `effects=ultra` sets, because the
command line wins. A reference machine must not carry it. `-nohltv` disables client SourceTV support
and deserves a deliberate decision on a project that records demos.

These live in Steam's cloud-synced config, not in the game folder, so verifying files or reinstalling
does not touch them.

## 7. Still unverified

**`texture_quality=ultra` sets `mat_picmip -10`**, outside the −1…2 range the game normally accepts,
while `very_high` sets −1. If the value is clamped without `sv_cheats`, the two levels produce an
identical picture and the config's stated state is not its actual state. `mat_picmip` with no
argument echoes the effective value; that check has not been run yet.

Until it is, treat `-10` as "requested" rather than "in effect" — which is exactly the class of
silent divergence this document exists to prevent.

---

## Wrong turns, kept

**"Post-processing is a taste call."** It reads like one — mastercomfig's levels are named `calm`,
`vivid`, `washed`, `dreamy`. It is not; `calm` is `mat_hdr_level 2` and therefore selects the
lightmap set. A setting can be presented as an aesthetic and still change which bytes are read.

**"An LDR monitor means HDR cannot be tested."** Natural, and wrong. Source's HDR predates HDR
displays by roughly a decade — it is high range in the *lighting computation*, tonemapped to ordinary
8-bit output. It displays on any panel. The reason to avoid it here is items 1 and 2, not the
hardware.

**"`bandwidth=6.0Mbps` is not a valid mastercomfig level."** Claimed while enumerating module levels
with a pattern that excluded `.` from level names, which silently truncated the list at `762Kbps`.
`bandwidth_6.0Mbps` exists and sets `rate 786432`. A tool that filters its own input is a tool that
reports confident absences.

---

## Where the config lives

`PinKushin/Pin-Config` — a flat Source `.cfg` with every render cvar stated explicitly, harvested
from mastercomfig's module bodies rather than depending on them at runtime. It is versioned so that
a capture can be attributed to an exact draw state, and regenerable so a mastercomfig update can be
diffed rather than absorbed.

That repo is the machine-readable statement of what the game was told to draw. When a capture and
our output disagree, it is the first thing to check for drift.
