using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// The <c>CEconItemView</c> a <see cref="TfPlayerModelPanel"/> carries (econ_item_view.h): a definition, its quality, its
/// resolved attributes and the client flag <c>kEconItemFlagClient_ForceBlueTeam</c> (tf_playermodelpanel.cpp:1095).
/// </summary>
/// <param name="DefinitionIndex">`m_iItemDefinitionIndex`.</param>
/// <param name="Quality">`GetQuality()`.</param>
/// <param name="Attributes">`IterateAttributes`' answer, by attribute definition index (econ_item_view.cpp:523).</param>
public sealed record TfItemView(int DefinitionIndex, int Quality, IReadOnlyDictionary<int, EconAttributeValue> Attributes)
{
    /// <summary>`kEconItemFlagClient_ForceBlueTeam`, which the paint proxy reads as <c>bAltColor</c>.</summary>
    public bool ForceBlueTeam { get; init; }

    /// <summary>A carried item as the HUD hands it over (tf_hud_playerstatus.cpp:485, :508), attributes resolved.</summary>
    /// <param name="item">The item entity.</param>
    /// <param name="schema">The item schema, for its definition attributes.</param>
    /// <returns>The view, or null for an item with no definition (<c>!IsValid()</c>).</returns>
    public static TfItemView? From(SceneItem item, ItemSchema schema)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(schema);

        if (item.DefinitionIndex is not { } definition)
        {
            return null;
        }

        Dictionary<int, EconAttributeValue> attributes = [];

        foreach (EconAttributeValue attribute in EconAttributes.Resolve(
            item.Wire.Local, item.Wire.NetworkedForDemos, item.Wire.HasValidItemId, schema.DefinitionAttributesFor(definition)))
        {
            attributes[attribute.DefinitionIndex] = attribute;
        }

        return new TfItemView(definition, item.Quality ?? 0, attributes);
    }

    /// <summary>`FindAttribute( pAttrDef, &amp;value )`: the attribute by schema name, or null.</summary>
    /// <param name="schema">The schema naming it.</param>
    /// <param name="name">The attribute's name.</param>
    /// <returns>Its value, or null when the item lacks it.</returns>
    public EconAttributeValue? Attribute(ItemSchema schema, string name)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return schema.AttributeDefinitionIndex(name) is { } index && Attributes.TryGetValue(index, out EconAttributeValue value)
            ? value
            : null;
    }

    /// <summary>
    /// `GetItemStyle()` (econ_item_view.cpp:731): `item style override` first. **Partly ported:** `style changes on
    /// strange level` (:747-776) needs `item_levels`, which the schema does not read yet; the SOC branch (:779) reads an
    /// inventory the viewer subscribes to (:853), and a demo has none.
    /// </summary>
    /// <param name="schema">The schema.</param>
    /// <returns>The style, or null for <c>INVALID_STYLE_INDEX</c>.</returns>
    public int? Style(ItemSchema schema) => Attribute(schema, "item style override") is { } style ? (int)style.Value : null;
}

/// <summary>
/// <c>CTFPlayerModelPanel : CBaseModelPanel</c> (game/client/tf/vgui/tf_playermodelpanel.cpp): the class model dressed in
/// the carried items, holding one of them.
/// </summary>
/// <remarks>
/// <para>
/// **Every model goes through <c>OnModelLoadComplete</c>** (:1194). <c>LoadAndAttachAdditionalModel</c> (:1129) takes the
/// hard-load branch only for a model with no model index; every item a player wears or holds is a networked entity whose
/// model the server set, so it has one, and the callback fires once the model is loaded — here, the first frame
/// <see cref="VguiMdlPanel.MdlCache"/> finds it.
/// </para>
/// <para>
/// **Not applicable to a demo:** <c>ToggleZoom</c> (:1328, a UI command), <c>PlayVCD</c>/choreo scenes (:481, set by the
/// loadout and taunt UIs, never by tf_hud_playerstatus.cpp), the phoneme and flex state (:125), and the inventory branch
/// of <c>GetItemInSlot</c> (:884, the viewer's own GC inventory).
/// </para>
/// </remarks>
public sealed class TfPlayerModelPanel : VguiBaseModelPanel
{
    private const int ClassUndefined = 0;
    private const int ClassSpy = 8;
    private const int TeamRed = 2;
    private const int TeamBlue = 3;

