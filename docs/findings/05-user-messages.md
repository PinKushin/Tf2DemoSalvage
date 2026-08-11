# 05 — User messages

`svc_UserMessage` is the game's own extension point: the engine carries a type byte, a length, and
an opaque body, and the *game DLL* decides what any of it means. Nothing on the wire names the
message. That makes this layer the one where a parser is most likely to produce confident nonsense.

Status as of 2026-08-11: **395 of 445 user messages in the corpus decode, up from 217.** Opaque
bits fell from 14,672 to 3,920.

## The id table is registration order — stable at the head, shifting at the tail

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

Eighteen years, and **through id 28** the table does not shift. Widths agree too, which is a second
independent check — a shifted id would land a 24-bit Rumble body on a name expecting something
else.

### Above id 28 it shifts twice, and the width is what reveals it

**This was initially recorded as "the table has not moved", and that was an overclaim** — the
histogram above only covered the ids the older demos actually contain, all of which are ≤ 28.
Extending it to the higher ids shows the opposite.

`CheapBreakModel` is a short and a coordinate vector, so its full form is **85 bits**, and that
width is unmistakable:

| demo | protocol | id carrying an 85-bit body |
|---|---|---|
| 2009 | 15 | **40** |
| 2011, both POV and SourceTV | 16 | **41** |
| every protocol-24 file | 24 | **42** |

Corroborated by a second disagreement in the same files: id 52 carries a 32-bit body in 2011, where
protocol 24 puts a 229-bit `SpawnFlyingBird` there. So **two messages were inserted rather than
appended between 2009 and 2013**, and every id after them moved.

The consequence was a live bug: this project reported the 2009 demo's id 40 as
`PlayerShieldBlocked` — which the registration table declares as **2 bytes** against an observed 85
bits. A wrong name on a correctly decoded message, which is exactly the failure this layer is most
prone to. Names above 28 are now withheld below protocol 24 and the id is reported by number.

**A distinctive body width is a fingerprint for its own id.** That is what made the shift visible
at all, and it is the general technique: where a message's length is unusual, it identifies itself
regardless of what the name table claims.

### The registration table declares sizes, and they are a free cross-check

`game/shared/tf/tf_usermessages.cpp` registers each message with a byte size, or `-1` for variable:

```cpp
usermessages->Register( "Geiger", 1 );
usermessages->Register( "Shake", 13 );
usermessages->Register( "Fade", 10 );
usermessages->Register( "Rumble", 3 );
usermessages->Register( "PlayerShieldBlocked", 2 );
usermessages->Register( "Damage", -1 );
```

Every fixed size matches what this project decodes — Geiger 8 bits, Shake 104, Fade 80, Rumble 24.
More usefully, **a declared size that contradicts an observed width proves a misalignment** without
needing to understand the layout at all. That is how the id shift was confirmed rather than merely
suspected.

**Why this matters beyond bookkeeping:** when protocol 14's `Damage` decoded to garbage, a shifted
id was the first suspect. One histogram eliminated it and pointed at the layout instead. *Check
alignment before suspecting a layout* — it is one measurement and it halves the search. The same
instrument later found a shift that was real.

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

## Three different kinds of era change, at the same layer

Worth separating, because they need different defences and the first two were initially confused
for each other:

| Kind | Example | What moves | Detected by |
|---|---|---|---|
| **Layout changes, id fixed** | `Damage` at protocol 14/15 | the body's shape | body width arithmetic |
| **Ids change, layout fixed** | `CheapBreakModel` 40 → 41 → 42 | which id carries it | a distinctive width acting as a fingerprint |
| **Length grows, id and prefix fixed** | `AchievementEvent` | fields appended | two exact widths, compatible prefix |

`AchievementEvent` is the third: the modern writer sends `WRITE_SHORT( iAchievement ); WRITE_SHORT(
iCount );` for 32 bits, and the 2009 demo's is **16** — the achievement only, before the count
existed. Both are accepted, which is not a fallback dressed up: 16 and 32 are exact, they are the
only two forms the writer has had, and the achievement occupies the same leading short in both.
Keying it on protocol would need a boundary the corpus cannot supply.

## `WRITE_ANGLES` is `WRITE_VEC3COORD`

`bf_write::WriteBitAngles` copies the angle triple into a `Vector` and calls `WriteBitVec3Coord`,
carrying a standing fix-me comment from Valve saying exactly that.

