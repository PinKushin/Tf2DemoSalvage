---
name: a-viewmodel-is-one-model-or-two
description: A v_ model contains the hands and is drawn alone; a c_ model is hands plus a separate weapon. Decide from what the demo networked, not from today's item schema.
metadata:
  type: project
---

TF2 has two exclusive first-person schemes, and `CTFWeaponBase::GetViewModel`
(`tf_weaponbase.cpp:651`) is the whole rule:

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

**Why it must come from the demo, not the schema:** `attach_to_hands` describes the item as it is
*today*. The stickybomb launcher attaches to hands now and did not in 2011, so asking the installed
`items_game.txt` about a 2011 recording returns a confident wrong answer. The recording says which
branch the engine actually took, because it networks the viewmodel's model — compare it against the
class's hands. See [[the-demo-dates-its-own-fields]].

**How to apply:** the symptom is two identical weapons at one point in space. The log line
`viewmodel scheme:` names the networked model and the hands, so it says which branch was taken.
Related: [[a-player-has-two-viewmodels]], [[the-client-builds-what-the-demo-omits]],
[[check-backwards-compat-on-old-demos]].
