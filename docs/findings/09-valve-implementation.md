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
| `mathlib`, format headers (`bspfile.h`, `studio.h`, `coordsize.h`) | The `.dem` container **code** |
| Map/model compilers (`vbsp`, `vrad`, `studiomdl`) | SourceTV's relay implementation |
| **`materialsystem/stdshaders` — 1,192 files of shader source** | the material system's own runtime |
| **`public/demofile/demoformat.h` — the header struct and `dem_*` list** | |
| **`public/haptics` — user-message registrations, with sizes** | |

So: **user message layouts are readable** (they live in game code), **bit-level primitives are
readable** (`tier1`), and **the entity-delta engine is not**. That split explains why this project's
user-message work is transcription while its `svc_PacketEntities` work had to be inferred.

> **Amended 2026-08-16, after five wrong absence claims traced back to this kind of summary.**
>
> The right-hand column was too coarse in three places, and each cost real work:
>
> - **`materialsystem` was listed as absent.** `materialsystem/stdshaders` holds **1,192 files** of
>   published shader source — enough to settle `$modblend` and to derive the 489-parameter
>   conformance denominator. Only the runtime is missing.
> - **"The `.dem` container code"** is right about the code and was read as covering the format.
>   `public/demofile/demoformat.h` declares `demoheader_t`, `DEMO_HEADER_ID` and every `dem_*`
>   command.
> - **Nothing pointed at `public/`.** The haptics user messages live there, and a search scoped to
>   TF2's game directory concluded the SDK said nothing about them.
>
> **A "what is not available" table is exactly the artefact that stops people looking**, so its rows
> have to be as narrow as the evidence. "The material system's runtime" and "`materialsystem`" differ
> by 1,192 files. See `an-empty-search-needs-a-control`.

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

## `VoiceMask` grew twice, and the size is a dated proxy for `MAX_PLAYERS`

Read from the registration calls in the shipped clients (see [05](05-user-messages.md)). Valve
registers each user message with a byte size, and `VoiceMask` writes `VOICE_MAX_PLAYERS_DW`
dword *pairs* — an audible mask and a server-banned mask — followed by one byte for the server
mod enable flag. So `size = 8 × dwords + 1`, and the size inverts to a player ceiling:

| build | registered size | dword pairs | implied `VOICE_MAX_PLAYERS` |
|---|---|---|---|
| 2007 launch, 2008 | **9** | 1 | 32 |
| 2009 | **17** | 2 | 64 |
| 2011, 2013, 2026 | **33** | 4 | 128 (101 rounds to 4) |

The 2007 **client and server** DLLs agree on 9 independently, which is the control — two
separately compiled binaries from one build.

Two things worth keeping. First, this is a Valve internal constant dated by measurement, which is
the sort of thing no changelog records. Second, it is a live decoding hazard: this project sizes
`VoiceMask` at 33 bytes, so a launch-era one is a quarter of the expected width. It fails safely
only because the reader demands *exact* consumption — under a `<=` check it would have read
sixteen fabricated dwords of mute state and reported them as fact. That rule was adopted for
`Damage` at protocol 14 and paid off again here, on a message and an era nobody was looking at.

## What is *not* knowable from the SDK

Worth listing so the next investigation does not start here:

- The container format. No `.dem` code ships.
- `svc_PacketEntities` delta semantics, the entity baseline mechanism, the deletion list.
- SendTable **flattening order** — this project's version of it was wrong and was corrected by
  differential comparison against `demostf/parser`, not by reading anything.
- SourceTV's relay behaviour, and therefore how far a relayed recording may diverge from what a
  player saw.

**One item came off this list on 2026-08-11: the pre-2013 user message tables.** They are not in
any SDK — the 2009 SDK ships no TF2 game code, and the sdk2013 drop describes a build years later
than its name. They are, however, plainly readable in every shipped `client.dll`, because
`usermessages->Register("Name", size)` compiles to a push of the size then a push of the name, so
the table is a literal sequence in `.text`. Six eras were read that way, and the result named
every previously-unnamed id in the corpus.

**The general point: "not in the source" is not "not knowable".** The distinction that matters is
between something Valve never wrote down and something Valve never *published*. The second is
still sitting in the binary, in order, with its constants attached.

## `entitygroundcontact` is guarded by different macros on each side of the wire (2026-08-11)

Found while reading `game/shared/usercmd.cpp` for the `dem_usercmd` layout ([01](01-container.md)).
The last field of a user command is an optional list of ground contacts, and it is conditional:

```cpp
// in WriteUsercmd
#if defined( HL2_CLIENT_DLL )
	if ( to->entitygroundcontact.Count() != 0 ) { ... }
#endif

// in ReadUsercmd
#if defined( HL2_DLL )
	if ( buf->ReadOneBit() ) { ... }
#endif
```

