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
| Fonts, glyphs, font effects | `VguiFontManager.cs`, `VguiWin32Font.cs`, `VguiGlyphCache.cs`, `VguiFontEffects.cs` | vguimatsurface `CFontManager`, `CWin32Font` |
| Text, labels | `VguiTextImage.cs`, `VguiLabel.cs` | `TextImage.cpp`, `Label.cpp` |
| Images | `VguiImagePanel.cs` (with `VguiBitmap`), `VguiScalableImagePanel.cs` | `ImagePanel.cpp`, vgui2 `Bitmap` |
| TF controls | `TfControls.cs` | `CExLabel`, `CTFImagePanel` |
| Localisation | `VguiLocalize.cs` (not yet wired — nothing localised draws) | vgui2 `CLocalizedStringTable::AddFile` |
| The FPS meter | `Presentation/FpsPanel.cs`, `VguiTools.cs` | `vgui_fpspanel.cpp` |

`vguimatsurface.dll` is imported: project `tf2vguimatsurface`, MCP on port 8093
(`D:\ghidra-proj\ghidra-mcp-vguimatsurface.bat`, pmux session `ghidra-vguimatsurface`).

## Next, in order

1. ~~Text~~, ~~Label~~, ~~ImagePanel~~, ~~ScalableImagePanel~~, ~~`CExLabel`~~, ~~`CTFImagePanel`~~, ~~localisation~~
   — done. `CExButton` waits for `Button`, which no HUD element needs yet.
2. **The HUD root**: `ClientScheme.res`, the viewport, `scripts/hudlayout.res` and `CHudElement` (elements by name,
   `ShouldDraw`), the localisation files (`valve_`, `tf_`, `gameui_`, `chat_` `%language%`) wired into its context,
   and the first output-level assertion.
3. **Elements with demo data**: health, ammo, killfeed, timer, crosshair, target ID, and the rest.
4. **`AnimationController`**: `scripts/hudanimations_manifest.txt`, events fired by the elements.
5. Auto-resize on a parent resize (`_autoResizeDirection`); `SolveTraverse`'s exact order in `vguimatsurface.dll`.

## In progress — model panels (`feat/hud-model-panel`, 2026-09-27)

Everything else on the owner's HUD list reached main at `8bb70037`. What remains draws a 3D model inside a vgui panel:
the class portrait (`CTFHudPlayerClass`'s `classmodelpanel`, a `CTFPlayerModelPanel`, on by default through
`cl_hud_playerclass_use_playermodel`), and the match-start doors and round sign (`CModelPanel`, matchmaking only).

Valve's shape: `CPotteryWheelPanel::Paint` (matsys_controls/potterywheelpanel.cpp:842) does `Begin3DPaint` on the
panel's rectangle, clears depth (colour too only for a render texture), sets the panel's own camera and lights, draws,
`End3DPaint` — in the middle of the vgui paint order. Chain: `CPotteryWheelPanel` → `CMDLPanel` → `CBaseModelPanel`
(game_controls/basemodel_panel.cpp) → `CTFPlayerModelPanel` (tf/vgui/tf_playermodelpanel.cpp, 2992 lines).

Plan, bottom layer first:
1. ~~**Render**~~ — done (`5a8f3004`): `IVguiSurface.Paint3D` → `VguiModelDraw` in paint order, `VguiRenderer.Steps`,
   `Device3D.DrawPanelModels`. `Begin3DPaint` (vguimatsurface 0x180008db0) is the whole panel, unclipped, no scissor.
2. **`CPotteryWheelPanel`/`CMDLPanel`**: camera (origin, angles, `fov`), lighting state, the model's pose at a
   sequence and cycle. What is known so far:
   - Camera (potterywheelpanel.cpp:225-265): `m_flZNear` 3, `m_flZFar` 16384·√3, `m_flFOV` 30 until the `.res` says.
     `ComputeViewMatrix`/`ComputeProjectionMatrix` are tier2 (`camerautils.h`), closed, linked into `client.dll` —
     import started into `D:\ghidra-proj\tf2client` (`import-client.bat`, pmux `ghidra-import-client`) to settle
     whether the projection is `MatrixBuildPerspectiveX( fov, w / h, near, far )` (fov horizontal, as `LookAt` treats
     it). `FreeCamera.ToMatrix` already builds that shape.
   - Lights (`CreateDefaultLights`, :316): ambient cube 0.4 on all six faces, one white directional light down
     (0, 0, −1) — maps onto `ModelInstance.Light` (`AmbientCube`) and `ModelInstance.Sun` (`SunLight`), so no renderer
     change. `SetupRenderState` (:723-731) transforms every light into `pDesc[0]` (`pDesc->m_Position`), a Valve bug
     that only matters with more than one light. A `.res` `lights` block overrides (`ParseLightsFromKV`, :271).
   - Cycle (`CMDLPanel::OnTick`, mdlpanel.cpp:638): `m_flTime = GetAutoPlayTime() - m_flCycleStartTime`, real time.
     Draw (`OnPaint3D`, :415): root `SetUpBones`, then each merge model `SetupBonesWithBoneMerge` onto it; the default
     env cubemap is bound for reflections.
   - Our pieces (mapped 2026-09-27): posing is `SkeletonPose` + `AnimatingEntity.SetupBones`
     (Animation/Animating/AnimatingEntity.cs:147), built per entity in `EntityModelSet.EntityFor`
     (Scene/EntityModels.cs:1462) — keyed by entity index, so a panel needs its own keyed entry, not a fake index;
     bone merge is `AnimatingEntity.Follows` (:88); sequence by activity is `SkinnedModel.SequenceWithActivity`
     (Scene/PropModels.cs:2444); skin is `EntityModelSet.SkinSwap` (:4500); a model loads with
     `EntityModelSet.Precache` (:4929) and reaches the GPU only through `Device3D.UploadModels` (Render/Device3D.cs:262)
     — check when MainForm calls it, since a panel's model may be first asked for mid-demo.
   - Step 1's code has no production caller yet, so the branch stays unmerged until a panel paints (D180).
3. **`CBaseModelPanel`**: `ApplySettings`/`ParseModelResInfo` (the `.res` `model` block), animations.
4. **`CTFPlayerModelPanel`**: class model, team skin, carried weapon and wearables, `HoldItemInSlot`, eye glow;
   `customclassdata` per class.
5. **`CTFHudPlayerClass`**: `OnThink` (tf_hud_playerstatus.cpp:184) and its 2D fallback.

## Traps

- Positions measure against the SCREEN unless `proportionalToParent` is 1 — not the parent.
- `proportional_int`/`proportional_float` scale even on a panel that is not proportional; the float goes through an int.
- The owner's modern install has a custom HUD in `tf/custom`: probes default to stock (`WithoutCustom`), and the era
  clients are the stock reference (`docs/memory/modern-tf2-is-not-a-stock-reference.md`).
