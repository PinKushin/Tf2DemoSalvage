using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
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
    /// `GetItemStyle()` (econ_item_view.cpp:731): `item style override`, then `style changes on strange level` — the
    /// `kill eater` score's level in its score type's `item_levels` block, capped at the attribute (:747-776). The SOC
    /// branch (:779) reads an inventory the viewer subscribes to (:853), and a demo has none.
    /// </summary>
    /// <param name="schema">The schema.</param>
    /// <returns>The style, or null for <c>INVALID_STYLE_INDEX</c>.</returns>
    public int? Style(ItemSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        // `style_index_t` is a uint8: the float truncates into it.
        if (Attribute(schema, "item style override") is { } style)
        {
            return (byte)style.Value;
        }

        // `FindAttribute( pAttr, &uint32 )` reads the raw 32 bits.
        if (Attribute(schema, "style changes on strange level") is not { } maxStyle)
        {
            return null;
        }

        if (Attribute(schema, "kill eater") is not { } score)
        {
            return 0;
        }

        uint scoreType = Attribute(schema, "kill eater score type") is { } type ? (uint)type.Value : 0u;
        string block = schema.KillEaterLevelingDataName(scoreType) ?? "KillEaterRank";

        if (schema.ItemLevelForScore(block, unchecked((uint)score.RawBits)) is not { } level)
        {
            return 0;
        }

        return (byte)Math.Min(level, unchecked((uint)maxStyle.RawBits));
    }
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

        // "always allow particle for this panel" (:191-192).
        UseParticle = true;

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

        UpdateStatTrack(schema, item);

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

    /// <summary>`m_StatTrackModel` (tf_playermodelpanel.h:218): disabled until a StatTrak weapon is held.</summary>
    public VguiMdl StatTrackModel { get; } = new() { Disabled = true };

    /// <summary>`m_flStatTrackScale`.</summary>
    public float StatTrackScale { get; private set; } = 1f;

    /// <summary>`g_KillEaterAttr`'s score attributes (econ_item_constants.cpp:486-496).</summary>
    private static readonly string[] KillEaterScores =
        ["kill eater", "kill eater 2", "kill eater 3", "kill eater user 1", "kill eater user 2", "kill eater user 3"];

    /// <summary>`AE_STRANGE` (econ_item_constants.h).</summary>
    private const int QualityStrange = 11;

    /// <summary>
    /// `SwitchHeldItemTo`'s StatTrak half (:550-616): a paintkitted weapon with `weapon_uses_stattrak_module`
    /// (`GetStattrak`, econ_item_interface.cpp:483), if strange, shows that module, scaled by
    /// `weapon_stattrak_module_scale`, in `ACT_IDLE`'s place (sequence 1).
    /// </summary>
    /// <remarks>
    /// `IsErrorModel` (:589) is the module failing to load; here an unloaded module simply draws nothing until the cache
    /// has it, and it is precached with the rest.
    /// </remarks>
    private void UpdateStatTrack(ItemSchema schema, TfItemView held)
    {
        StatTrackModel.Disabled = true;
        StatTrackModel.Path = null;

        if (held.Attribute(schema, "paintkit_proto_def_index") is null
            || schema.DefinitionStringAttribute(held.DefinitionIndex, "weapon_uses_stattrak_module") is not { Length: > 0 } module)
        {
            return;
        }

        bool strange = held.Quality == QualityStrange;

        foreach (string score in KillEaterScores)
        {
            strange |= held.Attribute(schema, score) is not null;
        }

        if (!strange)
        {
            return;
        }

        // `m_flStatTrackScale = (float&)unFloatAsUint32` from `weapon_stattrak_module_scale`, else 1 (:581-586).
        StatTrackScale = held.Attribute(schema, "weapon_stattrak_module_scale") is { } scale ? scale.Value : 1f;

        StatTrackModel.Path = module;
        StatTrackModel.Disabled = false;
        StatTrackModel.Sequence = 1;
        StatTrackModel.Paint = ItemPaint.Tint(held.Attributes, schema, held.ForceBlueTeam);
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

        // For `m_iTeam` (:871). Valve sets m_nBody here and does not call SetBody (:875); the next SetBody carries it.
        (int bodyOverride, int stateOverride) = schema.WorldmodelBodygroupOverrideFor(item.DefinitionIndex, Team);

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

        // "Set the StatTrack model skin" (:1273-1278).
        if (!StatTrackModel.Disabled)
        {
            StatTrackModel.Skin = Team == TeamRed ? 0 : 1;
        }

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

    /// <summary>`modelpanel_particle_system_t` (tf_playermodelpanel.h:89-98), indexing `m_aParticleSystems`.</summary>
    private enum ParticleSlot
    {
        Head,
        Misc1,
        Misc2,
        Weapon,
        ActionSlot,
        EyeGlowLeft,
        EyeGlowRight,
        EyeSparkLeft,
        EyeSparkRight,
        Taunt,
        Count,
    }

    /// <summary>`CUSTOM_COLOR_CP1` (effect_dispatch_data.h:37).</summary>
    private const int CustomColorCp1 = 9;

    /// <summary>`s_mergeModelSlot` (:1464-1476).</summary>
    private static readonly (int Position, ParticleSlot System)[] MergeModelSlot =
    [
        (ItemSchema.LoadoutSlotHead, ParticleSlot.Head),
        (ItemSchema.LoadoutSlotMisc, ParticleSlot.Misc1),
        (ItemSchema.LoadoutSlotMisc2, ParticleSlot.Misc2),
        (ItemSchema.LoadoutSlotPrimary, ParticleSlot.Weapon),
        (ItemSchema.LoadoutSlotSecondary, ParticleSlot.Weapon),
        (ItemSchema.LoadoutSlotMelee, ParticleSlot.Weapon),
    ];

    /// <summary>`static bool bAlternate` in `PostPaint3D` (:1370): shared by every panel, flipped each paint.</summary>
    private static bool s_alternate;

    /// <summary>`bAlternate`'s read-then-flip.</summary>
    private static bool Alternate()
    {
        bool value = s_alternate;
        s_alternate = !value;
        return value;
    }

    private readonly ParticleData?[] _particleSystems = new ParticleData?[(int)ParticleSlot.Count];
    private bool _playSparks;
    private bool _updateEyeGlows;
    private string _eyeGlowParticleName = string.Empty;
    private Vector3 _eyeGlowColor1;
    private Vector3 _eyeGlowColor2;

    /// <summary>`C_TFPlayer::GetLocalTFPlayer()->m_Shared.GetDecapitations()` (:1748), for the demoman's left eye.</summary>
    public int LocalDecapitations { get; set; }

    /// <summary>`m_aParticleSystems`' system names, for a test or a diagnostic; null where a slot is empty.</summary>
    public IReadOnlyList<string?> ParticleSystemNames
    {
        get
        {
            string?[] names = new string?[_particleSystems.Length];

            for (int index = 0; index < names.Length; index++)
            {
                names[index] = _particleSystems[index]?.Effect.System.Name;
            }

            return names;
        }
    }

    /// <summary>`SetEyeGlowEffect` (:1860-1884).</summary>
    /// <param name="effectName">The glow system, or null.</param>
    /// <param name="color1">`m_vEyeGlowColor1`.</param>
    /// <param name="color2">`m_vEyeGlowColor2`.</param>
    /// <param name="forceUpdate">Whether to rebuild the glows.</param>
    /// <param name="playSparks">Whether to play the kill spark.</param>
    public void SetEyeGlowEffect(string? effectName, Vector3 color1, Vector3 color2, bool forceUpdate, bool playSparks)
    {
        _eyeGlowColor1 = color1;
        _eyeGlowColor2 = color2;
        _playSparks = playSparks;

        if (forceUpdate)
        {
            _updateEyeGlows = true;
        }

        if (effectName is null)
        {
            if (_eyeGlowParticleName.Length > 0)
            {
                _updateEyeGlows = true;
            }

            _eyeGlowParticleName = string.Empty;
        }
        else if (!string.Equals(_eyeGlowParticleName, effectName, StringComparison.Ordinal))
        {
            _eyeGlowParticleName = effectName;
            _updateEyeGlows = true;
        }
    }

    /// <summary>`PostPaint3D` (:1363-1405): the glow color alternates, flags reset, stale systems deleted, then the base renders.</summary>
    /// <inheritdoc/>
    protected override void PostPaint3D(VguiRenderContext renderContext)
    {
        Vector3 color = Alternate() ? _eyeGlowColor1 : _eyeGlowColor2;

        foreach (ParticleSlot slot in (ReadOnlySpan<ParticleSlot>)[ParticleSlot.EyeGlowRight, ParticleSlot.EyeSparkRight, ParticleSlot.EyeGlowLeft, ParticleSlot.EyeSparkLeft])
        {
            _particleSystems[(int)slot]?.Effect.SetControlPoint(CustomColorCp1, ParticleControlPoint.Unoriented(color));
        }

        _updateEyeGlows = false;
        _playSparks = false;

        for (int index = 0; index < _particleSystems.Length; index++)
        {
            if (_particleSystems[index] is { IsUpdateToDate: false })
            {
                SafeDeleteParticleData(ref _particleSystems[index]);
            }
        }

        base.PostPaint3D(renderContext);
    }

    /// <summary>`RenderingRootModel` (:1411-1425): both eyes, then the action-slot and taunt effects.</summary>
    /// <inheritdoc/>
    protected override void RenderingRootModel(
        VguiRenderContext renderContext, PropModels.ModelFrames studioHdr, string mdlHandle, BoneAccessor worldMatrix)
    {
        if (!UseParticle)
        {
            return;
        }

        UpdateEyeGlows(studioHdr, worldMatrix, isRightEye: true);
        UpdateEyeGlows(studioHdr, worldMatrix, isRightEye: false);
    }

    /// <summary>`RenderingMergedModel` (:1459-1512): the merge model's item by slot, then its unusual effect.</summary>
    /// <inheritdoc/>
    protected override void RenderingMergedModel(
        VguiRenderContext renderContext, PropModels.ModelFrames studioHdr, string mdlHandle, BoneAccessor worldMatrix)
    {
        if (!UseParticle)
        {
            return;
        }

        // `pEconItem` is reassigned every iteration (:1485), so a pass that runs on past an up-to-date match ends on
        // whatever the LAST slot answered — null when that slot has no match.
        (TfItemView Item, ParticleSlot System, int Position)? picked = null;

        for (int index = 0; index < MergeModelSlot.Length; index++)
        {
            (int position, ParticleSlot slot) = MergeModelSlot[index];

            if (GetLoadoutItemFromMDLHandle(position, mdlHandle) is not { } found)
            {
                picked = null;
                continue;
            }

            picked = (found, slot, position);

            // "this fixes multiple unusual cosmetics with same default loadout to update their particles" (:1491).
            if (_particleSystems[(int)slot] is not { IsUpdateToDate: true })
            {
                break;
            }
        }

        if (picked is not { } chosen)
        {
            return;
        }

        UpdateCosmeticParticles(studioHdr, worldMatrix, chosen.System, chosen.Item);

        if (CurrentSlotIndex == chosen.Position)
        {
            RenderStatTrack(renderContext, mdlHandle);
        }
    }

    /// <summary>`RenderStatTrack` (:1539-1581): the module bone-merged onto the weapon, scaled, and drawn.</summary>
    private void RenderStatTrack(VguiRenderContext renderContext, string weaponPath)
    {
        if (!StatTrackModel.Disabled)
        {
            DrawBoneMergedOnto(renderContext, StatTrackModel, weaponPath, StatTrackScale);
        }
    }

    /// <summary>`GetLoadoutItemFromMDLHandle` (:1427-1456): the carried item in that slot whose display model is this one.</summary>
    private TfItemView? GetLoadoutItemFromMDLHandle(int position, string modelName)
    {
        if (Schema is not { } schema)
        {
            return null;
        }

        foreach (TfItemView item in _itemsToCarry)
        {
            int slot = schema.LoadoutSlot(item.DefinitionIndex, CurrentClassIndex);

            if (((IsMiscSlot(slot) && IsMiscSlot(position)) || (IsValidPickupWeaponSlot(slot) && slot == position))
                && schema.PlayerDisplayModel(item.DefinitionIndex, CurrentClassIndex, Team, item.Style(schema)) is { } display
                && string.Equals(modelName, display, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>`UpdateCosmeticParticles` (:1584-1714).</summary>
    private void UpdateCosmeticParticles(PropModels.ModelFrames studioHdr, BoneAccessor worldMatrix, ParticleSlot slot, TfItemView item)
    {
        if (Schema is not { } schema || _particleSystems[(int)slot] is { IsUpdateToDate: true })
        {
            return;
        }

        // `attach particle effect`, its value read as a float and passed as an int id (:1600-1603). The quality sparkle
        // (:1609) reads SOC data (econ_item_view.cpp:1080), which a demo lacks: 0.
        AttributeParticleSystem? particleSystem = item.Attribute(schema, "attach particle effect") is { } effect
            ? schema.AttributeControlledParticleSystem((int)effect.Value)
            : null;

        if (particleSystem?.SystemName is not { } baseName)
        {
            return;
        }

        if (Team == TeamBlue && baseName.Contains("_teamcolor_red", StringComparison.OrdinalIgnoreCase))
        {
            particleSystem = schema.FindAttributeControlledParticleSystem(baseName.Replace("_teamcolor_red", "_teamcolor_blue", StringComparison.Ordinal));

            if (particleSystem?.SystemName is null)
            {
                return;
            }
        }

        PropModels.SkinnedModel? model = studioHdr.Skinned;
        int bone = BoneIndexByName(model, "bip_head");

        if (bone < 0)
        {
            bone = BoneIndexByName(model, "prp_helmet");

            if (bone < 0)
            {
                bone = BoneIndexByName(model, "prp_hat");
            }
        }

        if (bone < 0)
        {
            bone = 0;
        }

        // `if ( !FindAttribute( pAttrDef_UseHead, &iUseHead ) || !iUseHead == 0 )` (:1655): `!iUseHead == 0` is
        // `iUseHead != 0`, so the attachments are searched when the attribute is absent OR non-zero.
        List<int> attachments = [];
        EconAttributeValue? useHeadAttribute = item.Attribute(schema, "particle effect use head origin");
        uint useHead = useHeadAttribute is { } head ? unchecked((uint)head.RawBits) : 0u;

        if (useHeadAttribute is null || useHead != 0)
        {
            foreach (string? name in particleSystem.ControlPoints)
            {
                if (name is { Length: > 0 } && FindAttachment(studioHdr, name) is var found and >= 0)
                {
                    attachments.Add(found);
                }
            }
        }

        string systemName = particleSystem.SystemName;

        // "Weapon Remap for a Base Effect to be used on a specific weapon" (:1674).
        if (particleSystem.UseSuffixName && schema.ParticleSuffix(item.DefinitionIndex) is { } suffix)
        {
            systemName = systemName + "_" + suffix;
        }

        ref ParticleData? data = ref _particleSystems[(int)slot];

        if (data is not null)
        {
            if (!string.Equals(data.Effect.System.Name, systemName, StringComparison.Ordinal))
            {
                SafeDeleteParticleData(ref data);
                data = CreateParticleData(systemName);
            }
        }
        else
        {
            data = CreateParticleData(systemName);
        }

        if (data is null)
        {
            return;
        }

        Vector3 offset = Vector3.Zero;

        if (useHead > 0 && item.Attribute(schema, "particle effect vertical offset") is { } vertical)
        {
            offset = new Vector3(0f, 0f, vertical.Value);
        }

        data.UpdateControlPoints(studioHdr, worldMatrix, attachments, bone, offset);
    }

    /// <summary>`UpdateEyeGlows` (:1718-1791).</summary>
    private void UpdateEyeGlows(PropModels.ModelFrames studioHdr, BoneAccessor worldMatrix, bool isRightEye)
    {
        ParticleSlot eyeSystem = isRightEye ? ParticleSlot.EyeGlowRight : ParticleSlot.EyeGlowLeft;
        ParticleSlot sparkSystem = isRightEye ? ParticleSlot.EyeSparkRight : ParticleSlot.EyeSparkLeft;

        int attachment = FindAttachment(studioHdr, isRightEye ? "eyeglow_R" : "eyeglow_L");

        if (attachment == -1)
        {
            return;
        }

        if (_updateEyeGlows)
        {
            string? glowEffectName = _eyeGlowParticleName;

            SafeDeleteParticleData(ref _particleSystems[(int)eyeSystem]);

            // "demo man has a green eyeglow for eyelander if applicable" (:1742).
            if (!isRightEye && CurrentClassIndex == TfKillStreakEyes.Demoman)
            {
                glowEffectName = TfKillStreakEyes.DemomanEyeEffectName(LocalDecapitations);
            }

            if (glowEffectName is { Length: > 0 })
            {
                _particleSystems[(int)eyeSystem] = CreateParticleData(glowEffectName);
            }
        }

        if (_playSparks && _eyeGlowColor1 != Vector3.Zero)
        {
            SafeDeleteParticleData(ref _particleSystems[(int)sparkSystem]);

            // "Generate an eye spark as well not for demo" (:1763).
            _particleSystems[(int)sparkSystem] = CreateParticleData("killstreak_t0_lvl1_flash");
        }

        // `matAttachToWorld` is never set, so its forward column times `flOffset` 0 is zero (:1771-1780).
        _particleSystems[(int)eyeSystem]?.UpdateControlPoints(studioHdr, worldMatrix, [attachment]);
        _particleSystems[(int)sparkSystem]?.UpdateControlPoints(studioHdr, worldMatrix, [attachment]);
    }

    /// <summary>`Studio_FindAttachment` (bone_setup.cpp): the first attachment of that name, case ignored, or -1.</summary>
    private static int FindAttachment(PropModels.ModelFrames studioHdr, string name)
    {
        IReadOnlyList<StudioAttachment> attachments = studioHdr.Attachments ?? [];

        for (int index = 0; index < attachments.Count; index++)
        {
            if (string.Equals(attachments[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>`Studio_BoneIndexByName` (bone_setup.cpp): the bone of that name, case ignored, or -1.</summary>
    private static int BoneIndexByName(PropModels.SkinnedModel? model, string name)
    {
        IReadOnlyList<StudioBone> bones = model?.Bones ?? [];

        for (int index = 0; index < bones.Count; index++)
        {
            if (string.Equals(bones[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>`IsMiscSlot` (tf_item_constants.h:105).</summary>
    private static bool IsMiscSlot(int slot) => slot is ItemSchema.LoadoutSlotMisc or ItemSchema.LoadoutSlotMisc2 or ItemSchema.LoadoutSlotHead;

    /// <summary>`IsValidPickupWeaponSlot` (tf_item_constants.h:152).</summary>
    private static bool IsValidPickupWeaponSlot(int slot) =>
        slot is ItemSchema.LoadoutSlotPrimary or ItemSchema.LoadoutSlotSecondary or ItemSchema.LoadoutSlotMelee;

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

        if (!StatTrackModel.Disabled && StatTrackModel.Path is { } module)
        {
            yield return module;
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
