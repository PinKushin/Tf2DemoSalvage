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

Fetching that file returns **HTTP 451 Unavailable For Legal Reasons**.

**Whose decision this was, corrected 2026-08-22: the assistant's, taken without asking.** The
original wording here presented "we do not route around the takedown" as a settled position of the
project, which reads as the owner's by default. It was not — he was never consulted, and said so on
being shown this:

> i didnt make that choice not to use the leaked source, you did without asking

Recorded because a decision attributed to the wrong person is worse than an unrecorded one: it
cannot be revisited by the person who would actually have to revisit it, and it borrows authority it
was never given. The same failure mode as the rest of this session's recovery work, arriving from
the opposite direction — there a reason was lost, here one was invented.

The reasoning offered for it stands on its own merits and nothing more: reverse-engineering a binary
the owner is licensed to run is a different act from obtaining source that was removed on legal
grounds, and this project already does the former freely.

**The owner's actual position, given afterwards, makes the question mostly moot:**

> for this i dont think its really super needed to copy valve directly because i dont think anyone
> will be able to tell a small difference in far away sounds

So exact parity on the distance curve is **not a requirement**. That downgrades B142 from a defect
to be corrected into a refinement worth having if the binary is being read anyway.

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

## Soundscripts: 13,052 entries, and 78% of them are one soundlevel

*Evidence class: measured on the shipped install, plus read from published source.*

**A precached name that is not a path is a soundscript key**, and this is the table that resolves
it. `FX_RicochetSound.Ricochet` names an entry in `scripts/game_sounds_weapons.txt` carrying a
channel, a volume, a pitch, a soundlevel, and one or more waves — so the demo says *what happened*
and the script says *what that sounds like*.

**The defaults are published**, in `CSoundParameters`' constructor in
`public/SoundEmitterSystem/isoundemittersystembase.h`:

| field | default |
|---|---|
| `channel` | `CHAN_AUTO` (0) |
| `volume` | `VOL_NORM` (1) |
| `pitch` | `PITCH_NORM` (100) |
| `soundlevel` | `SNDLVL_NORM` (75) |

These matter more than an edge case would, because **most entries state only some fields**. A wrong
default is not a wrong sound in one place; it is a wrong sound across thousands.

**The shipped scripts document their own syntax**, which is where the symbolic values were read from
rather than from a wiki. `game_sounds_weapons.txt` opens with the channel list, the statement that
*"these can be set with `channel` `2` or `channel` `chan_voice`"* — both forms occur, and handling
one silently mis-channels every entry using the other — and the legacy attenuation constants under
Valve's own heading, *"DON'T USE THESE - USE SNDLVL_ INSTEAD!!!"*.

That header also states `ATTN_NORM 0.8f`. **This is an independent confirmation of
`SNDLVL_TO_ATTN(75) = 0.8` from `soundflags.h`** — two shipped sources, one code and one data,
agreeing on a number that was previously known from one. Worth more than either alone, and it is
exactly the *shipped data is a source* point: the answer was sitting in a text file the game reads.

**Measured across the whole install** — all 21 `game_sounds*.txt` files in `tf2_misc_dir.vpk`:

| | |
|---|---|
| scripts | 21 |
| entries | **13,052** |
| entries using `rndwave` | 1,626 |
| entries stating a range for pitch or volume | 343 |

Every one of them parsed, every one carried at least one wave, and every soundlevel landed inside
the declared range. That is the standard `decode-must-be-total` sets and the same one that caught
`taunt_eng_swoosh.wav` in the WAV reader.

**The soundlevel distribution is lopsided in a way worth recording:**

| `SNDLVL` | entries |
|---|---|
| 95 | **10,115** |
| 75 (`SNDLVL_NORM`) | 759 |
| 0 (`SNDLVL_NONE`) | 676 |
| 74 | 330 |
| 80 (`SNDLVL_TALKING`) | 198 |

