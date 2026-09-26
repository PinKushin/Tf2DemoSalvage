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
| Scheme / think / solve passes | `VguiLayout.SolveTraverse` | `CMatSystemSurface_SolveTraverse` (vguimatsurface) |
| Painting, backgrounds, border paints | `VguiPanel.PaintTraverse`, `VguiBorders.cs` | Panel.cpp:1128, `Border_Paint2` and kin |
| The surface's draw rules | `VguiDrawList.cs` (portable) | `PushMakeCurrent`, `DrawSetColor`, `ClipRect` |
| Drawing it | `Render/VguiRenderer.cs`, `Device3D.DrawFrame( vgui: )` | UnlitGeneric `$vertexalpha` |

`vguimatsurface.dll` is imported: project `tf2vguimatsurface`, MCP on port 8093
(`D:\ghidra-proj\ghidra-mcp-vguimatsurface.bat`, pmux session `ghidra-vguimatsurface`).

## Next, in order

1. **Text**: the surface's font calls (`DrawSetTextFont`, `DrawUnicodeChar`, `GetCharABCwide`, `GetFontTall`) from
   vguimatsurface, TF2's own `.ttf`s loaded from `CustomFontFiles` (GDI finds only installed families today), then
   `Label`/`TextImage`. Then retire `HudRenderer`, `HudText` and `SchemeFont` by porting the FPS meter as the VGUI
   panel it is (`CFPSPanel`) — D180.
2. **Controls**: ImagePanel, ScalableImagePanel, then TF's `CExLabel`, `CTFImagePanel`, `CExButton`.
   The viewer's HUD root (`MainForm` → `Device3D.SetVguiResolver` + `DrawFrame( vgui: )`) lands with the first element,
   and with it the first output-level assertion.
3. **`hudlayout.res`** and `CHudElement` (the viewport's elements by name, `ShouldDraw`).
4. **Elements with demo data**: health, ammo, killfeed, timer, crosshair, target ID, and the rest.
5. **`AnimationController`**: `scripts/hudanimations_manifest.txt`, events fired by the elements.
6. Auto-resize on a parent resize (`_autoResizeDirection`); `SolveTraverse`'s exact order in `vguimatsurface.dll`.

## Traps

- Positions measure against the SCREEN unless `proportionalToParent` is 1 — not the parent.
- `proportional_int`/`proportional_float` scale even on a panel that is not proportional; the float goes through an int.
- The owner's modern install has a custom HUD in `tf/custom`: probes default to stock (`WithoutCustom`), and the era
  clients are the stock reference (`docs/memory/modern-tf2-is-not-a-stock-reference.md`).
