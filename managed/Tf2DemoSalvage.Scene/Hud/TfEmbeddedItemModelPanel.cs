using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// `CEmbeddedItemModelPanel : CBaseModelPanel` (game/client/econ/item_model_panel.cpp:162): an item's inventory image, or
/// its model when the image is not used (B751).
/// </summary>
/// <remarks>
/// <para>
/// **The render target is drawn in place.** With `use_item_rendertarget` (the freeze panel's) or an attribute that asks
/// for one, `Paint` renders the model into one of three shared targets and copies it to a texture asynchronously
/// (:915-1027); from then on the copy is drawn and the 3D paint never runs again. Here the model is drawn straight into the
/// panel while the copy is made, and once it is made, with no particle — the copy holds the first frame's, which has
/// emitted nothing. *Interpolated:* the copy's latency, one frame here.
/// </para>
/// <para>
/// *Not built:* the pedestal (`use_pedestal`, no stock HUD `.res` sets it); a war paint's override material (B754); the
/// strangifier's tool-target overlay (:873-910); `model_rotate_yaw_speed` (:1029-1033). The item's world attached models are
/// its merge models, drawn before `RenderingRootModel` rather than in it.
/// </para>
/// </remarks>
public sealed class TfEmbeddedItemModelPanel : VguiBaseModelPanel
{
    private const int TeamUnassigned = 0;
    private const int TeamRed = 2;
    private const int FirstGameTeam = 2;

    /// <summary>`IMAGETYPE_SMALL`.</summary>
    public const int ImageTypeSmall = 0;

    /// <summary>`IMAGETYPE_LARGE`.</summary>
    public const int ImageTypeLarge = 1;

    private TfItemView? _item;
    private int _cameraAttachment = -1;
    private bool _imageNotLoaded;
    private bool _weaponAllowInspect;
    private bool _isFestivized;
    private bool _isPaintKitItem;
    private bool _useRenderTargetAsIcon;
    private bool _cachedWeaponIcon;
    private string? _pendingIcon;
    private int _pendingFrame;

    /// <summary>`CEmbeddedItemModelPanel( parent, name )` (:162).</summary>
    /// <param name="parent">The parent.</param>
    /// <param name="name">The name.</param>
    /// <param name="mdlCache">The models; null to ask the viewport.</param>
    public TfEmbeddedItemModelPanel(VguiPanel? parent, string? name, IMdlCache? mdlCache = null)
        : base(parent, name, mdlCache ?? new DeferredMdlCache())
    {
        if (MdlCache is DeferredMdlCache deferred)
        {
            deferred.Owner = this;
        }

        // `CPanelAnimationVar`s (item_model_panel.h:181-186).
        DeclareAnimationVar("force_use_model", VguiPanelVarType.Bool, "0");
        DeclareAnimationVar("use_item_rendertarget", VguiPanelVarType.Bool, "0");
        DeclareAnimationVar("inventory_image_type", VguiPanelVarType.Whole, "0");
        DeclareAnimationVar("force_square_image", VguiPanelVarType.Bool, "0");
        DeclareAnimationVar("model_rotate_yaw_speed", VguiPanelVarType.Real, "0");
        DeclareAnimationVar("use_pedestal", VguiPanelVarType.Bool, "0");
    }

    /// <summary>
    /// `vgui::MDLCache()` for a panel a `.res` file makes, which no constructor can hand one: the viewport's, asked on each
    /// use. Its owner is set after the base constructor, which needs a cache first.
    /// </summary>
    private sealed class DeferredMdlCache : IMdlCache
    {
        public VguiPanel? Owner { get; set; }

        private IMdlCache? Cache => Owner is { } owner ? HudViewport.Of(owner)?.MdlCache : null;

        public PropModels.ModelFrames? FindMdl(string path) => Cache?.FindMdl(path);

        public int FindBodygroup(string modelPath, string group) => Cache?.FindBodygroup(modelPath, group) ?? -1;

