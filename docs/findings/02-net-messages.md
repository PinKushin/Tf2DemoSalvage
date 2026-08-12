# 02 — The network message layer

Inside `dem_packet` is a bit stream of network messages: a type field, then a body whose shape
depends entirely on the type. For the current description of each message see `docs/SPEC.md`
Layer 2. This file records the structural facts that shaped how the layer had to be implemented.

## There are no length prefixes, so this is a dependency chain

**The defining property of the layer, and the one that dictates the whole order of work.** Most
messages do not state their size. A decoder that does not know a message's layout cannot skip it —
it cannot find where the next one starts.

So implementing messages in order of usefulness is impossible. **You implement whatever is blocking
the stream**, however uninteresting, or you decode nothing after it. A single unimplemented message
type truncates every packet that contains one, and the damage is silent: the packet simply appears
to end early.

That produced a distinctive progress curve. Continuous entity decoding went 0 → 62 → 205 → 332 →
complete, and **every jump came from implementing another message type that had been truncating its
packet**, not from any change to the entity decoder itself. If a decoder looks broken, check first
whether the data is even arriving.

## The type field width changes between protocol 15 and 16

Five bits at 15 and below, six above. Measured on both sides: the 2009 demo (protocol 15) and the
2011 demo (protocol 16) each decode end to end only with their own width.

**The failure mode is loud, which is the only reason guessing was tolerable before those specimens
arrived.** Reading six bits where five were written desynchronises the first message of the signon:
before the fix the 2009 demo produced 11,002 unreadable packets and a server protocol of 25,482.
There is no reading of a wrong width here that quietly produces plausible output.

This was originally an interpolation — the flip was known only to be somewhere in 16–23, and 15 was
chosen because 16 is where Replay shipped and a protocol number only moves when the wire format
does. The reasoning was right; it is now evidence.

## Widths are derived from counts, not written down

Several fields are sized by something the stream established earlier:

| Field | Width |
|---|---|
| entity class id | `floor(log2(classCount)) + 1` |
| string table index | `floor(log2(maxEntries))` |
| string table entry count | `floor(log2(maxEntries)) + 1` |

The `+1` differences are not decorative — they are the difference between a stream that decodes and
one that does not, and they are easy to get wrong in a way that only shows up several messages
later. Centralising them in one place (`WireWidths`) rather than recomputing at each call site is
the single change that made this tractable.

This is also why `svc_ServerInfo` must be decoded before entities: `MaxClasses` determines the
class id width, so entity decoding cannot begin without it.

## `svc_UserMessage` is the game's extension point

The engine carries a type byte, a length, and an opaque body; the *game DLL* decides what any of it
means, and nothing on the wire names the message. That makes it the layer most likely to produce
confident nonsense, and it gets its own chapter: [05-user-messages.md](05-user-messages.md).

The length is stated **in bits and is exact**, not padded — bodies of 77 and 113 bits occur. That
single fact is what makes a guessed layout testable at all.

## `svc_EntityMessage` is not generically decodable

Same shape as a user message, but the body's meaning is determined by the **receiving entity's
class**. There is no table to transcribe and no generic layout to infer; decoding it would mean
implementing per-class handlers for every entity type that sends one. It is carried faithfully and
reported by length, which is the honest limit.

## Round trip status

Every message type this project decodes also re-encodes, and the whole message stream round-trips
byte-identically — 87,733 messages, 100% of message bits. See
[07-writing-demos.md](07-writing-demos.md) for what that does and does not prove, and for the
"record the encoding shape" pattern that made it possible.

## Voice: three codecs in nineteen years, and the era is declared in the demo

Measured 2026-08-11 from `svc_VoiceInit` across the corpus. The message names its codec as a
string, so every demo states its own audio era without inference:

| era | `svc_VoiceInit` | voice packets seen |
|---|---|---|
| 2007 – 2013 | `codec "vaudio_speex" quality 5` | 125 in the 2007 POV/STV pair |
| 2020 (`z1800.dem`) | `codec "vaudio_celt" quality 22050` | **806** |
| 2026 | `codec "steam" quality 0` | none in the two POVs held |

