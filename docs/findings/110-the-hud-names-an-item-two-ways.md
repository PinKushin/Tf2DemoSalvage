# 110 — The HUD names an item two ways, and one of them passes a quality as a bool (B757)

## What was believed first

B757, filed while reviewing B751, said the HUD's four item names took the definition alone and so lost the strange,
killstreak and festive prefixes that `CEconItemView::GetItemName()` builds from an item's attributes. The fix it
proposed was to hand each call site the item and name it through the description, as the freeze panel's item panel
does.

That was half right. *Read from published source* (an engine-reader pass over the four call sites, 2026-11-16).

## What the engine does

Two of the four call `GetItemName()`:

- the medic's "weapon blocks healing" line (`c_tf_player.cpp:9819-9821`), on the target's active weapon;
- the medic's charge line, `#TF_playerid_mediccharge_wpn` (`:9792-9794`), on the medigun, when its `GetItemQuality()` is
  not `AE_NORMAL`.

`GetItemName()` (`econ_item_view.cpp:1131-1145`) is the description's name line: the item's own attributes, at
`GetMarketQuality()`. With no backpack in a demo that quality is `AE_NORMAL` (B755), so there is no "Strange" and no "The".
The killstreak, festive and australium parts all come from attributes, and those stay.

The other two never touch the item's attributes:

- the dropped weapon's ID (`tf_hud_target_id.cpp:953`, `:957`);
- the "carrying" panel for a weapon the player does not own (`tf_hud_playerstatus.cpp:323`, the file that holds
  `CTFHudPlayerClass`; there is no `tf_hud_playerclass.cpp`).

Both call:

```cpp
CEconItemLocalizedFullNameGenerator( GLocalizationProvider(), pItem->GetItemDefinition(), pItem->GetItemQuality() ).GetFullName()
```

and the constructor is declared (`econ_item_description.h:354`):

```cpp
CEconItemLocalizedFullNameGenerator( const CLocalizationProvider *pLocalizationProvider, const CEconItemDefinition *pItemDef, bool bUseingHashContext = true, entityquality_t eQuality = AE_UNIQUE );
```

**The quality lands in the third parameter, a `bool`.** `eQuality` is never passed, so it is always `AE_UNIQUE`. The
definition is wrapped in `CGameItemDefinition_EconItemInterfaceWrapper` (`econ_item_description.cpp:3771-3809`), whose
`GetQuality()` is that `AE_UNIQUE` and whose attributes are the definition's alone. So:

- the name is the definition's unique name, with no prefix from the item's own attributes;
- `bUseProperName` is `m_iEntityQuality != 0`, so nearly every weapon (quality 6) gets "The" when the definition has
  `propername`, and a stock weapon (quality 0) does not.

This is what TF2 shows. Read the way the code compiles, the call is "the article for anything that is not normal",
which is a sensible reading. That rule decided it, not the parameter name
(`docs/memory/a-reading-that-makes-valve-absurd-is-the-reading.md`).

## What we had

All four sites went through one `HudViewport.ItemName(definition)`, which named the definition at `AE_NORMAL`. That got
the wrong one of the two routes at every site:

- the dropped weapon and "carrying" lacked the article;
- the noheal and charge lines lacked the item's attribute prefixes.

The noheal line also had a branch that the engine never reaches. It printed "#TF_playerid_noheal_unknown" when no
definition arrived, but `GetAttributeContainer()->GetItem()` is the weapon's embedded `m_Item`, never null. Without a
definition the engine names the item "".

## The fix

`HudViewport.ItemName(SceneItem)` is `GetItemName()`, through `TfItemDescription.FullItemName`.
`HudViewport.DefinitionName(definition, entityQuality)` is the generator, through `TfItemDescription.DefinitionFullName`,
the same entry the description's set list already used for its members. `TfItemName` is retired.
