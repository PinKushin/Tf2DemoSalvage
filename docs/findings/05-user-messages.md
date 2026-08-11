# 05 — User messages

`svc_UserMessage` is the game's own extension point: the engine carries a type byte, a length, and
an opaque body, and the *game DLL* decides what any of it means. Nothing on the wire names the
message. That makes this layer the one where a parser is most likely to produce confident nonsense.

Status as of 2026-08-11: **395 of 445 user messages in the corpus decode, up from 217.** Opaque
bits fell from 14,672 to 3,920.

## The id table is registration order, and it has not moved

A user message's id is its position in the game's registration list. Insert a message rather than
append one and every id after it shifts — the same trap as the property-type renumbering in
RISKS B18. The table this project uses is transcribed from `game/shared/tf/tf_usermessages.cpp` in
the 2013 SDK, so a priori it describes one build.

**Measured, it describes all of them.** Histogramming id against body width across protocols 11,
14, 15, 16 and 24:

| id | name | width | present at |
|---|---|---|---|
| 0 | Geiger | 8 | every era |
| 1 | Train | 8 | every era |
| 5 | TextMsg | varies | every era |
| 6 | ResetHUD | 8 | every era |
| 8 | ItemPickup | 168/176 | 2008 on |
| 10 | Shake | 104 | 2008 on |
| 11 | Fade | 80 | every era |
| 12 | VGUIMenu | 80/88 | 2008 on |
| 13 | Rumble | 24 | every era |
| 18 | Damage | see below | 2008 on |
| 28 | PlayerStatsUpdate | 112–272 | every era |

Eighteen years, and the head of the table does not shift. Widths agree too, which is a second
independent check — a shifted id would land a 24-bit Rumble body on a name expecting something
else.

**Why this is worth stating explicitly:** when protocol 14's `Damage` decoded to garbage, a shifted
id was the first suspect. One histogram eliminated it and pointed at the layout instead. *Check
alignment before suspecting a layout* — it is one measurement and it halves the search.

## `Damage`, and the era break at protocol 15

The message behind a POV demo's red directional damage indicator. It is the only record of the
*direction* incoming damage came from.

**Modern layout** (protocol 15 and up), from `CHudDamageIndicator::MsgFunc_Damage` in
`tf_hud_damageindicator.cpp`:

```c
damage.iScale = msg.ReadShort();     // 16 bits
msg.ReadLong();                      // 32 bits, read and discarded by the game
if ( !msg.ReadOneBit() ) return;     // 1 bit: does a position follow
msg.ReadBitVec3Coord( vecOrigin );   // 3 presence bits, then the axes sent
```

**Protocol 14 and below send a different message**: one byte of damage, then the vector. No
damage-type long, no presence bit, and the vector is always there.

### How the old layout was found

Not by trying candidates. **By arithmetic on lengths, before reading a byte.**

The protocol-14 bodies are 77 and 72 bits. A `BitVec3Coord` is three presence bits plus its axes;
an axis is 22 bits with a fraction and 17 without. So a full vector is 69 bits and one bare axis
makes it 64 — leaving **exactly 8 bits of header either way**. The modern widths, 118 and 113,
show the same five-bit step, which is what says the two eras share a vector encoding and differ
only ahead of it. The leading byte then reads 36, 40, 50, 44 across the demo. Those are damage
values.

Two general lessons, both now standing rules:

- **A constant body length falsifies any layout with optional fields.** Every protocol-14 body was
  77 bits until the last one.
- **The gap between two observed lengths names the optional field.** 118 vs 113 and 77 vs 72 are
  the same gap, and noticing that is what connected the eras.

### The wrong hypothesis, kept

RISKS B26 carried a standing guess: TF2 had inherited HL2's `Damage`, whose reader takes a byte of
armour, a byte of damage, a long, and a vector — "48 bits before the vector where the modern form
has 49", so the two would sit one bit apart and a wrong guess would yield a plausible position.

Reading the file rather than recalling it killed that instantly. HL2's message sends no vector at
all — three raw `WRITE_FLOAT`s — and is a fixed **144 bits**. It was never a candidate for a
77-bit body, and one subtraction says so. The guess was the cheap part; skipping the verification
is what would have cost.

### The second defect, which is the general one

The layout was wrong, but so was the check that should have caught it. The decoder accepted
`BitsRead <= bodyBits`, and the modern layout fits *under* a 77-bit body. Result: 20 of the 2008
demo's 24 messages reported invented fields — `damage=16164`, against a game whose largest single
hit is about 450 — and the other 4 overran and were refused.

