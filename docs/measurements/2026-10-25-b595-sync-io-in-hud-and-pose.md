# Are the `hud`/`pose` stalls left after D230 main-thread file reads waiting on a slow disk? (2026-10-25, B595 part 5)

Follows `2026-10-24-d230-async-file-log.md`, whose async run 4 had four frames over 100 ms (430, 407, 174, 409 ms) in
`hud` (3) and `pose` (1), each with 0 ms of log time, beside 10.5 s of flush on the log writer thread.

## Instruments (added on `perf/b595-sync-io-instrument`)

- **Main-thread archive reads.** `GameArchives.Read` times itself and adds the call to a `[ThreadStatic]` total
  (`GameArchives.ThreadReads`: reads, ms, reads over 5 ms). It is the one function every content reader goes through for a
  file that is not in a map's pakfile (VPK entries via `VpkArchive.ReadFile`, which opens the numbered archive per call,
  and loose files). `--measure` differences the main thread's total per slow frame (`archive reads …`) and over the run
  (`main thread archive reads: …`). Not covered: pakfile reads (in memory), and the load-time reads of settings, configs
  and the demo, which are not frame code.
- **Process page faults.** `GetProcessMemoryInfo`'s `PageFaultCount` (every thread, soft and hard together) read at the
  same frame boundary, differenced per slow frame (`page faults N`) and over the run.
- Test: `GameArchivesReadTimingTests` (3 reads counted on this thread, 0 seen on another; changing `Reads + 1` to
  `Reads + 0` reddens it).

## Prediction (written before the runs)

- **Quiet runs (5 × 60 s):** in play, main-thread archive reads are few (the HUD and models are loaded before the
  window), a few dozen reads and under 20 ms a run. Frames over 100 ms: 0-1 a run.
- **Under a background disk load (3 × 60 s):** if the hypothesis holds, frames over 100 ms appear, and each carries
  archive-read time of the same order as the stall (most of it), or a page-fault count in the thousands with no read time
  (a mapped page waiting on the disk).
- **If false:** stalls appear with 0 archive-read ms and a page-fault delta no larger than a normal frame's. Then the
  stall is not this process's disk reads and what is left is named from the phase split.

## Method

f12: `D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 60 +fps_max 0`,
`TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720`, Debug build of this branch, through `run-exclusive.ps1`, each run
preceded by a quiet gate (three CPU samples under 25%). Disk load: a copy of a multi-GB file into the scratchpad, on the
same drive as the viewer's log, started just before the run and running throughout.

**Where the files live (measured, `Get-Partition`):** C: and D: are physical disk 0 (the log under LocalAppData, the
demo, the build); F: is disk 1 (the TF2 install, so every VPK the viewer reads in play). The disk load, a 5 GB copy
`D:\tf2-builds\tf2-2013-1729296.7z` → scratchpad with `xcopy /J`, therefore loads disk 0 only. The only scratch location I
may write is on C:, so disk 1 was not loaded on purpose.

## Results (measured)

### Quiet, 5 × 60 s (quiet gate 1-7% every run)

| run | mean ms | > 50 ms | > 100 ms | main-thread archive reads in play | page faults (process) |
|---|---|---|---|---|---|
| 1 | 9.13 | 3 (658 ms) | 2 | 48 reads, **594.8 ms**, 4 over 5 ms | 144,439 |
| 2 | 9.05 | 0 | 0 | 48, 6.8 ms | 147,869 |
| 3 | 8.91 | 0 | 0 | 48, 7.1 ms | 145,728 |
| 4 | 9.00 | 0 | 0 | 48, 6.8 ms | 150,082 |
| 5 | 8.99 | 0 | 0 | 48, 8.4 ms | 147,258 |

Run 1's slow frames, each with its own window's counters:

