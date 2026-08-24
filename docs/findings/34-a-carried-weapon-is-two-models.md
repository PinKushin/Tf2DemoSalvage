# 34 — A carried weapon is two models, and `m_nModelIndex` is the wrong one

*Evidence class: read from published source (`source-sdk-2013`), confirmed by measurement on the
local corpus.*

## The symptom, and how badly it described the cause

A player in first person saw **two weapons overlapping**. Reported as "thats 2 sticky launchers
overlapping each other", and separately "soldier has a weird glitch no idea what it is". It happened
in point-of-view recordings and in SourceTV recordings alike, and it stopped when the player died.

Every one of those facts pointed somewhere useful and none of them pointed here. Nine theories were
formed and eight were wrong:

| Theory | Killed by |
|---|---|
| the viewmodel pass is unwired | it logs two props and two instances every frame |
| the viewmodel is placed wrongly | `CalcViewModelView` puts it at the eye, which is where ours is |
| `$includemodel` brings geometry as well as sequences | reading the merge: it reads only `StudioSequences` and pose parameters |
| the arms model packs the weapon | true for the soldier, and `c_demo_arms.mdl` has no weapon material at all |
| all bodygroup alternatives are drawn | measured: one alternative per part, and the loaded grenade is correctly hidden |
| the world weapon is not hidden in first person | the ownership rule is right; see below |
| the entity is a `DT_BaseViewModel` in the prop list | those send no origin and never reach it |
| the entity is a `CTFWearableVM` | measured: the class name is `CTFRocketLauncher` |

The pattern is worth naming because it cost a day: **a screenshot supports a claim about what it
shows**, and each theory promoted it to a claim about the system.

## What it actually is

A weapon has **two** model indices, and they are separate networked properties.
`basecombatweapon_shared.cpp:290`:

```cpp
m_iViewModelIndex  = 0;
m_iWorldModelIndex = 0;
if ( GetViewModel() && GetViewModel()[0] )
    m_iViewModelIndex  = CBaseEntity::PrecacheModel( GetViewModel() );
if ( GetWorldModel() && GetWorldModel()[0] )
    m_iWorldModelIndex = CBaseEntity::PrecacheModel( GetWorldModel() );
```

Both go on the wire — `SendPropModelIndex( SENDINFO(m_iWorldModelIndex) )`, line 2870 — and the
client draws the world model from the second, `modelinfo->GetModel( m_iWorldModelIndex )` at
`tf_weaponbase.cpp:2144`.

**A carried weapon's own `m_nModelIndex` is its VIEW model.** So a reader that resolves every
entity's model through `DT_BaseEntity.m_nModelIndex` gets, for a weapon, the first-person arms.

Measured on `movement-test-pov-cp_process`, all three of one soldier's weapons:

```
entity 228  CTFRocketLauncher   -> models/weapons/c_models/c_soldier_arms.mdl
entity 376  CTFShotgun_Soldier  -> models/weapons/c_models/c_soldier_arms.mdl
entity 380  CTFShovel           -> models/weapons/c_models/c_soldier_arms.mdl
```

Three different weapons, one pair of disembodied arms, drawn **in the world at the owner's hand**.
After reading `m_iWorldModelIndex`:

```
entity 376  CTFShotgun_Soldier  -> models/weapons/c_models/c_shotgun.mdl
entity 380  CTFShovel           -> models/weapons/c_models/c_pickaxe.mdl
```

## Why every confusing detail follows from that

- **Both POV and SourceTV.** Nothing about it is a camera or a visibility rule, so no recording type
  escapes it.
- **It stops on death.** The weapon entity leaves the player's hand, so the misresolved model stops
  being drawn there. That looked like a visibility rule engaging and was simply the hand emptying.
- **The extra weapon is untextured.** It is not a weapon. It is a pair of arms, whose materials are
  first-person content the world material path does not resolve.
- **The soldier's "weird glitch".** `c_soldier_arms.mdl` contains `w_rocketlauncher/w_rocket01`
  with no bodygroup that could hide it (finding 33) — so the arms drawn in the world bring a rocket
  with them.
- **A demoman saw two launchers rather than arms.** With three weapons all resolving to the same
  arms model, what is stacked at the hand is several copies of one model, not several weapons. The
  identification in the report was of the *silhouette*, and the silhouette was wrong.

## The ownership rule was correct and fixed nothing

`C_BaseCombatWeapon::ShouldDraw` hides a weapon owned by the player whose eyes you are in, because
the viewmodel draws it. That was implemented against `m_hOwnerEntity`, matches the SDK, and changed
nothing — which is exactly the shape of a rule applied to data that never arrived.

It had not, but not for the reason assumed. The probe that should have been written first shows
`m_hOwnerEntity` **does** arrive, for 3 of 4 weapon tracks. The rule fires; it just cannot help,
because the entity it correctly hides in first person is *also* drawn wrongly everywhere else.

A viewmodel, separately, sends `m_hOwner` on `DT_BaseViewModel`
(`baseviewmodel_shared.cpp:568`) — a different property in a different table, and
`BEGIN_NETWORK_TABLE_NOBASE`, so it inherits no `DT_BaseEntity` at all. Worth knowing before the next
handle is read.

## Method

The two facts that ended this both came from instruments that could fail, written after four fixes
that could not:

1. An offline test reading Valve's shipped models out of `tf2_misc_dir.vpk` — which killed the arms
   theory for the demoman in twenty minutes (finding 33).
2. A corpus probe printing the class name beside the resolved model. Two guesses had already been
   made about what those entities were, and both were wrong; printing the answer took one line.

**Ask whether the data arrived before theorising about the rule that reads it** is already a memory
in this project. It was not applied here, and the cost was four fixes aimed at a correct rule.
