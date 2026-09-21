# 61 — Debris is the old particle system

**Question.** A bullet into concrete throws dust and flecks; into metal, sparks. Which system draws them, and with
what colour?

## The branch nobody sets

`PerformCustomEffects` (`fx_impact.cpp`) has two implementations, chosen by `cl_new_impact_effects`, whose default is
`"0"` (`fx_impact.cpp:212`). The new one looks up PCF systems (`impact_concrete` and friends) that TF2 does ship. The
old one is a set of hand-written emitters: `FX_DebrisFlecks`, `FX_DustImpact`, `FX_MetalSpark`, and `g_pEffects->Sparks`.
No shipped config sets the cvar. The owner's own `ultra.cfg` sets `r_drawflecks 1` and leaves the cvar alone, and
`low.cfg` sets it to 0 explicitly. So TF2 draws the old emitters. *Read from source and the shipped configs.*

The old emitters are their own particle system, `CSimpleEmitter` and its subclasses, with rules that are easy to
lose in a port:

- **`CDustParticle` ignores its own start and end alpha.** Its `UpdateAlpha` is `1 − t`, squared once it falls below
  0.75. Its velocity decays by `exp( log( 0.0001 ) · dt / 0.5 )` and never falls below 32 units a second. Its roll
  decays by a factor of eight a second and never falls below 0.5.
- **`CFleckParticles` bounce off planes found in advance.** `CParticleCollision::Setup` simulates one average
  particle in eight steps over two seconds, along the burst's direction and its right and left. The first surface met
  on each becomes a plane. A fleck crossing one of those planes is retraced against the real world. It settles if the
  surface faces up and it is falling slower than 48 units a second; otherwise it reflects, damped. A fleck crossing
  no plane is never traced at all.
- **`FX_DustImpact` computes an offset for its third kind of particle and never uses it.** The particles start at
  the origin. Reproduced.
- **`particle/particle_smokegrenade` states `srgb?$alpha .27`.** TF2 on the PC takes the sRGB branch, so every dust
  puff draws at 27% of the alpha its emitter gives it.

## The colour: three closed functions

`GetColorForSurface` is published and short. It traces 1.1 times the distance to the hit with
`engine->TraceLineMaterialAndLighting`, then returns `pow( diffuse, 1/2.2 ) · base`. Everything it calls is closed.

- **`TraceLineMaterialAndLighting`** (`CEngineClient` vtable slot 2, `engine.dll` `0x180071480`) forwards to
  `0x1801cb5b0`. That calls `R_LightVec` (`0x1800d40a0`) for the face, its light and its texture coordinate, then
  `material->GetLowResColorSample( s, t, base )`.
- **`R_LightVec`'s walk** (`0x1800d4b00`) goes near side first. Where the ray crosses a node's plane it tries that
  node's faces, keeping sky aside. In a leaf it tries the faces not on a node whose plane the ray meets from the
  front. The face test (`0x1800d3990`) accepts a point inside the face's lightmap rectangle.
- **The light is the face's AVERAGE, not the luxel under the point.** The face test has two branches, chosen by
  `r_avglight`, which `engine.dll` registers with default `"1"` and `FCVAR_CHEAT`. The default branch reads the
  per-style average colours vbsp stores just before the samples, style k at `lightofs − 4·(k+1)`. The luxel read was
  ported first, from the other branch, and replaced once the convar's default was read.
- **`GetLowResColorSample`** (`materialsystem.dll` `0x180039c10`, through `CMaterial`'s representative texture at
  `+0xb0`) samples the VTF thumbnail bilinearly with wrap. It divides the bytes by 255 with no gamma conversion.
  `CTexture::LoadLowResTexture` converts that thumbnail to RGB888 at load.

## Found on the way: bursts replayed every frame

`ParticleEffects.Retire` dropped a burst the moment it finished, but the viewer offers each explosion for 200 ticks
and each tracer for 120. The next frame therefore found a finished burst missing, built it again and replayed it from
its own tick. It did this every frame, for every finished effect still in its window. *Read in the code; not
measured.* B415 had filed it as a suspicion from a run whose viewer was in the background.

## Not established

The list is under B415 in `docs/RISKS.md`. The largest gaps are fleck-emitter merging, displacements and props in
the light walk, and stepping per tick rather than per frame.
