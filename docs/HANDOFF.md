# Handoff — third person is real; displacement collision is what's left

Written 2026-08-29. **Supersedes the previous handoff**, whose subject (a viewmodel that vanished)
is fixed and merged.

Everything below is on `main`, gate green: **4,202 across twelve assemblies, plus 29 UI.**

## Read this first

**The launch options are done** (B223, D118), and one sentence of what this section used to say was
wrong: it claimed `--first-person` and autoplay both failed to reach the running viewer. **Only
autoplay was broken.** `--first-person` works and was measured working — the claim about it was an
inference from a bad invocation that was never checked and then written down as fact.

Autoplay's defect was real and structural: `Apply` started playback and then called
`SetDemoLength`, whose last act is `Playing = false` through a setter that deliberately does not
raise. The viewer logged "playback started at load" and sat paused. It is the third time that
ordering has broken; the length now lives inside `DemoSystems.Open`, so there is no gap left.

**Next is displacement collision**, which is the largest single item outstanding. The chase camera's
wall clip walks BRUSHES only, so terrain is invisible to it: on a map with displacement ground —
which is most TF2 maps — the camera passes through the hillside behind a player. Its plan is below.

Everything else on the chase camera is done and matches the engine.

## What this session finished

| piece | source |
|---|---|
| animation SECTIONS | `mstudioanimdesc_t::pAnim` — this is what tore the sticky launcher |
| `m_nAnimationParity` | viewmodel animations restart instead of running off demo time |
| liveness in the CAMERA | `CalcInEyeCamView`; a dead target changes the MODE, not the viewmodel |
| `ChaseCamera` | `CalcChaseCamView` — placement, wall recovery, director parameters, second target |
| `BspLeafTree.Sweep` | `CM_TraceToLeaf` / `CM_ClipBoxToBrush` — the project's first real trace |
| `MASK_SOLID` | glass, grates and moving brushes stop the camera |
| `hltv_chase` | the director's shots reach the timeline |
| `CameraMode.ThirdPerson` | a real third mode, reached by Source's own command names |

## After that: displacement collision

**Geometry already exists.** `BspTerrain.ReadTriangles(BspSurface)` returns a displacement's
triangles, and `BspDisplacements` reads the lumps. What is missing is the collision maths and the
narrowing.

Three parts, in the order they should be built:

1. **The primitive** — `CDispCollTree::SweptAABBTriIntersect` (`public/dispcoll.cpp:869`). A swept
   SAT: the box's three axial planes, then the triangle plane, then the nine edge cross-product
   planes. About 400 lines in Valve's version. It returns a fraction exactly as
   `CM_ClipBoxToBrush` does, so it plugs into `BspLeafTree.Sweep` beside the brush clip.
2. **The narrowing** — leaf → `LUMP_LEAFFACES` → faces → `dispinfo`, so a trace tests the terrain
   near it rather than every triangle on the map. `BspLumpIndex.LeafFaces` is already declared.
3. **The per-displacement AABB tree** Valve walks inside one displacement
   (`CDispCollTree::AABBTree_*`). Leave this last: it is an optimisation, and correctness without
   it is testable.

**Fixture, not corpus.** A real map cannot isolate one triangle, so the exact prediction needs a
hand-built world — the same reason `BspTraceMaskConformanceTests` builds a single brush.
`BspLeafTree.FromCollisionLumps` is the entry point that exists for this; displacements will need a
sibling.

**The trap that will cost an hour if it is not known**: the sweep's leaf walk splits the segment,
but the brush clip is handed the WHOLE ray, because `CM_TraceToLeaf` clips the entire trace and the
tree walk only chooses candidates. Handing a sub-segment to a clip finds no entry — the piece
already begins inside the surface — and the sweep reports clear. Do the same for displacements.

## Also open, smaller

- **`--look` and `--zoom` are parsed and then ignored**, and say nothing about it. They meant
  something only for the orthographic camera D98 removed. Either give them a meaning for a camera
  placed in the world — fly to a point, at what distance — or refuse them with a message. A silently
  dropped option is the class of defect B223 was.
