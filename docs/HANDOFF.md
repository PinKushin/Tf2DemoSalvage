# Handoff — culling, the map checksum, and what comes next

Written 2026-08-28 at the end of a very long session. **Supersedes the previous handoff**, which
covered the convar audit, local lights and the skip helper — all merged and done.

**Everything is on `main`, pushed, gate green.** No branch is waiting, nothing is half-applied, and
no viewer process is left running. Decompiler project and scripts are on `D:`, outside every repo.

| project | floor | | project | floor |
|---|---:|---|---|---:|
| core | 1539 | | content | 744 |
| cli | 74 | | corpus | 129 |
| audio | 183 | | rendering | 656 |
| presentation | 396 | | viewer | 101 |
| scene | 202 | | logging | 17 |
| animation | 41 | | fonts | 7 |

Plus 27 UI, run separately under `run-exclusive.ps1`. `build/gate.sh` holds the authoritative floors
and prints them beside what it measured.

```bash
TF2DEMOSALVAGE_GCOR_ONLY=1 bash build/gate.sh
```

```bash
pwsh ../run-exclusive.ps1 dotnet test tests/Tf2DemoSalvage.Viewer3D.UiTests
```

---

## The next task, set by the owner

> *"i guess we should go 2 pass models now"*

**DONE 2026-08-28 — and the paragraph below was wrong, which is why it is kept.** It read:

> *"This project has no two-pass concept and draws every model once."*

Both halves are false. `Device3D` drew every model **twice**, and `WorldRenderer.DrawModel` filtered
each pass by material — which is `STUDIORENDER_DRAW_OPAQUE_ONLY` / `_TRANSLUCENT_ONLY`, already
implemented. The divergence ran the other way: the renderer split **every** model, where the engine
splits only those declaring `$mostlyopaque` — **88 of 14,109**.

The owner's read on how that happened:

> *"handoff was probably wrong, that previous session didnt really research and look into the 2 pass
> much that im aware"*

**The lesson is that a gap can be filed backwards.** "We do not do X" and "we do X unconditionally"
produce the same next task, and only one of them is a starting point that leads anywhere. The
paragraph was written from the SDK without reading the renderer. See D114 and
`docs/findings/44-what-makes-a-model-two-pass.md`.

### After that, in the owner's stated order of interest

- **Static-prop culling.** The largest remaining performance gap: static props are drawn whole every
  frame, never culled. `BspStaticProps.ReadPayload` already *reads and discards* the per-prop leaf
  array, with a comment saying it "matters for a renderer that culls by PVS and not for one drawing
  the map" — written before we were such a renderer. The door was left open deliberately.
- **Era rendering**, now that the per-era SDK headers are local (below).
- **Ragdolls.** The owner corrected an earlier assessment that this was uncertain: *"the data for the
  ragdolls is available in the sdk, source physics is deterministic so it wont be hard to implement,
  just very large."* Same kind of work as everything else — read Valve, transcribe — only bigger.

---

## What landed today

### Culling, D110–D112

Valve's opaque draw order, view frustum, and world visibility. Details in `docs/findings/42`
(the three-pass Valve audit) and `docs/DECISIONS.md` D110–D112.

- `OpaqueBuckets` — biggest bucket first, thresholds 200/80/30 from `DetectBucketedRenderGroup`.
- `ViewFrustum` — `GeneratePerspectiveFrustum` and `R_CullBox`, six planes, normals inward.
- `WorldVisibility` / `VisibleWorld` / `WorldCulling` — PVS plus per-node and per-leaf frustum,
  front to back, gathered into merged `WorldBatch` runs.
- `ModelInstance.WorldBounds` — the box already placed, computed scene-side, because only the scene
  knows what places a model.

**Four defects shipped and were caught, three of them by the owner looking at the screen.** All four
were the same shape: a correct function called with the wrong argument, at the wrong point, or on the
wrong entity. None was wrong arithmetic. The full list is in `docs/findings/42`; the ones most worth
remembering are that displacements are named by NO leaf (finding 41) and that a skinned model leaves
its matrix at identity.

**Performance:** 274 fps before any culling, 149 with a per-frame recompute, **300 now**. The cull is
recomputed only when the camera actually changes. Watch for this shape: per-frame drawing time was
unchanged throughout, because the cost sat in `SetCamera`, which the drawing timer does not measure.
The UI suite's duration was the only instrument that saw it.

### The map checksum, D113

`BspMapChecksum.Matches(file, recorded)` answers "is this the map this demo was recorded on" for both
eras. Four bytes compare a **complemented** CRC32; sixteen compare an MD5. Callers do not branch.

Confirmed against four era demos and their own clients' maps, a modern demo, and two negative
controls. Full account in `docs/findings/43`.

**This is not yet wired into the viewer.** The pieces exist and are tested; nothing warns at load
yet. That is the remaining half of D113 and it is small: compute, compare, log loudly on a mismatch.

---

## Things that cost hours today — read before repeating them

- **Use `cp_process_f12` for anything the owner will look at.** A badlands demo neither of us knew
  cost most of a night: three defects investigated as regressions, one of which was never code at
  all. `docs/memory/the-f12-demo-is-the-parity-reference.md` now states this as a trigger on
  BOOTING, not on comparing.
- **A demo's map name does not identify the map.** That was the "never code at all" one.
- **When a correct algorithm keeps giving a wrong answer, suspect the input and the identity of what
  you are measuring.** Two faults at once — wrong field and a missing complement — defeated every
  single-variable search for a day. The owner supplied the rule;
  `docs/memory/suspect-the-input-not-the-algorithm.md`.
- **A coverage test can only find what its denominator enumerates.** The ground vanished while the
  suite was green because the denominator was the leaves.
- **Check era stability before assuming an era difference.** `CRC_MapFile` is byte-identical between
  `orangebox` and `source-sdk-2013`; one `cmp` would have saved a day.

## Where things are

| what | where |
|---|---|
| TF2, period clients | `F:\SteamLibrary\...\Team Fortress 2`, `F:\tf2-builds\tf2-{2007,2008,2011,2013}` |
| SDK, 2013 snapshot + **shaders** | `F:\src\source-sdk-2013` |
| SDK, **per-era headers**, 27 branches | `F:\src\hl2sdk` (on `orangebox`; `git checkout <era>`) |
| decompiler project and scripts | `D:\ghidra-proj`, `D:\ghidra_12.1.2_PUBLIC` — never in a repo |

**Ghidra headless scripting is broken on this JDK** — Felix aborts in `handleJavaVersionChange`.
**radare2 works** and did today's decompilation:

```bash
r2 -q -A -c "s 0x100217c0; af; pdf" engine.dll
```

**No SDK ships engine source**, in any branch or year. An engine-behaviour question is a decompiler
question; do not go looking for a fourth SDK.

## Open, not forgotten

- Static props never culled — largest perf gap, and now **next**.
- `m_clrRender` / `m_nRenderFX` / `m_nRenderMode` are not decoded, so nothing can fade — B221.
- Viewmodel two-list ordering — B218, half closed by D114.
- Wiring the map-checksum warning into the viewer.
- Ragdoll bounds and origin — approximated, not matched.
- Detail props — not drawn at all.
- The 32-bit `svc_ServerInfo` field this project calls `MapCrc` is **not** the map checksum and
  remains unidentified. It is `0xFFFFFFFF` in every modern demo.
- Pre-packing period maps (D113 step 2) — less urgent now that the checksum can detect a mismatch,
  and the era specimens already sit beside their own clients.
