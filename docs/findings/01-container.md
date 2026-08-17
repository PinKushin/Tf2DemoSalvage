# 01 — The container

The outermost layer, and the only one whose **code** Valve has never published — `engine.dll` holds
the `.dem` reader and it is not in the SDK.

> **Correction, 2026-08-16.** This continued "Everything here was established by measurement", which
> overstated the absence. The reader is genuinely missing; the **format declarations are not**.
>
> `src/public/demofile/` contains exactly one file, `demoformat.h`, and it declares `demoheader_t` in
> full — the 8-byte stamp, two protocol ints, four `MAX_OSPATH` strings, the playback float and three
> ints — plus `DEMO_HEADER_ID` and the `dem_*` command enumeration with `dem_lastcmd`.
>
> **Nothing in this project cited it**: not this file, not the reader, not a test. So the container
> was the only layer with no conformance check against the SDK, because the SDK was believed to hold
> nothing to check against.
>
> The measurements were right — `DemoHeaderConformanceTests` passed on its first run, deriving 1072
> from Valve's member list. What changes is that the layout is now pinned to the declaration instead
> of to a correct guess, and the command names are checked rather than remembered.
>
> Fifth instance of an absence recorded more broadly than the evidence supported; the running list is
> in `05-user-messages.md`. The shape here is subtler than the others — the strong claim (no reader)
> was true, and the sentence after it quietly widened to cover things that were published.

For the current, correct description see `docs/SPEC.md` Layer 1. This file records how it was
learned and what was wrong on the way.

## Shape

A **1072-byte header**, then a stream of commands. Each command is a 5-byte header — a type byte
and a 4-byte tick — and `dem_packet` and `dem_signon` additionally carry a 76-byte `democmdinfo_t`
prologue before their payload.

The header states the demo's length **three separate ways**, and all three are read:

| Field | Meaning |
|---|---|
| `playbackframes` | number of `dem_packet` commands |
| `playbackticks` | the last tick reached |
| `playbacktime` | seconds — ticks × the tick interval |

They are mutually consistent, which is more useful than it sounds: `playbacktime / playbackticks`
recovers the tick interval (0.015 for TF2) **without decoding a single message**, and it agrees
with what `svc_ServerInfo` carries. An operation on the container therefore never needs to depend
on the message layer to know how long the demo is.

## Tick zero is `dem_synctick`, not the smallest tick present

The finding that cost the most surprise per byte, and it is invisible until you try to *write* a
demo.

**Everything in the connect phase carries the server's tick.** In a 2007 recording the `dem_signon`
commands sit at ticks 2083–2152 and `dem_datatables` at 2153, while the first `dem_packet` is at
tick **0**. The packet stream restarts; the connect phase does not.

So "the largest tick in the file" is not the demo's length — it is whatever the server's uptime
happened to be at connect. Computing a demo's length that way reports a one-frame file as 32
seconds long, which is exactly how this was found ([07](07-writing-demos.md)).

`dem_synctick` is the engine's own marker for where playback ticks begin. **SourceTV recordings
carry none at all**, so any fallback must count packets rather than scan the file, or the bug
returns on precisely the files that cannot signal otherwise.

## The two clocks, and how a client stall shows up in the file

A packet carries **two** clocks: the container's command-header tick, which counts from the start
of the recording, and `net_Tick`, which carries the **server's own absolute tick**. They differ by
a large constant — around 5,000 in one pub recording, simply because the server had been up a
while before the client connected.

**The offset is stable to within a tick or two**, which is a much stronger check than it sounds.
The two numbers are decoded by completely different paths — a 32-bit little-endian field in the
container versus 32 bits pulled from a bit stream at an arbitrary offset — so any desynchronisation
in either shows up immediately as the offset wandering.

### A recording gap and a decoder desync look identical until you check the demo clock

Both make the offset spread. They are trivially separable, and the separation is the useful part:

| | demo clock | server clock | offset |
|---|---|---|---|
| **recording stall** | gaps, seconds between consecutive packets | gaps too | steps once, stable either side |
| **decoder desync** | normal, 1–3 ticks per packet | garbage | wanders continuously |

