# Verification — this project's measurements that are currently true

Empirical facts about **this repository**: the date, the exact command, the exact output. What was
measured, never why a choice was made.

**Not a changelog.** A superseded entry is **replaced in place**, never appended below. If a number
here is no longer true, correct it or delete it — a stale measurement that reads as current is worse
than none, because it is quoted rather than re-run.

This is B1 of `CONVENTIONS-HARVEST.md`. The house-wide file is `PinKushin/VERIFICATION.md`, which
holds facts about the machine and the toolchain and says plainly that *"repo-specific measurements
still belong in that repo"* — this is that half.

## What goes here, and what does NOT

**Which document answers what is one table, in `docs/findings/README.md`** — this file is its
`docs/verification/` row and does not restate the rest. The distinction that matters is against
`docs/findings/`, and it is stated there.

**A measurement is cited from those documents, not copied into them.** Two copies of a number is the
drift the one-owner rule exists to prevent.

---

## Test suites

### The two-phase gate, per assembly

**2026-09-09**, `TF2DEMOSALVAGE_GCOR_ONLY=1 bash build/gate.sh`, then the UI phase separately:

```
core 1803 · cli 74 · logging 17 · fonts 7 · animation 252 · scene 639
audio 183 · presentation 444 · content 1070 · corpus 156 · rendering 769 · viewer 108
```

0 failed in every assembly. The UI suite is **31**, run under `run-exclusive.ps1` because it takes
the desktop:

```
pwsh run-exclusive.ps1 dotnet test tests/Tf2DemoSalvage.Viewer3D.UiTests
Passed!  - Failed: 0, Passed: 31, Skipped: 0, Total: 31
```

**The floors live in `build/gate.sh` and are not restated here** — it prints each beside what it
measured and refuses a drop until the reason is written next to it, which is a stronger guarantee
than a number in a document.

**This total was inline in `CLAUDE.md` and had already drifted.** It read *"roughly 4,690 and 31 as
of 2026-09-03"* against 5,522 and 31 measured here — in the same file that warns, two paragraphs
below, that a per-assembly table *"drifted by about four hundred tests while the warning sat
directly beneath it"*. A count in an always-loaded document is paid for every turn and corrected on
none of them; that is why B1 exists and why the number now lives here with the command that
produces it.

### One `dotnet test` over the solution fails the UI suite

**2026-08-16** (B89). The UI suite passes in **2 seconds** run alone, and failed **one of eight** at
**10 seconds** inside a single-invocation gate, because `dotnet test` runs assemblies concurrently
and the UI suite was competing with roughly **1,700** other tests for one desktop.

### The corpus suite: gcor 28 seconds, lcor about 30 minutes

**2026-08-10.** `TF2DEMOSALVAGE_GCOR_ONLY=1` runs the committed corpus alone — **10 demos, 20.3 MB**
across five measured protocols. The full superset adds lcor, **49 demos and 774 MB** in
`tools/corpus/local/`, which is why it is two orders of magnitude slower.

**`tools/corpus/local/` is not all of lcor.** The real pool is several gigabytes across at least
four locations (the owner, 2026-08-26); 774 MB is the part a test currently sees.

---

## The viewer's frame cost

### Release runs 340–490 fps; Debug is not a measurement of anything

**2026-09-08**, `20130518_0313_cp_granary_blu_blu.dem`, uncapped:

```
TF2VIEW_AUTOPLAY=1 pwsh run-exclusive.ps1 tf2demoview <demo> --tick 5000 --first-person --measure 20 +fps_max 0
```

Release: steady **340–490 fps**, best sample 743, frame ~2 ms — `draw` ~1 ms, `project` 0.5–2 ms,
pose 0.5–2 ms.

**A Debug build measured 150–378 fps with two stalls to 51 and 69**, and those stalls do not exist
in Release. An "the viewer got slow" report taken from a Debug run is measuring the build
configuration. Measure Release, or say which one it was.

---

## Ragdolls and gibs

### A corpse rests above its networked origin

**2026-09-09**, `z1800.dem` and `cp_granary`, corpse root bone against the `m_vecRagdollOrigin` the
wire carries. A correctly resting corpse sits **~40 units above** it, which is the pelvis standing
off the model origin — the control corpse measures +42.

`corpse-drop` settles **4 of 5** seeds; the fifth leaves the world and is B369, not the contact
path.

### What the shipped `.phy` files declare

**2026-09-09**, `dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- ragdoll-constraints`,
over **1108** readable `.phy` files:

```
solid               1108 of 1108   (parsed)
ragdollconstraint     18 of 1108   (parsed)
collisionrules        18 of 1108   (parsed)
break                 20 of 1108   (parsed — the gib list)
animatedfriction       0 of 1108
editparams          1108 of 1108
```

**0 of 882 joint axes declare a nonzero friction**, so vphysics' spring/friction branch never runs
on this game's models.

### A class model declares nine gibs

**2026-09-09**, `… -- ragdoll models/player/medic.mdl`:

```
models/player/medic.mdl: 24 bodies, 23 joints, 92 bones, 9 gibs
    gib models/player/gibs/medicgib001.mdl fades after 10s
```

`medicgib001`–`008` and `random_organ`, each `health 0` and `fadetime 10`. Each gib model is **1
solid, 1 hull**, builds no ragdoll body and does build a prop body — its solid is
`medicgib001_reference` against a single bone named `polymsh`.

### Deaths on the reference demo

**2026-09-09**, `… -- corpses z1800`: **407** `CTFRagdoll` entities, **231 gibbed**, 37 burning,
344 died on the ground, and **3** play a death animation. Most at once: 161, at tick 49549.
