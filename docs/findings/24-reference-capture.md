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

## 7. `mat_picmip` below −1 is a no-op, and the reason matters for our texture path — **CONFIRMED**

*Evidence class: measured in game 2026-08-16, then explained from published source.*

mastercomfig's `texture_quality=ultra` sets **`mat_picmip -10`**, outside the −1…2 range TF2's own
options offer, while `very_high` sets −1. Whether that was doing anything was an open question, and
it is the kind that has to be answered rather than assumed: a config claiming a state it does not
have is the exact failure this document exists to prevent.

Measured, in this order:

1. **Not clamped on set.** `mat_picmip -10` then `mat_picmip` echoes back `-10`.
2. **No visible difference.** Flipped between `-1` and `-10` live at red last on Badlands. Nothing
   changed on screen. (Owner observation — the right instrument for an appearance claim.)

3. **It does crash the client, but only through `mat_texture_list`** — the texture dump panel, which
   is `sv_cheats` gated. Map load and normal play are unaffected. So `ultra` is safe to ship; it is
   the *measuring* command that dies.

That third point is worth its own note, because `mat_texture_list` is what this document originally
proposed as the instrument for settling item 7. **It crashes at exactly the value it was meant to
measure.** Same family as the unescaped-`%` crontab trap in `CLAUDE.md`: a mechanism that fails the
same way as the thing under test tells you nothing either way.

So the value is stored and does nothing visible, which is the worst-looking combination until the
mechanism explains it. `vtf.h` does:

```c
#define VTF_RSRC_TEXTURE_LOD_SETTINGS ( MK_VTF_RSRC_ID( 'L','O','D' ) )
struct TextureLODControlSettings_t
{
    // keeps texture from exceeding (1<<m_ResolutionClamp) at picmip 0.
    // at picmip 1, it won't exceed (1<<(m_ResolutionClamp-1)), etc.
    uint8 m_ResolutionClampX;
    uint8 m_ResolutionClampY;
    ...
};
```

The cap is a **power of two, and picmip subtracts from its exponent**. Positive picmip lowers the
cap; negative raises it. But a texture's own mip 0 is a hard ceiling no exponent can climb past, so
once `-1` reaches native size there is nothing left for `-10` to unlock. That is also why TF2's own
UI stops at `-1`.

**Consequence for this project, and it is already true rather than planned.** "Render better than
TF2" is not `mat_picmip -10` — implementing that would faithfully reproduce a no-op. The real lever
is the `LOD` resource: a VTF carrying `m_ResolutionClampX/Y` renders *smaller* in TF2 than the file
actually contains.

`VtfTexture` reads the header through `lowResHeight` and goes straight to the image data. It never
walks the 7.3+ resource-entry table, so `m_ResolutionClamp` is never read and **this viewer already
loads at native size where TF2 would clamp**. That is a real divergence from TF2, currently in our
favour, arrived at by omission rather than intent — the shape `23-drawing-what-the-entity-says.md`
is about, with the sign flipped.

It is also deliberate policy from here: the owner's position is to leave it unclamped so video
makers can exceed the game's own look. Which means **reference capture is a parity mode, not the
ceiling** — see below.

## 8. Parity is a mode, not the goal

Items 1 through 6 pin TF2 so a capture is comparable. They do not say the viewer must never exceed
what TF2 draws, and item 7 is a case where it already does.

Both hold at once, on one condition: **a parity check is only meaningful with the viewer's own
enhancements off.** A capture compared against a viewer running unclamped textures measures the
enhancement, not the renderer. Whatever "better than TF2" features accumulate, each needs a switch
that the parity path turns off.

---

## Wrong turns, kept

**"Post-processing is a taste call."** It reads like one — mastercomfig's levels are named `calm`,
`vivid`, `washed`, `dreamy`. It is not; `calm` is `mat_hdr_level 2` and therefore selects the
lightmap set. A setting can be presented as an aesthetic and still change which bytes are read.

**"An LDR monitor means HDR cannot be tested."** Natural, and wrong. Source's HDR predates HDR
displays by roughly a decade — it is high range in the *lighting computation*, tonemapped to ordinary
8-bit output. It displays on any panel. The reason to avoid it here is items 1 and 2, not the
hardware.

**A test procedure written to be convenient, and insensitive because of it.** The `-1` versus `-10`
comparison was specified as a *live* flip mid-playback, explicitly to avoid a map reload. Textures
were therefore already resident, so nothing about texture loading was exercised — and the crash that
does exist went unseen until it turned up by other means. The convenience was the defect: an input
was chosen for which working and broken predict the same observation. Then the *first* instinct on
hearing "it crashes" was to change the config, before asking what condition produced the crash; it
was `mat_texture_list`, not map load, and the config needed no change at all.

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