So **a client hitch is visible in the file**: consecutive packets sit seconds apart on *both*
clocks, and the offset takes a permanent step because server time passed while demo time did not.
Nothing else produces that signature.

Measured on a 2026 pub demo: rock-stable offset (±1 tick) for the first 800 packets, a step of
~3,500 ticks across packets 1064–1068 where consecutive packets are 4–5 seconds apart on the demo
clock and 10–20 seconds apart on the server's, then rock-stable again for the remaining 12,500
packets. That is a **36-second freeze**, recorded faithfully as a hole.

The cause is worth recording because it is a methodological lesson rather than a format one: this
repository's own mutation suite was saturating the machine while the demo was being recorded. **The
measurement instrument perturbed the subject.** Long CPU work does not belong on a machine that is
simultaneously producing specimens — see `docs/memory/`.

Two consequences beyond specimen hygiene:

- **A viewer must not interpolate across such a gap.** Entity positions either side are real and
  the seconds between them are not; smoothing them together invents movement that never happened.
- **It is a specimen quality check.** A demo with a large mid-recording step is a poor choice for
  anything timing-related, whatever it is fine for otherwise — the pub demo above still settled a
  user-message id question perfectly well.

## `democmdinfo` is the camera, and it is authoritative for POV

Each packet carries a 76-byte prologue holding view origin, angles and flags. It is a *separate
authority* from the entity stream, and for a POV demo it is the one the client follows: rewriting
the local player's entity origin does not move the camera, while the prologue does
([07](07-writing-demos.md)).

That has a direct consequence for any viewer built on this data — a POV reconstruction must decide
which source it follows, and the answer the engine gives is the prologue.

It is also useful as an **independent control**. The prologue is decoded by completely different
code from `svc_Sounds` or a user message body, so agreement between them at the same tick is real
evidence rather than a tautology. That is how the protocol-14 `Damage` layout was confirmed.

## Command mix distinguishes recording modes

| Command | POV | SourceTV |
|---|---|---|
| `dem_usercmd` | one per tick — thousands | **none** |
| `dem_consolecmd` | present | none |
| `dem_stringtables` | protocol 15 and up | protocol 15 and up |
| `dem_synctick` | present | **absent** |

Nobody is pressing keys on a SourceTV recording, so it carries no input stream at all. This is the
structural difference between the modes and it is why a POV demo of the same session is
substantially larger.

## Size is dominated by the signon

A one-frame cut of a 460 KB demo is still **160 KB**. The signon — schema and string tables — is a
fixed cost paid before any gameplay is recorded; playback content is roughly 12 KB per second on
top.

Practical consequence: **a short demo is not a cheap demo.** Corpus specimens cannot be shrunk
below their era's schema size, however briefly they are recorded.

## `dem_stringtables` does not exist below protocol 15

Not mentioned in `proto_version.h`, and not predicted. At protocol 14 and below the tables arrive
only as `svc_CreateStringTable` during signon.

Confirmed rather than assumed, because the corpus holds a POV **and** a SourceTV recording at
protocol 14 and both lack it — which makes it a property of the era rather than of the recording
mode. One file could not have established that. See [08-method.md](08-method.md) on recording both
points of view.

## A truncated file is still a readable file

`z1800.dem`, the project's founding specimen, is **one byte short** of complete. It decodes end to
end regardless, because the container is a command stream rather than a length-prefixed archive:
the reader simply runs out at the end.

Worth knowing before treating a short read as corruption — for this format it usually means the
recording stopped, not that the file is damaged.

### It is 43% of a real archive, not an oddity (2026-08-12)

**Evidence class: measured**, on 370 competitive demos from an ESEA archive.

**Provenance, recovered 2026-08-13 and recorded because it had already been lost once.** The
archive is **`http://demos.igmdb.org/`**, and calling it "an ESEA archive" undersells it twice
over. It carries per-season directories across **several leagues and regions** - ETF2L (Europe),
RGL (North America) and ozfortress (Australia) among them, alongside the ESEA seasons **29-32**
this project's 370 demos came from. cp_process came from it too. It is run by IGMDb, which began
as a fragmovie and demo-render site rather than as a league, which is why it outlived the leagues'
own hosting - and why ESEA's expiry-date problem does not apply to it.

