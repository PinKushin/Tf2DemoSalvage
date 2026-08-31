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

### The "2" in that table was the bug, and it sat there unread for a day

**A player has two viewmodels, not one.** `shareddefs.h:325` sets `MAX_VIEWMODELS 2`, and TF2 names
the second one outright:

```cpp
CBaseViewModel *CTFPlayer::GetOffHandViewModel()
{
    // off hand model is slot 1
    return GetViewModel( 1 );
}
```

Slot 0 is the weapon in the player's hands. Slot 1 is the off hand, claimed by exactly two things in
the shipped game code — `CTFWeaponInvis::Spawn` (the spy's Invis Watch) and `tf_weaponbase_grenade`.
Which one an entity is arrives on the wire as `m_nViewModelIndex`, **one bit, unsigned**
(`VIEWMODEL_INDEX_BITS 1`, `baseviewmodel_shared.h:29`; sent at `.cpp:563`).

The first implementation of `DemoTimeline.ViewmodelAt` ignored the slot and kept whichever viewmodel
it walked past last. On every demo carrying one that is correct by luck. On the 2009 badlands POV —
the single row in the table above reading **2** — it answered `v_watch_spy` while the recorder's
networked `m_iClass` went soldier, then scout. *(evidence class: measured on the corpus, against
published source)*

**The row was already in this document and read as a curiosity.** A measurement that disagrees with
every other row in its own table is a finding, not noise; it was written down and not followed.

**The property is not modern.** Every corpus demo back to the 2007 build declares
`DT_BaseViewModel.m_nViewModelIndex` at 1 bit unsigned — asserted per demo in
`ViewmodelConformanceTests`, from each file's own schema rather than from the SDK header. The era
question was raised as possibly needing a decompiler and needed nothing: **a demo carries the schema
that describes it**, so "did this field exist in 2009" is answerable from the 2009 file.

**An absent slot means the main hand, not an unknown one.** `CBaseViewModel`'s constructor sets
`m_nViewModelIndex = 0` (`baseviewmodel_shared.cpp:53`), so a property that never arrived is the
engine's default. `EntityState.ViewmodelSlot()` still reports null for "the demo did not say" — the
reader states the wire, the consumer applies the default.

### The off hand is drawn as well as the weapon, not instead of it

From the owner, who has played the class:

> main viewmodel doesnt get hidden when a spy goes invis, the watch just comes up and everything
> goes transparent

and on which hand is which:

> yep the watch is the left hand, the weapon in in the right, unless you use left handed
> viewmodels, then its the opposite

So a spy mid-cloak has both viewmodels on screen at once, and `ViewmodelAt` answering with the main
hand is one weapon short of what that player saw. That is a deliberate, smaller error than the wrong
weapon; drawing both is separate work. *(evidence class: owner's account of the live game)*

**A screenshot of a fully cloaked spy settles what "transparent" means here**, and it is stronger
than the word suggests: at full cloak the viewmodels are drawn so far towards invisible that the
frame reads as the bare world, with only a faint sliver left at the bottom of the screen. So the
entity is present, networked and animating the whole time — the change is in the material, not in
whether the model exists. A reader that inferred "the spy's weapon disappears" from looking at
gameplay would conclude the viewmodel was removed, and be wrong about the thing this project
actually decodes. *(evidence class: owner's screenshot of the live game, 2026-08-20)*

The handedness note lands on the cull mode rather than on the lookup: `cl_flipviewmodels` mirrors
the model, and `C_BaseViewModel::InternalDrawModel` switches to `MATERIAL_CULLMODE_CW` when it is
mirrored. A demo records the entity, not the viewer's preference, so which hand a weapon appears in
is a property of the person watching the playback — not of the recording.

### What the agreement test settled on the way past

`ViewmodelClassAgreementTests` cross-checks the resolved model path against the recorder's networked
`m_iClass` — two unrelated decode paths, a Snappy-compressed string table resolved by index and a
delta-compressed integer on the player entity. After the fix, no demo disagrees.

It also closed an open question in the other direction. The 2013 badlands POV resolves
`c_sniper_arms`, and the owner said he never played sniper on it. The demo says otherwise, and says
it twice: at some ticks `m_iClass` is 2 with `v_sniperrifle_sniper` in hand. Across the file he
plays scout, sniper, soldier, demo and pyro. **The resolution was right and the recollection was
not** — which is why the test was written against the demo rather than against anyone's memory.

**The test now asserts rather than reports.** As first written it printed AGREED and DISAGREED lists
and asserted only that *something* was compared, so the fix could not have been proved by it. An
empty disagreement list is also what "this demo stopped resolving a weapon at all" looks like, so it
now names the two-viewmodel demo explicitly and requires a comparison from it.

### Cloak is computed, not recorded — and how much of it depends on who is watching

The blur is client-only, and the SDK says exactly how much of it we would have to compute.

**`m_flInvisibility` is not on the wire.** It has no SendProp; the only cloak value networked is
`m_flCloakMeter` (the ammo). The invisibility level is recomputed every frame in
`CTFPlayerShared::InvisibilityThink` from conditions and timers — `IsStealthed()`,
`m_flInvisChangeCompleteTime`, `TF_COND_STEALTHED_BLINK`, and for motion cloak the player's own
speed. What a demo carries is `m_nPlayerCond`, a networked varint bitfield (`tf_player_shared.cpp:536`).
So cloak is reproducible from the recording, but only by re-running the engine's arithmetic — which
is the same shape as the feet-yaw and air-walk work already in `DemoTimeline`. *(evidence class:
read from published source)*

**The viewmodel never goes fully invisible.** `CViewModelInvisProxy::OnBind` (`tf_viewmodel.cpp:432`)
remaps the player's invisibility into a narrow band:

```cpp
#define TF_VM_MIN_INVIS  0.22
#define TF_VM_MAX_INVIS  0.5

flWeaponInvis = ( flPercentInvisible < 0.01 ) ? 0.0
              : RemapVal( flPercentInvisible, 0.0, 1.0, TF_VM_MIN_INVIS, TF_VM_MAX_INVIS );
```

At 100% cloak the weapon sits at 0.5, and a blink pins it to 0.3. That is why the owner's
full-cloak screenshot still shows a sliver of the model rather than nothing: the first-person case
tops out half-transparent by design.

Incidentally the proxy finds its player through `pVM->GetOwner()` — the same `m_hOwner` this project
reads off `DT_BaseViewModel`, arrived at independently.

**For other players the spy is not blurry, he is gone — unless you are spectating.**
`C_TFPlayer::GetEffectiveInvisibilityLevel` splits on the viewer:

```cpp
bool bLimitedInvis = !IsEnemyPlayer() || bHalloweenSpellStealth;

// If this is a teammate of the local player or viewer is observer,
// dont go above a certain max invis
if ( bLimitedInvis ) { flPercentInvisible = min( flPercentInvisible, tf_teammate_max_invis ); }
```

`tf_teammate_max_invis` defaults to **0.95** (`c_tf_player.cpp:1702`). An enemy gets the unclamped
1.0 and sees nothing at all, which is the whole point of the ability; a teammate or an observer gets
0.95, a faint shimmer rather than a blur.

**That second branch is the one this project is in.** `IsEnemyPlayer()` returns false when there is
no local player, which is exactly a demo. So the reference behaviour for a viewer here is the 0.95
clamp — a cloaked spy should be drawn barely-there, not culled — and it matches what spectating in
the live game looks like. Getting this backwards would mean a spy vanishing from a demo the engine
would have shown. *(evidence class: read from published source, prompted by the owner's account)*

---

## An entering entity is a delta against its class baseline, and the state table was not applying it

*(evidence class: read from published source, then measured differentially on the corpus)*

**Two questions about one entity look identical and are not.** "What did this snapshot carry" is
wire-faithful: exactly the properties on the bits, which is what a re-encoder must reproduce or the
demo does not round-trip. "What is this entity now" is state-faithful: that list laid on top of the
class's **instance baseline**, because an entity entering the visible set is a delta against the
baseline and omits everything equal to it. The engine merges them in `CL_CopyNewEntity` before the
entity exists at all.

This project had both. `DecodedEntity.Properties` answered the first and
`EntityDecoder.EffectiveProperties` answered the second, with a doc comment that spelled the split
out in as many words. `EntityStateTable.Apply` — whose entire job is the second question — read the
first, and had since it was written.

**The defect was invisible because of what TF2 sends, not because of what the code does.** A player
resends origin, health, team and the rest constantly, so the baseline supplies only values that
arrive again within a second or two and the accumulated state converges either way. Applying
baselines changes **no property count on any demo in the corpus**, era or modern, for any entity
anyone had looked at.

An entity whose whole state *is* its baseline never converges. `CFogController` is the pure case: it
enters once at tick one carrying fifteen properties, **none of them on the wire**, and is never
mentioned again in the file. Measured on `tf2-2011-build4604-stv-koth_viaduct.dem`, entity #212
appeared in the entity table on 3,762 consecutive packets holding zero properties, while a trace of
the same file printed all fifteen. Nineteen of that table's 195 entities were empty the same way.

**The trace was right and the table was wrong, from the same decoder, on the same packet.** That is
what made it hard to see: the trace writer had already been fixed to call `EffectiveProperties`, and
its commit message noted that "DemoTimeline has always done this" — meaning it applied the baseline
string table to the decoder, which it did. Nobody checked the other half.

### The cross-source confirmation, which the corpus alone cannot give

A demo's fog is a `CFogController`'s send-table state. A map's fog is the keyvalues a mapper typed
into Hammer, sitting in BSP lump 0. Nothing connects the two inside this project, so an agreement
between them is evidence about the decode rather than evidence that a fixture agrees with the code
that produced it.

| map | authored in the BSP | networked in the demo | unpacked |
|---|---|---|---|
| `cp_granary` | 225 225 225, 0→14000, density .8 | `colorPrimary 14803425` | 0xE1E1E1 |
| `koth_viaduct` | 213 174 221, 0→6500, density 1 | `colorPrimary 14528213` | 0xDDAED5 |
| `cp_foundry` | 131 121 134, 1707→4634, density .7 | `colorPrimary 8812931` | 0x867983 |

**Viaduct is the specimen that fixes the byte order.** A `color32` travels as one 32-bit int and
reading it reversed is the plausible mistake; granary is 225 grey and cannot tell the two readings
apart, foundry's 131 and 134 differ by three, and viaduct's 213/174/221 can only be read one way.
Red is the low byte.

### What the fix uncovered one layer up

`CWorld` began arriving with model index 1 — `maps/<name>.bsp`, the map itself — and became a prop
track covering the whole world. It had never appeared before because the world states its model once,
in its class baseline, and never again.

**Valve excludes it by entity index, not by model type.** `C_BaseEntity::ShouldDraw`, at
`game/client/c_baseentity.cpp:1450`:

```cpp
return (model != 0) && !IsEffectActive(EF_NODRAW) && (index != 0);
```

So the world model is an ordinary brush model — `mod_brush`, the same `modtype_t` as the `*N`
submodel a door uses, differing only in which submodel it names — and what keeps it off the screen
is that its index is zero. Classifying it as an unrecognised reference would have been a statement
about the format that is not true.

That same line also says an `EF_NODRAW` entity is not drawn. This project does that already, in
`DemoTimeline.PropsAt` — a hidden pose never becomes a `SceneProp`, so the renderer has nothing to
skip. It was briefly filed as a gap (B133) on the strength of a search scoped to the renderer alone,
and withdrawn the same day: the absence of the flag downstream is caused by the filter being
upstream, which is the correct place for it.

### A creating update is a delta against the instance baseline, and the baseline is a stranger

**Evidence class: measured on the corpus, and cross-checked against the map's own entity lump.**

`cp_fulgur`, the owner's recording. Slot 432 is the BLU spawn's windowed door. Watching every update
to that slot in order, with the `instancebaseline` string table applied:

```
Enter 432 serial 998 props 2  modelindex 1154 origin (3440 -2096 240)
Enter 432 serial 998 props 11 modelindex 1177 origin (2 0 -59)
```

Index 1177 is `models/props_gameplay/windowed_door.mdl`. Index **1154** is
`models/props_gameplay/resupply_locker.mdl`, and `(3440 -2096 240)` is `prop_locker_blu_5`'s world
origin, read out of the map:

```
PROP prop_dynamic models/props_gameplay/resupply_locker.mdl
       name    (prop_locker_blu_5)
       origin  (3440 -2095.56 240.16)
       parent  [unparented]
```

**Neither value belongs to the door.** They belong to whichever entity supplied `CDynamicProp`'s
instance baseline. An entity is created as a delta against that baseline, so a creating update
carries only what differs from it — and everything it omits is a stranger's state until the next
update corrects it.

**The engine is untroubled by this because it re-reads the model every update.**
`C_BaseEntity::PostDataUpdate`, `client/c_baseentity.cpp:2603`:

```cpp
    Assert( m_hNetworkMoveParent.Get() || !m_hNetworkMoveParent.IsValid() );
    HierarchySetParent(m_hNetworkMoveParent);

    MarkMessageReceived();

    // Make sure that the correct model is referenced for this entity
    ValidateModelIndex();

    // If this entity was new, then latch in various values no matter what.
    if ( updateType == DATA_UPDATE_CREATED )
```

**Both calls sit above the `DATA_UPDATE_CREATED` test, so both run on every update.**
`ValidateModelIndex` ends in `SetModelByIndex( m_nModelIndex )` (`c_baseentity.cpp:2531`).

This project followed the first of that pair and not the second: `ScenePropTrack.AttachedTo` was
assigned every update, with a comment citing these very lines, while `ModelPath` was fixed at
construction. So the door was named a resupply cabinet for the rest of the recording, and nine other
entities took the same baseline's identity the same way — every one of them reporting
`(3440 -2096 240)`, one map prop's position, which is what made the pattern visible at all.

**Half a mechanism, and the half that was implemented is the one that made it hard to see.** A
correct parent on a wrongly-named prop looks like a naming problem; a wrong parent would have looked
like a parenting problem, which is what three earlier rounds of this investigation assumed.

#### The origin half is a different mechanism, and it is not implemented at all

Correcting the model leaves the cabinet tracks holding two keyframes — the baseline's origin and the
real one — and the timeline interpolates between them, so a BLU spawn cabinet flies across the map
and back. The pose sequence measured for slot 312, `prop_locker_blu_3`:

```
(6232 -3024 384) | (3440 -2096 240) | (6043 -2961 374)
```

The third is an interpolation between the first two.

The engine does not do this because `svc_PacketEntities` carries two more fields this project
decodes, round-trips and never consumes — `baseline` and `update_baseline`. Counted across the
recording:

| flags | snapshots |
|---|---|
| `baseline=0 updatebaseline=0` | 12,340 |
| `baseline=0 updatebaseline=1` | 1,169 |
| `baseline=1 updatebaseline=0` | 12,798 |
| `baseline=1 updatebaseline=1` | 1,171 |

**2,340 snapshots ask the client to update a baseline, and the index alternates.** An entity
entering the PVS deltas against its *per-entity* baseline in the named slot, not against the class
instance baseline — which is exactly how a two-property `EnterPVS` can describe a door completely.

`docs/RISKS.md` filed this in the B12/B13 write-up as *"This parser ignores both, and a baseline
swap that changes how a later delta is interpreted would look exactly like this"*, listed it first
under **Still to read**, and it was never read. The note was right and sat unactioned for months;
the measurement above is the first evidence that the mechanism is live in a real recording rather
than merely possible.

#### Implemented, and what it moved

**Evidence class: measured, before and after, on the same recording.**

`EntityBaselineSlots` maintains the two arrays. An entering entity deltas against its own stored
state when the snapshot's named slot holds one **of the same class** and the snapshot is a **delta**;
otherwise against its class baseline. On `update_baseline` the named array is copied wholesale into
the other one and each entering entity's **merged** state is written over the copy.

The eight `resupply_locker` entities in `cp_fulgur`, before and after, against the map:

| entity | before | after | map |
|---|---|---|---|
| 52 | (3440 -2096 240) | (3440 -2096 240) | `prop_locker_blu_5` |
| 54 | (3024 -1736 368) | (3024 -1736 368) | `prop_locker_blu_6` |
| 82 | (2480 2784 192) | (2480 2784 192) | `prop_locker_red_2` |
| 105 | (1744 880 104) **+ (3440 -2096 240)** | (1744 880 104) | `prop_locker_red_3` |
| 312 | (6232 -3024 384) **+ (3440 -2096 240) + (6043 -2961 374)** | (6232 -3024 384) | `prop_locker_blu_3` |
| 314 | (5744 -2664 384) **+ (3440 -2096 240) + (5719 -2658 383)** | (5744 -2664 384) | `prop_locker_blu_4` |
| 413 | (-864 -1104 152) | (-864 -1104 152) | `prop_locker_blu_7` |
| 420 | (-0 -1580 -64) | (-0 -1580 -64) | `prop_locker_blu_8` |

**The bolded poses are the class baseline's**, `prop_locker_blu_5`'s world origin, plus what the
timeline interpolated on the way there and back. Entities 472, 473, 477, 478 and 717 disappeared
entirely — they were slots briefly holding a stranger's state and nothing else, and the walk's prop
count fell from 27 to 21. The two BLU spawn cabinets went from 78 samples across three positions to
**303 at one**.

**Six of the nine tests are controls, and that ratio is not padding.** Every wrong implementation
here produces plausible numbers rather than an error: read the wrong slot and entities still decode,
skip the array copy and they decode for one snapshot in two, drop the class check and a reused slot
inherits a stranger. The only way to tell those apart is to write the case down first. Five
deliberate sabotages each killed exactly the test written for it.

### A weapon's model is not on the wire, and for some weapons nothing is

**Evidence class: measured on the corpus, cross-checked against published SDK source.**

Owner's report: *"mediguns still are not drawing on other players too, but the flamethrower, and it
looks like everything else, draws"*.

Two readings died first, and both are worth keeping because both looked right:

1. **"It draws in the wrong place."** The render log says `c_medigun.mdl` IS drawn, at a real world
   position. Those are the ten `CTFDroppedWeapon` entities on the floor — correct in every respect.
2. **"The bone-merge rule is not firing for weapons."** Every track carrying `c_medigun.mdl`
   reported `parent none, merged False`. Same ten entities; a dropped weapon is not merged to
   anybody. `SchemaClasses.BoneMergesItself` is `True` for `CWeaponMedigun` throughout.

Both were facts about the wrong entities. Every weapon in the recording that has an OWNER:

| entity | class | `m_nModelIndex` | `m_iWorldModelIndex` | item | owner |
|---|---|---|---|---|---|
| 968 | `CTFRocketLauncher` | 996 | 426 | 513 | 18 |
| 953 | `CTFRocketLauncher` | 996 | 200 | 907 | 22 |
| 1100 | `CTFFlameThrower` | none | 225 | 40 | 5 |
| 940 | `CTFMinigun` | none | 393 | 424 | 24 |
| **1017** | **`CWeaponMedigun`** | **none** | **none** | **211** | **8** |
| **1109** | **`CWeaponMedigun`** | **none** | **none** | **211** | **21** |
| **1192** | **`CTFMinigun`** | **none** | **none** | **15123** | **23** |

**Item 211 is the stock Medi Gun.** Nothing was missing from the recording — the number that names
the model was there the whole time, and no one asked it. `DemoTimeline.ModelFor` reads
`WorldModelIndex() ?? ModelIndex()`, got nothing, and returned before a track was ever made.

**It is not medigun-specific**, which the minigun proves: the rule is "weapons that network no model
index", and the medigun is merely the class where that is always true.

#### The engine never reads that field for an econ entity

`CEconEntity::UpdateModelToClass`, `game/shared/econ/econ_entity.cpp:382`:

```cpp
    pszModel = pItem->GetPlayerDisplayModel( m_iOldOwnerClass, nTeam );
    if ( pszModel && pszModel[0] )
    {
        if ( V_stricmp( STRING( GetModelName() ), pszModel ) != 0 )
        {
            ...
            SetModel( pszModel );
        }
    }
```

The model comes from `model_player` in `items_game.txt`, keyed by the owner's CLASS — which is why
a shotgun shared by soldier, pyro, heavy and engineer is four different models under one item.

**This project already knew that.** `DemoTimeline.cs:1600` carries the citation and the failed
experiment: *"Taking the weapon entity's own `m_nModelIndex` was tried on 2026-08-28 and drew no
weapon at all: `m_hWeapon` says WHICH weapon and the schema says what it looks like."*
`WeaponModels.For` implements it, and was wired to the viewmodel and to the followed player. The
weapon entities other players carry were the one caller that never asked — **the fourth
half-implemented mechanism found in a single session**, after the model index, the baseline slots
and the animation cycle.

#### A divergence this fix does NOT close, stated rather than hidden

Valve's rule is *the item wins whenever it names something different*. This fix is narrower: it
resolves only when the entity networks **no** model at all, and leaves a weapon that sent one alone.

The two agree for every weapon measured here, because the server sets `m_iWorldModelIndex` from the
same item — flamethrower index 225 resolves to `c_backburner.mdl`, a `c_` model, not the legacy
`w_` one. They can disagree when an entity's owner CLASS changes, since Valve re-resolves per class
and this keeps the networked value.

The narrower rule was chosen because the wider one changes every working weapon and the difference
cannot be checked without looking at the screen. It is recorded here rather than in a code comment
because a divergence is a question for the owner, not a note to self.

### The mechanical audit: which decoded fields have no consumer

**Evidence class: mechanical, over the repository.** Every bug found on 2026-08-30 had one of two
shapes — a field decoded and never read, or a mechanism wired to one caller of several. That is
auditable rather than discoverable one symptom at a time, and the audit is two lines of shell:

```bash
# every public accessor on EntityState
grep -oE "public [A-Za-z0-9_?<>., ()]+ [A-Z][A-Za-z0-9_]*\(" EntityState.cs | ...
# each one with no production caller outside the file that declares it
grep -rlE "\.${name}\(" managed/ --include=*.cs | grep -v Scene/EntityState.cs
```

**46 accessors, 5 with no production caller:**

| accessor | verdict |
|---|---|
| `Attachment` | dead — replaced by `AttachmentHandle`, which checks the handle's serial |
| `RenderColor` | `m_clrRender`, decoded and never applied |
| `ViewmodelMuzzleFlashParity` | known gap, documented at the declaration |
| `ViewmodelNewSequenceParity` | known gap; the `DT_BaseAnimating` twin is now consumed |
| `ViewmodelResetEventsParity` | known gap — this viewer dispatches no animation events |

**It caught a divergence introduced an hour earlier.** `ClientSideAnimation` appeared on that list
because the frame-reset fix honoured `m_bClientSideFrameReset` unconditionally, where
`c_baseanimating.cpp:5021` reads it only inside `if ( m_bClientSideAnimation )`. A server-animated
entity toggling the field would have restarted on it. The audit found that in seconds; no amount of
looking at the screen would have.

#### What the audit CANNOT see, which is the larger set

It enumerates fields this project already decodes. A field never decoded at all is invisible to it,
and those are the more expensive gaps:

- **`m_flAnimTime`** — cited in seven comments, decoded nowhere. It lives in
  `DT_AnimTimeMustBeFirst`, not `DT_BaseEntity`, so asking under the obvious name silently matches
  nothing.
- **Spy disguises.** `m_nDisguiseTeam` and `m_nDisguiseClass` are networked
  (`tf_player_shared.cpp:400`), and the string "Disguise" appears **zero** times in the entire
  managed tree. The owner's report is the symptom: *"a spy looked like a blue spy and a red demo at
  the same time"*.

**So the denominator has to come from the SDK's RecvTables, not from our own accessors** — the same
argument `SdkCoverageTests` already makes for shader parameters and BSP lumps, applied to networked
properties. A coverage test over `RecvPropInt( RECVINFO( ... ) )` for the classes this viewer draws
would turn every remaining gap of this kind into a number, and it is the single highest-value thing
left undone here.
