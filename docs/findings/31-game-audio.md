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