        public int SetBodygroup(string modelPath, int group, int value, int body) => Cache?.SetBodygroup(modelPath, group, value, body) ?? body;
    }

    /// <inheritdoc/>
    public override string ClassName => "CEmbeddedItemModelPanel";

    /// <summary>`m_bForceUseModel`.</summary>
    public bool ForceUseModel => GetBool("force_use_model");

    /// <summary>`m_bUseItemRenderTarget`.</summary>
    public bool UseItemRenderTarget => GetBool("use_item_rendertarget");

    /// <summary>`m_iInventoryImageType`.</summary>
    public int InventoryImageType
    {
        get => GetInt("inventory_image_type");
        set => SetAnimationValue("inventory_image_type", value);
    }

    /// <summary>`m_bModelIsHidden`.</summary>
    public bool ModelHidden { get; set; }

    /// <summary>`m_bGreyedOut`.</summary>
    public bool GreyedOut { get; set; }

    /// <summary>`m_pItem`.</summary>
    public TfItemView? Item => _item;

    /// <summary>`UseRenderTargetAsIcon()` (item_model_panel.h:141).</summary>
    public bool UseRenderTargetAsIcon => _useRenderTargetAsIcon || UseItemRenderTarget;

    /// <summary>Whether the render target's copy has been made: `m_pCachedWeaponIcon->GetTexture()`.</summary>
    public bool HasCachedIcon => _cachedWeaponIcon;

    private ItemSchema? Schema => HudViewport.Of(this)?.Items;

    /// <summary>`SetItem` (:256).</summary>
    /// <param name="item">The item, or null.</param>
    public void SetItem(TfItemView? item)
    {
        CleanUpCachedWeaponIcon();
        SetMDL(null);
        ClearMergeMDLs();
        StatTrack.Disabled = true;
        StatTrack.Path = null;
        _cameraAttachment = -1;
        _item = item;

        if (item is null || Schema is not { } schema)
        {
            return;
        }

        string? inventoryImage = InventoryImage(schema, item);

        // Set, never cleared here: only `LoadInventoryImage` clears it (:282-287).
        if (inventoryImage is { Length: > 0 } && !MaterialLoaded(inventoryImage))
        {
            _imageNotLoaded = true;
        }

        _weaponAllowInspect = item.Attribute(schema, "weapon_allow_inspect") is { } inspect && inspect.Value != 0f;
        _isFestivized = item.Attribute(schema, "is_festivized") is not null;
        _isPaintKitItem = item.Attribute(schema, "paintkit_proto_def_index") is not null;
        _useRenderTargetAsIcon = ShouldUseRenderTargetAsIcon(schema, item);

        if (ModelHidden || !(inventoryImage is null || ForceUseModel || _weaponAllowInspect || UseRenderTargetAsIcon))
        {
            return;
        }

        // "the first class that can use it" (:330-341), and team 0 (:343).
        int playerClass = FirstUsingClass(schema, item.DefinitionIndex);

        if (schema.PlayerDisplayModel(item.DefinitionIndex, playerClass, 0, item.Style(schema)) is not { Length: > 0 } model)
        {
            return;
        }

        SetMDL(model);

        if (MdlCache.FindMdl(model) is { } frames)
        {
            // No pedestal: the camera at `icon_camera` (:376-380), then the style's bodygroup (:383-391).
            _cameraAttachment = FindAttachment(frames, "icon_camera");
            UpdateCameraForIcon();

            if (schema.StyleBodygroup(item.DefinitionIndex, item.Style(schema)) is { } bodygroup
                && MdlCache.FindBodygroup(model, bodygroup.Name) is var group and not -1)
            {
                SetBody(MdlCache.SetBodygroup(model, group, bodygroup.Submodel, Body));
            }
        }

        // The ITEM's team picks the attached models' block (:414): the killer's weapon is the killer's team.
        LoadAttachedModels(schema, item, item.TeamNumber);
        LoadStatTrack(schema, item);

        // "If we aren't in a game we default to previewing the red team skin" (:490-509).
        int team = LocalTeam();
        int skinTeam = team == TeamUnassigned ? TeamRed : team;
        int skin = team;

        if (skin != TeamUnassigned)
        {
            // "Use the first skin for the first team, and the second skin for the other".
            skin = team == FirstGameTeam + 1 ? 1 : 0;
        }
        int overrideSkin = schema.Skin(item.DefinitionIndex, skinTeam, item.Style(schema));

        SetSkin(overrideSkin != -1 ? overrideSkin : skin);
    }

