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
