# 90 — a first sound play is loaded off the frame, and a second precache lists nothing twice

**The report** (finding 87, B568): after the model sounds were precached, a handful of sounds on f12 still missed the
precache and were read and decoded on the render thread at first play: the Über `invulnerable_on`/`_off`, `vo/null.mp3`
and a near-miss whiz. On a busy machine one such read blocked for ~170-395 ms (finding 87). And (B570) the second
`DemoSounds.Precache`, on the UI thread in `ViewerPresenter.Apply`, re-decoded whatever the 512 MB cap had evicted.

## What the engine does (read from the shipped engine.dll, Ghidra on D:)

- **`S_PrecacheSound`** (FUN_18002cab0, `snd_dma.cpp:0x4da` by its telemetry label) finds the sfx (FUN_18002b290,
  `S_FindName`) and calls `S_LoadSound(sfx, NULL)` (FUN_18002c350). That creates the sfx's audio source. That the client calls it for each `soundprecache` entry is *interpolated*: the
  string-table side that calls it was not read. Whether the source's sample data is read there or later is not settled by
  this read.
- **The mixer's readiness check** (FUN_18004bc10) reads `snd_async_fullyasync` (registered with default `"0"`, help *"All
  playback is fully async (sound doesn't play until data arrives)."*). When the source is an async-load one (its vtable
  `+0xc0`) or the cvar is set, it answers ready only when the source's cache status (`+0x70`) is loaded; otherwise it asks
  the source to load (`+0x78`) and answers not ready. *Read from disassembly; the slot names are interpolated from the
  shape, not from symbols.*
- **So finding 87's line "Nothing in the engine decodes a sound on the render thread at first play" is too strong.** The
  engine has a blocking path (fully-async off, a source that is not async-load) and a deferred one. Which path TF2's
  stock client takes for these four sounds was not established. **Interpolated:** the deferred path is the engine's own
  answer to the stall this project measured.

## The fix

- **B568.** `SoundCache.OffFrame` is where a play-time miss is loaded; the viewer sets it to the thread pool. A sound not
  held is read and decoded there (only `SoundFile.Open` and the decoder, nothing of the cache), and the result is taken
  in on the owner's thread at the next `Sample`. Until then `Sample` answers null and queues no second load. A load that
  finishes after `Level()` is dropped, because a pakfile can shadow the path (B485). The precache stays on its caller:
  it is outside any frame and must arrive complete. Each arrival logs `loaded off the frame '<name>'` and is counted in
  `SoundCacheHeld.OffFrame`; `Misses` still counts decodes paid on the caller.
- **B570.** `SoundCache.Precache` skips any name an earlier precache of the level listed. The UI-thread pass therefore
  decodes only names it is the first to list (the emitted ones), and can never re-decode what the cap evicted.
- **The `soundprecache` table is not consulted.** The deferral removes the stall for any miss, in the table or not, so
  making the table the registry would change no outcome here. `DemoSounds.ModelWaves` stays: without it eight weapon
  sounds would become silent first plays instead of precached ones.

## The first version dropped the play; review sent it back (`60f4606a` → `a76b5acf`)

**What was wrong.** `60f4606a` answered silence for the deferred play and kept the sample for the next one. On f12
that silenced the first Über on/off and a near-miss whiz each run, and a loop asked for while loading was lost for that
start ("would not open"). The coordinator refused it as a parity divergence: the engine plays the sound late.

**What the engine does with a channel whose data has not arrived** (read from engine.dll):

- The mix list builder (FUN_18003f2c0, called from `MIX_PaintChannels`, `snd_mix.cpp`) asks each active channel's mixer
  through vtable slot `+0x60`. When that answers false, the channel is taken out of THIS pass's list (the `goto` that
  removes the list entry), without `S_FreeChannel` (FUN_1800449d0) and without the `SkipSamples` slot (`+0x10`) that
  `MIX_MixChannelsToPaintbuffer` (FUN_18003f7f0) calls for a quiet channel. The channel stays in the active set.
- **So the sound starts late, from its first sample, on the pass its data arrives**, with the channel's own parameters
  (the volume, pitch and entity `S_StartSound` gave it; position is respatialized every pass anyway). There is no
  timeout in either function. *Read from the decompiler view; slot `+0x60` being `IsReadyToMix` is interpolated from
  its use.*

**The fix.** `SoundPresenter` keeps a sound whose sample is still loading (`SoundCache.Loading`) and starts it again at
the next pass, before that pass's new sounds, until it arrives: from the sample's start, with the `SceneSound` it was
asked for with (volume, pitch, channel, entity; position recomputed against the listener as every start is). A seek
empties the list, as it stops every channel. A loop started while loading becomes a loop once it starts. The
soundscape does not mark a loading loop as started, so its next pass asks again. A load that fails is reported and the
sound is not started (`late sound '<name>' ... did not arrive`). Each late start logs `late start '<name>'`.

