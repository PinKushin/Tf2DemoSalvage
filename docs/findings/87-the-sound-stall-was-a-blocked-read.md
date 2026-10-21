# 87 — the 400 ms sound stall was a blocked file read, and a model's sounds were never precached

**The report** (finding 84, *An oddity, not explained*): on f12, first person, the viewer logged `STALL decoding
'<file>' took ~398 ms` for many different files. `bulletLtoR08.wav` (17,178 frames) took 399 ms and `draw_primary.wav`
(45,824 frames) took 398 ms. A time that does not grow with the work is a wait, not a decode.

## What the 395 ms is (measured)

**The instrument (throwaway, not committed).** Inside `SoundCache.Sample`, timed separately: the file read
(`SoundFile.Open`), the decode, the calling thread's CPU time (`GetThreadTimes`), and the deltas of GC pause and JIT time.
Then a per-source split inside `GameArchives.Read`, and an open/read split inside `VpkArchive.ReadFile`. Every value was
taken where it happened and carried to the line (B243).

**Run 1** (f12 tick 30000, `--measure 60`, 05:42; it began as another agent's UI gate ended):

| sound | frames | total | read | decode | thread CPU | GC | JIT |
|---|---:|---:|---:|---:|---:|---:|---:|
| `weapons/draw_secondary.wav` | 38,016 | 395 ms | 395.4 | 0.0 | 0.0 | 11.2 | 0.0 |
| `weapons/grenade_launcher_drum_start.wav` | 6,896 | 396 | 395.9 | 0.0 | 0.0 | 16.4 | 0.0 |
| `weapons/grenade_launcher_drum_close.wav` | 24,448 | 395 | 395.1 | 0.0 | 0.0 | 13.2 | 0.0 |
| `weapons/stickybomblauncher_boltback.wav` | 13,856 | 168 | 168.4 | 0.0 | 0.0 | 6.0 | 0.0 |
| `>weapons/fx/nearmiss/bulletLtoR08.wav` | 17,178 | 395 | 395.0 | 0.0 | 0.0 | 0.0 | 0.0 |
| `weapons/draw_primary.wav` | 45,824 | 395 | 395.3 | 0.1 | 0.0 | 0.0 | 0.0 |

- **All of it is the read**, and the thread spends it **without running**: CPU 0.0 ms. `GetThreadTimes` ticks at
  15.6 ms, so that means under one tick.
- It is not a decode, a lock, a JIT or a GC.
- **Run 2** narrowed it to one archive source's `ReadFile`: `bulletLtoR13.wav`, 394.3 ms in the archive.
- **The after run** caught the split inside it. On `vo/null.mp3`, `File.OpenRead` was fast and the `ReadExactly` of
  **3,659 bytes** at offset 29,252,882 of `tf2_sound_vo_english_002.vpk` blocked for **168.9 ms**.
- **So the frame waits on a synchronous file read.** That read is on the render thread, because the sound was never
  precached.

**The controls.**

- **Outside the viewer, the same reads are fast.** The `sound-read` probe reads each name twice through the production
  search path. Cold, `draw_primary.wav` read in 4.6 ms (JIT included) and `bulletLtoR08.wav` and `drum_start.wav` in
  0.2 ms. The second reads were 0.2–0.4 ms.
- **Inside the viewer, on a quiet machine, the same misses are fast too.** In run 6 (06:18), every in-play miss was
  logged with no threshold. The same twelve sounds missed as in run 1, and each took **0.2–1.1 ms**.
- **Runs 3, 4 and 5 logged no stall at all**, and that silence is explained. Run 6 shows the misses still happened;
  they were fast.
- **The disk is an NVMe SSD** (`Get-PhysicalDisk`), so the cost is not seek time.

**So two things are multiplied, and they are of different kinds.**

- **The number of misses is deterministic, and it is our bug:** 12 per minute on f12.
- **The cost of each miss is environmental:** 0.2 ms on a quiet machine, and 170–396 ms when run 1 ran, with other
  agents' work on the machine. The same constant, about 398 ms, also shows up outside sound. Two `hud` frames of 397.7
  and 398.4 ms and a 216 ms `draw` frame came in runs with no sound stall at the time, so whatever blocks a first file
  read also blocks other readers.
- **What blocks the read is not identified.** It is *interpolated* as an OS-level wait: it is outside the process,
  it uses no CPU on our thread, and its duration is roughly fixed.
  - *Not checked:* Defender. It is on with real-time protection, but its exclusions need administrator rights to read,
    and its CPU counter returned nothing to a non-elevated read.
  - A test against a fresh 108 MB copy of one archive did not reproduce the wait: 11 ms cold, then 0.7 ms.
- **The fix does not depend on which component it is.** The divergence is a synchronous file read on the render thread
  during play, which the engine does not do.

**A second in-frame cost, measured and separate:** NLayer's first MP3 decode in a process. `vo/null.mp3` decoded in
139.9 ms the first time and 2.9 ms the second (probe). In the viewer, the worker's precache pays it before playback
(0.3 ms in the after run's stall line). Any MP3 missed before any MP3 was precached would pay it in the frame.

## What the engine does (read from published source)

**Corrected after review (below): everything in this section is the SERVER.** The client's
`C_BaseEntity::PrecacheScriptSound` only looks an index up (`SoundEmitterSystem.cpp:1427-1434`). The client receives
these waves, and every other server precache, through the `soundprecache` string table the demo carries.

- **A model's sounds are precached with the model, on the server.**
  - `CBaseEntity::PrecacheModelComponents` (`game/server/baseentity.cpp:5250-5412`, `C_BaseEntity` on the client) takes
    the model's entry from `g_ModelSoundsCache` on PC and calls `PrecacheSoundList()`.
  - `CModelSoundsCache::BuildAnimationEventSoundList` (`game/shared/ModelSoundsCache.cpp:131-218`) builds that entry by
    walking every sequence's events.
  - It adds as a script sound the options of every `CL_EVENT_SOUND`, `AE_CL_PLAYSOUND` (client-typed only),
    `SCRIPT_EVENT_SOUND`, `SCRIPT_EVENT_SOUND_VOICE` and new-system `AE_SV_PLAYSOUND`, plus four footstep scripts per
    `CL_EVENT_FOOTSTEP_*`.
  - The event switch in `PrecacheModelComponents` itself is the X360 path (`if ( IsX360() )`). PC reads the cache.
- **Sounds are precached before play, and a late one is an error.** `CBaseEntity::PrecacheSound` asserts *"too late"*
  outside `IsPrecacheAllowed()` (`SoundEmitterSystem.cpp:1497`). Nothing in the engine decodes a sound on the render
  thread at first play.

**The divergence.** `DemoSounds.Precache` named the timeline's sounds, the client's emitted ones, the surfaces' and the
soundscapes'. It named no model's sounds. A viewmodel's draw and reload are the client's own animation events, so no
demo message names them, and each was read from the VPK inside the frame that first played it.

## The fix

`EntityModelSet.AnimationEvents()` walks every packed model's merged sequence table, as `BuildAnimationEventSoundList`
does. `DemoSounds.ModelWaves` turns the events this viewer plays (`CL_EVENT_SOUND`, 5004, the one `SoundPresenter` fires)
into every wave of each named script. `DemoSounds.Precache` adds those waves after the surfaces' and before the
timeline's, on the load worker, after `DemoModels.Precache` has packed the models. That is the server's order (models,
then their sounds); the client takes the result from the `soundprecache` table, so this is an interim stand-in (review
item 1, below). Since the review it also takes only events the client fires.

**Ported narrowly.** The other cases in the list are HL2's footsteps, NPC script events and new-system server sounds.
Nothing in this viewer fires them, so they precache nothing here, and this one event is the whole port.

The STALL line now says `reading`, and gives read and decode separately. Its old wording ("decoding") is what hid this.

## Before and after

f12 tick 30000, first person, Debug, `--measure 60`, one machine. Each run built and played inside the machine-wide
lock, with load and a process snapshot taken before and after inside it.

| run | build | quiet? | sounds precached | in-play misses | sound frames > 100 ms | all frames > 100 ms after the seek |
|---|---|---|---:|---:|---:|---:|
| 1 (05:42) | before | uncertain: began minutes after another agent's UI gate, no process snapshot taken; load 26% before, 1.5–3% after | 936 | 12 (inferred: same set as run 6) | **6**: 444, 408, 404, 176, 425, 412 | 12 |
| 6 (06:18) | before | quiet (snapshot: steam only) | 936 | **12**, each 0.2–1.1 ms | 0 | 5 |
| after (06:41) | after | quiet at the gate; another agent's gate had just finished | **1,372** | **4** | 1: `vo/null.mp3`, 186 ms | 4 |

- **Misses per minute: 12 → 4.** That count is deterministic and is the fix's effect. Every weapon animation-event
  sound left the list: `draw_primary`, `draw_secondary`, `grenade_launcher_drum_start`/`_open`/`_load`/`_close` and
  `stickybomblauncher_boltback`/`_boltforward`.
- **The precache grows by 436 sounds**, and its first pass took 3,119 ms against 1,859–2,345 ms. That cost is on the
  load worker, behind the B146 barrier, not in a frame.
- **Stalls over 100 ms per minute** depend on the machine. They are 6 → 1 against the busy before run, and 0 → 1 against
  the quiet one. The one left is a remaining miss (below), paid at the environment's price.

## What still misses, and where the engine precaches it (B568)

| sound | path that plays it | in f12's `soundprecache`? (measured, review item 1) |
|---|---|---|
| `player/invulnerable_on.wav`, `_off.wav` | a player condition (Über) | yes |
| `vo/null.mp3` | a voice/response line | yes |
| `>weapons/fx/nearmiss/bulletLtoR*.wav` | a tracer's near-miss whiz | yes, eleven of them |

All four are in the table the client receives. Honouring that table is option (c) below.
Models packed during play (the `STALL reading models` path) would also need their sounds precached at pack time; on
f12 every model is packed by the precache, so this did not arise.

## The review of `50d192b1`, and the table the demo already carries

A neutral review returned five findings. Each was checked against the code and the SDK before anything changed.

| item | finding | verdict | what changed |
|---|---|---|---|
| 1 | the client never walks models; it gets the server's precaches through `soundprecache`, which the demo carries and nothing precaches | **right on the route and the facts; its fix is blocked** (below) | the model walk is kept and labelled an interim stand-in for the server's route (B568) |
| 2 | `PrecacheScriptSound` precaches a raw `.wav`/`.mp3` directly, and `ModelWaves` drops one | **wrong for `ModelWaves`.** The model list goes through `FindOrAddScriptSound` (`ModelSoundsCache.cpp:112-124`), which keeps only a name `GetSoundIndex` finds, so the engine drops a raw wave here too. The raw branch (`SoundEmitterSystem.cpp:397-410`) is for other callers. **Right for play:** `EmitSound` plays a raw name (`:556-559`), and `EntitySounds.Emit` does not | test comment corrected; the play half filed as B569 |
| 3 | `ModelWaves` ignores the event's type | **real** | `ModelWaves` takes only events `StudioEvent.FiresOnTheClient()` passes; the test's server-typed 5004 event was red before the change |
| 4 | the test comment says the engine warns on an empty option | **real, the comment was wrong.** The warning is in the `AE_CL_PLAYSOUND` case; `CL_EVENT_SOUND` and `FindOrAddScriptSound` drop an empty name silently | comment corrected |
| 5 | `SoundReadProbe` reads without the map's pakfile | **real** | the probe's summary now says so |

**Item 1, measured.** A throwaway probe printed f12's `soundprecache` through the production timeline. It holds **6,713
names**, including all twelve first-play misses:

- the eight weapon sounds (`draw_primary`, `draw_secondary`, four `grenade_launcher_drum_*`, two
  `stickybomblauncher_bolt*`);
- `player/invulnerable_on.wav` and `_off.wav`;
- eleven `>weapons/fx/nearmiss/bulletLtoR*.wav`;
- `vo/null.mp3`.

The control, `rocket_shoot`, returned four entries. So the table is the client's list, and it covers B568 as well.

**Precaching the whole table, as the review asked, does not work with this cache.** The table's entries were added ahead
of the demo's sounds, and the result was measured in one run. The lock flagged "4 bash running" during that run, so its
seconds are indicative only. The conclusion rests on the count.

- The first pass decoded 7,673 of 7,691 names in **45.8 s** on the worker.
- The second pass, which runs in `Apply` **on the UI thread**, took **38.8 s**.
- f12 held 277 MB for about 926 sounds, so 7,673 sounds are several times the 512 MB cap. The cache evicted, and the
  second pass decoded again whatever had been let go (B570).

The change was reverted. The engine does not decode the whole table up front either. That the client registers each entry
and loads the data on demand, off the render thread, is *interpolated*: that part of the engine is closed and was not
read.

**The options, with numbers:**

- **(a)** The model walk, kept as an interim. It removes 8 of the 12 misses, adds 436 sounds and costs about 3.1 s on the
  worker.
- **(c)** Make the table the registry: decode a first play off the render thread and start the sound late rather than
  freezing the frame. That is engine-shaped, covers B568, and replaces (a). It needs the engine's sound data-cache path
  read first (Ghidra).

## Evidence classes

- The split table, the controls and the before/after rows are **measured**.
- The engine's precache path is **read from published source**.
- What blocks the read is **interpolated**.
