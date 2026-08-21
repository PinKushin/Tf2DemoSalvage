# Handoff — first person, and the lighting it exposed

Rewritten 2026-08-20 at the end of a long session. The viewmodel work that named this file is done
and merged; most of the session was what looking at the picture then turned up. Everything below is
committed, pushed, and green.

Read `docs/findings/30-viewmodel-drawing.md` for the viewmodel reasoning and `docs/RISKS.md` B95,
B119–B123 for the lighting. This file is the state of play and the traps.

---

## What works

| Piece | Where |
|---|---|
| First-person camera, POV and SourceTV | `FreeCamera.AtEye` / `.SpectatingEye` |
| Choosing whom to watch | `SpectatorTarget`, `--spectate <entity>` |
| Opening at a tick without capturing | `--tick`, `--first-person`, `--colours` |
| Arms, correct class and animation | `DemoTimeline.ViewmodelAt` |
| The weapon in those hands | `ItemSchema`, `WornModels`, bone-merged |
| The spy's watch beside it | `OffHandViewmodelAt`, gated on `EF_NODRAW` |
| Local lights | `LocalLights`, now in the right units |
| Props with no baked lighting | `PropModels.Load`, lit from the cache |

```bash
./managed/Tf2DemoSalvage.Viewer3D/bin/Debug/net10.0-windows/tf2demoview.exe tools/corpus/demos/z1800.dem --spectate 11 --tick 47601 --first-person
```

Gate: **2,746** across six assemblies, plus **12** UI. Floors are exact — `build/gate.sh`.

---

## Open, in the order I would take them

**B55 — reflections.** The capture point draws as a dark disc because `cap_point_base` is
`VertexLitGeneric` with `$envmap env_cubemap` and nothing samples it. **The lump reader is NOT the
blocker**: `BspCubemaps` is complete and correct — placements, the size code, and vbsp's own texture
name. What is missing is the renderer: load a cubemap VTF as a cube texture, bind it per prop, sample
with the reflection vector, mask by normal-map alpha, tint by `$envmaptint`. Measured: 225 of
`cp_badlands`'s 225 samples have a packed texture, and 79 of `cp_process_final`'s 410 materials ask
for `$envmap`.

**B71 — brush entities never move.** Drawn at their compiled position, so doors and spawn gates are
shut for the whole demo. This is why a capture at the end of the badlands demo is a wall.

**B123's other half.** Static props still receive no *local* lights even when their baked lighting is
valid — `LightAt` reaches them only when nothing was baked. The engine gives every prop the cube and
up to four local lights.

**The HUD**, and **spy cloak transparency** — `m_nPlayerCond` is not decoded at all, so cloak has no
input yet. Bob, lag and shake stay deliberately unimplemented: all three are functions of movement and
elapsed time rather than anything a demo records.

---

## Instruments that exist — use them before building another

- **`build/gate.sh`** — six projects, exact per-project floors. It caught a deleted implementation
  this session.
- **`build/assert-decision-numbers.sh`** — fails on a decision number used twice, or a gap.
- **Frame hashes.** Two identical launches produce byte-identical PNGs, so a hash change proves a
  render change before anyone looks: `352EBD85` → `B2192859` → `08C14B3E` tracked three fixes.
- **`FrameStructure.Colours`** — distinct coarse colours in a capture. A wall is 17, a map view 146.
  Brightness cannot tell them apart; 93% of a map capture's pixels are "lit" and so are planks.
- **Probes**, all `[Explicit]`: `OffHandProbe` (which tick to open at), `LocalLightContributionProbe`,
  `OverlayAndCubemapProbe`, `HeldWeaponProbe`, `ItemDefinitionProbe`.

---

## Traps this session actually hit

**Search before building.** `BspCubemaps` and its ten tests already existed and I overwrote them with
a thinner, partly wrong copy — the write said "updated", not "created". B120 was a duplicate of B95.
The lightmap-versus-cube comparison had been half-done already. Grep costs seconds.

**Measure what a change can reach before capturing what it did.** `1186 of 1232` placements keep their
baked lighting, so the prop-lighting fix could never move the capture point — and that count was in
the log before the screenshot was taken.

**A green suite can defend the wrong answer.** Twelve light tests wrote the wrong scale into their own
expectations; an on-axis spotlight test could not see a missing cosine; a UI capture asserted
brightness where a wall passes. What broke each open was measuring against something outside this
project: vrad's source, a map's authored `_light` keys, the compiler's own lightmap.

**The comments knew.** `LocalLights` said "a test that supplies its own intensity has no opinion about
what units a map uses" — then the constant was chosen on such a test. `SourceSdk` forbade sharing an
answer between callers — then handed out a mutable reference. `WornModelPaths` described the
bake-versus-merge failure in full while the viewmodel weapon bypassed it.

**Two confident numbers were wrong.** A lightmap-to-cube ratio of 231.8 compared a stored value with a
used one and nearly "fixed" a correct decoder. The console test total disagrees with the `.trx` by
one, and I nearly lowered a floor on it. Both were persuasive because they were nearly 255 and nearly
right.

**A background-task "completed" reports the wrapper, not the app.** Ask `Get-Process`. A log read
while it is still being written looks exactly like a crash — one went from 860 lines to 79 MB while I
called it dead.

---

## Corrections the owner made, which changed the work

- **"we shouldnt be forcing any sequence only stuff from the demo or how valve does it."** The viewer
  substituted `VM_IDLE` for the recorded sequence, which posed the spy's arms for a weapon they were
  not holding and dropped the knife to the bottom of the frame.
- **If a convention is wrong, fix it rather than transposing around it.** Checked: two conventions,
  deliberate. The boundary was unnamed and implemented twice, which was the real defect.
- **The screenshots were of a wall.** The capture test jumped to the demo's end to satisfy a
  constraint that did not exist — a comment claiming TF2 no longer ships `v_` viewmodels. It does;
  every off-hand watch in z1800 is one.
- **"theres scout and soldier ticks"** and **"i know i went out of spwan"**, both right, and both
  contradicting my conclusion that the recorder never leaves spawn.
- **75, then "sorry 70 if thats parity"** — D43. The bound stays the engine's; only the default is
  ours.
- **Grenades do not use the off hand.** Living SDK code that nothing shipped exercises.

---

## Corpus notes

`z1800` is the only real match in the committed corpus — 9v9 Highlander, `koth_harvest_final`, 25
players, all nine classes, six spies, three sappers. Every era specimen is a solo recording with one
player and no cosmetics. `tf2-2013-build1729296-pov-cp_badlands` is the UI suite's demo and the only
POV recording with a recorded camera; it opens at tick 2500, where the recorder is out on the map with
a rocket launcher.

`TF2DEMOSALVAGE_GCOR_ONLY=1` runs the corpus suite in 28 seconds instead of 30 minutes.
