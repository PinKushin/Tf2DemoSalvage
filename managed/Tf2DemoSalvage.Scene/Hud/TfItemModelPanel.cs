using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// `CItemModelPanel : vgui::EditablePanel` (game/client/econ/item_model_panel.cpp:1360): an item's image or model, its name
/// in its quality's color, and its description — what the freeze panel shows of the killer's weapon (B751).
/// </summary>
/// <remarks>
/// <para>
/// **The description loads a frame behind the icon, as the engine's does.** `UpdatePanels` marks the description dirty and
/// asks for a tick; each tick `LoadData` does the one loading task the previous frame asked for first — icons before
/// descriptions (:3446-3456) — so the name and attributes arrive the frame after the icon is ready, and the content
/// container stays hidden until then.
/// </para>
/// <para>
/// **Not applicable to a demo:** the mouse-over panel and its collections (`is_mouseover`, a tooltip's); the contained item
/// panel, which shows a gift's interior item from SOC data (:2340); recipe matching and the quantity label, which need a
/// tool the freeze panel never shows; greyed-out items; button behavior.
/// </para>
/// <para>
/// *Not built:* the collection labels, which `ItemModelPanel.res` does not declare. *Interpolated:*
/// `tf_time_loading_item_panels`, a wall-clock budget (see <see cref="LoadData"/>).
/// </para>
/// </remarks>
public sealed class TfItemModelPanel : VguiEditablePanel, IVguiTickSignal
{
    /// <summary>`LOADING_ICONS`.</summary>
    private const int LoadingIcons = 0;

    /// <summary>`LOADING_DESCRIPTIONS`.</summary>
    private const int LoadingDescriptions = 1;

    /// <summary>`NUM_LOADING_TYPES`.</summary>
    private const int LoadingTypes = 3;

    /// <summary>`TF_VISION_FILTER_HALLOWEEN` (tf_shareddefs.h).</summary>
    private const int VisionFilterHalloween = 2;

    private TfEmbeddedItemModelPanel? _modelPanel;
    private TfExLabel? _nameLabel;
    private VguiLabel? _attribLabel;
    private VguiLabel? _equippedLabel;
    private VguiLabel? _quantityLabel;
    private VguiImagePanel? _visionRestrictionImage;
    private VguiImagePanel? _strangeImage;
    private VguiImagePanel? _unusualImage;
    private VguiImagePanel? _loanerImage;
    private VguiLabel? _seriesLabel;
    private VguiLabel? _matchesLabel;
    private VguiEditablePanel? _mainContent;
    private VguiImagePanel? _loadingSpinner;
    private VguiPanel? _tf2Icon;
    private TfPaintIconPanel? _paintIcon;
    private VguiFontAmalgam? _fontNameSmallest;
    private VguiFontAmalgam? _fontNameSmall;
    private VguiFontAmalgam? _fontNameLarge;
    private VguiFontAmalgam? _fontNameLarger;
    private VguiFontAmalgam? _fontAttribSmallest;
    private VguiFontAmalgam? _fontAttribSmall;
    private VguiFontAmalgam? _fontAttribLarge;
    private VguiFontAmalgam? _fontAttribLarger;
    private VguiContext? _context;
    private TfItemView? _item;
    private bool _hideModel;
    private bool _descriptionDirty;
    private bool _needsToLoad;
    private HudViewport? _tickViewport;
    private IReadOnlyList<TfDescriptionLine>? _description;
    private bool _hasUnknownPlayer;
    private (byte, byte, byte, byte) _orgItemTextColor = (255, 255, 255, 255);
    private (byte, byte, byte, byte) _noItemTextColor = (255, 255, 255, 255);

    /// <summary>
    /// `sm_nCurrentDecriptionUpdateFrame`, `sai_NumLoadingRequests` and `se_CurrentLoadingTask` (:1353-1356): STATIC in the
    /// engine, so every item panel shares one, held here by the viewport.
    /// </summary>
    internal sealed class Loading
    {
        public int Frame { get; set; }

        public int[] Requests { get; } = new int[LoadingTypes];

        public int CurrentTask { get; set; } = LoadingIcons;
    }

    /// <summary>`CItemModelPanel( parent, name )` (:1360).</summary>
    /// <param name="parent">The parent.</param>
    /// <param name="name">The name.</param>
    public TfItemModelPanel(VguiPanel? parent, string? name)
        : base(parent, name)
    {
        foreach (string variable in (string[])
        [
            "special_attributes_only", "model_center_x", "model_center_y", "noitem_use_fullpanel", "text_center", "text_center_x",
            "use_item_sounds", "name_only", "attrib_only", "model_only", "model_hide", "paint_icon_hide", "resize_to_text",
            "standard_text_color", "is_mouseover", "hide_collection_panel", "hide_modifier_icons",
        ])
        {
            DeclareAnimationVar(variable, VguiPanelVarType.Bool, "0");
        }

        foreach (string variable in (string[])
        [
            "model_xpos", "model_ypos", "model_wide", "model_tall", "tf2_icon_offset_x", "tf2_icon_offset_y", "text_xpos", "text_ypos",
            "text_wide", "text_yoffset", "padding_height", "max_text_height", "inset_eq_x", "inset_eq_y", "wide", "tall",
            "collection_list_xpos", "text_xpos_collection",
        ])
        {
            DeclareAnimationVar(variable, VguiPanelVarType.ProportionalInt, "0");
        }

        DeclareAnimationVar("name_label_alignment", VguiPanelVarType.Whole, "4");
        DeclareAnimationVar("text_forcesize", VguiPanelVarType.Whole, "0");
        RegisterColorAsOverridable("noitem_textcolor", color => _noItemTextColor = color);
    }

