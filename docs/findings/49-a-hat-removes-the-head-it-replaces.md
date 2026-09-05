# 49 — A hat removes the head it replaces

**Subject:** `player_bodygroups` — how a cosmetic edits the player it is worn on, and why the number
it edits cannot be built by addition.

A hat is not drawn on top of a head. It replaces one. The player model ships with the hair, the
headset and the shoes as separate, switchable pieces, and the item that covers a piece turns it off
by name. Nothing in this project did that until B352, so every player wore their cosmetics *and*
everything underneath — which reads as clipping rather than as a missing feature, and is why it sat
unremarked while three animation defects were chased.

## The mechanism is a rebuild, not an edit

```cpp
void CTFPlayerShared::RecalculatePlayerBodygroups( void )
{
    // We have to clear the m_nBody bitfield.
    m_pOuter->m_nBody = 0;
    CTFWeaponBase::UpdateWeaponBodyGroups( m_pOuter, false );
    CEconWearable::UpdateWearableBodyGroups( m_pOuter );
    CTFWeaponBase::UpdateWeaponBodyGroups( m_pOuter, true );
}
```

(`tf_player_shared.cpp:13693`.) It runs from `RecalcBodygroupsIfDirty`, which `C_TFPlayer::DrawModel`
calls at `c_tf_player.cpp:6935` — so the number is thrown away and rebuilt from the equipped set
every time it is dirty, rather than being adjusted when an item is added or removed. Valve's own
comment says why: *"Leaving bits on from previous player classes can have weird effects."*

Each item then resolves its own names against the **wearer**, not against itself
(`econ_entity.cpp:2044`):

```cpp
int iBody = 0;
const char *pszBodyGroup = pItemDef->GetModifiedBodyGroup( 0, i, iBody );
if ( iBody != iState ) continue;
int iBodyGroup = pOwner->FindBodygroupByName( pszBodyGroup );
if ( iBodyGroup == -1 ) continue;
pOwner->SetBodygroup( iBodyGroup, iState );
```

Resolution by name is what makes one hat wearable by nine classes: `hat` is part 0 on the scout and
does not exist at all on the medic, and the item carries no index for either.

## `if ( iBody != iState ) continue;` is the line that gets misread

The schema writes pairs — `"hat" "1"` — and the obvious reading is "set the group named `hat` to
state 1". That is wrong in a way that gives the right answer 1,044 times out of 1,052.

The value is **matched against the pass**, not stored as one. Both callers run at state 1
(`pWpn->UpdateBodygroups( pPlayer, 1 )` at `tf_weaponbase.cpp:6229`, and `nVisibleState = 1` at
`econ_wearable.cpp:317`), so an entry valued 1 applies and an entry valued 0 does not. The eight
shipped entries valued 0 exist for the one case that lowers the state:

```cpp
if ( pItem->ShouldHideForVisionFilterFlags() )
{
    // Items that shouldn't draw (pyro-vision filtered) shouldn't change any body group states
    // unless they have no model (hatless hats)
    nVisibleState = 0;
}
```

An item hidden by pyro-vision puts the part **back**. A demo carries no vision filter, so those
eight entries are unreachable here — but a reader that treats the pair as an assignment applies
them and removes a piece the engine leaves alone.

**Evidence class: read-from-source** for the rule, **measured** for the split (1,044 entries valued
1, 8 valued 0, across 747 items).

## The three passes exist for eight items

Written out, `UpdateWeaponBodyGroups` runs twice with a flag and skips every weapon whose item
disagrees with it:

```cpp
const bool bHideBodygroupsDeployedOnly = pScriptItem->GetStaticData()->GetHideBodyGroupsDeployedOnly();
if ( bHideBodygroupsDeployedOnly != bHandleDeployedBodygroups ) continue;
if ( bHideBodygroupsDeployedOnly && pPlayer->GetActiveWeapon() != pWpn ) continue;
```

Eight shipped items set `hide_bodygroups_deployed_only`, and **all eight are weapons** — the Fists
of Steel, the KGB, Apoco-Fists, the Holiday Punch, the Bread Bite, the GRU MvM variant, one style
entry, and the Short Circuit. It is the mechanism behind a detail people notice without naming: the
Fists of Steel's oversized hands appear when you pull them out and vanish when you put them away.

For everything else the passes are indistinguishable, because they all run at state 1 and no two
applied entries can disagree. That is not an excuse to collapse them — it is the reason collapsing
them is *provably* equivalent, which is a different claim and the only one worth making.

