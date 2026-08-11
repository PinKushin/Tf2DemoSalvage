# 01 — The container

The outermost layer, and the only one whose code Valve has never published — `engine.dll` holds the
`.dem` reader and is not in the SDK. Everything here was established by measurement.

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
