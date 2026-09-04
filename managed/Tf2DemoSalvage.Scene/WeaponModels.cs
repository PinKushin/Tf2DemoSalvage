using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>Which model is in a player's hands, from the game's own item schema.</summary>
/// <remarks>
/// **This was <c>MainForm.WeaponModelFor</c>, <c>WeaponModel</c> and <c>ItemDefinitions</c>, with
/// the two fields that cached the schema** (B188, D90). None of it is window work — it is two
/// lookups into <c>items_game.txt</c> — and none of it had a test, because reaching it meant
/// constructing a form.
///
/// **The client builds what the demo omits, which is why this exists at all.** Nothing on the wire
/// carries a weapon's model path; the demo carries an item index, and the schema turns that into a
/// model exactly as the game does.
///
/// **Two routes, and the second is needed more often than it looks.** Measured on z1800, 22 of 56
/// held weapons never send <c>m_iItemDefinitionIndex</c> at all, so the weapon's own class is used
/// to find the stock item instead. Together they answered for 56 of 56.
/// </remarks>
public sealed class WeaponModels
{
    private readonly Func<string, byte[]?> _read;
    private readonly ILogger _render;

    private ItemSchema? _schema;
    private bool _missing;

    /// <summary>Creates a resolver over an install.</summary>
    /// <param name="read">Opens a content path, answering null when it is absent.</param>
    /// <param name="render">Where a missing schema is reported.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public WeaponModels(Func<string, byte[]?> read, ILogger render)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(render);

