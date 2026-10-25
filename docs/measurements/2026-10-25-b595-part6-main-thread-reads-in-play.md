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

BEFORE is this branch with the two precache calls absent; both carry the instrument and the VGUI decode share.

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

## Not measured

The coordinator's (a)/(b) (count of `ResolveVguiMaterial` decodes per sheet, the first kill-feed draw's `hud` ms before
and after): the double decode is read from code (no cache between `VguiTextureSizes` and `Device3D`'s resolver;
`VguiRenderer` resolves once per name), fixed by `VguiMaterialLoads`, and not timed.
