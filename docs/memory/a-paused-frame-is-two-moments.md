---
name: a-paused-frame-is-two-moments
description: "A paused TF2 frame draws entities at their last received origin and particles where the last PLAYING frame left them, an interpolation delay behind; added light over a surface is an effect, not the surface (B536, B538)."
metadata:
  type: project
---

**A frame taken by playing to a moment and pausing shows two moments.** Entities snap to their last received origin
(`InterpolateServerEntities` stops interpolating while paused, B399). Particles do not move at all: the particle
manager is stepped by `gpGlobals->frametime` (`cdll_client_int.cpp:2234`). So every effect that follows a player —
a medigun beam's far end and glow, overheal pluses — hangs the interpolation delay BEHIND him: FOUR ticks with the
installed config (lerp two, plus the two an update waits), a dozen units for a runner. From a chase camera that puts
a glow born inside his chest on his back.

**"Ten ticks" was a wrong turn (B538, corrected B539).** One picture gave two delays: the cloud left of the patient
said eleven, the glow on his body said four. The engine's arithmetic says four and has no SourceTV case —
`GetClientInterpAmount` (`cdll_bounded_cvars.cpp:127`), `cl_updaterate` clamped to `sv_min`/`maxupdaterate` and
nothing else (`engine.dll 0x180164230`), `tv_snapshotrate` referenced only by its own registration. **When two
features disagree about one number, trust the one the engine's arithmetic predicts, and file the other as a thing
not drawn.** The cloud is open in B539.

**Why:** B536 was two batches on a "sky-lit wall 1.4-1.9 times too dark and not blue enough", through the lightmap
formula, the LDR lumps and `mat_hdr_level`. Every measurement of the wall was correct. One `mat_fullbright 2` capture
(flat grey world, blue still there) showed it was the heal glow parked on the wall by the pause. The sentence that
should have stopped it was already in the entry: the difference *"reads as ADDED light rather than a factor"*.

**How to apply:**

- **Added light is an effect.** A surface that differs by an amount rather than a ratio, more in one channel, and
  only near a player: look for a particle or a sprite before reading a shader. Ask for `mat_fullbright 2` first; it
  costs the owner one capture and removes the whole lighting path.
- **Compare world lighting on a frame with no effect in it.** A paused reference near a healed, burning or
  overhealed player is not such a frame.
- **The viewer keeps a following effect at the PLAYING samples** on a paused frame and through a seek's catch-up
  (`AddHealBeams`, `AddOverheals`: `DemoTimeline.PlayersAt( tick, …, interpolating )`), so a paused `--shot`
  reproduces both halves.
- **The delay is the watcher's lerp, the same config both sides, so the golden recipe pins nothing about it.** Never
  pass `+cl_interp` to "match" a reference: it moved the glow off the patient to imitate a cloud that was something
  else.
- **`mat_fullbright 2` is `TEXTURE_GREY` read as sRGB**: 0.2158 as light, not 0.502 (B539). `+mat_fullbright 2` works
  at launch; a lighting-only shot of ours is now comparable with TF2's.
- **Not evidenced, do not re-derive:** `CClockDriftMgr::SetServerTick` (`engine.dll 0x180164940`) leaves the client
  clock alone within `cl_clockdrift_max_ms` (ten ticks) and `AdjustFrameTime` (`0x180164610`) corrects nothing while a
  demo plays, so a `demo_gototick` could leave a session up to ten ticks late. `cl_clock_showdebuginfo 1` prints it.
- **Bones and origin must come from one pose.** The frame a seek lands on asks for hitboxes before its models are
  posed again; last tick's bones with this tick's origin put a two-second catch-up hundreds of units away.
  `EntityModels.PosedHitboxes` returns the origin it was posed at — use that pair, never the frame's player list.

**From the same hunt, engine facts worth not re-deriving:**

- SpriteCard's vertex shader converts the particle tint from gamma (`GammaToLinear( v.vTint )`,
  `spritecard_vsxx.fxc:182`); the spline (trail) shader does not (`splinecard_vsxx.fxc:60`). A pale, cyan-ish
  particle that should be saturated is the missing conversion, not exposure.
- `Movement Lock to Bone`'s fade is in SECONDS of age, not life fractions.
- `Position on Model Random`'s "hitbox scale" is an int unpacked into a float member: 1 arrives as 1.4e-45, so each
  box coordinate is one minus its draw. Reproduced on purpose.
- The medigun trail's cards being collinear on the path is the port agreeing with `C_OP_ConstrainDistanceToPath` and
  `Simulate` as re-read in `particles.obj`; why min = max = 16 gives a line and not a tube is *interpolated* (the
  first pull is along the path). The reference's beam IS a wide haze where ours is a streak (B539, open): this
  argument is the first thing to test there, against a side-on capture.
