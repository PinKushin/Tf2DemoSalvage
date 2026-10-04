# 53 — A taunt names its sequence inside a compiled scene, under an actor

**Subject:** `scenes/scenes.image`, the compiled binary VCD inside it, and why a first reader of it
returns nothing for every taunt in the game. B351.

## What the wire carries, and what it does not

A taunt reaches the client as a **filename and nothing else**. `DT_SceneEntity` sends
`m_nSceneStringIndex` into the `"Scenes"` network string table (`gameinterface.cpp:1448`). The
sequence the player's model actually plays is a **string inside the compiled scene** —
`info->m_nSequence = LookupSequence( event->GetParameters() )` (`c_tf_player.cpp:9456`) — resolved
against the player's own model, not against anything in the scene.

The item cannot substitute for the filename: the server picks the scene at random from the item's
list, `int iScene = RandomInt( 0, pTauntData->GetIntroSceneCount( iClass ) - 1 );`
(`tf_player.cpp:17391`). Read-from-source.

## Both formats are published; the parser is not, and that does not matter

`src/public/scenefilecache/SceneImageFile.h` declares the container in full — `MAKEID('V','S','I','F')`,
version 2, a five-int header, a table of absolute string offsets, then a CRC-sorted directory of
`SceneImageEntry_t { crcFilename, nDataOffset, nDataLength, nSceneSummaryOffset }`.
`src/game/shared/choreoscene.cpp:3702` declares the compiled VCD down to the byte. The runtime reader
ships only in `scenefilecache.dll`, which never came into it.

Measured on the shipped archive: 3,679,138 bytes, 9,939 scenes, 14,880 pooled strings, directory at
381,348. No loose `.vcd` ships anywhere in the install or inside any of the nine `*_dir.vpk` — checked
with a known-present extension as the control.

## The name is normalised before it is hashed, and the digest is little-endian

`SceneImageEntry_t`'s own comment says the filename is *"expected to be normalized as
scenes\???.vcd"*. So: forward slashes to back, a `scenes\` prefix if absent, a `.vcd` suffix if
absent, `V_strlower` (byte-wise ASCII, not culture-aware), hashed **without** the terminator
(`sceneimage.cpp:439`). Valve's CRC32 is the standard one; the only thing left to decide was which
end the digest is read from, and the answer is little-endian:

```
'scenes/player/scout/low/taunt_hi5_start.vcd': little 0x319001CF FOUND, big 0xCF019031 no
```

Measured. Arriving at that took the shipped data — four invented scene names all came back empty,
which says nothing at all while the names are unverified. `items_game.txt` carries
`custom_taunt_scene_per_class` with 730 real paths in it, and it is the source that gets forgotten
because it is not code.

## The three divergences, and why the first is the one that hides everything

### 1. The top-level event list holds only the events with NO actor

This is the one that makes a careful reader return nothing for every taunt in the game.

```cpp
for ( i = 0 ; i < m_Events.Size(); i++ )
{
    CChoreoEvent *e = m_Events[ i ];
    if ( e->GetActor() )
        continue;

    eventList.AddToTail( e );
}
...
buf.PutUnsignedChar( c );
```

`choreoscene.cpp:3711`. Everything belonging to an actor is written **after** that list, as
actor → channel → event (`choreoactor.cpp:243`, `choreochannel.cpp:522`) — a pooled name, a count, the
children, then an active flag at each level. A taunt's `GESTURE` belongs to an actor, so a reader that
stops at the top level finds the actor-less events and gives up.

For `scenes/player/scout/low/taunt_hi5_start.vcd` those are exactly one event, and it decodes cleanly:

```
62 76 63 64  04  CE B7 39 4B  01   0C    22 01   AC 1C 22 40   00 00 80 BF
'bvcd'       v4  text CRC     n=1  LOOP  name    start 2.533   end -1.0
```

A `LOOP` at 2.533s with no end — the high-five idle, waiting for a partner. Correct data, correct
parse, wrong place to be looking. The symptom is not a crash or a wrong sequence: it is silence, and
silence reads identically to "this scene has no gesture".