    /// <summary>`GetInventoryImage()` (econ_item_view.cpp:873): the style's image, else the definition's.</summary>
    private static string? InventoryImage(ItemSchema schema, TfItemView item) =>
        item.Style(schema) is { } style && schema.StyleInventoryImage(item.DefinitionIndex, style) is { Length: > 0 } styled
            ? styled
            : schema.InventoryImage(item.DefinitionIndex);

    /// <summary>`ShouldUseRenderTargetAsIcon` (:1053): festive, or "use_model_cache_icon".</summary>
    private bool ShouldUseRenderTargetAsIcon(ItemSchema schema, TfItemView item) =>
        _isFestivized || (item.Attribute(schema, "use_model_cache_icon") is { } cache && cache.Value != 0f);

    /// <summary>
    /// The first set bit of `GetClassUsability()` (:331-340), 0 with none. The bits are the classes of `used_by_classes`,
    /// scout (1) through engineer (9); bit 0 is never set, and `LoadoutSlot` answers class 0 with the default slot.
    /// </summary>
    private static int FirstUsingClass(ItemSchema schema, int definition)
    {
        for (int playerClass = 1; playerClass <= 9; playerClass++)
        {
            if (schema.LoadoutSlot(definition, playerClass) != ItemSchema.LoadoutSlotInvalid)
            {
                return playerClass;
            }
        }

        return 0;
    }

    /// <summary>`GetLocalPlayerTeam()`.</summary>
    private int LocalTeam() => HudViewport.Of(this)?.State.Team ?? TeamUnassigned;

    /// <summary>The world model attachments, festive ones too when festivized (:411-435), as merge models.</summary>
    /// <remarks>`LoadAttachedModel` (:522) takes only those with `kAttachedModelDisplayFlag_WorldModel`.</remarks>
    private void LoadAttachedModels(ItemSchema schema, TfItemView item, int team)
    {
        foreach (AttachedModel attached in schema.AttachedModelsFor(item.DefinitionIndex, team, _isFestivized))
        {
            if ((attached.DisplayFlags & AttachedModel.WorldModel) != 0)
            {
                SetMergeMDL(attached.Model);
            }
        }
    }

    /// <summary>The StatTrak module (:437-485): only on a paint-kitted, strange item.</summary>
    private void LoadStatTrack(ItemSchema schema, TfItemView item)
    {
        if (item.StatTrakModule(schema) is not { } module)
        {
            return;
        }

        // `GetQuality()` is normal without a backpack; `GetItemQuality()` is the networked one.
        if (item.Quality != TfItemDescription.QualityStrange && !TfItemDescription.HasKillEaterScore(schema, item))
        {
            return;
        }

        StatTrackScale = item.Attribute(schema, "weapon_stattrak_module_scale") is { } scale ? scale.Value : 1f;
        StatTrack.Path = module;
        StatTrack.Disabled = false;
    }

    /// <summary>`m_StatTrackModel`.</summary>
    private VguiMdl StatTrack { get; } = new() { Disabled = true };

    /// <summary>`m_flStatTrackScale`.</summary>
    private float StatTrackScale { get; set; } = 1f;