| at | frame | largest | hud | pose | archive reads | page faults |
|---|---|---|---|---|---|---|
| 0.224 s | 53 ms | camera | 18.0 | 12.0 | 7, 5.1 ms | 1,401 (start-up frame: 108 methods JIT, particles 15.7) |
| 7.919 s | **371 ms** | hud | **362.2** | 4.3 | **3 reads, 361.1 ms**, 2 over 5 ms | 472 |
| 10.081 s | **233 ms** | hud | **224.8** | 3.7 | **3 reads, 223.9 ms**, 2 over 5 ms | 239 |

**The two `hud` stalls are main-thread archive reads, to within 1 ms.** Their page-fault deltas (239-472) are of the order of a
normal frame's share (about 22 a frame, 144,439 / 6,573), so faults do not carry them. The read time is the whole stall. Run
1 was the first viewer run in about an hour, so its F: reads were cold; runs 2-5 read the same 48 files in 7-8 ms.

### Disk-0 load, 3 × 60 s

| run | mean ms | > 50 ms | > 100 ms | archive reads | page faults |
|---|---|---|---|---|---|
| 1 | 9.11 | 0 | 0 | 48, 7.2 ms | 154,059 |
| 2 | 9.49 | 1 (56 ms: camera, pose 6.7, **0 reads**, log 0) | 0 | 48, 7.0 ms | 144,110 |
| 3 | 9.95 | 0 | 0 | 48, 7.0 ms | 148,734 |

The load slowed the mean (9.0 → 9.5-10.0 ms) but caused no stall: the reads go to disk 1 and were warm. The one 56 ms
frame had 0 reads and 0 log time; it is `camera`, not disk.

### Which reads (measured, one 20 s run with a temporary per-read print, not committed)

On the main thread in play: `materials/HUD/d_images_v2.vmt/.vtf/.vmt` at the first kill drawn with that sheet, the same
for `d_images_v3`, then VO `.mp3`s and weapon scripts (`scripts/tf_weapon_*.txt/.ctx`) at about 0.1-0.3 ms each warm.
**The three-read groups are the stalls' three reads.** Matching on count and phase is inference; the per-read print and the
slow-frame line were not taken in the same run.

## Cause and fix

**Engine (read from source):** `CHud::Init` (`hud.cpp:457-474`) loads `scripts/hud_textures` and `scripts/mod_textures` and
puts each icon through `SetupNewHudTexture` (`hud.cpp:708-733`). For a texture icon that calls `DrawSetTextureFile` and
`DrawGetTextureSize`, so every kill-feed sheet is loaded with the HUD. **Ours:** `HudTextures.SetUp` ported only the font
half. A texture icon's size was first asked at its first draw (`HudTexture.TexCoords`), which made `VguiTextureSizes`
resolve the vmt and vtf on the main thread in the middle of play.

**Fix:** `SetUp` asks `DrawGetTextureSize` for a texture icon at load. Test:
`HudTexturesConformanceTests.Load_TextureIcons_AskEachTextureSizeAtLoad` fails before the fix and passes after; Scene.Tests
3,229/3,229. **Output check (measured, one 20 s run):** `d_images*`/`dneg_images*` and the `hud_obj_*` sheets now read on
the main thread at HUD load. In-play main-thread archive reads went from 45 to 33 over 20 s, and none of them is a HUD
sheet. The cold-disk stall cannot be reproduced on purpose here (no way to drop F:'s cache), so the saving is shown by
where the reads moved, not by a stall that went away.

**Still read in play, on the main thread (filed, not fixed):** VO `.mp3` and weapon `.txt/.ctx` scripts. They are the same
class of read and would stall the same way on a cold disk. The engine precaches the scripts' sounds and reads weapon
scripts at precache (`PrecacheFileWeaponInfoDatabase`); not checked here.

**Not explained by this record:** the D230 run-4 stalls. Their disk (disk 0, the log's) holds no file the frame reads, so
3 `hud` + 1 `pose` stalls during a disk-0 stall are not these reads unless they were also cold F: reads in the same run.
That is unverified. This record's disk-0 load did not reproduce them.
