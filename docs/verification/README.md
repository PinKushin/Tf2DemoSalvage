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

**2026-09-30**, `TF2DEMOSALVAGE_GCOR_ONLY=1 bash build/gate.sh` on 7e1030f1, 657 s, the `.trx` totals:

```
core 2163 · cli 74 · logging 17 · fonts 5 · animation 5335 · scene 2045
audio 207 · presentation 542 · content 1445 · corpus 171 · rendering 860 · viewer 115
```

0 failed in every assembly; 12,979 in all. The UI suite was not re-run that day; on **2026-09-09** it was
**31**, run under `run-exclusive.ps1` because it takes the desktop:

```
pwsh run-exclusive.ps1 dotnet test tests/Tf2DemoSalvage.Viewer3D.UiTests
Passed!  - Failed: 0, Passed: 31, Skipped: 0, Total: 31
```

**The floors live in `build/gate.sh` and are not restated here** — it prints each beside what it
measured and refuses a drop until the reason is written next to it, which is a stronger guarantee
than a number in a document.

**This total was inline in `CLAUDE.md` and had already drifted.** It read *"roughly 4,690 and 31 as
of 2026-09-03"* against 5,522 and 31 measured here on 2026-09-09 — in the same file that warns, two paragraphs
below, that a per-assembly table *"drifted by about four hundred tests while the warning sat
directly beneath it"*. A count in an always-loaded document is paid for every turn and corrected on
none of them; that is why B1 exists and why the number now lives here with the command that
produces it.

### One `dotnet test` over the solution fails the UI suite

**2026-08-16** (B89). The UI suite passes in **2 seconds** run alone, and failed **one of eight** at
**10 seconds** inside a single-invocation gate, because `dotnet test` runs assemblies concurrently
and the UI suite was competing with roughly **1,700** other tests for one desktop.

### The corpus suite: gcor about a minute, the superset fifty

**2026-09-30.** `TF2DEMOSALVAGE_GCOR_ONLY=1` runs the committed corpus alone — **10 demos, 20.3 MB**:
the corpus assembly took **67 s** inside the gate above. The full superset adds lcor, **49 demos and
1.8 GB** in the main checkout's `tools/corpus/local/` (it was 774 MB on 2026-08-10); the corpus assembly
over both, on 7e1030f1:

```
TF2DEMOSALVAGE_GCOR_ONLY=0 dotnet test tests/Tf2DemoSalvage.Corpus.Tests
Failed!  - Failed: 8, Passed: 181, Skipped: 1, Total: 190, Duration: 50 m 5 s
.trx: total 220, executed 189, passed 181, failed 8
host peak (the OS's own counters, sampled every 5 s): working set 14.13 GB, private 14.59 GB
timeline builds (TIMELINE built lines): 92 of 55 demos, 4,056 s, 37 of them rebuilds
```

The eight failures are the filed B440 (five), B441, B442 and B443. **Every other assembly is unchanged
by the switch** — the full gate under `TF2DEMOSALVAGE_GCOR_ONLY=0` on 9eadede9 gave the counts above for
the nine before corpus, and rendering and viewer run on their own under it gave 860 and 115.

**Where the memory went, run by run** (B439): the unbounded cache, B438 — 39 GB private at 43 minutes,
stopped; the bounded cache alone, 9eadede9 — 23.39 GB private, 18.80 GB working set, 50 min 11 s, 97
builds; with the round-trip test and the last builders fixed, above.

**`tools/corpus/local/` is not all of lcor.** The real pool is several gigabytes across at least
four locations (the owner, 2026-08-26); 1.8 GB is the part a test currently sees.

### One demo's timeline: forty to eighty-four times the file

**2026-09-30**, `dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- timeline-heap <demo>`
— the live heap after a full compacting collection — and the wall time of that command, `dotnet run`
included:

