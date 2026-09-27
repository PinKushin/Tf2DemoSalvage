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
3. ~~**`CBaseModelPanel`**~~ — done (below).
4. **`CTFPlayerModelPanel`** — dressing done (`TfPlayerModelPanel`, wired in `TfHudPlayerClass.UpdateModelPanel`).
   Eye glows, unusual effects, the spellbook hand effect and StatTrak are ported too. The taunt branch is
   unreachable from the HUD (proof on `TfPlayerModelPanel.SwitchHeldItemTo`). Still open, outside the panel:
   world players' `BRenderAsZombie` skin (c_tf_player.cpp:725), and `FireEvent`/flex/`GetOverrideMaterial` below.
5. **`CTFHudPlayerClass`**: `OnThink` (tf_hud_playerstatus.cpp:184) and its 2D fallback.

### Steps 2 and 3, done 2026-09-27

Three classes in `managed/Tf2DemoSalvage.Scene/Hud/`, one per Valve class, each member on the class Valve declares
it on (the owner rejected an earlier fold into one `VguiModelPanel`: Valve shape always, D163/D196, and step 4's
`CTFPlayerModelPanel` overrides members from every layer):

- `VguiPotteryWheelPanel : VguiEditablePanel` (potterywheelpanel.h:38), abstract — camera, pivot/offset, lights,
  `ParseLightsFromKV`, `Paint`, and the abstract `OnPaint3D` (:88).
- `VguiMdlPanel : VguiPotteryWheelPanel` (mdlpanel.h:40) — root and merge models, cycle clock, skin, virtual
  `SetModelAnglesAndPosition` (:89), `OnPaint3D` (mdlpanel.cpp:415) with the virtual hooks `PrePaint3D`,
  `PostPaint3D`, `RenderingRootModel`, `RenderingMergedModel` (:136-139). The MDL cache (`vgui::MDLCache()->FindMDL`,
  mdlpanel.cpp:185) is a constructor argument, so no panel exists without one.
- `VguiBaseModelPanel : VguiMdlPanel` (basemodel_panel.h:150) — the `.res` `model` block, `force_pos`, animations,
  `PlaySequence`, `LookAtBounds`, `OnTick` expiry, and the `SetModelAnglesAndPosition` override that caches
  `m_angPlayer`/`m_vecPlayerPos` (cpp:322-329).

Tests: `tests/Tf2DemoSalvage.Scene.Tests/VguiModelPanelConformanceTests.cs` (37, synthetic fixtures via
`SyntheticSkinnedModel`/`AnimatedStudioBytes`, no corpus demos).

**Open for step 4 — `CTFPlayerModelPanel`'s overrides with no seam yet.** It overrides `FireEvent`
(mdlpanel.h:106, tf_playermodelpanel.h:44), `SetupFlexWeights` (mdlpanel.h:101, tf:64) and `GetOverrideMaterial`
(mdlpanel.h:140, tf:83). None exists here because the mechanism behind each is unported: animation events
(`DoAnimationEvents`), flex weights, and `ForcedMaterialOverride`. Each needs its mechanism ported in
`VguiMdlPanel.OnPaint3D` at the line mdlpanel.cpp calls it (:459 flex, :465/:495 material), not an empty virtual.

**Open divergences carried through the split unchanged** (a pure refactor did not fix them):
- `force_pos` is applied per paint through a zero camera, and the stored pivot/offset are left alone.
  `PerformLayout` (basemodel_panel.cpp:381-387) overwrites them instead. So `LookAtBounds`' centring
  `CameraOffset` (:763) is discarded by any later paint, because `LookAtBounds` also sets `ForcePosition`, which
  Valve's does not.
- `move_x` = 1 (`SetupModelAnimDefaults`, basemodel_panel.cpp:175) is applied in `VguiMdlPanel.OnPaint3D` for every
  panel, where Valve sets it only from `CBaseModelPanel` into `m_PoseParameters`.
- `ModelName`/`Sequence`/`Skin` are plain properties; Valve's `SetMDL` (mdlpanel.cpp:153-178, overridden at
  basemodel_panel.cpp:284-317) resets the cycle start, pose parameters and sequence, then runs `SetupModelDefaults`.

**Reviewed by a coordinator against basemodel_panel.cpp after the first pass (5353203b) and found not done** — nine
items, all addressed in follow-up commits the same day: the camera/model transform was backwards (fixed, below),
frame stepping was unwired (fixed, below), `fov` parsed as a float rather than truncating `GetInt` (fixed), the
`SetModelAnim` fallback order and `move_x`/`attached_model` parsing were missing (fixed), `ParseLightsFromKV`'s gaps
were undersold as "narrowed" rather than listed (fixed, divergence section below), and a skinning loop was
duplicated between this file and `EntityModelSet` (fixed via `BoneSkinning.Fill`). Bone merge (`AnimatingEntity.Follows`)
was already correct on review — a test was added to confirm it rather than leaving that unverified.