**So an orientation is encoded as a position.** Three presence bits and coordinate axes; an angle
costs precisely what a coordinate costs. There is no separate angle encoding in this layer to look
for, which matters whenever a width involving angles is being derived — `BreakModel` is a short, a
position, an *orientation encoded as a position*, and a short.

## Coordinate widths depend on the value, not just the field

An axis is 22 bits with a fraction and 17 without, so **the same field encodes to different widths
for different values**. A position at whole-numbered coordinates is 54 bits where a fractional one
is 69.

This bit the test suite rather than the parser: a `SpawnFlyingBird` fixture built at (10, 20, 30)
came to 214 bits where every bird in the corpus is 229, because whole numbers skip their fractional
part. A fixture has to carry fractions to be stating the same claim the corpus does. Recorded
because it is a standing hazard for any width predicted from a layout — **the layout gives a range
of widths, not one**, unless the values are known.

## What is still opaque

**435 of 445 decode. 479 bits remain**, and every one of them is a *naming* problem rather than a
layout problem:

| message | count | opaque bits | why |
|---|---|---|---|
| ids `#40`, `#41`, `#44`, `#52` | 7 | 383 | pre-2013 ids the name table cannot label — withheld deliberately |
| `PlayerLoadoutUpdated` | 3 | 96 | see below |

The unnamed ids are the era-shifted ones, and their bodies are readable: `#40` and `#41` are 85-bit
`CheapBreakModel` shapes. What is missing is not the layout but the *identity*, and naming them
from their shape would be a guess where the whole point of the gate is to stop guessing.

### The id table can shift within one protocol number

`PlayerLoadoutUpdated`'s writer is a single byte — `WRITE_BYTE( entindex() )` — so 8 bits. The
March 2013 demo carries **32 bits** at that id, and it is protocol 24, the same protocol the name
table was transcribed for.

**So the table is a property of the game DLL, not of the network protocol.** A protocol number
bumps when the *engine's* wire format changes; the user message list lives in `tf_usermessages.cpp`
and can be reordered by any game update without the protocol moving at all. Protocol 24 spans
thirteen years ([06](06-protocol-eras.md)) — far too long to assume one registration order.

Evidence is thin — one demo, three messages, and no other protocol-24 file containing id 69 to
compare against — so the *cause* stays open. The *symptom* does not: a name is now withheld
whenever a known layout refuses the body.

### A name is a claim, and a refusing layout is evidence against it

Generalised from the two era fixes, and it needs no protocol boundary at all:

- The id is **always** reported.
- The name is reported **unless** this project knows the message's layout and the body does not fit
  it. Then the id stands alone.
- A message with **no** layout keeps its name, because nothing contradicts it. Withholding there
  would discard information rather than avoid a false claim — the rule is evidence-driven, not
  precautionary.

Measured across the corpus, this fires on exactly five ids — `#40`, `#41`, `#44`, `#52`, `#69` —
and touches nothing else. 435 messages keep both their name and their fields. That it does not
over-fire is the point: a rule that withheld names broadly would satisfy the same tests while
destroying the table's whole purpose.

It also caught its own motivating case without being told about it. `PlayerLoadoutUpdated` was
named right up until the rule existed; now the March 2013 demo reports `#69`, which is the honest
statement — *something* is at that id and it is not a one-byte message.

## `svc_EntityMessage` is not generic, but it is a closed set

Recorded here because it was initially written off as undecodable in principle, and that was wrong.

Its body is handled by the *receiving entity's class* — `ReceiveMessage( int classID, bf_read
&msg )` — so it is not schema-driven the way entity state is. But the SDK contains only **18
`ReceiveMessage` implementations in total**, most of them HL2 and episodic, and
`game/client/tf/` overrides it **not at all**. TF2's entity messages are therefore the inherited
set: `C_BaseEntity`, `C_BasePlayer`, `C_RopeKeyframe`, `C_Tesla`, `C_EnvScreenEffect`.

Every implementation has the same opening move — read a **message-type byte**, then switch:

```cpp
int messageType = msg.ReadByte();
switch( messageType )
{
    case BASEENTITY_MSG_REMOVE_DECALS:  RemoveAllDecals();  break;   // == 1
}
```

And the corpus agrees: every `svc_EntityMessage` in it is **8 bits with class id 1** — one type
byte and no payload, which is `RemoveAllDecals` and nothing else.

So the honest statement is not "impossible" but "**not generic, and small**". The class id on the
wire selects the handler, the first byte selects the case, and the set of handlers is enumerable
from published source.