That breadth matters here specifically: the era axis is short of protocols **12-13** and
**17-23**, and a multi-league archive spans dates and regions that one league's seasons cannot.
Worth walking before assuming a gap needs a period client and a manufactured recording.

Three things about it are worth writing down rather than rediscovering:

- **It is HTTP only in practice.** The host serves a certificate valid for `igmdb.org` and
  `www.igmdb.org` and *not* for `demos.igmdb.org`, so any client that upgrades to HTTPS fails with
  a name mismatch rather than a 404. That reads as "the archive is gone" and it is not.
- **It is the exception, not the rule.** ESEA's own SourceTV demos shipped with expiry dates and
  cannot be downloaded from the league; the community's standing advice is to ask individuals who
  kept them. So this archive is a survivor, and the same fragility applies to it - the measurement
  above should be treated as the durable artefact, not the link.

The original write-up said only "an ESEA archive", which was enough to reproduce nothing. Any
future corpus source gets named here at the time it is used, with the season or date range and the
retrieval method - see also the Benroads collection in `docs/TIMELINE.md`.

**159 of them — 43% — end in the middle of a command.** Every one stops within four kilobytes of
the end of the file and none fails anywhere else; the median demo is 99.995% complete. So refusing
a truncated file meant discarding a twenty-megabyte recording over its final two hundred bytes,
and the "usually" above is an understatement: for competitive demos this is the normal ending.

That is what a match ending looks like. The server stops writing mid-packet when the map changes
or the process goes away, and nothing returns to tidy up the tail.

### A truncated demo also lies about its own length, and that one is silent

**Evidence class: measured**, and it is the more dangerous half.

`PlaybackTicks`, `PlaybackFrames` and `PlaybackTimeSeconds` sit in the header, which is written at
the *start* of recording — with zeroes. The engine fills them in by seeking back to offset zero
when recording **stops**. A recording that ends because the server died never reaches that write.

So the file claims to be empty while holding a full match:

```
warning: esea_match_13977649.dem has no dem_stop: the recording was truncated, not ended
warning: esea_match_13977649.dem declares 0 frames but holds 110,238
Map                cp_process_final
Playback time      0.00 s
Playback ticks     0
```

Unlike the truncated tail, nothing about this reads as damage. Zero is a number, the header parses
cleanly, and every field is in range. A viewer that believes it shows a demo with no timeline and
a dead play button — which is how this was found.

The count exists twice by unrelated routes: once as a number the engine wrote, and once as a
consequence of the commands themselves. When the cheap copy is missing the expensive one is still
there, so the walk that recovers it is the same "two recordings of one value" technique used
against the string tables. It costs a pass over the file, and it is only paid when the header
states nothing — a complete demo is taken at its word.

## The engine's own header reader, read out of `engine.dll` (2026-08-11)

Everything above was worked out from the bytes. The 2008 engine (build 3420, protocol 14) was then
disassembled, and two functions settle the container outright. `CDemoFile` ships in no SDK, so this
is the only place the answers exist.

### `ReadDemoHeader` — the accept rule, and it is not an equality

```
memset(header, 0, 0x430)
compare 8 bytes against "HL2DEMO"        -> "%s has invalid demo header ID."
if (network != 14 && network < 12)       -> "ERROR: demo network protocol %i outdated, engine version is %i"
if (demoProtocol < 4 && demoProtocol > 1) -> accept
                                          -> "ERROR: demo file protocol %i outdated, engine version is %i"
```

Four facts fall out, none of them previously knowable:

**The header is `0x430` = 1072 bytes**, memset as one block. That is the number this project
arrived at by measurement, now confirmed at the source.

**Two protocols, validated separately, with separate messages.** The engine distinguishes the
*container* version from the *network* version and always has.

**The network accept rule is `>= 12`, not `== 14`.** This is the compatibility code Valve's
15 November 2007 patch note describes — "backward compatibility code to allow demos recorded with
protocol 12 to continue to be playable under protocol version 13" — still present four months
later. It answers a question the changelogs could not:

| transition | breaking? | evidence |
|---|---|---|
| 11 → 12 | **yes** | the protocol-14 engine *refuses* a protocol-11 demo |
| 12 → 13 → 14 | **no** | one engine accepts all three interchangeably |