### 2. A `SPEAK` and a `LOOP` each write a trailer after the flex tracks

```cpp
if ( GetType() == LOOP )       buf.PutChar( GetLoopCount() );
if ( GetType() == SPEAK )    { buf.PutChar( cc type ); buf.PutShort( token ); buf.PutChar( flags ); }
```

`choreoevent.cpp:4213`. Events are written back to back with **no length prefix**, so missing either
trailer leaves the cursor one or four bytes inside the next event — whose first byte is then read as
its type. The failure is not local: it corrupts every event after it, and in a taunt that includes the
gesture.

### 3. A flex sample is seven bytes, and the ramp's samples really are five

```cpp
buf.PutFloat( s->time );
buf.PutUnsignedChar( v );
buf.PutUnsignedShort( s->GetCurveType() );      // choreoevent.cpp:4419
```

versus `CCurveData::SaveToBuffer` fifty lines earlier, which writes a float and a byte and stops
(`choreoevent.cpp:4362`). Two structures that look alike, in one file, differing by one field — and
the error grows with the sample count, so a track with five samples walks the cursor ten bytes off.

## What this cost, and what caught each part

The CRC lookup was right from the first attempt and stayed right, which is what made the failure hard:
`SequenceFor` returned null and every candidate explanation was plausible. Two controls separated them.

- **The search's control**: five CRCs read straight out of the directory, handed back to the binary
  search. `5 of 5` found says the search is not the problem, so an empty answer means the name is not
  what the archive calls that scene — a different fix entirely
  (`docs/memory/instrument-bugs-outnumber-decoder-bugs.md#an-empty-search-needs-a-control`).
- **`BodyFor`, a second method beside `SequenceFor`**, because one method returning null for three
  different causes cannot tell them apart. It printed `156 bytes, tag 'bvcd'` — so the name resolved,
  the entry decompressed, and the LZMA path was innocent. That left the event walk, and nothing else.

`docs/memory/instrument-bugs-outnumber-decoder-bugs.md` is about instruments lying; this is the other
half of the same rule. **A diagnostic that collapses three causes into one null is not lying — it is
refusing to answer**, and it costs the same.

## The 730 was the wrong denominator, and censusing the archive found two failures

730 of 730 taunt paths naming a sequence is 7.3% of the archive. A stride wrong for a kind of event
no taunt uses would pass every one of them, so the reader was run over all 9,939 scenes:

```
census: 9,937 of 9,939 scenes walked to the end, 2 ran out of bytes, 1,703 name a sequence
```

**1,703 naming a sequence is the expected shape** — most of the archive is speech, all `SPEAK` and
flex, with no animation to name. The two that do not finish are both the Engineer's jackhammer-rodeo
taunt, both drift exactly two bytes inside a 6.5 KB `EXPRESSION` event, and both still read their
sequence correctly because it sits at byte 17 and byte 47. Filed as **B376**, with what has and has
not been checked.

That is the whole argument for `docs/memory/the-denominator-decides-what-can-be-lost.md`: the taunt
paths were a real measurement of a real population, and they could not have found this.

## The wire half, and the vector that keyed itself differently

Reading the archive is only useful if a recording says which scene played. It does, and every piece
is present in a modern POV demo:

```
tf2-2026-pub-pov-clean.dem protocol 24, 363 classes
  class 114 'CSceneEntity' table 'DT_SceneEntity'
    DT_SceneEntity.m_nSceneStringIndex   m_hActorList.000 … .015
    DT_SceneEntity.m_bIsPlayingBack      m_hActorList.lengthproxy.lengthprop16
  -> 'Scenes': 4,431 entries
  1,824 scene playbacks; 37 of them name a taunt; 1,824 name an actor
  of the 23 distinct taunt names, 20 give a sequence
```

**The three that do not are `SandwichTaunt01/02/14.vcd`, and their events are `[5, 2]` — `SPEAK` and
`EXPRESSION`.** They are the Heavy's voice lines during the taunt, not the taunt. Asking for the event
TYPES is what separates "this scene has no animation" from "our walk failed to reach one", and without
it the same three lines read as a defect.

