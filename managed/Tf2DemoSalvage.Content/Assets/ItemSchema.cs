using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>An extra model an item hangs on itself, from the schema's <c>attached_models</c>.</summary>
/// <param name="Model">The <c>.mdl</c> path, as the schema writes it.</param>
/// <param name="DisplayFlags">
/// <c>model_display_flags</c>: <c>kAttachedModelDisplayFlag_WorldModel</c> is 1 and
/// <c>kAttachedModelDisplayFlag_ViewModel</c> is 2 (<c>econ_item_schema.h:881</c>). The draw is
/// filtered on it — <c>DrawEconEntityAttachedModels</c> takes a mask and skips anything the mask
/// does not match, so a pilot light meant for the viewmodel does not appear on the world weapon.
/// </param>
/// <param name="Team">
/// <c>""</c> for a plain <c>visuals</c> block, or <c>red</c> / <c>blu</c> for the per-team ones.
/// <c>GetNumAttachedModels( iTeam )</c> takes the team, and the shipped schema uses the split for
/// exactly one item — rare, and free to honour.
/// </param>
/// <param name="Festive">
/// Whether it came from <c>attached_models_festive</c>, which the engine adds only when the item
/// carries the <c>is_festivized</c> attribute (<c>econ_entity.cpp:1109</c>). 310 blocks in the
/// shipped schema against 29 plain ones, so treating the two alike would put a festive attachment
/// on every ordinary weapon.
/// </param>
/// <param name="Key">The child's name in its block (`"0"`, `"1"`), by which a prefab merge matches it.</param>
public readonly record struct AttachedModel(
    string Model, int DisplayFlags, string Team, bool Festive, string Key = "")
{
    /// <summary><c>kAttachedModelDisplayFlag_WorldModel</c>.</summary>
    public const int WorldModel = 0x01;

    /// <summary><c>kAttachedModelDisplayFlag_ViewModel</c>.</summary>
    public const int ViewModel = 0x02;

    /// <summary><c>kAttachedModelDisplayFlag_MaskAll</c>, and the schema's default.</summary>
    /// <remarks>
    /// `pKVAttachedModelData->GetInt( "model_display_flags", kAttachedModelDisplayFlag_MaskAll )`
    /// (<c>econ_item_schema.cpp:2503</c>) — so an entry that says nothing shows in both views.
    /// Defaulting to zero instead would hide every one of them, silently.
    /// </remarks>
    public const int MaskAll = WorldModel | ViewModel;
}

/// <summary>
/// One of an item definition's own particle systems — an <c>attachedparticlesystem_t</c> its visuals block declares
/// (<c>econ_item_schema.cpp:2516-2535</c>, B69).
/// </summary>
/// <param name="System"><c>pszSystemName</c>: <c>system</c>, or null.</param>
/// <param name="Attachment"><c>pszControlPoints[0]</c>: <c>attachment</c>, or null.</param>
/// <param name="FollowRootBone"><c>bFollowRootBone</c>: <c>attach_to_rootbone</c> as <c>GetBool</c>.</param>
/// <param name="CustomType">
/// <c>iCustomType</c>: 0 from <c>attached_particlesystems</c>, 1 from <c>custom_particlesystem</c>, 2 from
/// <c>custom_particlesystem2</c>. <c>SetParticleSystemsVisible</c> skips any non-zero one (<c>econ_entity.cpp:1550-1552</c>).
/// </param>
public readonly record struct AttachedParticle(string? System, string? Attachment, bool FollowRootBone, int CustomType);


/// <summary>
/// TF2's item schema, reduced to the question "what model is this item".
/// </summary>
/// <remarks>
/// **A demo names the item and the item schema names the model.** The weapon a player sees in their
/// own hands is a client-side entity the recording cannot carry
/// (<c>econ_entity.cpp:1153</c>, <c>InitializeAsClientEntity</c>), and most weapon scripts no longer
/// hold the path — six of the nine weapon classes in the corpus answer "viewmodel is now defined in
/// _items_main.txt". What the demo does carry is
/// <c>DT_ScriptCreatedItem.m_iItemDefinitionIndex</c>, networked from the 2009 build on.
///
/// **The resolution order is <c>CEconItemView::GetPlayerDisplayModel</c>'s**
/// (<c>econ_item_view.cpp:924</c>): a style's model if the item has styles, then the definition's
/// per-class model, then its base model. Styles are not implemented — see the remarks on
/// <see cref="ModelFor"/>.
///
/// **Everything is inherited through prefabs, and that is not an optimisation in the file — it is
/// where the data lives.** A stock weapon's definition is a name and a <c>prefab</c>; the model,
/// the attach flag and the rest are one or more levels up. A reader that looked only at the
/// definition would answer nothing for every stock weapon in the game.
///
/// **Read once and kept**, because the shipped file is eight megabytes of KeyValues and a viewer
/// asks this question every time a player changes weapon.
/// </remarks>
public sealed class ItemSchema
{
    /// <summary>What one definition or prefab says, before inheritance is applied.</summary>
    private sealed class Entry
    {
        /// <summary>The prefabs it inherits from, in the order the schema names them.</summary>
        public List<string> Prefabs { get; } = [];

        /// <summary>Its <c>model_player</c>, <c>""</c> included, or null.</summary>
        public string? Model { get; set; }

        /// <summary>Its <c>attach_to_hands</c>, or null when it does not say.</summary>
        public bool? AttachToHands { get; set; }

        /// <summary>Its <c>drop_type</c>, or null when it does not say.</summary>
        public string? DropType { get; set; }

        /// <summary>Its <c>item_slot</c>, or null when it does not say.</summary>
        public string? LoadoutSlot { get; set; }