## A load that throws, and what runs on two threads (review items 2 and 3)

- **A pool load catches every exception** (`SoundCache.LoadCaught`): on a pool thread nothing is above it, and an
  unhandled one ends the process. It is logged with the sound's name and held as a failed sound, so it is neither
  "loading" forever nor asked for again (`Sample_AnOffFrameLoadThatThrows_IsLoggedByNameAndMarkedFailed`).
- **What a pool load touches**, read in the code: `SoundFile.Open`, `GameArchives.Read`, `VpkArchive.ReadFile` (immutable
  directory, a new `FileStream` per read), `PakFile.ReadFile` (immutable entry table over read-only memory; `Inflate`
  makes new streams per call; `ValveLzma.Decode` has no static fields), and `SoundSampleReader` (the WAV path has no
  static state). **Safe without a lock.**
- **NLayer 2.0.1 is not shown safe.** Reflection over the shipped `NLayer.dll` lists static fields that are not
  read-only: `Huffman._llCache`, `Huffman._llCacheMaxBits`, `FrameBase._totalAllocation`,
  `LayerDecoderBase.DEWINDOW_TABLE`, `SYNTH_COS64_TABLE`, `LayerIIIDecoder.GAIN_TAB` and two `HybridMDCT` tables. Some
  may be set once in a static constructor; that was not read. **So MP3 decodes take one lock** (`SoundSampleReader`'s
  `Mp3Gate`): the load worker's precache and the pool never decode two MP3s at once.
- **The test** `Sample_TwoOffFrameLoadsAtOnce_BothArriveIntact` runs two loads on the real pool, rendezvousing their
  reads on a barrier so they provably overlap, and checks each arrives with its own sample rate. It uses WAVs.
- **The MP3 version, on an authored specimen.** `SyntheticMp3` writes MPEG-1 Layer III frames from the format alone
  (valid headers, zeroed side info and main data; no Valve bytes). NLayer decodes them to the exact rate, channels and
  sample count put there (`SyntheticMp3Tests`); it refuses a single frame, measured, so the builder uses two or more.
  `Sample_SeveralOffFrameMp3LoadsAtOnce_EachArrivesWithItsOwnFrames` overlaps eight MP3 loads on the real pool, each a
  different frame count.
- **That test does NOT prove the lock.** With the lock removed it passed six runs of six. Silent frames have zero
  `part2_3_length`, so they likely never reach the Huffman decode whose cache is the static at risk (*interpolated*). The
  lock rests on the reflection reading above; the test is kept as a regression for the concurrent path.

## Before and after

`docs/measurements/2026-10-21-sound-first-play-off-frame.md`. f12 tick 30000, first person, Debug, `--measure 60`.

| run | decodes in play | STALL lines | loaded off the frame | second precache names |
|---|---:|---:|---:|---:|
| before (`e1ec318a`) | 5 | 1 (`vo/null.mp3`, 171 ms) | — | 1,394 |
| after (`60f4606a`) | **0** | 0 | **5** | **12** |

- **The count moved, it did not vanish**: 5 in play before, 5 off the frame after. The before log names only the one miss
  over the 30 ms threshold, so that the before five are these five is *inferred* (they match finding 87's list). After:
  `invulnerable_off` (read 367.8 ms), `invulnerable_on` (402.2 ms),
  `vo/null.mp3` (1.1 ms), `bulletLtoR08` (111.5 ms), `bulletLtoR11` (1.3 ms). Three of those reads would each have
  frozen a frame; now they wait on a pool thread.
- **After the late start** (`a76b5acf`, `docs/measurements/2026-10-21-sound-late-start-f12.md`, load 16%/10%): 0
  decodes in play, 5 loaded off the frame, and all 3 that were STARTS started late, 1 to 28 ticks after they were asked
  for (`vo/null.mp3`, `bulletLtoR13` after a 398 ms read on the pool, `bulletLtoR08`). None was dropped. The two
  `invulnerable` loads came from a stop or a re-established one-shot, neither of which starts a sound (*inferred*).
- The before run was the busier machine (68 frames over 100 ms against 16), so its frame times are not a comparison.

## Evidence classes

- The engine's precache and readiness paths are **read from the shipped binary** (disassembly and decompiler view in
  Ghidra, nothing carried into the tree but these notes).
- The counts are **measured**; which engine path the stock client takes for these sounds is **interpolated**.