78% of every entry TF2 ships is `SNDLVL_95dB`, because the bulk of the entries are voice lines and
they are loud. Two consequences: the attenuation curve will be exercised almost entirely at one
input, and **a test asserting only that a soundlevel is "in range" cannot see a total failure of
symbolic resolution** — a collapse to the default puts everything at 75, which is in range. That
weakness was live in the first draft of the conformance suite and was found by sabotage, not by
review; the fix was to predict the shape (95 must outnumber 75 by an order of magnitude) and to
assert exact values against one real shipped entry.

**Two smaller findings:**

- **Ranges are ordinary, not exotic.** `"pitch" "90, 110"` means the engine picks per play, which is
  what stops a repeated sound going mechanical. A reader taking the first number produces a
  plausible sound with no variation — audible only by comparison with the game, and so among the
  hardest defects to notice.
- **Sound characters appear inside soundscripts too.** Shipped entries include
  `"wave" ">weapons/fx/nearmiss/bulletLtoR08.wav"`. Wave names are therefore kept verbatim and split
  at the point of use, so the prefix handling lives in one place rather than two.

## The manifest is not a glob, and the difference is 3,910 entries

*Evidence class: read from published source, then measured on the shipped install.*

**`scripts/game_sounds_manifest.txt` decides which soundscripts exist.** The SDK states the rule
from the other side, identically in `baseentity.h` and `c_baseentity.h`:

> These files need to be listed in scripts/game_sounds_manifest.txt

Loading every `game_sounds*.txt` in the archive is the obvious shortcut and it is wrong. Measured:

| | |
|---|---|
| `game_sounds*.txt` files shipped | 20 |
| files the manifest lists | **16** |
| entries a glob would load | 13,052 |
| entries the manifest actually loads | **9,142** |

**A glob adds 3,910 entries the engine does not have — 30% more than exist.** Every one of them
would resolve to a plausible sound, which is the failure mode that has no symptom.

**Three entries are commented out with `//` in the shipped manifest**, not absent from it:

```
	"preload_file"  	"scripts/game_sounds_player.txt"
	"precache_file"  	"scripts/game_sounds_mvm.txt"
//	"preload_file"  	"scripts/game_sounds_vo_mvm.txt"
//	"preload_file"  	"scripts/game_sounds_vo_mvm_mighty.txt"
//	"precache_file" 	"scripts/mvm_level_sounds.txt"
```

So Valve disabled two MvM voice scripts and left them shipping. A reader that does not handle
KeyValues comments loads them.

**Two keys name a script, not one.** `precache_file` and `preload_file` differ in *when* the engine
pulls samples into memory, not in whether the entries exist. Handling only `precache_file` loses
`game_sounds_player.txt` — the pain and footstep sounds, which is to say most of what a demo plays.

**Two files ship and are never listed at all**: `game_sounds_footsteps.txt` and
`game_sounds_vo_phonemes.txt`. Whether anything else loads them is *not* established here — this
manifest is the one in `tf2_misc_dir.vpk`, and a search path can carry another. Recorded as an open
question rather than as a conclusion: what is measured is that this manifest does not name them.

### The test that passed for the wrong reason

Worth recording because it nearly shipped. `Load_ACommentedOutEntry_IsNotRead` was written in
Valve's exact shape — `//`, then the key, then the path — and it **passed with comment handling
sabotaged**. An unhandled `//` becomes a token itself and shifts the pairing to
`("//", "precache_file")`, leaving the path orphaned in key position, so the script fails to load in
both worlds and the assertion cannot tell them apart.

That is a wrong *condition*, not a weak assertion, and the doctrine's remedy applies exactly: fix
the input. One extra token ahead of the key makes an unhandled comment pair
`("//", "x")` and then `("precache_file", "scripts/disabled.txt")` — which loads it, so correct and
broken now differ. Only then did the sabotage turn the test red.

**The general shape: a test whose fixture mimics real data exactly can be blind precisely because
the real data is well formed.** The distinguishing input was one no shipped file contains.

## Measured: the precache table holds paths, never script names — and old demos point at deleted sounds

*Evidence class: measured, across all ten gcor demos (34,436 precached names).*

Two findings, and the first invalidates an assumption this project's own soundscript reader was
built on.

### 1. Not one precached name is a soundscript key