So the format did break once in TF2's first five months, at the very first step, and then not
again through 14. Valve drew the compatibility line immediately above 11 and never moved it —
which is also why a launch-era recording cannot be played by any later client, and why this
project exists.

**The container accept range is 2 and 3.** `demoProtocol < 4 && demoProtocol > 1`. Version 2 was
still playable in 2008; every TF2 demo in the corpus is 3.

### `StartRecording` — the writer states the constants

The recorder zeroes the same `0x430` block and writes:

```
header + 0x08 = 3       demo protocol
header + 0x0C = 14      network protocol
```

both as literals. **That dates build 3420 to protocol 14 from the binary alone**, independently of
running the client — and the same technique would date any build whose `engine.dll` can be
obtained, which is the cheap triage path for the 17–23 gap.

The field offsets used by both functions confirm the layout field for field: magic at 0, demo
protocol at 8, network protocol at 12, server name at 16, client name at 276, map at 536, game
directory at 796, and the sign-on length written last at 1068 from a file-position call.

**One loose end worth recording.** The whole validation block sits inside a guard on a flag at
`+0x548`; when that flag is set, no check runs at all. What sets it is not yet known.

### Three engines, nineteen years, one unchanged constant

The 2007 and July 2026 engines were read the same way. The header test is the same three lines in
all three, and only one number in it has ever moved:

| engine | protocol | the test | accepts |
|---|---|---|---|
| 2007, build 3258 | 11 | `if (network != 0xb)` | **11 only — a strict equality** |
| 2008, build 3420 | 14 | `if (network != 0xe && network < 0xc)` | ≥ 12 |
| July 2026 | 24 | `if (network != 0x18 && network < 0xc)` | ≥ 12 |

**The launch engine had no compatibility at all.** Equality, one accepted value. The `< 0xc` clause
appears in 2008 and is the November 2007 code.

**And it is still there today, with 12 still in it.** Ten protocol bumps later, the floor Valve
wrote five weeks after release has never been raised. `0xc` is now a **vestigial constant**: it
admits eight protocol versions that no modern client can decode a single packet of, because
nothing downstream of the header knows anything about them.

Two consequences, and the second is the interesting one.

**Protocol 11 is the only version modern TF2 refuses at the door.** Everything from 12 up passes
the header check and fails later, in the stream. So "the client can no longer play this file" has
two entirely different mechanisms behind it depending on whether the demo predates or postdates
12 November 2007, and only the launch era gets a clean error.

**The container version has never moved.** `demoProtocol` is accepted when it is 2 or 3 — written
`(iVar3 < 4) && (1 < iVar3)` in 2008 and compiled to the unsigned trick `iVar3 - 2U < 2` today —
identical range in both. The `.dem` container is the single most stable thing in this format: one
version bump in nineteen years, and the previous version is still accepted.

This is the same category as the vestigial fields in [09](09-valve-implementation.md): **a
constant that stopped being maintained is a fossil, and it dates the last time anyone looked at
the code around it.** Nobody has revisited demo header compatibility since 2007.

**A smaller fossil, in the same function.** The live engine's second error string reads

> `ERROR: demo file protocol %i outdated, engine vnoteersion is %i`

with `note` spliced into `version`. It is in the shipped binary — the raw bytes and the decompiler
agree — so modern TF2 ships a corrupted format string on a path nobody has hit in years.

## `dem_usercmd` is the player, and its padding is the command before it (2026-08-11)

The last container-level payload this project carried without reading. A `dem_usercmd` is one
`CUserCmd` — view angles, movement, buttons, impulse, weapon switch, raw mouse deltas — written by
`CDemoRecorder::RecordUserInput` at command rate. **It is the only thing in a demo that describes
the person rather than the world**, and SourceTV recordings have none of it, because there is no
player behind the camera.

*Sourced*, from `game/shared/usercmd.cpp` and `game/client/in_main.cpp`. Read from the **encoder**:
`WriteUsercmd` states the condition that clears each presence bit, and `ReadUsercmd` only implies
it.

Four things a reasonable guess gets wrong.

