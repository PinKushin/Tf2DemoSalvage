# Game audio — what a demo asks for, and what the game ships

Written 2026-08-22, at the start of the work. Nothing is played yet; this is what was measured
before writing a decoder, and two of the three measurements changed the plan.

## The decode side was already done

`svc_Sounds` decodes to `DecodedSound`, `SoundNames` maps an index to the precached name, and
`GameArchives.Read` opens any path in the user's VPKs. Every consumer of a decoded sound is a text
writer — trace, JSON, scan — so nothing plays. That is the expected state rather than a defect: the
parser was taken to a byte-identical round trip before the viewer existed, so decoded-and-not-yet-
used is the shape of the whole repository.

## A precached sound name is not always a path

*(evidence class: read from published source, then measured on the corpus)*

`public/soundchars.h` declares ten characters that may lead a name, and `PSkipSoundChars` skips
them before the remainder is opened:

```c
#define CHAR_STREAM        '*'   #define CHAR_USERVOX      '?'
#define CHAR_SENTENCE      '!'   #define CHAR_DRYMIX       '#'
#define CHAR_DOPPLER       '>'   #define CHAR_DIRECTIONAL  '<'
#define CHAR_DISTVARIANT   '^'   #define CHAR_OMNI         '@'
#define CHAR_SPATIALSTEREO ')'   #define CHAR_FAST_PITCH   '}'
```

So `archives.Read("sound/" + name)` returns null for any name carrying one, and the sound is
**silent** — indistinguishable from one not yet implemented, on a feature whose entire output is
sound.

**Measured across the ten committed demos** (`SoundCharProbe`): 34,436 precached names, **1,971 —
5.7% — carrying a prefix**. `)` 1783, `#` 122, `>` 60, `*` 28, `^` 3. About one sound in eighteen.

Two details worth keeping:

- **The characters are instructions, not noise.** `*` streams rather than loading whole, `#` takes
  the sound out of the DSP chain, `)` spatialises a stereo file. Stripping them keeps the path and
  loses the behaviour, which is the half-fix that looks complete.
- **Valve's comment and Valve's code disagree, and the code wins.** The comment says "as one of 1st
  2 chars"; `PSkipSoundChars` loops with no limit. Transcribed from the loop.

## MP3 is the majority format, and measuring one archive says the opposite

*(evidence class: measured on the shipped game)*

`public/tier2/riff.h` publishes the format codes — PCM 1, ADPCM 2, Xbox ADPCM 0x69, XMA 0x165 — and
Valve's own `VDAT` chunk. Which of them matter is a question about the shipped data.

`SoundFormatProbe`, over `tf2_sound_misc` alone — 3,230 entries:

| extension | count |
|---|---:|
| `.wav` | 2,757 |
| `.mp3` | 472 |

Which reads as "WAV is 85% of it, MP3 is a corner". **Over both sound archives — 15,958 entries —
it inverts:**

| extension | count | share |
|---|---:|---:|
| `.mp3` | **13,140** | 82% |
| `.wav` | 2,817 | 18% |
| `.midi` | 1 | |

The difference is `tf2_sound_vo_english`, which is voice lines and is almost entirely MP3. **So an
MP3 decoder is not an optional extra, it is most of the files** — and measuring the archive whose
name sounds general would have got that backwards.

**One qualification, stated so the number is not over-read:** this is a count of FILES, not of
sounds played. A match fires far more weapon and footstep effects than voice lines, so by playback
frequency the WAVs likely dominate. Both are needed; neither is a corner.

Of the WAVs, **2,815 of 2,817 are plain PCM** and two are ADPCM. So the WAV reader is a RIFF walk
and a copy, and ADPCM can be left until something asks for it — provided it is *reported* rather
than silently skipped.

## The mixer is closed, and the string map is most of a specification

*(evidence class: read from a decompilation — partial)*

`snd_dma.cpp` and `SND_Spatialize` are not published. What `engine.dll` gives up cheaply is the
cvar name table, which names the model:

```
snd_front_headphone_position       snd_rear_headphone_position
snd_front_stereo_speaker_position  snd_rear_stereo_speaker_position
snd_front_surround_speaker_position snd_rear_surround_speaker_position
snd_headphone_pan_exponent         snd_headphone_pan_radial_weight
snd_stereo_speaker_pan_exponent    snd_stereo_speaker_pan_radial_weight
snd_surround_speaker_pan_exponent  snd_surround_speaker_pan_radial_weight
snd_rear_speaker_scale             snd_obscured_gain_dB
dsp_dist_min  dsp_dist_max  dsp_mix_min  dsp_mix_max  dsp_db_min  dsp_db_mixdrop
```