    /// <summary>`IsLoadingWeaponSkin` (:548): a render-target icon not yet copied.</summary>
    /// <returns>Whether the icon is still to be made.</returns>
    public bool IsLoadingWeaponSkin()
    {
        if (ForceUseModel || _item is null)
        {
            return false;
        }

        // A paint kit's composite (`GetWeaponSkinBaseCompositor`) is B754's; its cached icon is the same wait.
        if ((_weaponAllowInspect && _isPaintKitItem) || UseRenderTargetAsIcon)
        {
            return !_cachedWeaponIcon;
        }

        return false;
    }

    /// <summary>`IsImageNotLoaded` (:573).</summary>
    /// <returns>Whether the inventory image is still to be loaded.</returns>
    public bool IsImageNotLoaded() => !ForceUseModel && _imageNotLoaded && _item is not null;

    /// <summary>
    /// `LoadInventoryImage` (:618): `GetMaterialForImage` finds the image of the panel's type, which loads THAT material —
    /// `_large` for a large panel, so the base name `SetItem` asks about stays unloaded. *Not built:* `IMAGETYPE_DETAILED`,
    /// which no HUD `.res` uses.
    /// </summary>
    public void LoadInventoryImage()
    {
        if (_item is { } item && Schema is { } schema && InventoryImage(schema, item) is { } image)
        {
            HudViewport.Of(this)?.LoadedMaterials.Add(InventoryImageType == ImageTypeLarge ? image + "_large" : image);
        }

        _imageNotLoaded = false;
    }

    /// <summary>
    /// `CItemModelPanel::LoadData`'s `m_pModelPanel->Paint()` under skin-composition painting (item_model_panel.cpp:3476-3481):
    /// the model drawn into the render target and its copy requested, as <see cref="Paint"/>'s model branch does.
    /// </summary>
    public void PaintIntoRenderTarget() => RenderModelForIcon();

    /// <summary>`CleanUpCachedWeaponIcon` (:200): the copy, and any copy still in flight, dropped.</summary>
    private void CleanUpCachedWeaponIcon()
    {
        _cachedWeaponIcon = false;
        _pendingIcon = null;
    }

    /// <summary>
    /// The model branch's copy (:982-1027): a frame drawn into the render target becomes the icon. A texture of that name
    /// already loaded completes at once (`IsTextureLoaded`, :999-1006); otherwise the asynchronous copy lands by the next
    /// paint. *Interpolated:* that latency, one paint here.
    /// </summary>
    private void RenderModelForIcon()
    {
        CompletePendingIcon();

        // `bShouldCacheToTexture` (:983-992): a paint kit's once its skin is drawn — *interpolated* as at once, its composite
        // being B754's — anything else when the icon is wanted.
        bool shouldCache = _isPaintKitItem ? IsLoadingWeaponSkin() : UseRenderTargetAsIcon;

        // No model test: a model-less item (a powerup) still has its empty frame copied (:982-1027).
        if (_cachedWeaponIcon || _pendingIcon is not null || ForceUseModel || !shouldCache || _item is not { } item
            || Schema is not { } schema)
        {
            return;
        }

        // "proc/icon/item%d_id%lld%lld_w%d_h%d" (:996) with `GetPaintKitDefIndex`, 0 without one.
        long paintKit = item.Attribute(schema, "paintkit_proto_def_index") is { } kit ? kit.RawBits : 0;
        string name = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"proc/icon/item{item.DefinitionIndex}_id{unchecked((long)item.ItemId)}{paintKit}_w{Wide}_h{Tall}");

        if (HudViewport.Of(this)?.LoadedMaterials.Contains(name) == true)
        {
            _cachedWeaponIcon = true;
            return;
        }