**`m_hActorList` came back empty on the first attempt, on every one of the 1,824.** The cause is a
real asymmetry in how this project keys properties, and it is not a defect on either side:

- `EntityStateTable` keys a property by its PATH only when the flattener marked it element-scoped, and
  that mark is set for a **datatable** member named `lengthproxy` or all digits
  (`SchemaFlattener.cs:237`).
- An `m_AnimOverlay` element IS a sub-table, so it keys as `…m_AnimOverlay.000.m_nSequence`.
- An `m_hActorList` element is a plain `EHANDLE`, so it keys FLAT as `_ST_m_hActorList_16.000` — while
  the length, which reaches its property through the `lengthproxy` sub-table, keys as
  `m_hActorList.lengthproxy.lengthprop16`.

Both halves of one vector, keyed two different ways — and correctly so, because the element's flat
name already carries its index, so the collision `ElementScoped` exists to prevent cannot happen here.
But a reader written by copying `AnimationLayers`, which is the obvious thing to do, matches on a
leading dot, finds the length, finds none of the elements, and reports every scene as having no actor.

**The tell was the ratio.** 1,824 playbacks and 0 actors is not a plausible fact about TF2, and a demo
trace settled it: `entity 323 ENTER class CSceneEntity { _LPT_m_hActorList_16.lengthprop16 1;
_ST_m_hActorList_16.000 1521686; }`. The data was there, in a spelling the reader could not see.

## What it looks like once it is wired

`cycle tf2-2026-pub-pov-clean 9 1074`, at the tick the wire says a soldier began `taunt_laugh`:

```
1074  POSED seq 150  gestures 1  layers 2
  W[seq6'PRIMARY_aimmatrix_idle':78of86 …
    seq288'taunt_laugh':0of86 f0+0/146 animdelta True seqdelta False post False]
```

**Sequence 288, advancing through 146 frames, as a second layer over the idle.** The taunt is not a
delta, unlike every other player gesture — `seqdelta False` — which is why it overrides the pose
rather than adding to it.

**That measurement first said `gestures 0`, and the probe was at fault rather than the code.**
`CycleProbe` built `new GameAppearance(content.Classes, null)` instead of calling
`DemoAppearance.Ensure`, so it loaded no weapon roles, no item schema and no scene archive — three
silent omissions that each look like the demo containing nothing. It now takes the viewer's own path.
*"A probe that skipped the resolution step the viewer runs"* is the first entry on this project's own
list of instrument faults, and this was that fault again.

## Two things Valve's own code does that are worth recording

**`case LOOP:` has no `break` and falls through into `case SPEAK:`** (`c_sceneentity.cpp:530`). It is
inert for a networked taunt, because the `SPEAK` body is guarded by `IsClientOnly()`, but it is real
and it is in shipping code.

**TF2 throws away the scene time the server sends it.** `m_flForceClientTime` arrives on the wire with
a receive proxy that calls `OnResetClientTime`, whose only statement is wrapped in `#ifndef
TF_CLIENT_DLL` — *"In TF2 we ignore this as the scene is played entirely client-side"*
(`c_sceneentity.cpp:70`). The scene's clock is `m_flCurrentTime += gpGlobals->frametime` from the
moment playback begins, so a viewer driving its own clock reproduces it exactly from one tick.
`SceneChoreography` therefore does not carry `m_flForceClientTime` at all: carrying it would invite a
consumer to honour a value the game discards.

## The scene runs on its own clock, and the clock loops

A press-and-hold taunt does not play once. `DispatchProcessLoop` reads the `LOOP` event's parameter as
a TIME and hands it to `SetCurrentTime`:

```cpp
float backtime = (float)atof( event->GetParameters() );
…
scene->LoopToTime( backtime );
SetCurrentTime( backtime, true );
```

