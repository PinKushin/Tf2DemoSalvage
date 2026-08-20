# 04 — Entities: schema and delta decoding

The layer that makes the whole project viable, and the one Valve publishes least about — the delta
engine lives in `engine.dll`, which is closed. See `docs/SPEC.md` Layer 3 for the current
description.

## The demo carries its own schema, which is why this is tractable at all

`dem_datatables` embeds the server's `SendTable` definitions: every networked class, its
properties, their types, bit widths and flags. **A parser that decodes generically off whatever
schema the file provides does not need to know any particular TF2 version.**

That is the founding insight of the project. The alternative — hardcoding one era's field layout —
is what makes other tools break when Valve changes the schema, and it is why demos the live client
can no longer play are still readable here. What actually has to be era-aware is the *container and
bit-packing*, which changes far less often ([06](06-protocol-eras.md)).

## Flattening order was wrong, and only a differential could have caught it

Properties are declared in nested tables and must be **flattened** into the linear order the wire
uses. Getting that order wrong does not throw: it reads the right number of bits into the wrong
fields, producing complete, plausible entities that are silently mislabelled.

No hand-built fixture could have found this, and none did. A fixture encodes what its author
already believes, so the test and the implementation shared the misunderstanding and agreed
perfectly. It died in a single differential run against `demostf/parser`, across **204,000
properties**.

**This is the strongest argument in the project for differential testing over fixtures**, and it
generalises: where you are testing your *reading of a spec* rather than your code, you need a
second independent implementation, not a better test.

## The deletion list can end without its terminator

A long-standing mystery: some snapshots re-encoded *longer* than the original, and some deletion
lists simply stopped.

The cause is not in the format — it is in the writer. `bf_write` **abandons a field that does not
fit rather than truncating it**, so when the buffer fills mid-list the terminator is never written
and the message just stops. Detail and the published source in
[09-valve-implementation.md](09-valve-implementation.md).

Two consequences:

- A decoder must treat "ran out of stated length" as a legitimate end of list. Guarding the read
  with `if (lengthBits - reader.BitsRead < RemovedIndexBits) break;` recovered 366 snapshots that
  previously failed outright.
- A faithful **re-encoder must reproduce the giving-up**, not politely finish the list. Writing the
  terminator Valve omitted produces bytes Valve never wrote.

## Numeric encodings fail as plausible numbers, never as errors

The recurring hazard in this layer. Every one of these produces a number rather than an exception
when read wrongly:

- **Range-encoded floats** — a value packed into N bits between a stated min and max. Read with the
  wrong width and you get a real number in a believable range.
- **Sign extension** — a signed field read as unsigned is only wrong for negative values, so it
  passes on most data.
- **Derived square roots** — a third component reconstructed from two others produces `NaN` when
  the inputs are slightly wrong, and `NaN` propagates silently.

The defence is plausibility bounds drawn from the format itself rather than from taste: coordinates
inside the world extent, entity indices inside `MAX_EDICTS`, volumes in 0…1, sound indices inside
the precache table's own size. The last is the sharpest, because the index comes from the bit
stream and the table comes from `svc_CreateStringTable` by a completely independent path — there is
no way to land inside it by accident across thousands of sounds.

## Coordinates are the unit of measurement for this whole format

From `public/coordsize.h`: 14 integer bits, 5 fractional. A `ReadBitCoord` is two presence bits, a
sign bit if either is set, then the parts that were sent — so an axis is 22 bits with a fraction,
17 integer-only, 8 fraction-only, 2 absent.

Those four numbers identify message layouts from body lengths alone, before any payload is read.
They are used that way repeatedly in [05](05-user-messages.md) and [08](08-method.md).

## Where it stands

All corpus demos decode end to end with no stops, across every protocol held. Entity snapshots
re-encode byte-identically except for a residue of roughly a thousand, which remains open and is
tracked in `docs/RISKS.md`.

## A property's wire name is not always its C++ name

Evidence class: **read from published source**, swept across `src/game`.

`SENDINFO` names a send prop after its member. `SENDINFO_NAME` takes two arguments and sends under
the **second**:

```c
#define SENDINFO_NAME(varName,remoteVarName)   #remoteVarName, ...
```

Seventeen uses, six distinct aliases in the whole SDK:

| C++ member | wire name |
|---|---|
| `m_hMoveParent` | `moveparent` |
| `m_MoveType` | `movetype` |
| `m_MoveCollide` | `movecollide` |
| `m_nEntIndex` | `entindex` |
| `m_flHDRColorScale` | `HDRColorScale` |
| `m_flValue` | **`m_iRawValue32`** |

The first four drop the Hungarian prefix; the fifth keeps a capital. **The sixth is the interesting
one, because the rename carries information about the encoding.**

### `m_flValue` is a float sent as an unsigned integer

`econ_item_view.cpp:67` and `:73`:

```c
SendPropInt( SENDINFO_NAME(m_flValue, m_iRawValue32), 32, SPROP_UNSIGNED ),
RecvPropInt( RECVINFO_NAME(m_flValue, m_iRawValue32) ),
```

The member is `CNetworkVar( float, m_flValue )`. The prop is a **`SendPropInt`, 32 bits, unsigned**.
So an econ attribute's value travels as the float's **bit pattern reinterpreted as an integer**, and
the wire name says so — `RawValue32` is a warning, not a typo.

Decoding it as a number gives **1065353216** where the value is **1.0**. That is not an error, it is
a large plausible integer, so it fails the way the whole `numeric-decoding-traps` family does. Every
item attribute in TF2 — paint, unusual effects, killstreaks, every balance change — arrives through
this one property.

### Why this cost something

