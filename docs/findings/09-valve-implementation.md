# 09 — What Valve's own code says

The wire format is one subject; **how Valve's engine and game code actually behave** is another,
and it is the more interesting one. Several things this parser had to get right are not properties
of "the format" at all — they are properties of a specific implementation, visible only by reading
it.

Every claim here is from **published source** (`ValveSoftware/source-sdk-2013`) unless marked
otherwise. That distinction matters legally and practically; see `CLAUDE.md`.

## What the SDK actually contains, and what it does not

A recurring waste of time is reaching for the SDK expecting the demo parser. It is not there.

| In the SDK | Not in the SDK |
|---|---|
| Game client/server DLL source (`game/client`, `game/server`, `game/shared`) | `engine.dll` — the demo reader, netcode, entity delta engine |
| `tier0`/`tier1` utilities, including **`bitbuf.cpp` — the bit reader/writer itself** | `materialsystem`, the renderer |
| `mathlib`, format headers (`bspfile.h`, `studio.h`, `coordsize.h`) | The `.dem` container code |
| Map/model compilers (`vbsp`, `vrad`, `studiomdl`) | SourceTV's relay implementation |

So: **user message layouts are readable** (they live in game code), **bit-level primitives are
readable** (`tier1`), and **the container and entity-delta engine are not**. That split explains
why this project's user-message work is transcription while its `svc_PacketEntities` work had to be
inferred.

`bf_write`/`bf_read` being public is worth stating loudly, because it removes any reason to
disassemble for bit-level questions: `src/tier1/bitbuf.cpp` and `public/tier1/bitbuf.h`.

## The writer gives up mid-message — it never truncates a field

The single most consequential engine behaviour found, and the answer to a mystery that cost real
effort: some entity snapshots re-encoded *longer* than the original, and some deletion lists ended
without their terminator.

`bf_write` does not write a partial field when the buffer is full. It **abandons the write
entirely**:

```cpp
inline void bf_write::WriteOneBit(int nValue)
{
	if( m_iCurBit >= m_nDataBits )
	{
		SetOverflowFlag();
		CallErrorHandler( BITBUFERROR_BUFFER_OVERRUN, GetDebugName() );
		return;                       // nothing written, m_iCurBit unchanged
	}
	WriteOneBitNoCheck( nValue );
}
```

`CheckForOverflow` behaves the same way for multi-bit writes. Once the flag is set every
subsequent write returns immediately, so the message simply *stops*, mid-structure, with no
marker.

**Consequences for any parser:**

- A message can end without the terminator its format requires. That is not corruption and not a
  parser bug — it is what the engine emits under pressure. A decoder must treat "ran out of stated
  length" as a legitimate end of list, not as an error.
- The remaining bits are whatever they were; there is no partial field to misread, which is the
  one mercy here.
- **A faithful re-encoder must reproduce the giving-up, not just the data.** Writing the
  terminator that the original omitted produces a longer, "more correct" message that no longer
  matches the bytes Valve wrote.

Corroborated independently: the same shape was visible in `engine.dll` (via Ghidra, output kept
outside every repository) as a check that consumed the remaining bits and set an overflow flag
rather than writing the field. The published header is the citable source; the disassembly only
confirmed the engine's inlined variant does the same thing.

## `proto_version.h` enumerates the boundaries

Valve ships the list of protocol changes. Each constant names **the last build *without* the
change** — an off-by-one that inverts the meaning if misread:

```c
#define PROTOCOL_VERSION_14   14   // create string table with compression flag
#define PROTOCOL_VERSION_17   17   // MD5 in map version
#define PROTOCOL_VERSION_22   22   // sound index bits
#define PROTOCOL_VERSION_23   23   // varint lengths
```

This is why this project's protocol tests read `protocol > Constant` rather than `>=`. It also
means several era rules could be written *before* any demo from that era existed — and two of them
were later confirmed by demos that had never run through them. See
[06-protocol-eras.md](06-protocol-eras.md).

Note the header is not complete: the absence of `dem_stringtables` at protocol 14 is a real era
difference that `proto_version.h` does not mention at all.