**Two halves of one wire format, gated on two different macros.** `HL2_CLIENT_DLL` and `HL2_DLL`
are the client and server sides of the same game, so any configuration defining exactly one of them
writes a command the other cannot read — and it fails silently, because the desynchronisation is a
single presence bit at the end of a message with no terminator and no checksum.

TF2 defines neither, so its commands simply stop after the mouse deltas and this never fires. That
is *why* it survives: the bug is unreachable in the configuration anyone ships, which is exactly
the condition under which a mismatch like this never gets found.

Same category as the vestigial protocol floor in [01](01-container.md) and the misspelled format
string beside it: **the parts of Valve's code nobody executes are where the interesting things
are still sitting.**

## `random_seed` is derived, not transmitted (2026-08-11)

A smaller one from the same file, and worth stating because it looks like a missing field. `CUserCmd`
has a `random_seed` used to keep client and server prediction of spread and recoil in step, and it
never goes on the wire. `ReadUsercmd` computes it:

```cpp
move->random_seed = MD5_PseudoRandom( move->command_number ) & 0x7fffffff;
```

So it is a pure function of a field already present. A parser reporting it is not reading anything
out of the file — which is why this project does not report it, rather than deriving a number and
presenting it alongside measured ones. *Sourced.*

## `model_player_per_class` is two different keys wearing one name

A cosmetic's model is looked up per class, and the schema block that says so has **two forms**. The
obvious one is a map:

```
"model_player_per_class"
{
    "scout" "models/player/items/scout/hat_scout.mdl"
    "spy"   "models/player/items/spy/hat_spy.mdl"
}
```

The other is a single pattern, and it is by far the more common — **5,518 occurrences in the shipped
`items_game.txt` against a few hundred of the map form**:

```
"model_player_per_class"
{
    "basename"	"models/player/items/%s/%s_cap.mdl"
}
```

`InitPerClassStringArray` (`tf_item_schema.cpp:489`) resolves both at load, per class, explicit entry
first:

```cpp
CUtlString strClassString( pPerClassData->GetString( ClassUsabilityStrings[i], NULL ) );
if ( !strClassString.IsEmpty() )   ... use it
else if ( pszBaseName )            fmtStr.sprintf( pszBaseName, name, name, name );
```

*Read from published source.*

**Three details that are not guessable from the schema file**, and each one is the difference
between a model and nothing:

- **The name is supplied three times to one `sprintf`.** A pattern may therefore carry up to three
  `%s`; the shipped file uses one or two. A fourth would read adjacent stack in the engine.
