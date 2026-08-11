# 07 — Writing demos back, and writing new ones

Reading a format proves you can consume it. Writing it proves you understood it. This project does
both, and the second turned out to be the sharper instrument by a wide margin.

There are three distinct levels here, and conflating them overstates the result:

| Level | Claim | Status |
|---|---|---|
| **Round trip** | decode to text, recompile, byte-identical | achieved, 100% of message bits |
| **Authoring** | generate a file no recorder wrote, engine plays it | achieved, 2026-08-11 |
| **Synthesis** | build a demo from nothing, no source file | not attempted |

## Round trip: the strongest self-check available without a second parser

The demo decompiles to a readable assembly text (`.dasm`) and compiles back. The criterion is
**byte-identical**, which is unforgiving in the right way: any field this project misunderstands
produces different bytes, and there is no partial credit.

What makes it a genuine test rather than a tautology is that the text form is *structured*, not a
hex dump. A message re-encoded from `svc_packetentities delta=1 from=2345 … prop 19/4/0
DT_BaseEntity.m_flSimulationTime i 52` has been through a real decode and a real encode. Where the
text still holds raw hex, the round trip proves nothing about that region, which is why the
measurement that matters is **what fraction of bits are structured**, not whether the files match.

That distinction was itself a finding: an early report printed a clean queue while 6.3 million bits
were still hex, because it measured the writer's *capability* rather than its *output*. See
[08-method.md](08-method.md).

### Recording the encoding shape, not just the values

The recurring obstacle, hit at least six times in different messages: **which optional fields were
sent is not recoverable from the values themselves.** A sound with volume 1.0 might have sent the
volume field explicitly or relied on the default. Both decode to 1.0; only one re-encodes to the
original bytes.

The fix each time was to have the decoder record the *shape* alongside the data — a `SoundFields`
mask, an `IndexPayloadBits`, a `CoordShape`, a `StringTableEntry` history record. Any decoder
intended to support re-encoding has to do this from the start; retrofitting it means revisiting
every call site.

### The writer's own giving-up has to be reproduced

The hardest single case. Some snapshots re-encoded *longer* than the original because this parser
politely wrote a terminator that Valve's writer had omitted — not by choice, but because `bf_write`
abandons a field that does not fit rather than truncating it. Reproducing the format meant
reproducing the failure mode. Full detail in [09-valve-implementation.md](09-valve-implementation.md).

## Authoring: the engine plays files this project generated

Confirmed 2026-08-11, in the March 2007 client (build 3258, protocol 11) that recorded the source
demo. Four files, produced by cutting the command stream and **rewriting the header**, so the byte
sequence is one no recorder ever emitted:

| frames | ticks | length | size | result |
|---|---|---|---|---|
| 1 | 0 | 0.000 s | 159,986 B | renders a still — correct for one frame |
| 20 | 60 | 0.900 s | 172,167 B | never leaves the startup pause every demo has |
| 70 | 227 | 3.405 s | 186,696 B | plays normally |
| 300 | 995 | 14.925 s | 257,634 B | plays normally |

Nothing crashed, and **the behaviour tracks the length**, which is what separates a correct file
from one the engine merely tolerates.

### Content edits, not just framing

Cutting a file up only exercises the container. The stronger test is changing what the demo
*says*. Two edits, both applied to decoded values in the assembly text and recompiled:

- **A player teleported.** Every `DT_TFLocalPlayerExclusive.m_vecOrigin` rewritten in three phases:
  untouched, then displaced +1024 on x, then raised +768 on z. Decoding the result back confirmed
  the edit exactly — middle-third x moved 952…1117 → 1976…2141, final-third z −456 → 312.
- **Rockets and explosions raised** +512 z: 73 rocket origins and 44 explosion positions.

Both compiled to **471,848 bytes — the original's exact size**, because the new coordinates encode
at the same widths. Both played. The raised explosions were visibly far overhead; the teleported
player was not visible, which is itself the finding below.

**So the encoder can express values that never existed in any recording, and the engine accepts
them.** That is a different and stronger statement than the round trip.

### `democmdinfo` drives the POV camera, not the player entity

The teleport had no visible effect on the camera. That is not a failure — it identifies which of
two candidate sources the client trusts. A POV demo carries the camera independently, in the
`democmdinfo` prologue attached to each packet, and editing the local player's *entity* origin does
not move the view. To move the camera you edit the prologue.

Consequence for anyone building on this: **the entity stream and the recorded camera are separate
authorities**, and a viewer reconstructing a POV must decide which one it follows.

### Tick zero is `dem_synctick`, not the smallest tick in the file

Found by cutting a demo to one frame and getting a file that claimed to be 32 seconds long.

Everything in the connect phase carries the **server's** tick — 2083 through 2153 in the 2007
recording — while the packet stream restarts at 0. So "largest tick in the file" reports the connect
phase as the demo's length. `dem_synctick` is the engine's own marker for where playback ticks
begin, which makes it the honest boundary rather than a list of command types to skip.

SourceTV recordings carry no `dem_synctick` at all, so any fallback has to count packets rather
than scan the whole file — otherwise the bug returns on exactly the files that cannot signal
otherwise.

### Length is stated three ways, and the header states its own tick interval

`playbackframes` counts packets, `playbackticks` is the last tick, `playbacktime` is ticks × the
interval. The interval does not need to come from `svc_ServerInfo`: the header already states it
twice over, as `playbacktime / playbackticks` (51.945 / 3463 = 0.015, matching what the message
carries).

### A short demo is not a cheap demo

A one-frame cut of a 460 KB recording is still **160 KB**, essentially all signon — schema and
string tables. Playback content is roughly 12 KB per second on top of that fixed cost.

This matters for corpus planning: shrinking specimens by recording shorter sessions has a hard
floor, and the floor is set by the era's schema size, not by the recording.

## Why none of this is in the product

The truncation code was written, tested, and deleted the same day. This project is not a TAS tool
and not a demo editor; the edits above are **probes**, and their value is the finding, not the
capability. Probe scripts live in a scratchpad, findings live here.
