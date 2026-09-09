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
  (`docs/memory/an-empty-search-needs-a-control.md`).
- **`BodyFor`, a second method beside `SequenceFor`**, because one method returning null for three
  different causes cannot tell them apart. It printed `156 bytes, tag 'bvcd'` — so the name resolved,
  the entry decompressed, and the LZMA path was innocent. That left the event walk, and nothing else.

`docs/memory/instrument-bugs-outnumber-decoder-bugs.md` is about instruments lying; this is the other
half of the same rule. **A diagnostic that collapses three causes into one null is not lying — it is
refusing to answer**, and it costs the same.

## What is NOT established

- **Which gesture a scene with several of them plays.** `SequenceFor` returns the first, which is
  right for every taunt measured and is a guess for anything else. The engine plays events on the
  scene's clock (`C_SceneEntity`), so a scene staging two gestures in sequence needs the times, not
  the first name. The walk already reads start and end time and currently discards them.
- **That `taunt_highFiveStart` exists as a sequence on the class models.** The archive names it; that
  `LookupSequence` finds it has not been checked here.
- **The scene ramp and everything after the actors.** The reader stops once the actors are walked, so
  nothing past that point in a compiled scene has been read or verified.
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

## What is NOT established

- **Which gesture a scene with several of them plays.** `SequenceFor` returns the first, which is
  right for every taunt measured and is a guess for anything else. The engine plays events on the
  scene's clock (`C_SceneEntity`), so a scene staging two gestures in sequence needs the times, not
  the first name. `EventsFor` carries them; nothing consumes them yet.
- **That `taunt_highFiveStart` exists as a sequence on the class models.** The archive names it; that
  `LookupSequence` finds it has not been checked here.
- **The scene ramp and everything after the actors.** The reader stops once the actors are walked, so
  nothing past that point in a compiled scene has been read or verified beyond arithmetic on its
  known 4-byte shape.
- **The cause of B376.** Localised to one 2-byte field, not identified.