- **A blue medic draws with a red viewmodel.** Reported, untouched.
- **Audio was lost at some point** in the days before this session. An output-level instrument was
  added to `SoundPresenter` (submitted vs dropped for zero gain) and has never been read on a run.
- **`bip_upperArm_L` jumps 3–9 units between frames** in `c_demo_animations`, down from 245 after
  the section fix. Real motion or a second smaller decode fault — undetermined. Everything
  structural was ruled out: sections, zeroframes, local hierarchy, chain order, posscale/rotscale
  offsets, raw-vs-RLE mixing, encoding flags.
- **`CTRL+b` has no UI coverage.** The harness cannot drive modifier combos (B216, established with
  a control arm), so the third-person UI tests reach the mode through the SPACE cycle instead. The
  mode is covered; the binding's resolution is not.
- **No wall trace for the recovery while the demo is paused** is correct, not a gap — see below.

## Things that were measured, so nobody re-measures them

- **No demo within reach carries `hltv_chase`.** Searched the bytes: the string appears zero times
  in `cp_process_f12` and the 2013 badlands specimen, while `player_death` appears in both. Every
  demo here is point-of-view; only a SourceTV broadcast has a director. A corpus test asserts that
  absence and will go red when it stops being true — that is the moment to point
  `DirectorShotTests`' authored specimen at real bytes.
- **`cp_process_f12` entity 1 is dead for the whole demo**, one transition. The badlands recorder is
  alive 1295 of 2017 samples with seven clean transitions, so `m_lifeState` decodes correctly.
- **The UI suite opens at tick 1900**, chosen because the recorder is alive there. It was 2500,
  where he is dead — and the position that constant's comment praised as "out on the map" is the
  freezecam, not the player. Alive spans: 0–2008, 3208–4944, 6228–7700.
- **TF2's `config_default.cfg` binds 64 keys** and leaves exactly one letter free: `o`. All twelve
  function keys are taken between TF2 and this viewer. CTRL combos are the only free space, since
  Source's bind syntax has no modifiers.

## Two rules this session produced

- **D116 — an invariant is part of the mechanism.** Porting one half of a behaviour Valve split
  across two systems breaks the invariant the other half relies on, silently. `ShouldDrawViewModel`
  has no liveness term because the CAMERA guarantees it never needs one.
- **D117 — implement the feature; do not omit it and document the omission.** The chase camera
  shipped with five parts of `CalcChaseCamView` missing, each with an accurate comment. Accurate
  notes are what made the shape hard to see.

## How to run the gate

```bash
TF2DEMOSALVAGE_GCOR_ONLY=1 bash build/gate.sh
```

```bash
pwsh ../run-exclusive.ps1 dotnet test tests/Tf2DemoSalvage.Viewer3D.UiTests
```

Two phases, and one `dotnet test` on the solution is not a valid substitute — the UI suite drives a
real window and must not run beside 4,000 other tests. **Set `MSBUILDDISABLENODEREUSE=1`**: MSBuild's
node daemons and `VBCSCompiler` outlive the build and accumulate (589 MB found sitting idle this
session; `dotnet build-server shutdown` reaps them, never `pkill -f`).

**Read the trx total, never the console line.** A direct run printed `Total: 115` while the trx
recorded `total="133"`. `assert-test-count.sh` reads the trx, and this session wasted several minutes
chasing a "falling count" that came from believing the console.

## Verification practice that earned its keep tonight

Three defects were found by **sabotage**, not by writing tests:

- Inverting the brush clip's convex test left all four of its tests GREEN — they were counting a
  fraction of `0` as "the floor is right there", when `0` means startsolid, "the sweep never
  started". Fixing that one assertion turned three false passes into an honest failure, and every
  bug after it was found through that failure.
- Restoring `CONTENTS_SOLID` in place of `MASK_SOLID` reddens exactly three cases — glass, grate,
  moving brush — which is how the mask tests are known to measure the mask.
- Disabling the section lookup reddens the continuity tests; one case, animation 76, survived it and
  was replaced. A case that cannot fail is not a weak test, it is an absent one.