        /// <summary>Its <c>used_by_classes</c> block, class name to "1" or a slot name (tf_item_schema.cpp:958).</summary>
        public Dictionary<string, string> UsedByClasses { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Its <c>item_rarity</c>, or null (econ_item_schema.cpp:3081).</summary>
        public string? ItemRarity { get; set; }

        /// <summary>Its <c>model_player_per_class</c> entries, keyed by the schema's class name.</summary>
        public Dictionary<string, string> PerClass { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Its <c>model_player_per_class</c> <c>basename</c>, or null.</summary>
        /// <remarks>
        /// **The block's second form, and the one that was silently dropped.** A `basename` carries
        /// `%s` placeholders that <c>InitPerClassStringArray</c> expands per class rather than a
        /// map of class to path — `models/player/items/%s/%s_cap.mdl` becomes
        /// `models/player/items/scout/scout_cap.mdl`. Stored as a key called "basename" it looked
        /// like a class nobody plays, so every item using this form resolved to nothing.
        /// </remarks>
        public string? PerClassBaseName { get; set; }

        /// <summary>The entity class it is, such as <c>tf_weapon_scattergun</c>.</summary>
        public string? ItemClass { get; set; }

        /// <summary>The <c>item_name</c> key: a localisation token, or the raw name when it has none.</summary>
        public string? ItemName { get; set; }

        /// <summary>The <c>propername</c> key, read with <c>GetInt</c>.</summary>
        public string? ProperName { get; set; }

        /// <summary>Its <c>attached_models</c> and <c>attached_models_festive</c>, in schema order.</summary>
        public List<AttachedModel> AttachedModels { get; } = [];

        /// <summary>Its attached and custom particle systems, per block, with each child's name (B69).</summary>
        public List<(string Team, string Key, AttachedParticle Particle)> AttachedParticles { get; } = [];

        /// <summary>The wearer's body parts it changes — <c>player_bodygroups</c> (B352).</summary>
        /// <remarks>
        /// **How a hat removes the head it sits on.** `CEconEntity::UpdateBodygroups`
        /// (<c>econ_entity.cpp:2024</c>) resolves each name on the WEARER and sets that group, so a
        /// cosmetic hides the default part it replaces rather than sitting on top of it.
        ///
        /// **Keyed by name because the engine resolves by name** — `FindBodygroupByName` — and the
        /// index differs per class model. A dictionary rather than a list because a bodygroup is one
        /// state per name: unlike <see cref="AttachedModels"/>, there is nothing to accumulate.
        /// </remarks>
        public Dictionary<string, int> PlayerBodygroups { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether it only changes them while it is the active weapon (B352).</summary>
        /// <remarks>
        /// **`hide_bodygroups_deployed_only`, which is why the Fists of Steel only enlarge the
        /// hands while they are out.** It selects which of the engine's two weapon passes handles
        /// the item, and the second one skips it unless the player is holding it:
        ///
        /// <code>
        ///   if ( bHideBodygroupsDeployedOnly != bHandleDeployedBodygroups ) continue;
        ///   if ( bHideBodygroupsDeployedOnly &amp;&amp; pPlayer-&gt;GetActiveWeapon() != pWpn ) continue;
        /// </code>
        ///
        /// (<c>tf_weaponbase.cpp:6222</c>.) Eight shipped items declare it and all eight are
        /// weapons — six pairs of fists, plus the Short Circuit.
        ///
        /// **Null rather than false when the item does not say**, so a prefab's answer is
        /// distinguishable from an item's own denial: the schema writes the key on the item and on
        /// prefabs like <c>weapon_gru</c>, and a bool defaulting to false makes the first entry in
        /// the chain look like a deliberate "no".
        /// </remarks>
        public bool? HideBodygroupsDeployedOnly { get; set; }

        /// <summary>The vision needed to see it at all, or null when it does not say (B354).</summary>
        /// <remarks>
        /// **`vision_filter_flags`, read straight off the item definition**
        /// (<c>econ_item_schema.cpp:3156</c>) and consumed by
        /// `CEconEntity::ShouldHideForVisionFilterFlags` (<c>econ_entity.cpp:1820</c>), which hides
        /// the item from any VIEWER lacking that vision.
        ///
        /// **On the item, not inside `visuals`**, unlike the bodygroup override two members down —
        /// checked against the shipped file rather than inferred from its neighbours.
        ///
        /// **Null rather than 0 when unstated**, so an item can turn a prefab's filter off. The
        /// engine gets that free by merging the prefab chain into one KeyValues block before
        /// reading it; here the chain is walked, so "states 0" and "states nothing" have to stay
        /// distinguishable or a prefab's filter would be unremovable.
        /// </remarks>
        public int? VisionFilterFlags { get; set; }

        /// <summary>Its definition attributes by NAME, from both shipped forms.</summary>
        /// <remarks>
        /// The named block (<c>"attributes" { "damage bonus" { … "value" "1.1" } }</c>) and the
        /// flat pair (<c>"static_attrs" { "is_festivized" "1" }</c>) both feed the engine's
        /// definition iterator, so both land here. Name-keyed because that is how the file spells
        /// them; the index arrives from the top-level <c>attributes</c> section at resolve time.
        /// </remarks>
        public List<(string Name, string Value)> DefinitionAttributes { get; } = [];

        /// <summary>Whether it is the stock item for its class, from <c>baseitem</c>.</summary>
        public bool IsBaseItem { get; set; }

        /// <summary>Which visuals blocks it declares — "" for `visuals`, then `red`, `blu`, `mvm_boss`.</summary>
        /// <remarks>
        /// **Presence is itself an answer**: a team whose block exists uses that block alone
        /// (`GetBestVisualTeamData`), so a red block that sets no sound hides the base block's sound for red.
        /// </remarks>
        public HashSet<string> VisualsSections { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// `sound_&lt;category&gt;` replacements (`econ_item_schema.cpp:2648`), keyed <c>block/category</c> and compared
        /// without case, as the engine's `Q_stricmp` does for both.
        /// </summary>
        public Dictionary<string, string> WeaponSounds { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Its `tags` block's names, which `HasEconTag` asks (B767).</summary>
        public HashSet<string> Tags { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Scalar keys the model panel reads (`model_world`, `extra_wearable`, `anim_slot`, ...), by key.</summary>
        public Dictionary<string, string> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether it declares a `taunt` block (tf_item_schema.cpp:1041).</summary>
        public bool HasTauntData { get; set; }

        /// <summary>Visuals scalars (`skin`, `use_per_class_bodygroups`), keyed <c>block/key</c>.</summary>
        public Dictionary<string, string> VisualKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>`player_poseparam` per visuals block (econ_item_schema.cpp:2583).</summary>
        public Dictionary<string, List<(string Name, float Value)>> PlayerPoseParams { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>`animation_replacement` per visuals block (econ_item_schema.cpp:2551-2558): activity to replacement.</summary>
        public Dictionary<string, Dictionary<string, string>> ActivityReplacements { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The visuals blocks with an `animation_*` entry for `taunt_concept` (econ_item_schema.cpp:2551-2582).</summary>
        public HashSet<string> TauntConceptBlocks { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>`player_bodygroups` of the base `visuals` block alone.</summary>
        public Dictionary<string, int> BasePlayerBodygroups { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The base visuals block's `styles`, or null when it declares none (econ_item_schema.cpp:2686-2693).</summary>
        public List<ItemStyle>? Styles { get; set; }
    }

    /// <summary>One `model_player_per_class*` block: class entries and an optional `basename` (tf_item_schema.cpp:489).</summary>
    private sealed class PerClassBlock
    {
        public Dictionary<string, string> PerClass { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? BaseName { get; set; }
    }

    /// <summary>`CTFStyleInfo` (tf_item_schema.cpp:1150) plus `CEconStyleInfo::BInitFromKV` (econ_item_schema.cpp:2824).</summary>
    private sealed class ItemStyle
    {
        /// <summary>`skin`, or null: it sets every team (:2831).</summary>
        public int? CommonSkin { get; set; }

        /// <summary>`skin_red`, default 0 (tf_item_schema.cpp:1154).</summary>
        public int SkinRed { get; set; }

        /// <summary>`skin_blu`, default 0 (:1155).</summary>
        public int SkinBlu { get; set; }

        /// <summary>`model_player` (:1160, econ_item_schema.cpp:2864).</summary>
        public string? ModelPlayer { get; set; }

        /// <summary>`GetString( "name", "#TF_UnknownStyle" )` (econ_item_schema.cpp:2863).</summary>
        public string Name { get; set; } = "#TF_UnknownStyle";

        /// <summary>`image_inventory` (:2866), or null.</summary>
        public string? InventoryImage { get; set; }

        /// <summary>`m_bUseSmokeParticleEffect`, true unless the style says otherwise (econ_item_schema.cpp:2867, B69).</summary>
        public bool UseSmokeParticleEffect { get; set; } = true;

        /// <summary>`m_pszPlayerDisplayModel[0]`: the last of `model_player_per_class`/`_red` (:1170-1171).</summary>
        public PerClassBlock? Red { get; set; }

        /// <summary>`m_pszPlayerDisplayModel[1]`: `model_player_per_class_blue` (:1172).</summary>
        public PerClassBlock? Blue { get; set; }

        /// <summary>`additional_hidden_bodygroups` names (econ_item_schema.cpp:2853).</summary>
        public List<string> HideBodygroups { get; } = [];

        /// <summary>`bodygroup/name` (:2872), or null.</summary>
        public string? BodygroupName { get; set; }

        /// <summary>`bodygroup/submodel_index` (:2874), -1 unset.</summary>
        public int BodygroupSubmodelIndex { get; set; } = -1;

        /// <summary>`CEconStyleInfo::GetSkin( iTeam, false )` (econ_item_schema.h:994).</summary>
        public int Skin(int team) => CommonSkin ?? team switch
        {
            RedTeam => SkinRed,
            BluTeam => SkinBlu,
            _ => 0,
        };
    }

    /// <summary>`pWeaponSoundCategories` (`weapon_parse.cpp:20`), indexed by `WeaponSound_t`.</summary>
    private static readonly string[] WeaponSoundCategories =
    [
        "empty", "single_shot", "single_shot_npc", "double_shot", "double_shot_npc", "burst", "reload",
        "reload_npc", "melee_miss", "melee_hit", "melee_hit_world", "special1", "special2", "special3", "taunt",
        "deploy",
    ];

    /// <summary>How deep a prefab chain is followed before giving up.</summary>
    /// <remarks>
    /// A schema with a cycle would otherwise hang the viewer. Six is well past the deepest real
    /// chain — a weapon sits on a weapon prefab which sits on a base prefab — and a limit that is
    /// hit is a fact worth having rather than a crash.
    /// </remarks>
    private const int LongestChain = 6;

    /// <summary>The class names the schema uses, indexed by TF2's class number.</summary>
    /// <remarks>
    /// **Not the same spellings the rest of this project uses.** The schema writes <c>demoman</c>
    /// and <c>heavy</c> where <c>tf_shareddefs.h</c>'s enum reads <c>TF_CLASS_DEMOMAN</c> and
    /// <c>TF_CLASS_HEAVYWEAPONS</c>, and a per-class lookup spelled either of those finds nothing
    /// and silently falls back to the base model — a wrong hat rather than an error.
    /// </remarks>
    private static readonly string[] ClassNames =
    [
        "", "scout", "sniper", "soldier", "demoman",
        "medic", "heavy", "pyro", "spy", "engineer",
    ];

    private readonly Dictionary<int, Entry> _items = [];

    private readonly Dictionary<string, Entry> _prefabs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The top-level <c>attributes</c> section's name → definition index bridge.</summary>
    private readonly Dictionary<string, int> _attributeIndexByName =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Definition indices whose value is an integer in the 32-bit union, not a float.</summary>
    private readonly HashSet<int> _attributeStoredAsInteger = [];

    /// <summary>Each attribute definition's <c>attribute_class</c>, the name a hook asks for.</summary>
    private readonly Dictionary<int, string> _attributeClass = [];

    /// <summary>Each attribute definition's <c>description_format</c>, which decides how a hook applies it.</summary>
    private readonly Dictionary<int, string> _attributeFormat = [];

    /// <summary>Each attribute definition's scalar keys, as `BInitFromKV` reads them (econ_item_schema.cpp:2175).</summary>
    private readonly Dictionary<int, Dictionary<string, string>> _attributeKeys = [];

    /// <summary>One `item_sets` entry as the file has it, items by definition name.</summary>
    private sealed class ItemSetEntry
    {
        public string? LocalizedName { get; set; }

        public bool IsHidden { get; set; }

        public List<string> ItemNames { get; } = [];

        public List<(string Name, float Value)> Attributes { get; } = [];
    }

    /// <summary>`item_sets` by name, in file order.</summary>
    private readonly Dictionary<string, ItemSetEntry> _itemSets = new(StringComparer.Ordinal);

    /// <summary>Items by `name`, the first of a name keeping it.</summary>
    private readonly Dictionary<string, int> _itemIndexByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Each item's `name`, `GetDefinitionName()`.</summary>
    private readonly Dictionary<int, string> _itemNames = [];

    /// <summary>`GetDefinitionName()`: the item's `name`, which `CSchemaItemDefHandle` finds it by, or null.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>The name, or null.</returns>
    public string? ItemDefinitionName(int definitionIndex) => _itemNames.GetValueOrDefault(definitionIndex);

    /// <summary>`GetItemDefinitionByName( name )`'s index, or null.</summary>
    /// <param name="name">The item's `name`.</param>
    /// <returns>The definition index, or null.</returns>
    public int? ItemDefinitionIndex(string name) => _itemIndexByName.TryGetValue(name, out int index) ? index : null;

    /// <summary>`GetItemDefinition( index ) != NULL`.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>Whether the schema defines it.</returns>
    public bool HasDefinition(int definitionIndex) => _items.ContainsKey(definitionIndex);

    /// <summary>`master_maps_list` by def index: `localizedname` and `strangeprefixtoken`.</summary>
    private readonly Dictionary<int, (string? Name, string? Prefix)> _masterMaps = [];

    private ItemSetEntry SetNamed(string name)
    {
        if (!_itemSets.TryGetValue(name, out ItemSetEntry? set))
        {
            set = new ItemSetEntry();
            _itemSets[name] = set;
        }

        return set;
    }

    private static double PanelLayoutAtof(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0d;

    /// <summary>`GetItemSetDefinition()` (econ_item_schema.cpp:452, :5482-5497): the set listing the item, or its remap's.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>The set's name token, its items, its bonus attributes by index and value, and `is_hidden_set`.</returns>
    public ItemSetDefinition? ItemSet(int definitionIndex) =>
        System.Threading.LazyInitializer.EnsureInitialized(ref _setsByItem, BuildItemSets).GetValueOrDefault(definitionIndex);

    private Dictionary<int, ItemSetDefinition>? _setsByItem;

    /// <summary>
    /// Each set's `SetItemSetDefinition` on its items, in schema order, the first set holding an item keeping it; then
    /// every item whose `set_item_remap` names another takes that one's set (:5482-5497).
    /// </summary>
    private Dictionary<int, ItemSetDefinition> BuildItemSets()
    {
        Dictionary<int, ItemSetDefinition> byItem = [];

        foreach (ItemSetEntry set in _itemSets.Values)
        {
            List<int> items = [];

            foreach (string name in set.ItemNames)
            {
                if (_itemIndexByName.TryGetValue(name, out int index))
                {
                    items.Add(index);
                }
            }

            List<(int Attribute, float Value)> attributes = [];

            foreach ((string name, float value) in set.Attributes)
            {
                if (AttributeDefinitionIndex(name) is { } attribute)
                {
                    attributes.Add((attribute, value));
                }
            }

            ItemSetDefinition definition = new(set.LocalizedName, items, attributes, set.IsHidden);

            foreach (int item in items)
            {
                byItem.TryAdd(item, definition);
            }
        }

        foreach (int item in _items.Keys)
        {
            // `set_item_remap` (:3248-3259): an item by name, never itself.
            if (Inherited(item, entry => entry.Keys.GetValueOrDefault("set_item_remap")) is { } remapName
                && _itemIndexByName.TryGetValue(remapName, out int remap) && remap != item)
            {
                if (byItem.GetValueOrDefault(remap) is { } remapped)
                {
                    byItem[item] = remapped;
                }
                else
                {
                    byItem.Remove(item);
                }
            }
        }

        return byItem;
    }

    /// <summary>One `item_collections` entry as the file has it, items by definition name.</summary>
    private sealed class CollectionEntry
    {
        public string? LocalizedName { get; set; }

        public string? LocalizedDescription { get; set; }

        public bool IsReference { get; set; }

        /// <summary>The rarity blocks' items, each with its block's rarity name, in file order.</summary>
        public List<(string Item, string Rarity)> RarityItems { get; } = [];

        /// <summary>The items given as values of `items` itself, which take no rarity.</summary>
        public List<string> LooseItems { get; } = [];
    }

    /// <summary>`item_collections` by name, in file order.</summary>
    private readonly Dictionary<string, CollectionEntry> _collections = new(StringComparer.Ordinal);

    private CollectionEntry CollectionNamed(string name)
    {
        if (!_collections.TryGetValue(name, out CollectionEntry? collection))
        {
            collection = new CollectionEntry();
            _collections[name] = collection;
        }

        return collection;
    }

    /// <summary>Each item's collection and each collection-given rarity, built on first use.</summary>
    private sealed record CollectionIndex(Dictionary<int, ItemCollectionDefinition>ByItem, Dictionary<int, string> RarityByItem);

    private CollectionIndex? _collectionIndex;

    private CollectionIndex Collections() => System.Threading.LazyInitializer.EnsureInitialized(ref _collectionIndex, BuildCollections);

    /// <summary>`GetItemCollectionDefinition()` (econ_item_schema.h:1307): the collection holding the item, or null.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>The collection, or null.</returns>
    public ItemCollectionDefinition? Collection(int definitionIndex) => Collections().ByItem.GetValueOrDefault(definitionIndex);

    /// <summary>
    /// `BInitItemCollections` (econ_item_schema.cpp:5526-5545), `BInitFromKV` (:593-719), `BInitCollectionReferences`
    /// (:5548-5574) and `BPostSchemaInit`'s sort (:722-744).
    /// </summary>
    /// <remarks>
    /// <para>
    /// **A collection's rarity block sets its items' rarity** (`SetRarity`, :1526), over any `item_rarity` the item has:
    /// the setter only asserts the old value was "any" (econ_item_schema.h:1267), and a release build writes regardless.
    /// A later collection, or a later `SetItemCollectionDefinition`, likewise replaces an earlier one.
    /// </para>
    /// <para>
    /// *Interpolated, and invisible:* `SortCollectionByRarity` (:530-589) runs through `qsort`, which is not stable, and
    /// breaks a paint-kit tie by the localized "kit item" name; this sorts stably by rarity alone. No panel that shows
    /// the order is drawn: the item panels drop collection lines from their text (finding 109) and count them only.
    /// </para>
    /// </remarks>
    private CollectionIndex BuildCollections()
    {
        Dictionary<int, string> rarityByItem = [];

        foreach (CollectionEntry entry in _collections.Values)
        {
            foreach ((string name, string rarity) in entry.RarityItems)
            {
                if (_itemIndexByName.TryGetValue(name, out int index))
                {
                    rarityByItem[index] = rarity;
                }
            }
        }

        Dictionary<int, ItemCollectionDefinition>byItem = [];
        Dictionary<string, ItemCollectionDefinition> byName = new(StringComparer.Ordinal);

        foreach ((string collectionName, CollectionEntry entry) in _collections)
        {
            // The rarity blocks' items first, then the loose ones (:1476, :1531).
            List<int> items = [];

            foreach (string name in entry.RarityItems.ConvertAll(item => item.Item).Concat(entry.LooseItems))
            {
                if (_itemIndexByName.TryGetValue(name, out int index) && !items.Contains(index))
                {
                    items.Add(index);
                }
            }

            // Highest rarity first, stably; an item with no rarity last.
            int[] sorted = [.. items.OrderByDescending(
                item => RarityValue(rarityByItem.GetValueOrDefault(item) ?? Inherited(item, e => e.ItemRarity)) ?? int.MinValue)];

            ItemCollectionDefinition collection = new(collectionName, entry.LocalizedName, entry.LocalizedDescription, sorted);
            byName[collectionName] = collection;

            if (!entry.IsReference)
            {
                foreach (int item in items)
                {
                    byItem[item] = collection;
                }
            }
        }

        foreach (int item in _items.Keys)
        {
            if (CollectionReference(item) is { } reference && byName.TryGetValue(reference, out ItemCollectionDefinition? referenced))
            {
                byItem[item] = referenced;
            }
        }

        return new CollectionIndex(byItem, rarityByItem);
    }

    /// <summary>A rarity's `value` by its name, or null for "any" or an unknown name.</summary>
    private int? RarityValue(string? rarityName) =>
        rarityName is not null && !string.Equals(rarityName, "any", StringComparison.OrdinalIgnoreCase)
            && _rarities.TryGetValue(rarityName, out (int? Value, string Color) named)
            ? named.Value
            : null;

    /// <summary>`GetRarity()`'s rarity name for a definition: its collection's block, else its `item_rarity`.</summary>
    private string? RarityName(int definitionIndex) =>
        Collections().RarityByItem.GetValueOrDefault(definitionIndex) ?? Inherited(definitionIndex, entry => entry.ItemRarity);

    private Dictionary<uint, int>? _paintKitTools;

    /// <summary>
    /// `GetPaintKitItemDefinition( kit )` (econ_item_schema.cpp:7154-7163): the tool whose econ tool type is "paintkit"
    /// and whose own `paintkit_proto_def_index` is the kit (:5324-5339), the first by definition index keeping it.
    /// </summary>
    /// <param name="paintKit">The kit's `paintkit_proto_def_index`.</param>
    /// <returns>The War Paint tool's definition index, or null.</returns>
    public int? PaintKitTool(uint paintKit) =>
        System.Threading.LazyInitializer.EnsureInitialized(ref _paintKitTools, BuildPaintKitTools).TryGetValue(paintKit, out int tool) ? tool : null;

    private Dictionary<uint, int> BuildPaintKitTools()
    {
        Dictionary<uint, int> tools = [];

        if (AttributeDefinitionIndex(PaintKitAttribute) is not { } kitAttribute)
        {
            return tools;
        }

        int[] definitions = [.. _items.Keys];
        Array.Sort(definitions);

        foreach (int definition in definitions)
        {
            if (!IsTool(definition) || !string.Equals(ToolType(definition), "paintkit", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (EconAttributeValue attribute in DefinitionAttributesFor(definition))
            {
                if (attribute.DefinitionIndex == kitAttribute)
                {
                    tools.TryAdd(unchecked((uint)attribute.RawBits), definition);
                    break;
                }
            }
        }

        return tools;
    }

    /// <summary>`GetMasterMapDefByIndex( index )` (tf_item_schema.cpp:2491): the map's `localizedname` and `strangeprefixtoken`.</summary>
    /// <param name="index">The map's def index.</param>
    /// <returns>The two tokens, or null for no such map.</returns>
    public (string? Name, string? StrangePrefix)? MasterMap(int index) =>
        _masterMaps.TryGetValue(index, out (string? Name, string? Prefix) map) ? map : null;

    /// <summary>`GetDefinitionString( "collection_reference" )` (econ_item_schema.cpp:3174), or null.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>The collection's name, or null.</returns>
    public string? CollectionReference(int definitionIndex) => Inherited(definitionIndex, entry => entry.Keys.GetValueOrDefault("collection_reference"));

    /// <summary>`GetDefinitionString( "event_desc_footer", NULL )` (econ_item_description.cpp:2650), or null.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>A localization token, or null.</returns>
    public string? EventDescriptionFooter(int definitionIndex) => Inherited(definitionIndex, entry => entry.Keys.GetValueOrDefault("event_desc_footer"));

    /// <summary>`string_lookups` (`BInitStringTables`): each table's tokens by index.</summary>
    private readonly Dictionary<string, Dictionary<int, string>> _stringTables = new(StringComparer.Ordinal);

    /// <summary>Each rarity's `loc_key` by its name (econ_item_schema.cpp:333).</summary>
    private readonly Dictionary<string, string> _rarityLocKeys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>`rank_name` by `item_levels` block and level (econ_item_schema.cpp:7100).</summary>
    private readonly Dictionary<(string Block, uint Level), string> _rankNames = [];

    /// <summary>`kill_eater_score_types`' `type_name` by score type (econ_item_schema.cpp:6223).</summary>
    private readonly Dictionary<uint, string> _killEaterTypeNames = [];

    /// <summary>`g_AttributeDescriptionFormats` (econ_item_schema.cpp:109), in `ATTDESCFORM_` order.</summary>
    private static readonly string[] AttributeDescriptionFormats =
    [
        "value_is_percentage", "value_is_inverted_percentage", "value_is_additive", "value_is_additive_percentage", "value_is_or",
        "value_is_date", "value_is_account_id", "value_is_particle_index", "value_is_killstreakeffect_index",
        "value_is_killstreak_idleeffect_index", "value_is_item_def", "value_is_from_lookup_table",
    ];

    /// <summary>`g_EffectTypes` (econ_item_schema.cpp:125), in `attrib_effect_types_t` order.</summary>
    private static readonly string[] EffectTypes = ["unusual", "strange", "neutral", "positive", "negative"];

    /// <summary>`ATTRIB_COL_RARITY_COMMON` (econ_item_constants.h:564).</summary>
    public const int AttribColorRarityCommon = 12;

    /// <summary>`GetItemSchema()->GetAttributeDefinition( index )`'s display fields, or null for an index the schema lacks.</summary>
    /// <param name="index">The attribute's definition index.</param>
    /// <returns>The definition.</returns>
    public ItemAttributeDefinition? AttributeDefinition(int index)
    {
        if (!_attributeKeys.TryGetValue(index, out Dictionary<string, string>? keys))
        {
            return null;
        }

        return new ItemAttributeDefinition(index, keys.GetValueOrDefault("name", "(unnamed)"))
        {
            DescriptionString = keys.GetValueOrDefault("description_string"),
            DescriptionFormat = StringFieldToInt(keys.GetValueOrDefault("description_format"), AttributeDescriptionFormats),
            EffectType = StringFieldToInt(keys.GetValueOrDefault("effect_type"), EffectTypes),
            Hidden = GetInt(keys.GetValueOrDefault("hidden") ?? "0", 0) != 0,
            StoredAsInteger = GetInt(keys.GetValueOrDefault("stored_as_integer") ?? "0", 0) != 0,
            UserGenerationType = GetInt(keys.GetValueOrDefault("is_user_generated") ?? "0", 0),
        };
    }

    /// <summary>`StringFieldToInt` (econ_item.cpp:31): the index compared without case, -1 for missing, empty or unknown.</summary>
    private static int StringFieldToInt(string? value, string[] table) =>
        string.IsNullOrEmpty(value) ? -1 : Array.FindIndex(table, each => string.Equals(each, value, StringComparison.OrdinalIgnoreCase));

    /// <summary>`GetItemTypeName()`: `item_type_name`, "" when unstated (econ_item_schema.cpp:3133).</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>A localization token, or "".</returns>
    public string ItemTypeName(int definitionIndex) => Inherited(definitionIndex, entry => entry.Keys.GetValueOrDefault("item_type_name")) ?? string.Empty;

    /// <summary>`GetItemDesc()`: `item_description`, or null (:3134).</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>A localization token, or null.</returns>
    public string? ItemDescription(int definitionIndex) => Inherited(definitionIndex, entry => entry.Keys.GetValueOrDefault("item_description"));

    /// <summary>`GetInventoryImage()`: `image_inventory`, or null (:3137).</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>A material path under `materials/`, or null.</returns>
    public string? InventoryImage(int definitionIndex) => Inherited(definitionIndex, entry => entry.Keys.GetValueOrDefault("image_inventory"));

    private const string ToolTypeKey = "tool/type";

    /// <summary>`GetEconTool()->GetTypeName()`: the `tool` block's `type`, or null without a tool.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>The type, such as "paint_can".</returns>
    public string? ToolType(int definitionIndex) => Inherited(definitionIndex, entry => entry.Keys.GetValueOrDefault(ToolTypeKey));

    /// <summary>`IsHidden()`: `GetInt( "hidden", 0 ) != 0` (econ_item_schema.cpp:3177), prefabs included.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>Whether the item is hidden.</returns>
    public bool IsItemHidden(int definitionIndex) =>
        InheritedInt(definitionIndex, "hidden") != 0;

    /// <summary>`IsTool()` (econ_item_schema.cpp:3172): `is_tool`, or the item class "tool".</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>Whether it is a tool.</returns>
    public bool IsTool(int definitionIndex) =>
        InheritedInt(definitionIndex, "is_tool") != 0
        || string.Equals(ItemClass(definitionIndex), "tool", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// `GetPaintItemDefinitionForPaintedItem` (econ_item_description.cpp:2333): the first tool, in schema order
    /// (`m_mapToolsItems`, filled as the items load, :5323-5326), whose tool is a "paint_can" and whose own
    /// "set item tint RGB" has these bits.
    /// </summary>
    /// <param name="tintBits">The painted item's "set item tint RGB" bits.</param>
    /// <returns>The paint can's definition index, or null.</returns>
    public int? PaintCanFor(int tintBits)
    {
        if (AttributeDefinitionIndex("set item tint RGB") is not { } tint)
        {
            return null;
        }

        foreach (int definition in _items.Keys)
        {
            if (!IsTool(definition) || !string.Equals(ToolType(definition), "paint_can", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (EconAttributeValue attribute in DefinitionAttributesFor(definition))
            {
                if (attribute.DefinitionIndex == tint)
                {
                    if (attribute.RawBits == tintBits)
                    {
                        return definition;
                    }

                    break;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// `expiration_date` (econ_item_schema.cpp:3103): "YYYY-MM-DD hh:mm:ss", or "!" and a custom name, as written; null or
    /// empty without one.
    /// </summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>The string, or null.</returns>
    public string? ExpirationDate(int definitionIndex) => Inherited(definitionIndex, entry => entry.Keys.GetValueOrDefault("expiration_date"));

    /// <summary>`GetHolidayRestriction()`: `holiday_restriction`, or null (:3155).</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>The holiday's name, or null.</returns>
    public string? HolidayRestriction(int definitionIndex) => Inherited(definitionIndex, entry => entry.Keys.GetValueOrDefault("holiday_restriction"));

    /// <summary>`GetInventoryImagePosition` and `GetInventoryImageSize` (:3150-3153): `atoi` of each key, defaults 0, 0, 128, 82.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>The image's offset and size.</returns>
    public (int X, int Y, int Wide, int Tall) InventoryImageData(int definitionIndex)
    {
        int Key(string key, int fallback) =>
            Inherited(definitionIndex, entry => entry.Keys.GetValueOrDefault(key)) is { } value ? GetInt(value, 0) : fallback;

        return (Key("image_inventory_pos_x", 0), Key("image_inventory_pos_y", 0), Key("image_inventory_size_w", 128), Key("image_inventory_size_h", 82));
    }

    /// <summary>`GetInventoryOverlayImage( i )` for each `i`: `image_inventory_overlay`, then `_overlay2` (:3139-3148).</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>The overlays present, in order.</returns>
    public IReadOnlyList<string> InventoryOverlayImages(int definitionIndex)
    {
        List<string> overlays = [];

        foreach (string key in (string[])["image_inventory_overlay", "image_inventory_overlay2"])
        {
            if (Inherited(definitionIndex, entry => entry.Keys.GetValueOrDefault(key)) is { Length: > 0 } overlay)
            {
                overlays.Add(overlay);
            }
        }

        return overlays;
    }

    /// <summary>`GetStyleInfo( style )->GetName()`, or null for no such style (econ_item_schema.cpp:2863).</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <param name="style">The style index.</param>
    /// <returns>A localization token, or null.</returns>
    public string? StyleName(int definitionIndex, int style) =>
        StylesOf(definitionIndex) is { } styles && style >= 0 && style < styles.Count ? styles[style].Name : null;

    /// <summary>`GetStyleInventoryImage( style )`, or null (:2866).</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <param name="style">The style index.</param>
    /// <returns>A material path, or null.</returns>
    public string? StyleInventoryImage(int definitionIndex, int style) =>
        StylesOf(definitionIndex) is { } styles && style >= 0 && style < styles.Count ? styles[style].InventoryImage : null;

    /// <summary>
    /// `GetRarityDefinition( GetRarity() )`'s `loc_key` and `attrib_colors_t` (econ_item_description.cpp:1524-1530), or null
    /// where the item's rarity is `k_unItemRarity_Any`, which has no definition.
    /// </summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>The rarity's token and its color index, `GetAttribColorIndexForName` of its `color`.</returns>
    public (string LocKey, int AttribColor)? Rarity(int definitionIndex)
    {
        if (RarityName(definitionIndex) is not { } rarityName
            || string.Equals(rarityName, "any", StringComparison.OrdinalIgnoreCase)
            || !_rarities.TryGetValue(rarityName, out (int? Value, string Color) named))
        {
            return null;
        }

        // `GetRarityDefinition( value )`: the first rarity carrying the value answers.
        foreach ((string name, (int? value, string color)) in _rarities)
        {
            if (value == named.Value)
            {
                return (_rarityLocKeys.GetValueOrDefault(name, string.Empty), Math.Max(0, Array.IndexOf(AttribColorDefs, color)));
            }
        }

        return null;
    }

    /// <summary>`GetColorNameForAttribColor` (econ_item_constants.cpp:304): the `colors` section's name, else "ItemAttribNeutral".</summary>
    /// <param name="attribColor">An `attrib_colors_t`.</param>
    /// <returns>A scheme color name.</returns>
    public string AttribColorName(int attribColor) =>
        attribColor >= 0 && attribColor < AttribColorDefs.Length && _colorNames.TryGetValue(AttribColorDefs[attribColor], out string? name)
            ? name
            : "ItemAttribNeutral";

    /// <summary>`GetItemLevelForScore( block, score )->GetNameLocalizationKey()`: the level's `rank_name`, else block and level.</summary>
    /// <param name="block">The `item_levels` block.</param>
    /// <param name="score">The score.</param>
    /// <returns>A localization token, or null for an unknown or empty block.</returns>
    public string? ItemLevelRankName(string block, uint score) =>
        ItemLevelForScore(block, score) is { } level
            ? _rankNames.GetValueOrDefault((block, level), block + level.ToString(CultureInfo.InvariantCulture))
            : null;

    /// <summary>`GetKillEaterScoreTypeLocString( type )` (econ_item_schema.cpp:6358), or null for an unknown type.</summary>
    /// <param name="scoreType">The kill eater score type.</param>
    /// <returns>A localization token, or null.</returns>
    public string? KillEaterTypeName(uint scoreType) => _killEaterTypeNames.GetValueOrDefault(scoreType);

    /// <summary>`FindStringTableEntry( table, index )`: a `string_lookups` token, or null.</summary>
    /// <param name="table">The table, named for its attribute.</param>
    /// <param name="index">The entry.</param>
    /// <returns>A localization token, or null.</returns>
    public string? StringTableEntry(string table, int index) =>
        _stringTables.TryGetValue(table, out Dictionary<int, string>? entries) ? entries.GetValueOrDefault(index) : null;

    /// <summary><see cref="DefinitionAttributesFor"/>'s answers, one per definition asked — `m_vecStaticAttributes`.</summary>
    private readonly ConcurrentDictionary<int, EconAttributeValue[]> _definitionAttributes = new();

    /// <summary>The <c>rarities</c> section by name: each one's <c>value</c> and <c>color</c> (econ_item_schema.cpp:329).</summary>
    private readonly Dictionary<string, (int? Value, string Color)> _rarities = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The <c>colors</c> section: each definition's <c>color_name</c> (econ_item_schema.cpp:357).</summary>
    private readonly Dictionary<string, string> _colorNames = new(StringComparer.Ordinal);

    private const string ParticlesSection = "attribute_controlled_attached_particles";

    /// <summary>`m_mapAttributeControlledParticleSystems` by id, and in file order for the by-name search.</summary>
    private readonly Dictionary<int, AttributeParticleSystem> _particleSystems = [];

    private readonly List<AttributeParticleSystem> _particleOrder = [];

    /// <summary>`GetAttributeControlledParticleSystem( id )` (econ_item_schema.cpp:6850), or null.</summary>
    /// <param name="id">The system's index — an unusual effect's value, or a killstreak eye's.</param>
    /// <returns>The system, or null.</returns>
    public AttributeParticleSystem? AttributeControlledParticleSystem(int id) => _particleSystems.GetValueOrDefault(id);

    /// <summary>`FindAttributeControlledParticleSystem( name )` (econ_item_schema.cpp:6858): the first by name, case ignored.</summary>
    /// <param name="systemName">The particle system's name.</param>
    /// <returns>The system, or null.</returns>
    public AttributeParticleSystem? FindAttributeControlledParticleSystem(string systemName) =>
        _particleOrder.Find(each => string.Equals(each.SystemName, systemName, StringComparison.OrdinalIgnoreCase));

    /// <summary>`GetParticleSuffix()`: `particle_suffix` (econ_item_schema.cpp:3268), or null.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>The suffix, or null.</returns>
    public string? ParticleSuffix(int definitionIndex) => Inherited(definitionIndex, entry => entry.Keys.GetValueOrDefault("particle_suffix"));

    /// <summary>`m_vecItemLevelingData`: each `item_levels` block's levels, in file order (econ_item_schema.cpp:6178).</summary>
    private readonly Dictionary<string, List<(uint Level, uint Score)>> _itemLevels = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>`m_mapKillEaterScoreTypes`' `level_data` by score type (econ_item_schema.cpp:6209).</summary>
    private readonly Dictionary<uint, string> _killEaterLevelData = [];

    /// <summary>`GetKillEaterScoreTypeLevelingDataName( type )` (econ_item_schema.cpp:6370), or null for an unknown type.</summary>
    /// <param name="scoreType">The kill eater score type.</param>
    /// <returns>The `item_levels` block name, or null.</returns>
    public string? KillEaterLevelingDataName(uint scoreType) => _killEaterLevelData.GetValueOrDefault(scoreType);

    /// <summary>
    /// `GetItemLevelForScore( block, score )` (econ_item_schema.cpp:6325): the first level whose `score` exceeds
    /// <paramref name="score"/>, else the last; null for an unknown or empty block.
    /// </summary>
    /// <param name="block">The `item_levels` block.</param>
    /// <param name="score">The score.</param>
    /// <returns>The level's number, or null.</returns>
    public uint? ItemLevelForScore(string block, uint score)
    {
        if (!_itemLevels.TryGetValue(block, out List<(uint Level, uint Score)>? levels) || levels.Count == 0)
        {
            return null;
        }

        foreach ((uint level, uint required) in levels)
        {
            if (score < required)
            {
                return level;
            }
        }

        return levels[^1].Level;
    }

    private ItemSchema()
    {
    }

    /// <summary>Reads <c>items_game.txt</c>.</summary>
    /// <param name="schema">The file's bytes.</param>
    /// <returns>The schema, ready to answer.</returns>
    /// <remarks>
    /// **One pass, keeping only what is asked for.** The blocks that matter are <c>items</c> and
    /// <c>prefabs</c>, both at depth 1; an entry sits at depth 2 and its keys at depth 3, with
    /// <c>model_player_per_class</c> opening a block whose entries are at depth 4.
    /// </remarks>
    public static ItemSchema Read(ReadOnlySpan<byte> schema)
    {
        ItemSchema read = new();

        // Where the walk currently is. Names rather than a stack, because only three levels are
        // interesting and a stack would need unwinding on every close brace the reader does not
        // report.
        string section = string.Empty;
        Entry? entry = null;
        bool inPerClass = false;

        // **Where in a `visuals` block the walk is.** `attached_models` sits three levels below an
        // entry — `visuals` / `attached_models` / an index / `model` — and the per-team variants
        // are sibling blocks named `visuals_red` and `visuals_blu`, which is how
        // `GetNumAttachedModels( iTeam )` gets a different answer per side.
        string visualsTeam = string.Empty;
        bool inVisuals = false;
        bool inAttached = false;
        bool inBodygroups = false;
        bool inCustomParticle = false;

        // `attached_particlesystems` and either `custom_particlesystem` block, each child one `attachedparticlesystem_t` (B69).
        bool inAttachedParticles = false;
        bool inAnyCustomParticle = false;
        bool inTool = false;
        bool inTags = false;
        bool attachedIsFestive = false;

        // The model panel's visuals blocks: `styles`, `player_poseparam`, and `animation_*`.
        bool inStyles = false;
        bool inPoseParam = false;
        bool inAnimation = false;
        bool inReplacement = false;
        ItemStyle? style = null;
        string styleBlock = string.Empty;
        PerClassBlock? perClassBlock = null;
        AttributeParticleSystem? particle = null;
        string attachedModel = string.Empty;
        string attachedKey = string.Empty;
        int attachedFlags = AttachedModel.MaskAll;

        // The top-level `attributes` section's walk: which definition index is open.
        int attributeDefinition = -1;

        // An item's two definition-attribute forms: the flat `static_attrs` pairs, and the named
        // blocks whose value arrives a level deeper.
        bool inStaticAttrs = false;
        bool inItemAttributes = false;
        string pendingAttributeName = string.Empty;

        // `used_by_classes` inside an entry, and the name of the `rarities`/`colors` child being read.
        bool inUsedByClasses = false;
        string sectionChild = string.Empty;

        // An item set's open block (`items` or `attributes`) and the bonus attribute whose `value` is due.
        string setBlock = string.Empty;
        string pendingSetAttribute = string.Empty;

        // An item collection's open block (`items`) and, inside it, the rarity block being read.
        string collectionBlock = string.Empty;
        string collectionRarity = string.Empty;

        KeyValuesReader.Read(schema, (key, value, depth) =>
        {
            switch (depth)
            {
                case 1:
                    section = key;
                    entry = null;
                    inStyles = inPoseParam = inAnimation = inReplacement = false;
                    inPerClass = false;
                    inVisuals = false;
                    inAttached = false;
                    inAttachedParticles = inAnyCustomParticle = false;
                    inStaticAttrs = false;
                    inItemAttributes = false;
                    attributeDefinition = -1;
                    break;

                case 2:
                    inStyles = inPoseParam = inAnimation = inReplacement = false;
                    inPerClass = false;
                    inVisuals = false;
                    inAttached = false;
                    inAttachedParticles = inAnyCustomParticle = false;
                    inStaticAttrs = false;
                    inItemAttributes = false;
                    inUsedByClasses = false;
                    sectionChild = key;
                    entry = read.Begin(section, key);

                    // `BInitItemLevels` / `BInitKillEaterScoreTypes` (econ_item_schema.cpp:6185, :6216): true subkeys only.
                    if (value is null && string.Equals(section, "item_levels", StringComparison.OrdinalIgnoreCase))
                    {
                        read._itemLevels[key] = [];
                    }
                    else if (value is null && string.Equals(section, "kill_eater_score_types", StringComparison.OrdinalIgnoreCase))
                    {
                        // `GetString( "level_data", "KillEaterRank" )` (:6226); `atoi` of the name (:6218).
                        read._killEaterLevelData[unchecked((uint)GetInt(key, 0))] = "KillEaterRank";
                    }

                    // The top-level `attributes` section: each child is one definition, keyed by
                    // its index as text — the same spelling `instancebaseline` entries use.
                    // Stryker disable once : removing TryParse leaves 'index' undeclared in ternary, CS0165
                    attributeDefinition =
                        string.Equals(section, "attributes", StringComparison.OrdinalIgnoreCase)
                        && value is null
                        && int.TryParse(
                            key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                            ? index
                            : -1;

                    break;

                // The definition's own keys: `name` is the bridge the wire needs, and
                // `stored_as_integer` decides what the 32-bit union HOLDS for this attribute.
                case 3 when attributeDefinition >= 0 && value is not null:
                    if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
                    {
                        read._attributeIndexByName[value] = attributeDefinition;
                    }
                    else if (string.Equals(key, "stored_as_integer", StringComparison.OrdinalIgnoreCase)
                        && value != "0")
                    {
                        read._attributeStoredAsInteger.Add(attributeDefinition);
                    }
                    else if (string.Equals(key, "attribute_class", StringComparison.OrdinalIgnoreCase))
                    {
                        read._attributeClass[attributeDefinition] = value;
                    }
                    else if (string.Equals(key, "description_format", StringComparison.OrdinalIgnoreCase))
                    {
                        read._attributeFormat[attributeDefinition] = value;
                    }

                    // Every scalar, for `AttributeDefinition`: the KeyValues copy `BInitFromKV` reads from (:2177).
                    if (!read._attributeKeys.TryGetValue(attributeDefinition, out Dictionary<string, string>? attributeKeys))
                    {
                        attributeKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        read._attributeKeys[attributeDefinition] = attributeKeys;
                    }

                    attributeKeys[key] = value;
                    break;

                // `BInitStringTables`: one table per child, one token per numbered entry.
                case 3 when entry is null && value is not null
                    && string.Equals(section, "string_lookups", StringComparison.OrdinalIgnoreCase):
                    if (!read._stringTables.TryGetValue(sectionChild, out Dictionary<int, string>? table))
                    {
                        table = [];
                        read._stringTables[sectionChild] = table;
                    }

                    table.TryAdd(GetInt(key, 0), value);
                    break;

                // `CEconItemRarityDefinition::BInitFromKV` (econ_item_schema.cpp:331, :336) and
                // `CEconColorDefinition::BInitFromKV` (:360).
                case 3 when entry is null && value is not null
                    && string.Equals(section, "rarities", StringComparison.OrdinalIgnoreCase):
                    (int? Value, string Color) rarity = read._rarities.GetValueOrDefault(sectionChild, (null, string.Empty));

                    if (string.Equals(key, "value", StringComparison.OrdinalIgnoreCase))
                    {
                        rarity.Value = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rarityValue) ? rarityValue : -1;
                    }
                    else if (string.Equals(key, "color", StringComparison.OrdinalIgnoreCase))
                    {
                        rarity.Color = value;
                    }
                    else if (string.Equals(key, "loc_key", StringComparison.OrdinalIgnoreCase))
                    {
                        read._rarityLocKeys[sectionChild] = value;
                    }

                    read._rarities[sectionChild] = rarity;
                    break;

                case 3 when entry is null && value is not null
                    && string.Equals(section, "colors", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(key, "color_name", StringComparison.OrdinalIgnoreCase):
                    read._colorNames[sectionChild] = value;
                    break;

                // `BInitAttributeControlledParticleSystems` (econ_item_schema.cpp:6126-6149): one system per positive index.
                case 3 when entry is null && value is null
                    && string.Equals(section, ParticlesSection, StringComparison.OrdinalIgnoreCase):
                    particle = GetInt(key, 0) is var id and > 0 ? new AttributeParticleSystem(id) : null;

                    if (particle is not null)
                    {
                        read._particleSystems[particle.Id] = particle;
                        read._particleOrder.Add(particle);
                    }

                    break;

                case 4 when entry is null && value is not null && particle is not null
                    && string.Equals(section, ParticlesSection, StringComparison.OrdinalIgnoreCase):
                    particle.Apply(key, value);
                    break;

                // `CItemLevelingDefinition::BInitFromKV` (econ_item_schema.cpp:7096): the level is `atoi` of the name.
                case 3 when entry is null && value is null
                    && read._itemLevels.TryGetValue(sectionChild, out List<(uint Level, uint Score)>? levels)
                    && string.Equals(section, "item_levels", StringComparison.OrdinalIgnoreCase):
                    levels.Add((unchecked((uint)GetInt(key, 0)), 0u));
                    break;

                case 4 when entry is null && value is not null
                    && string.Equals(key, "score", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(section, "item_levels", StringComparison.OrdinalIgnoreCase)
                    && read._itemLevels.TryGetValue(sectionChild, out List<(uint Level, uint Score)>? scored) && scored.Count > 0:
                    scored[^1] = (scored[^1].Level, unchecked((uint)GetInt(value, 0)));
                    break;

                // `GetString( "rank_name", "%s%i" )` (econ_item_schema.cpp:7100), the default made at the lookup.
                case 4 when entry is null && value is not null
                    && string.Equals(key, "rank_name", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(section, "item_levels", StringComparison.OrdinalIgnoreCase)
                    && read._itemLevels.TryGetValue(sectionChild, out List<(uint Level, uint Score)>? named) && named.Count > 0:
                    read._rankNames[(sectionChild, named[^1].Level)] = value;
                    break;

                case 3 when entry is null && value is not null
                    && string.Equals(key, "level_data", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(section, "kill_eater_score_types", StringComparison.OrdinalIgnoreCase):
                    read._killEaterLevelData[unchecked((uint)GetInt(sectionChild, 0))] = value;
                    break;

                // `ScoreType.m_pszTypeString = GetString( "type_name" )` (:6223).
                case 3 when entry is null && value is not null
                    && string.Equals(key, "type_name", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(section, "kill_eater_score_types", StringComparison.OrdinalIgnoreCase):
                    read._killEaterTypeNames[unchecked((uint)GetInt(sectionChild, 0))] = value;
                    break;

                // `GetItemDefinitionByName`'s map: an item's `name`, the key item sets list it by (econ_item_schema.cpp:436).
                case 3 when entry is not null && value is not null
                    && string.Equals(key, "name", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(section, "items", StringComparison.OrdinalIgnoreCase):
                    read._itemIndexByName.TryAdd(value, GetInt(sectionChild, 0));
                    read._itemNames[GetInt(sectionChild, 0)] = value;
                    break;

                // `CEconItemSetDefinition::BInitFromKV` (econ_item_schema.cpp:407): `name`, `is_hidden_set`, and the blocks.
                case 3 when entry is null && string.Equals(section, "item_sets", StringComparison.OrdinalIgnoreCase):
                    setBlock = value is null ? key : string.Empty;

                    if (value is not null)
                    {
                        ItemSetEntry set = read.SetNamed(sectionChild);

                        if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
                        {
                            set.LocalizedName = value;
                        }
                        else if (string.Equals(key, "is_hidden_set", StringComparison.OrdinalIgnoreCase))
                        {
                            set.IsHidden = GetInt(value, 0) != 0;
                        }
                    }

                    break;

                case 4 when entry is null && string.Equals(section, "item_sets", StringComparison.OrdinalIgnoreCase):
                    if (value is not null && string.Equals(setBlock, "items", StringComparison.OrdinalIgnoreCase))
                    {
                        read.SetNamed(sectionChild).ItemNames.Add(key);
                    }
                    else if (value is null && string.Equals(setBlock, "attributes", StringComparison.OrdinalIgnoreCase))
                    {
                        pendingSetAttribute = key;
                    }

                    break;

                case 5 when entry is null && value is not null && pendingSetAttribute.Length > 0
                    && string.Equals(key, "value", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(section, "item_sets", StringComparison.OrdinalIgnoreCase):
                    read.SetNamed(sectionChild).Attributes.Add((pendingSetAttribute, (float)PanelLayoutAtof(value)));
                    pendingSetAttribute = string.Empty;
                    break;

                // `CEconItemCollectionDefinition::BInitFromKV` (econ_item_schema.cpp:593-719): `name`, `description`,
                // `is_reference_collection`, and the `items` block.
                case 3 when entry is null && string.Equals(section, "item_collections", StringComparison.OrdinalIgnoreCase):
                    collectionBlock = value is null ? key : string.Empty;
                    collectionRarity = string.Empty;
                    CollectionEntry collection = read.CollectionNamed(sectionChild);

                    if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
                    {
                        collection.LocalizedName = value;
                    }
                    else if (string.Equals(key, "description", StringComparison.OrdinalIgnoreCase))
                    {
                        collection.LocalizedDescription = value;
                    }
                    else if (string.Equals(key, "is_reference_collection", StringComparison.OrdinalIgnoreCase) && value is not null)
                    {
                        collection.IsReference = GetInt(value, 0) != 0;
                    }

                    break;

                // In `items`: a true subkey is a rarity block, a value a loose item (:1476, :1531).
                case 4 when entry is null && string.Equals(collectionBlock, "items", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(section, "item_collections", StringComparison.OrdinalIgnoreCase):
                    if (value is null)
                    {
                        collectionRarity = key;
                    }
                    else
                    {
                        read.CollectionNamed(sectionChild).LooseItems.Add(key);
                    }

                    break;

                case 5 when entry is null && value is not null && collectionRarity.Length > 0
                    && string.Equals(section, "item_collections", StringComparison.OrdinalIgnoreCase):
                    read.CollectionNamed(sectionChild).RarityItems.Add((key, collectionRarity));
                    break;

                // `CTFItemSchema::BInitMaps` (tf_item_schema.cpp:1984): each map's def index, `localizedname`, `strangeprefixtoken`.
                case 3 when entry is null && value is not null
                    && string.Equals(section, "master_maps_list", StringComparison.OrdinalIgnoreCase):
                    (string? Name, string? Prefix) map = read._masterMaps.GetValueOrDefault(GetInt(sectionChild, 0));

                    if (string.Equals(key, "localizedname", StringComparison.OrdinalIgnoreCase))
                    {
                        map.Name = value;
                    }
                    else if (string.Equals(key, "strangeprefixtoken", StringComparison.OrdinalIgnoreCase))
                    {
                        map.Prefix = value;
                    }

                    read._masterMaps[GetInt(sectionChild, 0)] = map;
                    break;

                case 3 when entry is not null:
                    inUsedByClasses = value is null
                        && string.Equals(key, "used_by_classes", StringComparison.OrdinalIgnoreCase);

                    inPerClass =
                        value is null &&
                        string.Equals(key, "model_player_per_class", StringComparison.OrdinalIgnoreCase);

                    // `visuals`, `visuals_red`, `visuals_blu`. The suffix IS the team.
                    inVisuals = value is null
                        && key.StartsWith("visuals", StringComparison.OrdinalIgnoreCase);

                    visualsTeam = inVisuals && key.Length > "visuals".Length
                        ? key["visuals_".Length..]
                        : string.Empty;

                    if (inVisuals)
                    {
                        entry.VisualsSections.Add(visualsTeam);
                    }

                    inAttached = false;
                    inCustomParticle = false;
                    inAttachedParticles = inAnyCustomParticle = false;
                    inStyles = inPoseParam = inAnimation = inReplacement = false;

                    if (value is null && string.Equals(key, "taunt", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.HasTauntData = true;
                    }

                    // The two definition-attribute forms open here; every other depth-3 key closes
                    // both, so a stray pair after the block cannot be swallowed into it.
                    inStaticAttrs = value is null
                        && string.Equals(key, "static_attrs", StringComparison.OrdinalIgnoreCase);

                    inItemAttributes = value is null
                        && string.Equals(key, "attributes", StringComparison.OrdinalIgnoreCase);

                    // `tool { type }`: the econ tool `GetEconTool()->GetTypeName()` names (econ_item_schema.cpp:3187-3190).
                    inTool = value is null && string.Equals(key, "tool", StringComparison.OrdinalIgnoreCase);

                    // `tags { <name> "1" }`: what `HasEconTag` asks (B767).
                    inTags = value is null && string.Equals(key, "tags", StringComparison.OrdinalIgnoreCase);

                    Apply(entry, key, value);
                    break;

                // **A scalar inside `visuals`, addressing a part by number rather than by name**
                // (B353). Read only for the WORLD model: `vm_bodygroup_override` sets a part on the
                // wearer's own view model (`econ_entity.cpp:2091`), which a demo viewer drawing
                // another player never has — and the Purity Fist declares both pairs with the same
                // numbers, so a reader keyed to the wrong prefix passes every shipped case.
                // Stryker disable all : removing TryParse from switch 'when' leaves 'part' undeclared, CS0165
                case 4 when entry is not null && inVisuals && value is not null
                    && key.StartsWith("wm_bodygroup", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(
                        value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int part):

                    // Per visuals block: `m_iWorldModelBodyGroupOverride` lives on `perteamvisuals_t` (econ_item_schema.cpp:2674-2680).
                    entry.VisualKeys[visualsTeam + "/" + key] = part.ToString(CultureInfo.InvariantCulture);

                    break;
                // Stryker restore all

                // **`sound_special1` and its siblings**, one weapon sound each, compared by prefix exactly as
                // the engine does ("intentionally comparing prefixes", `econ_item_schema.cpp:2648`). Kept per
                // block, because which block answers depends on the listener's team.
                case 4 when entry is not null && inVisuals && value is not null
                    && key.StartsWith("sound_", StringComparison.OrdinalIgnoreCase):
                    entry.WeaponSounds[visualsTeam + "/" + key["sound_".Length..]] = value;
                    break;

                // `muzzle_flash`, `tracer_effect` (`econ_item_schema.cpp:2623`) and `material_override` (:2644), per
                // block for the same reason.
                case 4 when entry is not null && inVisuals && value is not null
                    && (key.Equals("muzzle_flash", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("tracer_effect", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals(MaterialOverrideKey, StringComparison.OrdinalIgnoreCase)):
                    entry.WeaponSounds[visualsTeam + "/" + key] = value;
                    break;

                // `skin` and `use_per_class_bodygroups` (econ_item_schema.cpp:2615-2622), per block; `use_visualsblock_as_base`
                // (:2484-2497), which starts the block as a copy of the one it names — read for the particles (B69).
                case 4 when entry is not null && inVisuals && value is not null
                    && (key.Equals("skin", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("use_per_class_bodygroups", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals(VisualsBaseKey, StringComparison.OrdinalIgnoreCase)):
                    entry.VisualKeys[visualsTeam + "/" + key] = value;
                    break;

                case 4 when entry is not null && inUsedByClasses && value is not null:
                    entry.UsedByClasses[key] = value;
                    break;

                case 4 when entry is not null && inTags:
                    entry.Tags.Add(key);
                    break;

                case 4 when entry is not null && inTool && value is not null && key.Equals("type", StringComparison.OrdinalIgnoreCase):
                    entry.Keys[ToolTypeKey] = value;
                    break;

                // `static_attrs` is flat: the pair IS the attribute.
                case 4 when entry is not null && inStaticAttrs && value is not null:
                    entry.DefinitionAttributes.Add((key, value));
                    break;

                // The named form opens a block per attribute; its `value` arrives a level deeper.
                case 4 when entry is not null && inItemAttributes && value is null:
                    pendingAttributeName = key;
                    break;

                case 5 when entry is not null && inItemAttributes && value is not null
                    && pendingAttributeName.Length > 0
                    && string.Equals(key, "value", StringComparison.OrdinalIgnoreCase):
                    entry.DefinitionAttributes.Add((pendingAttributeName, value));
                    break;

                case 4 when entry is not null && inVisuals && value is null:
                    inAttached =
                        key.StartsWith("attached_models", StringComparison.OrdinalIgnoreCase);

                    attachedIsFestive =
                        key.EndsWith("_festive", StringComparison.OrdinalIgnoreCase);

                    // **A sibling of `attached_models`, at the same depth** (B352). Tracked with its
                    // own flag rather than by testing the key again below, because the level-5 case
                    // has to know which block it is inside: an attachment's children are numbered
                    // and a bodygroup's are named.
                    inBodygroups =
                        key.Equals("player_bodygroups", StringComparison.OrdinalIgnoreCase);

                    inCustomParticle =
                        key.Equals(CustomParticleKey, StringComparison.OrdinalIgnoreCase);

                    // `attached_particlesystems`, and the two custom blocks that add one entry each with `iCustomType` 1 and 2
                    // (econ_item_schema.cpp:2516-2535, B69).
                    inAttachedParticles = key.Equals("attached_particlesystems", StringComparison.OrdinalIgnoreCase);
                    inAnyCustomParticle = inCustomParticle || key.Equals(CustomParticleKey + "2", StringComparison.OrdinalIgnoreCase);

                    if (inAnyCustomParticle)
                    {
                        entry.AttachedParticles.Add((visualsTeam, key, new AttachedParticle(null, null, false, inCustomParticle ? 1 : 2)));
                    }

                    // "Styles are only valid in the base "visuals" section" (econ_item_schema.cpp:2689).
                    inStyles = visualsTeam.Length == 0 && key.Equals("styles", StringComparison.OrdinalIgnoreCase);
                    inPoseParam = key.Equals("player_poseparam", StringComparison.OrdinalIgnoreCase);
                    inAnimation = key.StartsWith("animation_", StringComparison.OrdinalIgnoreCase);
                    inReplacement = key.Equals("animation_replacement", StringComparison.OrdinalIgnoreCase);
                    style = null;

                    if (inStyles)
                    {
                        entry.Styles = [];
                    }

                    break;

                case 5 when entry is not null && inPoseParam && value is not null:
                    if (!entry.PlayerPoseParams.TryGetValue(visualsTeam, out List<(string Name, float Value)>? poses))
                    {
                        poses = [];
                        entry.PlayerPoseParams[visualsTeam] = poses;
                    }

                    poses.Add((key, float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float pose) ? pose : 0f));
                    break;

                case 5 when entry is not null && inAnimation && value is not null
                    && key.Equals("taunt_concept", StringComparison.OrdinalIgnoreCase):
                    entry.TauntConceptBlocks.Add(visualsTeam);

                    // `taunt_concept` can be replaced too; the block it sits in decides both.
                    if (inReplacement)
                    {
                        Replacements(entry, visualsTeam)[key] = value;
                    }

                    break;

                // `GetOrCreateAnimationActivity( pVisData, name )->pszReplacement` (econ_item_schema.cpp:2553-2557).
                case 5 when entry is not null && inReplacement && value is not null:
                    Replacements(entry, visualsTeam)[key] = value;
                    break;

                // `FOR_EACH_SUBKEY( pKVStyles, pKVStyle )` (econ_item_schema.cpp:2810): one style per child, in order.
                case 5 when entry is not null && inStyles && value is null && entry.Styles is { } styles:
                    style = new ItemStyle();
                    styles.Add(style);
                    styleBlock = string.Empty;
                    break;

                case 6 when style is not null && inStyles && value is not null:
                    ApplyStyle(style, key, value);
                    break;

                case 6 when style is not null && inStyles && value is null:
                    styleBlock = key;
                    perClassBlock = null;

                    // `InitPerClassStringArray` replaces the whole array on each block it is given (:1170-1172).
                    if (key.Equals("model_player_per_class", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("model_player_per_class_red", StringComparison.OrdinalIgnoreCase))
                    {
                        perClassBlock = style.Red = new PerClassBlock();
                    }
                    else if (key.Equals("model_player_per_class_blue", StringComparison.OrdinalIgnoreCase))
                    {
                        perClassBlock = style.Blue = new PerClassBlock();
                    }

                    break;

                case 7 when style is not null && inStyles && value is not null:
                    if (styleBlock.Equals("additional_hidden_bodygroups", StringComparison.OrdinalIgnoreCase))
                    {
                        style.HideBodygroups.Add(key);
                    }
                    else if (styleBlock.Equals("bodygroup", StringComparison.OrdinalIgnoreCase))
                    {
                        // `bodygroup { name, submodel_index }` (econ_item_schema.cpp:2869-2876).
                        if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
                        {
                            style.BodygroupName = value;
                        }
                        else if (key.Equals("submodel_index", StringComparison.OrdinalIgnoreCase))
                        {
                            style.BodygroupSubmodelIndex = GetInt(value, -1);
                        }
                    }
                    else if (perClassBlock is not null && key.Equals("basename", StringComparison.OrdinalIgnoreCase))
                    {
                        perClassBlock.BaseName = value;
                    }
                    else if (perClassBlock is not null)
                    {
                        perClassBlock.PerClass[key] = value;
                    }

                    break;

                // **`custom_particlesystem { system … }`**, `iCustomType` 1 (`econ_item_schema.cpp:2533`): the system a
                // medigun adds beside its beam. Kept per block like the flat keys above.
                case 5 when entry is not null && inAnyCustomParticle && value is not null
                    && key.Equals("system", StringComparison.OrdinalIgnoreCase):
                    if (inCustomParticle)
                    {
                        entry.WeaponSounds[visualsTeam + "/" + CustomParticleKey] = value;
                    }

                    entry.AttachedParticles[^1] = entry.AttachedParticles[^1] with
                    {
                        Particle = entry.AttachedParticles[^1].Particle with { System = value },
                    };

                    break;

                // One `attached_particlesystems` child: committed empty, its fields written as they arrive (B69).
                case 5 when entry is not null && inAttachedParticles && value is null:
                    entry.AttachedParticles.Add((visualsTeam, key, new AttachedParticle(null, null, false, 0)));
                    break;

                case 6 when entry is not null && inAttachedParticles && value is not null && entry.AttachedParticles.Count > 0:
                    AttachedParticle pending = entry.AttachedParticles[^1].Particle;

                    pending = key.ToUpperInvariant() switch
                    {
                        "SYSTEM" => pending with { System = value },
                        "ATTACHMENT" => pending with { Attachment = value },

                        // `GetBool`: `GetInt() != 0` (KeyValues.cpp).
                        "ATTACH_TO_ROOTBONE" => pending with { FollowRootBone = GetInt(value, 0) != 0 },
                        _ => pending,
                    };

                    entry.AttachedParticles[^1] = entry.AttachedParticles[^1] with { Particle = pending };
                    break;

                // **`"hat" "1"` — a body part's name and the state to put it in.** The engine reads
                // the pair through `GetModifiedBodyGroup`, which hands back both, and applies it
                // only when the value matches the pass it is running (`econ_entity.cpp:2046`).
                // Stryker disable all : removing TryParse from switch 'when' leaves 'state' undeclared, CS0165
                case 5 when entry is not null && inBodygroups && value is not null
                    && int.TryParse(
                        value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int state):
                    entry.PlayerBodygroups[key] = state;

                    if (visualsTeam.Length == 0)
                    {
                        entry.BasePlayerBodygroups[key] = state;
                    }

                    break;
                // Stryker restore all

                // Each numbered child of the block is one attachment. Its fields arrive next, so
                // the pending record is reset here and committed when the following one starts or
                // the file ends — which is why `model` is written straight into the list below.
                case 5 when entry is not null && inAttached && value is null:
                    attachedModel = string.Empty;
                    attachedFlags = AttachedModel.MaskAll;
                    attachedKey = key;
                    break;

                case 6 when entry is not null && inAttached && value is not null:
                    if (string.Equals(key, "model", StringComparison.OrdinalIgnoreCase))
                    {
                        attachedModel = value;

                        entry.AttachedModels.Add(new AttachedModel(
                            attachedModel, attachedFlags, visualsTeam, attachedIsFestive, attachedKey));
                    }
                    // Stryker disable once : removing TryParse leaves 'flags' undeclared in else-if body, CS0165
                    else if (string.Equals(
                        key, "model_display_flags", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(
                            value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int flags))
                    {
                        attachedFlags = flags;

                        // **The flags can arrive AFTER the model**, since KeyValues preserves file
                        // order and the schema is not consistent about it. Rewriting the record
                        // just added keeps both orders working; dropping this would leave every
                        // such entry at the default mask and show a viewmodel-only attachment on
                        // the world model.
                        if (attachedModel.Length > 0 && entry.AttachedModels.Count > 0)
                        {
                            entry.AttachedModels[^1] = new AttachedModel(
                                attachedModel, attachedFlags, visualsTeam, attachedIsFestive, attachedKey);
                        }
                    }

                    break;

                case 4 when entry is not null && inPerClass && value is not null:
                    if (string.Equals(key, "basename", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.PerClassBaseName = value;
                    }
                    else
                    {
                        entry.PerClass[key] = value;
                    }

                    break;

                default:
                    break;
            }

            return true;
        });

        return read;
    }

    /// <summary>The model an item shows in a given class's hands, or <c>null</c>.</summary>
    /// <param name="definitionIndex">The item, as <c>m_iItemDefinitionIndex</c> gives it.</param>
    /// <param name="playerClass">Whose hands, as <c>m_iClass</c> gives it.</param>
    /// <returns>
    /// A model path; empty when the nearest <c>model_player</c> is <c>""</c>, which is the item's answer (B105); or
    /// <c>null</c> when the schema names none.
    /// </returns>
    /// <remarks>
    /// **An item's own empty `model_player` hides its prefab's model, and is returned as "".** The merge keeps the
    /// nearest declaration whatever it holds (`econ_item_schema.cpp:2909`, `:2962`, `:2967`), `BInitFromKV` reads it with
    /// a NULL default (`:3158`) and `GetPlayerDisplayModel` hands it back (`econ_item_view.cpp:969`). Eighteen shipped
    /// declarations, reaching 34 items — the fists, the spellbooks, the Duel MiniGame, gifts and medals; none sits over a
    /// prefab with a model, but a TF weapon's world model is exactly this answer, so "" is not "unknown" (B105).
    ///
    /// **Styles are not implemented, and this is very nearly what the engine does anyway** — which
    /// is not what an earlier version of this note claimed. `CEconItemView::GetItemStyle`
    /// (`econ_item_view.cpp:731`) ends at `GetSOCData()->GetStyle()`, and `GetSOCData` finds an
    /// inventory only for an account the client is subscribed to — its own (`:839`). A live client
    /// watching another player therefore gets `INVALID_STYLE_INDEX`, `GetStyleInfo` returns null,
    /// and the lookup falls through to exactly the per-class-then-base order below. In a demo there
    /// is no subscribed inventory at all.
    ///
    /// **The one real gap is the `item style override` attribute**, which is a networked attribute
    /// rather than backpack state and which a demo does carry — see `RISKS.md` B234. Nothing here
    /// decodes attributes yet, so an item wearing that attribute draws the wrong variant. Every
    /// other styled item draws what the engine would have drawn.
    /// </remarks>
    public string? ModelFor(int definitionIndex, int playerClass)
    {
        // Per class first, then the base — GetPlayerDisplayModel's own order
        // (`econ_item_view.cpp:962`), and both are inherited, so the whole chain is searched for
        // one before falling back to the other.
        if (playerClass > 0 && playerClass < ClassNames.Length)
        {
            return PerClassModel(definitionIndex, playerClass)
                ?? Inherited(definitionIndex, entry => entry.Model, emptyAnswers: true);
        }

        // **`TF_CLASS_UNDEFINED` is not an empty slot, it is a copy of the first class's answer.**
        // `InitPerClassStringArray` ends every iteration with
        // `if ( outputArray[0] == NULL ) outputArray[0] = outputArray[i]`
        // (`tf_item_schema.cpp:541`), and `CEconItemView::GetPlayerDisplayModel` reads slot zero
        // like any other before reaching the base model. So a prop whose owner is not a player this
        // moment knows about still resolves.
        for (int candidate = 1; candidate < ClassNames.Length; candidate++)
        {
            if (PerClassModel(definitionIndex, candidate) is { } first)
            {
                return first;
            }
        }

        return Inherited(definitionIndex, entry => entry.Model, emptyAnswers: true);
    }

    /// <summary><c>TF_CLASS_DEMOMAN</c>, whose model files disagree with his schema name.</summary>
    private const int Demoman = 4;

    /// <summary>What <c>model_player_per_class</c> says for one class, in Valve's order.</summary>
    /// <remarks>
    /// **Two forms, and reading one of them is the bug this fixes.** `InitPerClassStringArray`
    /// (`tf_item_schema.cpp:489`) takes the class's own entry when there is one and otherwise
    /// expands the block's `basename`:
    ///
    /// <code>
    ///   CUtlString strClassString( pPerClassData-&gt;GetString( ClassUsability[i], NULL ) );
    ///   if ( !strClassString.IsEmpty() )  use it
    ///   else if ( pszBaseName )           sprintf( pszBaseName, name, name, name )
    /// </code>
    ///
    /// Both halves are looked up through the prefab chain, because the engine merges a definition
    /// with its prefabs before reading the block at all — so a child naming one class and a prefab
    /// carrying the pattern is one block by the time Valve sees it.
    /// </remarks>
    private string? PerClassModel(int definitionIndex, int playerClass)
    {
        string className = ClassNames[playerClass];

        // Stryker disable once : removing TryGetValue leaves 'model' undeclared in lambda ternary, CS0165
        if (Inherited(definitionIndex, entry =>
            entry.PerClass.TryGetValue(className, out string? model) ? model : null) is { } named)
        {
            return named;
        }

        if (Inherited(definitionIndex, entry => entry.PerClassBaseName) is not { } pattern)
        {
            return null;
        }

        // **The demoman is spelled `demo` here and Valve's own source apologises for it**: *"the
        // vast majority of his models are whatever_demo.mdl ... If this class is the
        // TF_CLASS_DEMOMAN, just force 'demo'"* (`tf_item_schema.cpp:519`). Without this every
        // demoman cosmetic using a pattern names a file that does not exist, which looks on screen
        // exactly like naming no file at all.
        string token = playerClass == Demoman ? "demo" : className;

        // **Valve supplies the name three times to one `sprintf`**, so a pattern with more `%s`
        // than that is undefined behaviour in the engine and simply reads adjacent stack. Replacing
        // every occurrence is the same answer for every pattern the schema actually contains — the
        // most any of them uses is two — and is defined for the rest.
        //
        // The substituted name is lower case where Valve's `ClassUsabilityStrings` are capitalised.
        // Source's filesystem is case-insensitive and the schema's own patterns are lower case, so
        // this produces the path the engine resolves to rather than the one it constructs.
        return pattern.Replace("%s", token, StringComparison.Ordinal);
    }

    /// <summary>Whether this item is drawn as its own model parented to the player's arms.</summary>
    /// <remarks>
    /// <c>attach_to_hands</c>, which <c>CEconEntity</c> reads as <c>ShouldAttachToHands</c> to
    /// decide whether to create a viewmodel attachment at all. An item without it is not attached,
    /// which for a weapon means the viewmodel itself carries the model — the older arrangement,
    /// where the viewmodel is <c>v_scattergun_scout.mdl</c> rather than a pair of arms.
    /// </remarks>
    public bool AttachesToHands(int definitionIndex) =>
        Inherited(definitionIndex, entry => entry.AttachToHands is true ? "1" : null) is not null;

    /// <summary>Whether the item's viewmodel is built left-handed — <c>flip_viewmodel</c> (B515).</summary>
    /// <remarks>
    /// `m_bFlipViewModel = m_pKVItem->GetInt( "flip_viewmodel", 0 ) != 0;` (`econ_item_schema.cpp:3169`), after the
    /// prefabs are folded in. One shipped item sets it: the Huntsman's `c_bow.mdl`.
    /// </remarks>
    public bool FlipsViewmodel(int definitionIndex) =>
        InheritedInt(definitionIndex, "flip_viewmodel") != 0;

    /// <summary>
    /// <c>ITEM_DROP_TYPE_NONE</c> — the item stays attached to the body (<c>econ_wearable.h:33</c>),
    /// and what <see cref="DropType"/> answers for an item that does not say.
    /// </summary>
    /// <remarks>
    /// **This is the constructor's default, and it is NOT the number a loaded item actually carries
    /// for a missing key — that distinction is the finding here.**
    /// <c>CEconItemDefinition::BInitFromKV</c> (<c>econ_item_schema.cpp:3173</c>) unconditionally
    /// overwrites the constructor's <c>m_iDropType( 1 )</c> (<c>econ_item_schema.cpp:2302</c>) with
    /// <c>StringFieldToInt( m_pKVItem-&gt;GetString("drop_type"), g_szDropTypeStrings, 4 )</c>, every
    /// time the schema loads. <c>GetString</c>'s own default-default is <c>""</c>
    /// (<c>KeyValues.h:176</c>), and <c>StringFieldToInt</c>'s guard —
    /// <c>if ( !szValue || !szValue[0] ) return -1;</c> (<c>econ_item.cpp:33</c>) — fires before the
    /// comparison loop ever runs. So a missing OR blank <c>drop_type</c> lands on <b>-1</b> at
    /// runtime, never on the constructor's 1 — and that same guard is why the string table's own
    /// <c>""</c> entry (index 0, <c>ITEM_DROP_TYPE_NULL</c>) can never be what the loop matches
    /// either; it is a documented value nothing can parse into.
    ///
    /// **Returned here anyway, because no consumer in the SDK can tell -1 from 1 apart.** Every
    /// reader of <c>GetDropType()</c> compares specifically against <see cref="DropTypeDrop"/>:
    /// <c>c_tf_player.cpp:7525</c> (<c>!= ITEM_DROP_TYPE_DROP</c>), <c>c_tf_player.cpp:10199</c>
    /// (<c>&gt;= ITEM_DROP_TYPE_DROP</c>), <c>econ_wearable.cpp:819</c>
    /// (<c>== ITEM_DROP_TYPE_DROP</c>). -1, 0 and 1 are interchangeable at every one of them, so this
    /// wrapper answers with the named, in-range value rather than reproducing the parse artefact.
    /// </remarks>
    public const int DropTypeNone = 1;

    /// <summary>
    /// <c>ITEM_DROP_TYPE_NULL</c> (<c>econ_wearable.h:32</c>) — the string table's <c>""</c> entry.
    /// Named for completeness; <see cref="DropType"/> can never return it — see
    /// <see cref="DropTypeNone"/>'s remarks for why the parse that would produce it never runs.
    /// </summary>
    public const int DropTypeNull = 0;

    /// <summary><c>ITEM_DROP_TYPE_DROP</c> — the item drops off the body (<c>econ_wearable.h:34</c>).</summary>
    public const int DropTypeDrop = 2;

    /// <summary>
    /// <c>ITEM_DROP_TYPE_BREAK</c> (<c>econ_wearable.h:35</c>). Valve's own comment calls it "Not
    /// implemented, but an example of a type that could be added" (<c>econ_item_schema.cpp:74</c>).
    /// </summary>
    public const int DropTypeBreak = 3;

    /// <summary>
    /// The table <c>StringFieldToInt</c> matches a <c>drop_type</c> value against, in enum order.
    /// </summary>
    /// <remarks>
    /// <c>g_szDropTypeStrings</c>, <c>econ_item_schema.cpp:69-74</c>. Index 0 is kept only so the
    /// table's shape matches the engine's; see <see cref="DropTypeNone"/>'s remarks for why a real
    /// value can never land there.
    /// </remarks>
    private static readonly string[] DropTypeStrings = ["", "none", "drop", "break"];

    /// <summary>
    /// <c>GetDropType()</c> — whether an item drops or breaks off the body on death, or stays on it.
    /// </summary>
    /// <param name="itemDefinitionIndex">The item, as <c>m_iItemDefinitionIndex</c> gives it.</param>
    /// <returns>
    /// <see cref="DropTypeNone"/>, <see cref="DropTypeDrop"/> or <see cref="DropTypeBreak"/>.
    /// </returns>
    /// <remarks>
    /// <c>m_iDropType = StringFieldToInt( m_pKVItem-&gt;GetString("drop_type"), g_szDropTypeStrings,
    /// ARRAYSIZE(g_szDropTypeStrings) )</c> (<c>econ_item_schema.cpp:3173</c>), matched
    /// case-insensitively (<c>Q_stricmp</c>, <c>econ_item.cpp:37</c>). Inherited through the prefab
    /// chain like every other field here, because <c>BInitFromKV</c> reads this off <c>m_pKVItem</c>
    /// AFTER <c>MergeDefinitionPrefab</c> has already folded the prefab chain into it
    /// (<c>econ_item_schema.cpp:3023-3024</c>) — the same reason <see cref="ModelFor"/> searches
    /// prefabs rather than reading only the item's own block.
    ///
    /// See <see cref="DropTypeNone"/>'s remarks for why a missing, blank or unrecognised value all
    /// resolve here rather than to the engine's literal runtime sentinel of -1.
    /// </remarks>
    public int DropType(int itemDefinitionIndex)
    {
        if (Inherited(itemDefinitionIndex, entry => entry.DropType) is { } raw)
        {
            for (int ordinal = 0; ordinal < DropTypeStrings.Length; ordinal++)
            {
                if (string.Equals(raw, DropTypeStrings[ordinal], StringComparison.OrdinalIgnoreCase))
                {
                    return ordinal;
                }
            }
        }

        return DropTypeNone;
    }

    /// <summary><c>LOADOUT_POSITION_INVALID</c> (<c>tf_item_constants.h:49</c>).</summary>
    /// <remarks>
    /// What <c>m_iDefaultLoadoutSlot</c> is constructed with (<c>tf_item_schema.cpp:892</c>) and
    /// what it STAYS as for an item with no <c>item_slot</c> key: unlike <c>drop_type</c>, the parse
    /// is skipped entirely rather than run on a blank string —
    /// <c>if ( *pszLoadoutSlot ) { … }</c> (<c>tf_item_schema.cpp:939-952</c>) — so this one default
    /// is unambiguous both in the constructor and at runtime.
    /// </remarks>
    public const int LoadoutSlotInvalid = -1;

    /// <summary><c>LOADOUT_POSITION_PRIMARY</c> (<c>tf_item_constants.h:51</c>).</summary>
    public const int LoadoutSlotPrimary = 0;

    /// <summary><c>LOADOUT_POSITION_SECONDARY</c> (<c>tf_item_constants.h:52</c>).</summary>
    public const int LoadoutSlotSecondary = 1;

    /// <summary><c>LOADOUT_POSITION_MELEE</c> (<c>tf_item_constants.h:53</c>).</summary>
    public const int LoadoutSlotMelee = 2;

    /// <summary><c>LOADOUT_POSITION_UTILITY</c> (<c>tf_item_constants.h:54</c>).</summary>
    public const int LoadoutSlotUtility = 3;

    /// <summary><c>LOADOUT_POSITION_BUILDING</c> (<c>tf_item_constants.h:55</c>).</summary>
    public const int LoadoutSlotBuilding = 4;

    /// <summary><c>LOADOUT_POSITION_PDA</c> (<c>tf_item_constants.h:56</c>).</summary>
    public const int LoadoutSlotPda = 5;

    /// <summary><c>LOADOUT_POSITION_PDA2</c> (<c>tf_item_constants.h:57</c>).</summary>
    public const int LoadoutSlotPda2 = 6;

    /// <summary>
    /// <c>LOADOUT_POSITION_HEAD</c> (<c>tf_item_constants.h:59</c>). **Not reachable from
    /// <see cref="DefaultLoadoutSlot"/> for a schema-declared default slot** — see that method's
    /// remarks for the rewrite that sends every "head" to <see cref="LoadoutSlotMisc"/> instead.
    /// </summary>
    public const int LoadoutSlotHead = 7;

    /// <summary><c>LOADOUT_POSITION_MISC</c> (<c>tf_item_constants.h:60</c>).</summary>
    public const int LoadoutSlotMisc = 8;

    /// <summary><c>LOADOUT_POSITION_ACTION</c> (<c>tf_item_constants.h:63</c>).</summary>
    public const int LoadoutSlotAction = 9;

    /// <summary><c>LOADOUT_POSITION_TAUNT</c> (<c>tf_item_constants.h:69</c>).</summary>
    public const int LoadoutSlotTaunt = 11;

    /// <summary>
    /// The table <c>StringFieldToInt</c> matches an <c>item_slot</c> value against, in enum order.
    /// </summary>
    /// <remarks>
    /// <c>g_szLoadoutStrings</c>, <c>tf_item_schema.cpp:1513-1533</c>, for <c>EQUIP_TYPE_CLASS</c> —
    /// the table every wearable and weapon uses, since <c>"class"</c> is the schema's default
    /// <c>equip_type</c> (<c>tf_item_schema.cpp:928</c>) and the account table has no head or misc
    /// position at all. Index 10 (<c>LOADOUT_POSITION_MISC2</c>) ships blank in the SDK and is
    /// unreachable by the same guard as the drop-type table's index 0; the blank
    /// <c>taunt2</c>–<c>taunt8</c> tail that follows it there is the same shape and is not worth
    /// carrying here.
    /// </remarks>
    private static readonly string[] LoadoutSlotStrings =
    [
        "primary", "secondary", "melee", "utility", "building", "pda", "pda2",
        "head", "misc", "action", "", "taunt",
    ];

    /// <summary><c>GetDefaultLoadoutSlot()</c> — which loadout slot an item occupies by default.</summary>
    /// <param name="itemDefinitionIndex">The item, as <c>m_iItemDefinitionIndex</c> gives it.</param>
    /// <returns>
    /// One of the <c>LoadoutSlot*</c> constants, or <see cref="LoadoutSlotInvalid"/> when the schema
    /// does not say.
    /// </returns>
    /// <remarks>
    /// <c>const char *pszLoadoutSlot = pKVInitValues-&gt;GetString("item_slot", "");</c>
    /// (<c>tf_item_schema.cpp:939</c>), read off the same prefab-merged <c>m_pKVItem</c>
    /// <see cref="DropType"/> reads, so it is inherited the same way.
    ///
    /// **The one rewrite that makes this worth having its own test.** Immediately before the table
    /// lookup, the engine does
    /// <c>if ( !V_strcmp( pszLoadoutSlot, "head" ) ) pszLoadoutSlot = "misc";</c>
    /// (<c>tf_item_schema.cpp:941-944</c>), and <c>V_strcmp</c> is plain, case-SENSITIVE <c>strcmp</c>
    /// (<c>strtools.h:160</c>) — unlike the case-insensitive table match
    /// (<c>Q_stricmp</c> inside <c>StringFieldToInt</c>) that follows it. So a schema-declared
    /// <c>item_slot "head"</c> can NEVER resolve to <see cref="LoadoutSlotHead"/>; it always becomes
    /// <see cref="LoadoutSlotMisc"/> instead, and the real armory UI already assumes this —
    /// <c>charinfo_armory_subpanel.cpp:605</c> tests only <c>== LOADOUT_POSITION_MISC</c>. A
    /// differently-cased declaration such as <c>"Head"</c> is NOT caught by the exact-case rewrite
    /// and resolves to <see cref="LoadoutSlotHead"/> via the case-insensitive lookup below — an
    /// accident of Valve's own comparison, not a second deliberate rule.
    /// </remarks>
    public int DefaultLoadoutSlot(int itemDefinitionIndex)
    {
        if (Inherited(itemDefinitionIndex, entry => entry.LoadoutSlot) is not { } raw)
        {
            return LoadoutSlotInvalid;
        }

        // Valve rewrites the exact, lower-case "head" to "misc" before resolving it — see the
        // remarks above for the citation and why a different casing is not caught by it.
        string resolved = string.Equals(raw, "head", StringComparison.Ordinal) ? "misc" : raw;

        for (int ordinal = 0; ordinal < LoadoutSlotStrings.Length; ordinal++)
        {
            if (string.Equals(resolved, LoadoutSlotStrings[ordinal], StringComparison.OrdinalIgnoreCase))
            {
                return ordinal;
            }
        }

        return LoadoutSlotInvalid;
    }

    /// <summary><c>CTFItemDefinition::GetLoadoutSlot( iLoadoutClass )</c> (tf_item_schema.cpp:1271).</summary>
    /// <param name="itemDefinitionIndex">The item.</param>
    /// <param name="playerClass">The class asking, 1 Scout through 9 Engineer.</param>
    /// <returns>The slot, or <see cref="LoadoutSlotInvalid"/> for a class the item is not used by.</returns>
    /// <remarks>
    /// Outside 1..9 the default slot (:1278). Otherwise `m_iLoadoutSlots[ class ]`, filled from `used_by_classes`
    /// (:958-981): the default slot for a value starting with '1', else the named slot when it is one — the class table
    /// matched without case, and with no "head" rewrite. The prefab chain's blocks merge, the item's own keys winning
    /// (`MergeDefinitionPrefab`, econ_item_schema.cpp:2940).
    /// </remarks>
    public int LoadoutSlot(int itemDefinitionIndex, int playerClass)
    {
        int defaultSlot = DefaultLoadoutSlot(itemDefinitionIndex);

        if (playerClass <= 0 || playerClass >= ClassNames.Length)
        {
            return defaultSlot;
        }

        if (Inherited(itemDefinitionIndex, entry => entry.UsedByClasses.GetValueOrDefault(ClassNames[playerClass])) is not { } value)
        {
            return LoadoutSlotInvalid;
        }

        // Valve's own test (:972). No slot name starts with '1', so this branch and the fallback below agree on every
        // value — kept for the shape, and an equivalent mutant.
        if (value.StartsWith('1'))
        {
            return defaultSlot;
        }

        int named = Array.FindIndex(LoadoutSlotStrings, slot => slot.Length > 0 && string.Equals(slot, value, StringComparison.OrdinalIgnoreCase));

        return named >= 0 ? named : defaultSlot;
    }

    /// <summary><c>GetItemSchema()-&gt;GetRarityColor( GetItemDefinition()-&gt;GetRarity() )</c> (econ_item_schema.cpp:6633).</summary>
    /// <param name="itemDefinitionIndex">The item.</param>
    /// <returns>A scheme color name, or null where the item has no rarity definition — the caller's "TanLight".</returns>
    /// <remarks>
    /// `item_rarity` (:3081, prefabs included) names a rarity, "any" being `k_unItemRarity_Any`; the rarity found by its
    /// `value` (`GetRarityDefinition`, :6597) gives a `color`, which `GetAttribColorIndexForName`
    /// (econ_item_constants.cpp:293) matches exactly against `g_AttribColorDefs` — index 0, `desc_level`, when none —
    /// and whose `color_name` in the `colors` section answers, or "ItemAttribNeutral" when that section lacks it (:311).
    /// </remarks>
    public string? RarityColor(int itemDefinitionIndex)
    {
        if (RarityName(itemDefinitionIndex) is not { } rarityName
            || string.Equals(rarityName, "any", StringComparison.OrdinalIgnoreCase)
            || !_rarities.TryGetValue(rarityName, out (int? Value, string Color) named))
        {
            return null;
        }

        // `GetRarityDefinition( value )`: the map is keyed by value, so the first rarity carrying it answers.
        foreach ((int? value, string color) in _rarities.Values)
        {
            if (value == named.Value)
            {
                string colorDef = Array.IndexOf(AttribColorDefs, color) is var index and >= 0 ? AttribColorDefs[index] : AttribColorDefs[0];

                return _colorNames.GetValueOrDefault(colorDef, "ItemAttribNeutral");
            }
        }

        return null;
    }

    /// <summary>`g_AttribColorDefs` (econ_item_constants.cpp:263-289), in `attrib_colors_t` order.</summary>
    private static readonly string[] AttribColorDefs =
    [
        "desc_level", "desc_attrib_neutral", "desc_attrib_positive", "desc_attrib_negative", "desc_itemset_name",
        "desc_itemset_equipped", "desc_itemset_missing", "desc_bundle", "desc_limited_use", "desc_flags",
        "desc_limited_quantity", "desc_default", "desc_common", "desc_uncommon", "desc_rare", "desc_mythical",
        "desc_legendary", "desc_ancient", "desc_immortal", "desc_arcana", "desc_strange", "desc_unusual",
    ];

    /// <summary>The stock item's model for a weapon entity class, such as <c>tf_weapon_wrench</c>.</summary>
    /// <param name="itemClass">The weapon's entity class, from its script name.</param>
    /// <param name="playerClass">Whose hands, as <c>m_iClass</c> gives it.</param>
    /// <returns>
    /// A model path, empty when that item names <c>""</c> (<see cref="ModelFor"/>), or <c>null</c> when no base item claims
    /// that class.
    /// </returns>
    /// <remarks>
    /// **The fallback for a weapon whose item index never arrives**, which is a real and common
    /// case: measured on z1800, 22 of 56 held weapons carry no
    /// <c>m_iItemDefinitionIndex</c> — the same weapon CLASS appearing identified on one player and
    /// not on another, so it is a property of the entity rather than of the weapon.
    ///
    /// The schema marks stock items with <c>"baseitem" "1"</c> and names the entity class they
    /// stand for, which is the same pairing <c>LINK_ENTITY_TO_CLASS</c> makes on the code side. So
    /// an unidentified <c>CTFWrench</c> resolves to whatever <c>items_game.txt</c> calls the base
    /// item for <c>tf_weapon_wrench</c> — the stock wrench, which is what an unmodified loadout
    /// slot holds.
    ///
    /// **This is a fallback and it can be wrong**, in exactly one direction: a player carrying a
    /// reskin whose index did not arrive gets the stock model. That is the right weapon in the
    /// wrong finish, and it is visibly better than an empty hand.
    /// </remarks>
    public string? ModelForClass(string itemClass, int playerClass)
    {
        ArgumentNullException.ThrowIfNull(itemClass);

        int? anyOfThatClass = null;

        foreach ((int index, Entry entry) in _items)
        {
            if (!string.Equals(
                    Search(entry, item => item.ItemClass, LongestChain),
                    itemClass,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.IsBaseItem)
            {
                return ModelFor(index, playerClass);
            }

            // **Kept in case no base item claims the class**, which is the shape of every weapon
            // that only ever existed as an unlock: nothing is the "stock" Direct Hit or Rescue
            // Ranger, so a base-item-only search answers nothing for them. Measured on z1800 as the
            // last two of fifty-six held weapons.
            //
            // Lowest index rather than first met, so the answer does not depend on dictionary
            // order — a schema with several skins of one class would otherwise resolve differently
            // between runs.
            anyOfThatClass = anyOfThatClass is { } best ? Math.Min(best, index) : index;
        }

        return anyOfThatClass is { } fallback ? ModelFor(fallback, playerClass) : null;
    }

    /// <summary>Every item definition index the schema declares.</summary>
    /// <remarks>
    /// For instruments that need a DENOMINATOR rather than an answer about one item — "how many
    /// items carry an attachment" is a fact about the game, where a count of schema blocks is only
    /// a fact about the file, and the two differ by however many definitions inherit each block.
    /// </remarks>
    public IEnumerable<int> DefinitionIndices => _items.Keys;

    /// <summary>The extra models an item hangs on itself, filtered as the engine filters them.</summary>
    /// <param name="definitionIndex">The item, as <c>m_iItemDefinitionIndex</c> gives it.</param>
    /// <param name="team">
    /// The owner's team — <c>SceneTeams.Red</c> or <c>SceneTeams.Blu</c> — or null when unknown.
    /// `CEconEntity::UpdateAttachmentModels` reads `GetNumAttachedModels( GetTeamNumber() )`, so a
    /// per-team block belongs to one side only.
    /// </param>
    /// <param name="festivized">
    /// Whether the item carries <c>is_festivized</c>. The festive block is added only then
    /// (<c>econ_entity.cpp:1109</c>), and there are ten times as many festive entries as plain
    /// ones — so getting this wrong decorates the whole server for Christmas.
    /// </param>
    /// <returns>The attachments, in schema order. Empty for an item that declares none.</returns>
    /// <remarks>
    /// **Inherited through prefabs like every other item field.** A stock weapon says almost
    /// nothing itself; the attachment can be on a prefab several levels up, which is the shape
    /// `Inherited` already exists for.
    /// </remarks>
    public IReadOnlyList<AttachedModel> AttachedModelsFor(
        int definitionIndex, int? team, bool festivized)
    {
        // `GetNumAttachedModels( iTeamNumber )` and its festive twin (econ_item_schema.h:1735-1796): the block
        // `GetBestVisualTeamData` picks, alone. An unknown team asks as team 0, the base block.
        if (!_items.TryGetValue(definitionIndex, out Entry? item) || BestVisualSection(item, team ?? 0) is not { } section)
        {
            return [];
        }

        // `MergeDefinitionPrefab` (econ_item_schema.cpp:2940-2967): prefabs back to front, each after its own prefabs,
        // then the item; `RecursiveInheritKeyValues` (:2897) replaces a child of the same name in place, else appends.
        List<AttachedModel> plain = [];
        List<AttachedModel> festive = [];

        foreach (Entry level in MergeOrder(item, LongestChain))
        {
            foreach (AttachedModel attached in level.AttachedModels)
            {
                if (!string.Equals(attached.Team, section, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                List<AttachedModel> into = attached.Festive ? festive : plain;
                int existing = into.FindIndex(each => string.Equals(each.Key, attached.Key, StringComparison.OrdinalIgnoreCase));

                if (existing >= 0)
                {
                    into[existing] = attached;
                }
                else
                {
                    into.Add(attached);
                }
            }
        }

        // The festive loop runs after the plain one, and only under `is_festivized` (econ_entity.cpp:1107-1131).
        if (festivized)
        {
            plain.AddRange(festive);
        }

        return plain;
    }

    /// <summary>The order `MergeDefinitionPrefab` applies an entry's chain in: the last prefab first, the entry last.</summary>
    private List<Entry> MergeOrder(Entry entry, int remaining)
    {
        List<Entry> order = [];

        if (remaining > 0)
        {
            for (int index = entry.Prefabs.Count - 1; index >= 0; index--)
            {
                if (_prefabs.TryGetValue(entry.Prefabs[index], out Entry? prefab))
                {
                    order.AddRange(MergeOrder(prefab, remaining - 1));
                }
            }
        }

        order.Add(entry);

        return order;
    }

    /// <summary><c>TF_TEAM_RED</c>, matching <c>SceneTeams.Red</c>.</summary>
    private const int RedTeam = 2;

    /// <summary><c>TF_TEAM_BLUE</c>, matching <c>SceneTeams.Blu</c>.</summary>
    private const int BluTeam = 3;

    /// <summary>The wearer's body parts an item changes, prefabs included.</summary>
    /// <param name="definitionIndex">The item, as <c>m_iItemDefinitionIndex</c> gives it.</param>
    /// <returns>Each bodygroup NAME and the state to put it in; empty when the item changes none.</returns>
    /// <remarks>
    /// **747 shipped items declare one** — `hat` on 457, `headphones` on 306, then `grenades`,
    /// `head`, `dogtags`, `shoes_socks` and `backpack`. Those are real body parts on a class model
    /// whose alternative 1 carries NO MESH, so setting one removes the default part a cosmetic
    /// replaces (B352).
    ///
    /// **The item's own entry wins over its prefab's for the same name**, which is `model_player`'s
    /// rule rather than `attached_models`': a bodygroup is a single state per name, so there is
    /// nothing to accumulate and an item saying `"hat" "0"` under a prefab saying `"hat" "1"` is
    /// deliberately putting the part back.
    /// </remarks>
    /// <remarks>
    /// Read from the BASE visuals block only — `GetModifiedBodyGroup( 0, ... )` in both callers (econ_entity.cpp:2045,
    /// tf_playermodelpanel.cpp:843). The nearest definition wins per name.
    /// </remarks>
    public IReadOnlyDictionary<string, int> BasePlayerBodygroupsFor(int definitionIndex) => BaseBodygroups(definitionIndex);

    /// <summary>Whether an item changes those parts only while it is the active weapon.</summary>
    /// <param name="definitionIndex">The item, as <c>m_iItemDefinitionIndex</c> gives it.</param>
    /// <returns>True when the item or a prefab sets <c>hide_bodygroups_deployed_only</c>.</returns>
    /// <remarks>
    /// **The nearest definition wins and silence is not an answer**, which is why
    /// <see cref="Entry.HideBodygroupsDeployedOnly"/> is nullable: the search stops at the first
    /// entry in the chain that states the key, so an item can turn its prefab's flag off.
    /// <see cref="Search"/> is the same walk for a string, and this is deliberately not folded into
    /// it — the value is a tri-state and encoding it as `"1"`/`"0"`/absent through a string search
    /// puts a parse in the middle of a lookup.
    /// </remarks>
    // Stryker disable once : removing TryGetValue leaves 'item' undeclared in && operand, CS0165
    public bool HidesBodygroupsWhenDeployedOnly(int definitionIndex) =>
        _items.TryGetValue(definitionIndex, out Entry? item)
        && DeployedOnly(item, LongestChain) == true;

    /// <summary>A wearer's body part an item addresses by NUMBER, and the state to put it in.</summary>
    /// <param name="definitionIndex">The item, as <c>m_iItemDefinitionIndex</c> gives it.</param>
    /// <returns>The pair from <c>wm_bodygroup_override</c>, each -1 when the chain does not state it.</returns>
    /// <remarks>
    /// **Reported as the file has it, guard and all left to the caller** (B353). The engine applies
    /// `if ( iBodyOverride &gt; -1 &amp;&amp; iBodyStateOverride &gt; -1 )` at the point of use
    /// (<c>econ_entity.cpp:2085</c>), and half a declaration is a real shape in the schema — so
    /// collapsing the pair here would mean this method deciding a question the engine decides
    /// elsewhere, and a reader could no longer tell "declares nothing" from "declares half".
    ///
    /// **The two halves are searched independently**, because the chain can split them: an item may
    /// restate the part while taking the state from its prefab.
    /// </remarks>
    /// <param name="team">The owner's team — `GetWorldmodelBodygroupOverride( pOwner->GetTeamNumber() )`.</param>
    /// <remarks>
    /// `GetBestVisualTeamData( iTeam )` picks the block (econ_item_schema.h:2161-2186); a block leaves each half at -1
    /// (`perteamvisuals_t()`, :1067-1068). **With no block at all both halves are 0**, which passes the `&gt; -1` guard
    /// and sets part 0 to 0 — Valve's own return, so reproduced. An index the schema lacks is the `default` item
    /// (econ_item_schema.cpp:6694), which has no visuals: also 0.
    /// </remarks>
    public (int Group, int State) WorldmodelBodygroupOverrideFor(int definitionIndex, int team)
    {
        if (!_items.TryGetValue(definitionIndex, out Entry? item) || BestVisualSection(item, team) is not { } section)
        {
            return (0, 0);
        }

        return (
            GetInt(Search(item, entry => entry.VisualKeys.GetValueOrDefault(section + "/wm_bodygroup_override"), LongestChain), -1),
            GetInt(Search(item, entry => entry.VisualKeys.GetValueOrDefault(section + "/wm_bodygroup_state_override"), LongestChain), -1));
    }

    /// <summary>The vision a viewer needs before this item is drawn to them (B354).</summary>
    /// <param name="definitionIndex">The item, as <c>m_iItemDefinitionIndex</c> gives it.</param>
    /// <returns>The flag set, or 0 — which is all but 23 shipped items.</returns>
    /// <remarks>
    /// `m_nVisionFilterFlags = m_pKVItem-&gt;GetInt( "vision_filter_flags", 0 )`
    /// (<c>econ_item_schema.cpp:3156</c>). Zero is the engine's own default and the value its
    /// consumer's `!= 0` guard reads as "never hidden", so an unknown item answering 0 degrades the
    /// same way the engine does rather than hiding something.
    /// </remarks>
    // Stryker disable once : removing TryGetValue leaves 'item' undeclared in ternary, CS0165
    public int VisionFilterFlagsFor(int definitionIndex) =>
        _items.TryGetValue(definitionIndex, out Entry? item)
            ? Vision(item, LongestChain) ?? 0
            : 0;

    /// <summary>The first stated vision filter in an entry's prefab chain, or null.</summary>
    private int? Vision(Entry entry, int remaining)
    {
        if (entry.VisionFilterFlags is { } stated)
        {
            return stated;
        }

        if (remaining <= 0)
        {
            return null;
        }

        foreach (string name in entry.Prefabs)
        {
            if (_prefabs.TryGetValue(name, out Entry? prefab)
                && Vision(prefab, remaining - 1) is { } inherited)
            {
                return inherited;
            }
        }

        return null;
    }

    /// <summary>The first answer in an entry's prefab chain, or null when none states it.</summary>
    private bool? DeployedOnly(Entry entry, int remaining)
    {
        if (entry.HideBodygroupsDeployedOnly is { } stated)
        {
            return stated;
        }

        if (remaining <= 0)
        {
            return null;
        }

        foreach (string name in entry.Prefabs)
        {
            if (_prefabs.TryGetValue(name, out Entry? prefab)
                && DeployedOnly(prefab, remaining - 1) is { } inherited)
            {
                return inherited;
            }
        }

        return null;
    }

    /// <summary>The definition index a named attribute resolves to, or null for an unknown name.</summary>
    /// <remarks>
    /// The top-level <c>attributes</c> section's bridge — <c>GetAttributeDefinitionByName</c> in
    /// the engine. Consumers ask by name so a renumbered schema cannot silently retarget them.
    /// </remarks>
    public int? AttributeDefinitionIndex(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Stryker disable once : removing TryGetValue leaves 'index' undeclared in ternary, CS0165
        return _attributeIndexByName.TryGetValue(name, out int index) ? index : null;
    }

    /// <summary>The attributes an item DEFINITION carries — <c>IterateAttributes</c>' branch 4.</summary>
    /// <param name="definitionIndex">The item, as <c>m_iItemDefinitionIndex</c> gives it.</param>
    /// <returns>The definition's attributes as wire-shaped values. Empty when it declares none.</returns>
    /// <remarks>
    /// **Per-NAME nearest-wins through the prefab chain**, because KeyValues prefab merging is
    /// per-key with the item outermost — an item restating a prefab's attribute overrides it, one
    /// entry rather than two. A name the top-level section does not know is skipped: nothing is
    /// the honest answer, where a guessed index would collide with a real attribute.
    ///
    /// **The value string's reading depends on <c>stored_as_integer</c>.** The union holds 32 raw
    /// bits; an integer attribute's <c>"64"</c> is the integer itself, a float attribute's
    /// <c>"1.1"</c> is the float's bit pattern — and confusing the two produces a denormal, not a
    /// number.
    ///
    /// **Built once per definition and kept, as the engine keeps it.** `CEconItemDefinition::BInitFromKV` fills
    /// `m_vecStaticAttributes` at schema init (econ_item_schema.cpp:3330-3359) and `IterateAttributes` only walks it
    /// (:3544-3561). The schema never changes after <see cref="Read"/>, so the answer for an index is a constant; it was
    /// rebuilt — a list, a name set and the prefab walk — per prop per frame (B599). Thread-safe: the scene and its
    /// workers both ask.
    /// </remarks>
    public IReadOnlyList<EconAttributeValue> DefinitionAttributesFor(int definitionIndex) =>
        // An index the schema lacks is kept NOWHERE: it comes from the recording, so a corrupt or hostile demo could carry
        // any 32-bit value and a cache entry per one would grow for the schema's lifetime (review, 2026-10-30).
        _items.ContainsKey(definitionIndex)
            ? _definitionAttributes.GetOrAdd(definitionIndex, static (index, schema) => schema.BuildDefinitionAttributes(index), this)
            : [];

    private EconAttributeValue[] BuildDefinitionAttributes(int definitionIndex)
    {
        if (!_items.TryGetValue(definitionIndex, out Entry? item))
        {
            return [];
        }

        List<EconAttributeValue> found = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        CollectDefinitionAttributes(item, found, seen, LongestChain);

        return [.. found];
    }

    /// <summary>
    /// A STRING attribute an item definition carries (`CAttribute_String`), nearest definition first — such as
    /// `weapon_uses_stattrak_module` (econ_item_interface.cpp:492). A demo networks attribute values as 32 bits, so a
    /// string attribute only ever comes from the definition.
    /// </summary>
    /// <param name="definitionIndex">The item.</param>
    /// <param name="name">The attribute's name.</param>
    /// <returns>The value, or null.</returns>
    public string? DefinitionStringAttribute(int definitionIndex, string name) =>
        Inherited(definitionIndex, entry => entry.DefinitionAttributes.Find(
            each => string.Equals(each.Name, name, StringComparison.OrdinalIgnoreCase)).Value);

    /// <summary>`CALL_ATTRIB_HOOK_FLOAT` over an item definition's attributes.</summary>
    /// <param name="definitionIndex">The item, as <c>m_iItemDefinitionIndex</c> gives it.</param>
    /// <param name="attributeClass">The hook's class, such as <c>set_weapon_mode</c>.</param>
    /// <param name="initial">The value the caller starts from.</param>
    /// <returns>The value after every matching attribute is applied.</returns>
    /// <remarks>
    /// `ApplyAttribute` (`econ/attribute_manager.cpp:580`) by the attribute's <c>description_format</c>: a percentage
    /// multiplies, an additive or particle index adds, a lookup-table or killstreak index replaces, `value_is_or` sets bits,
    /// and any other format replaces. *Not carried:* the attributes an item's own instance sends on the wire.
    /// </remarks>
    public float HookValue(int definitionIndex, string attributeClass, float initial) =>
        Apply(DefinitionAttributesFor(definitionIndex), attributeClass, initial);

    /// <summary>`CEconItemAttributeIterator_ApplyAttributeFloat` over one list: each attribute of the hook's class, applied.</summary>
    /// <param name="attributes">The attributes, as one item or player resolves them.</param>
    /// <param name="attributeClass">The hook's class.</param>
    /// <param name="initial">The value so far.</param>
    /// <returns>The value after the list.</returns>
    public float Apply(IEnumerable<EconAttributeValue> attributes, string attributeClass, float initial)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(attributeClass);

        float value = initial;

        foreach (EconAttributeValue attribute in attributes)
        {
            value = ApplyOne(attribute, attributeClass, value);
        }

        return value;
    }

    /// <summary>The same over an item's resolved walk, which allocates nothing (B599).</summary>
    /// <param name="attributes">The attributes, as <see cref="EconAttributes.Iterate"/> walks them.</param>
    /// <param name="attributeClass">The hook's class.</param>
    /// <param name="initial">The value so far.</param>
    /// <returns>The value after the walk.</returns>
    public float Apply(ResolvedAttributes attributes, string attributeClass, float initial)
    {
        ArgumentNullException.ThrowIfNull(attributeClass);

        float value = initial;

        foreach (EconAttributeValue attribute in attributes)
        {
            value = ApplyOne(attribute, attributeClass, value);
        }

        return value;
    }

    /// <summary>One attribute of a hook's class applied by its <c>description_format</c>; any other attribute leaves the value.</summary>
    private float ApplyOne(EconAttributeValue attribute, string attributeClass, float value)
    {
        if (!_attributeClass.TryGetValue(attribute.DefinitionIndex, out string? named) ||
            !string.Equals(named, attributeClass, StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        float modifier = _attributeStoredAsInteger.Contains(attribute.DefinitionIndex) ? attribute.AsInteger : attribute.Value;

        return _attributeFormat.GetValueOrDefault(attribute.DefinitionIndex) switch
        {
            "value_is_percentage" or "value_is_inverted_percentage" => value * modifier,
            "value_is_additive" or "value_is_additive_percentage" or "value_is_particle_index" => value + modifier,
            "value_is_or" => (int)value | (int)modifier,
            _ => modifier,
        };
    }

    /// <summary>Gathers definition attributes, nearest declaration winning per name.</summary>
    private void CollectDefinitionAttributes(
        Entry entry, List<EconAttributeValue> into, HashSet<string> seen, int remaining)
    {
        foreach ((string name, string value) in entry.DefinitionAttributes)
        {
            // Stryker disable once : removing TryGetValue leaves 'index' undeclared at line below, CS0165
            if (!seen.Add(name) || !_attributeIndexByName.TryGetValue(name, out int index))
            {
                continue;
            }

            if (_attributeStoredAsInteger.Contains(index))
            {
                // Stryker disable once : removing TryParse leaves 'integer' undeclared in body, CS0165
                if (int.TryParse(
                    value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer))
                {
                    into.Add(new EconAttributeValue(index, integer));
                }
            }
            // Stryker disable once : removing TryParse leaves 'number' undeclared in body, CS0165
            else if (float.TryParse(
                value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
            {
                into.Add(new EconAttributeValue(index, BitConverter.SingleToInt32Bits(number)));
            }
        }

        if (remaining <= 0)
        {
            return;
        }

        foreach (string name in entry.Prefabs)
        {
            if (_prefabs.TryGetValue(name, out Entry? prefab))
            {
                CollectDefinitionAttributes(prefab, into, seen, remaining - 1);
            }
        }
    }

    /// <summary>An item's replacement for one of its weapon's sounds, or null.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <param name="team">The listener's team — TF2's own numbering, red 2 and blue 3.</param>
    /// <param name="weaponSound">A `WeaponSound_t`: `SPECIAL1` is 11.</param>
    /// <returns>The sound script key the item names, or null when it names none.</returns>
    /// <remarks>
    /// **`CEconItemDefinition::GetWeaponReplacementSound`** (`econ_item_schema.h:2223`), with
    /// `GetBestVisualTeamData` (`:2240`) choosing the block: a team with its own `visuals_red`/`visuals_blu` uses
    /// that block alone, any other team — spectator, whose section `g_TeamVisualSections` leaves null — uses the
    /// base `visuals`, and an item with no base block answers nothing. Prefabs contribute blocks and keys as the
    /// engine's prefab merge would, nearest definition winning.
    ///
    /// An index the schema does not know answers null here; the engine answers the `"default"` item there
    /// (`econ_item_schema.cpp:6694`), whose shipped definition has no visuals — the same answer.
    /// </remarks>
    public string? WeaponSoundReplacement(int definitionIndex, int team, int weaponSound)
    {
        return weaponSound < 0 || weaponSound >= WeaponSoundCategories.Length
            ? null
            : Visual(definitionIndex, team, WeaponSoundCategories[weaponSound]);
    }

    /// <summary>An item's muzzle flash particle system, or null — `CEconItemDefinition::GetMuzzleFlash( team )`.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <param name="team">The weapon's team.</param>
    /// <returns>The system the item names in place of its script's, or null when it names none.</returns>
    public string? MuzzleFlash(int definitionIndex, int team) => Visual(definitionIndex, team, "muzzle_flash");

    /// <summary>
    /// The material an item's model is drawn with in place of its own, or null — `CEconItemDefinition::GetMaterialOverride(
    /// team )` (`econ_item_schema.h:1966-1976`), which `CEconEntity::OnDataChanged` takes when no paint kit names one.
    /// </summary>
    /// <param name="definitionIndex">The item.</param>
    /// <param name="team">The team whose visuals block answers.</param>
    /// <returns>A VMT path, or null.</returns>
    public string? MaterialOverride(int definitionIndex, int team) => Visual(definitionIndex, team, MaterialOverrideKey);

    private const string MaterialOverrideKey = "material_override";

    private const string RemappedKey = "remapped_item_def_index";

    /// <summary>The attribute naming an item's paint kit, `GetPaintKitDefIndex`'s (econ_item_interface.h:501-516).</summary>
    public const string PaintKitAttribute = "paintkit_proto_def_index";

    /// <summary>
    /// `GetRemappedItemDefIndex()` (`econ_item_schema.h:1252`): the item `remapped_item_def_index` names (`:3273`, resolved
    /// by name at `:3404-3410`), else the item's own index.
    /// </summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>The index a paint kit recipe is looked up for.</returns>
    public int RemappedDefinitionIndex(int definitionIndex) =>
        Inherited(definitionIndex, entry => entry.Keys.GetValueOrDefault(RemappedKey)) is { } name
        && _itemIndexByName.TryGetValue(name, out int remapped)
            ? remapped
            : definitionIndex;

    /// <summary>
    /// The system an item's `custom_particlesystem` names, or null — the `iCustomType == 1` attached particle
    /// `CWeaponMedigun::UpdateEffects` creates beside its beam (`tf_weapon_medigun.cpp:2469`).
    /// </summary>
    /// <param name="definitionIndex">The item.</param>
    /// <param name="team">The weapon's team.</param>
    /// <returns>The system, such as <c>medicgun_beam_attrib_overheal_red</c>.</returns>
    public string? CustomParticle(int definitionIndex, int team) => Visual(definitionIndex, team, CustomParticleKey);

    /// <summary>`anim_slot "FORCE_NOT_USED"` (tf_item_schema.cpp:1018): the item never drives the player's animation.</summary>
    public const int AnimSlotNotUsed = -2;

    /// <summary>`g_szWeaponTypeSubstrings` (tf_item_schema.cpp:1591), indexed by `TF_WPN_TYPE_*`.</summary>
    public static IReadOnlyList<string> WeaponTypeSubstrings { get; } =
    [
        "PRIMARY", "SECONDARY", "MELEE", "GRENADE", "BUILDING", "PDA", "ITEM1", "ITEM2", "HEAD", "MISC",
        "MELEE_ALLCLASS", "SECONDARY2", "PRIMARY2", "ITEM3", "ITEM4", "PASSTIME_BALL",
    ];

    /// <summary>`GetAnimSlot()`: `m_iAnimationSlot`, -1 by default (tf_item_schema.cpp:893, :1015-1026).</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>A `TF_WPN_TYPE_*`, <see cref="AnimSlotNotUsed"/>, or -1.</returns>
    /// <remarks>
    /// **An item's own EMPTY `anim_slot` hides its prefab's** (B105): the merge sets the item's value over the prefab's
    /// (econ_item_schema.cpp:2909) and `if ( pszAnimSlot &amp;&amp; pszAnimSlot[0] )` (tf_item_schema.cpp:1016) then
    /// skips it, leaving -1. The Half-Zatoichi is the shipped case — `"anim_slot" ""` over `weapon_sword`'s `item1` —
    /// so a soldier's katana takes his script's melee where the sword prefab would have made it ITEM1.
    /// </remarks>
    public int AnimSlot(int definitionIndex)
    {
        if (Inherited(definitionIndex, entry => entry.Keys.GetValueOrDefault("anim_slot"), emptyAnswers: true) is not
            { Length: > 0 } raw)
        {
            return -1;
        }

        if (string.Equals(raw, "FORCE_NOT_USED", StringComparison.OrdinalIgnoreCase))
        {
            return AnimSlotNotUsed;
        }

        // `StringFieldToInt` (econ_item.cpp:33): `Q_stricmp` against the table, -1 when none matches.
        for (int index = 0; index < WeaponTypeSubstrings.Count; index++)
        {
            if (string.Equals(raw, WeaponTypeSubstrings[index], StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>`IsWearableSlot` (tf_item_constants.h:131): head, misc, action, misc2 and the taunt slots.</summary>
    /// <param name="slot">A loadout slot.</param>
    /// <returns>Whether an item there is worn.</returns>
    public static bool IsWearableSlot(int slot) => slot is LoadoutSlotHead or LoadoutSlotMisc or LoadoutSlotAction or LoadoutSlotMisc2 or LoadoutSlotTaunt;

    /// <summary><c>LOADOUT_POSITION_MISC2</c> (<c>tf_item_constants.h:68</c>).</summary>
    public const int LoadoutSlotMisc2 = 10;

    /// <summary>`CTFItemDefinition::IsAWearable` (tf_item_schema.cpp:1287).</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>Whether it is worn rather than wielded.</returns>
    public bool IsAWearable(int definitionIndex) =>
        (IsWearableSlot(DefaultLoadoutSlot(definitionIndex)) && InheritedInt(definitionIndex, "act_as_weapon") == 0)
        || InheritedInt(definitionIndex, "act_as_wearable") != 0;

    /// <summary>`m_pKVItem->GetInt( key, 0 )` on the item with its prefabs folded in.</summary>
    private int InheritedInt(int definitionIndex, string key) =>
        GetInt(Inherited(definitionIndex, entry => entry.Keys.GetValueOrDefault(key)), 0);

    /// <summary>`GetWorldDisplayModel()`: `model_world` (econ_item_schema.cpp:3160), or null.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>The path; empty when the nearest `model_world` is <c>""</c>; or null when none is declared.</returns>
    /// <remarks>
    /// **An item's own "" is its answer** (B105): `GetString( "model_world", NULL )` returns it as it is and both readers
    /// test the pointer — `CTFWeaponBase::GetWorldModel` (tf_weaponbase.cpp:686) and the loadout panel
    /// (tf_playermodelpanel.cpp:1024-1025) — so it wins over `model_player` and names no model. The shipped file declares none.
    /// </remarks>
    public string? WorldDisplayModel(int definitionIndex) =>
        Inherited(definitionIndex, entry => entry.Keys.GetValueOrDefault("model_world"), emptyAnswers: true);

    /// <summary>`GetExtraWearableModel()`: `extra_wearable` (econ_item_schema.cpp:3161), or null.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>The path, or null.</returns>
    public string? ExtraWearableModel(int definitionIndex) => Inherited(definitionIndex, entry => entry.Keys.GetValueOrDefault("extra_wearable"));

    /// <summary>`GetExtraWearableViewModel()`: `extra_wearable_vm` (econ_item_schema.cpp:3162), or null.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>The path, or null.</returns>
    public string? ExtraWearableViewModel(int definitionIndex) => Inherited(definitionIndex, entry => entry.Keys.GetValueOrDefault("extra_wearable_vm"));

    /// <summary>`GetNumStyles()` (econ_item_schema.h:1683): the base visuals block's style count.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>The count.</returns>
    public int StyleCount(int definitionIndex) => StylesOf(definitionIndex)?.Count ?? 0;

    /// <summary>
    /// `CEconItemView::GetPlayerDisplayModel( iClass, iTeam )` (econ_item_view.cpp:924): the style's per-class model
    /// (`CTFStyleInfo::GetPlayerDisplayModel`, tf_item_schema.cpp:1253, blue only where set), its `model_player`, then
    /// <see cref="ModelFor"/>.
    /// </summary>
    /// <param name="definitionIndex">The item.</param>
    /// <param name="playerClass">The class.</param>
    /// <param name="team">The team.</param>
    /// <param name="style">`GetItemStyle()`, or null for `INVALID_STYLE_INDEX`.</param>
    /// <returns>The path, or null.</returns>
    public string? PlayerDisplayModel(int definitionIndex, int playerClass, int team, int? style)
    {
        if (StyleAt(definitionIndex, style) is { } picked)
        {
            if (picked.ModelPlayer is { } common)
            {
                return common;
            }

            if (team == BluTeam && picked.Blue is { } blue && ExpandPerClass(blue, playerClass) is { Length: > 0 } blueModel)
            {
                return blueModel;
            }

            if (picked.Red is { } red && ExpandPerClass(red, playerClass) is { } redModel)
            {
                return redModel;
            }
        }

        return ModelFor(definitionIndex, playerClass);
    }

    /// <summary>
    /// `CEconItemView::GetSkin( iTeam )` (econ_item_view.cpp:975): 0 outside the five visuals sections; with styles the
    /// style's skin or `default_skin`; else the best visuals block's `skin` (-1 unset), else `default_skin` (-1).
    /// </summary>
    /// <param name="definitionIndex">The item.</param>
    /// <param name="team">The team.</param>
    /// <param name="style">`GetItemStyle()`, or null for `INVALID_STYLE_INDEX`.</param>
    /// <returns>The skin, or -1 for "use the team skin".</returns>
    public int Skin(int definitionIndex, int team, int? style)
    {
        if (team is < 0 or >= TeamVisualSections)
        {
            return 0;
        }

        int defaultSkin = GetInt(Inherited(definitionIndex, entry => entry.Keys.GetValueOrDefault("default_skin")), -1);

        if (StyleCount(definitionIndex) > 0)
        {
            return StyleAt(definitionIndex, style) is { } picked ? picked.Skin(team) : defaultSkin;
        }

        if (!_items.TryGetValue(definitionIndex, out Entry? item) || BestVisualSection(item, team) is not { } section)
        {
            return defaultSkin;
        }

        return GetInt(Search(item, entry => entry.VisualKeys.GetValueOrDefault(section + "/skin"), LongestChain), -1);
    }

    /// <summary>`UsesPerClassBodygroups( iTeam )` (econ_item_schema.h:2191): the best block's `use_per_class_bodygroups`.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <param name="team">The team.</param>
    /// <returns>Whether bodygroup 1 selects the class.</returns>
    public bool UsesPerClassBodygroups(int definitionIndex, int team) =>
        _items.TryGetValue(definitionIndex, out Entry? item)
        && BestVisualSection(item, team) is { } section
        && GetInt(Search(item, entry => entry.VisualKeys.GetValueOrDefault(section + "/use_per_class_bodygroups"), LongestChain), 0) != 0;

    /// <summary>`GetModifiedBodyGroup( 0, i, state )` (econ_item_schema.h:2057) over the BASE visuals block, prefabs included.</summary>
    private Dictionary<string, int> BaseBodygroups(int definitionIndex)
    {
        Dictionary<string, int> found = new(StringComparer.OrdinalIgnoreCase);

        if (_items.TryGetValue(definitionIndex, out Entry? item))
        {
            Walk(item, LongestChain, entry =>
            {
                foreach ((string name, int state) in entry.BasePlayerBodygroups)
                {
                    _ = found.TryAdd(name, state);
                }
            });
        }

        return found;
    }

    /// <summary>`CEconStyleInfo::GetAdditionalHideBodygroups()` (econ_item_schema.h:1009) of one style.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <param name="style">The style, or null.</param>
    /// <returns>The names, empty without that style.</returns>
    public IReadOnlyList<string> StyleHiddenBodygroups(int definitionIndex, int? style) =>
        StyleAt(definitionIndex, style)?.HideBodygroups ?? [];

    /// <summary>`GetPlayerPoseParameters( iTeam, i )` (econ_item_schema.h:1878): the best block's `player_poseparam`.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <param name="team">The team.</param>
    /// <returns>Each name and value, in schema order.</returns>
    public IReadOnlyList<(string Name, float Value)> PlayerPoseParametersFor(int definitionIndex, int team)
    {
        if (!_items.TryGetValue(definitionIndex, out Entry? item) || BestVisualSection(item, team) is not { } section)
        {
            return [];
        }

        List<(string Name, float Value)>? found = null;

        Walk(item, LongestChain, entry =>
        {
            if (found is null && entry.PlayerPoseParams.TryGetValue(section, out List<(string Name, float Value)>? poses))
            {
                found = poses;
            }
        });

        return found ?? [];
    }

    /// <summary>
    /// `IsTauntItem` (tf_playermodelpanel.cpp:41): a taunt slot, then `GetTauntData()` or an `animation_*` entry for
    /// `taunt_concept` in the best visuals block.
    /// </summary>
    /// <param name="definitionIndex">The item.</param>
    /// <param name="team">The team.</param>
    /// <param name="playerClass">The class.</param>
    /// <returns>Whether the model panel plays it as a taunt.</returns>
    public bool IsTauntItem(int definitionIndex, int team, int playerClass)
    {
        if (LoadoutSlot(definitionIndex, playerClass) != LoadoutSlotTaunt || !_items.TryGetValue(definitionIndex, out Entry? item))
        {
            return false;
        }

        if (Search(item, entry => entry.HasTauntData ? "taunt" : null, LongestChain) is not null)
        {
            return true;
        }

        return BestVisualSection(item, team) is { } section
            && Search(item, entry => entry.TauntConceptBlocks.Contains(section) ? "taunt_concept" : null, LongestChain) is not null;
    }

    /// <summary>
    /// `GetDefaultBodygroupStateMap()` (econ_item_schema.h:2620): every `player_bodygroups` name an item declares, each
    /// at 0 — "the schemas are all authored assuming that the default is 0" (`AssignDefaultBodygroupState`, :5096).
    /// </summary>
    public IReadOnlyDictionary<string, int> DefaultBodygroupStates => _defaultBodygroupStates ??= BuildDefaultBodygroupStates();

    private Dictionary<string, int>? _defaultBodygroupStates;

    private Dictionary<string, int> BuildDefaultBodygroupStates()
    {
        Dictionary<string, int> states = new(StringComparer.OrdinalIgnoreCase);

        foreach (Entry item in _items.Values)
        {
            Walk(item, LongestChain, entry =>
            {
                foreach (string name in entry.PlayerBodygroups.Keys)
                {
                    states[name] = 0;
                }
            });
        }

        return states;
    }

    /// <summary>
    /// `CTFInventoryManager::GetBaseItemForClass( iClass, iSlot )` (tf_item_inventory.cpp:656): the first base item by
    /// definition index (`GenerateBaseItems`, :245-251) whose slot for that class is <paramref name="slot"/>, or null —
    /// the invalid `m_pDefaultItem` — for a slot from head up (:705).
    /// </summary>
    /// <param name="playerClass">The class.</param>
    /// <param name="slot">The loadout slot.</param>
    /// <returns>A definition index, or null.</returns>
    /// <remarks>The action slot's spellbook/grappling hook/canteen branch (:672-703) is reached only by a taunt's forced
    /// slot, which the panel cannot run (see <c>TfPlayerModelPanel.SwitchHeldItemTo</c>).</remarks>
    public int? BaseItemForClass(int playerClass, int slot)
    {
        if (playerClass < 1 || playerClass > 10 || slot < 0 || slot >= LoadoutSlotHead)
        {
            return null;
        }

        int? found = null;

        foreach ((int index, Entry entry) in _items)
        {
            if (entry.IsBaseItem && (found is null || index < found) && LoadoutSlot(index, playerClass) == slot)
            {
                found = index;
            }
        }

        return found;
    }

    /// <summary>`CEconItemDefinition::GetActivityOverride( iTeam, baseAct )` (econ_item_schema.cpp:3567-3595).</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <param name="team">The holder's team.</param>
    /// <param name="activity">The activity the weapon's table left.</param>
    /// <returns>The best block's `animation_replacement` for it, or the activity itself.</returns>
    /// <remarks>
    /// **The best block for the team** — `GetNumAnimations` and `GetAnimationData` go through `GetBestVisualTeamData`
    /// (`econ_item_schema.h:1831-1854`) — and the nearest declaration in the prefab chain, as every visual here is
    /// read. Not checked: `ActivityList_IndexForName( pszReplacement ) &gt; 0`, which drops a replacement naming an
    /// activity the game never registered; none of the shipped replacements was found to need it.
    /// </remarks>
    public string ActivityOverride(int definitionIndex, int team, string activity) =>
        ActivityReplacements(definitionIndex, team) is { } rows && rows.TryGetValue(activity, out string? replacement)
            ? replacement
            : activity;

    /// <summary>Every `animation_replacement` row <see cref="ActivityOverride"/> answers from, or null for none.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <param name="team">The holder's team.</param>
    /// <returns>Activity to replacement, the nearest declaration in the prefab chain winning; null when empty.</returns>
    public IReadOnlyDictionary<string, string>? ActivityReplacements(int definitionIndex, int team)
    {
        if (!_items.TryGetValue(definitionIndex, out Entry? item) || BestVisualSection(item, team) is not { } section)
        {
            return null;
        }

        Dictionary<string, string> found = new(StringComparer.OrdinalIgnoreCase);

        Walk(item, LongestChain, entry =>
        {
            if (entry.ActivityReplacements.TryGetValue(section, out Dictionary<string, string>? rows))
            {
                foreach ((string from, string to) in rows)
                {
                    _ = found.TryAdd(from, to);
                }
            }
        });

        return found.Count > 0 ? found : null;
    }

    /// <summary>One block's `animation_replacement` rows, created on first use.</summary>
    private static Dictionary<string, string> Replacements(Entry entry, string block)
    {
        if (!entry.ActivityReplacements.TryGetValue(block, out Dictionary<string, string>? rows))
        {
            rows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            entry.ActivityReplacements[block] = rows;
        }

        return rows;
    }

    /// <summary>`TEAM_VISUAL_SECTIONS` (econ_item_schema.cpp:77).</summary>
    private const int TeamVisualSections = 5;

    /// <summary>`GetBestVisualTeamData` (econ_item_schema.h:2240) as a block name, or null when that block is absent.</summary>
    private string? BestVisualSection(Entry item, int team)
    {
        string? section = team switch
        {
            0 => string.Empty,
            RedTeam => "red",
            BluTeam => "blu",
            MvmBossTeam => "mvm_boss",
            _ => null,
        };

        if (team is < 0 or >= TeamVisualSections || (team > 0 && (section is null || !Declares(item, section))))
        {
            section = string.Empty;
        }

        return Declares(item, section!) ? section : null;
    }

    /// <summary>The nearest styles block in an item's chain.</summary>
    private List<ItemStyle>? StylesOf(int definitionIndex)
    {
        List<ItemStyle>? found = null;

        if (_items.TryGetValue(definitionIndex, out Entry? item))
        {
            Walk(item, LongestChain, entry => found ??= entry.Styles);
        }

        return found;
    }

    /// <summary>`CEconStyleInfo::GetBodygroupName()` and `GetBodygroupSubmodelIndex()` of the item's style, or null without one.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <param name="style">`GetItemStyle()`, or null for `INVALID_STYLE_INDEX`.</param>
    /// <returns>The bodygroup and the submodel to set, or null.</returns>
    public (string Name, int Submodel)? StyleBodygroup(int definitionIndex, int? style) =>
        StyleAt(definitionIndex, style) is { BodygroupName: { } name } picked ? (name, picked.BodygroupSubmodelIndex) : null;

    /// <summary>`GetStyleInfo( unStyle )` (econ_item_schema.h:1722): null for an invalid index.</summary>
    private ItemStyle? StyleAt(int definitionIndex, int? style) =>
        style is { } index && StylesOf(definitionIndex) is { } styles && index >= 0 && index < styles.Count ? styles[index] : null;

    /// <summary>Visits an entry and then its prefabs, depth first, nearest first.</summary>
    private void Walk(Entry entry, int remaining, Action<Entry> visit)
    {
        visit(entry);

        if (remaining <= 0)
        {
            return;
        }

        foreach (string name in entry.Prefabs)
        {
            if (_prefabs.TryGetValue(name, out Entry? prefab))
            {
                Walk(prefab, remaining - 1, visit);
            }
        }
    }

    /// <summary>`KeyValues::GetInt`: the value as an integer, truncating a float, 0 for text, the default when absent.</summary>
    private static int GetInt(string? value, int fallback)
    {
        if (value is null)
        {
            return fallback;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer))
        {
            return integer;
        }

        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) ? (int)number : 0;
    }

    /// <summary>`InitPerClassStringArray` (tf_item_schema.cpp:489) for one class: its own entry, else `basename` expanded;
    /// class 0 takes the first class that has one (:542).</summary>
    private static string? ExpandPerClass(PerClassBlock block, int playerClass)
    {
        if (playerClass <= 0 || playerClass >= ClassNames.Length)
        {
            for (int candidate = 1; candidate < ClassNames.Length; candidate++)
            {
                if (ExpandPerClass(block, candidate) is { } first)
                {
                    return first;
                }
            }

            return null;
        }

        if (block.PerClass.TryGetValue(ClassNames[playerClass], out string? named) && named.Length > 0)
        {
            return named;
        }

        return block.BaseName?.Replace("%s", playerClass == Demoman ? "demo" : ClassNames[playerClass], StringComparison.Ordinal);
    }

    /// <summary>The visuals block naming an item's custom particle.</summary>
    private const string CustomParticleKey = "custom_particlesystem";

    /// <summary>`use_visualsblock_as_base` (econ_item_schema.cpp:2484).</summary>
    private const string VisualsBaseKey = "use_visualsblock_as_base";

    /// <summary>
    /// <c>GetNumAttachedParticles( iTeam )</c> and <c>GetAttachedParticleData</c> (<c>econ_item_schema.h:1931-1960</c>): the
    /// particle systems of the visuals block <c>GetBestVisualTeamData</c> picks, prefabs merged (B69).
    /// </summary>
    /// <param name="definitionIndex">The item.</param>
    /// <param name="team">The entity's <c>GetTeamNumber()</c>, or null for the base block.</param>
    /// <returns>Each system, in the order the block declares them.</returns>
    /// <remarks>
    /// **Merged as <c>attached_models</c> are** — <c>MergeDefinitionPrefab</c>'s order, a same-named child replaced in place
    /// (<c>econ_item_schema.cpp:2897-2967</c>). A block naming <c>use_visualsblock_as_base</c> starts from that block's list
    /// (<c>:2484-2497</c>); TF's team blocks are parsed after the base, which is the only block a shipped one names.
    /// </remarks>
    public IReadOnlyList<AttachedParticle> AttachedParticlesFor(int definitionIndex, int? team)
    {
        if (!_items.TryGetValue(definitionIndex, out Entry? item) || BestVisualSection(item, team ?? 0) is not { } section)
        {
            return [];
        }

        List<AttachedParticle> particles = [];

        if (section.Length > 0 &&
            Inherited(definitionIndex, entry => entry.VisualKeys.GetValueOrDefault(section + "/" + VisualsBaseKey)) is { } baseName &&
            baseName.Equals("visuals", StringComparison.OrdinalIgnoreCase))
        {
            particles.AddRange(ParticlesOf(item, string.Empty));
        }

        particles.AddRange(ParticlesOf(item, section));

        return particles;
    }

    /// <summary>One block's particle systems through the prefab chain, a same-named child replaced in place.</summary>
    private List<AttachedParticle> ParticlesOf(Entry item, string section)
    {
        List<(string Key, AttachedParticle Particle)> merged = [];

        foreach (Entry level in MergeOrder(item, LongestChain))
        {
            foreach ((string team, string key, AttachedParticle particle) in level.AttachedParticles)
            {
                if (!string.Equals(team, section, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int existing = merged.FindIndex(each => string.Equals(each.Key, key, StringComparison.OrdinalIgnoreCase));

                if (existing >= 0)
                {
                    merged[existing] = (key, particle);
                }
                else
                {
                    merged.Add((key, particle));
                }
            }
        }

        return [.. merged.Select(each => each.Particle)];
    }

    /// <summary><c>CEconStyleInfo::UseSmokeParticleEffect()</c> of the item's style, or null without style info (B69).</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <param name="style"><c>GetItemStyle()</c>, or null for <c>INVALID_STYLE_INDEX</c>.</param>
    /// <returns><c>use_smoke_particle_effect</c>, <c>GetBool( …, true )</c> (<c>econ_item_schema.cpp:2867</c>); null when
    /// <c>GetStyleInfo</c> answers null and the engine's rule does not run (<c>econ_entity.cpp:1563-1572</c>).</returns>
    public bool? StyleUsesSmokeParticleEffect(int definitionIndex, int? style) =>
        StyleAt(definitionIndex, style)?.UseSmokeParticleEffect;

    /// <summary>An item's weapon class — `item_class`, such as <c>tf_weapon_scattergun</c> — or null.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <returns>The class, which is also its weapon script's name.</returns>
    public string? ItemClass(int definitionIndex) => Inherited(definitionIndex, entry => entry.ItemClass);

    /// <summary><c>GetItemBaseName</c>: <c>GetString( "item_name", "" )</c> (econ_item_schema.cpp:3132), prefabs included.</summary>
    /// <returns>Empty for a definition without the key — never null, "to ensure we can sort" — and null for no definition.</returns>
    public string? ItemBaseName(int definitionIndex) =>
        _items.ContainsKey(definitionIndex) ? Inherited(definitionIndex, entry => entry.ItemName) ?? string.Empty : null;

    /// <summary><c>HasProperName</c>: <c>GetInt( "propername", 0 ) != 0</c> (econ_item_schema.cpp:3168), prefabs included.</summary>
    public bool HasProperName(int definitionIndex) =>
        int.TryParse(Inherited(definitionIndex, entry => entry.ProperName), NumberStyles.Integer, CultureInfo.InvariantCulture, out int proper)
        && proper != 0;

    /// <summary>One key from the visuals block `GetBestVisualTeamData` chooses for a team, prefabs included.</summary>
    /// <remarks>
    /// A team with its own `visuals_red`/`visuals_blu` uses that block alone; any other team the base `visuals`; an item
    /// with no base block answers nothing.
    /// </remarks>
    private string? Visual(int definitionIndex, int team, string key)
    {
        if (!_items.TryGetValue(definitionIndex, out Entry? item))
        {
            return null;
        }

        string section = team switch
        {
            RedTeam => "red",
            BluTeam => "blu",
            MvmBossTeam => "mvm_boss",
            _ => string.Empty,
        };

        if (!Declares(item, section))
        {
            section = string.Empty;
        }

        if (!Declares(item, section))
        {
            return null;
        }

        string wanted = section + "/" + key;

        return Search(item, entry => entry.WeaponSounds.GetValueOrDefault(wanted), LongestChain);
    }

    /// <summary>`TEAM_VISUAL_SECTIONS`' last: `visuals_mvm_boss`, MvM's giant robots.</summary>
    private const int MvmBossTeam = 4;

    /// <summary>Whether an item or its prefabs carry a tag — `CEconItemDefinition::HasEconTag` (B767).</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <param name="tag">The tag's name, such as <c>ghost_wearable</c>.</param>
    /// <returns>True when the item, or a prefab it inherits from, declares it.</returns>
    /// <remarks>Inherited as every key is: `RecursiveInheritKeyValues` merges a prefab's keys under the item's (econ_item_schema.cpp:2909).</remarks>
    public bool HasTag(int definitionIndex, string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        return _items.TryGetValue(definitionIndex, out Entry? item) && Tagged(item, tag, LongestChain);
    }

    private bool Tagged(Entry entry, string tag, int remaining)
    {
        if (entry.Tags.Contains(tag))
        {
            return true;
        }

        if (remaining <= 0)
        {
            return false;
        }

        foreach (string name in entry.Prefabs)
        {
            if (_prefabs.TryGetValue(name, out Entry? prefab) && Tagged(prefab, tag, remaining - 1))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether an item or its prefabs declare a visuals block.</summary>
    private bool Declares(Entry item, string section) => Declares(item, section, LongestChain);

    /// <summary>
    /// <see cref="Search"/>'s walk, written out: a lambda over <paramref name="section"/> was a closure per call, and this is
    /// asked for every worn item every frame (alloc-trace on f12, 2026-11-07).
    /// </summary>
    private bool Declares(Entry entry, string section, int remaining)
    {
        if (entry.VisualsSections.Contains(section))
        {
            return true;
        }

        if (remaining <= 0)
        {
            return false;
        }

        foreach (string name in entry.Prefabs)
        {
            if (_prefabs.TryGetValue(name, out Entry? prefab) && Declares(prefab, section, remaining - 1))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Searches an item and then its prefabs, in order, for the first answer.</summary>
    /// <param name="definitionIndex">The item.</param>
    /// <param name="ask">What each definition says, or null when it says nothing.</param>
    /// <param name="emptyAnswers">
    /// Whether an empty value is the nearest definition's answer rather than silence. It is, in the engine, for every
    /// key: `RecursiveInheritKeyValues` sets each of an item's own keys over its prefabs' whatever the value
    /// (econ_item_schema.cpp:2909, :2967). Opt-in here, per key: `anim_slot` and `model_player`, the two this port reads that
    /// the shipped file gives an empty value, and `model_world`, whose readers test the pointer and so take "" too (B105).
    /// </param>
    private string? Inherited(int definitionIndex, Func<Entry, string?> ask, bool emptyAnswers = false)
    {
        if (!_items.TryGetValue(definitionIndex, out Entry? item))
        {
            return null;
        }

        return Search(item, ask, LongestChain, emptyAnswers);
    }

    /// <summary>Depth-first through the prefab chain, nearest definition winning.</summary>
    private string? Search(Entry entry, Func<Entry, string?> ask, int remaining, bool emptyAnswers = false)
    {
        if (ask(entry) is { } answer && (emptyAnswers || answer.Length > 0))
        {
            return answer;
        }

        if (remaining <= 0)
        {
            return null;
        }

        foreach (string name in entry.Prefabs)
        {
            if (_prefabs.TryGetValue(name, out Entry? prefab) &&
                Search(prefab, ask, remaining - 1, emptyAnswers) is { } inherited)
            {
                return inherited;
            }
        }

        return null;
    }

    /// <summary>Starts an entry in whichever section the walk is in.</summary>
    private Entry? Begin(string section, string name)
    {
        if (string.Equals(section, "prefabs", StringComparison.OrdinalIgnoreCase))
        {
            Entry prefab = new();
            _prefabs[name] = prefab;
            return prefab;
        }

        // Stryker disable once : removing TryParse leaves 'index' undeclared after this if-block, CS0165
        if (!string.Equals(section, "items", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
        {
            // `items` carries a "default" entry alongside the numbered ones, and every other
            // section is of no interest here.
            return null;
        }

        Entry item = new();
        _items[index] = item;
        return item;
    }

    /// <summary>Records one key from an entry.</summary>
    private static void Apply(Entry entry, string key, string? value)
    {
        if (value is null)
        {
            return;
        }

        // Empty included: `ModelFor` asks for it, because an item's own `""` is its base model rather than a gap its
        // prefab fills (B105).
        if (string.Equals(key, "model_player", StringComparison.OrdinalIgnoreCase))
        {
            entry.Model = value;
            return;
        }

        // **An empty value is kept only here and among the scalar keys**, whose searches pass over it unless asked not
        // to — which `anim_slot` and `model_world` are, because an item's own `""` hides its prefab's value (B105).
        // Everywhere else it is dropped here, as it always was.
        if (value.Length == 0)
        {
            if (PanelKeys.Contains(key))
            {
                entry.Keys[key] = value;
            }

            return;
        }

        if (string.Equals(key, "prefab", StringComparison.OrdinalIgnoreCase))
        {
            // **Space separated, and the order is the search order.** The schema writes
            // `"prefab" "base_hat valve_promo"`, so a single-name reader takes the first and
            // silently loses every attribute the second would have supplied.
            entry.Prefabs.AddRange(value.Split(
                ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            return;
        }

        if (string.Equals(key, "attach_to_hands", StringComparison.OrdinalIgnoreCase))
        {
            entry.AttachToHands = value != "0";
            return;
        }

        if (string.Equals(key, "item_class", StringComparison.OrdinalIgnoreCase))
        {
            entry.ItemClass = value;
            return;
        }

        if (string.Equals(key, "item_name", StringComparison.OrdinalIgnoreCase))
        {
            entry.ItemName = value;
            return;
        }

        if (string.Equals(key, "propername", StringComparison.OrdinalIgnoreCase))
        {
            entry.ProperName = value;
            return;
        }

        if (string.Equals(key, "drop_type", StringComparison.OrdinalIgnoreCase))
        {
            entry.DropType = value;
            return;
        }

        if (string.Equals(key, "item_slot", StringComparison.OrdinalIgnoreCase))
        {
            entry.LoadoutSlot = value;
            return;
        }

        if (string.Equals(key, "item_rarity", StringComparison.OrdinalIgnoreCase))
        {
            entry.ItemRarity = value;
            return;
        }

        if (string.Equals(key, "hide_bodygroups_deployed_only", StringComparison.OrdinalIgnoreCase))
        {
            entry.HideBodygroupsDeployedOnly = value != "0";
            return;
        }

        // Stryker disable once : removing TryParse leaves 'vision' undeclared in body, CS0165
        if (string.Equals(key, "vision_filter_flags", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(
                value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int vision))
        {
            entry.VisionFilterFlags = vision;
            return;
        }

        if (string.Equals(key, "baseitem", StringComparison.OrdinalIgnoreCase))
        {
            entry.IsBaseItem = value != "0";
            return;
        }

        if (PanelKeys.Contains(key))
        {
            entry.Keys[key] = value;
        }
    }

    /// <summary>The scalar item keys the model panel's calls read (econ_item_schema.cpp:3159-3171, tf_item_schema.cpp:1015).</summary>
    private static readonly HashSet<string> PanelKeys = new(
        [
            "model_world", "extra_wearable", "extra_wearable_vm", "anim_slot", "act_as_wearable", "act_as_weapon", "default_skin",
            "particle_suffix", "flip_viewmodel",

            // What the item panel and its description read (econ_item_schema.cpp:3133-3155).
            "item_type_name", "item_description", "image_inventory", "image_inventory_overlay", "image_inventory_overlay2",
            "image_inventory_pos_x", "image_inventory_pos_y", "image_inventory_size_w", "image_inventory_size_h", "holiday_restriction",
            "collection_reference", "event_desc_footer", "expiration_date", "is_tool", "set_item_remap", "hidden",

            // The item a war paint's recipe is looked up for (econ_item_schema.cpp:3273).
            RemappedKey,
        ],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>One scalar key of a style (tf_item_schema.cpp:1154-1160, econ_item_schema.cpp:2831).</summary>
    private static void ApplyStyle(ItemStyle style, string key, string value)
    {
        int number = GetInt(value, 0);

        switch (key.ToUpperInvariant())
        {
            case "SKIN":
                style.CommonSkin = number == -1 ? null : number;
                break;
            case "SKIN_RED":
                style.SkinRed = number;
                break;
            case "SKIN_BLU":
                style.SkinBlu = number;
                break;
            case "MODEL_PLAYER":
                style.ModelPlayer = value;
                break;
            case "NAME":
                style.Name = value;
                break;
            case "IMAGE_INVENTORY":
                style.InventoryImage = value;
                break;
            case "USE_SMOKE_PARTICLE_EFFECT":
                // `GetBool( "use_smoke_particle_effect", true )` (econ_item_schema.cpp:2867): `GetInt() != 0`.
                style.UseSmokeParticleEffect = number != 0;
                break;
            default:
                break;
        }
    }
}