    /// <summary>`LOADOUT_POSITION_PRIMARY`/`SECONDARY` and `MAX_WEAPON_SLOTS` (shareddefs.h: 6).</summary>
    private const int MaxWeaponSlots = 6;

    /// <summary>`s_pszDefaultAnimForWpnSlot` (:956), indexed by `TF_WPN_TYPE_*`.</summary>
    private static readonly string?[] DefaultAnimForWpnSlot =
    [
        "ACT_MP_STAND_PRIMARY", "ACT_MP_STAND_SECONDARY", "ACT_MP_STAND_MELEE", null, "ACT_MP_STAND_BUILDING",
        "ACT_MP_STAND_PDA", "ACT_MP_STAND_ITEM1", "ACT_MP_STAND_ITEM2", null, null, "ACT_MP_STAND_MELEE_ALLCLASS",
        "ACT_MP_STAND_SECONDARY2", "ACT_MP_STAND_PRIMARY", "ACT_MP_STAND_ITEM3", "ACT_MP_STAND_ITEM4", "ACT_MP_STAND_PASSTIME",
    ];

    private readonly List<TfItemView> _itemsToCarry = [];

    /// <summary>`m_vecDynamicAssetsLoaded` with `m_vecItemsLoaded` (:1162-1163): the model and the item it was asked for.</summary>
    private readonly List<(string Path, TfItemView Item)> _dynamicAssetsLoaded = [];

    /// <summary>The `RegisterModelLoadCallback`s (:1166) not yet answered, because the model is not loaded.</summary>
    private readonly List<string> _pendingLoads = [];

    private readonly List<(float Fov, (float X, float Y, float Z) Position, (float X, float Y, float Z) Angles)> _customClassData = [];

    private (float X, float Y, float Z) _angPlayerOrg;
    private string _playerModelOverride = string.Empty;

    /// <summary>`CTFPlayerModelPanel( parent, name )` (:110).</summary>
    /// <param name="parent">The parent, or null.</param>
    /// <param name="name">The name, or null.</param>
    /// <param name="mdlCache">`vgui::MDLCache()`.</param>
    public TfPlayerModelPanel(VguiPanel? parent, string? name, IMdlCache mdlCache)
        : base(parent, name, mdlCache)
    {
    }

    /// <inheritdoc/>
    public override string ClassName => "CTFPlayerModelPanel";

    /// <summary>`m_iCurrentClassIndex`.</summary>
    public int CurrentClassIndex { get; private set; } = ClassUndefined;

    /// <summary>`m_iCurrentSlotIndex`.</summary>
    public int CurrentSlotIndex { get; private set; } = -1;

    /// <summary>`m_iTeam` — `GetTeam()`.</summary>
    public int Team { get; private set; } = TeamRed;

    /// <summary>`m_pHeldItem`.</summary>
    public TfItemView? HeldItem { get; private set; }

    /// <summary>`m_ItemsToCarry`.</summary>
    public IReadOnlyList<TfItemView> ItemsToCarry => _itemsToCarry;

    /// <summary>`m_nBody`: the root's body number as the dressing builds it.</summary>
    private int _body;

    private ItemSchema? Schema => HudViewport.Of(this)?.Items;

    /// <summary>`ApplySettings` (:162): the base's, `_minmode` keys under `cl_hud_minmode`, then `customclassdata`.</summary>
    /// <inheritdoc/>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);

        base.ApplySettings(block, context);

        _angPlayerOrg = ModelAngles;

        if (HudViewport.ConVarsOf(this).GetBool("cl_hud_minmode"))
        {
            block.ProcessResolutionKeys("_minmode");
        }

        _customClassData.Clear();

