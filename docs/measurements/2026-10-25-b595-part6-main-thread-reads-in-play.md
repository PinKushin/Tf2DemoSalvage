# Which files the main thread still reads in play, and where the engine reads them (2026-10-25, B595 part 6)

Follows `2026-10-25-b595-sync-io-in-hud-and-pose.md`, which moved the kill-feed sheets to HUD load and left VO `.mp3`
and `tf_weapon_*` reads in play.

## Instrument

`GameArchives.ThreadReadObserver` (per thread, null by default) is called after each `Read` with the path and ticks.
`--measure` sets it on the main thread when counting begins and prints each read as `read in play: <path> <ms> <- <four
callers outside GameArchives>`. The call site is a `StackTrace` taken inside the read, so it is the code that read, not a
second route. Cost: one stack walk per main-thread read, under `--measure` only.

## Attribution (measured: BEFORE binary, one 60 s run, warm)

36 reads, 5.8 ms, 0 over 5 ms.

| files | count | call site | engine |
|---|---|---|---|
| `sound/vo/*.mp3` (28), `sound/<empty>` + `sound/.mp3` (2) | 30 | `DemoAppearance`'s length reader ← `FaceSources.LengthOf` ← `FaceSources.Voices` ← `EntityModelSet.FaceDriving` | before play: the length is the audio source's, and `S_PrecacheSound` → `S_LoadSound` creates the source at level load (engine.dll FUN_18002cab0, finding 90; that the client calls it per `soundprecache` entry is interpolated there) |
| `scripts/tf_weapon_{pipebomblauncher,grenadelauncher,handgun_scout_secondary}.{txt,ctx}` | 6 | `WeaponScript.Read` ← `WeaponMuzzleFlashes.For` ← `BurstEffects.AddWeaponMuzzleFlashes` | before play: `C_World::Precache` (c_world.cpp:161-176) runs `W_Precache` (the manifest, `PrecacheFileWeaponInfoDatabase`, weapon_parse.cpp:163-194) and then `CPrecacheRegister::Precache`, which `UTIL_PrecacheOther`s every `PRECACHE_WEAPON_REGISTER` class (precache_register.h:15, cdll_util.cpp:474-494); each weapon's `Precache` reads its script (basecombatweapon_shared.cpp:258). All three classes are registered (tf_weapon_grenadelauncher.cpp:73, tf_weapon_pipebomblauncher.cpp:67, tf_weapon_pistol.cpp:247) |