So the panning is a **speaker position plus an exponent plus a radial weight, per output
configuration** — headphones, stereo speakers, surround each with their own three. That is a lot of
shape recovered without decompiling a single function.

**What is NOT yet recovered: the function bodies.** The addresses are found — nine use sites in
`.text` — but Ghidra has not defined functions there, so nothing decompiled. Two instrument notes
for whoever continues:

- **Ghidra's reference table is empty for these strings** even though the program is analysed
  (11,170 functions, 11,960 symbols). `getReferenceCountTo` returns 0 for every one. The byte scan
  for the little-endian address constant found all nine, which is exactly what
  `docs/memory/binaries-answer-what-the-sdk-cannot.md` already says to do.
- The next step is defining functions at those sites — the analysis covers 11,170 functions and not
  these — then decompiling.

Related: `docs/DECISIONS.md` D51 for why the mixing is ours and the output device is a sink.

## The WAV reader, and the one file that proved it too strict

*(evidence class: measured on the shipped game)*

`RiffWave` walks the chunks — it does not take the format at offset 20, because Valve ships its own
`VDAT` and `PADD` chunks and a sample rate read out of one of those plays the sound at the wrong
speed rather than failing. Odd-sized chunks carry a pad byte the size does not count, so skipping
exactly `size` lands one byte early on everything after it.

**Then it was run over all 2,757 shipped WAVs, and refused one.**

`sound/player/taunt_eng_swoosh.wav` carries a valid `fmt ` at offset 12 and a valid `data` at 36 —
100,924 bytes of audio, already read correctly — followed by `LIST`, `bext`, and an `FLLR` filler
chunk. After `FLLR` the ids read as `filr`, then `ilrl`, then four zero bytes: an authoring tool's
padding that nothing is meant to walk.

The reader returned null for the whole file. **It threw away audio it had already parsed**, because
of bytes past the end of everything it needed.

The fix is one word — `break` rather than `return null` — and the rule behind it is worth stating:
**a malformed chunk stops the walk; it does not condemn the file.** The engine reads `fmt ` and
`data` and does not care what trails them. A file whose damage lands *before* both chunks still
yields null, which is what the hostile-length test asserts, so the strictness that matters is
retained.

**Found by running against real data, not by thinking about it.** Ten hand-written fixtures all
passed; the file that mattered was the 2,757th real one. That is
`docs/memory/output-level-assertion-or-it-is-not-done.md` and `decode-must-be-total.md` arriving
together — a majority is not the standard, because the engine opens every one of these.

Sample rates across the 2,757: 44,100 for 2,449 of them, 22,050 for 302, 11,025 for four, and a
single file at 48,000. So a mixer has to resample, and the common case is a 44.1 kHz source.

## The attenuation parameters, recovered — the curve itself, not yet

*(evidence class: read from a decompilation, partial)*

The six cvars that parameterise Source's distance attenuation are all present in the live engine and
registered together in one block, `1032c490`–`1032c4dc`:

```
snd_refdist   snd_refdb   snd_foliage_db_loss   snd_gain   snd_gain_max   snd_gain_min
```

Their **defaults** were read straight out of the binary rather than from a decompiled function — the
compiler pools the literals, so each default sits beside its name in `.rdata`:

```
3320972  "36"            <- default
3320976  "snd_refdist"
3320988  "60"            <- default
3320992  "snd_refdb"
3321008  "snd_foliage_db_loss"
```

**`snd_refdist` = 36, `snd_refdb` = 60.** A reference distance and a reference level in dB, which
together are what a soundlevel is measured against.

**The formula is still missing, and this is the honest state of it.** The six use sites are all
inside one contiguous block at `1004249b`–`1004258b`, spaced exactly `0x30` apart — that is the
ConVar *registration*, a static initialiser, not the code that reads them. Ghidra has defined no
function there and the region is not disassembled, so nothing came out.

Two instrument notes for whoever finishes it:

- **Ghidra's reference table is empty for these strings** even though the program is analysed —
  11,170 functions, 11,960 symbols, and `getReferenceCountTo` returns 0 for every one. The byte
  scan for the little-endian address constant found all six, which is what
  `docs/memory/binaries-answer-what-the-sdk-cannot.md` already prescribes.
- The next step is not another scan. It is finding the function that reads the ConVar OBJECTS —
  reached from the object, not from the name string — which means defining functions in that region
  first.

**Do not guess the curve from the parameter names.** Several plausible dB falloff formulas fit
"reference distance 36, reference level 60", they disagree by several dB at ordinary ranges, and a
wrong one is a plausible mix rather than an audible error.

