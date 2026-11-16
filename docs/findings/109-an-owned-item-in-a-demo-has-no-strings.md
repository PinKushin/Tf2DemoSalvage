# 109 — An owned item in a demo has no strings

2026-11-16. B758, B759, B760.

## What the engine does

`CEconItemView::IterateAttributes` (econ_item_view.cpp:600-645) walks an item's own attribute list, then exactly one
fallback. With no backpack — every item a demo watcher sees, since nothing subscribes to their inventories — the SOC
branch (:628) is never taken, so the fallback is:

- the networked demo list, `m_NetworkedDynamicAttributesForDemos`, when the item has a valid ID and that list is not empty
  (:633); the comment above it says it "only copies the default type and floats";
- otherwise the definition's own attributes (:641), strings included.

So every STRING attribute of an owned item is unreachable on a demo client: `FindAttribute( def, CAttribute_String* )`
finds nothing, and `CALL_ATTRIB_HOOK_STRING` gets nothing from the weapon's own item. *Read from published source.*

What that takes away, each a reader that used to read the definition directly (B758, B760):

| reader | engine | an owned item gets |
|---|---|---|
| StatTrak module (`GetStattrak`) | econ_item_interface.cpp:483-502 | no module model; the strange icon, not the stat-clock one |
| item meter label | tf_hud_itemeffectmeter.cpp:250 | the loadout slot's default label |
| min-mode viewmodel offset | tf_weaponbase.cpp:6942 | no offset |

The stat clock's DESCRIPTION line is not among them: "Attrib_stattrakmodule" depends only on the paint kit index, a float
attribute (econ_item_description.cpp:1146-1151). Real TF2 drew "Hale's Own Stat Clock Attached" over an owned strange war
paint at `rgl-pug-2026-08-10-pov` tick 32774, which the corpus test now asserts with the game's own strings.

The rule lives once, `EconAttributes.ReadsDemoList`.

## A colour stream that runs past its text

`CItemModelPanel::UpdateDescription` builds the attribute TEXT with collection lines left out unconditionally
(item_model_panel.cpp:2628-2640). It then builds the colour-change STREAM, where a collection line goes to the
collection labels only if BOTH exist (:2757); otherwise it falls to the attribute branch and advances the attribute
stream's index. The shipped `resource/ui/econ/itemmodelpanel.res` declares no collection label (*read from shipped data*,
through the `game-file` probe), so in TF2 an item with collection lines gets colour changes indexed past text that is not
there. Ported as it is. No collection line is generated here yet (B754), so nothing can reach it today.

## Two confident review findings that were wrong

A Fable review of the merged B751 found five real bugs that two earlier reviews had missed. It also said the freeze
panel's test fixture was not the shipped `FreezePanel_Basic.res` block: the shipped one, it said, has
`use_item_rendertarget "0"`, `xpos "r200"`, `text_ypos "10"` and no `attriblabel`. The shipped file, read through the
`game-file` probe, has `use_item_rendertarget "1"`, `xpos "r270"`, `ypos "300"` and the `attriblabel` block, exactly as the
fixture did. The values it quoted are close to `Spectator.res`'s `itempanel` (B756).

A later review said a test for a model-less render-target icon passed for the wrong reason. Restoring the old guard alone
reddened it: the fixture's `use_item_rendertarget 1` makes `LoadData` paint into the render target, which reaches the
guard.

Both were predictions from reading. A review's quoted game data and its "this test cannot fail" are claims to check —
the first with the shipped file, the second by breaking the code — before acting on them.