- **Camera and lights are exact**: `NearZ` 3, `FarZ` 16384·√3, `FieldOfView` 30 (potterywheelpanel.cpp:250-252);
  ambient cube 0.4 all six faces and one white sun down `(0,0,-1)` (`CreateDefaultLights`, :316-333).

#### `ParseLightsFromKV` (potterywheelpanel.cpp:392-460) — point and spot now ported (fixed 2026-09-27)

Point and spot lights carry through to the renderer as `LocalLight` entries in `ModelInstance.Locals` — the SAME
structure a world prop's nearby BSP lights use, and `Device3D.DrawPanelModels` already threaded `instance.Locals`
through to `_world.DrawModel` before this fix (`Device3D.cs:1248,1354`), so no renderer change was needed, only that
`Paint` populates it. Origin, colour, attenuation (0/1/2), `maxDistance` (range), and for `spot` also direction,
cone angles (as cosines) and exponent are all read. `nLightCount`'s `MAX_LIGHT_COUNT` cap
(`LocalLights.MaximumLocalLights` = 4) is ONE counter shared by every kind, directional included, matching
`potterywheelpanel.cpp:394-459`.

**Checked whether a 2nd+ directional entry could go into `Locals` instead (coordinator review, 2026-09-27) — it
cannot, without building a new shader path.** `LocalLight` (`Content/Bsp/LocalLights.cs`) carries no directional
case (`IsLocal`, :348-349, explicitly excludes the sky light kind — "The sun is excluded deliberately", :330-334),
and the model shader that actually consumes `Locals` is unconditionally positional:
`WorldRenderer.cs:598-625` (`LampAttenuation`) computes `toLamp = localLightPosition[lamp].xyz - world` and divides
by `dot(atten, (1, distance, distance²))` for every lamp — there is no branch that skips the distance term, and
`localLightDirection[lamp].w` only `lerp`s between POINT and SPOT falloff (:624), never selects a third,
distance-independent case. A directional local light needs constant illumination regardless of distance, which is
structurally the one thing this attenuation formula cannot produce without a new branch — building that branch
would be the "new lighting path" this task was told not to build, so this stays exactly where it was: only the
FIRST `directional` entry survives, because `ModelInstance.Sun` — what the renderer actually draws a model's
directional light with — is a single `SunLight`, the same as every world prop. No stock HUD `.res` file writes more
than one.

**`SetupRenderState`'s per-light transform bug (potterywheelpanel.cpp:723-731) is reproduced by construction,
not separately coded.** For light `i > 0`, Valve transforms into `pDesc[0]` instead of `pDesc[i]`, so every light
after the first is sent with its RAW, untransformed position — but only `CPotteryWheelManip` (mouse-dragging a
light in the editor) ever moves `m_LightToWorld` away from identity, and this project models no mouse input at
all. With `m_LightToWorld` always identity, "transformed" and "raw" are the same numbers, so parsing straight into
world-space values (no separate transform stage) reproduces exactly what the bug also produces whenever nothing
has moved the light. Full reasoning in `VguiPotteryWheelPanel.ParseLightsFromKV`'s own remarks.

Tests: `ParseLightsFromKV_NoDirectionalEntry_ClearsTheSunRatherThanKeepingTheOldOne`,
`ParseLightsFromKV_APointEntry_BecomesALocalLightWithOriginColourAttenuationAndRange`,
`ParseLightsFromKV_AllZeroAttenuation_NormalisesToConstantOne`,
`ParseLightsFromKV_ASpotEntry_BecomesALocalLightWithConesAndExponent`,
`ParseLightsFromKV_MoreThanFourNonDirectionalEntries_StopsAtMaximumLocalLights`,
`ParseLightsFromKV_ADirectionalEntryAmongFourLocals_CountsTowardTheSharedLimit`,
`Paint_WithLocalLights_CarriesThemOnEveryModelInstance`.
- **Posing reuses `AnimatingEntity`/`SkeletonPose` directly**, keyed by model PATH in a small dictionary the panel
  owns — its own version of `EntityModelSet.EntityFor`, since a panel has no entity index. Bone merge is
  `AnimatingEntity.Follows`; skinning (bone-to-world folded with bind pose) is `BoneSkinning.Fill`, extracted from
  `EntityModelSet.Skinning` (`EntityModels.cs:3478`) so both share one implementation — the model panel calls it
  unbuffered (a handful of models, not hundreds a frame), `EntityModelSet` keeps its per-entity reused buffer.
