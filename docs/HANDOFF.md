# Handoff — the convar audit, and what comes next

Written 2026-08-27 at the end of a very long session. **Supersedes the previous handoff**, which
covered the `MainForm` thin-view refactor — that work is merged and done.

**Everything is on `main`, pushed, gate green.** Head is `795da1b`. No branch is waiting, nothing is
half-applied, and no viewer process is left running. CI was still pending on that sha at handoff
time: **check it before trusting the tail of this session**, because two earlier pushes today went
red on a test that failed rather than skipped without TF2 installed.

Gate green across twelve assemblies, D1..D108 each used once.

| project | floor | | project | floor |
|---|---:|---|---|---:|
| core | 1504 | | content | 713 |
| cli | 74 | | corpus | 113 |
| audio | 183 | | rendering | 538 |
| presentation | 391 | | viewer | 101 |
| scene | 202 | | logging | 17 |
| animation | 41 | | fonts | 7 |

Plus 20 UI, run separately under `run-exclusive.ps1`.

## The order of work, set by the owner

> *"i think the next step in the naked convars, then the local lights. interp is a much bigger thing
> than you realize i think."*

### 1. The naked convars — D106

**Twenty values are baked as constants that Valve declares as ConVars.** They are listed in
`docs/CVAR-COVERAGE.md` with their flags and defaults. D106 is the rule: *nothing is hardcoded that
Valve does not hardcode*, and the justification is the era axis this project already serves — a
default that moved between TF2 builds is already wrong for part of a corpus spanning 2007 to 2026.

Three homes for a value, and the distinction is the work:

1. **The watcher's config** — 42 names are read this way today.
2. **The demo** — ten of the twenty are replicated (`"rep"`), and `NET_SetConVar` **is already
   decoded and round-trips**. Nothing consumes it. `cl_interp` arrives separately, in `userinfo`.
3. **Valve's declared default** — the fallback, and the form D106 is mostly about. "Declared", not
   baked: a name and a default, not a `const float`.

**Start with `cl_forwardspeed`.** It is flagged `"sv"`, `"cheat"`, `"rep"` despite the `cl_` prefix —
server-controlled and replicated — and the free camera's walk speed is derived from it as a constant
(B215). It is the smallest complete instance of the whole shape: read the replicated value, fall back
to the declared default.

**The one real exemption** is a value Valve itself hardcodes — `MAX_EDICTS`, a struct's field order,
the overbright factor of two. The test is what Valve wrote, never whether the number moves.

### 2. Local lights for models — and B170 with it

B170 (washed-out viewmodels) is **parked on this**, deliberately. The chain is measured end to end in
`docs/RISKS.md`: the ambient cube is healthy (0.2344) and reaches the instance intact, our output is
arithmetically correct for its inputs, and `sun none` means the viewmodel receives **no phong at
all** indoors because our phong is gated on the sun.

Valve sums the specular term over the light cache's `locallight[]` as well. This project folds local
lights into the ambient cube, where they can light a diffuse but can never make a highlight. That the
missing term *is* phong from local lights is **inference from a screenshot and is marked as such** —
three confident conclusions already died in that entry, and the honest next step is the experiment:
drive the viewmodel's phong from something other than the sun and see whether it comes to look like
TF2's.

Measured comparison to work against, same room, `mat_hdr_level 0`: TF2's weapon metal reads 81–108
with specular off; ours reads 11–28.

### 3. Interpolation — bigger than this session understood

> *"interp is a much bigger thing than you realize i think."*

**Recorded as a warning rather than a plan.** This session found the pieces and did not scope the
work: `cl_interp` in `userinfo`, and the server's `sv_client_max_interp_ratio`, `sv_mincmdrate`,
`sv_minupdaterate` clamps in `net_setconvar` — a real match server sends all of them. The naive
reading is "read both halves and interpolate accordingly". The owner's flag says that reading is too
small, and whoever picks this up should get the scope from him before designing anything.

`demo_interpolateview`, `demo_legacy_rollback`, `demo_avellimit` and `demo_interplimit` are in the
same conversation and are unimplemented.

## Deprioritised, with the reason

**D105 — conformance tests in their own project.** The owner: *"im not super worried about the
conformance tests as long as there are plenty of them."* The decision stands as written and is not
urgent. Six suites landed in `Rendering.Tests` today; that is untidy rather than broken, and the
count floors keep it honest either way.

## Landed this session

**Fixed:** B187 (debug views never reached the viewmodel pass — a call with too few arguments, every
remaining one optional) · B219 (surface colours discarded every model's geometry; closed twice, once
by pairing the reset and then by removing the rebuild) · skinned models chose the cubemap nearest the
**map origin**, because their matrix is identity and their placement is in their bones · B220 (the
trace printed `svc_setconvar;` with no payload).

**Built:** `mat_phong`, Valve's convar, importable from a config and on the View menu · the category
view **repaints** instead of rebuilding, so it is instant and B219's class is gone ·
`IModelUpload.HasModels`, replacing a belief the scene held about the device with a question it asks ·
a hard guard on material-buffer width · D104's cvar inventory, with the denominator generated from
the game's own `cvarlist.log`.

**Decisions:** D103 (HDR is roadmap, not a fix) · D104 (the inventory) · D105 (conformance project) ·
D106 (nothing hardcoded that Valve does not) · D107 (LINQ off hot paths) · **D108 — the frame budget
is one millisecond and TF2 meets it**, which is the standard the rest are measured against.

## Things this session got wrong, worth not repeating

**Four B170 hypotheses died, three of them argued confidently.** Every one shared a shape: the test
built the condition where a correct renderer and a broken one agree. Four separate offscreen tests
translated their model through the model matrix, so `matrix[12..14]` was right by construction — and
the bug was precisely that a skinned model's matrix is identity. The instrument that worked was one
`LogInformation` inside the pass already identified as the only untested surface.

**A replace-all matched two of three sites** and shipped a constant buffer four floats short, which
`WriteDiscard` turned into a different picture every frame. `SetMaterial` now throws on a length
mismatch; see `docs/memory/replace-all-is-a-claim-about-every-site.md`.

**A second mechanism was invented where a tested one existed** — an `EntityModelSet.Forget()` with its
own flag and four tests, before noticing `MomentScene.Uploaded` had done that job since B148. Backed
out entirely.

**Two owner corrections are recorded as corrections**, not absorbed silently: D106's justification is
the era axis already in the corpus rather than hypothetical cross-game support, and D107 was written
as an outright ban that also argued against the two-standard position the owner actually holds.

## Standing facts worth carrying

- `bash build/gate.sh` with `TF2DEMOSALVAGE_GCOR_ONLY=1` for the merge gate; the UI suite runs
  separately inside `run-exclusive.ps1`.
- **CI is the machine without TF2** and is the only place the no-install path runs. A test that needs
  the game must `Assert.Ignore`, never assert.
- Both `build/gate.sh` and `.github/workflows/test.yml` carry duplicate exact floors. Move both.
- The corpus has **no mod demos**. A vanilla competitive server already sends 40 convars without
  touching movement; DM and MGE should replay correctly today and jump and surf should not be
  assumed to. One demo of each, in lcor, would make that testable.
- POV demos **do** carry `net_setconvar` (32–40 values on real match demos), so an empty result means
  the server changed nothing.