## The MP3s are voice, not music — and the first cut of my own measurement was wrong

*(evidence class: measured on the shipped game)*

13,140 of 15,958 sound files are MP3, and "we need an MP3 decoder" was too coarse to act on. The
owner's question was the right one: *"see what files those mp3s actually are, because we dont need
the main menu music and stuff like that, we only need sounds which you hear in game"*.

| folder | files | size | heard in an ordinary demo? |
|---|---:|---:|---|
| `sound/vo` | **4,447** | 167 MB | **yes** — class responses and announcer |
| `sound/vo/mvm/*` | ~4,041 | 79 MB | only in an MvM demo |
| `sound/vo/compmode` | 926 | 42 MB | competitive mode |
| `sound/vo/halloween_*` | ~1,200 | 94 MB | event maps only |
| `sound/vo/taunts/*` | ~1,300 | 30 MB | **yes** |
| `sound/ambient_mp3/*` | ~250 | 30 MB | event and MvM map ambience |
| `sound/ui/holiday` | 4 | 7 MB | the only UI audio at all |

**There is essentially no menu music to skip.** The split is voice against everything else, and
voice is heard in game — so an MP3 decoder is needed. What the breakdown does say is that roughly
40% of the MP3s (MvM, Halloween, contract VO) belong to content a competitive or pub demo never
touches, and that this is a *runtime* saving rather than a shipping one: nothing is bundled, and a
file is only ever decoded when a demo names it.

**The first run of this probe reported MvM as the largest category, and that was an instrument
bug.** It keyed on the first three path segments, so for `sound/vo/announcer_am_lastmanalive01.mp3`
the third segment is the FILENAME — the largest population split into thousands of singletons and
disappeared from the list entirely, while the deeply-nested MvM folders survived intact and looked
dominant. Keying on the directory inverted the answer.

Same shape as every other instrument fault recorded here: the number was real, and it was a number
about the grouping rather than about the game.

## TF2's MP3s are ordinary, which decides where "control" actually lives

*(evidence class: measured on the shipped game)*

Before choosing between a library and a hand-rolled or OS decoder, the question worth answering is
whether Valve ships anything unusual. `Mp3HeaderProbe`, over 3,000 files from both sound archives:

| | count |
|---|---:|
| MPEG-1 Layer III | 2,991 |
| MPEG-2 Layer III | 9 |
| 44,100 Hz | 2,991 |
| 22,050 Hz | 9 |
| mono | 2,745 |
| stereo | 205 |
| joint stereo | 50 |
| carrying ID3v2 tags | 2,273 |
| **no frame sync found** | **0** |

**Plain, mostly-mono MPEG-1 Layer III at 44.1 kHz with ID3 tags** — files any player opens, authored
with ordinary tools. No custom container, no free-format frames, no odd rates.

**So there is no Valve-shaped behaviour in the MP3 layer to keep control of.** Everything Valve does
that this project cares about happens AFTER decode — attenuation, spatialisation, pitch, DSP, the
soundlevel scale — and all of that is in the mixer, which is ours by D51. The codec's whole job is
bytes to PCM frames, against a standard frozen in 1993.

That also makes a dormant library a smaller risk than it looks: a decoder for a standard that cannot
change is not the same liability as a dormant HTTP client.

**The argument that decides it for this repository is testability.** Every reader here has
byte-level tests over hand-built and real inputs, and `Content.Tests` was just added to CI's Linux
job specifically so those readers are gated. A managed decoder can be tested that way anywhere; an
OS codec reached through COM cannot be tested on the Linux job at all, and would skip exactly where
the gate was just extended to cover.

## NLayer is fast enough, measured — so the C and COM options need not be built

*(evidence class: measured, BenchmarkDotNet ShortRun on this machine)*

The choice was NLayer (managed), Media Foundation through COM (Windows only), or a portable C
decoder such as dr_mp3 behind a C ABI. The latter two are real work, so the cheap move was to
measure the easy option and find out whether the others are worth building.

| clip | decode whole | **first buffer** | allocated (whole) |
|---|---:|---:|---:|
| short voice line | 2.9 ms | **0.37 ms** | 73 KB |
| long voice line | 14.0 ms | **0.33 ms** | 171 KB |
| music track | 137.4 ms | **0.47 ms** | 1,009 KB |

**The first-buffer column is the one that decides it.** Throughput was never in doubt — a match
fires a handful of voice lines a second and no plausible decoder is short of that. The risk was
LATENCY at the moment a sound starts, and it is **~0.4 ms, flat regardless of clip length**. A
512-sample buffer at 44.1 kHz is 11.6 ms of audio, so beginning a sound costs about 3% of one buffer
period. It cannot hitch.

