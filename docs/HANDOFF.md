# Handoff — the rendering conformance run, and the decode defect it uncovered

Written 2026-08-21 at the end of a long session. Everything below is committed, pushed and green
except B132, which is open and is the most valuable thing here.

The previous handoff, `docs/HANDOFF-viewmodel.md`, covers the session before this one. It is not
superseded — it describes the first-person work and the lighting fixes that came out of it.

---

## Start here: B132

**Some entities reach the entity table with no properties at all, and nothing notices.** Found while
implementing fog; it has nothing to do with fog.

Measured on `tf2-2011-build4604-stv-koth_viaduct.dem`:

| | |
|---|---|
| Entity #212, class `CFogController`, sightings in the entity table | 3,762 packets |
| Properties it holds | **0** |
| Properties its ENTER carries, per a trace of the same file | **15** |
| Entities in that table holding no properties | **19 of 195** |

The trace is unambiguous: `entity 212 ENTER class CFogController(47)` with `m_fog.enable 1`,
`m_fog.end 6500`, `m_fog.colorPrimary 14528213` and eleven more.

### What is ruled out — do not redo this

- **Not the property names.** `EntityFogTests` reads them correctly from values copied out of that
  trace. The qualified keys match what `EntityStateTable.Apply` composes.
- **Not `NetworkedProperties`.** It is an inventory, not a filter — established 2026-08-16 — so it
  gates nothing.
- **Not the class lookup.** The table knows #212 is a `CFogController`, and it can only have learned
  that from the ENTER the decoder read.
- **Not systemic.** 176 of 195 entities hold properties; props, players and brush entities all work.
- **Not a swallowed decode exception.** This was the leading hypothesis and it is wrong: there is no
  `try`/`catch` around `decoder.Decode` in `DemoTimeline`, nor inside `EntityDecoder`. A desync
  throws `InvalidDataException` and would fail the whole timeline build, which does not happen.

### The strongest remaining lead

`EntityStateTable.Apply` replaces the state when an **ENTER arrives with a different serial number**:

```csharp
bool statesSerial = entity.UpdateType == EntityUpdateType.Enter;

if (!_entities.TryGetValue(entity.EntityIndex, out EntityState? state) ||
    (statesSerial && state.SerialNumber != entity.SerialNumber))
{
    state = new EntityState(...);
}
```

A **re-ENTER carrying no property delta** would therefore discard fifteen properties and leave an
empty state with the right class name — which is exactly the observed shape.

**The check that settles it, and it has not been run:** the trace was taken with
`--entity-limit 4000`, which limits *expansion*, so a later re-ENTER would not have printed. Run it
with no limit and look:

```bash
./managed/Tf2DemoSalvage.Cli/bin/Debug/net10.0/tf2demosalvage tools/corpus/demos/tf2-2011-build4604-stv-koth_viaduct.dem -t -e | grep "entity 212 "
```

One line means the state is being lost some other way. More than one means the serial check is
eating it, and the question becomes what the engine intends by a re-ENTER of the same entity — which
is `docs/memory/read-the-encoder-not-the-decoder.md` territory.

### Why it matters beyond fog

Nineteen entities is not a rounding error, and nothing in the project currently asks these entities
for anything, so the loss is invisible. Any future feature reading a non-player, non-prop entity —
the round timer, the objective resource, the fog — hits this first and looks like its own bug.

### What is already built and waiting on it

`SceneFog`, `EntityState.Fog` with five unit tests, `DemoTimeline.FogAt`, the per-change sampling,
and `FogConformanceTests` pinning the arithmetic to the SDK. `FogDecodeTests` asserts the current
zero **deliberately** and says in its own failure message to replace itself rather than relax it when
the number changes.

---

## What landed

| Commit | |
|---|---|
| `d1b9325` | models reflect the map's nearest cubemap, by Valve's `Cubemap_FindClosestCubemap` |
| `334c5f7` | stop attenuating every reflection by a Fresnel term Valve turns off |
| `7e6d658` | the asset log counts local reflections, which nothing reported |
| `dfa83c9` | B126, ortho has no eye |
| `635afc3` | gap markers that outlive their gap now fail instead of skipping (D45) |
| `02def40` | `$normalmapalphaenvmapmask` |
| `83b5ac5` `f18795d` | `$phong` — specified, then implemented |
| `d67e651` | `$rimlight` |
| `a3162b5` | `$lightwarptexture` (D46) |
| `d561b14` | the material constant buffer was two `float4`s shorter than the shader |
| `a09ba7d` | B71 closed — brush entities move, and had for some time |
| `112ed8c` | fog read, and B132 filed |

Gate: **core 1455, cli 68, audio 28, content 603, corpus 90, viewer 541**, plus 12 UI.
`bash build/gate.sh`, then the UI suite inside `run-exclusive.ps1`.

---

## The mechanism worth knowing about: D45

**A conformance gap marker can now fail when its gap closes.** Five were false when this session
looked — cubemaps, `$envmap`, attachments twice, and viewmodels, the last of which had predicted its
own obsolescence in its comment and then skipped through the entire session that built the camera.

