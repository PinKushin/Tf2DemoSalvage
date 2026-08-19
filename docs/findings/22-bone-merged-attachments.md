# 22 — Weapons and cosmetics carry no position, on purpose

A player in the viewer was a bare class model: no weapon in hand, no hat, no badge. The obvious
reading was that the demo does not carry them, and the obvious second reading — after weapon models
turned out to be loading fine — was that a carried weapon's origin is relative to its owner and
needs a parent resolved. Both are wrong, and the second one is wrong in an interesting way.

## The measurement, and the instrument bug that came first

*Evidence class: measured on the corpus (gcor `demostf-cp_process_f12-2026-08-07.dem`).*

The first probe walked the demo to "tick 20000", counted props there, and reported **197 props of
which 1 was a weapon**. That number is worthless. A demos.tf recording starts at whatever tick the
server happened to be on, and on this file the first packet is already past 20000 — so the walk
stopped before processing a single command:

```
WEAP 0 of 106226 commands walked; 0 live entities across 0 classes:
```

Nothing in the earlier output said so. `PropsAt(20000)` answered with 197 props anyway, because a
tick outside the recording is not an error to it. **A hardcoded tick is not a tick.** Taking the
midpoint between the first and last packet commands instead:

```
WEAP 53117 of 106226 commands walked; 613 live entities across 59 classes:
     CBeam x211, CBaseEntity x130, CBaseAnimating x43, CTFWearable x37, CTFViewModel x24,
     CBaseDoor x18, CDynamicProp x16, CSceneEntity x15, CTFPlayer x13, ...
```

That is the third instrument bug in this area and it has the same shape as the others: the tool
reported a plausible number for a question it never asked. See
`docs/memory/instrument-bugs-outnumber-decoder-bugs.md`.

## What the entities actually carry

Every class holding entities with no origin at that tick:

```
CBeam 211/211, CTFWearable 37/37, CTFViewModel 24/24, CSceneEntity 15/15, CLightGlow 10/10,
CTFGrenadePipebombProjectile 8/8, CTFTeam 4/4, CTFRagdoll 4/4, CVoteController 3/3,
CParticleSystem 3/3, CTFRocketLauncher 3/3, CTFShovel 3/3
```

`CTFWearable` is the cosmetics — hats, badges, medals — and **all thirty-seven of them have no
position at all**. The carried weapons sit beside them: every live `CTFRocketLauncher` and
`CTFShovel` is origin-less too.

The full property set of one carried weapon, which is the part worth staring at:

```
CTFWeaponBuilder#404:
  DT_AttributeContainer.m_hOuter
  DT_BaseAnimating.m_nSequence
  DT_BaseCombatWeapon.m_iState
  DT_BaseEntity.m_fEffects
  DT_BaseEntity.m_flSimulationTime
  DT_LocalActiveWeaponData.m_flNextPrimaryAttack
  DT_LocalActiveWeaponData.m_flNextSecondaryAttack
  DT_TFWeaponBuilder.m_iBuildState
```

No `m_vecOrigin`. No `m_nModelIndex`. And — this is what killed the parent-resolution theory —
**no `moveparent` either**. There is nothing on the wire to resolve a position from, which means
the position is not meant to be resolved. It is meant to be *taken*.

## Why Valve sends nothing

*Evidence class: read from published source (source-sdk-2013).*

`CBaseCombatWeapon::Equip` (`shared/basecombatweapon_shared.cpp:983`):

```cpp
void CBaseCombatWeapon::Equip( CBaseCombatCharacter *pOwner )
{
    SetAbsVelocity( vec3_origin );
    RemoveSolidFlags( FSOLID_TRIGGER );
    FollowEntity( pOwner );
    SetOwner( pOwner );
    SetOwnerEntity( pOwner );
```

and `CBaseEntity::FollowEntity` (`shared/baseentity_shared.cpp:2360`):

```cpp
    SetParent( pBaseEntity );
    SetMoveType( MOVETYPE_NONE );

    if ( bBoneMerge )
        AddEffects( EF_BONEMERGE );

    AddSolidFlags( FSOLID_NOT_SOLID );
    SetLocalOrigin( vec3_origin );
    SetLocalAngles( vec3_angle );
```

`EF_BONEMERGE` is `0x001` (`public/const.h:284`). A bone-merged entity does not have a transform of
its own: the client walks the child model's bones, finds the bone of the same **name** on the
parent, and uses the parent's matrix outright. The child's own origin and angles are set to zero by
`FollowEntity` precisely because nothing will ever read them. Sending them would be sending zero.

So "no origin" is not a gap in the recording. It is the format saying *this thing is wherever its
owner's skeleton says it is*, and it is the same mechanism for a hat and for a rocket launcher.

Confirmed on the wire at that tick: **32 entities carry `EF_BONEMERGE`, 60 name an owner entity,
41 carry a model index.**

## What this costs us, and what it does not

It is not a decode problem — every bit needed is already being read. It is an emulation problem, and
it lands on infrastructure that already exists: `StudioBones.Remap` matches bones by name between
two skeletons, which is exactly what a bone merge is. What is genuinely new:

- **The owner link.** `m_hOwnerEntity` for weapons; wearables need checking separately.
- **Model resolution for entities with no `m_nModelIndex`.** 41 of the origin-less entities carry
  one and the rest do not, so for those the model comes from the item definition through the
  attribute container rather than from the precache.
