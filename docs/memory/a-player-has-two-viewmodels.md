---
name: a-player-has-two-viewmodels
description: MAX_VIEWMODELS is 2; slot 1 is the off hand and is drawn alongside the weapon, not instead of it.
metadata:
  type: project
---

**A TF2 player carries two viewmodel entities at once.** `MAX_VIEWMODELS` is 2
(`shareddefs.h:325`); slot 0 is the weapon in hand and slot 1 is the off hand —
`CTFPlayer::GetOffHandViewModel` is `return GetViewModel( 1 )`. Only two things claim slot 1:
`CTFWeaponInvis::Spawn` (the spy's Invis Watch) and `tf_weaponbase_grenade`. Which slot an entity is
arrives as `m_nViewModelIndex`, 1 bit unsigned, present on every corpus demo back to 2007.

**Grenades are a false second case.** `tf_weaponbase_grenade.cpp:74` does call `SetViewModelIndex(1)`
and reads as evidence, but TF2's throwables were cut before release and no shipped item names the
class — living SDK code that nothing exercises. The owner: "this isnt tf1, tf2 only has the spy
watch for offhand". Same shape as `$modblend` ([[shipped-data-is-a-source]]): a declaration in
source is not proof of a behaviour in the game.

**Both are on screen together.** From the owner: "main viewmodel doesnt get hidden when a spy goes
invis, the watch just comes up and everything goes transparent", and "the watch is the left hand,
the weapon in in the right, unless you use left handed viewmodels, then its the opposite".

**Why:** a lookup blind to the slot keeps whichever entity it walked past last. That is right by
luck on every demo carrying one viewmodel, and on the 2009 badlands POV it put `v_watch_spy` in a
soldier's hands and held it there across a change of class. `ViewmodelAt` and `OffHandViewmodelAt`
share one walk, since the owner rule is identical and duplicating it is how the two would diverge.
Both are drawn (D42).

**A slot-1 entity is not a watch in a hand, and this is the part that surprises.** Every player
carries BOTH viewmodels for their whole life — z1800 sends 23 slot-1 entities in a match with two
spies, 22 of them with model index 0. What separates "exists" from "draw it" is `EF_NODRAW`, which
`CTFWeaponInvis::SetWeaponVisible` sets on the VIEWMODEL rather than on the weapon. It arrives on
`DT_BaseViewModel.m_fEffects` — a property this project had recorded as not existing, because NOBASE
was read as "declares nothing" rather than "inherits nothing"
([[a-property-name-needs-its-declaring-table]]).

Measured after the fix: 190 of 9,165 sampled player-ticks, three models, all spy watches.

**How to apply:** anything reading a viewmodel filters on the slot. An absent `m_nViewModelIndex`
means slot 0, because `CBaseViewModel`'s constructor sets it to zero — see
[[sentinels-conflate-unknown-with-answer]] for why that direction matters. `cl_flipviewmodels`
belongs to the person watching, not to the recording, so handedness never affects the lookup — only
the cull mode at draw time. The defect was caught by cross-checking the model path against the
player's networked `m_iClass` ([[two-recordings-of-one-value]]); the same test also showed the
owner's recollection of never playing sniper on a 2013 demo was wrong and the decode was right.