## TF2 inherited HL2's messages, including fields it stopped reading

`Damage` is the clearest case. TF2's HUD reads the damage amount, then:

```cpp
msg.ReadLong();   // read and ignored
```

Tempting to call that padding. It is not — the server writes `info.GetDamageType()`, the live
`DMG_` flag set. **HL2's client used it** to choose which damage icon to draw; TF2's stopped, and
the server never stopped sending it.

The lesson generalises: **"the game ignores this field" is a statement about the reader, not about
the data.** You have to read the writer to know whether a field carries information.

And the opposite case exists in the same layer. `ResetHUD`'s reader takes *nothing at all*, yet
the message occupies 8 bits, because `player.cpp` writes `WRITE_BYTE( 0 )` — a literal placeholder
so the body is non-empty. Same symptom from the reader's side, opposite truth.

## Valve's own readers enforce exact consumption

This project independently arrived at the rule that a layout must consume its body exactly, and
refuses to report fields otherwise. Valve does the same, and says why:

```cpp
// sanity check: the message should contain exactly the # of bytes we expect based on the bit field
Assert( !msg.IsOverflowed() );
Assert( 0 == msg.GetNumBytesLeft() );
// if byte count isn't correct, bail out and don't use this data, rather than risk polluting
// player stats with garbage
```

— `CTFStatPanel::MsgFunc_PlayerStatsUpdate`. Worth noting because exact consumption is often
argued against as pedantry. It is the game's own standard.

## Dead code that tells you the format's history

`PlayerStatsUpdate` reads one 32-bit value per set bit of a 32-bit field, guarded by
`while ( iSendBits > 0 && iStat <= TFSTAT_LAST )`. In the 2013 SDK `TFStatType_t` runs to 44, so
bit 31 selects stat 32 and **stats 33 through 44 cannot be sent through this message at all**. The
guard is unreachable.

It was not always. The guard bites when the stat table is *shorter* than 32 entries — which is
what an earlier build looks like. **A dead guard is a fossil of the era when it was live**, and
noticing that is a cheap way to date a structure without a specimen.

## Clamps at the writer are format facts

```cpp
WRITE_SHORT( clamp( (int)info.GetDamage(), 0, 32000 ) );
```

The field is 16 bits and the game clamps it to 0–32000 before writing. So a decoded damage of
40,000 is not a big hit, it is a misparse — and the largest real single hit in TF2 is around 450.
Reading the writer gives you a **validity range for free**, which is what makes plausibility checks
sharp rather than arbitrary.

## Coordinate encoding is two constants

`public/coordsize.h`:

```c
#define COORD_INTEGER_BITS      14
#define COORD_FRACTIONAL_BITS    5
```

Everything about `ReadBitCoord` follows from those: two presence bits, a sign bit if either is set,
then up to 14 integer bits and 5 fractional. A full axis is 22 bits, integer-only is 17, fraction-
only is 8, absent is 2. **Those four numbers do more work in this project than any other constant**
— they are what let message layouts be identified from body lengths alone, before reading a byte.
See [05-user-messages.md](05-user-messages.md) for the worked example.

## Read the writer, not the reader

The standing rule that came out of all of the above.

A reader tells you what one client did with the bytes. A **writer states intent**: which fields
exist, in what order, under what condition, clamped to what range. The two disagree exactly where
it matters most — vestigial fields, placeholder bytes, clamps, and conditions the corpus never
exercises.

Where both are available, read both, and treat the writer as authoritative. Where only the reader
is available — as for `engine.dll` — expect to be missing the intent, and lean harder on
arithmetic and on the corpus.

## What is *not* knowable from the SDK

Worth listing so the next investigation does not start here:

- The container format. No `.dem` code ships.
- `svc_PacketEntities` delta semantics, the entity baseline mechanism, the deletion list.
- SendTable **flattening order** — this project's version of it was wrong and was corrected by
  differential comparison against `demostf/parser`, not by reading anything.
- SourceTV's relay behaviour, and therefore how far a relayed recording may diverge from what a
  player saw.