- **Which weapon is the active one.** A player carries several and holds one; the rest are drawn by
  nobody.

Filed as B63.

## The second half: the models were in a table nobody was reading

Recording the attachment was not enough. With attachments recorded, **29 of the demo's cosmetics
still failed to produce a track**, and the reason had nothing to do with attachment:

```
WEAP wearable models: 1 resolve, 35 do not (index -48; index -52; index -12; index -16; index -92)
```

Every cosmetic's `m_nModelIndex` is **negative**. That looked like a decode bug — a signed read of an
unsigned field — and it is not. `SendPropModelIndex` (`public/dt_send.h:715`) is

```cpp
return SendPropInt( pVarName, offset, sizeofVar, SP_MODEL_INDEX_BITS, 0 );
```

flags `0`, so the field is genuinely **signed**, 13 bits. And `ivmodelinfo.h:90` says what a negative
one means:

```cpp
// If index >= 0, then index references the precached model string table
// If index == -1, then the model is invalid
// If index < -1, then the model is DYNAMIC and has a DYNAMIC INDEX of (-2 - index)
// - if the dynamic index is ODD, then the model is CLIENT ONLY
//   and has a m_LocalDynamicModels lookup index of (dynamic index)>>1
// - if the dynamic index is EVEN, then the model is NETWORKED
//   and has a dynamic model string table index of (dynamic index)>>1
```

So `-48` is dynamic index 46, even, therefore **networked**, therefore entry 23 of a second string
table. This project had recorded the opposite conclusion in `ModelPrecache.Path`: that a negative
index is a model the recording client loaded for itself and is therefore absent from a demo of
someone else's session. That is true of the odd half and false of the even half, and the even half
is where every cosmetic lives.

The table's name is in no published header, because it is created engine side. The demos name it
themselves — every modern recording lists its own string tables:

```
decalprecache(133) downloadables(1) DynamicModels(60) EffectDispatch(3) GameRulesCreation(1)
genericprecache(1) InfoPanel(2) instancebaseline(63) lightstyles(64) Materials(8)
modelprecache(1212) ParticleEffectNames(5870) Scenes(4414) ...
```

`DynamicModels`, 60 entries. Reading it turns 3 attached props at the midpoint tick into **29**, with
names that settle the question by themselves: `hwn_spellbook_complete`, `short2014_ninja_boots`,
`dec21_festive_frames_Scout`, `all_domination_Sniper`, `brasil_fortress_6v6_primeiro_lugar_Sniper`.

The odd, client-only half stays unresolved deliberately. Halving it anyway lands on a real entry of
the networked table and draws a confidently wrong model.

## The wrong turns, kept

1. **"The demo does not carry cosmetics."** It carries thirty-seven of them at one tick.
2. **"Carried weapons have parent-relative origins; resolve `m_hMoveParent`."** They have no origin
   and no move parent. This one survived a whole round of work because it *sounds* like how
   parenting works in Source, and parenting does work that way — for entities that are parented
   rather than merged. The distinction is `EF_BONEMERGE`.
3. **"1 weapon is present mid-match."** Measured at a tick the demo does not contain.
4. **"An owner handle means the entity is attached."** A syringe knows which medic fired it through
   the same `m_hOwnerEntity` a held weapon uses. Reading ownership as attachment claimed 220
   syringe projectiles as worn items. Attachment is `moveparent`, or `m_hOwnerEntity` *and*
   `EF_BONEMERGE` — and which of the two an entity sends depends on what it is: a `CTFWearable`
   sends a parent and no effects field at all, a carried `CTFRocketLauncher` sends the effects flag
   and no parent. Either rule alone covers half the problem while looking complete, because the
   half it misses simply does not draw.
5. **"A negative model index is a client-only model absent from the demo."** True of the odd half
   only. This one had been written down as settled, in a doc comment, with reasoning — which is
   exactly the kind that gets repeated.

The pattern across all three: each was a confident answer to a question the measurement had not
been pointed at. Only the property dump — asking the entity what it holds rather than asking
whether a field we had already named was present — settled it.

## An open observation, deliberately not turned into a test

*Evidence class: measured on the corpus, and CONFOUNDED — see below.*

The probe that produced the findings above was deleted on 2026-08-19, because it asserted nothing
and its fallback path measured the wrong thing. Its last output is worth keeping, and worth
labelling honestly:

```
tf2-2007-build3258-pov-cp_granary.dem   110 prop tracks, 57 models, weapons:
tf2-2008-build3420-pov-cp_granary.dem    75 prop tracks, 43 models, weapons: 1x w_stickybomb_launcher.mdl
```

The 2007 recording carries **no carried-weapon entity at all**; the 2008 one carries a sticky
launcher for 220 ticks. That looks like an era change in how weapons are networked, and it may well
be one.

**It is not evidence of that, and the reason is worth stating.** One demo per build, recorded in
different sessions by a player who was doing different things. A demoman who never switched weapons
and a scout who never fired produce the same "no weapon entity" reading as a build that does not
send them. Attributing the difference to the build is the confound this project has walked into
before — a measurement whose condition varies on more than the axis being tested.

Settling it needs the pairing the corpus already uses elsewhere: two recordings of **one session**,
or two builds recorded doing the **same thing**. Until then this is a question, not a finding, and
it is written here rather than asserted in a test so that nobody has to rediscover the confound to
explain why the test is failing.