**The quality field changes meaning with the codec**, which is why `svc_VoiceInit` already has a
conditional read: 5 is a Speex quality setting, 22050 is a sample rate, and 0 is neither — under
`steam` the engine defers entirely to Steam's own voice API and the field carries nothing.

**What is already decoded, and it is more than it looks.** `svc_VoiceData` gives the speaking
client, a proximity flag, and the payload length, at a tick:

```
svc_voicedata client 20 proximity 0 bits 512;
```

That is everything a viewer needs to show *who is talking and when* — a speaking indicator, a
comms timeline, or which players were coordinating during a push. **None of it requires decoding
a single audio sample.** The distinction matters because "voice is opaque" reads as though nothing
about voice is available, and the useful half already is.

**What remains is the audio, and that is a dependency question rather than a format one.** The
payload is a codec bitstream, not a Valve wire format, so there is nothing here to reverse
engineer — the work is choosing and vendoring a decoder:

- `steam` is Opus, and pure-C# Opus decoders exist.
- `vaudio_celt` is raw CELT 0.11 frames, **not** Opus packets, so an Opus decoder does not read
  them despite Opus containing a CELT layer. This is the era with the most captured voice.
- `vaudio_speex` is Speex, a third decoder again.

Three codecs, three dependencies, and the era with the most data is the awkward one. That trade
belongs to whoever wants the audio; it is recorded here so the choice is made with the counts in
front of it rather than after picking a library.

### The codec switches are dated, and the changelog gave both ends

The corpus brackets them — Speex measured through March 2013, CELT observed in 2020 — and TF2's
patch notes close the gap:

| date | change | grade |
|---|---|---|
| **18 November 2016** | CELT added as opt-in beta: `sv_voicecodec vaudio_celt` with `sv_use_steam_voice 0` | **Sourced** |
| **21 December 2016** (Smissmas) | CELT becomes the **default** for all game servers | **Sourced** |

**Both are announcements, not repairs, so the dates are exact rather than ceilings** — see
[06](06-protocol-eras.md) for why that distinction decides the grade. The build shipping *is* the
event.

Two things fall out that the corpus alone could not say. The switch was **staged** — a month of
opt-in before the default moved — so demos recorded between those dates may carry either codec
depending on the server, and `svc_VoiceInit` is the only thing that says which. And
`sv_use_steam_voice` **already existed in 2016**, so Steam voice was an alternative years before
our 2026 demos show it as the default; that later default change is still unbracketed.

**This also settles which era matters.** Speex covers 2007–2016, CELT 2016 to somewhere before
2026, and `steam` after. A parser that wanted audio from the competitive archive on demos.tf would
need **CELT**, because that is what the bulk of recorded, downloadable TF2 voice is encoded in —
which is the awkward answer, since CELT is the one an Opus decoder cannot read.

## The voice payload framing, measured (2026-08-11)

`svc_VoiceData`'s body was carried whole and called opaque. It is not opaque — only its innermost
layer is, and that layer is what a codec library reads. Everything above it is framing this project
can resolve, and now has.

Established by **exact consumption**: parse the whole payload, require the parser to land precisely
on the end, and count. A model that is nearly right scores zero, not "most", which is what makes
the number worth reporting.

### `steam` — Opus, and fully framed

```
u64  steamID64                      the speaker, independent of the client slot
repeat until 4 bytes remain:
  u8 type
    0x0B  u16 sample rate           always 24000 across the corpus
    0x00  u16                       55 occurrences, all in 18-byte packets
    0x06  u16 length, then <length> bytes of Opus
u32  tail
```

*Measured*: **1452 of 1452 packets consume exactly.** The route there is worth keeping, because the
first two attempts scored **0**:

1. Parsing to the end of the payload — 0 exact, and a type histogram containing *every* byte value
   from `0x00` to `0xFF`. That histogram is the tell: a desynchronised parser walking noise. The
   only real signal in it was `0x0B` appearing 1458 times against 1452 packets.
2. Arithmetic settled it without further guessing. Three packets, declared type-`0x06` length
   against bytes remaining: 543/525/529, 309/291/295, 273/255/259 — **slack of exactly 4 every
   time.** A four-byte tail, not a parse error. With that, 1397 of 1452.
3. The remaining 55 were the 18-byte packets, which carry type `0x00` instead of `0x06`:
   8 + 3 + 1 + *2* + 4 = 18 gives the field width without needing to know the meaning. 1452 of 1452.