## Why the number cannot be summed

`SetBodygroup` is not an OR (`shared/animation.cpp:863`):

```cpp
int iCurrent = ( body / pbodypart->base ) % pbodypart->nummodels;
body = ( body - ( iCurrent * pbodypart->base ) + ( iValue * pbodypart->base ) );
```

The parts are digits of a mixed-radix number: on `scout.mdl`, `hat` has base 1, `headphones` base 2,
`shoes_socks` base 4, `dogtags` base 8. Setting one part means subtracting whatever digit it
currently holds before adding the new one.

**A helper that returns `value * base` for each item, to be added up, is correct exactly until two
items name the same part** — and 457 of the 747 name `hat`, so a player wearing a hat and a misc
that both hide it is the ordinary case, not an edge. Summing gives 2 for a part with two
alternatives, which overruns its digit and carries into the next part. The visible result is that a
*different* piece disappears — one the arithmetic never mentioned, on an item that has nothing to do
with the collision.

This is the second time in this project a mixed-radix field has been treated as a bitfield. Both
times the wrong version worked on every case anyone tried by hand.

## What a class model actually declares

Measured on the shipped models, and the answer is less uniform than the schema implies:

| model | body parts |
|---|---|
| `scout.mdl` | `hat` (base 1), `headphones` (2), `shoes_socks` (4), `dogtags` (8) |
| `demo.mdl` | four parts, including `grenades` (base 4) — no `hat`, no `headphones` |
| `medic.mdl` | **two**: `medic` and `medic_backpack` |
| `spy.mdl` | includes `spyMask` — the part `ValidateModelIndex` writes |

So a medic wearing a hat that declares `hat` and `headphones` has **neither part**, and
`FindBodygroupByName` returns -1 twice. Nothing is hidden and nothing is wrong: the medic's hair is
not a switchable piece. In a 12-player pug, three players read a body number of zero after the fix
for exactly this reason, which is why the measurement had to check the models rather than treating
zero as a failure.

Every part on `scout.mdl` carries a mesh only at alternative 0. Alternative 1 is empty — so
"setting a bodygroup" is literally "drawing nothing there".

## The mask is written by a different function, and it survives

`C_TFPlayer::ValidateModelIndex` sets the spy's mask (`c_tf_player.cpp:9024`), not
`RecalculatePlayerBodygroups`. Two writers of one integer is exactly the arrangement where order
decides the outcome, and the order is settled inside one frame:

- `C_TFPlayer::DrawModel` calls `RecalcBodygroupsIfDirty()` first (`c_tf_player.cpp:6935`), clearing
  and rebuilding from the items;
- then falls through to `C_BaseAnimating::DrawModel`, which calls `ValidateModelIndex()` under
  `TF_CLIENT_DLL` (`c_baseanimating.cpp:3195`).

The mask therefore lands **on top of** the equipment rather than being wiped by it. Because
`SetBodygroup` replaces only its own digit, the hat keeps its own — a disguised spy in a hat reads
as 9 on a model where the mask is 8 and the hat is 1.

## The style arm is dead in a demo, and that is a positive result

`UpdateBodygroups` continues into per-style hiding — `GetAdditionalHideBodygroups`, and a style's
own `bodygroup` override. 102 shipped items declare `additional_hidden_bodygroups`, so it looks like
a substantial gap.

It is unreachable. `GetStyleInfo( pItem->GetStyle() )` bottoms out at `GetSOCData()->GetStyle()`, and
`GetSOCData` finds an inventory only for the account the client is subscribed to — its own
(`econ_item_view.cpp:839`). **A live client watching another player already gets
`INVALID_STYLE_INDEX`**, so the engine takes the same branch we do. In a demo there is no subscribed
inventory at all.

The one real exception is the networked `item style override` attribute, which is entity state
rather than backpack state and does arrive in a demo. That is filed as B234.

This is worth keeping as a pattern: **a mechanism can be absent from our implementation and still be
at parity, because the engine's own preconditions do not hold for a spectator.** Checking that costs
one call chain and converts a plausible defect into a settled question.

## What is not established

Whether the pieces a cosmetic hides look right at close range — a body number is the wrong
instrument for that, and it is the owner's to judge. And `wm_bodygroup_override`, which addresses a
part by index rather than by name on two shipped items, is a real divergence still open as B353.