- **The demoman is `demo`, and Valve's source apologises for it in a comment**: *"the vast majority
  of his models are whatever_demo.mdl. The RIGHT fix would be to … change all the model and content
  files"*, followed by `if ( i == TF_CLASS_DEMOMAN ) fmtStr.sprintf( pszBaseName, "demo", "demo",
  "demo" )` (`:519`). The schema says `demoman` everywhere else, including in the same block's
  explicit entries. A faithful substitution that used the schema's own word names a file that does
  not exist for every demoman cosmetic in the game, and a missing file looks exactly like a missing
  entry.
- **Slot zero is a copy, not an absence.** Each iteration ends with
  `if ( outputArray[0] == NULL ) outputArray[0] = outputArray[i]` (`:541`), so `TF_CLASS_UNDEFINED`
  answers with whichever class resolved first, and `CEconItemView::GetPlayerDisplayModel` returns it
  before it ever reaches `model_player` (`econ_item_view.cpp:962`). An item with a per-class block
  and no base model is therefore *still* drawable when the class is unknown — which matters here,
  because a prop whose owner is not a player the current moment knows about has no class to offer.

**What it cost us.** This project read the map form and stored `basename` as a class name, so it sat
in the table under a class nobody plays and was never looked up. Measured on a real match afterwards:
**48 of 252 distinct (item, class) pairs resolved to no model at all, and every one was a cosmetic**.
After the fix, two — both item 241, the Duel MiniGame, an action-slot tool whose `model_player` is
literally `""` in the schema and which has no model to draw. *Measured on the corpus.*

**The shape of the mistake is worth more than the mistake.** One key, two forms, one of them read —
and the half that was missing failed silently, because a cosmetic that names no model and a cosmetic
whose name was never parsed are the same empty string by the time anything downstream sees it. The
same shape has now appeared three times in a fortnight: a weapon's model read from the wire but not
from its item, a disguise's body honoured but not its gear, and this.

## `CL_CopyNewEntity` — what an entity entering the PVS is decoded against

*Evidence class: read from a decompilation of `engine.dll` (TF2, x64, May 2026 build). The SDK ships
no engine networking, so this is the only source; the function names come from the binary's own
assert strings, which Valve left in.*

The client has **three** paths for an entity in `svc_PacketEntities`, and their names survive in the
binary: `CL_CopyNewEntity` (entering the visible set), `CL_CopyExistingEntity` (an ordinary delta),
and `CL_PreserveExistingEntity`. Only the middle one is a delta against what the client is holding.

`CL_CopyNewEntity` chooses a buffer to decode FROM — the binary names it `CL_CopyNewEntity->fromBuf`
— and never uses the entity's current state:

```c
if ( !asDelta
     || (stored = LookupEntityBaseline( table, baselineIndex, entityIndex )) == NULL
     || stored->classId != thisClass )
{
    if ( !GetClassBaseline( classId, &data, &bytes ) )
        Error( "CL_CopyNewEntity: GetClassBaseline(%d) failed." );   // fatal
    bits = bytes * 8;
}
else
{
    data = stored->data;
    bits = stored->bits & 0x7fffffff;
}
```

**Three things follow, and the third is the one that resolves an argument this project had with
itself.**

1. **An entering entity is REPLACED, not merged into.** Whatever the baseline and the update do not
   between them state is at the baseline's value, not at what the reader last accumulated.
2. **The per-entity baseline is preferred, and it is checked against the class.** That is
   `EntityBaselineSlots.For(slot, entityIndex, classId, isDelta)` line for line — including the
   delta condition, which exists because a full snapshot is the server saying "forget what you had".
3. **The class baseline is a FALLBACK, and missing one is fatal only on that path.** So a class with
   no `instancebaseline` entry is not a contradiction: its entities can enter for ever, provided the
   snapshot is a delta and the per-entity slot holds them. Measured on `tf2-2026-pub-pov-clean`,
   which the game plays: 363 classes, 68 with a class baseline, and `CWeaponMedigun` and
   `CTFBonesaw` among the ones without.

`GetClassBaseline` itself is `GetDynamicBaseline` in the binary. It formats the class id as a
decimal string, looks that up in the `instancebaseline` table, and on a miss dumps every entry to
`DevMsg` before `Error( "GetDynamicBaseline: FindStringIndex(%s-%s) failed." )`. It does **not**
synthesise an empty baseline — which was the reading this project needed to rule out, because under
it an absent entry would have meant "all defaults" and a very different fix.

The class id as the entry's TEXT confirms `BaselineBuilder`'s rule from the other side: the engine
writes `snprintf( name, 64, "%d", classId )` and looks it up by that name.

## A virtual's overrides are where the behaviour is — and some of them are dead

**Reading an engine function to its closing brace can tell you nothing about what the game does.**
`C_BaseAnimating::StandardBlendingRules` is a complete, sensible pose pipeline; everything TF2's
minigun does happens in an override that runs *after* `BaseClass::` returns
(`tf_weapon_minigun.cpp:1068`). Seven live overrides exist. Two of them exist purely to turn a
barrel bone the animation does not turn.

**Three of the seven are dead, in three different ways**, and each reads as a feature from the call
site:

| override | why it does nothing |
|---|---|
| `C_AI_BaseHumanoid` (`c_ai_basehumanoid.cpp:77`) | the **whole file** is wrapped `#if 0` … `#endif` (lines 13, 169) |
| `C_BaseFlex` (`c_baseflex.cpp:227`) | its entire body after `BaseClass::` is inside `#ifdef HL2_CLIENT_DLL` |
| `C_NPC_Hydra` (`c_npc_hydra.cpp:148`) | four parameters against the base's five — it does not override anything |

**`ChildLayerBlend` is the sharpest of them**, because it is called unconditionally from
`StandardBlendingRules` (`c_baseanimating.cpp:2005`) and its body opens with a bare `return;`
(`:1909`). Thirty-five lines of bone-merge follow, unreachable. A reader who quoted the call and not
the body would implement a whole child-merge pass the engine has never run.

## Valve's commented-out alternative uses a different axis from its live code

`CTFMinigun::StandardBlendingRules` sets the barrel bone with
`AngleQuaternion( RadianEuler( 0, 0, m_flBarrelAngle ), q[m_iBarrelBone] )` — the third component,
which `AngleQuaternion` reads into the YAW terms (`mathlib_base.cpp:2039`).

Directly above it sits a commented-out block, guarded by *"Weapon happens to be aligned to (0,0,0) /
If that changes, use this code block instead"*:

```cpp
RadianEuler a;
QuaternionAngles( q[iBarrelBone], a );
a.x = m_flBarrelAngle;
AngleQuaternion( a, q[iBarrelBone] );
```

**`a.x` is ROLL.** The live code and its own documented alternative rotate about different axes, so
the comment is a sketch of the general shape rather than an equivalent — and a reader who took the
commented version as authoritative, on the reasonable grounds that it is the more general one, would
spin the barrel about the wrong axis and have Valve's own text to point at.