        _read = read;
        _render = render;
    }

    /// <summary>A resolver for a viewer with no game installed, which answers nothing.</summary>
    /// <param name="render">Where the absence is reported.</param>
    /// <returns>A resolver that always answers null.</returns>
    /// <remarks>
    /// **A real object rather than a null field** (D83), so the scene asks the same question whether
    /// or not TF2 is present.
    /// </remarks>
    public static WeaponModels None(ILogger render) => new(_ => null, render);

    /// <summary>The model of the weapon a player is holding, or null.</summary>
    /// <param name="holder">The player, at whichever tick they were read.</param>
    /// <returns>The model path, or null when neither route answers.</returns>
    /// <remarks>
    /// Shared by the draw path and by the load set, deliberately: the set decides which models are
    /// packed and the draw path decides which is shown, so a disagreement between them is a weapon
    /// that resolves and cannot be drawn — which is exactly the failure this feature already had
    /// once, from the other direction.
    ///
    /// **No null guard, because <see cref="ScenePlayer"/> is a record STRUCT.** One was written here
    /// and the analyzer rejected it as a no-op (CA2264) — the same shape
    /// <c>docs/memory/nullable-pattern-on-a-struct-is-dead-code.md</c> records, where a guard
    /// compiles and can never fire.
    /// </remarks>
    public string? For(ScenePlayer holder) =>
        For(holder.WeaponItem, holder.WeaponClass, holder.PlayerClass);

    /// <summary>The display model for a weapon named by its item, its class, or both.</summary>
    /// <param name="weaponItem">The item definition index, when the weapon carries one.</param>
    /// <param name="weaponClass">Its entity class, for the stock route.</param>
    /// <param name="forClass">Which player class is holding it; models differ per class.</param>
    /// <returns>The display model, or <c>null</c> when neither route names one.</returns>
    /// <remarks>
    /// **Split out so the VIEWMODEL can ask about its own weapon** (B222). `DT_BaseViewModel`
    /// networks `m_hWeapon`, which is the engine's answer to what is in this hand; the player's
    /// `m_hActiveWeapon` is a reconstruction of the same thing and can disagree with it. Both routes
    /// end here, because the model itself comes from the item schema either way —
    /// `pItem->GetPlayerDisplayModel( iClass, team )`, `econ_entity.cpp:1167`.
    /// </remarks>
    public string? For(int? weaponItem, string? weaponClass, int? forClass)
    {
        if (Schema() is not { } schema)
        {
            return null;
        }

        int playerClass = forClass ?? 0;

        // **The item first, because it is what the player actually equipped.** The class route only
        // knows the stock version, so preferring it would draw a stock rocket launcher for every
        // unusual and reskin in the game.
        if (weaponItem is { } item &&
            schema.ModelFor(item, playerClass) is { Length: > 0 } named)
        {
            return named;
        }

        if (weaponClass is null)
        {
            return null;
        }

        foreach (string candidate in WeaponScriptName.Candidates(weaponClass, forClass))
        {
            if (schema.ModelForClass(candidate, playerClass) is { Length: > 0 } stock)
            {
                return stock;
            }
        }

        return null;
    }

    /// <summary>Every weapon model any player holds at any point in a demo.</summary>
    /// <remarks>
    /// **Resolved up front for the same reason the class models are.** A player switches weapon
    /// constantly, and a set built from what is held right now is missing whatever they draw next —
    /// which does not fail loudly, it just leaves an empty hand.
    ///
    /// **Distinct PAIRS rather than distinct players**, so a whole match resolves to a few dozen
    /// models rather than one lookup per player per frame.
    ///
    /// Shares <see cref="For(ScenePlayer)"/> with the draw path deliberately: the set decides which models are
    /// packed and the draw path decides which is shown, so a disagreement between them is a weapon
    /// that resolves and cannot be drawn.
    /// </remarks>
    /// <summary>An entity's attributes, resolved exactly as <c>IterateAttributes</c> resolves.</summary>
    /// <param name="prop">The prop, whose <see cref="SceneProp.Econ"/> is the wire's half.</param>
    /// <returns>First-writer-wins per definition: local, then demos-or-definition.</returns>
    /// <remarks>
    /// The wire's two lists and the item-id gate come from the recording; branch 4 — the item
    /// definition's own attributes — comes from `items_game.txt` here, which is why the resolution
    /// completes in this layer rather than in Core (B234).
    /// </remarks>
    public IReadOnlyList<EconAttributeValue> AttributesFor(SceneProp prop)
    {
        ArgumentNullException.ThrowIfNull(prop);

        IReadOnlyList<EconAttributeValue> definition =
            prop.ItemDefinitionIndex is { } item && Schema() is { } schema
                ? schema.DefinitionAttributesFor(item)
                : [];

        return EconAttributes.Resolve(
            prop.Econ?.Local ?? [],
            prop.Econ?.NetworkedForDemos ?? [],
            prop.Econ?.HasValidItemId ?? false,
            definition);
    }

    /// <summary>Whether this item is festivized — <c>CALL_ATTRIB_HOOK_INT( …, is_festivized )</c>.</summary>
    /// <remarks>
    /// **The index comes from the schema's own name bridge, not a constant** — 2053 today, but a
    /// hook is spelled by name in the engine and a hardcoded number would survive a renumbering
    /// silently. Measured on `tf2-2026-pub-pov-clean`: attribute 2053 appears 220 times, on the
    /// same mediguns B244 fixed, so this gate opening is what puts `c_medigun_festivizer.mdl` on
    /// them.
    /// </remarks>
    public bool IsFestivized(SceneProp prop)
    {
        if (Schema()?.AttributeDefinitionIndex("is_festivized") is not { } festivized)
        {
            return false;
        }

        foreach (EconAttributeValue attribute in AttributesFor(prop))
        {
            if (attribute.DefinitionIndex == festivized)
            {
                return attribute.RawBits != 0;
            }
        }

        return false;
    }

    /// <summary>The colour this item is painted, or null when it carries no paint (B330).</summary>
    /// <param name="prop">The prop, whose econ attributes carry the paint.</param>
    /// <param name="team">Its team, which chooses between a two-tone paint's two colours.</param>
    /// <returns>Three channels in 0..1, or null for an unpainted item.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prop"/> is null.</exception>
    /// <remarks>
    /// **The value half of TF2's `ItemTintColor` proxy**, resolved through the same
    /// <see cref="AttributesFor"/> every other attribute question uses so the four branches of
    /// `IterateAttributes` cannot be applied differently here. The arithmetic — the old-team
    /// sentinel, the alt fallback, the branch order, the float-to-integer conversion — is
    /// <see cref="ItemPaint"/>, with its citations.
    ///
    /// **`bAltColor` is BLU**, per `pEntity->GetTeam()->GetTeamNumber() == TF_TEAM_BLUE`
    /// (`econ_wearable.cpp:521-524`). A prop with no team takes the primary, which is what the
    /// engine's `kEconItemFlagClient_ForceBlueTeam` fallback resolves to for anything this project
    /// draws.
    /// </remarks>
    public (float Red, float Green, float Blue)? PaintFor(SceneProp prop, int? team)
    {
        ArgumentNullException.ThrowIfNull(prop);

        if (Schema() is not { } schema)
        {
            return null;
        }

        Dictionary<int, EconAttributeValue> byDefinition = [];

        foreach (EconAttributeValue attribute in AttributesFor(prop))
        {
            byDefinition[attribute.DefinitionIndex] = attribute;
        }

        return ItemPaint.Tint(byDefinition, schema, alternate: team == SceneTeams.Blu);
    }

    /// <summary>The extra models an item hangs on itself, for the team holding it.</summary>
    /// <param name="item">Its <c>m_iItemDefinitionIndex</c>, or null when the demo names none.</param>
    /// <param name="team">The owner's team, or null when it is not known.</param>
    /// <param name="festivized">Whether the item carries <c>is_festivized</c> — see <see cref="IsFestivized"/>.</param>
    /// <param name="displayFlagMask">
    /// Which view is drawing — <see cref="AttachedModel.WorldModel"/> or
    /// <see cref="AttachedModel.ViewModel"/> — matched against each entry's
    /// <c>model_display_flags</c> exactly as <c>DrawEconEntityAttachedModels</c> masks them (B252).
    /// </param>
    /// <returns>Model paths, in schema order. Empty for an item that declares none.</returns>
    /// <remarks>
    /// **`CEconEntity::UpdateAttachmentModels` (`econ_entity.cpp:1078`)**, which walks
    /// `GetNumAttachedModels( GetTeamNumber() )` and appends each entry's model. Measured on the
    /// shipped schema: 325 item definitions carry at least one, from 29 blocks — prefabs are the
    /// multiplier, so counting blocks understates the reach by an order of magnitude.
    ///
    /// **The festive gate is `CALL_ATTRIB_HOOK_INT( iFestivized, is_festivized )`**
    /// (`econ_entity.cpp:1109`), which <see cref="IsFestivized"/> now answers from the decoded
    /// attributes (B234). It spent a day hardcoded shut, with the reason recorded here: 314
    /// festive entries against 42 plain means an open gate without the attribute decorates the
    /// whole game for Christmas.
    /// </remarks>
    public IReadOnlyList<string> AttachmentsFor(
        int? item, int? team, bool festivized, int displayFlagMask)
    {
        if (item is not { } definition || Schema() is not { } schema)
        {
            return [];
        }

        List<string> models = [];

        foreach (AttachedModel attached in
            schema.AttachedModelsFor(definition, team, festivized))
        {
            // `(m_iModelDisplayFlags & iMatchDisplayFlags)` — the draw's own test, verbatim. An
            // entry that names neither bit of the mask belongs to the other view.
            if ((attached.DisplayFlags & displayFlagMask) != 0)
            {
                models.Add(attached.Model);
            }
        }

        return models;
    }

    /// <summary>Every attachment model the demo could ever show, for packing.</summary>
    /// <param name="timeline">The decoded demo.</param>
    /// <returns>Distinct model paths.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="timeline"/> is null.</exception>
    /// <remarks>
    /// **Both teams, because a prop's owner can change sides** and geometry is loaded once before
    /// the first frame — the engine treats a mid-play load as a programming error (D86), so the
    /// packing set is deliberately wider than any single moment needs.
    /// </remarks>
    public IEnumerable<string> AllAttachmentsIn(DemoTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        return ResolveAttachments(timeline);
    }

    /// <summary>The attachment walk, once the argument is known good.</summary>
    private IEnumerable<string> ResolveAttachments(DemoTimeline timeline)
    {
        if (Schema() is null)
        {
            yield break;
        }

        HashSet<int> asked = [];

        foreach (ScenePropTrack track in timeline.Props)
        {
            if (track.ItemDefinitionIndex is not { } item || !asked.Add(item))
            {
                continue;
            }

            foreach (int team in Teams)
            {
                // Festive included unconditionally HERE: this is the packing set, geometry loads
                // once before the first frame, and whether an instance is festivized is a per-tick
                // draw decision the pack must be a superset of.
                foreach (string model in AttachmentsFor(
                    item, team, festivized: true, AttachedModel.MaskAll))
                {
                    yield return model;
                }
            }
        }
    }

    /// <summary>The two playing teams, so a per-team block is packed for either side.</summary>
    private static readonly int[] Teams = [2, 3];

    /// <summary>Every weapon model any player holds at any point in a demo.</summary>
    /// <param name="timeline">The decoded demo.</param>
    /// <returns>Distinct model paths, in the order they are first seen.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="timeline"/> is null.</exception>
    public IEnumerable<string> AllIn(DemoTimeline timeline)
    {
        // **Checked here rather than in the iterator below, which is not a style rule.** An
        // iterator's body does not run until something enumerates it, so a guard inside one throws
        // at the `foreach` rather than at the call — pointing at the consumer instead of the caller
        // that passed null.
        ArgumentNullException.ThrowIfNull(timeline);

        return Resolve(timeline);
    }

    /// <summary>The walk itself, once the argument is known good.</summary>
    private IEnumerable<string> Resolve(DemoTimeline timeline)
    {
        if (Schema() is null)
        {
            yield break;
        }

        HashSet<(int? Item, string? Weapon, int Class)> seen = [];

        foreach (TimelineFrame frame in timeline.Frames)
        {
            foreach (ScenePlayer player in frame.Players)
            {
                if (player.ActiveWeapon is null ||
                    !seen.Add((player.WeaponItem, player.WeaponClass, player.PlayerClass ?? 0)))
                {
                    continue;
                }

                if (For(player) is { Length: > 0 } model)
                {
                    yield return model;
                }
            }
        }
    }

    /// <summary>TF2's item schema, read from the installed game once.</summary>
    /// <remarks>
    /// Null when the game is not installed or the file is not where it should be, which is the same
    /// condition every other asset lookup here already tolerates — the viewer draws what it can find
    /// and says what it could not.
    /// </remarks>
    /// <summary>The econ item schema, read on first use, or null when the install has none.</summary>
    /// <remarks>
    /// **Published because a corpse's cosmetics need the same two facts a weapon does** (B324): an
    /// item's `drop_type` decides whether it stays on the body or becomes a falling gib, and its
    /// loadout slot decides whether a decapitation takes it. Both live in `items_game.txt`, which is
    /// eight megabytes — reading it a second time for the ragdoll path would be the same file parsed
    /// twice per demo.
    ///
    /// **Reading this is a WRITE the first time**, since the parse is lazy
    /// (`docs/memory/a-lazy-cache-makes-reading-a-write.md`). That is the existing shape of this
    /// class rather than something introduced here, and the flag beside it means the eight-megabyte
    /// read is attempted once whether it succeeds or not.
    /// </remarks>
    public ItemSchema? Items => Schema();

    private ItemSchema? Schema()
    {
        if (_schema is not null || _missing)
        {
            return _schema;
        }

        if (_read(SchemaPath) is not { } bytes)
        {
            // Recorded so the eight-megabyte read is not attempted every frame, and reported once so
            // a viewer with no weapons in hand says why.
            _missing = true;
            _render.LogWarning(
                "{Message}", "no items_game.txt, so no weapon models in first person");

            return null;
        }

        _schema = ItemSchema.Read(bytes);
        _render.LogInformation("{Message}", "item schema read");

        return _schema;
    }

    /// <summary>Where the game keeps its item definitions.</summary>
    private const string SchemaPath = "scripts/items/items_game.txt";
}
