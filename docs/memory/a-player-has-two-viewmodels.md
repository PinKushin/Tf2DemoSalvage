---
name: a-player-has-two-viewmodels
description: A weapon is never one model — two viewmodel slots, two model indices, and two first-person schemes; reading the wrong one has caused three separate defects.
metadata:
  type: project
---

**Two memories were merged into this one on 2026-08-27** — `a-viewmodel-is-one-model-or-two` and
`a-carried-weapon-has-two-model-indices`. All three are the same trap seen three ways: **a weapon is
never one model, and picking the wrong one draws something plausible in the wrong place.**

## Two viewmodel SLOTS

**A TF2 player carries two viewmodel entities at once.** `MAX_VIEWMODELS` is 2
(`shareddefs.h:325`); slot 0 is the weapon in hand and slot 1 is the off hand —
`CTFPlayer::GetOffHandViewModel` is `return GetViewModel( 1 )`. Only two things claim slot 1:
`CTFWeaponInvis::Spawn` (the spy's Invis Watch) and `tf_weaponbase_grenade`. Which slot an entity is
arrives as `m_nViewModelIndex`, 1 bit unsigned, present on every corpus demo back to 2007.

**Grenades are a false second case.** `tf_weaponbase_grenade.cpp:74` does call `SetViewModelIndex(1)`
and reads as evidence, but TF2's throwables were cut before release and no shipped item names the
class — living SDK code that nothing exercises. The owner: *"this isnt tf1, tf2 only has the spy
watch for offhand"*. Same shape as `$modblend` ([[nothing-is-closed]]): a declaration in source is
not proof of a behaviour in the game.

**Both are on screen together.** From the owner: *"main viewmodel doesnt get hidden when a spy goes
invis, the watch just comes up and everything goes transparent"*, and *"the watch is the left hand,
the weapon in in the right, unless you use left handed viewmodels, then its the opposite"*.

A lookup blind to the slot keeps whichever entity it walked past last. That is right by luck on every
demo carrying one viewmodel, and on the 2009 badlands POV it put `v_watch_spy` in a soldier's hands
and held it there across a change of class. `ViewmodelAt` and `OffHandViewmodelAt` share one walk,
since the owner rule is identical and duplicating it is how the two would diverge. Both are drawn
(D42).

**A slot-1 entity is not a watch in a hand, and this is the part that surprises.** Every player
carries BOTH viewmodels for their whole life — z1800 sends 23 slot-1 entities in a match with two
spies, 22 of them with model index 0. What separates "exists" from "draw it" is `EF_NODRAW`, which
`CTFWeaponInvis::SetWeaponVisible` sets on the VIEWMODEL rather than on the weapon. It arrives on
`DT_BaseViewModel.m_fEffects` — a property this project had recorded as not existing, because NOBASE
was read as "declares nothing" rather than "inherits nothing"
([[a-property-name-needs-its-declaring-table]]). Measured after the fix: 190 of 9,165 sampled
player-ticks, three models, all spy watches.

**Anything reading a viewmodel filters on the slot.** An absent `m_nViewModelIndex` means slot 0,
because `CBaseViewModel`'s constructor sets it to zero — see
[[sentinels-conflate-unknown-with-answer]] for why that direction matters. `cl_flipviewmodels`
belongs to the person watching, not to the recording, so handedness never affects the lookup — only
the cull mode at draw time. The defect was caught by cross-checking the model path against the
player's networked `m_iClass` ([[two-recordings-of-one-value]]); the same test also showed the
owner's recollection of never playing sniper on a 2013 demo was wrong and the decode was right.

---

## `a-viewmodel-is-one-model-or-two` — two exclusive first-person schemes

TF2 has two, and `CTFWeaponBase::GetViewModel` (`tf_weaponbase.cpp:651`) is the whole rule:

```cpp
if ( pPlayer && pItem->IsValid() && pItem->GetStaticData()->ShouldAttachToHands() )
    return pPlayer->GetPlayerClass()->GetHandModelName( iHandModelIndex );
return GetTFWpnData().szViewModel;
```

- **attaches to hands** — the viewmodel IS the class's hands (`model_hands` in
  `scripts/playerclasses/<class>`, read by `tf_classdata.cpp:149`), and the weapon's `c_` model is a
  separate `C_ViewmodelAttachmentModel`. **Two models.**
- **does not** — the viewmodel is the weapon's own `v_` model, which has the hands modelled into it.
  **One model.** Adding an attachment draws the gun twice.

**It must come from the demo, not the schema.** `attach_to_hands` describes the item as it is
*today*. The stickybomb launcher attaches to hands now and did not in 2011, so asking the installed
`items_game.txt` about a 2011 recording returns a confident wrong answer. The recording says which
branch the engine actually took, because it networks the viewmodel's model — compare it against the
class's hands. See [[the-demo-dates-its-own-fields]].

**The symptom is two identical weapons at one point in space.** The log line `viewmodel scheme:`
names the networked model and the hands, so it says which branch was taken.

---

## `a-carried-weapon-has-two-model-indices` — and the world one is not `m_nModelIndex`

`CBaseCombatWeapon` precaches and networks **two** model indices
(`basecombatweapon_shared.cpp:290`, `SendPropModelIndex(SENDINFO(m_iWorldModelIndex))` at 2870).
A carried weapon's own `m_nModelIndex` is the **view** model; the world model is
`m_iWorldModelIndex`, which is what the client draws from (`tf_weaponbase.cpp:2144`).

Reading `m_nModelIndex` for weapons made all three of a soldier's weapons resolve to
`c_soldier_arms.mdl` and be drawn in the world at his hand, stacked on the real viewmodel.

**It presented as a first-person visibility bug and it is not one** — POV and SourceTV alike,
stopping on death only because the hand empties. Eight theories died to it, all supported by the
screenshots.

**When an entity's model looks wrong, check whether its class networks a second model index before
theorising about visibility.** A viewmodel is separate again: `DT_BaseViewModel` is
`BEGIN_NETWORK_TABLE_NOBASE`, sends no origin, and carries its owner as `m_hOwner`, not
`m_hOwnerEntity`.

---

Related: [[ask-whether-the-data-arrived]], [[measure-every-hop-before-blaming-one]],
[[the-client-builds-what-the-demo-omits]], [[check-backwards-compat-on-old-demos]],
[[bone-merge-sends-no-position]], [[negative-model-indices-are-dynamic]].