`c_sceneentity.cpp:574`. So the same parameter field that names a sequence on a `GESTURE` is a clock
position on a `LOOP` — which is why it cannot be read without the event's type. Measured, a real one
is the string `"2.500000"`.

**The scene therefore cycles over `[backtime, loopStart]` for as long as the server keeps it playing**,
crossing the gesture event again each time, and `StartGestureSceneEvent` re-adds the layer. In closed
form that is folding the elapsed time into the window — which a seeking viewer can evaluate where the
engine's per-frame stepping cannot. `SceneTaunt.TimeAt` does exactly that.

**Four of the scenes one real match played loop**, all of them the Pyro's Skating Scorcher:

```
resolved plans: 21 stage a gesture, 4 loop
  LOOPS 'scenes/workshop/player/pyro/low/taunt_the_skating_scorcher_intro.vcd': [1.77, 6.20], 1 gesture(s)
  LOOPS 'scenes/workshop/player/pyro/low/taunt_the_skating_scorcher_trick1.vcd': [2.05, 6.49], 1 gesture(s)
```

and on the drawn skeleton the fold is visible: entity 7's `taunt_skating_scorcher_intro` is at frame
181 at tick 960 and frame 62 at tick 990. Without the fold, auto-kill removes the layer the moment the
sequence's cycle passes one, and a held taunt collapses back to the idle pose part-way through.

## Stopping a scene only ends the taunt if the scene loops

The obvious implementation — clear the gesture when `m_bIsPlayingBack` goes false — is wrong, and
Valve says why in a comment:

```cpp
// The ResetGestureSlot call will prevent people from doing running taunts (which they like to do),
// so let's only reset the gesture slot if the scene contains a loop (such as the high five pose).
```

`c_tf_player.cpp:9491`. `StopGestureSceneEvent` walks the scene's events for a `LOOP` and resets
`GESTURE_SLOT_VCD` only if it finds one. So a one-shot taunt whose scene the server has already stopped
plays out to its end, and a held pose ends the instant it is released. Both halves have their own test,
because the stop and the loop predict opposite answers from the same input.

**The rule needs the scene's contents, so the decision cannot be made where the stop is seen.** Core
records `StoppedSeconds` on the gesture; the layer holding the archive decides. That is the same split
as everything else here: the wire says what happened, the installed game says what it means.

## What is NOT established

- **That `taunt_highFiveStart` exists as a sequence on the class models.** `taunt_laugh` does — it
  resolved to sequence 288 on a real soldier, and `taunt_skating_scorcher_intro` to 377 on a pyro — but
  the partner-taunt names have not been checked.
- **What a partner taunt looks like with two actors.** `m_hActorList` is read in full and every actor
  gets the gesture, but no measured taunt in the corpus has more than one actor, so the second slot has
  never carried anything.
- **Whether a looping sequence is exempt from auto-kill.** `StartGestureSceneEvent` branches on
  `STUDIO_LOOPING` when choosing the initial cycle and the taunt duration (`c_tf_player.cpp:9459`), and
  ours does not read that flag — it relies on the scene's own loop instead. For every scene measured
  the two agree, because the looping taunts are the ones with a `LOOP` event; a looping SEQUENCE inside
  a non-looping scene would diverge and none has been found.
## B376: Valve's own writer wraps a ramp count, and the "two bytes" was a coincidence

**The first diagnosis was wrong, and the wrong part is worth keeping.** The census found two scenes
ending "two bytes short" and B376 localised that to one 2-byte field in a flex track. Every flex stride
checked out against the SDK, because the flex tracks were never the problem.

**The cause is the EXPRESSION event's ramp** (read from source, then measured). `CCurveData::SaveToBuffer`
(`choreoevent.cpp:4362`) writes `PutUnsignedChar( c )` behind an `Assert( c <= 255 )` that a release
build drops, then loops over all `c` samples. `RestoreFromBuffer` reads the one byte back. The
jackhammer-rodeo expression (`player\engineer\emotion\emotion`, 0 to 9.9 s) has a **259-sample** ramp,
and the count byte is **3**, which is 259 mod 256. The 259 sample times rise monotonically, and the 260th
"sample" is `08 00000000`, which is the event's flags (active) and a zero distance to target. That
arithmetic decides it: 1 + 259 × 5 = 1,296 bytes of ramp, and the reader skipped 1 + 3 × 5 = 16.

