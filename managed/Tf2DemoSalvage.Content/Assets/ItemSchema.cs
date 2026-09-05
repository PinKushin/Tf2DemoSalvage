using System;
using System.Collections.Generic;
using System.Globalization;

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
public readonly record struct AttachedModel(
    string Model, int DisplayFlags, string Team, bool Festive)
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

        /// <summary>Its <c>model_player</c>, or null.</summary>
        public string? Model { get; set; }

        /// <summary>Its <c>attach_to_hands</c>, or null when it does not say.</summary>
        public bool? AttachToHands { get; set; }

        /// <summary>Its <c>drop_type</c>, or null when it does not say.</summary>
        public string? DropType { get; set; }

        /// <summary>Its <c>item_slot</c>, or null when it does not say.</summary>
        public string? LoadoutSlot { get; set; }

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

        /// <summary>Its <c>attached_models</c> and <c>attached_models_festive</c>, in schema order.</summary>
        public List<AttachedModel> AttachedModels { get; } = [];

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

        /// <summary>A wearer's body part addressed by NUMBER, or -1 (B353).</summary>
        /// <remarks>
        /// **`wm_bodygroup_override`, the one arm of `UpdateBodygroups` that uses no name**
        /// (<c>econ_entity.cpp:2083</c>). Two shipped items declare it — the Purity Fist and the
        /// Short Circuit — and both replace a hand with a robot arm.
        ///
        /// **-1, not 0, and the default is the load-bearing part.** The engine's guard is
        /// `iBodyOverride &gt; -1 &amp;&amp; iBodyStateOverride &gt; -1` against fields initialised to -1
        /// (<c>econ_item_schema.h:1065</c>), so a reader defaulting them to 0 satisfies it for every
        /// item in the schema and sets part 0 to 0 on every player — putting back the hair that
        /// item 30700's `hat` entry had just removed.
        /// </remarks>
        public int WorldModelBodygroupOverride { get; set; } = -1;

        /// <summary>Which alternative <see cref="WorldModelBodygroupOverride"/> takes, or -1.</summary>
        /// <remarks>
        /// **Both halves are required**, which is why this is tracked separately rather than folded
        /// into the pair: `wm_bodygroup_override` without `wm_bodygroup_state_override` is a real
        /// shape in the schema and the engine ignores it.
        /// </remarks>
        public int WorldModelBodygroupStateOverride { get; set; } = -1;

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
    }

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
        bool attachedIsFestive = false;
        string attachedModel = string.Empty;
        int attachedFlags = AttachedModel.MaskAll;

        // The top-level `attributes` section's walk: which definition index is open.
        int attributeDefinition = -1;

        // An item's two definition-attribute forms: the flat `static_attrs` pairs, and the named
        // blocks whose value arrives a level deeper.
        bool inStaticAttrs = false;
        bool inItemAttributes = false;
        string pendingAttributeName = string.Empty;

        KeyValuesReader.Read(schema, (key, value, depth) =>
        {
            switch (depth)
            {
                case 1:
                    section = key;
                    entry = null;
                    inPerClass = false;
                    inVisuals = false;
                    inAttached = false;
                    inStaticAttrs = false;
                    inItemAttributes = false;
                    attributeDefinition = -1;
                    break;

                case 2:
                    inPerClass = false;
                    inVisuals = false;
                    inAttached = false;
                    inStaticAttrs = false;
                    inItemAttributes = false;
                    entry = read.Begin(section, key);

                    // The top-level `attributes` section: each child is one definition, keyed by
                    // its index as text — the same spelling `instancebaseline` entries use.
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

                    break;

                case 3 when entry is not null:
                    inPerClass =
                        value is null &&
                        string.Equals(key, "model_player_per_class", StringComparison.OrdinalIgnoreCase);

                    // `visuals`, `visuals_red`, `visuals_blu`. The suffix IS the team.
                    inVisuals = value is null
                        && key.StartsWith("visuals", StringComparison.OrdinalIgnoreCase);

                    visualsTeam = inVisuals && key.Length > "visuals".Length
                        ? key["visuals_".Length..]
                        : string.Empty;

                    inAttached = false;

                    // The two definition-attribute forms open here; every other depth-3 key closes
                    // both, so a stray pair after the block cannot be swallowed into it.
                    inStaticAttrs = value is null
                        && string.Equals(key, "static_attrs", StringComparison.OrdinalIgnoreCase);

                    inItemAttributes = value is null
                        && string.Equals(key, "attributes", StringComparison.OrdinalIgnoreCase);

                    Apply(entry, key, value);
                    break;

                // **A scalar inside `visuals`, addressing a part by number rather than by name**
                // (B353). Read only for the WORLD model: `vm_bodygroup_override` sets a part on the
                // wearer's own view model (`econ_entity.cpp:2091`), which a demo viewer drawing
                // another player never has — and the Purity Fist declares both pairs with the same
                // numbers, so a reader keyed to the wrong prefix passes every shipped case.
                case 4 when entry is not null && inVisuals && value is not null
                    && key.StartsWith("wm_bodygroup", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(
                        value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int part):

                    if (key.Equals("wm_bodygroup_override", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.WorldModelBodygroupOverride = part;
                    }
                    else if (key.Equals(
                        "wm_bodygroup_state_override", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.WorldModelBodygroupStateOverride = part;
                    }

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

                    break;

                // **`"hat" "1"` — a body part's name and the state to put it in.** The engine reads
                // the pair through `GetModifiedBodyGroup`, which hands back both, and applies it
                // only when the value matches the pass it is running (`econ_entity.cpp:2046`).
                case 5 when entry is not null && inBodygroups && value is not null
                    && int.TryParse(
                        value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int state):
                    entry.PlayerBodygroups[key] = state;
                    break;

                // Each numbered child of the block is one attachment. Its fields arrive next, so
                // the pending record is reset here and committed when the following one starts or
                // the file ends — which is why `model` is written straight into the list below.
                case 5 when entry is not null && inAttached && value is null:
                    attachedModel = string.Empty;
                    attachedFlags = AttachedModel.MaskAll;
                    break;

                case 6 when entry is not null && inAttached && value is not null:
                    if (string.Equals(key, "model", StringComparison.OrdinalIgnoreCase))
                    {
                        attachedModel = value;

                        entry.AttachedModels.Add(new AttachedModel(
                            attachedModel, attachedFlags, visualsTeam, attachedIsFestive));
                    }
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
                                attachedModel, attachedFlags, visualsTeam, attachedIsFestive);
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
    /// <returns>A model path, or <c>null</c> when the schema names none.</returns>
    /// <remarks>
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
                ?? Inherited(definitionIndex, entry => entry.Model);
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

        return Inherited(definitionIndex, entry => entry.Model);
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

    /// <summary>The stock item's model for a weapon entity class, such as <c>tf_weapon_wrench</c>.</summary>
    /// <param name="itemClass">The weapon's entity class, from its script name.</param>
    /// <param name="playerClass">Whose hands, as <c>m_iClass</c> gives it.</param>
    /// <returns>A model path, or <c>null</c> when no base item claims that class.</returns>
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
        if (!_items.TryGetValue(definitionIndex, out Entry? item))
        {
            return [];
        }

        List<AttachedModel> found = [];

        Collect(item, found, LongestChain);

        if (found.Count == 0)
        {
            return [];
        }

        string wanted = team switch
        {
            RedTeam => "red",
            BluTeam => "blu",
            _ => string.Empty,
        };

        List<AttachedModel> kept = [];

        foreach (AttachedModel attached in found)
        {
            if (attached.Festive && !festivized)
            {
                continue;
            }

            // An untagged block applies to both sides; a tagged one only to its own. An unknown
            // team therefore takes the untagged blocks and nothing else, which is the honest answer
            // rather than guessing a side.
            if (attached.Team.Length > 0
                && !string.Equals(attached.Team, wanted, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            kept.Add(attached);
        }

        return kept;
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
    public IReadOnlyDictionary<string, int> PlayerBodygroupsFor(int definitionIndex)
    {
        if (!_items.TryGetValue(definitionIndex, out Entry? item))
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, int> found = new(StringComparer.OrdinalIgnoreCase);

        CollectBodygroups(item, found, LongestChain);

        return found;
    }

    /// <summary>Gathers an entry's bodygroups, then its prefabs' where it is silent.</summary>
    /// <remarks>
    /// **Nearest definition wins, so the entry is added FIRST and a prefab may not overwrite it.**
    /// The opposite order would let a class prefab's `hat` state override a cosmetic that
    /// deliberately restores the part.
    /// </remarks>
    private void CollectBodygroups(Entry entry, Dictionary<string, int> into, int remaining)
    {
        foreach ((string name, int state) in entry.PlayerBodygroups)
        {
            _ = into.TryAdd(name, state);
        }

        if (remaining <= 0)
        {
            return;
        }

        foreach (string name in entry.Prefabs)
        {
            if (_prefabs.TryGetValue(name, out Entry? prefab))
            {
                CollectBodygroups(prefab, into, remaining - 1);
            }
        }
    }

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
    public (int Group, int State) WorldmodelBodygroupOverrideFor(int definitionIndex) =>
        _items.TryGetValue(definitionIndex, out Entry? item)
            ? (Override(item, LongestChain, state: false), Override(item, LongestChain, state: true))
            : (-1, -1);

    /// <summary>The vision a viewer needs before this item is drawn to them (B354).</summary>
    /// <param name="definitionIndex">The item, as <c>m_iItemDefinitionIndex</c> gives it.</param>
    /// <returns>The flag set, or 0 — which is all but 23 shipped items.</returns>
    /// <remarks>
    /// `m_nVisionFilterFlags = m_pKVItem-&gt;GetInt( "vision_filter_flags", 0 )`
    /// (<c>econ_item_schema.cpp:3156</c>). Zero is the engine's own default and the value its
    /// consumer's `!= 0` guard reads as "never hidden", so an unknown item answering 0 degrades the
    /// same way the engine does rather than hiding something.
    /// </remarks>
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

    /// <summary>The first stated half of an override in an entry's prefab chain, or -1.</summary>
    private int Override(Entry entry, int remaining, bool state)
    {
        int stated = state
            ? entry.WorldModelBodygroupStateOverride
            : entry.WorldModelBodygroupOverride;

        if (stated > -1)
        {
            return stated;
        }

        if (remaining <= 0)
        {
            return -1;
        }

        foreach (string name in entry.Prefabs)
        {
            if (_prefabs.TryGetValue(name, out Entry? prefab)
                && Override(prefab, remaining - 1, state) is var inherited and > -1)
            {
                return inherited;
            }
        }

        return -1;
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

    /// <summary>Gathers attachments from an entry and its prefabs.</summary>
    /// <remarks>
    /// **Every level contributes, unlike <see cref="Search"/> which stops at the first answer.** A
    /// model is one value and the nearest definition wins; attachments are a LIST, and an item that
    /// adds one does not thereby discard what its prefab hangs on it.
    /// </remarks>
    private void Collect(Entry entry, List<AttachedModel> into, int remaining)
    {
        into.AddRange(entry.AttachedModels);

        if (remaining <= 0)
        {
            return;
        }

        foreach (string name in entry.Prefabs)
        {
            if (_prefabs.TryGetValue(name, out Entry? prefab))
            {
                Collect(prefab, into, remaining - 1);
            }
        }
    }

    /// <summary>The definition index a named attribute resolves to, or null for an unknown name.</summary>
    /// <remarks>
    /// The top-level <c>attributes</c> section's bridge — <c>GetAttributeDefinitionByName</c> in
    /// the engine. Consumers ask by name so a renumbered schema cannot silently retarget them.
    /// </remarks>
    public int? AttributeDefinitionIndex(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

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
    /// </remarks>
    public IReadOnlyList<EconAttributeValue> DefinitionAttributesFor(int definitionIndex)
    {
        if (!_items.TryGetValue(definitionIndex, out Entry? item))
        {
            return [];
        }

        List<EconAttributeValue> found = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        CollectDefinitionAttributes(item, found, seen, LongestChain);

        return found;
    }

    /// <summary>Gathers definition attributes, nearest declaration winning per name.</summary>
    private void CollectDefinitionAttributes(
        Entry entry, List<EconAttributeValue> into, HashSet<string> seen, int remaining)
    {
        foreach ((string name, string value) in entry.DefinitionAttributes)
        {
            if (!seen.Add(name) || !_attributeIndexByName.TryGetValue(name, out int index))
            {
                continue;
            }

            if (_attributeStoredAsInteger.Contains(index))
            {
                if (int.TryParse(
                    value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer))
                {
                    into.Add(new EconAttributeValue(index, integer));
                }
            }
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

    /// <summary>Searches an item and then its prefabs, in order, for the first answer.</summary>
    private string? Inherited(int definitionIndex, Func<Entry, string?> ask)
    {
        if (!_items.TryGetValue(definitionIndex, out Entry? item))
        {
            return null;
        }

        return Search(item, ask, LongestChain);
    }

    /// <summary>Depth-first through the prefab chain, nearest definition winning.</summary>
    private string? Search(Entry entry, Func<Entry, string?> ask, int remaining)
    {
        if (ask(entry) is { Length: > 0 } answer)
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
                Search(prefab, ask, remaining - 1) is { } inherited)
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
        if (value is not { Length: > 0 })
        {
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

        if (string.Equals(key, "model_player", StringComparison.OrdinalIgnoreCase))
        {
            entry.Model = value;
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

        if (string.Equals(key, "hide_bodygroups_deployed_only", StringComparison.OrdinalIgnoreCase))
        {
            entry.HideBodygroupsDeployedOnly = value != "0";
            return;
        }

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
        }
    }
}