**The steamID is the interesting field.** `svc_VoiceData` already gives a client slot; this gives
the account. A slot is only meaningful against the roster at that moment, and it is reused when
players leave — the steamID is not.

### Inside type `0x06`

```
repeat: u16 chunk length, u16 sequence, <chunk length> bytes
```

*Measured*: **1334 of 1397** blocks consume exactly. Chunk sizes cluster at 78–86 bytes; sequence
numbers run 0–164.

**All 1397 do, once the terminator is known** — and the route there is worth keeping because the
hypothesis written down first was wrong.

That hypothesis was that the 147 one-byte chunks were a marker with a different shape. They are
not: every one carries the payload `0x68`, which is a valid Opus TOC byte and the same one leading
the 78-86 byte chunks. They are ordinary minimal Opus packets.

Dumping the 63 failing blocks answered it in one pass. **Every one ended with exactly 2 bytes
remaining, and those two bytes were `FFFF`.** A block may end with a `0xFFFF` sentinel read through
the chunk-length field — the length field alone, with no sequence number and no data behind it.

```
repeat: u16 length, u16 sequence, <length> bytes
        a length of 0xFFFF ends the block instead
```

Both wrong readings fail badly and differently, which is why guessing was not an option: taking
`0xFFFF` as a length asks for 65535 bytes that are not there, and taking the block as malformed
discards audio that is perfectly well formed.

*Measured* with the sentinel handled: **1452 of 1452 payloads and all 3969 chunks consume
exactly**, and exactly 63 report the terminator — the same 63 that used to fail. 14 distinct
speakers across the corpus, every speaking client slot mapping to exactly one Steam account.

### `vaudio_celt` and `vaudio_speex` — no framing at all

Both are bare concatenated codec frames, which the length histograms show without any parsing:

| codec | packet lengths observed | implied frame |
|---|---|---|
| `vaudio_celt` (z1800) | 64, 128, 192 | **64 bytes** |
| `vaudio_speex` (2007 STV) | 28, 56, 84, 168 | **28 bytes** |

Every length is an exact multiple of one number, which is what "no header" looks like. CELT's first
byte is constant at `0x18` across all 806 packets while bytes 1–3 vary in 802 of them — consistent
with a fixed codec mode rather than with a packet header.

A 28-byte Speex frame is 224 bits, which is Speex narrowband quality 5 (220 bits, byte-aligned) —
and `svc_VoiceInit` independently reports quality 5 for every pre-2016 demo in the corpus. Two
unrelated routes to the same parameter, in the sense of
[01](01-container.md)'s view-angle cross-check.

## Wiring the three voice codecs: what worked, and what CELT still refuses (2026-08-11)

The framing above says where each codec's bytes are. This section is what happened when actual
decoders were pointed at them — two clean successes and one honest failure, kept here because the
failure's history is the more useful half.

### Opus (`steam`) — decodes completely

`libopus` from NuGet (MIT, prebuilt per-RID; no build step). All **3969** Opus chunks the corpus
carries decode without a single error: 14 distinct speakers, 85 real speaker interleavings,
1,905,120 samples — about 79 seconds of recovered speech, the first audio this project ever
produced from a demo.

**One decoder per speaker, keyed on the steamID**, not one shared and not one per packet. Opus is
delta-coded against its own running state, so a shared decoder desynchronises the moment two
speakers' packets interleave — which the corpus measurably does.

**A trap worth recording, found on the wrapper's first test run rather than by reasoning.** `fixed`
over an *empty* `ReadOnlySpan<byte>` yields a **null pointer**, and `opus_decode` reads a null data
pointer as *"this packet was lost, conceal it"* — `lost_flag = data == NULL`, not `len == 0`, per
libopus's own `opus_decoder.c`. So `Decode([])` would have silently returned plausible-sounding
concealment audio instead of rejecting a malformed frame. That is a worse failure than a crash: it
looks like real decoded speech. The wrapper now refuses an empty frame and points callers at its
explicit `ConcealLoss` entry point.

### Speex (`vaudio_speex`) — decodes completely

