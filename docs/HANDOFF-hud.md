# Handoff — the HUD, a VGUI emulation

Started 2026-09-25 on `feat/hud`. The owner's direction: the HUD is a full VGUI emulation, layered and `.res`-driven,
so a real TF2 HUD (stock or `tf/custom`) loads and draws exactly as the game does. 100% parity.

## Where the answers live

- **Published** (`F:/src/source-sdk-2013`): `vgui2/vgui_controls` — Panel, EditablePanel, BuildGroup, Label, ImagePanel,
  AnimationController — and every TF HUD element under `game/client`. TF2 links these statically into `client.dll`,
  so the SDK's copy IS TF2's. Port it; do not decompile it.
- **Closed** (`vgui2.dll`, Ghidra `D:\ghidra-proj\tf2vgui2`, MCP port 8092): `CScheme`, `CSchemeManager`, `VPanel`,
  the border classes. Every function ported is renamed there. `vguimatsurface.dll` (surface, font rasterising) is not
  imported yet.

## Done (bottom layer first)

| Layer | Code | Source |
|---|---|---|
| KeyValues, `#base`, conditionals, resolution keys | `Content/Assets/KeyValuesTree.cs` | `KeyValues.cpp` |
| Scheme colours, base settings | `Scene/Hud/VguiScheme.cs` | `CScheme_LoadFromFile`, `LookupSchemeSetting` |
| Fonts to glyph sets | `Scene/Hud/VguiFonts.cs` | `ReloadFontGlyphs` |
| Borders | `Scene/Hud/VguiBorders.cs` | `CScheme_LoadBorders`, three `ApplySchemeSettings` |
| Position and size strings, `o` form | `Scene/Hud/PanelLayout.cs` | `ComputePos`, `ComputeWide/Tall` |
| Panel tree from `.res` | `VguiPanel.cs`, `VguiEditablePanel.cs` | `Panel::ApplySettings`, `BuildGroup` |
| Animation variables | `VguiPanel.DeclareAnimationVar` | `CPanelAnimationVar` converters |
| Z-order, absolute position, sibling pins, clip | `VguiLayout.cs`, `VguiPanel.ZPos` | `VPanel_SetZPos`, `VPanel_Solve` |

## Next, in order

1. **Painting**: a surface interface (`DrawSetColor`, filled/outlined rect, textured rect, text) and `PaintTraverse`
   (Panel.cpp:1128 — border-first, background, `Paint`, children, border-last), `PaintBackground` types, the three
   border `Paint`s. Then the Render adapter over `HudRenderer`, replacing the old `SchemeFont` path (D180).
2. **Controls**: Label, ImagePanel, ScalableImagePanel, then TF's `CExLabel`, `CTFImagePanel`, `CExButton`.
3. **`hudlayout.res`** and `CHudElement` (the viewport's elements by name, `ShouldDraw`).
4. **Elements with demo data**: health, ammo, killfeed, timer, crosshair, target ID, and the rest.
5. **`AnimationController`**: `scripts/hudanimations_manifest.txt`, events fired by the elements.
6. Auto-resize on a parent resize (`_autoResizeDirection`); `SolveTraverse`'s exact order in `vguimatsurface.dll`.

## Traps

- Positions measure against the SCREEN unless `proportionalToParent` is 1 — not the parent.
- `proportional_int`/`proportional_float` scale even on a panel that is not proportional; the float goes through an int.
- The owner's modern install has a custom HUD in `tf/custom`: probes default to stock (`WithoutCustom`), and the era
  clients are the stock reference (`docs/memory/modern-tf2-is-not-a-stock-reference.md`).