    /// <inheritdoc/>
    public override string ClassName => "CItemModelPanel";

    /// <summary>`m_pModelPanel`.</summary>
    public TfEmbeddedItemModelPanel? ModelPanel => _modelPanel;

    /// <summary>`m_pItemNameLabel`.</summary>
    public TfExLabel? NameLabel => _nameLabel;

    /// <summary>`m_pItemAttribLabel`.</summary>
    public VguiLabel? AttribLabel => _attribLabel;

    /// <summary>`m_pMainContentContainer`.</summary>
    public VguiEditablePanel? MainContent => _mainContent;

    /// <summary>`m_pIsStrangeImage`.</summary>
    public VguiImagePanel? StrangeImage => _strangeImage;

    /// <summary>`m_pIsUnusualImage`.</summary>
    public VguiImagePanel? UnusualImage => _unusualImage;

    /// <summary>`GetItem()`: the item, or null.</summary>
    public TfItemView? Item => _item;

    /// <summary>`m_bInitializedAsContainedItem`.</summary>
    internal bool InitializedAsContainedItem { get; set; }

    private bool HideModelDefault => GetBool("model_hide");

    private bool ModelOnly => GetBool("model_only");

    private bool NameOnly => GetBool("name_only");

    private bool AttribOnly => GetBool("attrib_only");

    private bool NoItemFullPanel => GetBool("noitem_use_fullpanel");

    private bool ResizeToText => GetBool("resize_to_text");

    private bool IsMouseOverPanel => GetBool("is_mouseover");

    private int ForceTextSize => GetInt("text_forcesize");

    private int TextWide => GetInt("text_wide");

    /// <summary>`ATTRIB_LABEL_INDENT`: `XRES( 5 )` (item_model_panel.h:23).</summary>
    private double AttribLabelIndent => HudViewport.XRes(this, 5);

    /// <summary>`GetAttribWide` (item_model_panel.h:299): the indent's double, truncated once.</summary>
    private int GetAttribWide(int maxWide) => TextWide != 0 ? TextWide : (int)(maxWide - (AttribLabelIndent * 2));

    /// <summary>`HasItem()`: `m_ItemData.IsValid()`.</summary>
    public bool HasItem => _item is not null;