**The axis costs two hops to establish at all**, which is why this is worth writing down:
`RadianEuler`'s members are `x, y, z` in declaration order (`vector.h:1692`), and only
`AngleQuaternion`'s body says which of those is yaw. Its X360 branch carries the warning outright —
*"the ordering here is different … because p, y, r are not in the same locations in QAngle +
RadianEuler"*.

### Correction, same day: the ROLL is the v_model era, and both live paths use yaw

The section above called Valve's commented-out `a.x` "a sketch of the general shape". That is
incomplete, and reading the fourth override settles it: **`a.x` is what the OLD v_model path uses**,
and it is still in the tree at `tf_viewmodel.cpp:333`.

Five paths write a weapon barrel, and only four are live:

| path | bone | axis | write style | bone mask |
|---|---|---|---|---|
| minigun, world (`tf_weapon_minigun.cpp:1068`) | `barrel` | **z** | flat assign | no |
| minigun, viewmodel attachment (`:1343`) | `barrel` | **z** | read-modify-write | **yes** |
| minigun, viewmodel arms (`tf_viewmodel.cpp:313`) | `v_minigun_barrel` | **x** | read-modify-write | yes |
| grenade launcher, world (`tf_weapon_grenadelauncher.cpp:610`) | `procedural_chamber` | **z** | flat assign | no |
| grenade launcher, viewmodel attachment (`:683`) | `procedural_chamber` | **z** | read-modify-write | yes |

**The third row is dead on a modern install**, and measuring said so rather than reasoning:
`CTFViewModel::StandardBlendingRules` poses the VIEWMODEL entity, which every demo checked resolves
to `c_*_arms.mdl` — `c_soldier_arms`, `c_sniper_arms`. Those carry no bone called
`v_minigun_barrel`; the one that does is `v_minigun_heavy.mdl`, still shipped, with the bone at
index 2 of 18. The weapon itself is a separate `C_ViewmodelAttachmentModel` posed through
`ViewModelAttachmentBlending`, which is row two.

**So the axis is `z` on every live path**, and the commented-out block in the world file is a paste
from the era when it was `x`. A reader who took it as the more general form — which is exactly what
its guarding comment invites — would inherit a dead convention.

**Two write styles, split by which model is being posed.** The world paths assign the whole
quaternion and check no mask; the viewmodel-attachment paths read the existing angles, replace one
component, and are wrapped in `if ( hdr->boneFlags( iBarrelBone ) & boneMask )`. The two agree only
while the animation leaves the other two components at zero on that bone.

## A player's animation plays at rate 1, and the function that would scale it is dead

`CMultiPlayerAnimState::CalcMovementPlaybackRate` (`multiplayer_animstate.cpp:1070`) computes
`clamp( speed / maxGroundSpeed, 0.01, 10 )` — the obvious way to keep a run cycle in step with how
fast a player is actually moving. **Nothing in TF2's hierarchy calls it.**

- **`CMultiPlayerAnimState` is `DECLARE_CLASS_NOBASE`** (`multiplayer_animstate.h:172`). It does not
  derive from `CBasePlayerAnimState`, so the four call sites in `base_playeranimstate.cpp` (`:452`,
  `:550`, `:652`, `:683`) belong to a separate hierarchy that TF2 never instantiates.
  `CTFPlayerAnimState : public CMultiPlayerAnimState` (`tf_playeranimstate.h:25`) inherits the dead
  copy, not the live one.
- **Zero call sites** across `game/shared/Multiplayer`, `game/shared/tf` and `game/client/tf`.
- **Its own body carries a commented-out call** to `GetInterpolatedGroundSpeed()` (`:1077`), whose
  only other appearance is inside `DebugShowAnimState` (`:2069`). The helper exists to print.

**What TF2 does instead is set the rate to one, explicitly**, in both animstates and only for the
local player: `GetBasePlayer()->SetPlaybackRate( 1.0f )` (`multiplayer_animstate.cpp:1366`,
`tf_playeranimstate.cpp:506`). For a remote player nothing sets it at all. The speed matching is
done by the movement blend's pose parameters, not by the clock.

**Measured on the wire, which is what makes this actionable rather than interesting:** across 60,000
expanded snapshots of `tf2-2026-pub-pov-cheater`, **no `CTFPlayer` entity sends `m_flPlaybackRate`
at all**. The classes that do are `CDynamicProp` (4,508 sends, mostly 0 — a stopped prop),
`CBaseAnimating` (1,171 at 1), and `CTFViewModel` — which sends **1.313**, a genuinely non-unit
rate. That is the minigun's spin-up scaling: `SetPlaybackRate( TF_MINIGUN_SPINUP_TIME /
flSpinTimeMultiplier )` (`tf_weapon_minigun.cpp:266`), and it is read through
`ViewmodelPlaybackRate` already.

So a player defaulting to rate 1 is not a gap: it is what the engine leaves it at, confirmed from
both directions.