```
demostf-cp_snakewater_final1-2026-08-09-0231   97.4 MB   4,692 MB   149 s
demostf-koth_product_final-2026-08-08-2256     82.9 MB   3,995 MB   143 s
etf2l-12030-stv-2020-07-23                     72.1 MB   2,852 MB    97 s
rgl-pug-2026-08-10-pov                         52.2 MB   2,778 MB   119 s
demostf-cp_process_f12-2026-08-08-2207         58.2 MB   2,603 MB    96 s
20150119_2240_cp_process_final_(ovo)_blu       43.0 MB   1,997 MB    80 s
demostf-koth_ashville_final2-1491186           35.5 MB   1,764 MB    59 s
demostf-cp_process_f12-2026-08-07              33.0 MB   2,773 MB   (not timed)
```

z1800, 8.5 MB, holds 635 MB (B433). Forty to eighty-four times the file, so no lcor timeline is small
and the whole local corpus is on the order of 80 GB of them — the numbers `TimelineCache`'s bound
was sized by (B439).

---

## Decode census (D200)

**2026-09-30**, `dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -p:NativeAudioDirectory=<tools/native-audio> -- decode-census <pool…>`,
decoder as of cd6995e1. The pool is `D:/tf2-demo-archive`, the main checkout's lcor and gcor,
`tf2-comp-archive/raw/downloads/DEMO`, `F:/SteamLibrary/.../Team Fortress 2`, `F:/tf2-builds`, `D:/team fortress 2`,
and the demos extracted from the pool's archives.

- **Membership:** 505 `.dem` files; 44 are byte duplicates by SHA-256; 2 are HTML pages named `.dem`
  (`ETF2L Season 30/ree+CoppyZ_airshoted_scout_27200.dem`, `ETF2L Season 32/55000-58500-81500LeonardBroler+PATCHOULI.dem`),
  excluded and listed. **M = 459 distinct demos**, 429 of them outside archives (21,002 MB).
- **Every stage but the timeline: 214 of 459 pass** (198 of the 429). Of the 245 that fail: 65 only on
  `svc_SetPause` (B447), 115 only on a cut tail (B448), 42 on both, 17 on Speex voice (B441), 3 at the
  protocol-15 schema (B440), 2 at the truncated 2007 SourceTV schema (B24), 1 budget skip (B449).
  **The entity stage failed on none.**
- **Wall time:** pass 1, 5 h 40 min over the 429. **Peak working set:** under 250 MB for any demo under 50 MB;
  4,232 MB for the 2 GB prolands container; 9,046 MB for the 1.3 GB koth_product assembly (B449).
- **Controls:** all 8 gcor era specimens pass every stage with non-zero counts; both protocol-15 SourceTV demos
  fail at the schema with B440's message; gullywash fails voice with B441's own count (454 / 440).
- **Timeline stage:** a separate pass, smallest demo first, budget 6 GB. **Partial:** 56 of 505 files reached at
  the time of writing: 36 timelines pass; the 2 B24 SourceTV demos fail at their schema; the rest are
  duplicates or excluded. It runs at 11–30 s per 8–9 MB demo. The whole pool would take over 13 hours.

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

### The ported engine step agrees with the invented environment, to the bit

**2026-09-16**, `… -- ivp-step-compare 6`, one body at `1/66 s` with gravity `−600`, three cases: a free fall, a fall with TF2's
own damping (`0.1` linear, `4` rotational), and the same spinning at `(3, 0, 1)`.

**Every step of all three agrees exactly** — position, velocity and the visible orientation, `dz`, `dvz` and `d|orientation|` all
zero after six steps. The control is in the same table: the ported body's `qx` climbs `0.021 → 0.041 → 0.060`, so the bodies are
really turning rather than both sitting still.

**What that settles, and it narrows B369 usefully**: `IvpEnvironment.Simulate()`'s per-body STEP was never the divergence. What
differs is everything around it — the self-rescheduling PSI event, the unit list and its sleep, the controller priorities, the
impact loop and the contact bookkeeping — which is what `IvpSimulation` now carries (`docs/HANDOFF.md`, item 3).

*It does not compare a collision*: neither side collides in this probe, because the new simulation has no narrow phase wired in.

### Deaths on the reference demo

**2026-09-09**, `… -- corpses z1800`: **407** `CTFRagdoll` entities, **231 gibbed**, 37 burning,
344 died on the ground, and **3** play a death animation. Most at once: 161, at tick 49549.