        if (block.Find("customclassdata") is { } custom)
        {
            foreach (KeyValuesTree data in custom.Children)
            {
                _customClassData.Add((
                    Float(data, "fov"),
                    (Float(data, "origin_x"), Float(data, "origin_y"), Float(data, "origin_z")),
                    (Float(data, "angles_x"), Float(data, "angles_y"), Float(data, "angles_z"))));
            }
        }
    }

    /// <summary>`SetToPlayerClass` (:198).</summary>
    /// <param name="playerClass">The class.</param>
    /// <param name="forceRefresh">Whether to rebuild for the same class.</param>
    /// <param name="playerModelOverride">A model in place of the class's, or null.</param>
    public void SetToPlayerClass(int playerClass, bool forceRefresh = false, string? playerModelOverride = null)
    {
        if (!string.Equals(_playerModelOverride, playerModelOverride ?? string.Empty, StringComparison.OrdinalIgnoreCase))
        {
            forceRefresh = true;
            _playerModelOverride = playerModelOverride ?? string.Empty;
        }

        if (CurrentClassIndex == playerClass && !forceRefresh)
        {
            return;
        }

        CurrentClassIndex = playerClass;

        // `IsValidTFPlayerClass`: scout through engineer.
        if (playerClass is >= 1 and <= 9)
        {
            if (_playerModelOverride.Length > 0)
            {
                SetMDL(_playerModelOverride);
            }
            else
            {
                SetMDL(HudViewport.Of(this)?.ClassModels?.Model(playerClass));
                HoldFirstValidItem();
            }

            if (playerClass < _customClassData.Count)
            {
                FieldOfView = _customClassData[playerClass].Fov;
                ModelOrigin = _customClassData[playerClass].Position;
                ModelAngles = _customClassData[playerClass].Angles;
            }
            else
            {
                ModelAngles = _angPlayerOrg;
            }
        }
        else
        {
            SetMDL(null);
            RemoveAdditionalModels();
        }

        SetTeam(TeamRed);

        _body = 0;
    }

    /// <summary>`HoldFirstValidItem` (:258): the first carried weapon's slot, else the class's base primary or secondary.</summary>
    public void HoldFirstValidItem()
    {
        RemoveAdditionalModels();

        if (CurrentClassIndex == ClassUndefined || Schema is not { } schema)
        {
            return;
        }

        int desiredSlot = -1;

        foreach (TfItemView item in _itemsToCarry)
        {
            if (!schema.IsTauntItem(item.DefinitionIndex, Team, CurrentClassIndex)
                && (schema.IsAWearable(item.DefinitionIndex) || schema.AnimSlot(item.DefinitionIndex) == ItemSchema.AnimSlotNotUsed))
            {
                continue;
            }

            desiredSlot = schema.LoadoutSlot(item.DefinitionIndex, CurrentClassIndex);
            break;
        }

        if (desiredSlot != -1)
        {
            UpdateHeldItem(desiredSlot);
            return;
        }

        // "Some classes only have secondary weapons. Fall back to that." (:294).
        int? baseItem = schema.BaseItemForClass(CurrentClassIndex, ItemSchema.LoadoutSlotPrimary)
            ?? schema.BaseItemForClass(CurrentClassIndex, ItemSchema.LoadoutSlotSecondary);

        if (baseItem is { } definition)
        {
            SwitchHeldItemTo(BaseItem(schema, definition));
        }
    }

    /// <summary>`HoldItemInSlot` (:307).</summary>
    /// <param name="slot">The loadout slot.</param>
    /// <returns>Whether an item there is now held.</returns>
    public bool HoldItemInSlot(int slot) => CurrentClassIndex != ClassUndefined && UpdateHeldItem(slot);

    /// <summary>`HoldItem` (:318).</summary>
    /// <param name="itemNumber">The index into <see cref="ItemsToCarry"/>.</param>
    /// <returns>Whether that item is now held.</returns>
    public bool HoldItem(int itemNumber)
    {
        if (CurrentClassIndex == ClassUndefined || itemNumber >= _itemsToCarry.Count || Schema is not { } schema)
        {
            return false;
        }

        TfItemView item = _itemsToCarry[itemNumber];

        if (Wieldable(schema, item))
        {
            SwitchHeldItemTo(item);
            return true;
        }

        if (schema.LoadoutSlot(item.DefinitionIndex, CurrentClassIndex) != CurrentSlotIndex)
        {
            UpdateHeldItem(CurrentSlotIndex);
            return false;
        }

        HoldFirstValidItem();
        return false;
    }

    /// <summary>`UpdateHeldItem` (:354).</summary>
    private bool UpdateHeldItem(int desiredSlot)
    {
        HeldItem = null;

        if (GetItemInSlot(desiredSlot) is { } item && Schema is { } schema && Wieldable(schema, item))
        {
            SwitchHeldItemTo(item);
            return true;
        }

        // "If we were trying to switch to a new item, and it's not valid, stick to our current" (:371).
        if (desiredSlot != CurrentSlotIndex)
        {
            UpdateHeldItem(CurrentSlotIndex);
            return false;
        }

        HoldFirstValidItem();
        return false;
    }

    /// <summary>
    /// `SwitchHeldItemTo` (:517): the class again, wearables, the item, its slot, pose parameters.
    /// </summary>
    /// <remarks>
    /// **Partly ported.** Missing: the StatTrak module (:550-616, a second bone merge onto the weapon, which
    /// <see cref="VguiMdlPanel"/> cannot do yet); a taunt item's scene or sequence layer (:664-731, choreo and
    /// <c>SetSequenceLayers</c> have no port); the action-slot and taunt particles (:747-764, see
    /// <c>docs/HANDOFF-hud.md</c>). The yeti taunt (:528) is a taunt item and falls under the same gap.
    /// </remarks>
    private void SwitchHeldItemTo(TfItemView item)
    {
        _body = 0;
        HeldItem = item;

        SetToPlayerClass(CurrentClassIndex);

        RemoveAdditionalModels();
        EquipAllWearables(item);
        EquipItem(item);

        if (Schema is not { } schema)
        {
            return;
        }

        CurrentSlotIndex = schema.LoadoutSlot(item.DefinitionIndex, CurrentClassIndex);

        // "update poseparam" (:733).
        IReadOnlyList<(string Name, float Value)> poses = schema.PlayerPoseParametersFor(item.DefinitionIndex, Team);

        if (poses.Count > 0)
        {
            foreach ((string poseName, float value) in poses)
            {
                SetPoseParameterByName(poseName, value);
            }
        }
        else
        {
            SetPoseParameterByName("r_hand_grip", 0f);
        }
    }

    /// <summary>`EquipRequiredLoadoutSlot` (:773), reached only from a taunt's forced slot.</summary>
    /// <param name="requiredSlot">The slot, or -1.</param>
    public void EquipRequiredLoadoutSlot(int requiredSlot)
    {
        if (requiredSlot == ItemSchema.LoadoutSlotInvalid || Schema is not { } schema)
        {
            return;
        }

        foreach (TfItemView item in _itemsToCarry)
        {
            if (schema.IsAWearable(item.DefinitionIndex) || schema.AnimSlot(item.DefinitionIndex) == ItemSchema.AnimSlotNotUsed)
            {
                continue;
            }

            if (requiredSlot == schema.LoadoutSlot(item.DefinitionIndex, CurrentClassIndex))
            {
                EquipItem(item);
                return;
            }
        }

        if (schema.BaseItemForClass(CurrentClassIndex, requiredSlot) is { } baseItem)
        {
            EquipItem(BaseItem(schema, baseItem));
        }
    }

    /// <summary>`UpdateWeaponBodygroups` (:813).</summary>
    private void UpdateWeaponBodygroups(bool modifyDeployedOnlyBodygroups)
    {
        if (Schema is not { } schema)
        {
            return;
        }

        for (int slot = 0; slot < MaxWeaponSlots; slot++)
        {
            if (GetItemInSlot(slot) is not { } item)
            {
                continue;
            }

            bool deployedOnly = schema.HidesBodygroupsWhenDeployedOnly(item.DefinitionIndex);

            if (deployedOnly != modifyDeployedOnlyBodygroups || !(ReferenceEquals(HeldItem, item) || !deployedOnly))
            {
                continue;
            }

            UpdateHiddenBodyGroups(item);
        }
    }

    /// <summary>`UpdateHiddenBodyGroups` (:834): the base block's bodygroups, the style's hidden ones, the world override.</summary>
    private void UpdateHiddenBodyGroups(TfItemView item)
    {
        if (Schema is not { } schema || ModelName is not { } root)
        {
            return;
        }

        foreach ((string group, int state) in schema.BasePlayerBodygroupsFor(item.DefinitionIndex))
        {
            int index = MdlCache.FindBodygroup(root, group);

            if (index == -1)
            {
                continue;
            }

            _body = MdlCache.SetBodygroup(root, index, state, _body);
            SetBody(_body);
        }

        foreach (string group in schema.StyleHiddenBodygroups(item.DefinitionIndex, item.Style(schema)))
        {
            int index = MdlCache.FindBodygroup(root, group);

            if (index == -1)
            {
                continue;
            }

            // "force state to '1' here to mean hidden" (:865).
            _body = MdlCache.SetBodygroup(root, index, 1, _body);
            SetBody(_body);
        }

        // Valve sets m_nBody here and does not call SetBody (:875); the next SetBody carries it.
        // **Divergence:** Valve reads the override for `m_iTeam` (:871); the schema reader keeps one pair per item.
        (int bodyOverride, int stateOverride) = schema.WorldmodelBodygroupOverrideFor(item.DefinitionIndex);

        if (bodyOverride > -1 && stateOverride > -1)
        {
            _body = MdlCache.SetBodygroup(root, bodyOverride, stateOverride, _body);
        }
    }

    /// <summary>`GetItemInSlot` (:882): the first carried item whose slot for this class is <paramref name="slot"/>.</summary>
    private TfItemView? GetItemInSlot(int slot)
    {
        if (Schema is not { } schema)
        {
            return null;
        }

        foreach (TfItemView item in _itemsToCarry)
        {
            if (schema.LoadoutSlot(item.DefinitionIndex, CurrentClassIndex) == slot)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>`EquipAllWearables` (:904).</summary>
    private void EquipAllWearables(TfItemView heldItem)
    {
        if (Schema is not { } schema)
        {
            return;
        }

        // "First, reset all our bodygroups" (:906).
        if (ModelName is { } root)
        {
            foreach ((string group, int state) in schema.DefaultBodygroupStates)
            {
                int index = MdlCache.FindBodygroup(root, group);

                if (index > -1)
                {
                    _body = MdlCache.SetBodygroup(root, index, state, _body);
                }
            }
        }

        SetBody(_body);

        UpdateWeaponBodygroups(false);

        foreach (TfItemView item in _itemsToCarry)
        {
            if (schema.IsAWearable(item.DefinitionIndex))
            {
                EquipItem(item);
            }

            if (schema.ExtraWearableModel(item.DefinitionIndex) is { Length: > 0 } attached)
            {
                string? viewModel = schema.ExtraWearableViewModel(item.DefinitionIndex);

                if (ReferenceEquals(heldItem, item) || viewModel is null || viewModel.Length == 0 || viewModel[0] == '?')
                {
                    LoadAndAttachAdditionalModel(attached, item);
                }
            }
        }

        UpdateWeaponBodygroups(true);

        SetBody(_body);

        UpdatePreviewVisuals();
    }

    /// <summary>`EquipItem` (:980): a weapon's stand sequence, its model and attachments, then its hidden bodygroups.</summary>
    private void EquipItem(TfItemView item)
    {
        if (CurrentClassIndex == ClassUndefined || Schema is not { } schema)
        {
            return;
        }

        int definition = item.DefinitionIndex;

        if (!schema.IsAWearable(definition))
        {
            int animSlot = schema.AnimSlot(definition);

            // "Ignore items that don't want to control player animation" (:996).
            if (animSlot == ItemSchema.AnimSlotNotUsed)
            {
                return;
            }

            if (animSlot == -1)
            {
                animSlot = schema.LoadoutSlot(definition, CurrentClassIndex);
            }

            if (animSlot >= 0 && animSlot < ItemSchema.WeaponTypeSubstrings.Count && RootStudioHdr() is { } model)
            {
                int sequence = FindSequenceFromActivity(model, DefaultAnimForWpnSlot[animSlot]);

                if (sequence != -1)
                {
                    SetSequence(sequence, resetSequence: true);
                }
            }
        }

        string? attached = schema.WorldDisplayModel(definition)
            ?? schema.PlayerDisplayModel(definition, CurrentClassIndex, Team, item.Style(schema));

        if (attached is { Length: > 0 })
        {
            LoadAndAttachAdditionalModel(attached, item);

            // `GetBestVisualTeamData( m_iTeam )` (:1034) then only world-model attachments; festive ones with `is_festivized`.
            bool festivized = item.Attribute(schema, "is_festivized") is not null;

            foreach (AttachedModel model in schema.AttachedModelsFor(definition, Team, festivized))
            {
                if ((model.DisplayFlags & AttachedModel.WorldModel) != 0 && model.Model.Length > 0)
                {
                    LoadAndAttachAdditionalModel(model.Model, item);
                }
            }
        }

        UpdateHiddenBodyGroups(item);
    }

    /// <summary>`AddCarriedItem` (:1086): a copy, flagged to paint blue when this panel is BLU.</summary>
    /// <param name="item">The item.</param>
    /// <returns>Its index.</returns>
    public int AddCarriedItem(TfItemView item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _itemsToCarry.Add(item with { ForceBlueTeam = Team == TeamBlue });

        return _itemsToCarry.Count - 1;
    }

    /// <summary>`ClearCarriedItems` (:1106).</summary>
    public void ClearCarriedItems()
    {
        RemoveAdditionalModels();
        _itemsToCarry.Clear();
        HeldItem = null;
    }

    /// <summary>`RemoveAdditionalModels` (:1116).</summary>
    public void RemoveAdditionalModels()
    {
        ClearMergeMDLs();
        _pendingLoads.Clear();
        _dynamicAssetsLoaded.Clear();
    }

    /// <summary>`LoadAndAttachAdditionalModel` (:1129), by its callback branch (see the class remarks).</summary>
    private void LoadAndAttachAdditionalModel(string path, TfItemView item)
    {
        _dynamicAssetsLoaded.Add((path, item));
        _pendingLoads.Add(path);

        // "callback triggers immediately if not dynamic" (:1165).
        AnswerLoadedCallbacks();
    }

    /// <summary>`modelinfo`'s load callbacks: each model now loaded reaches <see cref="OnModelLoadComplete"/>, in order.</summary>
    private void AnswerLoadedCallbacks()
    {
        for (int index = 0; index < _pendingLoads.Count;)
        {
            string path = _pendingLoads[index];

            if (MdlCache.FindMdl(path) is null)
            {
                index++;
                continue;
            }

            _pendingLoads.RemoveAt(index);
            OnModelLoadComplete(path);
        }
    }

    /// <summary>`OnModelLoadComplete` (:1194): merge it, class bodygroup when per-class, then the team skin.</summary>
    private void OnModelLoadComplete(string path)
    {
        TfItemView? item = null;

        for (int index = _dynamicAssetsLoaded.Count - 1; index >= 0; index--)
        {
            if (string.Equals(_dynamicAssetsLoaded[index].Path, path, StringComparison.Ordinal))
            {
                item = GetPreviewItem(_dynamicAssetsLoaded[index].Item);
                break;
            }
        }

        if (item is null || SetMergeMDL(path) is not { } merged)
        {
            return;
        }

        // `m_pProxyData`: the ItemTintColor proxy reads the item's paint, blue when flagged.
        merged.Paint = Schema is { } schema ? ItemPaint.Tint(item.Attributes, schema, item.ForceBlueTeam) : null;

        if (Schema is { } items && items.UsesPerClassBodygroups(item.DefinitionIndex, Team) && GetMergeMDL(path) is { } mdl)
        {
            // "Classes start at 1, bodygroups at 0, so we shift them all back 1." (:1221).
            mdl.Body = MdlCache.SetBodygroup(path, 1, CurrentClassIndex - 1, 0);
        }

        SetMDLSkinForTeam(GetMergeMDL(path), item, Team);
    }

    /// <summary>`SetMDLSkinForTeam` (:1172): the item's skin, else 0 for RED and 1 otherwise.</summary>
    private void SetMDLSkinForTeam(VguiMdl? mdl, TfItemView item, int team)
    {
        if (mdl is null || Schema is not { } schema)
        {
            return;
        }

        int skin = schema.Skin(item.DefinitionIndex, team, item.Style(schema));

        if (skin == -1)
        {
            // "... if not, use the team skin." (:1184).
            skin = team == TeamRed ? 0 : 1;
        }

        mdl.Skin = skin;
    }

    /// <summary>`SetTeam` (:1234).</summary>
    /// <param name="team">The team.</param>
    public void SetTeam(int team)
    {
        Team = team;

        UpdatePreviewVisuals();
    }

    /// <summary>`UpdatePreviewVisuals` (:1241): the team skin, zombie skin under `player skin override`, then every merge's.</summary>
    /// <remarks>The held weapon's `m_MergeMDL` branch (:1267) is only set by the hard-load branch, which is never taken.</remarks>
    private void UpdatePreviewVisuals()
    {
        int skin = Team == TeamRed ? 0 : 1;

        if (Schema is { } schema)
        {
            foreach (TfItemView item in _itemsToCarry)
            {
                if (item.Attribute(schema, "player skin override") is { Value: 1f })
                {
                    skin = AdjustSkinIndexForZombie(CurrentClassIndex, skin);
                    break;
                }
            }
        }

        SetSkin(skin);

        foreach ((string path, TfItemView item) in _dynamicAssetsLoaded)
        {
            SetMDLSkinForTeam(GetMergeMDL(path), GetPreviewItem(item), Team);
        }
    }

    /// <summary>`C_TFPlayer::AdjustSkinIndexForZombie` (c_tf_player.cpp:7731): +22 for a spy's mask skins, else +4.</summary>
    /// <param name="playerClass">The class.</param>
    /// <param name="skin">The base skin.</param>
    /// <returns>The zombie skin.</returns>
    public static int AdjustSkinIndexForZombie(int playerClass, int skin) => skin + (playerClass == ClassSpy ? 22 : 4);

    /// <summary>`GetPreviewItem` (:1295): the carried item equal to it, else itself.</summary>
    private TfItemView GetPreviewItem(TfItemView match)
    {
        foreach (TfItemView item in _itemsToCarry)
        {
            if (item.DefinitionIndex == match.DefinitionIndex && ReferenceEquals(item.Attributes, match.Attributes))
            {
                return item;
            }
        }

        return match;
    }

    /// <summary>The drawn models, then those a load callback still waits on — `RegisterDynamicModel`'s load (:1137).</summary>
    /// <inheritdoc/>
    public override IEnumerable<string> ModelsToPrecache()
    {
        foreach (string path in base.ModelsToPrecache())
        {
            yield return path;
        }

        foreach (string path in _pendingLoads)
        {
            yield return path;
        }
    }

    /// <summary>`CBaseModelPanel::OnTick`'s place: each frame, before drawing, answer the load callbacks now due.</summary>
    /// <inheritdoc/>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        AnswerLoadedCallbacks();

        base.Paint(surface, context);
    }

    /// <summary>`Wieldable`: `bIsTauntItem || ( !IsAWearable() &amp;&amp; GetAnimationSlot() != -2 )` (:332, :364).</summary>
    private bool Wieldable(ItemSchema schema, TfItemView item) =>
        schema.IsTauntItem(item.DefinitionIndex, Team, CurrentClassIndex)
        || (!schema.IsAWearable(item.DefinitionIndex) && schema.AnimSlot(item.DefinitionIndex) != ItemSchema.AnimSlotNotUsed);

    /// <summary>`m_pBaseLoadoutItems[i]`: `Init( def, AE_USE_SCRIPT_VALUE, ... )` (tf_item_inventory.cpp:250) — no attributes of its own.</summary>
    private static TfItemView BaseItem(ItemSchema schema, int definition)
    {
        Dictionary<int, EconAttributeValue> attributes = [];

        foreach (EconAttributeValue attribute in schema.DefinitionAttributesFor(definition))
        {
            attributes[attribute.DefinitionIndex] = attribute;
        }

        return new TfItemView(definition, 0, attributes);
    }

    /// <summary>`KeyValues::GetFloat( key )`, 0 when absent.</summary>
    private static float Float(KeyValuesTree block, string key) =>
        block.Find(key)?.Value is { } text && float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value)
            ? value
            : 0f;
}