**The baseline is a default-constructed `CUserCmd`, not the previous command.**
`CInput::EncodeUserCmdToBuffer` puts `CUserCmd nullcmd;` on the stack for every call. So the
delta is against a constant, and every command decodes independently — a decoder that carried
state between commands would work on a clean file and desynchronise at the first gap.

**Because of that, an absent `command_number` means one, not zero.** The writer's condition is
`to->command_number != from->command_number + 1` and `from` is always zeroed, so the bit is cleared
for the value **1**. Same for `tick_count`. A decoder defaulting the field to zero is off by one on
nearly every command in the file.

**The `weaponsubtype` presence bit is nested inside `weaponselect`'s.** It is the only conditional
presence bit in the layout. Reading it unconditionally costs one bit and shifts both mouse deltas
into plausible-looking values.

**`mousedx`/`mousedy` go through `WriteShort` and are signed.** Read unsigned, a small leftward
flick becomes a number near 65535 — which looks like data until something integrates it.

### The finding: the padding is stale bits of the previous command

**This one was written up wrong first, and the wrong version is kept here because the correction is
the interesting part.**

`bf_write` composes its partial tail dword with a read-modify-write that preserves every bit outside
its mask:

```cpp
dword1 ^= ( mask1 & ( curData ^ dword1 ) );
```

and `StartWriting` never clears the buffer it is handed. So bits a write does not cover keep
whatever was already there. That much is *sourced*, from `tier1/bitbuf.cpp` and
`public/tier1/bitbuf.h`.

*Measured*, across the ten point-of-view demos:

| | |
|---|---|
| user commands | 385,236 |
| ending 3 bits short of a byte | 99.8% |
| values those 3 bits take | all of 0–7 |

**The first account of this said "uninitialised engine stack" and called it a leak.** That was an
assertion, not a measurement. Non-zero and varying is consistent with several mechanisms, and the
one asserted happened to be the alarming one. It also contradicted a sentence in the same paragraph
— that the per-demo distributions look like *leftovers from a previous, longer write* — which is a
different mechanism entirely and was sitting right there.

The condition that separates them: if the buffer is merely reused and never cleared, the untouched
tail still holds what the **previous** command wrote at those exact bit offsets, and that is
predictable. Foreign memory is not.

*Measured*: **150,606 of 199,929** non-zero pads — **75.3%** overall, and 86–97% within each demo
except the 2026 RGL pug at 62% — are bit-for-bit what the previous user command put at the same
absolute offsets. Chance for a three-bit field known to be non-zero is about one in seven.

So nothing escapes the file that was not already in the file. It is not a disclosure of anything:
the only content is the preceding command, which the demo contains anyway. What this measurement
cannot separate is a stack array at a stable address from a reused static buffer — both predict the
same result — and it does not account for the ~25% that do not match, which stays open rather than
explained.

The consequence for this project is unchanged by the correction. **A user command cannot be
re-encoded from its values**, so the padding has to be carried. Assuming zero would rebuild a file
differing from the original in nearly every user command *while every decoded field still read
correctly* — the failure shape that only a round-trip property catches, and it was caught on the
first corpus run.

### Two independent decodes of the same three floats agree

The strongest evidence the layout is right, and the only piece of it that does not depend on this
project's reading of Valve's source. A demo stores the view angles **twice**, by unrelated routes:
`democmdinfo_t` as plain little-endian floats ahead of every packet, and the user command
bit-packed behind presence bits. Neither path can see the other.

*Measured*: **329,969 of 330,853** packets carry angles bit-identical to the last user command
before them — 99.7%. The remainder is the client sending input faster than the server sends
snapshots. A transposed field or a width off by one could not produce that number.

Every one of the 385,236 commands re-encodes byte-exactly, at protocols 11, 14, 15, 16 and 24.
**The layout has not changed in nineteen years.**

### `dem_consolecmd` was never printed either

Not a decoding problem — it is a null-terminated string and always was. It went unprinted because
there was nothing to work out, so nothing prompted anyone to do it. Worth having anyway: it is
where every bound console command the recording player typed shows up, and the opening run of a
demo is a client dumping its `dsp_*` and `cl_*` settings.