**The manifest is not the list.** TF2 ships `scripts/weapon_manifest.txt` naming only `weapon_ifm_steadycam` (measured,
`game-file` probe, 66 bytes). A manifest-only precache would have read nothing; the registered classes are what the
engine precaches. That set is source, so `WeaponMuzzleFlashes.Precache` uses every `item_class` the schema names
(**interpolated**: an `item_class` is its weapon's entity class). The `sound/` and `sound/.mp3` pair is a sound with an
empty name, read and missed.

## Fix

- `FaceSources.PrecacheLengths`, called in `DemoAppearance` after the appearance is built (load), reads every started
  sound's length. The log line `face sound lengths read before play in N ms` carries its load-time cost.
- `WeaponMuzzleFlashes.Precache`, called by `BurstEffects.Step` when the game changes (the first frame, before the window).
- `TfWeaponData` parses each script once for both `Weapon` and `WeaponHud` (they read it twice).
- Tests: `FaceSourcesLengthPrecacheTests` (no read in play after the precache; answers equal the lazy read's),
  `WeaponMuzzleFlashesConformanceTests.Precache_ThenFor_ReadsNothingInPlayAndAnswersAsTheLazyRead` (TF2's real manifest
  shape), `TfWeaponDataPrecacheConformanceTests`. Each sabotaged (precache skipped or misdirected) and reddened.

## Before / after (measured: 5 interleaved 60 s runs, f12 tick 30000, gummo first person, Debug, quiet gate each run)

BEFORE was built from `6d4a692a` (instrument + VGUI decode share) plus the uncommitted first `TfWeaponData` cache with its
manifest precache call commented out; it has neither `FaceSources.PrecacheLengths` nor `WeaponMuzzleFlashes.Precache`. No
commit reproduces it exactly. AFTER is `a92b2ab2`. **Load-time cost of the face-length precache (measured, viewer log, the
five after runs): 51.5-65.9 ms warm**, on the load thread. The weapon-script precache runs on the main thread in the
first `BurstEffects.Step` (two files per schema item class); its cost and whether that frame is before `--measure`'s
counting were **not measured** — the 0 in-play reads show only that it was not counted.

| run | before mean ms | before reads in play | before > 50 / > 100 | after mean ms | after reads | after > 50 / > 100 |
|---|---|---|---|---|---|---|
| 1 | 10.39 | 36, 6.0 ms | 0 / 0 | 11.41 | **0** | 15 / 0 |
| 2 | 10.50 | 36, 5.4 ms | 2 / 0 | 10.18 | **0** | 2 / 0 |
| 3 | 10.69 | 36, 5.8 ms | 5 / 0 | 10.48 | **0** | 2 / 0 |
| 4 | 9.19 | 36, 5.4 ms | 2 / 0 | 11.56 | **0** | 48 / 0 |
| 5 | 8.92 | 36, 5.6 ms | 1 / 0 | 9.04 | **0** | 0 / 0 |

- **Main-thread reads in play: 36 → 0 in every run.** That is the output-level result; warm, it is 5.4-6.0 ms a run.
  Cold, finding 87 and part 5 measured 50-400 ms per such read; a cold disk was not reproduced (no way to drop F:'s
  cache here, not attempted).
- **The mean does not move beyond run-to-run spread** (before 8.92-10.69, after 9.04-11.56).
- **After runs 1 and 4 had 15 and 48 frames over 50 ms**, all 0 archive reads, mostly `camera` (pose 18-46 ms, wait up
  to 19 ms) clustered at 2-3 s. That is B595's open pose/wait class, not reads. Whether loading more before play
  (heap, LOH) makes it likelier is **not ruled out**; two of five runs is not enough to call it either way.

## Follow-up: where the weapon precache lands, and the slow frames (2026-10-25, 8 interleaved runs)

**Instrument.** `BurstEffects` carries the precache's own stopwatch ticks and end timestamp; `--measure` prints
`weapon scripts precached in N ms, ending T ms from the window's start` (negative = before) and appends `weapon precache N`
to a slow frame it ended in. The window starts at the first frame with the demo on screen, no load in flight, the opening
applied, and the previous frame also on screen (`ViewerSession.Frame.cs`, `onScreen && _wasOnScreen`).

**Measured: the precache takes 26.4-739.1 ms (seven runs 26-61 ms, one 739 ms), ending 10.3-15.5 s BEFORE the window in
all 9 after-runs.** It is never in a counted frame, so it cannot be the after-runs' slow frames.

**Runs.** BEFORE `D:/measure-f12/b595p6-before` (built from `f2c25eb3`, Presentation.dll md5 a37d7515…), AFTER
`D:/measure-f12/b595p6-after` (built from `39acc0ce`, Presentation.dll md5 05d25e8b…, Scene.dll 6d887109…), Debug,
`D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 60 +fps_max 0`, B/A alternating, no other
`tf2demoview` process before any run. Slow-frame times are seconds from the window's start; "early" is < 5 s.

| run | before mean ms | > 50 / > 100 | early / later | reads | after mean ms | > 50 / > 100 | early / later | reads |
|---|---|---|---|---|---|---|---|---|
| 1 | 13.68 | 20 / 5 | 5 / 10 | 36 | 14.11 | 56 / 2 | 16 / 38 | 0 |
| 2 | 26.38 | 176 / 8 | 30 / 138 | 36 | 13.57 | 15 / 1 | 10 / 4 | 0 |
| 3 | 20.65 | 81 / 3 | 14 / 64 | 36 | 11.57 | 15 / 1 | 1 / 13 | 0 |
| 4 | 9.70 | 4 / 1 | 0 / 3 | 36 | 14.85 | 13 / 1 | 6 / 6 | 0 |
| 5 | 21.40 | 125 / 9 | 6 / 110 | 36 | 10.56 | 18 / 2 | 4 / 12 | 0 |
| 6 | 12.93 | 27 / 1 | 1 / 25 | 36 | 9.77 | 3 / 0 | 0 / 3 | 0 |
| 7 | 10.33 | 2 / 0 | 0 / 2 | 36 | 9.23 | 0 / 0 | 0 / 0 | 0 |
| 8 | 9.00 | 1 / 0 | 1 / 0 | 36 | 9.04 | 0 / 0 | 0 / 0 | 0 |

Early / later count the `slow at` lines the run printed, which are fewer than its > 50 total (why it lists fewer was not read).
Ranges: before mean 9.00-26.38, > 50 ms 1-176; after mean 9.04-14.85, > 50 ms 0-56. Listed slow frames are "mostly
camera" on both sides (before 348 of 410, after 104 of 113). The phase split moves with the load, not the build: before run 2's
camera mean is 20.20 ms against 7.09 in run 8; in the quiet pair (runs 7-8) every phase agrees within 0.9 ms.

**Verdict.** The machine was loaded in runs 1-6 by something that is not a viewer (means up to 2.9× the quiet runs', on
both binaries; the process check looks only for `tf2demoview`/`--measure`, so the load is unnamed — interpolated: other
agents' builds or tests). Under that load BOTH builds produce many `camera` (pose/particles) frames over 50 ms, bunched
at the start or not depending on the run. (i) the precache is ruled out by measurement. (iii) is not supported: the
after build has fewer early slow frames than before in 4 of the 6 loaded pairs, and 0 in the quiet pair. (ii) fits: the
known B595 pose/particle stalls, whose count is set by machine contention. The previous 15 and 48 were this.
Not ruled out: a small effect of either sign that machine load of this size hides; only the quiet pair is a clean
comparison, and it is one pair.

## Not measured

The coordinator's (a)/(b) (count of `ResolveVguiMaterial` decodes per sheet, the first kill-feed draw's `hud` ms before
and after): the double decode is read from code (no cache between `VguiTextureSizes` and `Device3D`'s resolver;
`VguiRenderer` resolves once per name), fixed by `VguiMaterialLoads`, and not timed.