`SoundScript`'s doc comment stated that a precached name *"may be a path — or a SCRIPT NAME like
`FX_RicochetSound.Ricochet`"*. That was reasoned, not measured. Measured, across every demo in the
committed corpus:

| | |
|---|---|
| precached names | 34,436 |
| resolved through a soundscript | **0** |
| resolved as a raw path | **34,436** |

Zero out of thirty-four thousand is not a corpus gap, it is the answer. **`soundprecache` carries
file paths.** Script names are what *game code* uses — `PrecacheScriptSound`, `EmitSound` — and the
engine resolves them to waves before the table is built.

**And `svc_Sounds` already carries the parameters**, which is the other half of why this matters.
`DecodedSound` has held `Volume`, `SoundLevel`, `Pitch`, `Channel`, `Flags`, the origin and the delay
since it was written. So the playback chain is shorter than the one built for it:

```
svc_Sounds → SoundNumber → soundprecache path → GameArchives → PCM
              └─ volume, soundlevel, pitch, channel all arrive in the message
```

`SoundScriptCatalog` is therefore **not on the critical path for demo playback**. It is not wasted —
it still holds the `rndwave` sets and the parameters for sounds triggered by game code rather than
by `svc_Sounds`, and its manifest work stands — but it was built one layer ahead of the evidence.

**This is the failure `docs/memory/read-the-spec-before-measuring-our-data.md` describes, arriving
from the other direction.** That memory warns against measuring our own data when the question is
what the format does. Here the reverse: an assumption about what the format contains went unmeasured
for two commits while nineteen tests were written on top of it, every one of them green, because
they all asked whether the catalog agreed with its author.

### 2. Two thirds of a 2007 demo's sounds are not in the modern install

Resolving each precached path against a current TF2 install:

| Demo | Protocol | Precached | Cannot be opened |
|---|---|---|---|
| 2007 build 3258 | 11 | 2,230 | **1,476 (66%)** |
| 2008 build 3420 | 14 | 2,231 | 1,476 (66%) |
| 2009 build 3862 | 15 | 2,746 | 1,907 (69%) |
| 2011 build 4604 | 16 | 3,542 | 2,604 (74%) |
| 2013 build 1729296 | 24 | 4,437 | 3,316 (75%) |
| z1800 (modern) | 24 | 6,802 | **24 (0.35%)** |

**z1800 is the control and it is what makes this a finding rather than a bug report.** Same code,
same install, same resolver: 0.35% against 66–75%. The mechanism works; the content is gone.
Examples are unambiguous — `player/pain6.wav` through `player/pain14.wav`, and
`physics/metal/metal_grenade_roll_loop1.wav`. TF2 consolidated its pain sounds and the old files
were removed.

**The consequence for this project's whole premise is direct.** Decoding an old demo is only half of
playing one: the sounds it names no longer ship. A modern install cannot voice a 2007 demo, and no
amount of parser work changes that.

That is not a dead end, because the period clients are already on disk for the protocol dating work
(`docs/memory/where-the-game-and-clients-live.md`) and they carry the period *content* as well as
the period engine. So the search path becomes era-aware: resolve against the install matching the
demo's protocol, and fall back to the modern one. Unmeasured as yet — what is measured is that the
modern install alone is insufficient, by a factor of two thirds.

### Correction: the population above was the wrong one, and the real number is 10–20 per demo

*Evidence class: measured. Supersedes the percentages in the table above.*

**The precache table is the capability; `svc_Sounds` is the output.** A precache table lists
everything the map and the game modes *might* play, so measuring against it answers a question about
the install's completeness rather than about whether a given demo can be voiced. That is exactly the
error `docs/memory/measure-the-output-not-the-capability.md` names, committed here one commit after
citing that memory in a different context.

Measured on what each demo actually **plays**, distinct sound indices reached through `svc_Sounds`:

