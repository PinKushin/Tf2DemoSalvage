# Handoff — the rendering conformance run, and the decode defect it uncovered

Written 2026-08-21. Everything below is committed, pushed and green. B132 was the open item when
this was first written; it was closed the same day and the account of it is kept below, because how
it was found is the more useful half.

The previous handoff, `docs/HANDOFF-viewmodel.md`, covers the session before this one. It is not
superseded — it describes the first-person work and the lighting fixes that came out of it.

---

## B132, closed: the accumulator asked the wrong one of two questions

**Some entities reached the entity table with no properties at all, and nothing noticed.** Found
while implementing fog; it had nothing to do with fog. Full account in
`docs/findings/04-entities.md`; the short version:

`EntityStateTable.Apply` wrote `DecodedEntity.Properties` into the state. That member is
**wire-faithful** — exactly the bits the snapshot carried, which is what the assembler must
reproduce. An entity entering the visible set is a delta against its class's **instance baseline**
and omits everything equal to it, so for state the wire list is the wrong question.
`EntityDecoder.EffectiveProperties` had answered the right one correctly for months, with a comment
saying so; its only caller was the trace writer.

| | before | after |
|---|---|---|
| Properties a `CFogController` holds | 0 | **15** |
| Fog samples per demo (all nine gcor demos) | 0 | **1** |

**Fixed so the wrong call is unreachable** (D47): `IEntityBaselines` is a one-method interface
implemented by `EntityDecoder`, and `EntityStateTable` requires one in its constructor.
`EntityBaselines.None` is for fixtures and says so at the call site.

**Confirmed from outside this project.** Each map's own `env_fog_controller` keyvalues, read out of
the BSP entity lump by `MapFogProbe`, match what the demo networks — granary 225 grey to 14000,
viaduct 213/174/221 to 6500, foundry 131/121/134 from 1707 to 4634. Viaduct is the specimen that
fixes the colour byte order; a grey map cannot.

**The three things worth carrying forward from how it was found:**

1. **The trace and the accumulated table disagreed, from one decoder, on one packet.** That is the
   comparison that localised it. Four hypotheses died before it — including the leading one, a
   swallowed decode exception, which was wrong because there is no `try`/`catch` in the path at all.
2. **The instrument was nearly the suspect again.** The measurement that started it,
   `FogControllerProperties`, was checked for being wrong before the decoder was — correctly, per
   `docs/memory/instrument-bugs-outnumber-decoder-bugs.md`, and this time it was right.
3. **Fixing it surfaced a second thing immediately**, which is the sign a fix was real rather than
   cosmetic: `CWorld` began arriving with a model index and became a prop track covering the whole
   map. Valve excludes entity zero by index — `c_baseentity.cpp:1450` — so that is the rule here now.

A third was filed and withdrawn the same day. `ScenePose.Hidden` looked like it was written and read
by nothing, on a search scoped to the renderer; it is read one layer up, in `DemoTimeline.PropsAt`,
and the owner said so from memory of using the viewer before any code was touched. See B133 — kept
as a retraction rather than deleted, because the way the search went wrong is the reusable part.

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