Built from Xiph source (Speex 1.2.1) by `tools/native-audio/build.ps1`. Speex ships a real Windows
build path — `win32/config.h` and `win32/libspeex.def`, used verbatim — so nothing here was
hand-derived. All **272** narrowband frames in the 2007 SourceTV demo decode, zero errors, zero
silence.

**The latest release is correct here, unlike CELT**: Speex's bitstream has been stable across the
whole 1.2.x line, so 1.2.1 decodes 2007-era frames without needing a period-matched version.

### CELT (`vaudio_celt`) — builds, initialises, and rejects most real frames

This one is unresolved, and the trail matters more than the conclusion.

**Getting a library at all took an exact version.** CELT's bitstream was never guaranteed stable
between releases — which is *why* it was folded into Opus rather than maintained standalone — so
"the latest CELT" does not exist as a thing to fetch and would not decode TF2's frames if it did.
The pin is **0.11.3**, the last of the 0.11.x line, from `Distrotech/celt`, confirmed via the
GitHub API to be a mirror of the now-gone `git://git.xiph.org/celt.git` rather than a fork.

**A genuine upstream gap.** CELT 0.11.3's checked-in `libcelt/static_modes_float.c` references two
tables — `eband5ms` and `band_allocation` — that it never defines; they exist `static` and private
in `modes.c` instead. `static_modes_fixed.c` has the same gap. A plain `cl` over the official tree
fails with `C2065: 'eband5ms': undeclared identifier`. The build script supplies both verbatim from
`modes.c` as a separate translation unit rather than editing vendored source.

**A stale docstring that inverted the success check.** `celt_decode`'s header comment says
`@return Error code`. That is true only for failure — `celt.c` returns the decoded sample count on
success, the same contract as `opus_decode`. Checking `!= CELT_OK` treated every *successful*
decode as an error. Caught by the corpus test immediately, which a hand-built fixture might have
silently agreed with.

**What was measured, and what each measurement killed:**

| Hypothesis | Test | Result |
|---|---|---|
| Wrong sample rate / frame size | All 5 supported rates (8000–48000) with matching 20 ms frame sizes, 200 real packets | **Byte-identical every time**: 103 accepted, 163 rejected. Rate is not the variable. |
| Leading byte is a marker | Skip 1 byte; 63-byte slices; ignore trailing byte — 4 offset variants | No variant beats baseline. |
| Cross-speaker desync | All sampled frames came from one client slot | Not applicable — nothing to interleave. |
| Cross-packet desync | Fresh decoder per packet, intra-packet framing only | Failure rate unchanged. |
| Multi-frame concatenation | Break down by packet length and frame position | **Isolated single-frame 64 B packets still fail 58%** with a fresh decoder at position 0. Not a frame-boundary problem. |

Position 1 within a packet degrades further (71% vs 58%), so there *is* some additional state
effect layered on top — but the 58% base rate on isolated first frames is what rules out every
framing explanation tried.

**The community-documented "22 kHz, 22 kbps" for `vaudio_celt` is consistent with
`svc_VoiceInit`'s measured `22050`** — but 22050 is not among the five rates `resampling_factor`
accepts, and the sweep above proves rate does not affect success anyway, so that number does not
explain the failure either. It remains unexplained what `22050` denotes in this path.

**Two integers were recovered from TF2's own shipped `vaudio_celt.dll`** by scanning for call sites
and reading the immediate `push` values — the same technique that recovered the user-message
registration order from six shipped clients. All seven calls to `celt_mode_create` push
`(48000, 960, NULL)`, and the single `celt_decoder_create_custom` call passes `channels = 1`. Those
values are what the decoder now uses. Note they do *not* imply TF2 decodes voice at 48 kHz:
`celt_decoder_create` builds that same internal mode regardless of the caller's requested rate, so
the constants describe CELT's own default mode, not TF2's voice rate.

**Not consulted, deliberately:** the 2012 Source engine leak, which surfaces in searches for the
`VoiceCodec_Frame` / `IFrameEncoder` symbol names visible in the binary. That is Valve's
proprietary source redistributed without authorisation — a different and harder line than either
reading the published SDK or extracting a constant from a binary on a machine that owns the game.
The engine-side `VoiceCodec_Frame` wrapper remains the most likely place the answer lives, and it
has never been published.

See `RISKS.md` B33.