`ConformanceGapAuditTests` holds a row per marker with a probe, and **fails** rather than skips. It is
policed in **both directions**: a row naming a *deleted* marker fails too, because otherwise the audit
quietly checks nothing. Its own first version had exactly that defect.

It fired four times for real during the session — on `$normalmapalphaenvmapmask`, `$phong`,
`$rimlight` and `$lightwarptexture` — each time naming the marker to delete, without anyone
remembering to look.

**It also accused a correct entry once.** `MaterialProxies_AreNotEvaluated`'s text already said the
time-driven half worked and named a narrower gap; the probe measured "parsed" and the marker claimed
"evaluated". Renamed, not removed — and the mistake is recorded in the audit, because an audit
measuring the wrong quantity is the defect it exists to catch, one level up.

---

## Traps this session actually hit

**Four times the defect was a test's CONDITION, not its assertion.** This is the single most
repeated lesson here:

- Two sabotages at once cancelled, because a shared input made the broken answer a **tie**. Fix the
  input, not add a third test. → `docs/memory/cancelling-sabotages-mean-coupled-tests.md`
- The normal-map mask test compared the bump alpha's extremes and passed against an inverted mask,
  because moving the texture coordinate moves the **albedo** too and the albedo is the larger term.
- Dropping `$phong`'s N·L mask changed no pixel, and the arithmetic says why: on a quad facing the
  camera the mirrored view vector **equals** the normal, so `dot(R,L) > 0` implies `dot(N,L) > 0` and
  the mask is provably inert. It only bites at a grazing normal.
- That test's own positive control was then wrong as well — a light aimed straight at a grazing
  surface misses R entirely, and both draws came out identical.

**A green suite defended a constant-buffer overrun for months.** `NoDetail` was 40 floats, the
shader's `Material` block 48, and `SetMaterial` copied 192 bytes into a 160-byte buffer. Reflections
drew, and their pixels were measured, asserted on, sabotaged and restored — through a buffer two rows
shorter than the struct being read out of it. `MaterialBufferTests` now counts the `float4` rows in
the shader source and holds the array against them.

**An empty search is a fact about the search.** A grep for `DT_FogController` in the raw demo bytes
said 3 of 10 files had one. Every one does. Caught only because a counter reported 3,807 controllers
in a demo the grep had called empty.

**Two conformance checkers reported real properties as declared nowhere in the SDK.** Their
`SENDINFO` pattern matched identifier characters only, and `SENDINFO_STRUCTELEM( m_fog.start )` sends
under an expression containing a **dot**.

**`MaterialCensusTests` broke four times**, each break caused by the previous fix: its example of an
unimplemented parameter was `$envmap`, then `$phong`, then `$rimlight`, then `$lightwarptexture` —
each chosen as the last one's replacement shortly before being implemented. Its examples are now
picked for needing pipelines that do not exist.

---

## Owner's directions, in their words

- **"do it however valve does"** — settled the model cubemap rule. D44 records that the *rule* is read
  from published source and that applying it to a model at runtime is **interpolated**, because the
  engine's own routine is closed.
- **"we do not hesitate to change our own code to properly match valves"** — D46. Prompted by
  `$lightwarptexture`, which required editing a half-Lambert path that had been correct for a year.
- **"they were suppose to auto start working or you were suppose to keep them updated"** — D45, the
  gap-marker audit. The second half is a discipline and disciplines lapse; the first is a mechanism.
- **"i wasnt looking at it, if you boot if yourself shut it down when your done"**, then clarifying
  that an exit *neither* party asked for is a crash signal, and that silent exits are painful to
  debug.
- **"basically always hedge, id rather understate things than overstate them"** — on writing rules
  down. The memory entry that prompted it had been rewritten three times in an hour, each time
  because it claimed more than was meant.

---

## What is left, by cost

- **B132** above. Highest value: it is a decode defect, it is invisible today, and it blocks fog.
- **B131** — a moving brush entity is ambient-lit against a lightmapped wall. D46 settles the
  direction; the mechanism is a real choice between carrying lightmap coordinates into the entity
  vertex format and drawing brushwork with the world shader. Wants an explicit decision.
- **`$basetexturetransform`** — 24 materials. The shader half is published; the *parse* of
  `"center .5 .5 scale 1 1 rotate 0 translate 0 0"` is in the closed material system, and guessing
  the composition order is exactly the plausible-and-wrong this project avoids. A decompiler
  question.
- **EyeRefract** — 13 materials, needs its own shader.
- **B126** — nothing reflects under the ortho camera, because an orthographic projection has no eye.
  The fix is a constant incident direction; there is no Valve answer to copy, so it would be this
  project's own convention.
- Effects, each a subsystem rather than a parameter: particles, beams, sprites, temp entities,
  runtime decals, dynamic lights, shadows, cloak.
- Deliberately not correctness: visibility, areaportals, LOD.

**The material-parameter conformance work is finished.** What remains is decisions, a decompile, or
new subsystems.
