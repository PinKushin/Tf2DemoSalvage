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

**Both are on screen together.** From the owner: "main viewmodel doesnt get hidden when a spy goes
invis, the watch just comes up and everything goes transparent", and "the watch is the left hand,
the weapon in in the right, unless you use left handed viewmodels, then its the opposite".

**Why:** a lookup blind to the slot keeps whichever entity it walked past last. That is right by
luck on every demo carrying one viewmodel, and on the 2009 badlands POV it put `v_watch_spy` in a
soldier's hands and held it there across a change of class. `DemoTimeline.ViewmodelAt` answers with
the main hand only; the off hand is knowingly not drawn yet (D28).

**How to apply:** anything reading a viewmodel filters on the slot. An absent `m_nViewModelIndex`
means slot 0, because `CBaseViewModel`'s constructor sets it to zero — see
[[sentinels-conflate-unknown-with-answer]] for why that direction matters. `cl_flipviewmodels`
belongs to the person watching, not to the recording, so handedness never affects the lookup — only
the cull mode at draw time. The defect was caught by cross-checking the model path against the
player's networked `m_iClass` ([[two-recordings-of-one-value]]); the same test also showed the
owner's recollection of never playing sniper on a 2013 demo was wrong and the decode was right.