| Demo | Distinct played | Open | Missing |
|---|---|---|---|
| 2007 build 3258 POV | 22 | 12 | **10** |
| 2007 build 3258 STV | 30 | 19 | 11 |
| 2008 build 3420 POV | 36 | 20 | 16 |
| 2008 build 3420 STV | 45 | 27 | 18 |
| 2009 build 3862 POV | 46 | 26 | 20 |
| 2011 build 4604 POV | 37 | 24 | 13 |
| 2011 build 4604 STV | 43 | 28 | 15 |
| 2013 POV | 27 | 18 | 9 |
| 2013 STV | 76 | 57 | 19 |
| z1800 (modern) | 741 | 660 | **0** |

**Ten to twenty distinct sounds per old demo, not one to three thousand.** The proportion is similar
— a quarter to a half — but the absolute number is what decides the engineering, and it turns a
"ship the 2007 sound content" problem into a table small enough to write by hand.

That matters because **the app cannot go looking for period installs.** Owner, on being asked
whether resolution should become era-aware:

> uhh we cant look for old clients, if we need something from the old clients we have to include it
> in our app itself

Correct, and it should not have needed saying: the period clients on `F:` are research artefacts for
dating protocols. An end user has one modern install. Combined with the standing refusal to bundle
WAVs — *"they are horribly big for what they are"* — the only remaining shape is to ship the
**knowledge** rather than the assets: a mapping from removed paths to their surviving equivalents,
which is a few dozen rows of text.

Unresolved, and named rather than buried: **the probe's own table is incomplete.** It applies
`CreateStringTable` but not `UpdateStringTable`, so indices added by a later update cannot be
resolved and are skipped — 81 of z1800's 741 played indices fall in that hole. The missing counts
above are therefore over the *resolvable* subset. Fixing it needs the table-id-to-name mapping the
trace writer already does, and it can only make the "open" column larger.

### Resolved: they were re-encoded, not deleted. 60 of 63, recovered by an extension fallback

*Evidence class: measured.*

The owner's hypothesis, before any of this was checked:

> i really dont think any sounds have been removed, only moved and renamed

Correct, and the mechanism is narrower than "renamed": **TF2 re-encoded its voice lines from WAV to
MP3**, keeping the stem and the folder. `sound/vo/scout_BattleCry01.wav` ships today as
`sound/vo/scout_BattleCry01.mp3`.

The first search for them missed it by asking the wrong question — it indexed the install by
FILENAME, so `scout_BattleCry01.wav` matched nothing and reported 63 of 63 gone. Indexing by
**stem** instead:

| | |
|---|---|
| distinct played sounds that would not open | 63 |
| present under the same stem as MP3 | **60** |
| absent under any extension | **3** |

The three are `player/pl_fallpain4`, `8` and `10`.

**With a stated-path-first container fallback (`SoundFile`), the corpus resolves essentially
completely:**

| Demo | Played | Open | Missing |
|---|---|---|---|
| 2007 build 3258 POV | 22 | **22** | 0 |
| 2007 build 3258 STV | 30 | **30** | 0 |
| 2008 build 3420 POV | 36 | 34 | 2 |
| 2008 build 3420 STV | 45 | 44 | 1 |
| 2009 build 3862 POV | 46 | **46** | 0 |
| 2011 build 4604 POV / STV | 37 / 43 | **37 / 43** | 0 |
| 2013 POV / STV | 27 / 76 | **27 / 76** | 0 |
| z1800 (modern) | 741 | 660 | 0 |

**Three unopenable sounds across the entire corpus**, all the same fall-damage trio. A 2007 demo is
fully voiced by a 2026 install.

**Stated path first, always.** A file that still exists under its own name is the one the demo
meant, so the fallback runs only on a miss and can never prefer a re-encode over the original. That
ordering has its own test with a both-containers-present control, because without one "fell back
correctly" and "always uses the MP3" are indistinguishable.

**And if the last three ever do get shipped, MP3 is what Valve themselves chose.** Owner:

> tf2 even went mp3 in its later life too so us going to mp3 and converting old sounds wouldnt
> actually be different than valve really

So the size objection to bundling loses its force for a handful of files — though at three sounds,
silence is defensible too, and nothing is being shipped for now.

