---
name: a-player-has-two-viewmodels
description: "A weapon is never one model — two viewmodel slots, two model indices, and two first-person schemes; reading the wrong one has caused three separate defects."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:53:57.677Z
---

Three merged memories, same trap: **a weapon is never one model, and picking the wrong one draws
something plausible in the wrong place.**

## Two viewmodel SLOTS

**A TF2 player carries two viewmodel entities at once.** `MAX_VIEWMODELS` is 2 (`shareddefs.h:325`):
slot 0 is the weapon in hand, slot 1 the off hand (`CTFPlayer::GetOffHandViewModel` =
`GetViewModel(1)`). Only `CTFWeaponInvis` (spy's Invis Watch) and `tf_weaponbase_grenade` claim slot
1. Slot arrives as `m_nViewModelIndex`, 1 bit, present since 2007.

**Grenades are a false second case** — `tf_weaponbase_grenade.cpp:74` calls
`SetViewModelIndex(1)` but TF2's throwables were cut pre-release; no shipped item uses the class.
Owner: *"this isnt tf1, tf2 only has the spy watch for offhand"*. Same shape as `$modblend`
([[nothing-is-closed]]): SDK declaration ≠ game behaviour.

**Both are on screen together** — owner: *"main viewmodel doesnt get hidden when a spy goes invis,
the watch just comes up... the watch is the left hand, the weapon in the right, unless left handed."*
A slot-blind lookup keeps whichever entity it walked past last — right by luck usually, but put
`v_watch_spy` in a soldier's hands on the 2009 badlands POV, held across a class change.
`ViewmodelAt`/`OffHandViewmodelAt` share one walk (both drawn, D42).

**A slot-1 entity is not "a watch in a hand"** — every player carries both for their whole life
(z1800: 23 slot-1 entities, two spies, 22 with model index 0). `EF_NODRAW`
(`CTFWeaponInvis::SetWeaponVisible`, set on the VIEWMODEL not the weapon) separates "exists" from
"draw it" — arrives via `DT_BaseViewModel.m_fEffects`, which NOBASE misread as "declares nothing"
rather than "inherits nothing" ([[a-property-name-needs-its-declaring-table]]). After the fix: 190 of
9,165 sampled player-ticks, three models, all spy watches.

**Filter on the slot always** — absent `m_nViewModelIndex` means slot 0 (`CBaseViewModel`'s
constructor default, see [[sentinels-conflate-unknown-with-answer]]). `cl_flipviewmodels` is the
watcher's setting, not the recording's — affects only cull mode at draw time, never the lookup.
Caught by cross-checking model path against `m_iClass` ([[fixtures-are-the-weak-point]]).

## `a-viewmodel-is-one-model-or-two` — two exclusive first-person schemes

`CTFWeaponBase::GetViewModel` (`tf_weaponbase.cpp:651`):
```cpp
if ( pPlayer && pItem->IsValid() && pItem->GetStaticData()->ShouldAttachToHands() )
    return pPlayer->GetPlayerClass()->GetHandModelName( iHandModelIndex );
return GetTFWpnData().szViewModel;
```
- **attaches to hands**: viewmodel IS the class's hands (`model_hands`, `tf_classdata.cpp:149`);
  weapon's `c_` model is a separate `C_ViewmodelAttachmentModel`. Two models.
- **does not**: viewmodel is the weapon's own `v_` model with hands modelled in. One model. Adding an
  attachment draws the gun twice.

**Must come from the demo, not the schema** — `attach_to_hands` describes the item TODAY (stickybomb
launcher didn't attach in 2011); ask the recording which branch the engine took, not the installed
`items_game.txt` ([[the-demo-dates-its-own-fields]]). Symptom: two identical weapons at one point in
space; log line `viewmodel scheme:` names the branch taken.

## `a-carried-weapon-has-two-model-indices` — and the world one is not `m_nModelIndex`

`CBaseCombatWeapon` networks TWO model indices (`basecombatweapon_shared.cpp:290,2870`): a carried
weapon's `m_nModelIndex` is the VIEW model; `m_iWorldModelIndex` is what the client draws in the world
(`tf_weaponbase.cpp:2144`). Reading `m_nModelIndex` for weapons resolved all three of a soldier's
weapons to `c_soldier_arms.mdl`, drawn stacked on the real viewmodel — presented as a first-person
visibility bug, eight theories died to it.

**When an entity's model looks wrong, check for a second networked model index before theorising
about visibility.** `DT_BaseViewModel` is `BEGIN_NETWORK_TABLE_NOBASE`, sends no origin, carries
owner as `m_hOwner` not `m_hOwnerEntity`.

Related: [[ask-whether-the-data-arrived]], [[nothing-is-closed]],
[[the-client-builds-what-the-demo-omits]], [[author-the-specimen-the-corpus-lacks]],
[[bone-merge-sends-no-position]], [[negative-model-indices-are-dynamic]].