**These bodies end mid-byte, which proves the stated length is exact rather than padded.** The
check is `==`. A lenient bound does not tolerate rounding; it accepts every layout short enough.

### Verification

Three decoders that share no code agree at tick 280 of the protocol-14 demo:

| source | value |
|---|---|
| camera, from the container's `democmdinfo` prologue | (-1012.4, 6068.7, -398.5) |
| explosion, from `svc_Sounds` | (-1008, 6064, -352) |
| damage origin, from this layout | (-1061.5, 6127.0, -355.0) |

Corpus-wide the damage origin sits a median 57 units from the camera at protocol 14 and 21–57 at
later eras, with nothing beyond 140 — self-damage from a rocket jump puts the origin on the
player. Before the fix, the protocol-14 demo produced no complete vector at all.

The boundary is measured at **11, 14 and 15**. Protocol 11's evidence had to be manufactured: the
committed protocol-11 demos contain no `Damage` message because nobody was hurt in them. A
soldier rocket-jumping beside a resupply cabinet for 52 seconds produced 43 of them, all on the
old layout at the same 77 and 72 bits. **A period client that runs can be made to emit any message
on demand** — for those eras, a missing message is a recording task, not a search.

## The `Damage` long is live data with a dead consumer

The game reads and discards it, so it is tempting to call it padding. It is
`info.GetDamageType()` — the real `DMG_` flags. TF2's HUD stopped using it; **HL2's used it** to
choose which damage icon to draw. Inherited message, abandoned reader, server still sending.

It cross-checks the length independently. The writer omits the position when the damage is
`DMG_DROWN | DMG_FALL | DMG_BURN`, and our 49-bit no-vector bodies decode to `bits=32`, which is
`DMG_FALL`. The flag field and the message length agree without either being derived from the
other.

**The opposite case exists too.** `ResetHUD`'s client reader takes nothing at all, yet the message
is 8 bits: `player.cpp` writes `WRITE_BYTE( 0 )`, a literal placeholder. So "the game ignores it"
tells you nothing about whether the field carries information — you have to read the writer. See
[08-method.md](08-method.md).

## The batch transcribed from the SDK

Seven layouts added 2026-08-11, each read from Valve's client and each **predicting a width before
any body was read**:

| message | layout | predicted | corpus |
|---|---|---|---|
| `Fade` | 3 shorts + 4 bytes | 80 | all 20 are 80 |
| `Shake` | byte + 3 floats | 104 | all 6 are 104 |
| `Rumble` | 3 bytes | 24 | all 59 are 24 |
| `ResetHUD` | placeholder byte | 8 | all 28 are 8 |
| `VGUIMenu` | string, byte, byte, N string pairs | varies | 42 exact |
| `PlayerStatsUpdate` | byte, byte, long, 32 per set bit | 48 + 32n | 112…272, all fit |
| `MapStatsUpdate` | long id, long, 32 per set bit | 64 + 32n | all 5 are 96 |

Prediction before measurement is what makes these transcriptions rather than curve fits.

### `PlayerStatsUpdate` can only ever send 32 of its 44 stats

The set-bit field is 32 bits wide; `TFStatType_t` runs to 44. Bit 31 selects stat 32, so stats 33
through 44 are **unreachable through this message**, and Valve's own `iStat <= TFSTAT_LAST` guard
is dead code in this build.

Found by writing a test asserting the opposite — that a bit past the table is refused — and having
it fail. The guard is not dead in every build: it bites when the table is *shorter* than 32, which
is what an older era looks like.

### Valve enforces exact consumption too

`CTFStatPanel::MsgFunc_PlayerStatsUpdate` checks `0 == msg.GetNumBytesLeft()` and bails, with the
comment: rather than risk polluting player stats with garbage. The same rule this project arrived
at independently, for the same reason.

## What is still opaque

3,920 bits across 11 types, all small and all modern:

| message | count | opaque bits |
|---|---|---|
| `PlayerTauntSoundLoopStart` | 10 | 1,768 |
| `CheapBreakModel` | 15 | 1,265 |
| `BreakModel` | 4 | 466 |
| `PlayerLoadoutUpdated` | 3 | 96 |
| `PlayerShieldBlocked` | 1 | 85 |
| `PlayerTauntSoundLoopEnd` | 10 | 80 |
| `BreakModelRocketDud`, `SpawnFlyingBird` | 2 each | 64 each |
| `AchievementEvent` | 1 | 16 |
| `MVMResetPlayerStats`, `PlayerGodRayEffect` | 1 each | 8 each |

`EntityMessage` is a separate and harder case: its body is defined by the *receiving entity's
class*, so there is no generic layout to read at all.