Still open, and it is the probe's limitation rather than the resolver's: 81 of z1800's 741 played
indices are not in the probe's table at all, because it applies `CreateStringTable` and not
`UpdateStringTable`. Production handles both — `DemoTraceWriter` resolves the table id — so this is
a gap in the measurement, and closing it can only raise the "open" column.

## Every sound every corpus demo plays now decodes, with zero refusals

*Evidence class: measured.*

`SoundSampleReader` gives one decoded type — `SoundSample`, interleaved floats — for both containers,
because the mixer must not care which it got. TF2 ships 82% MP3 and 18% WAV, and the same weapon can
be either across eras.

**The container is sniffed from the bytes, never from the path**, and that is not fastidiousness:
`SoundFile` serves 60 of the corpus's `.wav` names from `.mp3` files, so the extension a demo asked
for says nothing about what actually arrived. Trusting it would hand MP3 bytes to the RIFF walk.

**Measured over the whole corpus, on the sounds demos actually play:**

```
DECODE refusals by reason: none
```

Every sound that opens also decodes — 22 and 30 at 2007, 46 at 2009, 43 at 2011, 76 at 2013, and
660 in z1800, MP3 voice lines included. The counts are identical to the open counts, so nothing was
lost between finding a file and turning it into samples.

### Three details that fail as plausible audio rather than as errors

- **16-bit PCM normalises against 32768, not 32767.** Two's complement runs −32768…32767, so
  dividing by 32767 lets the single most negative sample reach −1.000031 and clip. One value in the
  whole range is affected, which is precisely why it needs a test rather than an ear.
- **8-bit WAV is UNSIGNED and centred on 128**, where every wider depth is signed. Read as signed it
  comes out inverted and offset — a click and a hum, not an exception.
- **ADPCM is refused by name.** Two of TF2's 2,817 WAVs are ADPCM and deferring it was agreed only
  *"provided it is reported rather than silently skipped"*. A bare null would make "not implemented"
  indistinguishable from "corrupt file" and from "nothing was playing", and silence that reports
  nothing is this area's characteristic failure. The probe now counts refusals **by reason** for the
  same purpose: "12 sounds would not decode" cannot say whether that is two ADPCM files or a broken
  MP3 path, and those want different work.

**NLayer needed no fallback.** The benchmark that chose it measured 0.4 ms for a voice line against
a 100 ms `snd_mixahead` budget; against the corpus it also decoded every MP3 without one malformed-
input refusal. The C and COM options costed in `docs/findings/31-game-audio.md` stay unbuilt.

## The gain curve, recovered from the binary — and both errors in ours

**B142 is closed, 2026-08-24.** The section above concluded the curve was ours, flagged it, and
recorded that fetching a leaked-source mirror returned HTTP 451 and was not routed around. None of
that was necessary: the curve is in `engine.dll`, and reading a shipped binary is not that.

`SND_GetGain`, at `101cbb00` in the live x86 `engine.dll`:

```c
relative = distance * attenuation / snd_refdist;   // snd_refdist = 36
gain = relative <= 1 ? snd_gain : snd_gain / relative;
if ( gain < snd_gain_min ) { taper to zero }       // snd_gain_min = 0.01
```

Pure inverse distance past the reference, exactly as the *reference distance* name implied — the
earlier reasoning about that was right. What it got wrong is everything around it.

### Two errors, and the first is the one that mattered

**The attenuation was not in the distance term.** Ours computed `refdist / distance` and left
attenuation out entirely, so every sound in the game fell off at the same rate regardless of its
soundlevel — which is the only thing a soundlevel does. A gunshot at SNDLVL 140 and an idle hum at
60 attenuated identically over distance; only their cutoffs differed.

**The `1 − distance / AudibleRadius` fade was invented, and its radius answers a different
question.** `(2 * SOUND_NORMAL_CLIP_DIST) / attenuation` is from `recipientfilter.cpp` and governs
whether the SERVER SENDS the event. `SND_GetGain` never mentions it; the engine's own silence point
is `snd_gain_min`. For ATTN_NORM that put a hard edge at 2,500 units where the engine's is at 4,500,
and dragged everything inside it down as well.

Measured against a listener 873 units from a machine hum at SNDLVL 75: **0.027 against the engine's
0.0515**, and a different shape everywhere past ~50 units.