The whole-clip figures are decode-ahead costs paid once, and a voice line is replayed constantly, so
caching removes them entirely after the first play. Music at 137 ms is the largest file in the game
and would stream rather than decode whole — and even taken whole that is a real-time factor in the
thousands.

**So the C and COM alternatives are not worth building.** `CLAUDE.md` requires native code to be
justified by profiling rather than assumption, and the profile says there is nothing to fix. If that
ever changes, dr_mp3 behind a C ABI is a drop-in for the same narrow job — bytes to PCM — which is
exactly why this choice is low-stakes and reversible in a way the mixer's is not.

**Two honest qualifications.** This is a ShortRun of three iterations, and the error on the
whole-clip numbers is wide (the short clip reads 2.9 ms ± 1.3). The first-buffer numbers are tight,
and the margin against the 11.6 ms budget is thirty-fold, so the conclusion does not rest on the
precision. And an earlier claim of mine was wrong and is corrected in D51: COM does not preclude
cross-platform — Media Foundation specifically is Windows-only, but a portable C decoder behind a C
ABI would run anywhere. The testability argument stands only against Media Foundation.

### Correction: the budget was invented, and Valve ships a real one

The paragraph above compared NLayer's first-buffer cost against "a 512-sample buffer at 44.1 kHz is
11.6 ms". **That number was chosen, not sourced** — the owner challenged it immediately: *"how can
we know it will be fast enough without something to compare against? can we find tf2s audio
latency?"*

Yes, and it is in the engine, read the same way `snd_refdist` was — the default string sits
immediately before its cvar name in `.rdata`:

```
3049240  "0.1"
3049244  "snd_mixahead"
```

**`snd_mixahead` = 0.1, so Valve mixes 100 ms ahead of the play cursor.** That is the engine's own
latency budget: the window between a sound being triggered and being heard.

| | |
|---|---|
| Valve's mix-ahead window | **100 ms** |
| NLayer, first buffer | **0.33 – 0.47 ms** |
| margin | **~250x** |

So the conclusion is unchanged and the reasoning behind it is now sourced rather than assumed — and
the margin is twenty times larger than the invented budget suggested, not smaller.

**The method was the problem, not the answer**, which is the part worth keeping. "Fast enough"
against a threshold nobody can cite is the same fault as a conformance test asserting the SDK
against itself: it cannot fail for a reason that concerns the thing being judged. The same
adjacency trick that recovered `snd_refdist 36` and `snd_refdb 60` gave the real figure in about a
minute.

### What `snd_mixahead` actually means, from Valve's own game code

The entry above called it "how far ahead the mixer renders" and treated that as a budget. The owner
asked the right follow-up: *"does valve actually run it or does it work more like a clamp where
audio isnt heard if its over that latency?"* — and the answer is in `source-sdk-2013`, on the GAME
side, describing an engine cvar:

```cpp
// game/server/sceneentity.cpp:57
// Assume sound system is 100 msec lagged (only used if we can't find snd_mixahead cvar!)
#define SOUND_SYSTEM_LATENCY_DEFAULT ( 0.1f )

// :945
float CSceneEntity::GetSoundSystemLatency( void )
{
    if ( m_pcvSndMixahead )
        return m_pcvSndMixahead->GetFloat();

    // Assume 100 msec sound system latency
    return SOUND_SYSTEM_LATENCY_DEFAULT;
}
```

**It is neither a target the mixer chases nor a clamp that drops late audio. It is a fixed pipeline
DELAY that the engine treats as a known constant and schedules around.** The accessor is named
`GetSoundSystemLatency`, and `CSceneEntity` — choreographed scenes, lipsync, captions — reads it to
align facial animation with speech that will not be heard for another 100 ms. Nothing is discarded
for missing a deadline; everything is uniformly late by the same amount.

Two consequences, and the second is a parity requirement rather than a benchmark note:

- **For the decoder**, the reading is confirmed and the margin holds. 100 ms of already-mixed audio
  sits between the mixer and the speaker, so a 0.4 ms first buffer has enormous slack before a stall
  could be audible.
- **For the mixer**, a sound triggered at tick T is heard 100 ms later, and anything that must
  synchronise to audio has to account for it. That is a constant this project will need when the
  mix loop exists, alongside `snd_refdist 36` and `snd_refdb 60`.

