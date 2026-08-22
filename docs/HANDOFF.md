# Handoff — the overlay session, and what it left open

Written 2026-08-21 at the end of a very long session. Everything below is committed and the gate is
green: **core 1461, cli 68, audio 28, content 606, corpus 95, viewer 568**, plus 12 UI. Nothing is
pushed.

The previous handoff described the fog/decode work and is superseded for anything about rendering;
`docs/HANDOFF-viewmodel.md` covers the first-person work and still stands.

---

## Start here, in this order

### 1. Look at the current build before changing anything

Three rendering changes went in that the owner has **not** seen: render state moved onto the material,
the overlay pass culling back faces, and the slope-scaled depth bias restored. Each was verified by
test; none by eye.

```bash
pwsh run-exclusive.ps1 ./managed/Tf2DemoSalvage.Viewer3D/bin/Debug/net10.0-windows/tf2demoview.exe tools/corpus/local/demostf-cp_process_f12-2026-08-07.dem
```

What to check, and what each answer means:

| look at | if wrong |
|---|---|
| stripes on walls, moving the camera | shimmer means the slope bias is not enough; **do not add a constant one**, see B135 |
| pipes and light fixtures at BLU spawn | behind the stripes means the pass order regressed |
| REDSTONE CARGO lettering on its silo | readable through the tower means the cull is not applying |
| the mid shipping containers | rocks showing through means depth writes are off for props again |

### 2. B135 — finish or close it

**Open.** `docs/RISKS.md`. What is done: props draw after overlays; overlays cull back faces, write
no depth, compare `LessEqual`, and carry only the slope-scaled bias; depth state is chosen per
material from `$decal` rather than per pass.

What is **not** done: the owner last reported stripes shimmering with all bias at zero. The
slope-scaled term was restored in response and has not been looked at since. If it still shimmers,
the answer is **not** a constant bias — that is what pulled overlays through walls twice — it is that
`ClipFaceToOverlay` is producing geometry that is not exactly coplanar, which is a real defect worth
finding.

### 3. The conformance sweep — about 45 tests

**This is the highest-value work left, and it is mechanical.** `docs/CONFORMANCE.md` carries the
audit and the classification. The short version:

- 29 files / 107 tests import the SDK helper and **no production namespace**, so they cannot fail for
  any reason concerning this renderer.
- Of those, ~34 are gap markers with a legitimate job (D45) and 5 test the SDK helper itself.
- **~45 are value pins that assert an SDK constant and never compare it to ours.** Those are the work.

The change is one line per test: parse Valve's value, assert **our** constant equals it. Where the
two differ deliberately — the decal bias, a D3D9 value that does not carry to D3D11 — assert the
difference so the divergence is recorded rather than merely absent.

### 4. B136 — the height cut is a depth cut

**Open**, and independent of everything else. The shader clips `SV_POSITION.z`, which is NDC depth,
against a 0..1 fraction. "Depth is height" holds looking straight down through an orthographic
projection and nowhere else, so under the free camera it cuts by distance from the eye. `wpos` is
already in the same shader struct. The second half of that report — overlays surviving the cut — is
filed as **unexplained** and should be measured, not guessed.

---

## Filed, understood, not started

| | |
|---|---|
| **overlay render order** | four layers packed into `m_nFaceCountAndRenderOrder`; parsed and ignored. Matters now that depth writes are off, because overlapping overlays both draw and blend, so order decides the result |
| **overlay fade** | `doverlayfade_t` in `LUMP_OVERLAY_FADES` (60), with `r_overlayfadeenable/min/max`. Lump not read at all |
| **render state per material, the rest of it** | depth state moved onto the material; **blend and rasteriser state are still per pass**. Until they move too, a reordering can still break something |
| **B126** | no reflections under the ortho camera. Moot if D49 lands |
| **D49** | remove the ortho camera, make the overhead view a free-camera placement. Build the placement first so there is a reference to match |

---

## Claims still unsourced

**Settled this session:** `$decal` → `MATERIAL_VAR_DECAL` is bit 16, read out of `materialsystem.dll`
(`docs/findings/18-decals.md`). Four keys, one base, every one on its documented bit.

**Still not sourced:**

- **What `MATERIAL_VAR_DECAL` causes.** Both published reads only set
  `MATERIAL_VAR_NO_DEBUG_OVERRIDE`. The rest is in the surface renderer — a second decompilation
  target.
- **"translucent and additive write no depth."** This project's convention, asserted in
  `WorldRenderer.SetMaterial`. Valve disables writes conditionally per shader path (`bNoWriteZ`), not
  by translucency. Marked as an inference in the code.
- **The overlay fragment builder** (B134). `engine/Overlay.cpp` is unpublished and nothing in
  source-sdk-2013 touches the lump outside vbsp. `COverlayMgr::RenderOverlays` is located at
  `FUN_1010ce60` in `engine-live-x86.dll` if it needs settling.

**The decompiler is set up and the paths are in memory** —
`docs/memory/where-the-game-and-clients-live.md`. One script plus one import answered `$decal` in
twenty minutes, against a project that already existed. It had been carried as an inference for
months for want of trying.

---

## Process corrections from this session, all recorded

These cost real time and are written down because they will otherwise recur.

- **Conformance test first, and the reason is enumeration.** Writing the test forces the engine's
  behaviour to be read across the whole feature; reacting to a screenshot finds one thing at a time.
  B135 was four divergences found one per screenshot across an evening — and two more appeared in the
  minute it took to start writing the conformance test. `docs/memory/conformance-test-before-implementation.md`
- **A conformance test must compare against OURS.** Retesting the unchanging SDK is worthless: Valve
  tested that code. `docs/CONFORMANCE.md`
- **Name the trade before "fixing" Valve's code**, and check whether the trade was against D3D9 or a
  console. `docs/memory/name-the-trade-before-fixing-valve.md`, D46
- **Do not discard work that was asked for, works, and should already have been committed.** The fix
  for "should I revert this?" is usually "this should have been a commit an hour ago".
  `docs/memory/never-revert-without-asking.md`
- **A wrong-looking screenshot is a question, not a verdict** — I misread one and asserted a fix that
  was not there.

---

## What landed

| | |
|---|---|
| `1469815` | B132 — an entity is its baseline plus what the snapshot said |
| `eb76edf` | B131 — a brush entity takes the lightmap vrad baked for it |
| `36f157f` | B134 — clip the face to the overlay, not the overlay to the face |
| `54d715e` | D48 — match the engine's depth buffer format |
| `6ccae07` `4fa81b1` `6000461` | D46/D49 — the Valve-standard reasoning, and the ortho camera's provenance |
| `e7b95cf` | draw static props after the overlays |
| `1a5b0a1` | cull the overlay pass; enumerate the rest of the divergences |
| `8b1897a` | render state per material; a conformance test that can actually fail |
| `253380d` | `$decal` settled from the binary; the conformance audit classified |
