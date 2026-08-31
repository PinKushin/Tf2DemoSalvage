using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tf2DemoSalvage.Content.Assets;

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

        KeyValuesReader.Read(schema, (key, value, depth) =>
        {
            switch (depth)
            {
                case 1:
                    section = key;
                    entry = null;
                    inPerClass = false;
                    break;

                case 2:
                    inPerClass = false;
                    entry = read.Begin(section, key);
                    break;

                case 3 when entry is not null:
                    inPerClass =
                        value is null &&
                        string.Equals(key, "model_player_per_class", StringComparison.OrdinalIgnoreCase);

                    Apply(entry, key, value);
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

        if (string.Equals(key, "baseitem", StringComparison.OrdinalIgnoreCase))
        {
            entry.IsBaseItem = value != "0";
        }
    }
}