**Why it looked like two bytes:** after skipping 1,280 bytes too few, the walk read sample bytes as
tags and flex headers, and the resulting garbage strides happened to land 2 bytes from the end. The
channel really declares eight EXPRESSION events, not one. Only the first event's ramp is over 255.
The other seven ramps hold 47, 168, 198, 165, 201, 79 and 146 samples.

**The engine misreads these two scenes too, and so do we, on purpose.** `RestoreFromBuffer` takes the 3
at face value, so in-game the Engineer's jackhammer-rodeo facial track is read from the wrong offsets.

**A wrong turn worth keeping:** the first fix recovered the count. When the strict walk failed, it
extended the count by 256 while the sample times kept rising, and accepted the result only if the scene
then landed exactly. That reads what the writer meant, but it is not what TF2 shows. The owner's
standing ruling (Valve's way, always) overturned it, and it was replaced by a port of the engine's
restore.

**What the engine does with the desynced bytes** (read from source): every value is read through
`CUtlBuffer`. `CheckGet` (`utlbuffer.cpp:801`) refuses a read that does not fit and sets
`GET_OVERFLOW`, and it refuses every later read once that flag is set. `GetTypeBin`
(`utlbuffer.h:639`) then yields 0 without advancing. No restore in the chain notices: the event,
channel and actor restores fail only if a ramp or flex restore does, and neither ever does. So the
engine keeps every declared event. Garbage counts drive the loops until the buffer overflows, and the
rest of the scene reads as zeros. `EngineBuffer` reproduces these semantics, and the walk issues the
same reads in the same order. That includes the flex sample count, which is read as a SIGNED short:
a count past 32,767 reads no samples rather than stepping backwards. Measured on the two scenes: 10
and 9 events, an overflow, and a cursor 3 bytes short of the end.

**Completeness is now exact.** The walk reads the scene ramp and `m_bIgnorePhonemes`, which are the
last two things `SaveToBinaryBuffer` writes (`choreoscene.cpp:3757`). It counts a scene as complete
only when it lands on the end without overflowing. Before, the check accepted any cursor short of the
end, and that is how a 1,280-byte error passed as 2 bytes. Census: 9,943 of 9,945 complete, and the
two engine desyncs are pinned by crc (0x2D581E89 and 0xE4759CDC).

*Evidence class: read from source for the writer, the reader and `CUtlBuffer`; measured for the 259/3
count, the eight written events, the census and the desynced reads; arithmetic for the strides.*

- **What the engine renders for those two expressions.** We know its reader desyncs. We have not
  measured what a desynced restore produces on screen.

**The string pool on a garbage index — settled in disassembly.** `CChoreoStringPool::GetString`
(`c_sceneentity.cpp:738`) asks `scenefilecache->GetSceneString`. That function lives only in the
closed `scenefilecache.dll`. In TF2's x64 build it is slot 9 of `CSceneFileCache`'s vtable
(0x18001f3a8 → 0x180001db0), and its eleven instructions are `SceneImageHeader_t::String` from
`SceneImageFile.h` exactly. With no image loaded, a negative index, or an index at or past
`nNumStrings` (`[image + 0xC]`), it returns NULL. Otherwise it returns the image base plus
`table[index]`, with the table at `+0x14`. `GetString` copies NULL as "", and copies a found string
with `V_strncpy` into the caller's buffer, which is `char params[ 2048 ]` for a parameter. So a
garbage event's parameter is "" whenever its index is out of range, and any parameter is cut to
2,047 bytes. Ours now does both. Its only addition is a bound on the string's OFFSET: a corrupt table
would make the engine read arbitrary memory, which no port can reproduce. *Evidence class:
disassembly for the lookup, read from source for the copy.*
