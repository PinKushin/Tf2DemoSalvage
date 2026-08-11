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