        _pendingIcon = name;
        _pendingFrame = HudViewport.Of(this)?.FrameCount ?? 0;
    }

    /// <summary>`OnAsyncCreateComplete`: a copy requested in an earlier frame has landed.</summary>
    private void CompletePendingIcon()
    {
        if (_pendingIcon is { } name && (HudViewport.Of(this)?.FrameCount ?? 0) != _pendingFrame)
        {
            HudViewport.Of(this)?.LoadedMaterials.Add(name);
            _pendingIcon = null;
            _cachedWeaponIcon = true;
        }
    }

    /// <summary>`IsMaterialLoaded`.</summary>
    private bool MaterialLoaded(string material) => HudViewport.Of(this)?.LoadedMaterials.Contains(material) == true;

    /// <summary>`PerformLayout` (:634): the base's, the cached icon dropped, then the camera from `icon_camera` when found.</summary>
    protected override void PerformLayout()
    {
        base.PerformLayout();
        CleanUpCachedWeaponIcon();

        if (_item is not null && _cameraAttachment != -1)
        {
            UpdateCameraForIcon();
        }
    }

    /// <summary>`UpdateCameraForIcon` (:228): the camera at the model's `icon_camera` attachment, offset zero.</summary>
    private void UpdateCameraForIcon()
    {
        if (_cameraAttachment == -1 || RootAttachmentToWorld(_cameraAttachment) is not { } matrix)
        {
            return;
        }

        CameraOffset = (0f, 0f, 0f);
        CameraPivotOrigin = (matrix[3], matrix[7], matrix[11]);
        CameraPivotAngles = MatrixAngles(matrix);
    }

    /// <summary>`MatrixAngles( matrix, angles )` (mathlib.cpp): pitch, yaw, roll in degrees from a row-major 3x4.</summary>
    private static (float Pitch, float Yaw, float Roll) MatrixAngles(float[] matrix)
    {
        float forward0 = matrix[0];
        float forward1 = matrix[4];
        float forward2 = matrix[8];
        float left0 = matrix[1];
        float left1 = matrix[5];
        float left2 = matrix[9];
        float up2 = matrix[10];
        float xyDist = MathF.Sqrt((forward0 * forward0) + (forward1 * forward1));
        const float ToDegrees = 180f / MathF.PI;

        return xyDist > 0.001f
            ? (MathF.Atan2(-forward2, xyDist) * ToDegrees, MathF.Atan2(forward1, forward0) * ToDegrees, MathF.Atan2(left2, up2) * ToDegrees)
            : (MathF.Atan2(-forward2, xyDist) * ToDegrees, MathF.Atan2(-left0, left1) * ToDegrees, 0f);
    }

    /// <summary>
    /// `Paint` (:661): nothing without a valid item; the inventory image unless a render-target icon is wanted, with its
    /// overlays tinted by the paint; else the model.
    /// </summary>
    /// <inheritdoc/>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (_item is not { } item || Schema is not { } schema)
        {
            return;
        }

        if (ModelHidden)
        {
            base.Paint(surface, context);
            return;
        }

        // "Don't even try to render backpack icon if we're not loaded".
        if (_imageNotLoaded && !_weaponAllowInspect && !UseRenderTargetAsIcon)
        {
            return;
        }

        CompletePendingIcon();

        if (!IsLoadingWeaponSkin() && !ForceUseModel)
        {
            if (_cachedWeaponIcon)
            {
                // The copy: the model's first frame, which emitted no particle (see the class remarks).
                UseParticle = false;
                base.Paint(surface, context);
                return;
            }

            if (InventoryImage(schema, item) is { } image)
            {
                DrawInventoryImage(surface, schema, item, image);
                return;
            }
        }

        // No image to draw: the model (:938-1027), into the render target when an icon is wanted, and its copy asked for.
        base.Paint(surface, context);
        RenderModelForIcon();
    }

    /// <summary>The inventory image branch (:747-910).</summary>
    private void DrawInventoryImage(IVguiSurface surface, ItemSchema schema, TfItemView item, string image)
    {
        string material = InventoryImageType == ImageTypeLarge ? image + "_large" : image;
        (int mappingWide, int mappingTall) = surface.DrawGetTextureSize(material);

        if (mappingWide == 0 || mappingTall == 0)
        {
            return;
        }

        int x = 0;
        int y = 0;
        int wide = Wide;
        int tall = Tall;
        float texW = 1f;
        float texH = 1f;
        float texX = 0f;
        float texY = 0f;
        (int posX, int posY, int sizeW, int sizeH) = schema.InventoryImageData(item.DefinitionIndex);

        if (GetBool("force_square_image"))
        {
            sizeW = Math.Max(sizeW, sizeH);
            sizeH = sizeW;
        }

        if (sizeW == 0 && sizeH == 0)
        {
            sizeW = mappingWide;
            sizeH = mappingTall;
        }
        else
        {
            if (InventoryImageType != ImageTypeSmall)
            {
                // "Normal is 128*128, large is 512x512".
                sizeW *= 4;
                sizeH *= 4;
            }

            texW = (float)sizeW / mappingWide;
            texH = (float)sizeH / mappingTall;
            texX = (1f - texW) * 0.5f;
            texY = (1f - texH) * 0.5f;
        }

        if (posX != 0 || posY != 0)
        {
            x = (int)(x + HudViewport.XRes(this, posX));
            y = (int)(y + HudViewport.YRes(this, posY));
        }

        float ratio = (float)sizeW / sizeH;

        // `if ( flRatio != ((float)iWidth / (float)iHeight) )`: an exact compare, as the engine's.
        if (BitConverter.SingleToInt32Bits(ratio) != BitConverter.SingleToInt32Bits((float)wide / tall))
        {
            // "Fit to the height".
            int centerX = (int)(x + (wide * 0.5));
            wide = (int)(tall * ratio);
            x = (int)(centerX - (wide * 0.5));
        }

        surface.DrawSetTexture(material);
        surface.DrawSetColor(GreyedOut ? ((byte)96, (byte)96, (byte)96, (byte)255) : ((byte)255, (byte)255, (byte)255, (byte)255));
        surface.DrawTexturedSubRect(x, y, x + wide, y + tall, texX, texY, texX + texW, texY + texH);

        IReadOnlyList<string> overlays = schema.InventoryOverlayImages(item.DefinitionIndex);

        for (int index = 0; index < overlays.Count; index++)
        {
            // `GetModifiedRGBValue( i == 0 )`: the first overlay takes the ALT color (:860). *Not built:* the Halloween
            // spell's paint override (econ_item_view.cpp:1583-1594).
            int rgb = ItemPaint.Packed(item.Attributes, schema, alternate: index == 0) ?? 0;
            float scale = GreyedOut ? 96f / 255f : 1f;

            surface.DrawSetTexture(InventoryImageType == ImageTypeLarge ? overlays[index] + "_large" : overlays[index]);
            surface.DrawSetColor((
                (byte)(Math.Clamp((rgb >> 16) & 0xFF, 0, 255) * scale),
                (byte)(Math.Clamp((rgb >> 8) & 0xFF, 0, 255) * scale),
                (byte)(Math.Clamp(rgb & 0xFF, 0, 255) * scale),
                (byte)255));
            surface.DrawTexturedSubRect(x, y, x + wide, y + tall, texX, texY, texX + texW, texY + texH);
        }
    }

    /// <summary>`RenderingRootModel` (:1271): no pedestal, so the StatTrak module on the item.</summary>
    /// <inheritdoc/>
    protected override void RenderingRootModel(
        VguiRenderContext renderContext, PropModels.ModelFrames studioHdr, string mdlHandle, BoneAccessor worldMatrix)
    {
        if (!StatTrack.Disabled)
        {
            DrawBoneMergedOnto(renderContext, StatTrack, mdlHandle, StatTrackScale);
        }
    }

    /// <inheritdoc/>
    public override IEnumerable<string> ModelsToPrecache()
    {
        foreach (string path in base.ModelsToPrecache())
        {
            yield return path;
        }

        if (!StatTrack.Disabled && StatTrack.Path is { } module)
        {
            yield return module;
        }
    }
}