**And the source is worth noting as much as the answer.** The mixer is closed, but its cvar's
MEANING is documented in published game code that merely consumes it — the same shape as
`docs/memory/shipped-data-is-a-source.md` and `nothing-is-closed.md`. A grep of the SDK for the
cvar name found it immediately; the decompiler was never needed for this part.

## The audible radius IS published — found by grepping for callers

*(evidence class: read from published source)*

Acting on the lesson from `snd_mixahead`: the mixer is closed, so grep for what CALLS it rather than
for it. `SNDLVL_TO_ATTN` has four callers in the SDK, and one of them is the server deciding who is
sent a sound at all — which means it has to know how far the sound carries.

```cpp
// game/server/recipientfilter.cpp:409
maxAudible = ( 2 * SOUND_NORMAL_CLIP_DIST ) / attenuation;   // const.h:428 — 1000.0f

// :374, and this is the half that inverts easily
if ( attenuation <= 0 )
    return;              // no cropping at all: ATTN_NONE carries everywhere in the PVS
```

**So the audible radius is `2000 / attenuation`.** At `SNDLVL_NORM` (75) the attenuation is 0.8 and
the radius is **2,500 units**; at the clamped end, attenuation 4 gives **500 units**.

Two things worth stating because the intuition runs backwards on both:

- **A low soundlevel gives the SHORTEST radius, not the longest.** `SNDLVL_TO_ATTN` clamps to 4.0 at
  or below 50, and a larger attenuation divides into a smaller radius. Quiet sounds do not carry.
- **Attenuation zero means unbounded, not silent.** Valve's `Filter` returns early rather than
  computing a radius, leaving every recipient in. Reading it as a radius of zero would silence
  precisely the sounds meant to carry everywhere, and it would fail as a plausible mix rather than
  as an error.

**This is a cutoff, not a falloff.** It bounds where sound stops; it says nothing about the gain
curve inside that radius, which is what `snd_refdist` 36 and `snd_refdb` 60 parameterise and which
remains unrecovered. Implemented as `SoundAttenuation` with `SoundAttenuationConformanceTests`
pinning every constant against the SDK — seven tests, two verified by sabotage: reading zero as
silent reddens the ATTN_NONE test, and `>= 50` instead of `> 50` (a divide by zero at the boundary)
reddens two.

## The gain curve: what searching turned up, and why it is not being used

*(evidence class: none yet — this records a dead end, not a result)*

Two routes were tried after the parameters were recovered.

**The web has the formula, in a repository GitHub has legally removed.** A search surfaces it
attributed to `engine/audio/private/snd_dma.cpp` in a mirror of leaked 2007 Source engine code, in
the shape:

```
GAIN = (snd_refdist / dist) * 10 ^ ( ( SNDLVL - snd_refdb - dist * snd_foliage_db_loss / 1200 ) / 20 )
```

Fetching that file returns **HTTP 451 Unavailable For Legal Reasons**. That is a takedown, and
routing around it is not the same act as decompiling a binary the owner has a licence to run — which
this project already does and which `docs/DECISIONS.md` treats as a normal tool. The distinction is
worth stating because the two get conflated: reverse-engineering the shipped binary is the sanctioned
path here, and obtaining source that was removed on legal grounds is not.

**The Valve Developer Community does not document it.** `snd_refdb` and `snd_refdist` do not appear
on the wiki's console-variable pages at all.

**So the formula above is a hypothesis with no citable provenance, and it is NOT implemented.** It is
recorded here only so the next attempt knows what to test against, and because leaving it out would
invite somebody to re-derive the same dead end.

**It is at least consistent with what was recovered independently**, which is worth noting as a
weak check rather than as support:

| | |
|---|---|
| unity gain at | `36 × 10^((75−60)/20)` = **202 units** |
| gain at the published cutoff (2,500) | `(36/2500) × 5.62` = 0.081, about **−22 dB** |

Both are plausible, and consistency is not evidence — many curves through two points would satisfy
it. `docs/memory/fallbacks-do-not-make-guesses-safe.md` applies exactly: a formula that produces
sensible-looking numbers is the failure mode here, not the safeguard.

**The remaining legitimate route is the decompiler, and it needs one more step than the constants
did.** The six cvar name strings lead only to the ConVar REGISTRATION block — six constructors
`0x30` apart in a static initialiser. The code that READS them reaches the ConVar OBJECTS, not the
name strings, so the next attempt is:

1. disassemble the registration block to recover each ConVar object's address (the `this` passed to
   the constructor),
2. scan `.text` for references to those object addresses,
3. define functions at the hits and decompile.

Ghidra has 11,170 functions in this binary and none covering that region, so step 3 means forcing
disassembly rather than relying on the existing analysis.