- **Precache reuses the existing upload path with no change needed.** `EntityModelSet.Precache` sets `Grown`, and
  `MomentScene.Pack` already checks `_models.Grown` every frame regardless of what added to it (`MomentScene.cs:633`,
  written for B363) — so `VguiMdlPanel.ModelsToPrecache()` handed to the same `EntityModelSet.Precache` a caller
  already calls is enough; a model panel's model reaches the GPU the next frame with no `MainForm`/`Device3D` change.
- **Frame stepping is wired** (fixed after coordinator review, 2026-09-27): `FrameAt` feeds `CycleTime` through
  `SkinnedModel.BlendedCyclesPerSecond` and `StudioSequences.ClampCycle`/`FrameAt` — the same closed-form path
  `EntityModelSet.Simulate` already uses for every drawn prop (`EntityModels.cs:763-788`), just fed by a continuous
  clock instead of a per-tick advance. Tests: `FrameAt_HalfASecondIntoAOneCyclePerSecondSequence_IsFrame15Of31`,
  `FrameAt_TwoAndAHalfCyclesIntoALoopingSequence_WrapsToTheFraction`.
- **The model/camera transform was backwards, and is now fixed** (coordinator review, 2026-09-27):
  `ParseModelResInfo` caches `ModelAngles`/`ModelOrigin` (`m_angPlayer`/`m_vecPlayerPos`, basemodel_panel.cpp:101-102)
  but only `PerformLayout`'s `force_pos` branch (:381-387) ever actually MOVES anything with them — the previous
  code assigned them straight to the camera and drew the model at identity, the opposite of both of Valve's actual
  branches. Now: `ForcePosition` (`force_pos` in the `model` block) picks between `SetModelAnglesAndPosition` moving
  the MODEL while the camera resets to the world origin, and the default — model stays at its `AnimatingEntity` bind
  pose while the camera sits at `CameraPivotOrigin`/`CameraPivotAngles` (identity by default) plus `CameraOffset`
  (`(100,0,0)`, potterywheelpanel.cpp:248) rotated into the pivot's own axes — `ComputeCameraTransform`, porting
  `UpdateCameraTransform` (:765-773). Tests: `Paint_DefaultCameraState_MatchesThePotteryWheelPanelConstructor`,
  `Paint_ForcePosition_PutsTheCameraAtTheWorldOriginAndTheModelAtModelOrigin`,
  `Paint_NoForcePosition_LeavesTheModelAtTheOriginRegardlessOfModelOrigin`.

#### `start_framed` (`CBaseModelPanel::LookAtBounds`, basemodel_panel.cpp:649-769) — ported in full (fixed 2026-09-27)

`VguiBaseModelPanel.LookAtBounds` is the whole algorithm: the box's eight corners reprojected through the model's
own rotation, the panel's aspect ratio and field of view (`CalcFovY`, `mathlib_base.cpp:3893`, ported literally —
including feeding it a HALF angle where its own parameter comment says a full one, which is what Valve's caller
actually does); the MODEL repositioned to the fitting distance (`ModelOrigin`, with `ForcePosition` set so `Paint`
does not ignore it); the camera only nudged to centre the result (`CameraOffset`, X left at 0 — the model carries
the distance, not the camera).

**The bounding box itself is Valve's own header fields, not a bone-position approximation.**
`StudioRenderBounds.Of` (`Content/Assets/StudioRenderBounds.cs`) reads `view_bbmin`/`view_bbmax` when authored, else
`hull_min`/`hull_max` (`StudioLayout.HeaderHullMinOffset`/`HeaderViewBoundsMinOffset` etc.), which is exactly what
`GetBoundingBox`/`GetRenderBounds` reads too — not vertex data, but the SAME header boxes Valve's own function
prefers. `PropModels.SkinnedModel.RenderBounds()` exposes it.

**Not ported**: `allow_rotation`/`allow_pitch`'s offset-zeroing branch (:763-766) — it exists so a mouse-draggable
panel does not fight the player's own rotation, and this project has no mouse input at all (the same exclusion
`VguiPanel`'s own remarks state, see below), so the branch's condition is permanently false and porting it would be
dead code.