### Finding it took three hops, and each dead end reported success

`FindSoundMixer.java` had already located the cvar name strings and printed
`0 functions with callers` — four separate runs, across two sessions. That reads as "not in this
binary" and means nothing of the kind:

1. **A cvar name is a constructor argument.** The only code mentioning `"snd_refdb"` is the static
   initialiser. The code that READS the value never touches the string.
2. **Those initialisers were never disassembled.** Ghidra's reference database has no entry for
   undisassembled bytes, and `getReferencesTo` returns an empty list rather than an error — so the
   query and the answer both looked fine.
3. **A reader loads a FIELD, not the object.** `mov eax, [base + 0x2c]` embeds `base + 0x2c` as its
   constant. Scanning for the object base found exactly two hits in four megabytes — the initialiser
   and one accessor — and none of the readers. Scanning `base + 0..0x60` found twenty-five, of which
   five read all five gain cvars.

The general lesson is `docs/memory/binaries-answer-what-the-sdk-cannot.md` stated from the other
side: **an empty result from a decompiler's database is a fact about the analysis, not about the
binary.** Scan the bytes.

## A soundscape restart reuses the loops that did not change

Reading `UpdateAudioParams` alone gives the wrong answer here, and it is worth recording because the
wrong answer was implemented and shipped an inaudible map.

`UpdateAudioParams` restarts whenever `soundscapeIndex` or `entIndex` changes
(`c_soundscape.cpp`), and `StartNewSoundscape` sets every playing loop's `volumeTarget` to zero. Read
that far and the conclusion is that crossing between two `env_soundscape` entities naming the SAME
soundscape fades the ambience out and back in.

It does not. `AddLoopingSound` reclaims the slot first (`c_soundscape.cpp:1100-1133`):

```c
// NOTE: will reuse existing entry (fade from current volume) if possible
//		this prevents pops
...
// NOTE: Will always restart/crossfade positional sounds
if ( sound.id != m_loopingSoundId && sound.pitch == pitch && !Q_strcasecmp( pSoundName, sound.pWaveName ) )
{
    if ( isAmbient == true && sound.isAmbient == true )   { /* reuse this sound */ }
    else if ( isAmbient == sound.isAmbient )
        if ( VectorsAreEqual( position, sound.position, 0.1f ) ) { /* reuse this sound */ }
}
```

So an **unpositioned** loop survives an entity change unconditionally, keeping its current volume; a
**positioned** one survives only where its target agrees within 0.1 units. Matched on wave and
pitch — never on volume, which is written to `volumeTarget` and faded to.

**The symptom of getting this wrong was silence, not a pop.** cp_process has 21 entities naming
`Gorge.Outside`, and the viewer's selection crossed between them every few hundred milliseconds
against a three-second fade, so the outdoor wind and birds never rose above about a fifth of their
volume — while the log showed the correct soundscape being chosen the entire time. Measured on the
running viewer: 90 changes in 3m49s, with pairs alternating at the 250 ms selection interval.

Two faults fed it, and only one is fixed:

- **The hysteresis was dead.** `Choose` reassigned its running `chosen` during the walk, so the
  branch testing "is this the current one" compared against a contender instead, and the current
  placement's own range was never established. Selection degenerated to bare nearest-visible with
  nothing resisting a flip. The engine measures the current FIRST and then skips it in the loop
  (`soundscape_system.cpp:339-362`), seeding `currentDistance = 0` and `bInRange = false`.
- **The PVS restriction is still missing.** Only soundscapes in the listener's own visibility
  cluster contend in the engine (`m_soundscapesInCluster`). This project reads no visibility lump —
  `BspLumpIndex.Visibility` is defined and unused — so all 44 contend and a placement across the map
  can win on a long clear traceline.

**Valve hit the same wall in the same order**, and left the evidence in a comment four lines below
the reuse check: fading one positional sound out while fading another in sends alternating commands
naming the same sound, *"this will occasionally cause the sound to vanish entirely"*, so they stop
the old one immediately. Pops, then a crossfade, then the crossfade interfering with itself.