A conformance test written earlier in this project described attributes as "(definition index,
float) pairs". Accurate about the *member*, wrong about the *wire*, and an implementer following it
would have looked for a float send prop that does not exist.

**And the aliasing produced a false accusation.** `SendPropConformanceTests` scrapes `SENDINFO(...)`
for the set of names the engine sends, capturing only the first argument — so every aliased property
was missing from its denominator. When `moveparent` was added to the inventory of names this project
reads, the test reported it as a name "no send table in the SDK declares", against entirely correct
code.

Worse, that false negative had already been believed once. A test asserted:

> *"moveparent is not a SendProp and must not be checked as one … it will never appear in a
> `SENDINFO`."*

A limitation of a regex, written down as a fact about the format, and then defended by an assertion.
Both are corrected; the scraper now reads the remote name.

**The general rule: when looking for a property name in the SDK, search for it as a STRING, not as an
identifier.** The wire carries the string, and only `SENDINFO_NAME` tells you they can differ.

## A viewmodel says what it is, never where it is
*(read from published source; measured across the corpus — 20 August 2026)*

The weapon in a player's own hands is a networked entity, and it carries no position.
`baseviewmodel_shared.cpp:557` opens the table with `BEGIN_NETWORK_TABLE_NOBASE` — no base table,
therefore no `DT_BaseEntity`, therefore no `m_vecOrigin` and no `m_angRotation`:

```cpp
BEGIN_NETWORK_TABLE_NOBASE(CBaseViewModel, DT_BaseViewModel)
    SendPropModelIndex(SENDINFO(m_nModelIndex)),
    SendPropInt   (SENDINFO(m_nBody), 8),
    SendPropInt   (SENDINFO(m_nSkin), 10),
    SendPropInt   (SENDINFO(m_nSequence), 8, SPROP_UNSIGNED),
    SendPropFloat (SENDINFO(m_flPlaybackRate), 8, SPROP_ROUNDUP, -4.0, 12.0f),
    SendPropEHandle (SENDINFO(m_hWeapon)),
    SendPropEHandle (SENDINFO(m_hOwner)),
```

**This is the same shape as a bone-merged cosmetic** — the demo names the model and the pose, and
the client works out the placement. `CBaseViewModel::CalcViewModelView` starts it at the eye and
then adds bob, lag and shake, every one of which is a function of movement and elapsed time rather
than of anything recorded. The eye placement is what a recording can support; the embellishments
would be the viewer inventing motion.

It is also drawn with the cull mode flipped (`c_baseviewmodel.cpp:373`), because the model is
mirrored for the left-handed view — the detail that makes a naive implementation draw the weapon
inside out.

**SourceTV demos carry viewmodels too, which was not the expectation.** A viewmodel is the local
player's own weapon, so the obvious guess is that only a point-of-view recording has one. Counted
across the committed corpus:

| Demo | viewmodel property updates |
|---|---|
| 2007 granary POV | 968 |
| 2007 granary STV | **0** |
| 2008 granary POV | 694 |
| 2008 granary STV | 889 |
| 2009 badlands POV | 3359 |
| 2011 viaduct POV | 604 |
| 2011 viaduct STV | 667 |
| 2013 badlands POV | 487 |
| 2013 foundry STV | 1773 |
| z1800 (SourceTV) | 95480 |

Every era but the earliest broadcasts them to SourceTV, so a first-person view of a *spectated*
player can show their weapon as well. The 2007 zero is an era difference rather than a property of
that recording.

**The search that found this was wrong twice before it was right**, which is worth recording
because the failures were silent. Grepping the assembly output for `CTFViewModel` returned nothing
— and so did grepping it for `CTFPlayer`, which certainly exists, because class names are not
printed as text there at all. The count that mattered came from the property table name,
`DT_BaseViewModel`. An absence claim needs a positive control in the same sweep; this is the sixth
time in this project an instrument has been wrong before a decoder was.

### One viewmodel or thirty-seven, and only the modern one says whose it is
*(measured across the corpus — 20 August 2026)*

Counting distinct viewmodel ENTITIES rather than property updates gives a sharper answer than the
table above, and a different one:

| Demo | viewmodel entities | with an owner | owner resolves to a player |
|---|---|---|---|
| 2007 granary POV | 1 | 0 | 0 |
| 2008 granary POV / STV | 1 | 0 | 0 |
| 2009 badlands POV | 2 | 0 | 0 |
| 2011 viaduct POV / STV | 1 | 0 | 0 |
| 2013 badlands POV / foundry STV | 1 | 0 | 0 |
| z1800 (modern SourceTV) | **37** | **37** | **37** |

**A point-of-view recording carries exactly one viewmodel and does not say whose it is**, because
it does not need to: you only ever receive your own, so the owner is the recorder by definition.
That is why `m_hOwner` is unset in eight of the nine — not an era gap in the property, but an
absence of anything to disambiguate.

**A modern SourceTV recording carries one per player and every owner handle resolves.** 37 of 37,
which is what makes a first-person view of a *spectated* player able to show their weapon.

So the implementation has two cases and neither needs guessing: on a POV demo take the single
viewmodel, on an STV demo join by `m_hOwner`.

**The "0 owners are players" row was wrong for a whole measurement.** The survey compared
`ClassName` against `"Player"` for every entity and got zero every time — because `ClassName` is
seeded by `DemoTimeline.Build` from the schema's server classes, and a hand-rolled walk over the
entity stream never calls `SetClassName`. Every entity was anonymous, so the comparison could only
ever fail. Seeding the names turned 0 of 37 into 37 of 37 with no change to the code under test.

That is the seventh instrument bug ahead of a decoder bug in this project, and the third in this
one evening. The tell each time is the same: a clean zero that would have been reported as a fact
about the format.