Tests: `LookAtBounds_ALongThinBox_FitsTheTighterAxisNotABoundingSphere` (a 200×10×10 box: a bounding-sphere
approximation would back the camera to ~141.7 units at a 90° fov; the real box fit gives exactly 105 — the two are
verifiably different numbers, not the same answer read two ways), `ApplyStartFramed_AModelWithAuthoredHullBounds_FitsTheCameraToThem`
(the same box, written into synthetic `.mdl` header bytes at the real offsets and read back through
`start_framed 1` in a `.res` file end to end).

#### Not modelled: mouse-driven manipulation

`m_bAllowFullManipulation`/`m_bAllowRotation`/`m_bAllowPitch` and `PerformLayout`'s manipulation branch
(basemodel_panel.cpp:354-378) exist to let a player drag a class-selection model around with the mouse. This is the
same category `VguiPanel`'s own remarks already exclude — "Input, navigation... not modelled: a demo's HUD takes no
input" — and stays excluded for the same reason: an offline demo renderer has no mouse to drive it.

## Remaining HUD work (2026-09-27, `feat/hud-model-panel`, not yet on main)

On this branch: model panels in CMDL shape (edd41c62), CTFPlayerModelPanel dressing (1ed93891), client cvars through
the ConVar lookup (261e4e69), server time limit (133bed1e), no-heal line (5a0087af). Merge to main needs all three
gates.

1. CTFPlayerModelPanel partials — a subagent is on these: per-team world-model bodygroup override (:871),
   GetBestVisualTeamData for attached models (:1034), strange-level styles (econ_item_view.cpp:747-776), eye glow and
   unusual particles (:1584-1889), StatTrak (:550-616, :1539), taunt branch (:664-731).
2. Target ID generic targets — done: dropped weapons and revive markers decoded, traced and shown. The flag's branch
   cannot run: `FSOLID_NOT_SOLID` (entity_capture_flag.cpp:607). A dropped weapon is traced against its model's `.phy`.
3. `CTFMinigameLogic` refusal in TfHudPlayerStatus.ShouldDraw (tf_hud_playerstatus.cpp:1087).
4. `localplayer_pickup_weapon`: done — `PlayerPickupWeapon` is kept by name and fired to TfHudPlayerClass and to the
   item effect meters. The meters are ported whole: `TfItemEffectMeterManager`, the cloak meter, every weapon
   specialisation, the rune and item-attribute meters, on `ContinuousProgressBar`. Two readings to know: a demo's
   client never runs the charged SMG's `SecondaryAttack`, so its `m_flMinicritStartTime` stays 0; and the manager's
   `Update` runs in the render stage. The host frame, read in the x64 `engine.dll` (`tf2enginex64`, functions renamed
   there, D174): `_Host_RunFrame` 0x1801a4570 calls `_Host_RunFrame_Input` 0x1801a5b90, whose `ClientDLL_ProcessInput`
   0x18006ed60 calls `IBaseClientDLL` slot 10, `HudProcessInput`, and so `CHud::Think` (hud.cpp:1003); then
   `_Host_RunFrame_Client` 0x1801a5860, whose `CL_ReadPackets` 0x18008d1f0 reads the frame's packets (entity data,
   game events, user messages); later `_Host_RunFrame_Render` 0x1801a5d30 → `SCR_UpdateScreen` 0x1800e8b40 →
   `ClientDLL_FrameStageNotify` 0x18006e640 with `FRAME_RENDER_START` (5), whose `SimulateEntities` runs
   `ClientThink` and the meters' `Update` (cdll_client_int.cpp:2005, :2207, :2279). So `VguiHud.Frame` thinks the
   HUD on the LAST frame's state, then dispatches the packet-driven events, then updates the meters. The attribute
   meter's re-lookup ticks every 100 ms of real time (`AddTickSignal`, :1661).
5. Match-start doors and round sign: done, `VguiModelPanel` (CModelPanel), with the doors' team lists and party names.
   The rank-up message needs the GC's rating cache (tf_rating_data.cpp:22-41), never in a demo. The door slam is
   `TfParticlePanel` (CTFParticlePanel).
6. Model-shader directional local lights: done; a panel's second directional light is one. A map's only directional
   light, light_environment, already reaches world models as the sun.
7. `_minmode` keys: done — every `.res` read from disk (BuildGroup.cpp:953-960); a change reloads the scheme.

clangd on the SDK: `find_symbol` returns nothing until a document is opened (`open_document` first).

## Traps

- Positions measure against the SCREEN unless `proportionalToParent` is 1 — not the parent.
- `proportional_int`/`proportional_float` scale even on a panel that is not proportional; the float goes through an int.
- The owner's modern install has a custom HUD in `tf/custom`: probes default to stock (`WithoutCustom`), and the era
  clients are the stock reference (`docs/memory/modern-tf2-is-not-a-stock-reference.md`).