    private ItemSchema? Schema => HudViewport.Of(this)?.Items;

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:1427): the pointers dropped, the `.res` loaded again, the fonts, the borders.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        base.ApplySchemeSettings(context);
        _context = context;

        // A load asked for before the panel had a viewport; one already registered keeps its place.
        if (_tickViewport is null)
        {
            RegisterTick();
        }
        _modelPanel = null;
        _nameLabel = null;
        _attribLabel = null;
        _equippedLabel = null;
        _quantityLabel = null;
        _visionRestrictionImage = null;
        _strangeImage = null;
        _unusualImage = null;
        _loanerImage = null;
        _seriesLabel = null;
        _matchesLabel = null;
        _tf2Icon = null;
        _paintIcon = null;
        _mainContent = null;
        _loadingSpinner = null;
        LoadResFileForCurrentItem(context);

        // Always proportional, whatever the panel (:1466-1473).
        _fontNameSmallest = context.GetFont("ItemFontNameSmallest", true);
        _fontNameSmall = context.GetFont("ItemFontNameSmall", true);
        _fontNameLarge = context.GetFont("ItemFontNameLarge", true);
        _fontNameLarger = context.GetFont("ItemFontNameLarger", true);
        _fontAttribSmallest = context.GetFont("ItemFontAttribSmallest", true);
        _fontAttribSmall = context.GetFont("ItemFontAttribSmallv2", true);
        _fontAttribLarge = context.GetFont("ItemFontAttribLarge", true);
        _fontAttribLarger = context.GetFont("ItemFontAttribLarger", true);

        // `m_bContainedItem` is set only to show a gift's interior from SOC data (:2344), so never here.
        Border = context.Borders.Get("TFFatLineBorder");

        if (_modelPanel is { } model)
        {
            model.Border = context.Borders.Get("TFFatLineBorder");
        }
    }

    /// <inheritdoc/>
    /// <remarks>`ApplySettings` (:1490): the base's, the `itemmodelpanel` block passed on, then `UpdatePanels`.</remarks>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);

        base.ApplySettings(block, context);

        if (_modelPanel is { } model && block.Find("itemmodelpanel") is { } modelBlock)
        {
            model.ApplySettings(modelBlock, context);
        }

        UpdatePanels();
    }

    /// <summary>`LoadResFileForCurrentItem` (:1509), off the mouse-over panel: `ItemModelPanel.res` and the children it names.</summary>
    private void LoadResFileForCurrentItem(VguiContext context)
    {
        LoadControlSettings("Resource/UI/econ/ItemModelPanel.res", context);
        _hideModel = HideModelDefault;

        _modelPanel = FindChildByName("itemmodelpanel", true) as TfEmbeddedItemModelPanel;
        SetModelIsHidden(_hideModel);

        _nameLabel = FindChildByName("namelabel", true) as TfExLabel;
        _attribLabel = FindChildByName("attriblabel", true) as VguiLabel;
        _equippedLabel = FindChildByName("equippedlabel", true) as VguiLabel;
        _quantityLabel = FindChildByName("quantitylabel", true) as VguiLabel;
        _visionRestrictionImage = FindChildByName("vision_restriction_icon", true) as VguiImagePanel;
        _strangeImage = FindChildByName("is_strange_icon", true) as VguiImagePanel;
        _unusualImage = FindChildByName("is_unusual_icon", true) as VguiImagePanel;
        _loanerImage = FindChildByName("is_loaner_icon", true) as VguiImagePanel;
        _seriesLabel = FindChildByName("serieslabel", true) as VguiLabel;
        _matchesLabel = FindChildByName("matcheslabel", true) as VguiLabel;
        _mainContent = FindChildByName("MainContentsContainer") as VguiEditablePanel;
        _loadingSpinner = FindChildByName("LoadingSpinner") as VguiImagePanel;
        _tf2Icon = FindChildByName("tf2_icon", true);
        _paintIcon = FindChildByName("paint_icon", true) as TfPaintIconPanel;

        PaintBackgroundEnabled = false;

        if (_modelPanel is { } model)
        {
            model.BgColor = (0, 0, 0, 255);
        }

        if (_nameLabel is { } name)
        {
            _orgItemTextColor = name.FgColor;
            name.InvalidateLayout(layoutNow: true, reloadScheme: true, context);
        }

        _attribLabel?.InvalidateLayout(layoutNow: true, reloadScheme: true, context);

        // "Avoid infinitely creating contained item panels whenever layout gets invalidated" (:1694-1707).
        if (FindChildByName("contained_item_panel", true) is TfItemModelPanel contained)
        {
            contained.InitializedAsContainedItem = true;

            if (InitializedAsContainedItem)
            {
                contained.SetParent(null);
            }
        }

        UpdatePanels();
    }

    /// <summary>`SetModelIsHidden` (:3403).</summary>
    private void SetModelIsHidden(bool hide)
    {
        _hideModel = hide;

        if (_modelPanel is { } model)
        {
            model.ModelHidden = hide;
        }
    }

    /// <summary>
    /// `SetItem` (:2211): a different item replaces the old and the panels update. Only an item with an ID matches, by
    /// that ID (or, for MvM's bots, whose items are all ID 1, by definition); a base item never does, as `bMatch &amp;=`
    /// starts from false (:2255-2264); an item with a kill count never does.
    /// </summary>
    /// <param name="item">The item, or null.</param>
    public void SetItem(TfItemView? item)
    {
        bool match = false;

        if (item is not null)
        {
            bool canMatch = Schema is not { } schema || item.Attribute(schema, "kill eater") is null;

            if (canMatch && _item is { ItemId: not SceneItem.InvalidItemId } current)
            {
                bool useIndexCompare = HudViewport.Of(this)?.State.Rules.MannVsMachine == true && current.ItemId == 1 && item.ItemId == 1;

                match = useIndexCompare ? current.DefinitionIndex == item.DefinitionIndex : current.ItemId == item.ItemId;
            }

            if (!match)
            {
                _item = item;
                _description = null;
            }
        }
        else
        {
            match = _item is null;
            _item = null;
            _description = null;
        }

        if (!match)
        {
            UpdatePanels();
        }
    }

    /// <summary>`UpdatePanels` (:2995).</summary>
    public void UpdatePanels()
    {
        if (_modelPanel is not { } model)
        {
            return;
        }

        model.ModelHidden = _hideModel;

        // `RemoveTickSignal` (:3005) and `se_CurrentLoadingTask = LOADING_ICONS` (:3008).
        UnregisterTick();

        if (HudViewport.Of(this) is { } viewport)
        {
            viewport.ItemPanelLoading.CurrentTask = LoadingIcons;
        }

        model.SetItem(_item);

        if (!_hideModel && (model.IsImageNotLoaded() || model.IsLoadingWeaponSkin()))
        {
            if (_mainContent is { } content)
            {
                content.Visible = false;
            }

            if (_loadingSpinner is { } spinner)
            {
                spinner.Visible = true;
            }

            SetNeedsToLoad();
        }
        else if (_loadingSpinner is { } spinner)
        {
            spinner.Visible = false;
        }

        if (_item is not { } item)
        {
            // No item and no "no item" text in a demo: the labels follow the model-only rule, else hide.
            if (_nameLabel is { } name)
            {
                name.Visible = false;
            }

            if (_attribLabel is { } attrib)
            {
                attrib.Visible = false;
            }

            HideAllModifierIcons();
            return;
        }

        if (GetBool("hide_modifier_icons"))
        {
            HideAllModifierIcons();
            return;
        }

        if (_paintIcon is { } paintIcon && Schema is { } paintSchema)
        {
            UpdatePaintIcon(paintIcon, paintSchema, item);
        }

        if (_tf2Icon is { } tf2Icon)
        {
            // `GetSOCData()->IsForeign()`: no backpack, so never.
            tf2Icon.Visible = false;
        }

        if (NoItemFullPanel)
        {
            _nameLabel!.Visible = false;
            _attribLabel!.Visible = false;
        }
        else if (ModelOnly)
        {
            if (_nameLabel is { } name)
            {
                name.Visible = false;
            }

            if (_attribLabel is { } attrib)
            {
                attrib.Visible = false;
            }
        }
        else
        {
            // "deferred description loading".
            _descriptionDirty = true;
            SetNeedsToLoad();
            _mainContent?.SetDialogVariable("itemname", string.Empty);
        }

        if (_equippedLabel is { } equipped)
        {
            // `m_bShowEquipped && IsEquipped()`: neither in a demo.
            equipped.Visible = false;
        }

        foreach (VguiLabel? hidden in (VguiLabel?[])[_matchesLabel, _seriesLabel, _quantityLabel])
        {
            if (hidden is not null)
            {
                hidden.Visible = false;
            }
        }

        if (Schema is { } schema)
        {
            UpdateVisionRestriction(schema, item);
            UpdateStrangeAndUnusual(schema, item);
        }

        InvalidateLayout();
    }

    /// <summary>The paint splat (:3129-3160): a custom decal, or the paint's colors, never on a tool.</summary>
    private void UpdatePaintIcon(TfPaintIconPanel icon, ItemSchema schema, TfItemView item)
    {
        icon.Visible = false;

        if (_hideModel || GetBool("paint_icon_hide"))
        {
            return;
        }

        icon.PaintColors.Clear();

        if (CustomUserTextureId(schema, item) != 0)
        {
            icon.Visible = true;
        }

        bool isTool = schema.IsTool(item.DefinitionIndex);
        int primary = ItemPaint.Packed(item.Attributes, schema, alternate: false) ?? 0;
        int alternate = ItemPaint.Packed(item.Attributes, schema, alternate: true) ?? 0;

        if (isTool || (primary == 0 && alternate == 0))
        {
            return;
        }

        icon.Visible = true;
        icon.PaintColors.Add(Unpack(primary));

        if (primary != alternate)
        {
            icon.PaintColors.Add(Unpack(alternate));
        }
    }

    /// <summary>
    /// `GetCustomUserTextureID()` (econ_item_view.cpp:1636-1664): 0 under `tf_hide_custom_decals` unless the item's owner is
    /// the local player; else both halves of the decal's id, or 0 without both.
    /// </summary>
    private ulong CustomUserTextureId(ItemSchema schema, TfItemView item)
    {
        HudState state = HudViewport.Of(this)?.State ?? default;

        if (state.ConVars.GetBool("tf_hide_custom_decals")
            && !(state.PlayerByAccountId(item.AccountId) is { } owner && state.HasLocalPlayer && owner.EntityIndex == state.LocalIndex))
        {
            return 0;
        }

        return item.Attribute(schema, "custom texture lo") is { } low && item.Attribute(schema, "custom texture hi") is { } high
            ? unchecked(((ulong)(uint)high.RawBits << 32) | (uint)low.RawBits)
            : 0;
    }

    private static (byte, byte, byte, byte) Unpack(int rgb) =>
        ((byte)Math.Clamp((rgb & 0xFF0000) >> 16, 0, 255), (byte)Math.Clamp((rgb & 0xFF00) >> 8, 0, 255), (byte)Math.Clamp(rgb & 0xFF, 0, 255), 255);

    /// <summary>The vision restriction icon (:3231-3297).</summary>
    private void UpdateVisionRestriction(ItemSchema schema, TfItemView item)
    {
        if (_visionRestrictionImage is not { } image)
        {
            return;
        }

        int flags = 0;

        if (!ModelOnly)
        {
            flags = schema.VisionFilterFlagsFor(item.DefinitionIndex);

            if (schema.HolidayRestriction(item.DefinitionIndex) is { } holiday
                && TfHolidays.HolidayForString(holiday) is TfHolidays.Halloween or TfHolidays.FullMoon or TfHolidays.HalloweenOrFullMoon
                    or TfHolidays.HalloweenOrFullMoonOrValentines)
            {
                flags |= VisionFilterHalloween;
            }
        }

        switch (flags)
        {
            case 1:
                image.SetImage("viewmode_pyrovision");
                image.Visible = true;
                break;

            case VisionFilterHalloween:
                HudViewport? viewport = HudViewport.Of(this);
                bool spooky = viewport is not null && TfHolidays.IsHalloweenOrFullMoonActive(
                    name => viewport.State.ConVars.GetString(name), viewport.State.Rules.MapHolidayType, TfHolidays.WatcherClock);

                image.SetImage(spooky ? "viewmode_spooky" : "viewmode_spooky_off");
                image.Visible = true;
                break;

            default:
                // 0, 4, and any other value after the assert (:3270-3273).
                image.Visible = false;
                break;
        }
    }

    /// <summary>The strange, unusual and loaner icons (:3299-3366).</summary>
    private void UpdateStrangeAndUnusual(ItemSchema schema, TfItemView item)
    {
        if (_strangeImage is { } strange)
        {
            strange.Visible = false;

            // `GetQuality() == AE_STRANGE`, never without a backpack; then any kill eater score.
            if (!IsMouseOverPanel && TfItemDescription.HasKillEaterScore(schema, item))
            {
                strange.SetImage(item.StatTrakModule(schema) is not null ? "viewmode_statclock" : "viewmode_strange");
                strange.Visible = true;
            }
        }

        if (_unusualImage is { } unusual)
        {
            unusual.Visible = false;

            if (!IsMouseOverPanel
                && (item.Attribute(schema, "attach particle effect") is not null || item.Attribute(schema, "taunt attach particle index") is not null))
            {
                unusual.SetImage("viewmode_unusual");
                unusual.Visible = true;
            }
        }

        if (_loanerImage is { } loaner)
        {
            loaner.Visible = false;

            if (!IsMouseOverPanel && item.IsQuestLoaner(schema))
            {
                loaner.SetImage("viewmode_loaner");
                loaner.Visible = true;
            }
        }
    }

    /// <summary>`HideAllModifierIcons` (:2944).</summary>
    private void HideAllModifierIcons()
    {
        foreach (VguiPanel? icon in (VguiPanel?[])
        [
            _paintIcon, _tf2Icon, _equippedLabel, _quantityLabel, _visionRestrictionImage, _strangeImage, _unusualImage, _loanerImage,
            _seriesLabel, _matchesLabel,
        ])
        {
            if (icon is not null)
            {
                icon.Visible = false;
            }
        }
    }

    /// <summary>
    /// `SetNeedsToLoad` (:3429): `AddTickSignalToHead`, so it loads whether or not it is visible. vgui's list is global; a
    /// panel not yet under a viewport registers when the scheme pass first reaches it there.
    /// </summary>
    private void SetNeedsToLoad()
    {
        _needsToLoad = true;
        RegisterTick();
    }

    private void RegisterTick()
    {
        if (_needsToLoad && HudViewport.Of(this) is { } viewport)
        {
            _tickViewport = viewport;
            viewport.AddTickSignalToHead(this);
        }
    }

    /// <summary>`RemoveTickSignal` (:3005, :3534), from the list it was registered on.</summary>
    private void UnregisterTick()
    {
        _needsToLoad = false;
        _tickViewport?.RemoveTickSignal(this);
        _tickViewport = null;
    }

    /// <summary>
    /// `OnTick` (:3412): `LoadData`, and `LoadDataCompleted` once nothing is left. A panel taken out of its viewport's tree
    /// is dropped from the list, as vgui drops a deleted panel's.
    /// </summary>
    public void OnTick()
    {
        if (!ReferenceEquals(HudViewport.Of(this), _tickViewport) || !LoadData())
        {
            UnregisterTick();
        }
    }

    /// <summary>`LoadData` (:3434): the icon first, then the description, one task a frame across every item panel.</summary>
    /// <returns>Whether anything is still to load.</returns>
    /// <remarks>
    /// *Interpolated:* `tf_time_loading_item_panels`, 0.5 ms of wall clock a frame, is not a measure here; each task is
    /// taken to fit, which the first of a frame always does (the budget starts at zero).
    /// </remarks>
    public bool LoadData()
    {
        if (HudViewport.Of(this) is not { } viewport)
        {
            return false;
        }

        Loading shared = viewport.ItemPanelLoading;

        // "Different frame?": the task is the lowest type asked for last frame.
        if (shared.Frame != viewport.FrameCount)
        {
            shared.Frame = viewport.FrameCount;
            int type = LoadingTypes;

            for (int index = 0; index < LoadingTypes; index++)
            {
                if (shared.Requests[index] > 0 && index < type)
                {
                    type = index;
                }

                shared.Requests[index] = 0;
            }

            shared.CurrentTask = type;
        }

        bool stillWorking = false;

        if (!_hideModel && _modelPanel is { } model)
        {
            bool imageLoaded = true;
            bool loadingWeaponSkin = model.IsLoadingWeaponSkin();
            bool loadingBackpackIcon = model.IsImageNotLoaded();

            if (loadingWeaponSkin || loadingBackpackIcon)
            {
                shared.Requests[LoadingIcons]++;

                if (shared.CurrentTask == LoadingIcons)
                {
                    if (loadingWeaponSkin)
                    {
                        model.PaintIntoRenderTarget();
                    }

                    if (loadingBackpackIcon)
                    {
                        model.LoadInventoryImage();
                    }
                }

                stillWorking = model.IsLoadingWeaponSkin() || model.IsImageNotLoaded();
                imageLoaded = !stillWorking;
            }

            if (imageLoaded)
            {
                if (_mainContent is { Visible: false } content)
                {
                    content.Visible = true;
                }

                if (_loadingSpinner is { Visible: true } spinner)
                {
                    spinner.Visible = false;
                }
            }
        }

        if (_descriptionDirty)
        {
            shared.Requests[LoadingDescriptions]++;

            if (shared.CurrentTask == LoadingDescriptions)
            {
                UpdateDescription();
            }

            stillWorking |= _descriptionDirty;
        }

        return stillWorking;
    }

    /// <summary>`UpdateDescription` (:2583): the lines into the dialog variables, the name's color, the attributes' colors.</summary>
    public void UpdateDescription()
    {
        if (!_descriptionDirty)
        {
            return;
        }

        _descriptionDirty = false;

        IReadOnlyList<TfDescriptionLine> lines = Description();
        StringBuilder attributes = new();
        int written = 0;

        if (!NameOnly)
        {
            foreach (TfDescriptionLine line in lines)
            {
                // "skip mouse over panel only line"; the case bonus content is the mouse-over panel's own skip.
                if ((!IsMouseOverPanel && (line.MetaType & TfItemDescription.FlagMouseOverPanel) != 0)
                    || (IsMouseOverPanel && (line.MetaType & TfItemDescription.FlagCaseBonusContent) != 0))
                {
                    continue;
                }

                if (GetBool("special_attributes_only"))
                {
                    if (line.Color is TfItemDescription.ColorUnusual or TfItemDescription.ColorStrange)
                    {
                        attributes.Append(written++ == 0 ? string.Empty : "\n").Append(line.Text);
                    }
                }
                else if ((line.MetaType & (TfItemDescription.FlagCollectionName | TfItemDescription.FlagCollection)) != 0)
                {
                    // The collection labels, which `ItemModelPanel.res` does not declare.
                }
                else if ((line.MetaType & TfItemDescription.FlagName) == 0)
                {
                    attributes.Append(written++ == 0 ? string.Empty : "\n").Append(line.Text);
                }
            }

            // "If we have an unknown name, we should try to rebuild for 'awhile'" (:2648-2649).
            _descriptionDirty |= _hasUnknownPlayer;
        }

        string itemName = string.Empty;

        foreach (TfDescriptionLine line in lines)
        {
            if ((line.MetaType & TfItemDescription.FlagName) == TfItemDescription.FlagName)
            {
                itemName = line.Text;
                break;
            }
        }

        if (_mainContent is { } content)
        {
            content.SetDialogVariable("attriblist", attributes.ToString());
            content.SetDialogVariable("collectionname", string.Empty);
            content.SetDialogVariable("collectionlist", string.Empty);
            content.SetDialogVariable("itemname", itemName);
        }

        if (_nameLabel is { } name && _context is { } context)
        {
            // "Rarity Econ Colorization": the rarity's color unless self-made or unusual, else the quality's for a valid item.
            int quality = _item?.Quality ?? TfItemDescription.QualityNormal;

            if (_item is { } item && Schema is { } schema && schema.RarityColor(item.DefinitionIndex) is { } rarityColor
                && quality != TfItemDescription.QualitySelfMade && quality != TfItemDescription.QualityUnusual)
            {
                name.SetColorStr(rarityColor, context);
            }
            else if (_item is not null && !GetBool("standard_text_color") && TfItemDescription.QualityColor(quality) is { } qualityColor)
            {
                name.SetColorStr(qualityColor, context);
            }
            else
            {
                name.SetColorStr(_orgItemTextColor);
            }

            name.Visible = !AttribOnly;
        }

        if (_attribLabel is { } attrib)
        {
            attrib.Visible = !NameOnly;
        }

        InvalidateLayout(layoutNow: true, reloadScheme: false, _context ?? throw new InvalidOperationException("No scheme has reached the item panel."));
        ApplyAttributeColors(lines);
    }

    /// <summary>The attribute label's color stream (:2711-2817), every line not a name or a collection line.</summary>
    /// <remarks>
    /// **A collection line with no collection label falls to the attribute stream**, as the engine's `else` does when the
    /// labels are missing (:2757), so its length shifts the colors after it (B754's lines; none are built yet).
    /// </remarks>
    private void ApplyAttributeColors(IReadOnlyList<TfDescriptionLine> lines)
    {
        if (_attribLabel is not { } attrib || NameOnly || _context is not { } context || Schema is not { } schema)
        {
            return;
        }

        VguiTextImage image = attrib.TextImage;
        image.ClearColorChangeStream();

        (byte, byte, byte, byte) previous = (0, 0, 0, 0);
        int streamIndex = 0;

        for (int index = 0; index < lines.Count; index++)
        {
            TfDescriptionLine line = lines[index];

            if ((!IsMouseOverPanel && (line.MetaType & TfItemDescription.FlagMouseOverPanel) != 0)
                || (IsMouseOverPanel && (line.MetaType & TfItemDescription.FlagCaseBonusContent) != 0)
                || (line.MetaType & TfItemDescription.FlagName) != 0)
            {
                continue;
            }

            (byte, byte, byte, byte) color = context.Scheme.GetColor(schema.AttribColorName(line.Color), (255, 255, 255, 255));

            if (GetBool("special_attributes_only") && line.Color is not (TfItemDescription.ColorUnusual or TfItemDescription.ColorStrange))
            {
                continue;
            }

            // `prevAttrColor` starts at `Color( 0, 0, 0 )` — alpha 0 — and the first line always changes it.
            if (index == 0 || previous != color)
            {
                image.AddColorChange(color, streamIndex);
                previous = color;
            }

            // "add one character to deal with newlines".
            streamIndex += line.Text.Length + 1;
        }
    }

    /// <summary>`m_ItemData.GetDescription()`: built once for the item (`EnsureDescriptionIsBuilt`, econ_item_view.cpp:650).</summary>
    private IReadOnlyList<TfDescriptionLine> Description()
    {
        if (_description is { } built)
        {
            return built;
        }

        if (_item is not { } item || Schema is not { } schema || HudViewport.Of(this) is not { } viewport)
        {
            return [];
        }

        HudState state = viewport.State;

        _description = TfItemDescription.Generate(
            item,
            new TfItemDescriptionContext
            {
                Schema = schema,
                Find = token => viewport.Context?.Localize?.Invoke(token),
                PersonaName = account => PersonaName(state, account),
                IsMannVsMachine = state.Rules.MannVsMachine,
            },
            out _hasUnknownPlayer);

        return _description;
    }

    /// <summary>
    /// `InventoryManager()->PersonaName_Get( account )`: *interpolated* as the name of the demo's player whose `friendsID`
    /// it is — the players Steam would know as being in the game — else null, the engine's "[unknown]".
    /// </summary>
    private static string? PersonaName(HudState state, uint account) =>
        state.PlayerByAccountId(account) is { } player ? state.Names?.GetValueOrDefault(player.EntityIndex) : null;

    /// <inheritdoc/>
    /// <remarks>`PerformLayout` (:1721).</remarks>
    protected override void PerformLayout()
    {
        int w = GetInt("wide") != 0 ? GetInt("wide") : Wide;
        int h = GetInt("tall") != 0 ? GetInt("tall") : Tall;
        int textW = GetAttribWide(w);
        int modelWide = GetInt("model_wide");
        int modelTall = GetInt("model_tall");
        int modelW = modelWide != 0 && modelWide < w ? modelWide : w;
        int modelT = modelTall != 0 && modelTall < h ? modelTall : h;
        int modelX = GetBool("model_center_x") ? (int)((w - modelW) * 0.5) : GetInt("model_xpos");
        int modelY = GetBool("model_center_y") ? (int)((h - modelT) * 0.5) : GetInt("model_ypos");

        ResizeLabels();

        if (_modelPanel is { } model)
        {
            (model.X, model.Y) = (modelX, modelY);
            model.SetSize(modelW, modelT);
        }

        if (_loadingSpinner is { } spinner)
        {
            int size = Math.Max(modelW, modelT);
            (spinner.X, spinner.Y) = ((int)(w / 2f) - (int)(size / 2f), (int)(h / 2f) - (int)(size / 2f));
            spinner.SetSize(size, size);
        }

        if (NoItemFullPanel)
        {
            if (_nameLabel is { } name && _attribLabel is not null)
            {
                (name.X, name.Y) = ((int)HudViewport.XRes(this, 4), 0);
                name.SetSize((int)(Wide - HudViewport.XRes(this, 8)), Tall);
            }
        }
        else if (_nameLabel is { } name && _attribLabel is { } attrib && !ModelOnly)
        {
            LayOutLabels(name, attrib, w, h, textW, modelT, modelY);
        }

        // "pin icons to top right corner".
        int iconX = (int)(GetInt("wide") - HudViewport.XRes(this, 1));
        int iconY = (int)HudViewport.YRes(this, 1);

        foreach (VguiPanel? icon in (VguiPanel?[])[_paintIcon, _tf2Icon, _visionRestrictionImage, _unusualImage, _strangeImage, _loanerImage])
        {
            if (icon is not { Visible: true })
            {
                continue;
            }

            int offsetX = icon == _tf2Icon ? GetInt("tf2_icon_offset_x") : 0;
            int offsetY = icon == _tf2Icon ? GetInt("tf2_icon_offset_y") : 0;

            (icon.X, icon.Y) = (iconX - icon.Wide + offsetX, iconY + offsetY);
            iconY += (int)(icon.Tall * 0.9);
        }

        if (ModelOnly)
        {
            if (_nameLabel is { } name)
            {
                name.Visible = false;
            }

            if (_attribLabel is { } attrib)
            {
                attrib.Visible = false;
            }
        }

        base.PerformLayout();
    }

    /// <summary>The labels' sizes and places (:1756-1916).</summary>
    private void LayOutLabels(TfExLabel name, VguiLabel attrib, int w, int h, int textW, int modelT, int modelY)
    {
        Layout(name);
        Layout(attrib);
        name.SizeToContents();
        attrib.SizeToContents();

        // `"" strings still size themselves as one font-height tall, but 0 wide`.
        int attribTall = attrib.Wide != 0 ? attrib.Tall : 0;
        int nameTall = AttribOnly ? 0 : name.Tall;

        // Every branch of :1791-1814 sets the same sizes: `attribTall` is `attrib.Wide ? attrib.Tall : 0` in each.
        name.SetSize(textW, nameTall);
        attrib.SetSize(textW, attribTall);

        Layout(name);
        Layout(attrib);
        attrib.SizeToContents();

        // "Reget sizes, wtf" — and "Now we resize it again. Sets our height properly. Ridiculous."
        attribTall = attrib.Wide != 0 ? attrib.Tall : 0;
        attrib.SetSize(textW, attribTall);
        name.SetSize(textW, nameTall);

        // `|| (!HasItem() && m_pszNoItemText[0])` (:1830): only a loadout's `SetNoItemText` sets that text, never here.
        if (NameOnly)
        {
            attribTall = 0;
        }

        int labelOffset = 0;
        int textY = GetInt("text_ypos");
        int padding = GetInt("padding_height");

        if (ResizeToText)
        {
            h = textY + nameTall + attribTall + padding;

            if (!_hideModel)
            {
                h = Math.Max(h + modelT + modelY, textY + padding);
                labelOffset = modelT + modelY;
            }
        }

        int textX = (int)(GetInt("text_xpos") != 0 || TextWide != 0 ? GetInt("text_xpos") : AttribLabelIndent);

        if (GetBool("text_center_x"))
        {
            name.SizeToContents();
            attrib.SizeToContents();
            (name.X, name.Y) = ((int)((w - name.Wide) * 0.5f), textY + labelOffset);
            (attrib.X, attrib.Y) = ((int)((w - attrib.Wide) * 0.5f), textY + nameTall + labelOffset);
        }
        else if (textY != 0)
        {
            (name.X, name.Y) = (textX, textY + labelOffset);
            (attrib.X, attrib.Y) = (textX, textY + nameTall + labelOffset);
        }
        else if (GetBool("text_center"))
        {
            int top = Math.Max(0, (int)((h - (nameTall + attribTall)) * 0.5));
            (name.X, name.Y) = (textX, top);
            (attrib.X, attrib.Y) = (textX, top + nameTall);
        }
        else
        {
            int offsetY = (int)(GetInt("text_yoffset") != 0 ? GetInt("text_yoffset") : HudViewport.YRes(this, 8));
            (name.X, name.Y) = (textX, h - attribTall - nameTall - offsetY + padding);
            (attrib.X, attrib.Y) = (textX, h - attribTall - offsetY + padding);
        }

        if (ResizeToText)
        {
            SetSize(w, h);
        }
    }

    /// <summary>`InvalidateLayout( true )` on a label: laid out now, the scheme reloaded only when asked.</summary>
    private void Layout(VguiPanel label, bool reloadScheme = false)
    {
        if (_context is { } context)
        {
            label.InvalidateLayout(layoutNow: true, reloadScheme, context);
        }
    }

    /// <summary>`ResizeLabels` (:2032): the fonts, large, then smaller while they do not fit, and wrap as a last resort.</summary>
    private void ResizeLabels()
    {
        if (_nameLabel is not { } name || _attribLabel is not { } attrib || ModelOnly)
        {
            return;
        }

        int textW = GetAttribWide(Wide);
        int h = GetInt("max_text_height") != 0 ? GetInt("max_text_height") : Tall;
        int force = ForceTextSize;

        if (force is > 0 and <= 4)
        {
            if (!NoItemFullPanel)
            {
                name.SetCenterWrap(false);
            }

            // `InvalidateLayout( true, true )` (:2072-2073): with the scheme reloaded.
            Layout(name, reloadScheme: true);
            Layout(attrib, reloadScheme: true);

            (VguiFontAmalgam? nameFont, VguiFontAmalgam? attribFont) = force switch
            {
                1 => (_fontNameLarge, _fontAttribLarge),
                2 => (_fontNameSmall, _fontAttribSmall),
                3 => (_fontNameSmallest, _fontAttribSmallest),
                _ => (_fontNameLarger, _fontAttribLarger),
            };

            name.SetFont(nameFont);
            attrib.SetFont(attribFont);
            name.SizeToContents();
            attrib.SizeToContents();
        }
        else
        {
            name.SetFont(_fontNameLarge);
            name.SetCenterWrap(_item is { } item && Schema is { } schema && item.Attribute(schema, "force center wrap") is not null);
            name.SizeToContents();
            attrib.SetFont(_fontAttribLarge);
            attrib.SizeToContents();

            if (!ResizeToText)
            {
                int attribTall = attrib.Wide != 0 ? attrib.Tall : 0;
                int nameTall = AttribOnly ? 0 : name.Tall;

                if (name.Wide > textW || (!NameOnly && attrib.Wide > textW) || attribTall + nameTall > h)
                {
                    name.SetFont(_fontNameSmall);
                    attrib.SetFont(_fontAttribSmall);
                    Layout(name);
                    name.SizeToContents();

                    attribTall = attrib.Wide != 0 ? attrib.Tall : 0;
                    nameTall = name.Tall;

                    if (name.Wide > textW || (!NameOnly && attrib.Wide > textW) || attribTall + nameTall > h)
                    {
                        name.SetFont(_fontNameSmallest);
                        attrib.SetFont(_fontAttribSmallest);
                        Layout(name);
                        name.SizeToContents();
                    }
                }
            }
        }

        // "If it still doesn't fit, turn on wrap and pray".
        if (name.Wide > textW)
        {
            name.SetCenterWrap(true);
        }

        if (attrib.Wide > textW)
        {
            attrib.SetWrap(true);
        }

        name.SetSize(textW, name.Tall);
        attrib.SetSize(textW, attrib.Tall);

        if (_equippedLabel is { } equipped)
        {
            (equipped.X, equipped.Y) = (Wide - equipped.Wide - GetInt("inset_eq_x"), Tall - equipped.Tall - GetInt("inset_eq_y"));
        }
    }

    /// <inheritdoc/>
    /// <remarks>`OnSizeChanged` (:2003): laid out at once; the labels' new line positions follow from their own layout.</remarks>
    protected override void OnSizeChanged(int newWide, int newTall)
    {
        base.OnSizeChanged(newWide, newTall);

        if (_context is { } context)
        {
            InvalidateLayout(layoutNow: true, reloadScheme: false, context);
        }
    }
}
